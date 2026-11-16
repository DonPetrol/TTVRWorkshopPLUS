// TTVR Workshop Plus - the editor: a world's preview picture, relocating a moved model, and the shading, size and atlas controls.
// (One class in several files: Glue.cs says what is where.)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Steamworks;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;

namespace TTVRPlus
{
    public static partial class WorkshopPlus
    {
        // ---------------------------------------------------------------- a world's preview picture
        // The texture dialog offers DDS and TGA files as well (for objects), but the tool's own loader for a world's
        // preview picture can only read PNG and JPG. On the Worlds tab such a choice is refused: the world keeps the
        // picture it had, and a message says why.
        static WorkshopItem previewItem; static string previewBefore;

        static bool IsDdsOrTga(string path)
        {
            try { string ext = Path.GetExtension(path).ToLowerInvariant(); return ext == ".dds" || ext == ".tga"; }
            catch (Exception) { return false; }
        }

        /// called at the start of the texture Browse button: the picture the item has before a new one is chosen
        public static void RememberPreview(Workshop w)
        {
            previewItem = w.activeItem;
            previewBefore = w.activeItem != null ? w.activeItem.textureFile : null;
        }

        /// called at the start of the tool's preview loader, which goes on to load `path`
        public static void KeepReadablePreview(Workshop w, ref string path)
        {
            if (w.objectTabActive || w.activeItem == null || string.IsNullOrEmpty(path) || !IsDdsOrTga(path)) return;
            string refused = Path.GetFileName(path);
            // back to the picture it had before Browse or, failing that, the world's own preview.jpg (as the Clear button does)
            string back = previewItem == w.activeItem ? previewBefore : null;
            if (string.IsNullOrEmpty(back) || IsDdsOrTga(back))
            {
                try { back = Path.Combine(Path.GetDirectoryName(w.activeItem.sourceFile), "preview.jpg"); }
                catch (Exception) { back = string.Empty; }
            }
            w.activeItem.textureFile = back;
            if (w.textureField != null) w.textureField.text = back;
            path = back;
            SetMessage(w, "A world's preview has to be a PNG or JPG picture, so " + refused + " was not used.");
        }

        static void ResetRotation(Workshop w)
        {
            if (w.activeItem == null) return;
            w.objectRoot.localRotation = Quaternion.identity;
            w.activeItem.rotation = Quaternion.identity;
            w.UpdateObjectPosition(w.objectRoot);
            w.AddOrUpdateMetadata(w.activeItem);
        }

        // ---------------------------------------------------------------- relocating a moved model
        // Only for an existing entry being opened (its stored path is the one that's missing).
        static string Relocate(Workshop w, string oldPath, ref string textureFile)
        {
            WorkshopItem item = w.activeItem;
            if (item == null || item.sourceFile != oldPath) return null;
            string startDir = Path.GetDirectoryName(oldPath);
            while (!string.IsNullOrEmpty(startDir) && !Directory.Exists(startDir)) startDir = Path.GetDirectoryName(startDir);
            var filters = new SFB.ExtensionFilter[] { new SFB.ExtensionFilter("3D Models (OBJ, glTF, GLB) ", "obj", "gltf", "glb") };
            string[] sel = SFB.StandaloneFileBrowser.OpenFilePanel("Model moved: locate " + Path.GetFileName(oldPath) + " (or a replacement)" + " for \"" + item.title + "\"",
                                                                    startDir ?? "", filters, false);
            if (sel == null || sel.Length == 0 || string.IsNullOrEmpty(sel[0])) return null;
            string newPath = sel[0].StartsWith("file://") ? sel[0].Substring(7) : sel[0];
            if (!File.Exists(newPath)) return null;

            // the chosen texture: keep it if it still exists, otherwise look for it next to the model's new location
            if (!string.IsNullOrEmpty(item.textureFile) && !File.Exists(item.textureFile))
            {
                string found = FindMoved(item.textureFile, Path.GetDirectoryName(oldPath), Path.GetDirectoryName(newPath));
                if (found != null) item.textureFile = found;
            }
            textureFile = item.textureFile;
            ItemSettings.Move(oldPath, newPath);
            item.sourceFile = newPath;
            w.AddOrUpdateMetadata(item);
            Debug.Log("[WorkshopPlus] relocated '" + item.title + "': " + oldPath + " -> " + newPath);
            return newPath;
        }

        // same relative position to the model (e.g. textures\\a.png), or same file name in the model's new folder
        static string FindMoved(string oldFile, string oldDir, string newDir)
        {
            string prefix = oldDir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (oldFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string c = Path.Combine(newDir, oldFile.Substring(prefix.Length));
                if (File.Exists(c)) return c;
            }
            string n = Path.Combine(newDir, Path.GetFileName(oldFile));
            return File.Exists(n) ? n : null;
        }

