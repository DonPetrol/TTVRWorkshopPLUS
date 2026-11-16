// TTVR Workshop Plus - "Shader Properties": the starting look an item has in the game, for players with the
// Tiny Town Enhanced mod (smoothness, metallic, tint, glow, transparency, unlit, shadows - the settings of the mod's
// Edit tab). It is published inside the item as looks.txt, next to the model and its texture; the game itself never
// reads that file, so the item works exactly as before for everyone else. In the game (with the mod), every copy of
// the item starts with this look, and the Edit tab's Reset goes back to it.
// The button is only shown when Tiny Town Enhanced is installed in this game folder (checked when the tool starts).
// It sits in an item's side panel between the Texture rows and Visibility (which moves down a row for it, on item
// pages only), and opens a panel to the left of the side panel.
// Each item's look is kept in Development\workshopplus_looks\<item folder>.txt (the same text as looks.txt); an item
// with the game's own look has no file, and publishes none.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TTVRPlus
{
    public static class ItemLook
    {
        public const string FileName = "looks.txt";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// a look: what the mod's Edit tab sets (the defaults are the game's own look)
        public class Values
        {
            public float Smooth = 0.5f, Metal = 0f, GlowStrength = 1f, Transparency = 0f;
            public Color Tint = Color.white, Glow = Color.black;
            public bool GlowMap, Unlit, Shadows = true;

            public bool IsGameLook { get { return ToText(true) == new Values().ToText(true); } }

            /// as looks.txt (bare: the settings only, to compare two looks)
            public string ToText(bool bare = false)
            {
                var s = new System.Text.StringBuilder();
                if (!bare) s.Append("# The look this item starts with in Tiny Town VR with the Tiny Town Enhanced mod (made with Workshop Plus).\n# The game itself doesn't read this file.\n");
                s.Append("smoothness=").Append(Smooth.ToString("0.##", Inv)).Append('\n');
                s.Append("metallic=").Append(Metal.ToString("0.##", Inv)).Append('\n');
                s.Append("tint=").Append(Hex(Tint)).Append('\n');
                s.Append("glowColor=").Append(Hex(Glow)).Append('\n');
                s.Append("glowFromTexture=").Append(GlowMap ? "true" : "false").Append('\n');
                s.Append("glowStrength=").Append(GlowStrength.ToString("0.#", Inv)).Append('\n');
                s.Append("transparency=").Append(Transparency.ToString("0.##", Inv)).Append('\n');
                s.Append("unlit=").Append(Unlit ? "true" : "false").Append('\n');
                s.Append("castShadows=").Append(Shadows ? "true" : "false").Append('\n');
                return s.ToString();
            }

            public static Values Parse(string text)
            {
                var v = new Values();
                foreach (string raw in (text ?? "").Split('\n'))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.StartsWith("#") || eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant(), val = line.Substring(eq + 1).Trim();
                    float f; bool b; Color c;
                    switch (key)
                    {
                        case "smoothness": if (Num(val, out f)) v.Smooth = Mathf.Clamp01(f); break;
                        case "metallic": if (Num(val, out f)) v.Metal = Mathf.Clamp01(f); break;
                        case "tint": if (FromHex(val, out c)) v.Tint = c; break;
                        case "glowcolor": if (FromHex(val, out c)) v.Glow = c; break;
                        case "glowfromtexture": if (bool.TryParse(val, out b)) v.GlowMap = b; break;
                        case "glowstrength": if (Num(val, out f)) v.GlowStrength = Mathf.Clamp(f, 0.1f, 3f); break;
                        case "transparency": if (Num(val, out f)) v.Transparency = Mathf.Clamp(f, 0f, 0.9f); break;
                        case "unlit": if (bool.TryParse(val, out b)) v.Unlit = b; break;
                        case "castshadows": if (bool.TryParse(val, out b)) v.Shadows = b; break;
                    }
                }
                return v;
            }
        }

        public static string Hex(Color c)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f).ToString("X2") + Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f).ToString("X2") + Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f).ToString("X2");
        }
        public static bool FromHex(string s, out Color c)
        {
            c = Color.white; int n;
            s = (s ?? "").Trim(); if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, Inv, out n)) return false;
            c = new Color(((n >> 16) & 255) / 255f, ((n >> 8) & 255) / 255f, (n & 255) / 255f, 1f);
            return true;
        }
        static bool Num(string s, out float f) { return float.TryParse(s, NumberStyles.Float, Inv, out f) && !float.IsNaN(f) && !float.IsInfinity(f); }

        // ---------------------------------------------------------------- each item's look, on disk

        static string Dir() { return Path.Combine(FileUtils.GetWorkshopDevelopmentDirectory(), "workshopplus_looks"); }
        static string FileFor(WorkshopItem it) { return it == null || string.IsNullOrEmpty(it.uniqueDirectory) ? null : Path.Combine(Dir(), it.uniqueDirectory + ".txt"); }

        public static Values Get(WorkshopItem it)
        {
            try { string f = FileFor(it); if (f != null && File.Exists(f)) return Values.Parse(File.ReadAllText(f)); }
            catch (Exception e) { Plugin.Log.LogInfo("Shader Properties: " + e.Message); }
            return new Values();
        }

        public static void Set(WorkshopItem it, Values v)
        {
            string f = FileFor(it);
            if (f == null) return;
            try
            {
                if (v.IsGameLook) { if (File.Exists(f)) File.Delete(f); return; }
                Directory.CreateDirectory(Dir());
                File.WriteAllText(f, v.ToText());
            }
            catch (Exception e) { WorkshopPlus.SetMessage(ws, "Couldn't save the in-game look: " + e.Message); }
        }

        /// a duplicate starts with the same look
        public static void Copy(WorkshopItem from, WorkshopItem to)
        {
            try { string a = FileFor(from), b = FileFor(to); if (a != null && b != null && File.Exists(a)) File.Copy(a, b, true); } catch (Exception) { }
        }
        /// a deleted item's look goes with it
        public static void Remove(WorkshopItem it)
        {
            try { string f = FileFor(it); if (f != null && File.Exists(f)) File.Delete(f); } catch (Exception) { }
        }

        /// publishing: the item's folder gets its looks.txt (or loses an old one, when it has the game's look)
        public static void AddToUpload(WorkshopItem it, string folder)
        {
            string target = Path.Combine(folder, FileName);
            var v = Get(it);
            if (v.IsGameLook) { if (File.Exists(target)) File.Delete(target); return; }
            Directory.CreateDirectory(folder);
            File.WriteAllText(target, v.ToText());
        }

        // ---------------------------------------------------------------- is Tiny Town Enhanced installed here

        static bool? enhanced;
        public static bool EnhancedInstalled
        {
            get
            {
                if (enhanced.HasValue) return enhanced.Value;
                enhanced = false;
                try
                {
                    string plugins = Path.Combine(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BepInEx"), "plugins");
                    if (Directory.Exists(plugins)) enhanced = Directory.GetFiles(plugins, "TinyTownEnhanced.dll", SearchOption.AllDirectories).Length > 0;
                }
                catch (Exception e) { Plugin.Log.LogInfo("Shader Properties: " + e.Message); }
                Plugin.Log.LogInfo("Tiny Town Enhanced is " + (enhanced.Value ? "" : "not ") + "installed: the Shader Properties button is " + (enhanced.Value ? "shown" : "hidden"));
                return enhanced.Value;
            }
        }

        // ---------------------------------------------------------------- the panel

        static Workshop ws;
        static GameObject button, panel; static Font font;
        static RectTransform sidebar, visLabel, visDropdown; static Vector2 visLabelAt, visDropdownAt; static bool visMoved;
        const float ButtonRow = 44f;            // the room the button takes in the side panel
        static WorkshopItem editing; static Values look;
        static bool filling;
        static readonly List<Action> refresh = new List<Action>();
        static GameObject glowColorRow, glowStrengthRow;

        public static void Init(Workshop w)
        {
            ws = w;
            if (!EnhancedInstalled) return;
            try
            {
                font = Ui.Font(w);
                // in the side panel, under the Texture rows (the Size row, if Workshop Plus added it, or else the Texture box)
                sidebar = (RectTransform)w.titleField.transform.parent;
                visLabel = sidebar.Find("VisibilityLabel") as RectTransform;
                visDropdown = w.visibilityDropdown != null ? (RectTransform)w.visibilityDropdown.transform : null;
                var above = (sidebar.Find("TextureSizeDropdown") ?? sidebar.Find("TextureField")) as RectTransform;
                float top = above != null ? above.anchoredPosition.y - above.sizeDelta.y * above.pivot.y : -560f;
                if (visLabel != null) visLabelAt = visLabel.anchoredPosition;
                if (visDropdown != null) visDropdownAt = visDropdown.anchoredPosition;
                button = (GameObject)UnityEngine.Object.Instantiate(w.publishButton.gameObject, sidebar, false);
                button.name = "ShaderPropertiesButton";
                var rt = (RectTransform)button.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(220f, 34f); rt.anchoredPosition = new Vector2(15f, top - 8f);
                var b = button.GetComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent(); b.interactable = true;
                b.onClick.AddListener(Toggle);
                var t = button.GetComponentInChildren<Text>(); if (t != null) { t.text = "Shader Properties"; WorkshopPlus.FitButtonText(t); }
                Tooltip.Add(button, "How this item looks in the game for players with the Tiny Town Enhanced mod: smoothness, metallic, tint, glow and more. Everyone else sees it as usual.");
                Build(w.detailView.transform);
                button.SetActive(false); panel.SetActive(false);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Shader Properties: " + e); }
        }

        /// every frame (with the thumbnail box): shown on an item's page only; follows the item being edited
        public static void Update()
        {
            if (button == null || ws == null) return;
            bool on = ws.objectTabActive && ws.detailView.activeSelf && ws.activeItem != null;
            if (button.activeSelf != on) button.SetActive(on);
            // Visibility one row lower while the button is there (items), back where it was otherwise (worlds)
            bool move = ws.objectTabActive;
            if (move != visMoved)
            {
                visMoved = move;
                Vector2 down = new Vector2(0f, move ? -ButtonRow : 0f);
                if (visLabel != null) visLabel.anchoredPosition = visLabelAt + down;
                if (visDropdown != null) visDropdown.anchoredPosition = visDropdownAt + down;
            }
            if (!on) { if (panel.activeSelf) panel.SetActive(false); Preview(false); return; }
            if (editing != ws.activeItem) { editing = ws.activeItem; look = Get(editing); Fill(); previewCode = null; }
            if (Time.unscaledTime >= nextPreview) Preview(true);
        }

        // ---- the look on the 3D preview
        // The tool draws the model with one material of its own (its texture on it). While the item has shader
        // properties, the preview's model is drawn instead with a material made the way Tiny Town Enhanced makes it in
        // the game (Unity's Standard shader, the item's texture, the settings), and is put back to the tool's own when
        // they go. (The tool builds the model again now and then, with its own material: checked a few times a second.)
        static Material previewMat; static string previewCode; static Texture previewTex; static float nextPreview;
        static readonly List<Renderer> renderers = new List<Renderer>();

        static void Preview(bool on)
        {
            nextPreview = Time.unscaledTime + 0.15f;
            var root = ws.objectRoot;
            if (root == null) return;
            bool own = on && look != null && !look.IsGameLook;
            Material want = ws.defaultMaterial;
            if (own)
            {
                if (previewMat == null)
                {
                    var standard = Shader.Find("Standard");
                    if (standard == null) { Plugin.Log.LogInfo("Shader Properties: the Standard shader isn't in the tool; the preview can't show the look"); own = false; }
                    else previewMat = new Material(standard) { name = "WorkshopPlus shader properties" };
                }
                if (own)
                {
                    string code = look.ToText(true);
                    Texture tex = ws.defaultMaterial.mainTexture;
                    if (code != previewCode || tex != previewTex) { Dress(previewMat, look, tex); previewCode = code; previewTex = tex; }
                    want = previewMat;
                }
            }
            root.GetComponentsInChildren(true, renderers);
            foreach (var r in renderers)
            {
                if (r.sharedMaterial != ws.defaultMaterial && (previewMat == null || r.sharedMaterial != previewMat)) continue;     // (not the model)
                if (r.sharedMaterial != want) r.sharedMaterial = want;
                var shadows = own && !look.Shadows ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (r.shadowCastingMode != shadows) r.shadowCastingMode = shadows;
            }
            renderers.Clear();
        }

        // the material, as Tiny Town Enhanced makes it for a look in the game (Looks.cs there)
        static void Dress(Material m, Values v, Texture tex)
        {
            m.shaderKeywords = new string[0];
            m.mainTexture = tex;
            float alpha = 1f - Mathf.Clamp(v.Transparency, 0f, 0.9f);
            bool clear = v.Transparency > 0.004f;
            bool glows = v.GlowMap ? v.GlowStrength > 0.04f : Hex(v.Glow) != "000000";
            if (v.Unlit)
            {
                m.color = new Color(0f, 0f, 0f, alpha);
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", new Color(v.Tint.r, v.Tint.g, v.Tint.b, 1f));
                m.EnableKeyword("_EMISSION"); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
                m.SetFloat("_SpecularHighlights", 0f); m.SetFloat("_GlossyReflections", 0f);
                m.SetFloat("_Glossiness", 0f); m.SetFloat("_Metallic", 0f);
            }
            else
            {
                m.color = new Color(v.Tint.r, v.Tint.g, v.Tint.b, alpha);
                float strength = Mathf.Clamp(v.GlowStrength, 0f, 3f);
                m.SetTexture("_EmissionMap", glows && v.GlowMap ? tex : null);
                if (glows) m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", !glows ? Color.black : v.GlowMap ? new Color(v.Tint.r * strength, v.Tint.g * strength, v.Tint.b * strength, 1f) : new Color(v.Glow.r, v.Glow.g, v.Glow.b, 1f));
                m.SetFloat("_SpecularHighlights", 1f); m.SetFloat("_GlossyReflections", 1f);
                m.SetFloat("_Glossiness", Mathf.Clamp01(v.Smooth)); m.SetFloat("_Metallic", Mathf.Clamp01(v.Metal));
            }
            m.SetFloat("_Mode", clear ? 2f : 0f);
            m.SetInt("_SrcBlend", (int)(clear ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetInt("_DstBlend", (int)(clear ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
            m.SetInt("_ZWrite", clear ? 0 : 1);
            if (clear) m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = clear ? 3000 : -1;
        }

        static void Toggle()
        {
            if (panel == null) return;
            panel.SetActive(!panel.activeSelf);
            if (!panel.activeSelf) return;
            editing = ws.activeItem; look = Get(editing); Fill();
            panel.transform.SetAsLastSibling();
            Place();
        }

        // to the left of the side panel, level with the button, but kept inside the page
        static void Place()
        {
            var prt = (RectTransform)panel.transform;
            var page = (RectTransform)prt.parent;
            var c = new Vector3[4];
            sidebar.GetWorldCorners(c);
            Vector3 sideLeft = page.InverseTransformPoint(c[0]);                     // (bottom left of the side panel)
            ((RectTransform)button.transform).GetWorldCorners(c);
            float buttonMid = page.InverseTransformPoint((c[0] + c[1]) * 0.5f).y;
            Rect r = page.rect;
            float h = prt.sizeDelta.y;
            float y = Mathf.Clamp(buttonMid, r.yMin + h * 0.5f + 8f, r.yMax - h * 0.5f - 8f);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(1f, 0.5f);
            prt.anchoredPosition = new Vector2(sideLeft.x - r.center.x - 8f, y - r.center.y);
        }

        static void Changed()
        {
            if (filling || editing == null) return;
            Set(editing, look);
            Fill();
            Preview(true);
        }

        static void Fill()
        {
            if (panel == null || look == null) return;
            filling = true;
            try { foreach (var r in refresh) r(); }
            finally { filling = false; }
            if (glowColorRow != null) glowColorRow.SetActive(!look.GlowMap);
            if (glowStrengthRow != null) glowStrengthRow.SetActive(look.GlowMap);
        }

        const float RowH = 44f, LabelW = 170f;

        static void Build(Transform page)
        {
            panel = new GameObject("ShaderPropertiesPanel", typeof(RectTransform), typeof(Image));
            var prt = (RectTransform)panel.transform; prt.SetParent(page, false);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            panel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.97f);
            var outline = panel.AddComponent<Outline>(); outline.effectColor = new Color(0.3f, 0.27f, 0.55f, 0.8f); outline.effectDistance = new Vector2(2f, -2f);

            float y = -14f;
            Words(prt, "Shader Properties (Tiny Town Enhanced)", 20, FontStyle.Bold, new Vector2(14f, y), new Vector2(400f, 30f)); y -= 40f;

            Slider(prt, "Smoothness", 0f, 1f, () => look.Smooth, v => look.Smooth = v, v => Mathf.RoundToInt(v * 100f) + "%", ref y);
            Slider(prt, "Metallic", 0f, 1f, () => look.Metal, v => look.Metal = v, v => Mathf.RoundToInt(v * 100f) + "%", ref y);
            Colour(prt, "Tint", () => look.Tint, c => look.Tint = c, ref y);
            glowColorRow = Colour(prt, "Glow Color", () => look.Glow, c => look.Glow = c, ref y);
            float glowY = y + RowH;          // (Glow Strength takes Glow Color's place: only one of the two is shown)
            Check(prt, "Glow From Texture", () => look.GlowMap, on => look.GlowMap = on, ref y);
            float afterY = y;
            glowStrengthRow = Slider(prt, "Glow Strength", 0.1f, 3f, () => look.GlowStrength, v => look.GlowStrength = v, v => Mathf.RoundToInt(v * 100f) + "%", ref glowY);
            y = afterY;
            Slider(prt, "Transparency", 0f, 0.9f, () => look.Transparency, v => look.Transparency = v, v => Mathf.RoundToInt(v * 100f) + "%", ref y);
            Check(prt, "Unlit", () => look.Unlit, on => look.Unlit = on, ref y);
            Check(prt, "Cast Shadows", () => look.Shadows, on => look.Shadows = on, ref y);
            y -= 6f;
            var note = Words(prt, "Only players with Tiny Town Enhanced see this, once you publish again; everyone else sees the item as usual. " +
                                  "Tint, glow, transparency and unlit cost a little performance in the game.", 14, FontStyle.Italic, new Vector2(14f, y), new Vector2(410f, 70f));
            note.color = new Color(0.35f, 0.35f, 0.4f); note.alignment = TextAnchor.UpperLeft;
            y -= 76f;
            SmallButton(prt, "Reset", "Back to the game's own look (nothing extra is published).", delegate { look = new Values(); Changed(); }, new Vector2(14f, y));
            SmallButton(prt, "Close", null, delegate { panel.SetActive(false); }, new Vector2(150f, y));
            prt.sizeDelta = new Vector2(440f, -y + 34f + 14f);       // (as tall as what is on it)
        }

        // ---- controls

        static Text Words(Transform parent, string s, int size, FontStyle style, Vector2 pos, Vector2 sz)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = pos; rt.sizeDelta = sz;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.fontStyle = style; t.color = new Color(0.15f, 0.15f, 0.18f); t.text = s;
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false;
            return t;
        }

        static GameObject Row(Transform parent, string label, float y)
        {
            var row = new GameObject(label, typeof(RectTransform));
            var rt = (RectTransform)row.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(0f, y); rt.sizeDelta = new Vector2(430f, RowH);
            Words(rt, label, 17, FontStyle.Normal, new Vector2(14f, -4f), new Vector2(LabelW, RowH - 8f));
            return row;
        }

        static GameObject Slider(Transform parent, string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> shown, ref float y)
        {
            var row = Row(parent, label, y); y -= RowH;
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            var rt = (RectTransform)go.transform; rt.SetParent(row.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(LabelW + 14f, 0f); rt.sizeDelta = new Vector2(170f, 20f);
            // (plain shapes: the default control has no pictures of its own)
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                img.color = img.name == "Background" ? new Color(0.85f, 0.85f, 0.88f) : img.name == "Fill" ? new Color(0.43f, 0.38f, 0.72f) : new Color(0.3f, 0.27f, 0.55f);
            var s = go.GetComponent<UnityEngine.UI.Slider>();
            s.minValue = min; s.maxValue = max;
            var value = Words(row.transform, "", 15, FontStyle.Normal, new Vector2(LabelW + 194f, -4f), new Vector2(60f, RowH - 8f));
            s.onValueChanged.AddListener(delegate (float v) { value.text = shown(v); if (!filling) { set(v); Changed(); } });
            refresh.Add(delegate { s.value = get(); value.text = shown(get()); });
            return row;
        }

        static GameObject Check(Transform parent, string label, Func<bool> get, Action<bool> set, ref float y)
        {
            var row = Row(parent, label, y); y -= RowH;
            var go = DefaultControls.CreateToggle(new DefaultControls.Resources());
            var rt = (RectTransform)go.transform; rt.SetParent(row.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(LabelW + 14f, 0f); rt.sizeDelta = new Vector2(30f, 30f);
            foreach (var txt in go.GetComponentsInChildren<Text>(true)) txt.text = "";
            foreach (var img in go.GetComponentsInChildren<Image>(true))
            {
                var r = img.rectTransform;
                if (img.name == "Background") { img.color = new Color(0.85f, 0.85f, 0.88f); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.zero; r.pivot = Vector2.zero; r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(26f, 26f); }
                else if (img.name == "Checkmark") { img.color = new Color(0.3f, 0.27f, 0.55f); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(16f, 16f); r.anchoredPosition = Vector2.zero; }
            }
            var t = go.GetComponent<Toggle>();
            t.onValueChanged.AddListener(delegate (bool on) { if (!filling) { set(on); Changed(); } });
            refresh.Add(delegate { t.isOn = get(); });
            return row;
        }

        static GameObject Colour(Transform parent, string label, Func<Color> get, Action<Color> set, ref float y)
        {
            var row = Row(parent, label, y); y -= RowH;
            var fgo = (GameObject)UnityEngine.Object.Instantiate(ws.titleField.gameObject, row.transform, false);
            fgo.name = label + " hex"; fgo.SetActive(true);
            var frt = (RectTransform)fgo.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0f, 0.5f); frt.anchoredPosition = new Vector2(LabelW + 14f, 0f); frt.sizeDelta = new Vector2(110f, 32f);
            var field = fgo.GetComponent<InputField>();
            field.onValueChanged = new InputField.OnChangeEvent(); field.onEndEdit = new InputField.SubmitEvent();
            field.lineType = InputField.LineType.SingleLine; field.characterLimit = 7;
            if (field.placeholder is Text) ((Text)field.placeholder).text = "RRGGBB";
            var sgo = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
            var srt = (RectTransform)sgo.transform; srt.SetParent(row.transform, false);
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0.5f); srt.anchoredPosition = new Vector2(LabelW + 132f, 0f); srt.sizeDelta = new Vector2(32f, 32f);
            var swatch = sgo.GetComponent<Image>();
            field.onEndEdit.AddListener(delegate (string s)
            {
                Color c;
                if (FromHex(s, out c)) { set(c); Changed(); }
                else { field.text = Hex(get()); WorkshopPlus.SetMessage(ws, "Note: a colour is six hex digits, for example FF8800."); }
            });
            refresh.Add(delegate { field.text = Hex(get()); swatch.color = get(); });
            Tooltip.Add(fgo, "A colour as six hex digits (red, green, blue), for example FFFFFF for white.");
            return row;
        }

        static void SmallButton(Transform parent, string label, string tip, Action press, Vector2 pos)
        {
            Ui.Button(ws, parent, label, tip, press, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(126f, 34f));
        }
    }
}
