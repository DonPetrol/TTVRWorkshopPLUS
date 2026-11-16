// TTVR Workshop Plus - choosing a world to publish from a list of pictures, instead of a file dialog.
// On the Worlds tab, the green + opens a page over the window with every world saved in the game (the game's save
// folder, %USERPROFILE%\AppData\LocalLow\Lumbernauts\TinyTown\Internal\Worlds): each one's picture, name and when it
// was last saved, newest first, with a search box. Clicking one adds it, exactly as choosing its "data" file in the
// tool's own dialog does. "Select File..." still opens that dialog (the tool's own way, unchanged), and Cancel or Esc
// closes the page.
// A world's picture is its thumbnail.jpg if it has one (a picture chosen in the game with the Tiny Town Enhanced
// mod), otherwise the game's own preview.jpg; its name comes from name.txt (also the mod's) or else its folder.
// Worlds already in your list say so, and the game's downloaded copies of Workshop worlds are marked as such.
// The Items tab's + is not changed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TTVRPlus
{
    public static class WorldPicker
    {
        static Workshop ws;
        static GameObject panel;
        static InputField search; static Text heading, empty;
        static RectTransform grid; static ScrollRect scroll;
        static readonly List<Tile> tiles = new List<Tile>();
        static Coroutine loading;

        class Tile
        {
            public string Folder, Data, Name, Picture; public DateTime Saved; public bool Added, Workshop;
            public GameObject Go; public RawImage Image; public Texture2D Texture;
        }

        /// true while the tool's own + is being run for "Select File..." (so it isn't sent back here)
        public static bool Bypass;

        /// the green + on the Worlds tab: shows the page (false: let the tool's own + run)
        public static bool OnNew(Workshop w)
        {
            if (Bypass || w.objectTabActive) return false;
            ws = w;
            try { Open(); return true; }
            catch (Exception e) { Plugin.Log.LogWarning("World picker: " + e); return false; }       // (the tool's dialog, then)
        }

        static void Open()
        {
            if (panel == null) Build();
            Clear();
            if (search != null) search.text = "";
            string dir = SaveFolder();
            var found = new List<Tile>();
            if (Directory.Exists(dir))
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string data = Path.Combine(d, "data"), name = Path.GetFileName(d);
                    if (!File.Exists(data) || name.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var t = new Tile { Folder = d, Data = data, Saved = File.GetLastWriteTime(data), Workshop = name.StartsWith("wi_world_") };
                    t.Name = NameOf(d);
                    string thumb = Path.Combine(d, "thumbnail.jpg"), preview = Path.Combine(d, "preview.jpg");
                    t.Picture = File.Exists(thumb) ? thumb : File.Exists(preview) ? preview : null;
                    foreach (var it in ws.worldItems)
                        if (it != null && !string.IsNullOrEmpty(it.sourceFile) && Same(it.sourceFile, data)) { t.Added = true; break; }
                    found.Add(t);
                }
            found.Sort((a, b) => b.Saved.CompareTo(a.Saved));
            foreach (var t in found) MakeTile(t);
            heading.text = found.Count == 0 ? "No saved worlds found" : "Choose a world to publish (" + found.Count + ")";
            empty.gameObject.SetActive(found.Count == 0);
            empty.text = "No saved worlds were found in\n" + dir + "\n\nSave a world in the game first, or use Select File...";
            scroll.verticalNormalizedPosition = 1f;
            panel.SetActive(true);
            panel.transform.SetAsLastSibling();
            loading = ws.StartCoroutine(LoadPictures());
        }

        static void Close()
        {
            if (panel != null) panel.SetActive(false);
            Clear();
        }

        static void Clear()
        {
            if (loading != null && ws != null) { ws.StopCoroutine(loading); loading = null; }
            foreach (var t in tiles)
            {
                if (t.Texture != null) UnityEngine.Object.Destroy(t.Texture);
                if (t.Go != null) UnityEngine.Object.Destroy(t.Go);
            }
            tiles.Clear();
        }

        /// the game's save folder (the tool's own folder with the game's name in place of its own, as the tool works it out)
        static string SaveFolder()
        {
            return FileUtils.GetSaveDirectory().Replace('/', '\\').Replace("TinyTownWorkshop", "TinyTown");
        }

        static string NameOf(string folder)
        {
            try
            {
                string f = Path.Combine(folder, "name.txt");
                if (File.Exists(f))
                {
                    string n = File.ReadAllText(f).Trim();
                    int nl = n.IndexOfAny(new[] { '\r', '\n' });
                    if (nl >= 0) n = n.Substring(0, nl).Trim();
                    if (n.Length > 0) return n;
                }
            }
            catch (Exception) { }
            return Path.GetFileName(folder);
        }

        static bool Same(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // the pictures load one after another in the background, so the page appears at once
        static IEnumerator LoadPictures()
        {
            yield return null;
            int n = 0;
            foreach (var t in new List<Tile>(tiles))
            {
                if (t.Go == null || t.Picture == null) continue;
                // the file is read on a worker thread; the picture is made from it here (Unity's own objects only can be)
                byte[] bytes = null; string path = t.Picture;
                var job = Ui.Run(delegate { bytes = File.ReadAllBytes(path); });
                yield return Ui.Wait(job);
                if (t.Go == null) continue;                       // (the page was closed meanwhile)
                if (job.Error != null) { Plugin.Log.LogInfo("World picker: " + path + ": " + job.Error.Message); continue; }
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, true);
                if (tex.LoadImage(bytes)) { t.Texture = tex; t.Image.texture = tex; t.Image.color = Color.white; }
                else UnityEngine.Object.Destroy(tex);
                if (++n % 4 == 0) yield return null;
            }
            loading = null;
        }

        static void Choose(Tile t)
        {
            Close();
            try
            {
                if (ws.errorMessageText != null) ws.errorMessageText.text = string.Empty;
                // what the tool's + does once its dialog has given it the world's "data" file
                // (CreateWorldItem is private in the tool, so it is reached by name)
                var item = HarmonyLib.AccessTools.Method(typeof(Workshop), "CreateWorldItem").Invoke(ws, new object[] { t.Data });
                if (item != null) ws.ActivateDetailView();
            }
            catch (Exception e) { WorkshopPlus.SetMessage(ws, "Couldn't add the world: " + e.Message); Plugin.Log.LogWarning("World picker: " + e); }
        }

        static void SelectFile()
        {
            Close();
            Bypass = true;
            try { ws.OnNewButtonClicked(); }           // the tool's own dialog, in the game's save folder
            finally { Bypass = false; }
        }

        static void Filter()
        {
            string q = search != null ? search.text.Trim() : "";
            int shown = 0;
            foreach (var t in tiles)
            {
                bool on = q.Length == 0 || t.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || Path.GetFileName(t.Folder).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (t.Go != null) t.Go.SetActive(on);
                if (on) shown++;
            }
            empty.gameObject.SetActive(shown == 0);
            if (shown == 0 && tiles.Count > 0) empty.text = "No world's name matches \"" + q + "\".";
        }

        // ---------------------------------------------------------------- the page

        static void Build()
        {
            var root = ws.listView.transform.parent;
            panel = new GameObject("WorkshopPlusWorldPicker", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(root, false); Stretch(prt, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            panel.GetComponent<Image>().color = new Color(0.94f, 0.94f, 0.95f, 1f);
            panel.AddComponent<Keys>();

            heading = MakeText(prt, "Title", "", 30, TextAnchor.MiddleLeft, new Color(0.15f, 0.15f, 0.17f));
            heading.fontStyle = FontStyle.Bold;
            Stretch(heading.rectTransform, 0f, 1f, 0.6f, 1f, 24f, -64f, 0f, -12f);
            MakeButton(prt, "Cancel", null, Close, new Vector2(-24f, -14f), new Vector2(120f, 38f));
            MakeButton(prt, "Select File...", null, SelectFile, new Vector2(-156f, -14f), new Vector2(170f, 38f));

            var sGo = (GameObject)UnityEngine.Object.Instantiate(ws.titleField.gameObject, prt, false);
            sGo.name = "WorldPickerSearch"; sGo.SetActive(true);
            var srt = (RectTransform)sGo.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f); srt.sizeDelta = new Vector2(380f, 38f); srt.anchoredPosition = new Vector2(24f, -76f);
            search = sGo.GetComponent<InputField>();
            search.onValueChanged = new InputField.OnChangeEvent(); search.onEndEdit = new InputField.SubmitEvent();
            search.lineType = InputField.LineType.SingleLine; search.characterLimit = 0; search.text = "";
            if (search.placeholder is Text) ((Text)search.placeholder).text = "Search world names";
            search.onValueChanged.AddListener(delegate { Filter(); });

            // the pictures: a scrolling grid
            var sgo = new GameObject("Worlds", typeof(RectTransform));
            var lrt = (RectTransform)sgo.transform; lrt.SetParent(prt, false);
            Stretch(lrt, 0f, 0f, 1f, 1f, 24f, 24f, -24f, -126f);
            RectTransform vrt;
            scroll = Ui.Scroller(lrt, new Color(1f, 1f, 1f, 0.6f), 40f, false, out vrt);
            var cgo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            grid = (RectTransform)cgo.transform; grid.SetParent(vrt, false);
            grid.anchorMin = new Vector2(0f, 1f); grid.anchorMax = new Vector2(1f, 1f); grid.pivot = new Vector2(0.5f, 1f);
            grid.sizeDelta = Vector2.zero; grid.anchoredPosition = Vector2.zero;
            var g = cgo.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(240f, 300f); g.spacing = new Vector2(18f, 18f); g.padding = new RectOffset(18, 18, 18, 18);
            g.childAlignment = TextAnchor.UpperLeft;
            cgo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = grid;

            empty = MakeText(lrt, "Empty", "", 22, TextAnchor.MiddleCenter, new Color(0.35f, 0.35f, 0.38f));
            Stretch(empty.rectTransform, 0f, 0f, 1f, 1f, 40f, 40f, -40f, -40f);
        }

        static void MakeTile(Tile t)
        {
            var go = new GameObject("World " + t.Name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(grid, false);
            go.GetComponent<Image>().color = Color.white;
            var b = go.GetComponent<Button>();
            var colours = b.colors; colours.highlightedColor = new Color(0.86f, 0.93f, 1f); colours.pressedColor = new Color(0.75f, 0.87f, 0.98f); b.colors = colours;
            b.onClick.AddListener(delegate { Choose(t); });
            var rt = (RectTransform)go.transform;

            var pgo = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            var prt = (RectTransform)pgo.transform; prt.SetParent(rt, false);
            prt.anchorMin = new Vector2(0f, 1f); prt.anchorMax = new Vector2(1f, 1f); prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(-16f, 224f); prt.anchoredPosition = new Vector2(0f, -8f);
            t.Image = pgo.GetComponent<RawImage>(); t.Image.color = new Color(0.85f, 0.86f, 0.88f); t.Image.raycastTarget = false;

            var name = MakeText(rt, "Name", t.Name, 18, TextAnchor.UpperLeft, new Color(0.12f, 0.12f, 0.14f));
            name.fontStyle = FontStyle.Bold; name.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(name.rectTransform, 0f, 0f, 1f, 0f, 10f, 30f, -10f, 64f);
            string extra = t.Added ? "Already in your list" : t.Workshop ? "A downloaded Workshop world" : "";
            var when = MakeText(rt, "Saved", "Saved " + t.Saved.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + (extra.Length > 0 ? "\n" + extra : ""), 14,
                                TextAnchor.UpperLeft, t.Added ? new Color(0.75f, 0.42f, 0.05f) : new Color(0.42f, 0.42f, 0.46f));
            Stretch(when.rectTransform, 0f, 0f, 1f, 0f, 10f, 2f, -10f, 32f);
            Tooltip.Add(go, t.Folder);
            t.Go = go;
            tiles.Add(t);
        }

        // (the shared building blocks are in Ui.cs)
        static void Stretch(RectTransform r, float x0, float y0, float x1, float y1, float l, float b, float rt, float t) { Ui.Stretch(r, x0, y0, x1, y1, l, b, rt, t); }
        static Text MakeText(Transform parent, string name, string words, int size, TextAnchor anchor, Color color) { return Ui.Text(ws, parent, name, words, size, anchor, color); }
        static void MakeButton(Transform parent, string label, string tip, Action press, Vector2 pos, Vector2 size)
        {
            Ui.Button(ws, parent, label, tip, press, new Vector2(1f, 1f), new Vector2(1f, 1f), pos, size);
        }

        /// Esc closes the page
        public class Keys : MonoBehaviour
        {
            void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Close(); }
        }
    }
}