        // ---------------------------------------------------------------- shading dropdown
        static Dropdown shadingDropdown;
        static bool updatingDropdown;

        // ---------------------------------------------------------------- resolution scaling + text size
        const float UiScale = 0.72f;   // everything about 28% smaller than the original 1024x768 layout
        static void ScaleUi(Workshop w)
        {
            try
            {
                var canvas = w.listView.transform.parent.GetComponent<Canvas>();
                if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) return;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                {   // the tool was laid out for 1024x768: scale with window height (a larger reference size = everything smaller, see UiScale)
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1024f / UiScale, 768f / UiScale);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = 1f;
                }
                // "Back" labels: let them extend past their box instead of being cut off
                foreach (var bt in w.detailView.GetComponentsInChildren<Button>(true))
                    if (bt.name == "BackButton")
                        foreach (var t in bt.GetComponentsInChildren<Text>(true)) { t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; }
                if (w.objectLabel != null)
                {
                    w.objectLabel.text = "Model:";
                    // move the model Browse button right so it doesn't cover the longer label
                    var browse = w.objectLabel.transform.parent.Find("ObjectBrowseButton") as RectTransform;
                    if (browse != null)
                    {
                        var lr = w.objectLabel.rectTransform;
                        float labelEnd = lr.anchoredPosition.x - lr.sizeDelta.x * lr.pivot.x + w.objectLabel.preferredWidth;
                        // Browse and Reimport share the space after the label
                        float x0 = Mathf.Max(labelEnd + 6f, 100f), right = 250f - 6f, gap = 4f;
                        float bw = Mathf.Min(browse.sizeDelta.x, (right - x0 - gap) * 0.5f);
                        browse.sizeDelta = new Vector2(bw, browse.sizeDelta.y);
                        browse.anchoredPosition = new Vector2(x0 + bw * browse.pivot.x, browse.anchoredPosition.y);
                        var bt = browse.GetComponentInChildren<Text>(); if (bt != null) FitButtonText(bt);
                        var rGo = (GameObject)UnityEngine.Object.Instantiate(browse.gameObject, browse.parent, false);
                        rGo.name = "ObjectReimportButton";
                        var rrt = (RectTransform)rGo.transform;
                        rrt.anchoredPosition = new Vector2(x0 + bw + gap + bw * rrt.pivot.x, browse.anchoredPosition.y);
                        var rb = rGo.GetComponent<Button>();
                        rb.onClick = new Button.ButtonClickedEvent(); rb.interactable = true;
                        rb.onClick.AddListener(delegate { Reimport(w); });
                        var rtx = rGo.GetComponentInChildren<Text>(); if (rtx != null) { rtx.text = "Reimport"; FitButtonText(rtx); }
                        Tooltip.Add(rGo, "Read the model file again from the same path, picking up any changes (and its textures). If it can't be found or read, the current model stays.");
                        reimportButton = rGo;
                    }
                }

