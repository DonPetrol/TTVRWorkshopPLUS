// TTVR Workshop Plus - start-up (the window first, the list loaded after), Run Game, and tooltips for the tool's own buttons.
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
        // ---------------------------------------------------------------- faster start-up (replaces Workshop.Start)
        // The original asked Steam about every published item one at a time, inside Start(), before the window was
        // drawn. This shows the window straight away and fetches the item details in parallel.
        public static void Start(Workshop w)
        {
            w.objectItems = new List<WorkshopItem>();
            w.worldItems = new List<WorkshopItem>();
            w.defaultMaterial.mainTexture = w.defaultTexture;
            w.objectTabActive = true;
            w.activeItems = w.objectItems;
            Init(w);
            WorkshopList.Init(w);
            SteamStatus.Init(w);
            Debug.Log("[WorkshopPlus " + Version + "] started");
            ThumbPicker.Init(w);
            Watermark.Init(w);                      // "Requires TTVR Enhanced Watermark", under the Info box
            try { InitAtlasUi(w); } catch (Exception e) { Debug.Log("[WorkshopPlus] atlas options: " + e); }
            ItemLook.Init(w);                       // "Shader Properties" (after the atlas rows: it goes under them), only when Tiny Town Enhanced is installed
            try { AddStockTooltips(w); } catch (Exception e) { Debug.Log("[WorkshopPlus] tooltips: " + e.Message); }
            DescScroll.Setup(w);
            AddRunGameButton(w);
            AssetBrowser.Init(w);                   // "Asset Browser", left of Run Game
            w.ActivateListView();
            w.StartCoroutine(LoadItemsAsync(w));
        }

        const int MaxInFlight = 32;

        static bool loadingItems;

        /// gets the published items from Steam and puts them in the list. Only one run at a time: a second one
        /// started meanwhile (after reconnecting to Steam, say) would put every item in the list twice.
        static IEnumerator LoadItemsAsync(Workshop w)
        {
            if (loadingItems) yield break;
            loadingItems = true;
            try
            {
                IEnumerator run = LoadItemsRun(w);
                while (run.MoveNext()) yield return run.Current;
            }
            finally { loadingItems = false; }
        }

        static IEnumerator LoadItemsRun(Workshop w)
        {
            Text title = null;
            Transform tt = w.message != null ? w.message.transform.Find("Title") : null;
            if (tt != null) title = tt.GetComponent<Text>();
            string oldTitle = title != null ? title.text : null, oldDesc = w.messageDescription.text;
            bool oldTab = w.objectTabActive;
            GameObject arrow = null;
            Transform at = w.message != null ? w.message.transform.Find("Arrow") : null;
            if (at != null) arrow = at.gameObject;
            bool arrowWasActive = arrow != null && arrow.activeSelf;
            if (arrow != null) arrow.SetActive(false);
            if (title != null) title.text = "Loading...";
            w.messageDescription.text = "Getting your Workshop items from Steam.";
            yield return null; yield return null;          // let the window draw first

            var ids = new List<PublishedFileId_t>();
            var details = new List<RemoteStorageGetPublishedFileDetailsResult_t>();
            int failed = 0;
            bool steamOk = SteamManager.Initialized;
            if (steamOk)
            {
                // 1) item IDs, 50 per page (as before)
                int total = -1;
                while (total < 0 || ids.Count < total)
                {
                    bool done = false, ok = false; int returned = 0;
                    var cr = CallResult<RemoteStorageEnumerateUserPublishedFilesResult_t>.Create(
                        delegate (RemoteStorageEnumerateUserPublishedFilesResult_t r, bool io)
                        {
                            if (!io && r.m_eResult == EResult.k_EResultOK)
                            {
                                total = r.m_nTotalResultCount; returned = r.m_nResultsReturned; ok = true;
                                for (int i = 0; i < r.m_nResultsReturned; i++) ids.Add(r.m_rgPublishedFileId[i]);
                            }
                            else Debug.Log("[WorkshopPlus] Steam couldn't list published items (" + r.m_eResult + ")");
                            done = true;
                        });
                    cr.Set(SteamRemoteStorage.EnumerateUserPublishedFiles((uint)ids.Count));
                    var sw = Stopwatch.StartNew();
                    while (!done && sw.Elapsed.TotalSeconds < 20) { SteamAPI.RunCallbacks(); yield return null; }
                    cr.Dispose();
                    if (!ok || returned == 0) { if (!ok) steamOk = false; break; }
                    w.messageDescription.text = "Getting your Workshop items from Steam (" + ids.Count + " found).";
                }

                // 2) details for every item, many requests at once
                var results = new RemoteStorageGetPublishedFileDetailsResult_t[ids.Count];
                var got = new bool[ids.Count];
                var calls = new List<CallResult<RemoteStorageGetPublishedFileDetailsResult_t>>();
                int next = 0, pending = 0, completed = 0;
                var timer = Stopwatch.StartNew();
                while (completed < ids.Count && timer.Elapsed.TotalSeconds < 60)
                {
                    while (next < ids.Count && pending < MaxInFlight)
                    {
                        int idx = next++;
                        var c = CallResult<RemoteStorageGetPublishedFileDetailsResult_t>.Create(
                            delegate (RemoteStorageGetPublishedFileDetailsResult_t r, bool io)
                            {
                                if (!io && r.m_eResult == EResult.k_EResultOK) { results[idx] = r; got[idx] = true; }
                                else Debug.Log("[WorkshopPlus] Steam couldn't load item " + ids[idx] + " (" + r.m_eResult + ")");
                                pending--; completed++;
                            });
                        c.Set(SteamRemoteStorage.GetPublishedFileDetails(ids[idx], 0u));
                        calls.Add(c); pending++;
                    }
                    SteamAPI.RunCallbacks();
                    w.messageDescription.text = "Getting your Workshop items from Steam (" + completed + " of " + ids.Count + ").";
                    yield return null;
                }
                foreach (var c in calls) c.Dispose();
                for (int i = 0; i < ids.Count; i++) { if (got[i]) details.Add(results[i]); else failed++; }
                Debug.Log("[WorkshopPlus] loaded " + details.Count + " of " + ids.Count + " published items in " + timer.Elapsed.TotalSeconds.ToString("0.0") + "s");
            }

            // 3) build the list exactly like Workshop.LoadItems did
            if (title != null) title.text = oldTitle;
            // the text that was there belongs to the tab that was open then; after a change of tab, the tool's own text for this one
            w.messageDescription.text = w.objectTabActive == oldTab ? oldDesc
                : w.objectTabActive ? "Click on the green plus button to create your first item." : "Click on the green plus button to upload your first world.";
            if (arrow != null) arrow.SetActive(arrowWasActive);
            if (!steamOk || !SteamManager.Initialized)
            {
                Debug.Log("[WorkshopPlus] couldn't get published items from Steam");
                SteamStatus.ItemsLoaded(w, false);
                yield break;
            }
            Dictionary<ulong, WorkshopMetadata> meta = w.LoadMetadata();
            var objs = new List<WorkshopItem>(); var worlds = new List<WorkshopItem>();
            foreach (var d in details)
            {
                var item = new WorkshopItem();
                item.id = d.m_nPublishedFileId.m_PublishedFileId;
                WorkshopList.SetUpdated(item.id, d.m_rtimeUpdated);
                Thumbnails.SetSource(item.id, d.m_hPreviewFile, d.m_rtimeUpdated);
                item.title = d.m_rgchTitle;
                item.description = d.m_rgchDescription;
                item.rotation = Quaternion.identity;
                item.scale = 1f;
                item.uniqueDirectory = (d.m_pchFileName ?? "").Replace(".gz", string.Empty);
                switch (d.m_eVisibility)
                {
                    case ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate: item.visibility = 0; break;
                    case ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly: item.visibility = 1; break;
                    case ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic: item.visibility = 2; break;
                }
                WorkshopMetadata m;
                if (meta.TryGetValue(item.id, out m))
                {
                    item.rotation = m.rotation; item.scale = m.scale; item.sourceFile = m.sourceFile; item.textureFile = m.textureFile;
                }
                if (item.uniqueDirectory.Contains("_world")) worlds.Add(item); else objs.Add(item);
            }
            // keep anything created while loading, after the published items
            w.objectItems.InsertRange(0, objs);
            w.worldItems.InsertRange(0, worlds);
            if (failed > 0)
                w.SetErrorMessage(failed + " published item" + (failed == 1 ? "" : "s") + " couldn't be loaded from Steam. Restart to try again.");
            if (w.listView.activeSelf) { w.ActivateListView(); w.tableView.ReloadData(); }
            WorkshopList.PrefetchCounts(w);
            Thumbnails.StartPump(w);
            SteamStatus.ItemsLoaded(w, true);
        }

        /// after reconnecting to Steam
        public static void ReloadItems(Workshop w) { w.StartCoroutine(LoadItemsAsync(w)); }

        static Button flipButton; static Text flipText; static Color flipOnColor, flipOnTextColor; static bool? flipShown;
        static readonly Color FlipOffColor = new Color(0.92f, 0.92f, 0.92f, 1f), FlipOffTextColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        // x (from the right edge of the bar) where the "Scale:" text starts, so Flip X never covers it
        static float ScaleTextLeft(Transform contents)
        {
            float left = -290f;
            try
            {
                Transform lt = contents.Find("ScaleLabel"), ft = contents.Find("ScaleField");
                if (ft != null) { var fr = (RectTransform)ft; left = Mathf.Min(left, fr.anchoredPosition.x - fr.sizeDelta.x * fr.pivot.x); }
                var t = lt != null ? lt.GetComponent<Text>() : null;
                if (t != null)
                {
                    var r = t.rectTransform;
                    float rl = r.anchoredPosition.x - r.sizeDelta.x * r.pivot.x, rr = rl + r.sizeDelta.x, pw = t.preferredWidth;
                    float tl;
                    switch (t.alignment)
                    {
                        case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: tl = rl; break;
                        case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: tl = rr - pw; break;
                        default: tl = (rl + rr - pw) * 0.5f; break;
                    }
                    left = Mathf.Min(left, tl);
                }
            }
            catch (Exception) { }
            return left;
        }

        /// shrink a button's label to fit its button instead of wrapping out of sight
        public static void FitButtonText(Text t)
        {
            if (t == null) return;
            var r = t.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(4f, 2f); r.offsetMax = new Vector2(-4f, -2f);
            int fs = t.resizeTextForBestFit ? Math.Max(t.resizeTextMaxSize, t.fontSize) : t.fontSize;
            t.resizeTextForBestFit = true; t.resizeTextMaxSize = fs; t.resizeTextMinSize = Math.Max(8, fs / 3);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// a bit bigger text for the small boxes copied into the top bar (search, sort)
        public static void Larger(Text t)
        {
            if (t == null) return;
            t.fontSize = Mathf.RoundToInt(t.fontSize * 1.45f);
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// "Run Game" just left of the ? (help) button in the top bar
        static void AddRunGameButton(Workshop w)
        {
            try
            {
                Button help = Ui.ButtonCalling(w.listView.transform.parent, "OnHelpButtonClicked");
                if (help == null) { Debug.Log("[WorkshopPlus] help button not found, no Run Game button"); return; }
                var hrt = (RectTransform)help.transform;
                var run = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, hrt.parent, false);
                run.name = "RunGameButton"; run.SetActive(true);
                var rt = (RectTransform)run.transform;
                rt.anchorMin = hrt.anchorMin; rt.anchorMax = hrt.anchorMax; rt.pivot = new Vector2(1f, hrt.pivot.y);
                rt.sizeDelta = new Vector2(120f, Mathf.Max(30f, Mathf.Min(hrt.rect.height, 42f)));
                float helpLeft = hrt.anchoredPosition.x - hrt.rect.width * hrt.pivot.x;
                rt.anchoredPosition = new Vector2(helpLeft - 8f, hrt.anchoredPosition.y);
                var rb = run.GetComponent<Button>();
                rb.onClick = new Button.ButtonClickedEvent(); rb.interactable = true;
                rb.onClick.AddListener(delegate { RunGame(w); });
                var t = run.GetComponentInChildren<Text>();
                if (t != null) { t.text = "Run Game"; FitButtonText(t); }
                WorkshopList.AlignWithSearch(rt);          // (on the search box's line)
                WorkshopList.MakeRoomForHeaderButton(rt);
            }
            catch (Exception e) { Debug.Log("[WorkshopPlus] Run Game button: " + e.Message); }
        }

        /// starts Tiny Town VR.exe from the game folder (the folder Workshop.exe is in)
        public static void RunGame(Workshop w)
        {
            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;      // ...\Tiny Town VR
                string exe = Path.Combine(root, "Tiny Town VR.exe");
                if (!File.Exists(exe))
                {
                    exe = null;
                    foreach (var f in Directory.GetFiles(root, "*.exe"))
                    {
                        string n = Path.GetFileName(f).ToLowerInvariant();
                        if (n != "workshop.exe" && !n.Contains("unitycrashhandler")) { exe = f; break; }
                    }
                }
                if (exe == null) { w.SetErrorMessage("Couldn't find Tiny Town VR.exe in " + root + "."); return; }
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                w.SetErrorMessage("Note: starting Tiny Town VR...");
            }
            catch (Exception e) { w.SetErrorMessage("Couldn't start the game: " + e.Message); }
        }

        public static void AddStockTooltips(Workshop w)
        {
            var canvas = w.listView.transform.parent;
            foreach (var b in canvas.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                {
                    string m = b.onClick.GetPersistentMethodName(i), tip = null;
                    switch (m)
                    {
                        case "OnObjFileBrowseButtonClick": tip = "Pick a different model file (OBJ, glTF or GLB). The item's settings are kept."; break;
                        case "OnTextureBrowseButtonClick": tip = "Use one image (PNG, JPG, DDS or TGA) as the texture for the whole model."; break;
                        case "OnTextureClearButtonClick": tip = "Remove the picked image and go back to the model's own textures and colours."; break;
                        case "OnPublishButtonClicked":
                            Tooltip.Add(b.gameObject, () => w.activeItem != null && w.activeItem.id != 0
                                ? "Update this item on the Steam Workshop." : "Upload this item to the Steam Workshop as a new item.");
                            break;
                    }
                    if (tip != null) Tooltip.Add(b.gameObject, tip);
                }
            var scale = w.footerContents.transform.Find("ScaleField");
            if (scale != null) Tooltip.Add(scale.gameObject, "Size multiplier for the model in the game.");
        }

        static bool IsFlipped(Workshop w) { return w.objectRoot != null && w.objectRoot.localScale.x < 0f; }

        static void ToggleFlip(Workshop w)
        {
            if (w.flipToggle == null || w.objectRoot == null) return;
            bool want = !IsFlipped(w);
            w.flipToggle.isOn = want;
            w.OnFlipToggleChanged();             // also when the tick box already had that value
            UpdateFlipButton(w);
        }

        /// Builds the model again with the item's current settings (the tool's ActivateDetailView). That routine also sets
        /// the model's size back to the item's plain scale, which undoes Flip X, so the flip is put back afterwards.
        /// (Whether it was flipped is read from the model, not the hidden tick box: the box can be left over from another item.)
        public static void Rebuild(Workshop w)
        {
            bool flipped = IsFlipped(w);
            w.ActivateDetailView();
            if (flipped && !IsFlipped(w) && w.flipToggle != null)
            {
                w.flipToggle.isOn = true;
                w.OnFlipToggleChanged();             // sets the direction from the tick box, so calling it twice does no harm
                UpdateFlipButton(w);
            }
        }

        /// highlighted (Publish colour) while flipped, plain while not; called every frame
        public static void UpdateFlipButton(Workshop w)
        {
            if (flipButton == null) return;
            bool on = IsFlipped(w);
            if (flipShown.HasValue && flipShown.Value == on) return;
            flipShown = on;
            if (flipButton.image != null) flipButton.image.color = on ? flipOnColor : FlipOffColor;
            if (flipText != null) { flipText.text = on ? "Flip X: On" : "Flip X"; flipText.color = on ? flipOnTextColor : FlipOffTextColor; }
        }

        // texture picked with Browse while the item already had one: rebuild the model instead of only
        // swapping the image, so UVs that were laid out for an atlas or colours are redone for this texture
        public static void BrowseAssignTexture(Workshop w, string path, Texture2D texture)
        {
            // (the tool only gets here on the Objects tab, with an item open that already has a texture)
            if (texture != null) UnityEngine.Object.Destroy(texture);
            w.activeItem.textureFile = path;
            if (w.textureField != null) w.textureField.text = path;
            Rebuild(w);
        }

    }
}
