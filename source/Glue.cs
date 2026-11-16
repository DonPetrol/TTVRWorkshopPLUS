// TTVR Workshop Plus - main Unity side: start-up (window, list loading), model loading and building
// (Workshop.LoadObject), textures, the editor's extra controls (shading, atlas, crop, size, flip, reimport,
// export) and messages. Other features live in their own files (list, thumbnails, publish, help...).
// In three files: this one has textures, the caches and model loading and building; Glue.Startup.cs has start-up, the
// item list and Run Game; Glue.Editor.cs has the editor's extra controls.
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
        public const string Version = "1.1";
        public const int VertexLimit = 65000;     // same constant the game uses; 16-bit Unity meshes

        static Texture2D ToTexture(byte[] rgba, int size) { return Pixels.Texture(rgba, size, size, true, true); }

        static void SetMainTexture(Workshop w, Texture tex)
        {
            if (w.defaultMaterial.mainTexture != null && w.defaultMaterial.mainTexture != w.defaultTexture)
                UnityEngine.Object.Destroy(w.defaultMaterial.mainTexture);
            w.defaultMaterial.mainTexture = tex;
        }

        // ---------------------------------------------------------------- texture loading (replaces Workshop.LoadTexture)
        static List<string> currentNotes;      // notes for the model being loaded, if any

        // notes about a texture that couldn't be used are always shown; size/atlas notes only when importing
        static readonly HashSet<string> alwaysNotes = new HashSet<string>();
        static int importFrame = -1;

        /// called at the start of the + (new), model Browse, texture Browse and texture Clear buttons
        public static void MarkImport() { importFrame = Time.frameCount; }
        static bool Importing { get { return importFrame == Time.frameCount; } }

        static void TexNote(string msg, bool always)
        {
            if (always) alwaysNotes.Add(msg);
            TexNote(msg);
        }

        static void TexNote(string msg)
        {
            if (currentNotes != null) { if (!currentNotes.Contains(msg)) currentNotes.Add(msg); }
            else Debug.Log("[WorkshopPlus] " + msg);
        }

        static GameObject reimportButton;

        /// re-read the model (and textures) from the same path; the current model stays if that fails
        static void Reimport(Workshop w)
        {
            var item = w.activeItem;
            if (item == null || string.IsNullOrEmpty(item.sourceFile)) { SetMessage(w, "Unable to reimport: this item has no model file."); return; }
            string path = item.sourceFile, name = Path.GetFileName(path);
            if (!File.Exists(path)) { SetMessage(w, "Unable to reimport: " + path + " can't be found. The current model is unchanged."); return; }
            ObjModel model; List<ModelMaterial> mats = null; string warn = null;
            try
            {
                if (GltfReader.IsGltf(path)) model = GltfReader.Load(path, out mats, out warn);
                else model = ObjParser.Parse(path);
            }
            catch (Exception e)
            {
                Debug.Log("[WorkshopPlus] reimport: " + e);
                SetMessage(w, "Unable to reimport " + name + ": " + e.Message + ". The current model is unchanged.");
                return;
            }
            // forget everything cached for it, then rebuild from the fresh copy
            // (no key means the file's date and size couldn't be read just now: nothing is kept, and the rebuild reads it again)
            string key = FileKey(path);
            if (key != null) RememberModel(key, model, mats, warn);
            decodedCache.Clear(); decodedOrder.Clear(); decodedBytes = 0;
            atlasCache.Clear(); atlasOrder.Clear();
            FileCache.Forget(path);
            MarkImport();
            Rebuild(w);
            var t = w.errorMessageText != null ? w.errorMessageText.text : "";
            if (string.IsNullOrEmpty(t)) SetMessage(w, "Note: reimported " + name + ".");
            else if (t.StartsWith("Note:")) SetMessage(w, "Note: reimported " + name + "; " + t.Substring(6));
        }

        static int texMax = 512;          // the item's Texture size setting while it's being built
        static int SupportedSize(int v) { return v < 128 ? 128 : v < 256 ? 256 : 512; }   // same rule as Workshop.exe

        // ---------------------------------------------------------------- caches (so changing shading/atlas options doesn't re-read files)
        class Decoded { public int W, H; public byte[] Px; public int SqSize; public byte[] Sq; }
        static readonly Dictionary<string, Decoded> decodedCache = new Dictionary<string, Decoded>();
        static readonly List<string> decodedOrder = new List<string>();
        static long decodedBytes;

        static string FileKey(string path)
        {
            try { var fi = new FileInfo(path); return Path.GetFullPath(path).ToLowerInvariant() + "|" + fi.LastWriteTimeUtc.Ticks + "|" + fi.Length; }
            catch (Exception) { return null; }
        }

        /// decoded RGBA for a file or embedded image, decoded once and kept while it's in use (about 200MB at most)
        static Decoded GetDecoded(string key, Func<byte[]> read, string pathForDetect, out string err)
        {
            err = null;
            Decoded d;
            if (key != null && decodedCache.TryGetValue(key, out d)) { decodedOrder.Remove(key); decodedOrder.Add(key); return d; }
            byte[] data = read();
            int iw, ih; byte[] px;
            if (!DecodeImage(data, pathForDetect, out iw, out ih, out px, out err)) return null;
            d = new Decoded { W = iw, H = ih, Px = px };
            if (key != null)
            {
                decodedCache[key] = d; decodedOrder.Add(key); decodedBytes += px.Length;
                TrimDecoded();
            }
            return d;
        }

        // Drops the pictures used longest ago once more than about 200MB is kept. Only a picture's full-size pixels
        // are counted, the same when it is added as when it is dropped (its shrunk copy is 1MB at most).
        static void TrimDecoded()
        {
            while (decodedBytes > 200L * 1024 * 1024 && decodedOrder.Count > 1)
            {
                string old = decodedOrder[0]; decodedOrder.RemoveAt(0);
                Decoded o; if (decodedCache.TryGetValue(old, out o)) { decodedBytes -= o.Px.Length; decodedCache.Remove(old); }
            }
        }

        /// decodes the not-yet-cached DDS and TGA files in parallel (PNG and JPG have to go through Unity on the main thread)
        static void Prefetch(IList<string> paths)
        {
            var todo = new List<string>(); var keys = new List<string>();
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || p.StartsWith("embedded:") || todo.Contains(p) || !File.Exists(p)) continue;
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext != ".dds" && ext != ".tga") continue;
                string key = FileKey(p);
                if (key == null || decodedCache.ContainsKey(key)) continue;
                todo.Add(p); keys.Add(key);
            }
            if (todo.Count == 0) return;
            var results = new Decoded[todo.Count];
            int next = -1, size = texMax;
            int threads = Math.Max(1, Math.Min(Environment.ProcessorCount, todo.Count));
            var workers = new System.Threading.Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                workers[t] = new System.Threading.Thread(() =>
                {
                    int i;
                    while ((i = System.Threading.Interlocked.Increment(ref next)) < todo.Count)
                    {
                        try
                        {
                            byte[] data = File.ReadAllBytes(todo[i]);
                            int w, h; byte[] px; string err;
                            if (!ImageDecoder.TryDecode(data, todo[i], out w, out h, out px, out err)) continue;   // reported later by the normal path
                            var d = new Decoded { W = w, H = h, Px = px };
                            int s = SupportedSize(Math.Min(Math.Max(w, h), size));            // the shrunk copy too
                            d.Sq = (w == s && h == s) ? px : Atlas.Resize(new TextureSource { Width = w, Height = h, Rgba = px }, s, s);
                            d.SqSize = s;
                            results[i] = d;
                        }
                        catch (Exception) { }
                    }
                });
                workers[t].IsBackground = true;
                workers[t].Start();
            }
            foreach (var t in workers) t.Join();
            for (int i = 0; i < todo.Count; i++)
            {
                if (results[i] == null || decodedCache.ContainsKey(keys[i])) continue;
                decodedCache[keys[i]] = results[i]; decodedOrder.Add(keys[i]); decodedBytes += results[i].Px.Length;
            }
            TrimDecoded();
        }

        // parsed models, so rebuilding the same file skips parsing
        // (Descs: what an OBJ's material file says, filled in the first time the model is built)
        class ParsedModel { public string Key; public ObjModel Model; public List<ModelMaterial> Mats; public string Warn; public List<MaterialDesc> Descs; }
        static readonly List<ParsedModel> modelCache = new List<ParsedModel>();

        /// keeps a parsed model (the last three files), in place of any older version of the same file
        static void RememberModel(string key, ObjModel model, List<ModelMaterial> mats, string warn)
        {
            string prefix = key.Substring(0, key.IndexOf('|')) + "|";
            modelCache.RemoveAll(c => c.Key.StartsWith(prefix));
            modelCache.Add(new ParsedModel { Key = key, Model = model, Mats = mats, Warn = warn });
            while (modelCache.Count > 3) modelCache.RemoveAt(0);
        }

        // finished atlases (the pixel work), keyed by everything that goes into them
        static readonly Dictionary<string, AtlasResult> atlasCache = new Dictionary<string, AtlasResult>();
        static readonly List<string> atlasOrder = new List<string>();

        // Same contract as the original: a readable square texture of 128, 256 or 512, or null if the file can't be read.
        // Unlike Unity's LoadImage it never hands back Unity's red "?" placeholder for files it can't decode.
        public static Texture2D LoadTexture(Workshop w, string path)
        {
            Decoded d = DecodedFile(path);
            return d == null ? null : TextureFromDecoded(d, Path.GetFileName(path));
        }

        /// the decoded picture in a file, or null (with a note saying why) if it can't be read
        static Decoded DecodedFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string name = Path.GetFileName(path);
            string err;
            Decoded d;
            try { d = GetDecoded(FileKey(path), () => File.ReadAllBytes(path), path, out err); }
            catch (Exception e) { TexNote("could not read " + name + " (" + e.Message + ")", true); return null; }
            if (d == null) { TexNote(name + err, true); return null; }
            return d;
        }

        // The square texture the game gets when one picture is used as it is. Its notes (reduced, stretched) are only
        // true in that case, so pictures that go into an atlas don't come through here.
        static Texture2D TextureFromDecoded(Decoded d, string name)
        {
            int iw = d.W, ih = d.H;
            if (Math.Max(iw, ih) > 512) TexNote(name + " is " + iw + "x" + ih + "px; the game's maximum is 512px, so it was reduced" + (texMax < 512 ? " (to " + texMax + "px, the Texture size setting)" : ""));
            else if (Math.Max(iw, ih) > texMax) TexNote(name + " was reduced to " + texMax + "px (the Texture size setting)");
            if (iw != ih) TexNote(name + " isn't square, so it was stretched");
            int size = SupportedSize(Math.Min(Math.Max(iw, ih), texMax));
            if (d.Sq == null || d.SqSize != size)
            {
                d.Sq = (iw == size && ih == size) ? d.Px : Atlas.Resize(new TextureSource { Width = iw, Height = ih, Rgba = d.Px }, size, size);
                d.SqSize = size;
            }
            return Pixels.Texture(d.Sq, size, size, true);
        }


        /// PNG/JPG (through Unity), DDS or TGA -> RGBA pixels, rows in the same order for every format
        public static bool DecodeImage(byte[] data, string path, out int iw, out int ih, out byte[] px, out string err)
        {
            iw = ih = 0; px = null; err = null;
            switch (ImageDecoder.Detect(data, path))
            {
                case ImageKind.Png:
                case ImageKind.Jpg:
                    {
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!t.LoadImage(data) || t.width <= 8 && t.height <= 8 && data.Length > 2048)
                        {   // the 8x8 check catches Unity's "?" placeholder
                            UnityEngine.Object.Destroy(t); err = " couldn't be decoded"; return false;
                        }
                        px = Pixels.Of(t); iw = t.width; ih = t.height; UnityEngine.Object.Destroy(t);
                        return true;
                    }
                case ImageKind.Dds:
                case ImageKind.Tga:
                    string derr;
                    if (!ImageDecoder.TryDecode(data, path, out iw, out ih, out px, out derr)) { err = ": " + derr; return false; }
                    return true;
                default:
                    err = " isn't a supported image (use PNG, JPG, DDS or TGA)";
                    return false;
            }
        }

        // embedded glTF images seen while loading the current model (for UV cropping of a single embedded texture)
        static Dictionary<string, ModelMaterial> lastEmbedded = new Dictionary<string, ModelMaterial>();
        static string EmbeddedKey(string modelPath, ModelMaterial em)
        {
            string k = FileKey(modelPath);
            return k == null ? null : k + "|" + em.TextureKey + "|" + (em.TextureData != null ? em.TextureData.Length : 0);
        }

        static ModelMaterial embeddedFor(string key) { ModelMaterial m; return key != null && lastEmbedded.TryGetValue(key, out m) ? m : null; }

        /// a texture at its full resolution (file or embedded image)
        static TextureSource LoadFullSource(string modelPath, string path, ModelMaterial embedded)
        {
            try
            {
                string derr;
                var d = GetDecoded(embedded != null ? EmbeddedKey(modelPath, embedded) : FileKey(path),
                                   () => embedded != null ? embedded.TextureData : File.ReadAllBytes(path), embedded != null ? null : path, out derr);
                if (d == null) return null;
                return new TextureSource { Name = embedded != null ? embedded.TextureName : Path.GetFileName(path), Width = d.W, Height = d.H, Rgba = d.Px };
            }
            catch (Exception) { return null; }
        }

        static string ResolveTexture(string texPath, string objPath)
        {
            if (string.IsNullOrEmpty(texPath)) return null;
            if (File.Exists(texPath)) return texPath;
            string alt = Path.Combine(Path.GetDirectoryName(objPath), Path.GetFileName(texPath.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(alt) ? alt : null;
        }

        // Signature matches Workshop.LoadObject(string path, string textureFile, out string err)
        public static GameObject LoadObject(Workshop w, string path, string textureFile, out string err)
        {
            err = string.Empty;
            ShowFlatWarning(null);
            try
            {
                if (!string.IsNullOrEmpty(path) && !File.Exists(path))
                {
                    string moved = Relocate(w, path, ref textureFile);
                    if (moved == null)
                    {
                        err = "model file not found: " + path + ". Open the item again to locate it.";
                        return null;
                    }
                    path = moved;
                    MarkImport();                     // re-pointing the model counts as re-adding it
                }
                return LoadObjectImpl(w, path, textureFile, out err);
            }
            catch (Exception e)
            {
                Debug.Log("[WorkshopPlus] " + e);
                err = e.Message;
                return null;
            }
        }

        static GameObject LoadObjectImpl(Workshop w, string path, string textureFile, out string err)
        {
            err = string.Empty;
            w.SetErrorMessage(string.Empty);
            var notes = new List<string>();
            ObjModel model;
            List<ModelMaterial> modelMats = null;
            string mkey = FileKey(path);
            var cached = mkey == null ? null : modelCache.Find(c => c.Key == mkey);
            if (cached != null) { model = cached.Model; modelMats = cached.Mats; if (cached.Warn != null) notes.Add(cached.Warn); }
            else
            {
                string warn = null;
                if (GltfReader.IsGltf(path)) model = GltfReader.Load(path, out modelMats, out warn);
                else model = ObjParser.Parse(path);
                if (warn != null) notes.Add(warn);
                if (mkey != null) RememberModel(mkey, model, modelMats, warn);
            }
            alwaysNotes.Clear();              // (they are this build's notes; without this the set would only ever grow)
            currentNotes = notes;
            try { return BuildObject(w, path, textureFile, model, modelMats, notes, out err); }
            finally { currentNotes = null; }
        }

        static GameObject BuildObject(Workshop w, string path, string textureFile, ObjModel model, List<ModelMaterial> modelMats, List<string> notes, out string err)
        {
            err = string.Empty;

            var setItem = w.activeItem;
            texMax = ItemSettings.GetTexSize(setItem);
            try { return BuildObjectWith(w, path, textureFile, model, modelMats, notes, setItem, out err); }
            finally { texMax = 512; }
        }

        static GameObject BuildObjectWith(Workshop w, string path, string textureFile, ObjModel model, List<ModelMaterial> modelMats, List<string> notes, WorkshopItem setItem, out string err)
        {
            err = string.Empty;
            var mats = new List<MaterialSource>();
            var textures = new List<TextureSource>();
            var atlasKeyParts = new List<string>();
            Texture2D singleTexture = null; string singleTexturePath = null; bool assignSingle = false;
            // the texture picked with Browse, if any (one texture, as in the original tool)
            string picked = string.IsNullOrEmpty(textureFile) ? null : textureFile;

            Texture2D chosen = null;
            if (picked != null)
            {
                Prefetch(new[] { picked });
                chosen = LoadTexture(w, picked);
                if (chosen == null)
                {   // never show the placeholder: fall back to the model's own materials
                    notes.Add("the chosen texture " + Path.GetFileName(picked) + " couldn't be used, so the model's own materials are shown (press Clear to remove it)");
                    picked = null;
                }
            }
            if (picked != null)
            {
                // One texture chosen in the UI: same behaviour as the original tool (one texture, original UVs).
                Texture2D t = chosen;

                foreach (var n in model.MaterialNames) mats.Add(new MaterialSource { Name = n, TextureSlot = 0 });
                textures.Add(new TextureSource { Width = 1, Height = 1 });
                singleTexture = t; singleTexturePath = picked; assignSingle = true;
                lastEmbedded = new Dictionary<string, ModelMaterial>();     // (not needed here: lets go of the last model's embedded images)
            }
            else
            {
                var descs = new Dictionary<string, MaterialDesc>();
                if (modelMats == null)
                {
                    // An OBJ's material file is read once and kept with the parsed model, so changing shading or atlas
                    // options doesn't read the files again. (Reimport reads it afresh, like the model itself.)
                    ParsedModel kept = modelCache.Find(pm => pm.Model == model);
                    List<MaterialDesc> list = kept != null ? kept.Descs : null;
                    if (list == null)
                    {
                        list = MTLLoader.LoadMaterialDescriptions(path);
                        if (kept != null) kept.Descs = list;
                    }
                    foreach (var d in list)
                        if (d != null && d.name != null) descs[d.name] = d;
                }
                var embedded = new Dictionary<string, ModelMaterial>();     // glTF images stored inside the file
                lastEmbedded = embedded;

                // texture file per material: from the model file itself (glTF) or from the OBJ's MTL file
                var texForMat = new string[model.MaterialNames.Count];
                var mtlTex = new List<string>();
                for (int i = 0; i < model.MaterialNames.Count; i++)
                {
                    if (modelMats != null)
                    {
                        var mm = modelMats[i];
                        if (mm.TexturePath != null) mtlTex.Add(mm.TexturePath);
                        else if (mm.TextureData != null) { string key = "embedded:" + mm.TextureKey; embedded[key] = mm; mtlTex.Add(key); }
                        else
                        {
                            if (mm.TextureName != null) notes.Add("couldn't load the texture " + mm.TextureName);
                            mtlTex.Add(null);
                        }
                        continue;
                    }
                    MaterialDesc d; mtlTex.Add(descs.TryGetValue(model.MaterialNames[i], out d) ? d.diffuseTexture : null);
                }
                for (int i = 0; i < texForMat.Length; i++)
                {
                    texForMat[i] = mtlTex[i] != null && mtlTex[i].StartsWith("embedded:") ? mtlTex[i] : ResolveTexture(mtlTex[i], path);
                    if (texForMat[i] == null && !string.IsNullOrEmpty(mtlTex[i]))
                        Debug.Log("[WorkshopPlus] texture not found: " + mtlTex[i]);
                }

                Prefetch(texForMat);                 // decode DDS/TGA files on all cores at once
                var slotByPath = new Dictionary<string, int>();
                // the decoded pictures, one per texture in use. A Unity texture is only made from one (further down) when
                // it is used as it is; an atlas is put together from the decoded pixels.
                var loaded = new List<Decoded>(); var loadedPaths = new List<string>();
                var loadedNames = new List<string>(); var loadedKeys = new List<string>();
                for (int i = 0; i < model.MaterialNames.Count; i++)
                {
                    string n = model.MaterialNames[i];
                    var ms = new MaterialSource { Name = n };
                    MaterialDesc d;
                    if (modelMats != null) { ms.R = modelMats[i].R; ms.G = modelMats[i].G; ms.B = modelMats[i].B; }
                    else if (descs.TryGetValue(n, out d))
                    {
                        Color c = d.diffuseColor;
                        ms.R = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                        ms.G = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                        ms.B = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                    }
                    string tp = texForMat[i];
                    if (tp != null)
                    {
                        int slot;
                        if (!slotByPath.TryGetValue(tp, out slot))
                        {
                            ModelMaterial em;
                            Decoded dd;
                            string texName;
                            if (embedded.TryGetValue(tp, out em))
                            {
                                texName = em.TextureName ?? "embedded texture";
                                string derr; var ed = em.TextureData;
                                dd = GetDecoded(EmbeddedKey(path, em), () => ed, null, out derr);
                                if (dd == null) TexNote(texName + derr, true);
                            }
                            else { texName = Path.GetFileName(tp); dd = DecodedFile(tp); }
                            if (dd != null)
                            {
                                slot = loaded.Count; loaded.Add(dd); loadedPaths.Add(tp);
                                loadedKeys.Add(em != null ? EmbeddedKey(path, em) : FileKey(tp));
                                loadedNames.Add(texName);
                            }
                            else { slot = -1; notes.Add("could not read " + texName); }
                            // remembered either way: a picture that can't be read is tried and reported once, not once
                            // for every material that uses it
                            slotByPath[tp] = slot;
                        }
                        ms.TextureSlot = slot;
                    }
                    mats.Add(ms);
                }
                bool anyColorOnly = false;
                foreach (var ms in mats) if (ms.TextureSlot < 0) anyColorOnly = true;
                bool facesWithoutMaterial = model.TriMaterial.Contains(-1);
                if (loaded.Count == 1 && !anyColorOnly && !facesWithoutMaterial)
                {
                    singleTexture = TextureFromDecoded(loaded[0], loadedNames[0]); singleTexturePath = loadedPaths[0];
                    // MTL/glTF texture file: remember it like the original tool did (embedded images have no file to remember)
                    assignSingle = !singleTexturePath.StartsWith("embedded:");
                    textures.Add(new TextureSource { Width = singleTexture.width, Height = singleTexture.height });
                }
                else
                {   // atlas: work from the full-resolution images, so cropping and By area sizes keep as much detail as possible
                    for (int i = 0; i < loaded.Count; i++)
                    {
                        atlasKeyParts.Add(loadedKeys[i] ?? loadedPaths[i]);
                        textures.Add(new TextureSource { Name = loadedNames[i], Width = loaded[i].W, Height = loaded[i].H, Rgba = loaded[i].Px });
                    }
                }
            }

            // faces before any usemtl get their own grey material
            int noMat = -1;
            if (model.TriMaterial.Contains(-1) && !(singleTexturePath != null))
            {
                noMat = mats.Count; mats.Add(new MaterialSource { Name = "(none)" });
            }

            AtlasMode atlasMode = ItemSettings.GetAtlas(setItem);
            bool crop = ItemSettings.GetCrop(setItem);
            ShowAtlas(atlasMode, crop, texMax);
            AtlasOptions opts = AtlasStats.Measure(model, mats, textures.Count);
            opts.Mode = atlasMode; opts.Crop = crop;

            // UV cropping with a single texture: replace it with just the part the UVs use (tiling is lost)
            float[] singleWindow = null;
            if (crop && singleTexture != null && textures.Count == 1 && opts.Windows[0] != null)
            {
                TextureSource full = LoadFullSource(path, singleTexturePath, embeddedFor(singleTexturePath));
                if (full != null)
                {
                    float[] used;
                    var part = Atlas.CropToWindow(full, opts.Windows[0], out used);
                    if (part != full)
                    {
                        int size = SupportedSize(Math.Min(Math.Max(part.Width, part.Height), texMax));
                        var sq = Atlas.Resize(part, size, size);
                        UnityEngine.Object.Destroy(singleTexture);
                        singleTexture = ToTexture(sq, size);
                        singleWindow = used;
                        notes.Add("UV cropping uses " + Mathf.RoundToInt(100f * (used[2] - used[0]) * (used[3] - used[1])) + "% of " + Path.GetFileName(singleTexturePath) + ", now " + size + "px");
                    }
                }
            }

            AtlasResult atlas = null;
            string atlasKey = null;
            if (atlasKeyParts.Count == textures.Count && textures.Count > 0 && singleTexture == null)
            {
                var kb = new System.Text.StringBuilder();
                kb.Append((int)opts.Mode).Append('|').Append(opts.Crop ? 1 : 0).Append('|').Append(texMax);
                foreach (var p in atlasKeyParts) kb.Append('|').Append(p);
                foreach (var ms in mats) kb.Append('|').Append(ms.TextureSlot).Append(',').Append(ms.R).Append(',').Append(ms.G).Append(',').Append(ms.B);
                if (opts.Mode == AtlasMode.ByArea || opts.Crop)
                    for (int i = 0; i < textures.Count; i++)
                        kb.Append('|').Append(opts.Weights[i].ToString("R")).Append(',').Append(string.Join(",", Array.ConvertAll(opts.Windows[i], f => f.ToString("R"))));
                atlasKey = kb.ToString();
                atlasCache.TryGetValue(atlasKey, out atlas);
            }
            if (atlas == null)
            {
                atlas = Atlas.Build(mats, textures, texMax, 4, opts);
                if (atlasKey != null && atlas.Rgba != null)
                {
                    atlasCache[atlasKey] = atlas; atlasOrder.Add(atlasKey);
                    while (atlasOrder.Count > 6) { atlasCache.Remove(atlasOrder[0]); atlasOrder.RemoveAt(0); }
                }
            }
            UvRect cr = null;        // with a cropped single texture: where the part that was kept sits in the original picture
            if (singleWindow != null)
            {
                cr = new UvRect { U0 = 0, V0 = 0, U1 = 1, V1 = 1, SU0 = singleWindow[0], SV0 = singleWindow[1], SU1 = singleWindow[2], SV1 = singleWindow[3] };
                for (int i = 0; i < atlas.Rects.Length; i++) if (atlas.Rects[i] != null && atlas.Rects[i].Tile) atlas.Rects[i] = cr;
            }
            // (faces with no material use the cropped texture like the rest: they were measured with it)
            UvRect noRect = noMat >= 0 ? atlas.Rects[noMat]
                          : cr != null ? cr
                          : new UvRect { U0 = 0, V0 = 0, U1 = 1, V1 = 1, Tile = true };
            lastFlatModel = lastModel; lastFlatRects = lastRects; lastFlatNoRect = lastNoRect;     // what the Flat check (if any) was built from
            lastModel = model; lastRects = atlas.Rects; lastNoRect = noRect; lastPath = path; lastFlatCount = -1;
            Shading shading = ItemSettings.Get(setItem);
            float angle = ItemSettings.GetAngle(setItem);
            ShowShading(shading, angle);
            BuiltMesh b = shading == Shading.Flat && lastFlatMesh != null && lastFlatModel == model && SameRects(lastFlatRects, atlas.Rects) && SameRect(lastFlatNoRect, noRect)
                        ? lastFlatMesh : MeshBuilder.Build(model, atlas.Rects, noRect, shading, angle);
            lastFlatMesh = null;
            VertexCounter.SetExact(path, (int)shading, angle, textureFile, crop, b.VertexCount);

            // The shading setting is saved before the model is built with it. A Flat or Auto-Smooth setting that puts
            // the model over the limit would then stop the item from ever opening again, so the model is built once
            // more with Smooth (which needs the fewest vertices); if that fits, the setting is changed back to Smooth
            // and saved, with a note saying so.
            bool smoothIsOverToo = false;
            if (b.VertexCount > VertexLimit && (shading == Shading.Flat || shading == Shading.AutoSmooth))
            {
                BuiltMesh smooth = MeshBuilder.Build(model, atlas.Rects, noRect, Shading.Smooth, angle);
                VertexCounter.SetExact(path, (int)Shading.Smooth, angle, textureFile, crop, smooth.VertexCount);
                if (smooth.VertexCount > VertexLimit) smoothIsOverToo = true;
                else
                {
                    TexNote("the shading was changed back to Smooth, because " + (shading == Shading.Flat ? "Flat" : "Auto-Smooth") + " needs "
                            + b.VertexCount.ToString("N0") + " vertices and the game's limit is " + VertexLimit.ToString("N0"), true);
                    b = smooth; shading = Shading.Smooth;
                    if (setItem != null) ItemSettings.Set(setItem, shading);
                    ShowShading(shading, angle);
                }
            }

            if (b.VertexCount > VertexLimit)
            {
                string hint = smoothIsOverToo ? ""
                            : shading == Shading.Flat ? " Flat shading needs extra vertices; try Auto-Smooth or Smooth."
                            : shading == Shading.AutoSmooth ? " Try a larger Auto-Smooth angle." : "";
                if (singleTexture != null) UnityEngine.Object.Destroy(singleTexture);
                err = "the model has " + b.VertexCount.ToString("N0") + " vertices; the game's limit is "
                      + VertexLimit.ToString("N0") + ". Reduce it in Blender (Decimate modifier) or split it into parts." + hint;
                return null;
            }

            var verts = new Vector3[b.VertexCount]; var uvs = new Vector2[b.VertexCount];
            for (int i = 0; i < b.VertexCount; i++)
            {
                verts[i] = new Vector3(b.Positions[i * 3], b.Positions[i * 3 + 1], b.Positions[i * 3 + 2]);
                uvs[i] = new Vector2(b.Uvs[i * 2], b.Uvs[i * 2 + 1]);
            }
            FreeOldMeshes(w);
            var mesh = new Mesh();
            builtMeshes.Add(mesh);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = b.Triangles;
            var nrm = new Vector3[b.VertexCount];
            for (int i = 0; i < b.VertexCount; i++) nrm[i] = new Vector3(b.Normals[i * 3], b.Normals[i * 3 + 1], b.Normals[i * 3 + 2]);
            mesh.normals = nrm;
            mesh.RecalculateBounds();

            // texture
            if (singleTexturePath != null)
            {
                SetMainTexture(w, w.defaultTexture);
                if (singleTexture != null)
                {
                    if (assignSingle) w.AssignTexture(singleTexturePath, singleTexture);
                    else w.defaultMaterial.mainTexture = singleTexture;
                }
            }
            else SetMainTexture(w, ToTexture(atlas.Rgba, atlas.Size));

            int texCount = 0; foreach (var ms in mats) if (ms.TextureSlot >= 0) texCount = Math.Max(texCount, ms.TextureSlot + 1);
            if (atlas.Rgba != null && texCount > 0)
            {
                int k = (int)Math.Ceiling(Math.Sqrt(texCount + (mats.Exists(m => m.TextureSlot < 0) ? 1 : 0)));
                if (atlas.Layout != null) notes.Add(texCount + " textures share one " + atlas.Size + "px texture by area: " + atlas.Layout);
                else if (k > 1) notes.Add(texCount + " texture" + (texCount == 1 ? "" : "s") + " share one " + atlas.Size + "px texture, so each gets about " + (atlas.Size / k - 8) + "px" + (crop ? " (cropped to the parts the UVs use)" : ""));
            }
            if (b.ClampedUvs > 0)
                notes.Add(b.ClampedUvs + " UVs used texture tiling, which can't be combined with other materials, so they were clamped");
            // texture size / atlas notes only when the model or texture is being (re)imported, not on every rebuild
            bool importing = Importing;
            notes = notes.FindAll(n => importing || alwaysNotes.Contains(n));
            if (notes.Count > 0) w.SetErrorMessage("Note: " + string.Join("; ", notes.ToArray()) + ".");

            Debug.Log("[WorkshopPlus " + Version + "] " + path + ": " + b.VertexCount + " vertices, "
                      + model.TriangleCount + " triangles, shading " + b.Used + (b.Used == Shading.AutoSmooth ? " " + angle + " deg" : "") + ", " + mats.Count + " material(s), texture " + atlas.Size + "px");

            var go = new GameObject();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = w.defaultMaterial;
            return go;
        }

        // The meshes built here. Unity doesn't free a mesh when the object showing it is destroyed, so each build frees
        // the earlier ones that are no longer shown. The one on screen is left alone: the tool goes on showing it if
        // the new model can't be used (and it is freed by the build after that).
        static readonly List<Mesh> builtMeshes = new List<Mesh>();
        static void FreeOldMeshes(Workshop w)
        {
            var shown = new List<Mesh>();
            if (w.objectRoot != null)
                foreach (var f in w.objectRoot.GetComponentsInChildren<MeshFilter>(true)) shown.Add(f.sharedMesh);
            for (int i = builtMeshes.Count - 1; i >= 0; i--)
            {
                if (shown.Contains(builtMeshes[i])) continue;
                if (builtMeshes[i] != null) UnityEngine.Object.Destroy(builtMeshes[i]);
                builtMeshes.RemoveAt(i);
            }
        }

    }

    // (ItemSettings, which remembers each item's shading and other editor settings, is in Settings.cs)
}
