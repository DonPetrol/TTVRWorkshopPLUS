// TTVR Workshop Plus - "Requires TTVR Enhanced Watermark": a band across the bottom of the Workshop thumbnail
// saying "Requires TTVR Enhanced", for items and worlds made to be used with the Tiny Town Enhanced mod.
// A checkbox under the Info box turns it on for the item being edited (off for every item until it is ticked, and it
// can be ticked and unticked at any time). It goes on the thumbnail the tool makes itself, on a world's own picture,
// and on a thumbnail taken with "From camera". A picture chosen with "Choose image" is uploaded as it is: while an item
// has one, the checkbox is hidden.
// The Info box shows the band as it will look; the band is drawn onto the picture only when publishing.
// Each item's setting is a small marker file beside its custom thumbnail: <item folder>.watermark (ticked), and
// <item folder>.chosen (its custom thumbnail is a picture chosen with Choose image). A custom thumbnail without that
// marker - taken From camera, or made before this existed - gets the watermark like the automatic one.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TTVRPlus
{
    public static class Watermark
    {
        public const string Words = "Requires TTVR Enhanced";
        const float Gap = 30f;                  // how far the fields under the Info box move down to make room
        static Workshop ws;
        static GameObject toggleGo, band; static Toggle toggle; static bool setting;
        static WorkshopItem lastItem; static int lastState = -1;

        // ---- the settings, on disk
        static string Marker(WorkshopItem it, string ext)
        {
            string f = ThumbPicker.CustomFile(it);
            return f == null ? null : Path.ChangeExtension(f, ext);
        }
        static bool Has(WorkshopItem it, string ext) { string f = Marker(it, ext); return f != null && File.Exists(f); }
        static void Put(WorkshopItem it, string ext, bool on)
        {
            string f = Marker(it, ext);
            if (f == null) return;
            try
            {
                if (on) { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, ""); }
                else if (File.Exists(f)) File.Delete(f);
            }
            catch (Exception e) { Plugin.Log.LogInfo("Watermark: " + e.Message); }
        }

        public static bool On(WorkshopItem it) { return Has(it, ".watermark"); }
        /// the custom thumbnail was taken from the camera (so it gets the band); false for a chosen image (or none)
        public static void MarkCamera(WorkshopItem it, bool camera) { Put(it, ".chosen", false); lastState = -1; }
        /// the custom thumbnail is a picture chosen with Choose image (uploaded as it is)
        public static void MarkChosen(WorkshopItem it) { Put(it, ".chosen", true); lastState = -1; }
        static bool ChosenImage(WorkshopItem it) { return ThumbPicker.HasCustomThumb(it) && Has(it, ".chosen"); }
        /// does this item's uploaded thumbnail get the band
        public static bool Applies(WorkshopItem it) { return it != null && On(it) && !ChosenImage(it); }

        public static void Copy(WorkshopItem from, WorkshopItem to) { Put(to, ".watermark", On(from)); Put(to, ".chosen", Has(from, ".chosen")); }
        public static void Remove(WorkshopItem it) { Put(it, ".watermark", false); Put(it, ".chosen", false); }

        // ---- the checkbox, and the band shown on the Info box
        public static void Init(Workshop w)
        {
            ws = w;
            try
            {
                var sidebar = (RectTransform)w.titleField.transform.parent;
                var box = sidebar.Find("ThumbnailBox") as RectTransform;
                if (box == null) { Plugin.Log.LogInfo("Watermark: the Info box wasn't found"); return; }
                float boxBottom = box.anchoredPosition.y - box.sizeDelta.y;
                // the fields under the Info box move down a little to make room for the checkbox
                foreach (Transform c in sidebar)
                {
                    var r = c as RectTransform;
                    if (r == null || r == box || r.anchorMin.y != 1f || r.anchorMax.y != 1f) continue;
                    float top = r.anchoredPosition.y + (1f - r.pivot.y) * r.sizeDelta.y;
                    if (top <= boxBottom - 4f) r.anchoredPosition -= new Vector2(0f, Gap);
                }

                toggleGo = DefaultControls.CreateToggle(new DefaultControls.Resources());
                toggleGo.name = "WatermarkToggle";
                var trt = (RectTransform)toggleGo.transform; trt.SetParent(sidebar, false);
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 1f);
                trt.sizeDelta = new Vector2(232f, 24f); trt.anchoredPosition = new Vector2(15f, boxBottom - 6f);
                Font font = Ui.Font(w);
                foreach (var img in toggleGo.GetComponentsInChildren<Image>(true))
                {
                    var r = img.rectTransform;
                    if (img.name == "Background") { img.color = Color.white; r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f); r.anchoredPosition = new Vector2(0f, -2f); r.sizeDelta = new Vector2(20f, 20f); }
                    else if (img.name == "Checkmark") { img.color = new Color(0.3f, 0.27f, 0.55f); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(14f, 14f); r.anchoredPosition = Vector2.zero; }
                }
                var label = toggleGo.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = "Requires TTVR Enhanced Watermark";
                    if (font != null) label.font = font;
                    label.fontSize = 14; label.fontStyle = FontStyle.Bold; label.color = Color.white; label.alignment = TextAnchor.MiddleLeft;
                    label.resizeTextForBestFit = true; label.resizeTextMinSize = 9; label.resizeTextMaxSize = 14;
                    var lr = label.rectTransform; lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 1f); lr.offsetMin = new Vector2(26f, 0f); lr.offsetMax = Vector2.zero;
                }
                toggle = toggleGo.GetComponent<Toggle>();
                toggle.isOn = false;
                toggle.onValueChanged.AddListener(delegate (bool on)
                {
                    if (setting || ws.activeItem == null) return;
                    Put(ws.activeItem, ".watermark", on); lastState = -1;
                });
                Tooltip.Add(toggleGo, "Adds a \"" + Words + "\" band to the bottom of the Workshop thumbnail (the automatic one, or one taken From camera), so players know the item needs the Tiny Town Enhanced mod.");

                // the band, shown over the Info box's picture
                band = new GameObject("WatermarkBand", typeof(RectTransform), typeof(Image));
                var brt = (RectTransform)band.transform; brt.SetParent(box, false);
                brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0.17f); brt.offsetMin = new Vector2(2f, 2f); brt.offsetMax = new Vector2(-2f, 0f);
                var bi = band.GetComponent<Image>(); bi.color = BandColour; bi.raycastTarget = false;
                var t = Text(brt, font);
                t.resizeTextMaxSize = 11;
                band.SetActive(false);
                toggleGo.SetActive(false);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Watermark: " + e); }
        }

        static readonly Color BandColour = new Color(0.08f, 0.08f, 0.1f, 0.72f);

        static Text Text(RectTransform parent, Font font)
        {
            var go = new GameObject("Words", typeof(RectTransform), typeof(Text));
            var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(2f, 1f); r.offsetMax = new Vector2(-2f, -1f);
            var t = go.GetComponent<Text>();
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = Words; t.color = Color.white; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 6; t.resizeTextMaxSize = 300; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// every frame (with the Info box): follows the item being edited; checks the disk only when something changed
        public static void Update()
        {
            if (toggleGo == null || ws == null) return;
            var it = ws.detailView.activeSelf ? ws.activeItem : null;
            if (it != lastItem) { lastItem = it; lastState = -1; }
            if (lastState >= 0) return;
            bool chosen = it != null && ChosenImage(it), on = it != null && On(it);
            lastState = 1;
            toggleGo.SetActive(it != null && !chosen);
            setting = true; toggle.isOn = on; setting = false;
            band.SetActive(it != null && on && !chosen);
        }
        /// something about the item's thumbnail changed (a custom one set or removed): look again
        public static void Refresh() { lastState = -1; }

        // ---- the band, drawn onto a picture (publishing)
        /// a copy of the picture with the band across its bottom (the picture itself is left as it is)
        public static Texture2D Stamp(Texture src)
        {
            int w = src.width, h = src.height;
            int layer = Ui.FreeLayer(30);
            var root = new GameObject("WorkshopPlusWatermark");
            root.transform.position = new Vector3(0f, -7000f, 0f);
            var oldActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                // a flat picture with the band over it, seen square-on by a camera of its own that sees nothing else
                var cgo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
                var crt = (RectTransform)cgo.transform; crt.SetParent(root.transform, false);
                var canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                crt.sizeDelta = new Vector2(w, h); crt.localPosition = Vector3.zero; crt.localScale = Vector3.one;
                var pic = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
                var prt = (RectTransform)pic.transform; prt.SetParent(crt, false);
                prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
                pic.GetComponent<RawImage>().texture = src;
                var strip = new GameObject("Band", typeof(RectTransform), typeof(Image));
                var srt = (RectTransform)strip.transform; srt.SetParent(crt, false);
                srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0.15f); srt.offsetMin = srt.offsetMax = Vector2.zero;
                strip.GetComponent<Image>().color = BandColour;
                Font font = Ui.Font(ws);
                var t = Text(srt, font);
                t.resizeTextMaxSize = Mathf.RoundToInt(h * 0.085f);
                srt.offsetMin = Vector2.zero;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;

                var cam = new GameObject("Camera").AddComponent<Camera>();
                cam.transform.SetParent(root.transform, false);
                cam.transform.localPosition = new Vector3(0f, 0f, -10f); cam.transform.localRotation = Quaternion.identity;
                cam.enabled = false; cam.orthographic = true; cam.orthographicSize = h * 0.5f; cam.aspect = (float)w / h;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.cullingMask = 1 << layer;
                cam.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                var result = new Texture2D(w, h, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                result.Apply();
                return result;
            }
            finally
            {
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
