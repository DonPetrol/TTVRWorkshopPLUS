// TTVR Workshop Plus - small building blocks for the pages Workshop Plus adds over the tool's window (Asset Browser,
// world picker, Shader Properties, watermark): text, buttons made from the tool's own, scroll areas, and a spare
// layer for a camera of its own. Kept in one place so every page looks and behaves the same.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace TTVRPlus
{
    public static class Ui
    {
        static Font font;
        /// the tool's own font (that of its Title box), or Unity's Arial if that can't be found
        public static Font Font(Workshop w)
        {
            if (font == null) font = w != null && w.titleField != null && w.titleField.textComponent != null ? w.titleField.textComponent.font : null;
            return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// anchors (x0,y0)-(x1,y1) of the parent, and the edges that far from them
        public static void Stretch(RectTransform r, float x0, float y0, float x1, float y1, float l, float b, float rt, float t)
        {
            r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1); r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(rt, t);
        }

        /// plain words (they don't catch clicks)
        public static Text Text(Workshop w, Transform parent, string name, string words, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font(w); t.fontSize = size; t.alignment = anchor; t.color = color; t.text = words; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false;
            return t;
        }

        /// a copy of the tool's Publish button: placed at `anchor` of the parent by its `pivot`, with its own label and
        /// action; a tooltip only if one is given (none for buttons that say what they do)
        public static Button Button(Workshop w, Transform parent, string label, string tip, Action press, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, parent, false);
            go.name = "WorkshopPlus " + label; go.SetActive(true);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent(); b.interactable = true;
            b.onClick.AddListener(delegate { press(); });
            var text = go.GetComponentInChildren<Text>();
            if (text != null) { text.text = label; WorkshopPlus.FitButtonText(text); }
            if (!string.IsNullOrEmpty(tip)) Tooltip.Add(go, tip);
            return b;
        }

        /// a scrolling area filling `area`, with a scroll bar at its right; the caller puts its content in `viewport`
        /// and hands it to the ScrollRect (scroll.content)
        public static ScrollRect Scroller(RectTransform area, Color background, float sensitivity, bool barAlways, out RectTransform viewport)
        {
            area.gameObject.AddComponent<Image>().color = background;
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false; scroll.scrollSensitivity = sensitivity;
            var vgo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport = (RectTransform)vgo.transform; viewport.SetParent(area, false); Stretch(viewport, 0f, 0f, 1f, 1f, 0f, 0f, -18f, 0f);
            vgo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);        // (invisible, but catches the mouse wheel over empty space)
            scroll.viewport = viewport;

            var bgo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var brt = (RectTransform)bgo.transform; brt.SetParent(area, false); Stretch(brt, 1f, 0f, 1f, 1f, -16f, 2f, -2f, -2f);
            bgo.GetComponent<Image>().color = new Color(0.9f, 0.9f, 0.92f);
            var slide = new GameObject("Sliding Area", typeof(RectTransform));
            var srt = (RectTransform)slide.transform; srt.SetParent(brt, false); Stretch(srt, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            var hgo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            var hrt = (RectTransform)hgo.transform; hrt.SetParent(srt, false); Stretch(hrt, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            hgo.GetComponent<Image>().color = new Color(0.62f, 0.64f, 0.68f);
            var bar = bgo.GetComponent<Scrollbar>();
            bar.handleRect = hrt; bar.targetGraphic = hgo.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = barAlways ? ScrollRect.ScrollbarVisibility.Permanent : ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        /// work done on a worker thread (a coroutine waits for it with Wait); the window goes on drawing meanwhile.
        /// Only plain .NET work belongs there: Unity's own objects can only be touched on the main thread.
        public class Job { public volatile bool Done; public Exception Error; }
        public static Job Run(Action work)
        {
            var j = new Job();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { work(); } catch (Exception e) { j.Error = e; }
                j.Done = true;
            });
            return j;
        }
        public static System.Collections.IEnumerator Wait(Job j) { while (!j.Done) yield return null; }

        /// a layer nobody has named (so nothing of the tool's is on it), searched for from `from` downwards
        public static int FreeLayer(int from)
        {
            for (int i = from; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return from;
        }

        /// The tool's own button that calls the named routine of the Workshop (as set up in the tool's scene), looked for
        /// under `root`; null if there is none. (The tool's buttons have no names of their own worth going by.)
        public static Button ButtonCalling(Component root, string routine)
        {
            Button found = null;
            if (root == null) return null;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    if (b.onClick.GetPersistentMethodName(i) == routine) found = b;
            return found;
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // TTVR Workshop Plus - pictures as plain RGBA bytes (bottom-up rows, as everything in the mod keeps them) to and
    // from Unity's textures. This was written out wherever a texture was made or read.
    public static class Pixels
    {
        /// a texture's pixels as RGBA bytes (opaque: every alpha 255)
        public static byte[] Of(Texture2D t, bool opaque = false)
        {
            var px = t.GetPixels32();
            var b = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++) { int o = i * 4; b[o] = px[i].r; b[o + 1] = px[i].g; b[o + 2] = px[i].b; b[o + 3] = opaque ? (byte)255 : px[i].a; }
            return b;
        }

        /// RGBA bytes as a new texture. (Set as pixels and not as raw data: a texture with mipmaps would want every smaller
        /// copy in raw data, and Apply makes them from these.)
        public static Texture2D Texture(byte[] rgba, int w, int h, bool mipmaps, bool opaque = false, TextureFormat format = TextureFormat.RGBA32)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) { int o = i * 4; px[i] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], opaque ? (byte)255 : rgba[o + 3]); }
            var t = new Texture2D(w, h, format, mipmaps);
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