                // window title: "Workshop PLUS" with the version a little smaller after it
                var titleT = w.listView.transform.parent.Find("Header/Text");
                var title = titleT != null ? titleT.GetComponent<Text>() : null;
                if (title != null && title.text.Trim() == "Workshop")
                {
                    int fs = title.fontSize;
                    if (title.resizeTextForBestFit)
                    {
                        Canvas.ForceUpdateCanvases();
                        int used = title.cachedTextGenerator.fontSizeUsedForBestFit;
                        if (used > 0) fs = used;
                        title.resizeTextForBestFit = false; title.fontSize = fs;
                    }
                    title.supportRichText = true;
                    title.horizontalOverflow = HorizontalWrapMode.Overflow;
                    title.text = "Workshop PLUS <size=" + Mathf.Max(8, Mathf.RoundToInt(fs * 0.55f)) + ">v" + Version + "</size>";
                }
            }
            catch (Exception e) { Debug.Log("[WorkshopPlus] UI scaling: " + e.Message); }
        }

        // the 3D preview is rendered to a texture sized in canvas units; with a scaled canvas, render at the real pixel size
        static RenderTexture ownPreview;       // the texture made here, while the camera is using it
        public static void KeepPreviewSharp(Workshop w)
        {
            if (w.rttCamera == null || w.rttImage == null || !w.detailView.activeInHierarchy) return;
            var canvas = w.rttImage.canvas; if (canvas == null) return;
            Rect r = w.rttImage.rectTransform.rect;                          // the area the preview is actually shown in (keeps its shape)
            int pw = Mathf.RoundToInt(r.width * canvas.scaleFactor), ph = Mathf.RoundToInt(r.height * canvas.scaleFactor);
            if (pw < 16 || ph < 16) return;
            var cur = w.rttCamera.targetTexture;
            // The tool makes a texture of its own after every rebuild and just drops whichever one the camera had.
            // If that was the one made here, nothing uses it any more, so it is freed (it would otherwise pile up).
            if (ownPreview != null && cur != ownPreview) { ownPreview.Release(); UnityEngine.Object.Destroy(ownPreview); ownPreview = null; }
            if (cur != null && Mathf.Abs(cur.width - pw) <= 2 && Mathf.Abs(cur.height - ph) <= 2)
            {
                float want = (float)cur.width / cur.height;
                if (Mathf.Abs(w.rttCamera.aspect - want) > 0.001f) w.rttCamera.aspect = want;   // camera shape = picture shape
                return;
            }
            var rt = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            rt.antiAliasing = 4;
            w.rttCamera.targetTexture = rt;
            w.rttCamera.ResetAspect();
            w.rttCamera.aspect = (float)pw / ph;
            w.rttImage.texture = rt;
            ownPreview = rt;
            if (cur != null) { cur.Release(); UnityEngine.Object.Destroy(cur); }
        }

        public static void Init(Workshop w)
        {
            ScaleUi(w);
            try { Thumbnails.Init(FileUtils.GetWorkshopDevelopmentDirectory()); } catch (Exception) { }
            try
            {
                Transform contents = w.footerContents.transform;      // bottom bar of the object editor (hidden for worlds)
                Transform flipLabel = contents.Find("FlipLabel");

                var labelGo = (GameObject)UnityEngine.Object.Instantiate(flipLabel.gameObject, contents, false);
                labelGo.name = "ShadingLabel";
                var text = labelGo.GetComponent<Text>();
                text.text = "Shading:";
                text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                var lrt = (RectTransform)labelGo.transform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.sizeDelta = new Vector2(Mathf.Ceil(text.preferredWidth) + 4f, 40f);
                lrt.anchoredPosition = new Vector2(12f, 0f);

                var ddGo = (GameObject)UnityEngine.Object.Instantiate(w.visibilityDropdown.gameObject, contents, false);
                ddGo.name = "ShadingDropdown";
                var drt = (RectTransform)ddGo.transform;
                drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f);
                drt.pivot = new Vector2(0f, 0.5f);
                drt.anchoredPosition = new Vector2(12f + lrt.sizeDelta.x + 6f, 0f);
                shadingDropdown = ddGo.GetComponent<Dropdown>();
                shadingDropdown.onValueChanged = new Dropdown.DropdownEvent();   // drop the copied Visibility handler
                shadingDropdown.ClearOptions();
                shadingDropdown.AddOptions(new List<string> { "Imported", "Smooth", "Flat", "Auto-Smooth" });
                shadingDropdown.value = 0;
                shadingDropdown.RefreshShownValue();
                shadingDropdown.onValueChanged.AddListener(delegate (int i) { OnShadingChanged(w, i); });
                Tooltip.Add(ddGo, "How the surface is lit. Imported: the file's own normals. Smooth: everything smooth. Flat: every face flat (more vertices). Auto-Smooth: hard edges above the angle, smooth below.");
                drt.sizeDelta = new Vector2(Mathf.Max(drt.sizeDelta.x, 130f), drt.sizeDelta.y);   // room for "Auto-Smooth"

                // angle box (copy of the Scale field), only shown for Auto-Smooth
                var srcField = contents.Find("ScaleField");
                var fGo = (GameObject)UnityEngine.Object.Instantiate(srcField.gameObject, contents, false);
                fGo.name = "ShadingAngleField";
                var frt = (RectTransform)fGo.transform;
                frt.anchorMin = frt.anchorMax = new Vector2(0f, 0.5f);
                frt.pivot = new Vector2(0f, 0.5f);
                frt.sizeDelta = new Vector2(52f, drt.sizeDelta.y);
                frt.anchoredPosition = new Vector2(drt.anchoredPosition.x + drt.sizeDelta.x + 6f, 0f);
                angleField = fGo.GetComponent<InputField>();
                angleField.onValueChanged = new InputField.OnChangeEvent();     // drop the copied Scale handlers
                angleField.onEndEdit = new InputField.SubmitEvent();
                angleField.contentType = InputField.ContentType.DecimalNumber;
                angleField.characterLimit = 5;
                angleField.text = "30";
                angleField.onEndEdit.AddListener(delegate (string v) { OnAngleChanged(w, v); });
                Tooltip.Add(fGo, "Auto-Smooth angle: edges sharper than this stay hard. Press Enter to apply.");

                var degGo = (GameObject)UnityEngine.Object.Instantiate(labelGo, contents, false);
                degGo.name = "ShadingAngleUnit";
                degGo.GetComponent<Text>().text = "\u00B0";
                var urt = (RectTransform)degGo.transform;
                urt.sizeDelta = new Vector2(16f, 40f);
                urt.anchoredPosition = new Vector2(frt.anchoredPosition.x + frt.sizeDelta.x + 2f, 0f);
                angleWidgets = new GameObject[] { fGo, degGo };
                SetAngleVisible(false);

                // "Flip X" toggle button in place of the Flip tick box + label
                Transform flipToggleT = contents.Find("FlipToggle");
                RectTransform flipBtnRect = null;
                if (flipToggleT != null && w.publishButton != null)
                {
                    var fbGo = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, contents, false);
                    fbGo.name = "FlipXButton";
                    fbGo.SetActive(true);
                    flipBtnRect = (RectTransform)fbGo.transform;
                    flipBtnRect.anchorMin = flipBtnRect.anchorMax = new Vector2(1f, 0.5f);
                    flipBtnRect.pivot = new Vector2(1f, 0.5f);
                    flipBtnRect.sizeDelta = new Vector2(95f, 35f);
                    flipBtnRect.anchoredPosition = new Vector2(ScaleTextLeft(contents) - 10f, 0f);     // just left of "Scale:"
                    flipButton = fbGo.GetComponent<Button>();
                    flipButton.onClick = new Button.ButtonClickedEvent();
                    flipButton.interactable = true;
                    flipButton.onClick.AddListener(delegate { ToggleFlip(w); });
                    flipOnColor = flipButton.image != null ? flipButton.image.color : new Color(0.3f, 0.7f, 0.3f);
                    flipText = fbGo.GetComponentInChildren<Text>();
                    if (flipText != null)
                    {
                        flipText.text = "Flip X";
                        int fs = flipText.fontSize;
                        flipText.resizeTextForBestFit = true; flipText.resizeTextMaxSize = fs; flipText.resizeTextMinSize = Math.Max(8, fs / 2);
                        flipOnTextColor = flipText.color;
                    }
                    flipToggleT.gameObject.SetActive(false);
                    if (flipLabel != null) flipLabel.gameObject.SetActive(false);
                    flipShown = null;
                    UpdateFlipButton(w);
                }

                // "Reset rotation" just left of Flip X: back to the model's original orientation
                if (flipBtnRect != null)
                {
                    var rGo = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, contents, false);
                    rGo.name = "ResetRotationButton";
                    rGo.SetActive(true);
                    var rrt = (RectTransform)rGo.transform;
                    rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f);
                    rrt.pivot = new Vector2(1f, 0.5f);
                    rrt.sizeDelta = new Vector2(125f, 35f);
                    rrt.anchoredPosition = new Vector2(flipBtnRect.anchoredPosition.x - flipBtnRect.sizeDelta.x - 8f, 0f);
                    resetRect = rrt;
                    var rb = rGo.GetComponent<Button>();
                    rb.onClick = new Button.ButtonClickedEvent();          // drop the copied Publish handler
                    rb.interactable = true;
                    rb.onClick.AddListener(delegate { ResetRotation(w); });
                    Tooltip.Add(rGo, "Undo all X, Y and Z rotations and go back to the model's original orientation.");
                    var rtx = rGo.GetComponentInChildren<Text>();
                    if (rtx != null)
                    {
                        rtx.text = "Reset rotation";
                        int fs = rtx.fontSize;
                        rtx.resizeTextForBestFit = true; rtx.resizeTextMaxSize = fs; rtx.resizeTextMinSize = Math.Max(8, fs / 2);
                    }
                }

                // explanation shown when Flat would go over the vertex limit
                var warnGo = (GameObject)UnityEngine.Object.Instantiate(labelGo, contents, false);
                warnGo.name = "ShadingFlatWarning";
                flatWarning = warnGo.GetComponent<Text>();
                flatWarning.color = ErrorRed;
                flatWarning.horizontalOverflow = HorizontalWrapMode.Overflow;
                flatWarnRect = (RectTransform)warnGo.transform;
                flatWarnRect.sizeDelta = new Vector2(300f, 40f);
                flatWarnX = frt.anchoredPosition.x;
                warnGo.SetActive(false);

                // error line: wrap onto more lines (upwards) instead of being cut off
                Text et = w.errorMessageText;
                if (et != null)
                {
                    var ert = et.rectTransform;
                    ert.anchorMin = new Vector2(0f, 0f); ert.anchorMax = new Vector2(1f, 0f); ert.pivot = new Vector2(0f, 0f);
                    ert.offsetMin = new Vector2(10f, 65f);
                    ert.offsetMax = new Vector2(-260f, 95f);
                    et.horizontalOverflow = HorizontalWrapMode.Wrap;
                    et.verticalOverflow = VerticalWrapMode.Overflow;
                    et.alignment = TextAnchor.LowerLeft;
                    et.resizeTextForBestFit = false;
                    et.color = ErrorRed;
                }

                // permanent hint in the (empty) texture field
                if (w.textureField != null && w.textureField.placeholder is Text)
                    ((Text)w.textureField.placeholder).text = "PNG, JPG, DDS or TGA, max 512px";
            }
            catch (Exception e) { Debug.Log("[WorkshopPlus] could not add shading option: " + e); }
        }

        static InputField angleField;
        static GameObject[] angleWidgets;

        static void SetAngleVisible(bool on)
        {
            if (angleWidgets == null) return;
            foreach (var g in angleWidgets) if (g != null) g.SetActive(on);
        }

        // ---------------------------------------------------------------- atlas options (above the Texture field)
        static Dropdown atlasDropdown, texSizeDropdown; static Toggle cropToggle; static GameObject[] atlasWidgets; static bool updatingAtlas;

        public static void InitAtlasUi(Workshop w)
        {
            var sidebar = (RectTransform)w.titleField.transform.parent;
            var texLabel = sidebar.Find("TextureLabel") as RectTransform;
            if (texLabel == null || w.visibilityDropdown == null) return;
            const float CropY = 44f, Shift = 90f;          // crop row below the Atlas row; how far Texture/Visibility move down
            float ty = texLabel.anchoredPosition.y;
            var visLabel = sidebar.Find("VisibilityLabel") as RectTransform;
            float ddOffset = visLabel != null ? ((RectTransform)w.visibilityDropdown.transform).anchoredPosition.y - visLabel.anchoredPosition.y : 32f;
            // move the Texture and Visibility rows down (by name: their pivots differ, so positions can't be compared directly)
            foreach (var n in new[] { "TextureLabel", "TextureBrowseButton", "TextureClearButton", "TextureField", "VisibilityLabel", "VisibilityDropdown" })
            {
                var r = sidebar.Find(n) as RectTransform;
                if (r != null) r.anchoredPosition -= new Vector2(0f, Shift);
            }
            var widgets = new List<GameObject>();

            var lGo = (GameObject)UnityEngine.Object.Instantiate(texLabel.gameObject, sidebar, false);
            lGo.name = "AtlasLabel";
            ((RectTransform)lGo.transform).anchoredPosition = new Vector2(texLabel.anchoredPosition.x, ty);
            var lt = lGo.GetComponent<Text>(); lt.text = "Atlas:";
            widgets.Add(lGo);

            var vis = (RectTransform)w.visibilityDropdown.transform;
            var dGo = (GameObject)UnityEngine.Object.Instantiate(w.visibilityDropdown.gameObject, sidebar, false);
            dGo.name = "AtlasDropdown";
            var drt = (RectTransform)dGo.transform;
            drt.anchoredPosition = new Vector2(vis.anchoredPosition.x, ty + ddOffset);   // same place relative to its label as Visibility's
            drt.sizeDelta = new Vector2(Mathf.Max(vis.sizeDelta.x, 120f), vis.sizeDelta.y);
            atlasDropdown = dGo.GetComponent<Dropdown>();
            atlasDropdown.onValueChanged = new Dropdown.DropdownEvent();
            atlasDropdown.ClearOptions();
            atlasDropdown.AddOptions(new List<string> { "Equal", "By area" });
            atlasDropdown.value = 0; atlasDropdown.RefreshShownValue();
            atlasDropdown.onValueChanged.AddListener(delegate (int i)
            {
                if (updatingAtlas || w.activeItem == null || string.IsNullOrEmpty(w.activeItem.sourceFile)) return;
                ItemSettings.SetAtlas(w.activeItem, (AtlasMode)i);
                MarkImport(); Rebuild(w);
            });
            Tooltip.Add(dGo, "How several textures share the item's one 512px texture. Equal: the same space each. By area: space in proportion to how much of the model's surface uses each texture, so every part ends up about equally sharp. Only matters when the model uses more than one texture.");
            widgets.Add(dGo);

            var flip = w.footerContents.transform.Find("FlipToggle");
            if (flip != null)
            {
                var tGo = (GameObject)UnityEngine.Object.Instantiate(flip.gameObject, sidebar, false);
                tGo.name = "CropToggle"; tGo.SetActive(true);
                var trt = (RectTransform)tGo.transform;
                trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f); trt.pivot = new Vector2(0.5f, 0.5f);
                trt.sizeDelta = new Vector2(20f, 20f); trt.anchoredPosition = new Vector2(25f, ty + ddOffset - CropY);
                cropToggle = tGo.GetComponent<Toggle>();
                cropToggle.onValueChanged = new Toggle.ToggleEvent();
                cropToggle.isOn = false;
                cropToggle.onValueChanged.AddListener(delegate (bool on)
                {
                    if (updatingAtlas || w.activeItem == null || string.IsNullOrEmpty(w.activeItem.sourceFile)) return;
                    ItemSettings.SetCrop(w.activeItem, on);
                    MarkImport(); Rebuild(w);
                    if (on)
                    {   // the reminder about tiling goes after the rebuild's own notes, and never over an error
                        const string tiling = "Textures that tile (repeat) will be clamped instead of repeating; turn it off for models that rely on tiling.";
                        string shown = w.errorMessageText != null ? w.errorMessageText.text : "";
                        if (string.IsNullOrEmpty(shown)) SetMessage(w, "Note: UV cropping is on. " + tiling);
                        else if (shown.StartsWith("Note:")) SetMessage(w, shown + " UV cropping is on: " + tiling.Substring(0, 1).ToLowerInvariant() + tiling.Substring(1));
                    }
                });
                const string tip = "Keep only the part of each texture the model's UVs actually use, so it gets more of the 512px space. May break tiling textures: anything that repeats is clamped instead.";
                Tooltip.Add(tGo, tip);
                widgets.Add(tGo);

                var cGo = (GameObject)UnityEngine.Object.Instantiate(texLabel.gameObject, sidebar, false);
                cGo.name = "CropLabel";
                var crt = (RectTransform)cGo.transform;
                crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f); crt.pivot = new Vector2(0f, 0.5f);
                crt.sizeDelta = new Vector2(122f, 40f); crt.anchoredPosition = new Vector2(44f, ty + ddOffset - CropY);
                var ct = cGo.GetComponent<Text>();
                int small = Mathf.Max(10, Mathf.RoundToInt(ct.fontSize * 0.55f));
                ct.fontSize = small;
                ct.supportRichText = true;
                ct.alignment = TextAnchor.MiddleLeft;
                ct.horizontalOverflow = HorizontalWrapMode.Wrap; ct.verticalOverflow = VerticalWrapMode.Overflow;
                ct.lineSpacing = 0.9f;
                ct.text = "Crop to used UVs\n<size=" + Mathf.Max(9, Mathf.RoundToInt(small * 0.8f)) + "><color=#FF6A00>May break tiling</color></size>";
                var cb = cGo.AddComponent<Button>(); cb.transition = Selectable.Transition.None;
                cb.onClick.AddListener(delegate { cropToggle.isOn = !cropToggle.isOn; });
                Tooltip.Add(cGo, tip);
                widgets.Add(cGo);

                // "Export" on the same row: save the item's current texture (the atlas) as a PNG
                var eGo = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, sidebar, false);
                eGo.name = "ExportAtlasButton"; eGo.SetActive(true);
                var ert = (RectTransform)eGo.transform;
                ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f); ert.pivot = new Vector2(1f, 0.5f);
                ert.sizeDelta = new Vector2(72f, 30f); ert.anchoredPosition = new Vector2(250f - 6f, ty + ddOffset - CropY);
                var eb = eGo.GetComponent<Button>();
                eb.onClick = new Button.ButtonClickedEvent(); eb.interactable = true;
                eb.onClick.AddListener(delegate { ExportAtlas(w); });
                var etx = eGo.GetComponentInChildren<Text>(); if (etx != null) { etx.text = "Export"; FitButtonText(etx); }
                Tooltip.Add(eGo, "Save the item's texture (the atlas, exactly as it will be uploaded) as a PNG file on your PC.");
                widgets.Add(eGo);
            }
            // "Size:" row under the Texture box: 512 / 256 / 128 for the item's one texture (atlas or picked image)
            var texField = sidebar.Find("TextureField") as RectTransform;
            if (texField != null && visLabel != null)
            {
                const float SizeRow = 44f;
                foreach (var n in new[] { "VisibilityLabel", "VisibilityDropdown" })
                {
                    var r = sidebar.Find(n) as RectTransform;
                    if (r != null) r.anchoredPosition -= new Vector2(0f, SizeRow);
                }
                float fieldBottom = texField.anchoredPosition.y - texField.sizeDelta.y * texField.pivot.y;
                float rowY = fieldBottom - 8f - 16f;          // centre of the new row
                var sl = (GameObject)UnityEngine.Object.Instantiate(texLabel.gameObject, sidebar, false);
                sl.name = "TextureSizeLabel";
                var slt = sl.GetComponent<Text>();
                slt.text = "Size:";
                slt.fontSize = Mathf.Max(10, Mathf.RoundToInt(slt.fontSize * 0.7f));
                var slr = (RectTransform)sl.transform;
                slr.pivot = new Vector2(0f, 0.5f); slr.sizeDelta = new Vector2(100f, 32f);
                slr.anchoredPosition = new Vector2(texLabel.anchoredPosition.x, rowY);
                slt.alignment = TextAnchor.MiddleLeft;
                widgets.Add(sl);

                var sGo = (GameObject)UnityEngine.Object.Instantiate(w.visibilityDropdown.gameObject, sidebar, false);
                sGo.name = "TextureSizeDropdown";
                var srt = (RectTransform)sGo.transform;
                srt.pivot = new Vector2(0.5f, 0.5f);
                srt.anchoredPosition = new Vector2(vis.anchoredPosition.x, rowY);
                srt.sizeDelta = new Vector2(Mathf.Max(vis.sizeDelta.x, 120f), vis.sizeDelta.y);
                texSizeDropdown = sGo.GetComponent<Dropdown>();
                texSizeDropdown.onValueChanged = new Dropdown.DropdownEvent();
                texSizeDropdown.ClearOptions();
                texSizeDropdown.AddOptions(new List<string> { "512px", "256px", "128px" });
                texSizeDropdown.value = 0; texSizeDropdown.RefreshShownValue();
                texSizeDropdown.onValueChanged.AddListener(delegate (int i)
                {
                    if (updatingAtlas || w.activeItem == null || string.IsNullOrEmpty(w.activeItem.sourceFile)) return;
                    ItemSettings.SetTexSize(w.activeItem, i == 2 ? 128 : i == 1 ? 256 : 512);
                    MarkImport(); Rebuild(w);
                });
                Tooltip.Add(sGo, "Size of the item's one texture in the game (the atlas, or the image picked with Browse). 512px is the game's maximum; smaller sizes load faster and use less memory.");
                widgets.Add(sGo);
            }
            atlasWidgets = widgets.ToArray();
        }

        /// objects only (worlds have no model)
        public static void UpdateAtlasUi(Workshop w)
        {
            if (reimportButton != null && reimportButton.activeSelf != w.objectTabActive) reimportButton.SetActive(w.objectTabActive);
            if (atlasWidgets == null) return;
            bool show = w.objectTabActive;
            foreach (var g in atlasWidgets) if (g != null && g.activeSelf != show) g.SetActive(show);
        }

        static void ExportAtlas(Workshop w)
        {
            var tex = w.defaultMaterial != null ? w.defaultMaterial.mainTexture as Texture2D : null;
            if (w.activeItem == null || tex == null || tex == w.defaultTexture) { SetMessage(w, "Note: there's no texture to export for this item (it only uses the default material)."); return; }
            string model = string.IsNullOrEmpty(w.activeItem.sourceFile) ? "item" : Path.GetFileNameWithoutExtension(w.activeItem.sourceFile);
            string dir = string.IsNullOrEmpty(w.activeItem.sourceFile) ? "" : Path.GetDirectoryName(w.activeItem.sourceFile);
            string path;
            try { path = SFB.StandaloneFileBrowser.SaveFilePanel("Export texture", dir, model + "_atlas", "png"); }
            catch (Exception e) { SetMessage(w, "Couldn't open the save dialog: " + e.Message); return; }
            if (string.IsNullOrEmpty(path)) return;
            if (path.StartsWith("file://")) path = path.Substring(7);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
            try
            {
                byte[] png;
                try { png = tex.EncodeToPNG(); }
                catch (Exception)
                {   // not readable: copy it through a render texture first
                    var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0);
                    Graphics.Blit(tex, rt);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
                    copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); copy.Apply();
                    RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                    png = copy.EncodeToPNG(); UnityEngine.Object.Destroy(copy);
                }
                File.WriteAllBytes(path, png);
                SetMessage(w, "Note: saved " + tex.width + "x" + tex.height + "px texture to " + path + ".");
            }
            catch (Exception e) { SetMessage(w, "Couldn't save the texture: " + e.Message); }
        }

        static void ShowAtlas(AtlasMode m, bool crop, int texSize)
        {
            updatingAtlas = true;
            try
            {
                if (texSizeDropdown != null) { texSizeDropdown.value = texSize == 128 ? 2 : texSize == 256 ? 1 : 0; texSizeDropdown.RefreshShownValue(); }
                if (atlasDropdown != null) { atlasDropdown.value = (int)m; atlasDropdown.RefreshShownValue(); }
                if (cropToggle != null) cropToggle.isOn = crop;
            }
            finally { updatingAtlas = false; }
        }

        static void ShowShading(Shading s, float angle)
        {
            if (shadingDropdown == null) return;
            updatingDropdown = true;
            try
            {
                shadingDropdown.value = (int)s; shadingDropdown.RefreshShownValue();
                if (angleField != null) angleField.text = angle.ToString("0.#");
                SetAngleVisible(s == Shading.AutoSmooth);
            }
            finally { updatingDropdown = false; }
        }

        static void OnAngleChanged(Workshop w, string v)
        {
            if (updatingDropdown) return;
            WorkshopItem item = w.activeItem;
            if (item == null || string.IsNullOrEmpty(item.sourceFile)) return;
            float a;
            if (!float.TryParse(v.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a))
            { ShowShading(ItemSettings.Get(item), ItemSettings.GetAngle(item)); return; }
            a = Mathf.Clamp(a, 0f, 180f);
            if (Mathf.Approximately(a, ItemSettings.GetAngle(item))) { angleField.text = a.ToString("0.#"); return; }
            ItemSettings.SetAngle(item, a);
            Rebuild(w);
        }

        static BuiltMesh lastFlatMesh; static ObjModel lastFlatModel; static UvRect[] lastFlatRects; static UvRect lastFlatNoRect;
        static bool SameRect(UvRect a, UvRect b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            return a.U0 == b.U0 && a.V0 == b.V0 && a.U1 == b.U1 && a.V1 == b.V1 && a.Tile == b.Tile && a.SU0 == b.SU0 && a.SV0 == b.SV0 && a.SU1 == b.SU1 && a.SV1 == b.SV1;
        }
        static bool SameRects(UvRect[] a, UvRect[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!SameRect(a[i], b[i])) return false;
            return true;
        }
        static ObjModel lastModel; static UvRect[] lastRects; static UvRect lastNoRect; static string lastPath; static int lastFlatCount = -1;
        static Text flatWarning; static RectTransform flatWarnRect, resetRect; static float flatWarnX;
        public static readonly Color ErrorRed = new Color(1f, 0f, 0f, 1f);
        public static readonly Color NoteAmber = new Color(1f, 0.42f, 0f, 1f);   // vivid orange

        static void ShowFlatWarning(string msg)
        {
            if (flatWarning == null) return;
            if (msg == null) { flatWarning.gameObject.SetActive(false); return; }
            flatWarning.text = msg;
            float x = flatWarnX;
            if (angleWidgets != null && angleWidgets[1] != null && angleWidgets[1].activeSelf)
            {
                var u = (RectTransform)angleWidgets[1].transform; x = u.anchoredPosition.x + u.sizeDelta.x + 8f;
            }
            flatWarnRect.anchoredPosition = new Vector2(x, 0f);
            // wrap before the Reset rotation button instead of running underneath it
            float width = 300f;
            var parent = flatWarnRect.parent as RectTransform;
            if (resetRect != null && parent != null)
                width = Mathf.Max(80f, parent.rect.width + resetRect.anchoredPosition.x - resetRect.sizeDelta.x - 8f - x);
            flatWarnRect.sizeDelta = new Vector2(width, 50f);
            flatWarning.horizontalOverflow = HorizontalWrapMode.Wrap;
            flatWarning.verticalOverflow = VerticalWrapMode.Overflow;
            flatWarning.gameObject.SetActive(true);
        }

        // Workshop.SetErrorMessage replacement: errors in pure red, informational notes in amber
        public static void SetMessage(Workshop w, string text)
        {
            var t = w.errorMessageText;
            if (t == null) return;
            t.text = text ?? string.Empty;
            bool note = text != null && text.StartsWith("Note:");
            t.color = note ? NoteAmber : ErrorRed;
            t.fontStyle = FontStyle.Bold;
            noteShownAt = Time.unscaledTime;
        }

        static float noteShownAt;
        /// notes clear themselves 10 seconds after they were last set (errors stay until replaced)
        public static void FadeNotes(Workshop w)
        {
            var t = w.errorMessageText;
            if (t == null || string.IsNullOrEmpty(t.text) || !t.text.StartsWith("Note:")) return;
            if (Time.unscaledTime - noteShownAt > 10f) t.text = string.Empty;
        }

        static void OnShadingChanged(Workshop w, int i)
        {
            if (updatingDropdown) return;
            WorkshopItem item = w.activeItem;
            if (item == null || string.IsNullOrEmpty(item.sourceFile)) return;
            ShowFlatWarning(null);
            if ((Shading)i == Shading.Flat && lastModel != null && lastPath == item.sourceFile)
            {
                if (lastFlatCount < 0)
                {
                    lastFlatMesh = MeshBuilder.Build(lastModel, lastRects, lastNoRect, Shading.Flat);   // kept: the rebuild below reuses it
                    lastFlatCount = lastFlatMesh.VertexCount;
                }
                if (lastFlatCount > VertexLimit)
                {   // leave the current shading in place and say why
                    ShowShading(ItemSettings.Get(item), ItemSettings.GetAngle(item));
                    ShowFlatWarning("Flat needs " + lastFlatCount.ToString("N0") + " vertices (limit " + VertexLimit.ToString("N0") + ")");
                    return;
                }
            }
            ItemSettings.Set(item, (Shading)i);
            SetAngleVisible((Shading)i == Shading.AutoSmooth);
            Rebuild(w);                                // reload the preview with the new shading
        }

    }
}
