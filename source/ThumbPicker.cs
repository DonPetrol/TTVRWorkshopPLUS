// TTVR Workshop Plus - custom Workshop thumbnails and 3D view toggles (grid, ruler, direction arrow).
// The thumbnail box sits above the Title field: it previews the image that will be uploaded with the item,
// which is the tool's automatic thumbnail unless you take one from the current camera or pick an image.
// Custom thumbnails are kept in Development\workshopplus_customthumbs\<item folder>.png.
// For a world, the tool's own "Preview:" row (Browse / Reset) did the same job - pick the picture uploaded as the
// Workshop thumbnail - so on worlds that row is hidden and Choose image / Use default do all of it: the big picture in
// the middle of the page shows the chosen image (square, as the tool showed it), and Use default also undoes a picture
// picked earlier with the old Browse. (On items the row is the texture row, and stays.)
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TTVRPlus
{
    public static class ThumbPicker
    {
        const float Shift = 110f;               // how far the sidebar's fields move down to make room
        const float Top = -52f, Step = 37f;     // thumbnail box / button rows
        const int Size = 1024;                  // same size the tool uploads
        static Workshop ws;
        static GameObject[] group;
        static RawImage preview;
        static Button defaultButton, cameraButton, chooseButton;
        static bool shownOwned;                  // false when showing the world's own preview texture (not ours to destroy)
        static Texture2D shown;
        static float lastRender;

        static string Dir() { return Path.Combine(FileUtils.GetWorkshopDevelopmentDirectory(), "workshopplus_customthumbs"); }
        static string FileFor(WorkshopItem it)
        {
            if (it == null || string.IsNullOrEmpty(it.uniqueDirectory)) return null;
            return Path.Combine(Dir(), it.uniqueDirectory + ".png");
        }
        public static string CustomFile(WorkshopItem it) { try { return FileFor(it); } catch (Exception) { return null; } }
        static bool HasCustom(WorkshopItem it) { string f = FileFor(it); return f != null && File.Exists(f); }
        public static bool HasCustomThumb(WorkshopItem it) { try { return HasCustom(it); } catch (Exception) { return false; } }

        public static void Init(Workshop w)
        {
            ws = w;
            try { BuildThumbBox(w); } catch (Exception e) { Debug.Log("[WorkshopPlus] thumbnail box: " + e); }
            try { BuildViewToggles(w); } catch (Exception e) { Debug.Log("[WorkshopPlus] view toggles: " + e); }
        }

        // ---------------------------------------------------------------- thumbnail box
        static void BuildThumbBox(Workshop w)
        {
            var sidebar = (RectTransform)w.titleField.transform.parent;
            foreach (Transform c in sidebar)
            {
                var r = c as RectTransform;
                if (r != null && r.anchorMin.y == 1f && r.anchorMax.y == 1f) r.anchoredPosition -= new Vector2(0f, Shift);
            }
            var items = new List<GameObject>();

            var titleLabel = sidebar.Find("TitleLabel");
            if (titleLabel != null)
            {
                var lGo = (GameObject)UnityEngine.Object.Instantiate(titleLabel.gameObject, sidebar, false);
                lGo.name = "ThumbnailLabel";
                ((RectTransform)lGo.transform).anchoredPosition = new Vector2(15f, -67f);
                var lt = lGo.GetComponent<Text>(); if (lt != null) lt.text = "Info:";
                items.Add(lGo);
                titleLabel.gameObject.SetActive(false);        // the Title box sits right under the thumbnail instead
            }

            var box = new GameObject("ThumbnailBox", typeof(RectTransform));
            var brt = (RectTransform)box.transform;
            brt.SetParent(sidebar, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f); brt.pivot = new Vector2(0f, 1f);
            brt.sizeDelta = new Vector2(104f, 104f); brt.anchoredPosition = new Vector2(15f, Top);
            var bg = box.AddComponent<Image>(); bg.color = new Color(0.85f, 0.85f, 0.85f, 1f); bg.raycastTarget = false;
            var pGo = new GameObject("Preview", typeof(RectTransform));
            var prt = (RectTransform)pGo.transform; prt.SetParent(brt, false);
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = new Vector2(2f, 2f); prt.offsetMax = new Vector2(-2f, -2f);
            preview = pGo.AddComponent<RawImage>(); preview.raycastTarget = false; preview.color = Color.white;
            items.Add(box);

            cameraButton = MakeButton(w, sidebar, "ThumbFromCamera", "From camera", Top, delegate { FromCamera(w); },
                "Use the current 3D view (drag to orbit, scroll to zoom) as the Workshop thumbnail.");
            chooseButton = MakeButton(w, sidebar, "ThumbChooseImage", "Choose image", Top - Step, delegate { ChooseImage(w); },
                "Use an image file (PNG, JPG, DDS or TGA) as the Workshop thumbnail, at its own size and shape (made smaller only if needed for Steam's 1MB limit).");
            defaultButton = MakeButton(w, sidebar, "ThumbDefault", "Use default", Top - 2 * Step, delegate { UseDefault(w); }, "");
            Tooltip.Add(defaultButton.gameObject, () => ws != null && ws.objectTabActive
                ? "Go back to the automatic thumbnail the tool makes when publishing."
                : "Go back to the world's own preview.jpg.");
            items.Add(chooseButton.gameObject); items.Add(defaultButton.gameObject);
            group = items.ToArray();

            // the tool's Preview/Texture row: hidden on worlds (see the top of this file)
            var label = HarmonyLib.AccessTools.Field(typeof(Workshop), "textureLabel").GetValue(w) as Text;       // (private in the tool)
            previewRow.Add(label != null ? label.gameObject : null);
            previewRow.Add(w.textureField != null ? w.textureField.gameObject : null);
            foreach (string routine in new[] { "OnTextureBrowseButtonClick", "OnTextureClearButtonClick" })
            {
                var b = Ui.ButtonCalling(w.detailView.transform, routine);
                if (b != null) previewRow.Add(b.gameObject);
            }
        }
        static readonly List<GameObject> previewRow = new List<GameObject>();

        // a world's own picture (the default), and whether the item points at another one (picked with the old Browse)
        static string OwnPreview(WorkshopItem it)
        {
            try { return string.IsNullOrEmpty(it.sourceFile) ? null : Path.Combine(Path.GetDirectoryName(it.sourceFile), "preview.jpg"); }
            catch (Exception) { return null; }
        }
        static bool OldBrowsePicture(WorkshopItem it)
        {
            string own = OwnPreview(it);
            return own != null && !string.IsNullOrEmpty(it.textureFile) && !string.Equals(Path.GetFullPath(it.textureFile), Path.GetFullPath(own), StringComparison.OrdinalIgnoreCase);
        }

        // the big picture in the middle of a world's page, while it shows a chosen image (ours to free)
        static Texture2D centre;
        static readonly System.Reflection.MethodInfo squarePicture = HarmonyLib.AccessTools.Method(typeof(Workshop), "LoadPreviewImageIntoSquareTexture");
        static void ShowCentre(string path)
        {
            if (ws.previewImage == null || squarePicture == null) return;
            Texture2D t = null;
            try { t = squarePicture.Invoke(ws, new object[] { path }) as Texture2D; } catch (Exception e) { Debug.Log("[WorkshopPlus] world picture: " + e.Message); }
            if (t == null) return;
            if (centre != null && centre != t) UnityEngine.Object.Destroy(centre);
            centre = t; ws.previewImage.texture = t;
        }

        static Button MakeButton(Workshop w, Transform parent, string name, string label, float y, UnityEngine.Events.UnityAction click, string tip)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, parent, false);
            go.name = name; go.SetActive(true);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(110f, 31f); rt.anchoredPosition = new Vector2(126f, y);
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent(); b.interactable = true;
            b.onClick.AddListener(click);
            var t = go.GetComponentInChildren<Text>(); if (t != null) { t.text = label; WorkshopPlus.FitButtonText(t); }
            if (!string.IsNullOrEmpty(tip)) Tooltip.Add(go, tip);      // (none for buttons that say what they do)
            return b;
        }

        // every frame (from the list follower): keep the box in step with the item being edited
        public static void Update()
        {
            if (ws == null || group == null) return;
            bool objects = ws.objectTabActive;
            bool editing = ws.detailView.activeSelf && ws.activeItem != null;
            foreach (var g in group) if (g != null && g.activeSelf != ws.detailView.activeSelf) g.SetActive(ws.detailView.activeSelf);
            foreach (var g in previewRow) if (g != null && g.activeSelf != objects) g.SetActive(objects);
            // the tool put another picture in the middle (another world opened, or Use default): ours is no longer shown
            if (centre != null && (ws.previewImage == null || ws.previewImage.texture != centre)) { UnityEngine.Object.Destroy(centre); centre = null; }
            if (cameraButton != null && cameraButton.gameObject.activeSelf != (objects && ws.detailView.activeSelf))
            {   // worlds: no camera option; Choose image / Use default move up a row
                cameraButton.gameObject.SetActive(objects && ws.detailView.activeSelf);
                Place(chooseButton, objects ? Top - Step : Top); Place(defaultButton, objects ? Top - 2 * Step : Top - Step);
            }
            if (!editing || preview == null) return;
            // only cheap comparisons here (runs every frame): the disk is checked when the item changes or a thumbnail is set
            var it = ws.activeItem;
            if (it != lastItem) { lastItem = it; Invalidate(); }
            if (customState < 0)
            {
                customState = HasCustom(it) ? 1 : 0;
                if (defaultButton != null) defaultButton.interactable = customState == 1 || (!objects && OldBrowsePicture(it));
            }
            if (customState == 1)
            {
                if (shownMode != 1)
                {
                    Show(LoadPng(FileFor(it))); shownMode = 1;
                    if (!objects) ShowCentre(FileFor(it));          // a world: the big picture shows it too
                }
                return;
            }
            if (!objects)
            {   // a world's default Workshop thumbnail is its preview.jpg, already loaded by the tool
                var wt = ws.previewImage != null ? ws.previewImage.texture as Texture2D : null;
                int wid = wt != null ? wt.GetInstanceID() : 0;
                if (shownMode != 2 || wid != shownA) { Show(wt, false); shownMode = 2; shownA = wid; }
                return;
            }
            var root = ws.objectRoot;
            if (root == null || root.childCount == 0) return;
            var tex = ws.defaultMaterial != null ? ws.defaultMaterial.mainTexture : null;
            int child = root.GetChild(0).GetInstanceID(), texId = tex != null ? tex.GetInstanceID() : 0;
            if (shownMode == 3 && child == shownA && texId == shownB && root.localRotation == shownRot && root.localScale == shownScale) return;
            if (Time.unscaledTime - lastRender < 0.3f) return;
            lastRender = Time.unscaledTime;
            shownMode = 3; shownA = child; shownB = texId; shownRot = root.localRotation; shownScale = root.localScale;
            try { Show(ws.thumbnailGenerator.GenerateThumbnail(root.gameObject, 256, 256)); }
            catch (Exception e) { Debug.Log("[WorkshopPlus] thumbnail preview: " + e.Message); }
        }

        static WorkshopItem lastItem;
        static int customState = -1, shownMode, shownA, shownB;      // shownMode: 0 nothing, 1 custom, 2 world preview, 3 automatic
        static Quaternion shownRot; static Vector3 shownScale;
        static void Invalidate() { customState = -1; shownMode = 0; }

        static void Place(Button b, float y)
        {
            if (b == null) return;
            var rt = (RectTransform)b.transform; rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        }

        static void Show(Texture2D t) { Show(t, true); }
        const int MaxLongSide = 2048;           // a chosen image that has to be made smaller never stays larger than this
        const int MaxPreviewBytes = 1000 * 1000, UploadQuality = 75;     // Steam Workshop preview images must be under 1MB; the tool uploads JPG at quality 75

        static void Show(Texture2D t, bool owned)
        {
            if (shown != null && shown != t && shownOwned) UnityEngine.Object.Destroy(shown);
            shown = t; shownOwned = owned;
            preview.texture = t;
            // fit inside the box, keeping the picture's shape
            var r = preview.rectTransform;
            float a = t != null && t.height > 0 ? (float)t.width / t.height : 1f, box = 100f;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = a >= 1f ? new Vector2(box, box / a) : new Vector2(box * a, box);
        }

        static Texture2D LoadPng(string f)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try { t.LoadImage(File.ReadAllBytes(f)); } catch (Exception) { }
            return t;
        }

        static void Save(WorkshopItem it, Texture2D t)
        {
            Directory.CreateDirectory(Dir());
            File.WriteAllBytes(FileFor(it), t.EncodeToPNG());
            Invalidate();
        }

        // saves a new custom thumbnail and always frees the texture it was made from.
        // false = the file couldn't be written (disk full, folder read-only...); the reason has been shown.
        static bool SaveAndFree(Workshop w, WorkshopItem it, Texture2D t)
        {
            try { Save(it, t); return true; }
            catch (Exception e)
            {
                Debug.Log("[WorkshopPlus] thumbnail: " + e);
                w.SetErrorMessage("Couldn't save the thumbnail: " + e.Message);
                return false;
            }
            finally { UnityEngine.Object.Destroy(t); }
        }

        // the current 3D view, rendered like the automatic thumbnail (model only, same background), square
        static void FromCamera(Workshop w)
        {
            if (w.activeItem == null || w.objectRoot == null || w.rttCamera == null) return;
            var gen = w.thumbnailGenerator;
            var cam = gen != null ? gen.GetComponent<Camera>() : null;
            if (cam == null) { w.SetErrorMessage("Couldn't take a thumbnail from the camera."); return; }
            int layer = LayerMask.NameToLayer("VirtualCamera");
            var saved = new Dictionary<Transform, int>();
            foreach (var tr in w.objectRoot.GetComponentsInChildren<Transform>(true)) { saved[tr] = tr.gameObject.layer; if (layer >= 0) tr.gameObject.layer = layer; }
            Vector3 pos = cam.transform.position; Quaternion rot = cam.transform.rotation; float fov = cam.fieldOfView;
            var oldTarget = cam.targetTexture; bool wasEnabled = cam.enabled;
            var oldActive = RenderTexture.active;
            var rt = new RenderTexture(Size, Size, 24);
            Texture2D result = null;
            try
            {
                cam.transform.position = w.rttCamera.transform.position;
                cam.transform.rotation = w.rttCamera.transform.rotation;
                cam.fieldOfView = w.rttCamera.fieldOfView;
                cam.aspect = 1f;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                result = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                result.Apply();
            }
            catch (Exception e)
            {
                Debug.Log("[WorkshopPlus] thumbnail from camera: " + e);
                w.SetErrorMessage("Couldn't take a thumbnail from the camera: " + e.Message);
                if (result != null) UnityEngine.Object.Destroy(result);
                return;                                // (the finally below still puts everything back)
            }
            finally
            {
                RenderTexture.active = oldActive;      // also when something above failed
                cam.targetTexture = oldTarget; cam.enabled = wasEnabled; cam.ResetAspect();
                cam.transform.position = pos; cam.transform.rotation = rot; cam.fieldOfView = fov;
                foreach (var kv in saved) if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
                UnityEngine.Object.Destroy(rt);
            }
            if (!SaveAndFree(w, w.activeItem, result)) return;
            Watermark.MarkCamera(w.activeItem, true);          // (taken from the camera: it still gets the watermark, if ticked)
            w.SetErrorMessage("Note: thumbnail taken from the current view. It's uploaded the next time you press Publish.");
        }

        static void ChooseImage(Workshop w)
        {
            if (w.activeItem == null) return;
            string path = w.GetFilePath("Image Files (PNG, JPG, DDS, TGA) ", string.Empty, new string[] { "png", "jpg; *.jpeg; *.dds; *.tga" });
            if (string.IsNullOrEmpty(path)) return;
            int iw, ih; byte[] px; string err;
            byte[] data;
            try { data = File.ReadAllBytes(path); } catch (Exception e) { w.SetErrorMessage("Couldn't read " + Path.GetFileName(path) + ": " + e.Message); return; }
            if (!WorkshopPlus.DecodeImage(data, path, out iw, out ih, out px, out err)) { w.SetErrorMessage(Path.GetFileName(path) + err); return; }
            // kept at its own size and shape; only made smaller if the upload would go over Steam's 1MB preview limit
            int ow = iw, oh = ih;
            Texture2D t = null;
            while (true)
            {
                t = Pixels.Texture(px, iw, ih, false, true, TextureFormat.RGB24);
                int jpgLength = t.EncodeToJPG(UploadQuality).Length;
                if (jpgLength <= MaxPreviewBytes || Math.Max(iw, ih) <= 256) break;
                UnityEngine.Object.Destroy(t);
                // Too big for Steam. A JPG's size goes roughly with its number of pixels, so aim straight for the limit
                // (a little under it) instead of shrinking a small step at a time, and never keep more than 2048px on the
                // long side. The loop only comes round again if that guess still turned out too big.
                float k = Mathf.Sqrt((float)MaxPreviewBytes / jpgLength) * 0.95f;
                k = Mathf.Min(k, 0.95f, (float)MaxLongSide / Math.Max(iw, ih));
                int nw = Math.Max(1, (int)(iw * k)), nh = Math.Max(1, (int)(ih * k));
                px = Atlas.Resize(new TextureSource { Width = iw, Height = ih, Rgba = px }, nw, nh);
                iw = nw; ih = nh;
            }
            if (!SaveAndFree(w, w.activeItem, t)) return;
            Watermark.MarkChosen(w.activeItem);                // (a chosen picture: uploaded as it is, no watermark)
            w.SetErrorMessage("Note: thumbnail set from " + Path.GetFileName(path) + (iw != ow ? " (made smaller, " + iw + "x" + ih + "px, to stay under Steam's 1MB limit)" : "") + ". It's uploaded the next time you press Publish.");
        }

        static void UseDefault(Workshop w)
        {
            string f = FileFor(w.activeItem);
            try { if (f != null && File.Exists(f)) File.Delete(f); } catch (Exception) { }
            if (!w.objectTabActive && w.activeItem != null)
            {   // a world: what the tool's own Reset did - back to its preview.jpg, shown in the middle again - and remembered
                try { w.OnTextureClearButtonClick(); w.AddOrUpdateMetadata(w.activeItem); }
                catch (Exception e) { Debug.Log("[WorkshopPlus] world picture: " + e.Message); }
            }
            Watermark.MarkCamera(w.activeItem, false);
            Invalidate();
            w.SetErrorMessage(w.objectTabActive ? "Note: using the automatic thumbnail again. It's uploaded the next time you press Publish."
                                                : "Note: using the world's preview.jpg again. It's uploaded the next time you press Publish.");
        }

        public static void CopyCustom(WorkshopItem from, WorkshopItem to)
        {
            try { string a = FileFor(from), b = FileFor(to); if (a != null && b != null && File.Exists(a)) File.Copy(a, b, true); } catch (Exception) { }
            Watermark.Copy(from, to);
        }

        /// a world's custom Workshop thumbnail as JPG bytes, or null to keep its preview.jpg
        public static byte[] WorldPreviewOverride(WorkshopItem it)
        {
            if (!HasCustom(it))
            {
                // the world's own picture, with the watermark if it is ticked (Watermark.cs)
                var own = ws != null && ws.previewImage != null ? ws.previewImage.texture : null;
                if (own == null || !Watermark.Applies(it)) return null;
                var s = Watermark.Stamp(own);
                try { return s.EncodeToJPG(UploadQuality); } finally { UnityEngine.Object.Destroy(s); }
            }
            var t = LoadPng(FileFor(it));
            try
            {
                if (t.width <= 8) return null;
                if (Watermark.Applies(it)) { var s = Watermark.Stamp(t); UnityEngine.Object.Destroy(t); t = s; }
                return t.EncodeToJPG(UploadQuality);
            }
            finally { UnityEngine.Object.Destroy(t); }
        }

        /// replaces the thumbnail call in Workshop.GenerateObjectData (publishing)
        public static Texture2D GenerateThumbnail(ThumbnailGenerator gen, GameObject go, int width, int height)
        {
            // the tool turns the texture into a JPG and then forgets it without freeing it, so the one handed out
            // last time is freed here (by now the tool has finished with it)
            if (lastPublishThumb != null) UnityEngine.Object.Destroy(lastPublishThumb);
            lastPublishThumb = null;
            Texture2D result = null;
            if (ws != null && HasCustom(ws.activeItem))
            {
                var t = LoadPng(FileFor(ws.activeItem));
                if (t.width > 8) result = t; else UnityEngine.Object.Destroy(t);
            }
            if (result == null) result = gen.GenerateThumbnail(go, width, height);
            // the "Requires TTVR Enhanced" band, if ticked (not on a chosen picture: Watermark.cs)
            if (ws != null && result != null && Watermark.Applies(ws.activeItem))
            {
                try { var s = Watermark.Stamp(result); UnityEngine.Object.Destroy(result); result = s; }
                catch (Exception e) { Debug.Log("[WorkshopPlus] watermark: " + e); }
            }
            return lastPublishThumb = result;
        }
        static Texture2D lastPublishThumb;

        // ---------------------------------------------------------------- 3D view toggles
        class ViewToggle { public GameObject Target; public Button Button; public Text Label; public string Pref, Name; public bool? Shown; public Color OnColor, OnText; }
        static readonly List<ViewToggle> toggles = new List<ViewToggle>();

        static void BuildViewToggles(Workshop w)
        {
            var previewArea = w.detailView.transform.Find("Preview") as RectTransform;
            if (previewArea == null) return;
            var defs = new[] {
                new[] { "Scene/Canvas", "Direction", "Show or hide the Forward arrow and text." },
                new[] { "Scene/ScaleReference", "Ruler", "Show or hide the height ruler next to the model." },
                new[] { "Scene/Grid", "Grid", "Show or hide the floor grid." },
                new[] { "Scene/Background", "Floor", "Show or hide the flat floor the model stands on." } };
            float x = -10f;
            foreach (var d in defs)
            {
                var target = GameObject.Find(d[0]);
                if (target == null) { Debug.Log("[WorkshopPlus] " + d[0] + " not found"); continue; }
                var vt = new ViewToggle { Target = target, Name = d[1], Pref = "WorkshopPlus.View." + d[1] };
                var go = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, previewArea, false);
                go.name = "View" + d[1] + "Toggle"; go.SetActive(true);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(d[1] == "Direction" ? 100f : 72f, 31f); rt.anchoredPosition = new Vector2(x, -12f);
                x -= rt.sizeDelta.x + 6f;
                vt.Button = go.GetComponent<Button>();
                vt.Button.onClick = new Button.ButtonClickedEvent(); vt.Button.interactable = true;
                vt.Button.onClick.AddListener(delegate { Set(vt, !vt.Target.activeSelf); });
                vt.OnColor = vt.Button.image != null ? vt.Button.image.color : Color.white;
                vt.Label = go.GetComponentInChildren<Text>();
                if (vt.Label != null) { vt.Label.text = d[1]; vt.OnText = vt.Label.color; WorkshopPlus.FitButtonText(vt.Label); }
                toggles.Add(vt);
                Set(vt, PlayerPrefs.GetInt(vt.Pref, 1) == 1);
            }
        }

        static void Set(ViewToggle vt, bool on)
        {
            vt.Target.SetActive(on);
            PlayerPrefs.SetInt(vt.Pref, on ? 1 : 0);
            vt.Shown = null;
            Style(vt);
        }

        static readonly Color OffColor = new Color(0.92f, 0.92f, 0.92f, 1f), OffText = new Color(0.2f, 0.2f, 0.2f, 1f);
        static void Style(ViewToggle vt)
        {
            bool on = vt.Target != null && vt.Target.activeSelf;
            if (vt.Shown.HasValue && vt.Shown.Value == on) return;
            vt.Shown = on;
            if (vt.Button.image != null) vt.Button.image.color = on ? vt.OnColor : OffColor;
            if (vt.Label != null) vt.Label.color = on ? vt.OnText : OffText;
        }

        public static void UpdateToggles() { foreach (var vt in toggles) Style(vt); }
    }
}
