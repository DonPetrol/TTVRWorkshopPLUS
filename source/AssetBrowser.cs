// TTVR Workshop Plus - "Asset Browser": browse Tiny Town VR's own models and export them.
// A button in the top bar (left of Run Game) opens a page over the whole window: a searchable list of every model the
// game has (and those of the DLC packs that are installed), a turning 3D preview of the one chosen, and three buttons:
// Export (that model), Export All Shown (every model the search and filter leave in the list, into one folder) and
// Export All Models (every model, after an "are you sure"). The slow work runs in the background, with a loading
// screen the first time the page opens and a progress bar (with Cancel) while exporting several.
// A model is written as an OBJ file with its MTL, and its texture as a PNG. The game's textures are shared pictures
// (atlases) that many models use, so the PNG is named after the picture ("Shared_512_46.png") and written once per
// folder, however many models use it.
// Where the models come from, and how they are read, is in GameFiles.cs (the game itself) and below (the DLC packs,
// which are Unity asset bundles and are opened with Unity). Nothing of the game's is changed; the files are only read.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace TTVRPlus
{
    public static class AssetBrowser
    {
        const string DirPref = "WorkshopPlus.GameModels.Folder";
        static readonly string[] Stacks = { "Shared_256", "Shared_512", "Shared_1024", "Shared_Characters" };   // by materialIndex
        static Workshop ws;

        // ---------------------------------------------------------------- the top-bar button

        public static void Init(Workshop w)
        {
            ws = w;
            try
            {
                var root = w.listView.transform.parent;
                RectTransform run = null;
                foreach (var t in root.GetComponentsInChildren<RectTransform>(true)) if (t.name == "RunGameButton") run = t;
                if (run == null) { Plugin.Log.LogInfo("Run Game button not found, no Asset Browser button"); return; }
                var go = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, run.parent, false);
                go.name = "GameModelsButton"; go.SetActive(true);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = run.anchorMin; rt.anchorMax = run.anchorMax; rt.pivot = run.pivot;
                rt.sizeDelta = new Vector2(140f, run.sizeDelta.y);
                rt.anchoredPosition = new Vector2(run.anchoredPosition.x - run.sizeDelta.x - 8f, run.anchoredPosition.y);
                var b = go.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent(); b.interactable = true;
                b.onClick.AddListener(Open);
                var text = go.GetComponentInChildren<Text>();
                if (text != null) { text.text = "Asset Browser"; WorkshopPlus.FitButtonText(text); }
                Tooltip.Add(go, "Browse the game's own models and export them (OBJ, MTL and PNG).");
                WorkshopList.AlignWithSearch(rt);
                WorkshopList.MakeRoomForHeaderButton(rt);
            }
            catch (Exception e) { Plugin.Log.LogInfo("Asset Browser button: " + e.Message); }
        }

        // ---------------------------------------------------------------- work away from the main thread
        // Reading the game's files, unpacking a model and decoding or writing its texture take long enough to freeze the
        // window, so they run on a worker thread while the page goes on drawing; a coroutine waits for them. Only
        // Unity's own objects (the DLC packs' bundles, textures, the preview) are touched on the main thread.
        // (Ui.Run / Ui.Wait, in Ui.cs)
        static Ui.Job Run(Action work) { return Ui.Run(work); }
        static IEnumerator Wait(Ui.Job j) { return Ui.Wait(j); }

        // ---------------------------------------------------------------- the game's files

        static bool loaded, loading; static string loadError;
        static AssetFile resources, shared;
        static Dictionary<string, AssetFile.Blob> blobs;
        static readonly Dictionary<string, AssetFile.TextureArray> arrays = new Dictionary<string, AssetFile.TextureArray>();
        static readonly AssetBundle[] packs = new AssetBundle[GameModel.PackFiles.Length];
        static List<GameModel> all = new List<GameModel>();

        /// reads the list of models (once): the game's files on a worker thread, the DLC packs with Unity, in the background
        static IEnumerator Load()
        {
            if (loaded || loading) { while (loading) yield return null; yield break; }
            loading = true;
            string gameRoot = Directory.GetParent(Application.dataPath).FullName;           // ...\Tiny Town VR
            List<GameModel> models = null;
            var job = Run(delegate
            {
                string data = Path.Combine(gameRoot, "Tiny Town VR_Data");
                if (!Directory.Exists(data)) throw new Exception("the game's data folder wasn't found next to the tool (" + data + ")");
                resources = AssetFile.Open(Path.Combine(data, "resources.assets"));
                shared = AssetFile.Open(Path.Combine(data, "sharedassets0.assets"));
                blobs = resources.TextAssets();
                foreach (var a in shared.TextureArrays()) arrays[a.Name] = a;
                AssetFile.Blob list;
                if (!blobs.TryGetValue("MeshList", out list)) throw new Exception("the game's list of models wasn't found");
                models = GameModel.ParseList(System.Text.Encoding.UTF8.GetString(AssetFile.ReadBytes(resources.Path, list.Offset, list.Length)));
            });
            yield return Wait(job);
            if (job.Error != null) { loadError = job.Error.Message; Plugin.Log.LogInfo("Asset Browser: " + job.Error); loaded = true; loading = false; yield break; }
            // the DLC packs that are installed (each an asset bundle in the game folder)
            for (int i = 0; i < packs.Length; i++)
            {
                string f = Path.Combine(gameRoot, GameModel.PackFiles[i]);
                if (!File.Exists(f)) continue;
                var req = AssetBundle.LoadFromFileAsync(f);
                yield return req;
                packs[i] = req.assetBundle;
                if (packs[i] == null) Plugin.Log.LogInfo("Asset Browser: the " + GameModel.PackNames[i] + " couldn't be opened");
            }
            // only models whose data is really there (the game's list names a few it doesn't have). (Should a pack's
            // bundle not answer by name at all, its models are all listed, and any missing one says so when chosen.)
            var packHas = new int[packs.Length];
            foreach (var m in models) if (m.Pack >= 0 && packs[m.Pack] != null && packs[m.Pack].Contains(m.Name)) packHas[m.Pack]++;
            foreach (var m in models)
                if (m.Pack < 0 ? blobs.ContainsKey(m.Name) : packs[m.Pack] != null && (packHas[m.Pack] == 0 || packs[m.Pack].Contains(m.Name))) all.Add(m);
            all.Sort((a, b) => { int c = string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name); });
            Plugin.Log.LogInfo("Asset Browser: " + all.Count + " models");
            loaded = true; loading = false;
        }

        /// which picture a model uses: its stack (materialIndex) and place in it (vertex colour alpha x 256).
        /// The game numbers its materials in one list: its own four stacks (0-3), the Workshop's three (4-6), then
        /// each pack's four (7-10 fantasy, 11-14 military, 15-18 zombie) - so a pack model's stack is counted from its pack's first.
        static string PictureName(GameModel m, SerializableMeshData d, out int stack, out int layer)
        {
            stack = d.materialIndex - (m.Pack < 0 ? 0 : 7 + 4 * m.Pack);
            layer = d.colors != null && d.colors.Length >= 4 ? (int)Math.Round(d.colors[3] * 256f) : -1;
            if (stack < 0 || stack >= Stacks.Length || layer < 0) return null;
            return (m.Pack < 0 ? "" : GameModel.PackFiles[m.Pack] + "_") + Stacks[stack] + "_" + layer;
        }

        /// everything known about one model once fetched: its data, its picture's name and (if asked for) the picture
        class Fetched
        {
            public SerializableMeshData D; public string Picture, Why;
            public int Stack, Layer;
            public byte[] Rgba; public int W, H;            // the picture, rows bottom first
            public Exception Error;
        }

        /// a pack's TextAsset or Material, loaded in the background
        static IEnumerator FromPack<T>(int pack, string name, Action<T> got) where T : UnityEngine.Object
        {
            var req = packs[pack].LoadAssetAsync<T>(name);
            yield return req;
            got(req.asset as T);
        }

        /// fetches a model, and its picture as RGBA if `want` says so once the picture's name is known (it is asked on
        /// a worker thread for the game's own models), the slow parts on a worker thread
        static IEnumerator Fetch(GameModel m, Func<string, bool> want, Fetched r)
        {
            byte[] bytes = null;
            if (m.Pack >= 0)
            {
                TextAsset ta = null;
                yield return FromPack<TextAsset>(m.Pack, m.Name, t => ta = t);
                if (ta == null) { r.Error = new Exception("its data wasn't found in the " + GameModel.PackNames[m.Pack]); yield break; }
                bytes = ta.bytes;
            }
            var job = Run(delegate
            {
                if (bytes == null) { var b = blobs[m.Name]; bytes = AssetFile.ReadBytes(resources.Path, b.Offset, b.Length); }
                using (var s = new MemoryStream(bytes)) r.D = (SerializableMeshData)new BinaryFormatter().Deserialize(s);
                r.Picture = PictureName(m, r.D, out r.Stack, out r.Layer);
                if (r.Picture == null) { r.Why = "the model doesn't name one (material " + r.D.materialIndex + ")"; return; }
                if (m.Pack >= 0) return;                         // (a pack's picture is copied out on the main thread, below)
                if (!want(r.Picture)) return;
                AssetFile.TextureArray a;
                if (!arrays.TryGetValue(Stacks[r.Stack], out a) || r.Layer >= a.Depth || !Dxt.Supported(a.Format)) { r.Why = r.Picture + " wasn't found"; return; }
                r.W = a.Width; r.H = a.Height;
                r.Rgba = Dxt.Decode(AssetFile.ReadBytes(shared.Path, a.LayerOffset(r.Layer), a.LayerBytes), 0, a.Width, a.Height, a.Format);
            });
            yield return Wait(job);
            if (job.Error != null) { r.Error = job.Error; yield break; }
            if (m.Pack < 0 || r.Picture == null || !want(r.Picture)) yield break;

            // a pack's stack: the material of that name holds it (as the game loads it); one picture is copied out
            // of it here, and decoded on a worker thread
            Material mat = null;
            yield return FromPack<Material>(m.Pack, Stacks[r.Stack] + ".mat", t => mat = t);
            var arr = mat != null ? mat.mainTexture as Texture2DArray : null;
            if (arr == null || r.Layer >= arr.depth) { r.Why = r.Picture + " wasn't found"; yield break; }
            r.W = arr.width; r.H = arr.height;
            var one = new Texture2D(arr.width, arr.height, arr.format, false);
            byte[] raw = null;
            try
            {
                Graphics.CopyTexture(arr, r.Layer, 0, one, 0, 0);        // (both readable: the CPU copy is copied too)
                raw = one.GetRawTextureData();
                if (!Dxt.Supported((int)arr.format) || AllZero(raw))
                {
                    // otherwise through the graphics card: drawn into a picture and read back
                    raw = null;
                    var rt = RenderTexture.GetTemporary(arr.width, arr.height, 0, RenderTextureFormat.ARGB32);
                    var was = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(one, rt);
                        RenderTexture.active = rt;
                        var t = new Texture2D(arr.width, arr.height, TextureFormat.RGBA32, false);
                        t.ReadPixels(new Rect(0, 0, arr.width, arr.height), 0, 0); t.Apply(false);
                        r.Rgba = t.GetRawTextureData();
                        UnityEngine.Object.Destroy(t);
                    }
                    finally { RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt); }
                }
            }
            catch (Exception e) { r.Why = e.Message; }
            finally { UnityEngine.Object.Destroy(one); }
            if (raw != null)
            {
                int fmt = (int)arr.format;
                var decode = Run(delegate { r.Rgba = Dxt.Decode(raw, 0, r.W, r.H, fmt); });
                yield return Wait(decode);
                if (decode.Error != null) r.Why = decode.Error.Message;
            }
        }
        static bool AllZero(byte[] b) { for (int i = 0; i < b.Length; i += 7) if (b[i] != 0) return false; return true; }

        /// the picture as a texture for the preview (made on the main thread: Unity's objects can only be made there)
        static Texture2D FromRgba(byte[] rgba, int w, int h)
        {
            var t = Pixels.Texture(rgba, w, h, true);
            t.wrapMode = TextureWrapMode.Repeat;             // (some models' texture positions go past the picture's edge, and repeat)
            return t;
        }

        // ---------------------------------------------------------------- exporting

        /// writes the model's OBJ and MTL at objPath, and its picture next to them unless already written there
        /// (`written`: the pictures written so far into that folder). The writing happens on a worker thread.
        static IEnumerator Export(GameModel m, string objPath, HashSet<string> written, Fetched r)
        {
            string dir = Path.GetDirectoryName(objPath);
            // the picture is only decoded if it hasn't been written into that folder yet (many models share one).
            // (`written` is only changed between models, never while a worker thread is asking it)
            var f = new Fetched();
            yield return Fetch(m, name => written == null || !written.Contains(Path.Combine(dir, name + ".png")), f);
            if (f.Error != null) { r.Error = f.Error; yield break; }
            string png = f.Picture != null ? f.Picture + ".png" : null;
            string pngPath = png != null ? Path.Combine(dir, png) : null;
            bool need = f.Rgba != null;
            if (png != null && !need && (written == null || !written.Contains(pngPath))) png = null;     // (no picture after all)
            var job = Run(delegate
            {
                if (need) File.WriteAllBytes(pngPath, Png.Encode(f.Rgba, f.W, f.H));
                ObjWriter.Write(objPath, m.Display, f.D.vertices, f.D.normals, f.D.uvs, f.D.triangles, png);
            });
            yield return Wait(job);
            if (job.Error != null) { r.Error = job.Error; yield break; }
            if (need && written != null) written.Add(pngPath);
        }

        static string LastFolder()
        {
            string d = PlayerPrefs.GetString(DirPref, "");
            return Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        static void RememberFolder(string dir) { PlayerPrefs.SetString(DirPref, dir); PlayerPrefs.Save(); }

        static void ExportChosen()
        {
            if (busy) return;
            if (chosen == null) { Status("Choose a model in the list first."); return; }
            string path;
            try { path = SFB.StandaloneFileBrowser.SaveFilePanel("Export " + chosen.Display, LastFolder(), chosen.FileName, "obj"); }
            catch (Exception e) { Status("Couldn't open the save dialog: " + e.Message); return; }
            if (string.IsNullOrEmpty(path)) return;
            RememberFolder(Path.GetDirectoryName(path));
            ws.StartCoroutine(ExportOne(chosen, path));
        }

        static IEnumerator ExportOne(GameModel m, string path)
        {
            busy = true; SetButtons();
            ShowPopup("Exporting " + m.Display + "...", "", -1f, null, null, null, null);
            var r = new Fetched();
            yield return Export(m, path, null, r);
            HidePopup();
            busy = false; SetButtons();
            if (r.Error != null) { Status("Couldn't export " + m.Display + ": " + r.Error.Message); Plugin.Log.LogInfo("export: " + r.Error); }
            else Status("Exported " + m.Display + " to " + Path.GetDirectoryName(path) + " (" + Path.GetFileName(path) + ", its MTL and its texture).");
        }

        static bool busy, cancelled;
        static void ExportShown() { ExportMany(shown, "the " + shown.Count.ToString("N0") + " models shown"); }

        /// Export All Models: every model, whatever the search and filter show; asks first (it is a lot of files)
        static void ExportEverything()
        {
            if (busy || all.Count == 0) return;
            ShowPopup("Export all " + all.Count.ToString("N0") + " models?",
                      "Every model in the game and your DLC packs is saved into one folder you choose: an OBJ and MTL file for each, " +
                      "and one PNG for each texture they use. This can take several minutes.",
                      -1f, "Export All", delegate { HidePopup(); ExportMany(all, "all " + all.Count.ToString("N0") + " models"); }, "Cancel", HidePopup);
        }

        static void ExportMany(List<GameModel> list, string what)
        {
            if (busy) return;
            if (list.Count == 0) { Status("Nothing is shown to export."); return; }
            string[] dirs;
            try { dirs = SFB.StandaloneFileBrowser.OpenFolderPanel("Export " + what + " into a folder", LastFolder(), false); }
            catch (Exception e) { Status("Couldn't open the folder dialog: " + e.Message); return; }
            if (dirs == null || dirs.Length == 0 || string.IsNullOrEmpty(dirs[0])) return;
            RememberFolder(dirs[0]);
            ws.StartCoroutine(ExportAll(new List<GameModel>(list), dirs[0]));
        }

        static IEnumerator ExportAll(List<GameModel> list, string dir)
        {
            busy = true; cancelled = false; SetButtons();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int done = 0, failed = 0; string firstError = null;
            foreach (var m in list)
            {
                if (cancelled) break;
                ShowPopup("Exporting models...", (done + 1).ToString("N0") + " of " + list.Count.ToString("N0") + ": " + m.Display, (float)done / list.Count,
                          null, null, "Cancel", delegate { cancelled = true; });
                var r = new Fetched();
                yield return Export(m, Path.Combine(dir, m.FileName + ".obj"), written, r);
                if (r.Error != null) { failed++; if (firstError == null) firstError = m.Display + ": " + r.Error.Message; }
                done++;
            }
            HidePopup();
            busy = false; SetButtons();
            Status((cancelled ? "Stopped. " : "") + "Exported " + (done - failed).ToString("N0") + " models and " + written.Count.ToString("N0") + " textures to " + dir + "." +
                   (failed > 0 ? " " + failed + " couldn't be exported (" + firstError + ")." : ""));
        }

        // ---------------------------------------------------------------- the pop-up (loading, progress, "are you sure")
        // One box over the page, in front of everything on it (so nothing else can be clicked while it shows): a title,
        // a line of text, a progress bar (when there is progress to show) and up to two buttons.
        static GameObject popup; static Text popupTitle, popupText; static GameObject popupBar; static RectTransform popupFill;
        static Button popupA, popupB; static Action popupDoA, popupDoB;

        static void BuildPopup(RectTransform prt)
        {
            popup = new GameObject("Popup", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)popup.transform; rt.SetParent(prt, false); Stretch(rt, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            popup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var brt = (RectTransform)box.transform; brt.SetParent(rt, false);
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f); brt.sizeDelta = new Vector2(620f, 250f);
            box.GetComponent<Image>().color = Color.white;
            popupTitle = MakeText(brt, "Title", "", 24, TextAnchor.UpperLeft, new Color(0.15f, 0.15f, 0.17f));
            popupTitle.fontStyle = FontStyle.Bold;
            Stretch(popupTitle.rectTransform, 0f, 1f, 1f, 1f, 24f, -58f, -24f, -18f);
            popupText = MakeText(brt, "Text", "", 17, TextAnchor.UpperLeft, new Color(0.3f, 0.3f, 0.33f));
            Stretch(popupText.rectTransform, 0f, 0f, 1f, 1f, 24f, 104f, -24f, -64f);
            popupBar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            var bar = (RectTransform)popupBar.transform; bar.SetParent(brt, false); Stretch(bar, 0f, 0f, 1f, 0f, 24f, 80f, -24f, 98f);
            popupBar.GetComponent<Image>().color = new Color(0.88f, 0.88f, 0.9f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            popupFill = (RectTransform)fill.transform; popupFill.SetParent(bar, false); Stretch(popupFill, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f);
            fill.GetComponent<Image>().color = new Color(0.3f, 0.72f, 0.66f);
            popupA = MakeButton(brt, "OK", null, delegate { if (popupDoA != null) popupDoA(); }, 0.5f, 0f, new Vector2(-8f, 64f), new Vector2(180f, 42f));
            popupB = MakeButton(brt, "Cancel", null, delegate { if (popupDoB != null) popupDoB(); }, 0.5f, 0f, new Vector2(8f, 64f), new Vector2(180f, 42f), true);
            popup.SetActive(false);
        }

        /// progress: 0..1, or below 0 for none. A button with no label is left out (one button alone sits in the middle).
        static void ShowPopup(string title, string text, float progress, string a, Action doA, string b, Action doB)
        {
            if (popup == null) return;
            popupTitle.text = title; popupText.text = text;
            popupBar.SetActive(progress >= 0f);
            if (progress >= 0f) popupFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            popupDoA = doA; popupDoB = doB;
            Label(popupA, a); Label(popupB, b);
            // one button alone: in the middle
            var art = (RectTransform)popupA.transform; var brt = (RectTransform)popupB.transform;
            art.anchoredPosition = new Vector2(b == null ? 90f : -8f, 64f);
            brt.anchoredPosition = new Vector2(a == null ? -90f : 8f, 64f);
            popup.SetActive(true);
            popup.transform.SetAsLastSibling();
        }
        static void Label(Button b, string label)
        {
            b.gameObject.SetActive(label != null);
            if (label == null) return;
            var t = b.GetComponentInChildren<Text>(); if (t != null && t.text != label) t.text = label;
        }
        static void HidePopup() { if (popup != null) popup.SetActive(false); popupDoA = popupDoB = null; }
        static bool PopupShown { get { return popup != null && popup.activeSelf; } }

        // ---------------------------------------------------------------- the page

        const float RowHeight = 34f;
        static GameObject panel;
        static InputField search; static Dropdown filter; static Text countText, info, status;
        static ScrollRect scroll; static RectTransform content;
        static readonly List<Row> rows = new List<Row>();
        static List<GameModel> shown = new List<GameModel>();
        static readonly List<string> filters = new List<string>();     // the filter list's entries: "", a category, or "pack:N"
        static GameModel chosen;
        static Button exportOne, exportAll, exportEverything;
        class Row { public RectTransform Rect; public Image Back; public Text Name, Where; public int Index = -1; }

        static void Open()
        {
            try
            {
                if (panel == null) Build();
                panel.SetActive(true);
                panel.transform.SetAsLastSibling();
                Preview.Show(true);
                ws.StartCoroutine(OpenSoon());
            }
            catch (Exception e) { Plugin.Log.LogInfo("Asset Browser: " + e); WorkshopPlus.SetMessage(ws, "Couldn't open Asset Browser: " + e.Message); }
        }

        // the first time: a loading screen while the list of models is read (in the background)
        static IEnumerator OpenSoon()
        {
            if (!loaded) ShowPopup("Loading the game's models...", "Reading the game's files and DLC packs. This only happens the first time.", -1f, null, null, null, null);
            yield return Load();
            if (!busy) HidePopup();
            if (loadError != null) { Status("The game's models couldn't be read: " + loadError); yield break; }
            if (filters.Count == 0) FillFilters();
            Canvas.ForceUpdateCanvases();           // (so the list knows how tall it is)
            Refresh(false);
        }

        static void Close()
        {
            if (panel != null) panel.SetActive(false);
            Preview.Show(false);
        }

        static void Build()
        {
            var root = ws.listView.transform.parent;
            panel = new GameObject("WorkshopPlusGameModels", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(root, false); Stretch(prt, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            panel.GetComponent<Image>().color = new Color(0.94f, 0.94f, 0.95f, 1f);    // (covers, and catches clicks meant for, the list behind)
            panel.AddComponent<Driver>();

            var title = MakeText(prt, "Title", "Asset Browser", 30, TextAnchor.MiddleLeft, new Color(0.15f, 0.15f, 0.17f));
            title.fontStyle = FontStyle.Bold;
            Stretch(title.rectTransform, 0f, 1f, 0.6f, 1f, 24f, -64f, 0f, -12f);
            MakeButton(prt, "Close", null, Close, 1f, 1f, new Vector2(-24f, -14f), new Vector2(120f, 38f));
            // (in the top corner, beside Close: it doesn't depend on the search or on the model chosen)
            exportEverything = MakeButton(prt, "Export All Models...", "Export every model in the game (and your DLC packs) into one folder, whatever the search shows.", ExportEverything, 1f, 1f, new Vector2(-156f, -14f), new Vector2(230f, 38f));

            // search and filter
            var sGo = (GameObject)UnityEngine.Object.Instantiate(ws.titleField.gameObject, prt, false);
            sGo.name = "GameModelsSearch"; sGo.SetActive(true);
            var srt = (RectTransform)sGo.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f); srt.sizeDelta = new Vector2(380f, 38f); srt.anchoredPosition = new Vector2(24f, -76f);
            search = sGo.GetComponent<InputField>();
            search.onValueChanged = new InputField.OnChangeEvent(); search.onEndEdit = new InputField.SubmitEvent();
            search.lineType = InputField.LineType.SingleLine; search.characterLimit = 0; search.text = "";
            if (search.placeholder is Text) ((Text)search.placeholder).text = "Search names, categories, packs";
            search.onValueChanged.AddListener(delegate { Refresh(true); });
            Tooltip.Add(sGo, "Every word must appear in the model's name, file name, category, group or pack.");

            var fGo = (GameObject)UnityEngine.Object.Instantiate(ws.visibilityDropdown.gameObject, prt, false);
            fGo.name = "GameModelsFilter"; fGo.SetActive(true);
            var frt = (RectTransform)fGo.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0f, 1f); frt.sizeDelta = new Vector2(220f, 38f); frt.anchoredPosition = new Vector2(414f, -76f);
            filter = fGo.GetComponent<Dropdown>();
            filter.onValueChanged = new Dropdown.DropdownEvent();
            filter.onValueChanged.AddListener(delegate { Refresh(true); });

            countText = MakeText(prt, "Count", "", 18, TextAnchor.MiddleLeft, new Color(0.35f, 0.35f, 0.38f));
            Stretch(countText.rectTransform, 0f, 1f, 0f, 1f, 646f, -114f, 900f, -76f);

            BuildList(prt);

            // the preview, with what it is under it
            var pgo = new GameObject("GameModelsPreview", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            var pvr = (RectTransform)pgo.transform; pvr.SetParent(prt, false);
            var holder = new GameObject("PreviewArea", typeof(RectTransform));
            var hrt = (RectTransform)holder.transform; hrt.SetParent(prt, false);
            Stretch(hrt, 0.48f, 0.34f, 1f, 1f, 0f, 0f, -24f, -126f);
            pvr.SetParent(hrt, false); Stretch(pvr, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            var fit = pgo.GetComponent<AspectRatioFitter>(); fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = 1f;
            pgo.GetComponent<RawImage>().texture = Preview.Target;
            pgo.AddComponent<Turn>();
            Tooltip.Add(pgo, "Drag to turn the model.");

            info = MakeText(prt, "Info", "Choose a model in the list.", 18, TextAnchor.UpperLeft, new Color(0.2f, 0.2f, 0.22f));
            Stretch(info.rectTransform, 0.48f, 0.2f, 1f, 0.34f, 0f, 0f, -24f, -8f);

            exportOne = MakeButton(prt, "Export...", "Save the chosen model as an OBJ file, with its MTL and its texture (PNG) next to it.", ExportChosen, 0.48f, 0.2f, new Vector2(0f, -4f), new Vector2(170f, 40f), true);
            exportAll = MakeButton(prt, "Export All Shown...", "Export every model the list shows (after search and filter) into one folder.", ExportShown, 0.48f, 0.2f, new Vector2(182f, -4f), new Vector2(220f, 40f), true);


            status = MakeText(prt, "Status", "", 17, TextAnchor.UpperLeft, new Color(0.1f, 0.35f, 0.6f));
            Stretch(status.rectTransform, 0.48f, 0f, 1f, 0.2f, 0f, 64f, -24f, -52f);

            BuildPopup(prt);                        // (last: in front of everything)

        }

        // the list: a scrolling column of rows; only as many rows are made as fit, and they are moved and refilled as
        // it scrolls (there are some 2,700 models)
        static void BuildList(RectTransform prt)
        {
            var sgo = new GameObject("GameModelsList", typeof(RectTransform));
            var srt = (RectTransform)sgo.transform; srt.SetParent(prt, false);
            Stretch(srt, 0f, 0f, 0.46f, 1f, 24f, 24f, 0f, -126f);
            RectTransform vrt;
            scroll = Ui.Scroller(srt, Color.white, RowHeight, true, out vrt);
            var cgo = new GameObject("Content", typeof(RectTransform));
            content = (RectTransform)cgo.transform; content.SetParent(vrt, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f); content.anchoredPosition = Vector2.zero;
            scroll.content = content;
            scroll.onValueChanged.AddListener(delegate { Fill(); });
        }

        static Row MakeRow()
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
            var r = new Row { Rect = (RectTransform)go.transform, Back = go.GetComponent<Image>() };
            r.Rect.SetParent(content, false);
            r.Rect.anchorMin = new Vector2(0f, 1f); r.Rect.anchorMax = new Vector2(1f, 1f); r.Rect.pivot = new Vector2(0.5f, 1f);
            r.Rect.sizeDelta = new Vector2(0f, RowHeight);
            var b = go.GetComponent<Button>(); b.transition = Selectable.Transition.None;
            b.onClick.AddListener(delegate { if (r.Index >= 0 && r.Index < shown.Count) Choose(shown[r.Index]); });
            r.Name = MakeText(r.Rect, "Name", "", 17, TextAnchor.MiddleLeft, new Color(0.12f, 0.12f, 0.14f));
            Stretch(r.Name.rectTransform, 0f, 0f, 0.58f, 1f, 10f, 0f, 0f, 0f);
            r.Where = MakeText(r.Rect, "Where", "", 14, TextAnchor.MiddleRight, new Color(0.45f, 0.45f, 0.5f));
            Stretch(r.Where.rectTransform, 0.58f, 0f, 1f, 1f, 0f, 0f, -10f, 0f);
            rows.Add(r);
            return r;
        }

        static void FillFilters()
        {
            filters.Clear();
            var labels = new List<string>();
            filters.Add(""); labels.Add("All models");
            var seen = new HashSet<string>();
            foreach (var m in all) if (seen.Add(m.Category)) { filters.Add(m.Category); labels.Add(m.Category); }
            for (int i = 0; i < packs.Length; i++)
                if (packs[i] != null) { filters.Add("pack:" + i); labels.Add(GameModel.PackNames[i]); }
            filter.ClearOptions(); filter.AddOptions(labels); filter.value = 0; filter.RefreshShownValue();
        }

        static void Refresh(bool toTop)
        {
            string f = filter != null && filter.value < filters.Count ? filters[filter.value] : "";
            var words = (search != null ? search.text : "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            shown = new List<GameModel>();
            foreach (var m in all)
            {
                if (f.StartsWith("pack:") ? "pack:" + m.Pack != f : f.Length > 0 && m.Category != f) continue;
                bool ok = true;
                foreach (var w in words)
                    if (!Has(m.Display, w) && !Has(m.Name, w) && !Has(m.Category, w) && !Has(m.Filter, w) && !Has(m.PackName, w)) { ok = false; break; }
                if (ok) shown.Add(m);
            }
            content.sizeDelta = new Vector2(0f, shown.Count * RowHeight);
            if (toTop) content.anchoredPosition = Vector2.zero;
            countText.text = shown.Count == all.Count ? all.Count + " models" : shown.Count + " of " + all.Count + " models";
            SetButtons();
            Fill();
        }
        static bool Has(string s, string w) { return s != null && s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0; }

        /// puts the rows where the list is scrolled to, and fills them in
        static void Fill()
        {
            var view = (RectTransform)content.parent;
            int need = Mathf.CeilToInt(Mathf.Max(view.rect.height, 300f) / RowHeight) + 2;
            while (rows.Count < need) MakeRow();
            int first = Mathf.Max(0, Mathf.FloorToInt(content.anchoredPosition.y / RowHeight));
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; int index = first + i;
                bool on = index < shown.Count;
                r.Rect.gameObject.SetActive(on);
                if (!on) { r.Index = -1; continue; }
                r.Rect.anchoredPosition = new Vector2(0f, -index * RowHeight);
                var m = shown[index];
                r.Index = index;
                r.Name.text = m.Display;
                r.Where.text = m.Category + (m.Filter != null && m.Filter != m.Category && m.Filter.Length > 0 ? " · " + m.Filter : "") + (m.Pack >= 0 ? " · " + GameModel.PackNames[m.Pack] : "");
                r.Back.color = m == chosen ? new Color(0.78f, 0.88f, 0.98f) : index % 2 == 0 ? Color.white : new Color(0.965f, 0.965f, 0.975f);
            }
        }

        static void Choose(GameModel m)
        {
            chosen = m;
            Fill(); SetButtons();
            ws.StartCoroutine(Show(m, ++choosing));
        }
        static int choosing;            // (a model clicked while another is still loading: only the last one is shown)

        static IEnumerator Show(GameModel m, int ticket)
        {
            info.text = "<b>" + m.Display + "</b>   (" + m.Name + ")\nLoading...";
            // the picture is decoded only if the preview hasn't got it already (the names it has are copied for the
            // worker thread: Unity's own objects mustn't be touched there)
            var have = Preview.Names();
            var f = new Fetched();
            yield return Fetch(m, name => !have.Contains(name), f);
            if (ticket != choosing) yield break;
            Texture2D tex = f.Picture != null ? Preview.Lookup(f.Picture) : null;
            if (tex == null && f.Error == null && f.Rgba != null) { tex = FromRgba(f.Rgba, f.W, f.H); Preview.Keep(f.Picture, tex); }
            if (f.Error != null) { info.text = m.Display + ": couldn't be read (" + f.Error.Message + ")"; Plugin.Log.LogInfo(m.Name + ": " + f.Error); yield break; }
            try
            {
                var d = f.D; string picture = f.Picture;
                string why = tex == null ? (f.Why ?? (picture + " wasn't found")) : null;
                if (why != null) Plugin.Log.LogInfo("Asset Browser: no texture for " + m.Name + ": " + why);
                var size = Preview.Set(d, tex);
                int verts = d.vertices.Length / 3, tris = d.triangles.Length / 3;
                info.text = "<b>" + m.Display + "</b>   (" + m.Name + ")\n" +
                            m.Category + (string.IsNullOrEmpty(m.Filter) ? "" : " · " + m.Filter) + " · " + m.PackName + "\n" +
                            verts.ToString("N0") + " vertices, " + tris.ToString("N0") + " triangles · " +
                            size.x.ToString("0.###") + " x " + size.y.ToString("0.###") + " x " + size.z.ToString("0.###") + " (game units)\n" +
                            (tex != null ? "Texture: " + picture + ".png (" + tex.width + "px; a picture other models share too)" : "No texture: " + why + ".");
            }
            catch (Exception e) { info.text = m.Display + ": couldn't be read (" + e.Message + ")"; Plugin.Log.LogInfo("" + m.Name + ": " + e); }
        }

        /// arrow keys move through the list (when the search box isn't being typed in)
        static void Step(int by)
        {
            if (shown.Count == 0) return;
            int i = chosen == null ? -1 : shown.IndexOf(chosen);
            i = Mathf.Clamp(i + by, 0, shown.Count - 1);
            Choose(shown[i]);
            var view = (RectTransform)content.parent;
            float top = i * RowHeight, y = content.anchoredPosition.y;
            if (top < y) y = top;
            else if (top + RowHeight > y + view.rect.height) y = top + RowHeight - view.rect.height;
            content.anchoredPosition = new Vector2(0f, y);
            Fill();
        }

        static void SetButtons()
        {
            if (exportOne != null) exportOne.interactable = !busy && chosen != null;
            if (exportAll != null) exportAll.interactable = !busy && shown.Count > 0;
            if (exportEverything != null) exportEverything.interactable = !busy && all.Count > 0;
        }

        static void Status(string s) { if (status != null) status.text = s; }

        // ---- small UI helpers

        // (the shared building blocks are in Ui.cs)
        static void Stretch(RectTransform r, float x0, float y0, float x1, float y1, float l, float b, float rt, float t) { Ui.Stretch(r, x0, y0, x1, y1, l, b, rt, t); }
        static Text MakeText(Transform parent, string name, string words, int size, TextAnchor anchor, Color color) { return Ui.Text(ws, parent, name, words, size, anchor, color); }
        static Button MakeButton(Transform parent, string label, string tip, Action press, float ax, float ay, Vector2 pos, Vector2 size, bool topLeftPivot = false)
        {
            return Ui.Button(ws, parent, label, tip, press, new Vector2(ax, ay), topLeftPivot ? new Vector2(0f, 1f) : new Vector2(1f, 1f), pos, size);
        }

        /// the page's keys: Esc closes it, the arrow keys move through the list
        public class Driver : MonoBehaviour
        {
            void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    // with the pop-up up: Esc is its Cancel (if it has one); the page itself only closes when nothing is going on
                    if (PopupShown) { if (popupDoB != null) popupDoB(); return; }
                    if (!busy && !loading) Close();
                    return;
                }
                if (PopupShown) return;
                if (search != null && search.isFocused) return;
                if (Input.GetKeyDown(KeyCode.DownArrow)) Step(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow)) Step(-1);
                else if (Input.GetKeyDown(KeyCode.PageDown)) Step(10);
                else if (Input.GetKeyDown(KeyCode.PageUp)) Step(-10);
            }
        }

        /// dragging on the preview turns the model
        public class Turn : MonoBehaviour, IDragHandler
        {
            public void OnDrag(PointerEventData e) { Preview.Drag(e.delta); }
        }

        // ---------------------------------------------------------------- the 3D preview
        // A camera of its own sees only one layer (a layer no one else uses, which the tool's own cameras are told to
        // leave out) and draws into a picture shown on the page. The model turns slowly; dragging turns it by hand.
        // The model is shown close to the middle of the scene and scaled up to about one unit across: game models are
        // tiny (a rifle is 0.04 units long), and far from the middle the positions of their corners can only be stored
        // roughly, which bends and tears them.
        static class Preview
        {
            static readonly Vector3 Place = new Vector3(0f, -100f, 0f);
            static RenderTexture target; static Camera cam; static Light light;
            static Transform pivot; static MeshFilter filter; static MeshRenderer shape; static Material material; static Mesh mesh;
            static float pitch = 18f, spinPause; static bool ownLight;
            static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
            static readonly List<string> order = new List<string>();

            public static RenderTexture Target
            {
                get
                {
                    if (target == null)
                    {
                        // the same kind of picture the tool's own preview draws into (KeepPreviewSharp in Glue.cs)
                        target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                        target.antiAliasing = 4;
                        target.filterMode = FilterMode.Bilinear;
                        target.Create(); Make();
                    }
                    return target;
                }
            }

            static void Make()
            {
                int layer = Ui.FreeLayer(31);
                var cgo = new GameObject("WorkshopPlusGameModelsCamera");
                cam = cgo.AddComponent<Camera>();
                if (ws.rttCamera != null) cam.CopyFrom(ws.rttCamera);          // the edit page's camera settings (rendering, effects...)
                cam.targetTexture = target; cam.cullingMask = 1 << layer; cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.80f, 0.83f, 0.87f); cam.fieldOfView = 30f; cam.depth = -50f; cam.enabled = false;
                cam.orthographic = false;
                // the tool's other cameras leave this layer out (they would otherwise see the model too)
                foreach (var other in Resources.FindObjectsOfTypeAll<Camera>()) if (other != cam) other.cullingMask &= ~(1 << layer);
                // lit by the scene's own lights, as the edit page's model is; a light of its own only if none reaches the layer
                bool lit = false;
                foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>()) if (l.enabled && (l.cullingMask & (1 << layer)) != 0) lit = true;
                var lgo = new GameObject("WorkshopPlusGameModelsLight");
                light = lgo.AddComponent<Light>();
                light.type = LightType.Directional; light.cullingMask = 1 << layer; light.intensity = 0.9f; light.enabled = false;
                lgo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                ownLight = !lit;
                pivot = new GameObject("WorkshopPlusGameModel").transform;
                pivot.position = Place; pivot.gameObject.layer = layer;
                pivot.gameObject.AddComponent<Spin>();
                var m = new GameObject("Model");
                m.layer = layer; m.transform.SetParent(pivot, false);
                filter = m.AddComponent<MeshFilter>(); shape = m.AddComponent<MeshRenderer>();
                material = new Material(ws.defaultMaterial);                  // the edit page's material
                Plugin.Log.LogInfo("Asset Browser: preview layer " + layer + ", shader " + material.shader.name + (ownLight ? ", own light" : ", the scene's lights"));
                shape.sharedMaterial = material;
                mesh = new Mesh(); mesh.MarkDynamic(); filter.sharedMesh = mesh;
                pivot.gameObject.SetActive(false);
            }

            public static void Show(bool on)
            {
                if (target == null) return;
                cam.enabled = on; light.enabled = on && ownLight;
                pivot.gameObject.SetActive(on && mesh.vertexCount > 0);
            }

            /// the names of the pictures kept (a copy)
            public static HashSet<string> Names() { return new HashSet<string>(cache.Keys); }

            /// a picture made for an earlier model, if it is still kept (the last 8 are)
            public static Texture2D Lookup(string name)
            {
                Texture2D t;
                return cache.TryGetValue(name, out t) && t != null ? t : null;
            }

            /// keeps a picture for the next models that use it
            public static void Keep(string name, Texture2D t)
            {
                if (t == null) return;
                if (cache.ContainsKey(name)) order.Remove(name);
                cache[name] = t; order.Add(name);
                while (order.Count > 8)
                {
                    Texture2D old;
                    if (cache.TryGetValue(order[0], out old) && old != null && old != material.mainTexture) UnityEngine.Object.Destroy(old);
                    cache.Remove(order[0]); order.RemoveAt(0);
                }
            }

            /// shows this model; gives back its size
            public static Vector3 Set(SerializableMeshData d, Texture2D tex)
            {
                if (target == null) { var unused = Target; }
                int n = d.vertices.Length / 3;
                var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
                bool hasN = d.normals != null && d.normals.Length >= n * 3, hasUv = d.uvs != null && d.uvs.Length >= n * 2;
                for (int i = 0; i < n; i++)
                {
                    v[i] = new Vector3(d.vertices[i * 3], d.vertices[i * 3 + 1], d.vertices[i * 3 + 2]);
                    if (hasN) nr[i] = new Vector3(d.normals[i * 3], d.normals[i * 3 + 1], d.normals[i * 3 + 2]);
                    if (hasUv) uv[i] = new Vector2(d.uvs[i * 2], d.uvs[i * 2 + 1]);
                }
                mesh.Clear();
                mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.vertices = v; mesh.uv = uv;
                mesh.triangles = d.triangles;
                if (hasN) mesh.normals = nr; else mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                material.mainTexture = tex != null ? (Texture)tex : ws.defaultTexture;

                var b = mesh.bounds;
                filter.transform.localPosition = -b.center;
                // scaled to about one unit across (see above)
                float r0 = Mathf.Max(b.extents.magnitude, 1e-6f);
                pivot.localScale = Vector3.one / r0;
                pivot.rotation = Quaternion.Euler(0f, 200f, 0f);
                pitch = 18f;
                float dist = 1f / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
                cam.nearClipPlane = dist * 0.05f; cam.farClipPlane = dist * 3f;
                Aim(dist);
                pivot.gameObject.SetActive(cam.enabled);
                return b.size;
            }

            static void Aim(float dist)
            {
                cam.transform.position = Place + Quaternion.Euler(pitch, 0f, 0f) * (Vector3.back * dist);
                cam.transform.LookAt(Place);
            }

            public static void Drag(Vector2 delta)
            {
                if (pivot == null) return;
                spinPause = 2f;
                pivot.Rotate(0f, -delta.x * 0.4f, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - delta.y * 0.3f, -80f, 80f);
                Aim(Vector3.Distance(cam.transform.position, Place));
            }

            public class Spin : MonoBehaviour
            {
                void Update()
                {
                    if (spinPause > 0f) { spinPause -= Time.deltaTime; return; }
                    transform.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);
                }
            }
        }
    }
}
