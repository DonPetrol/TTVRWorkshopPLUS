// TTVR Workshop Plus - reading the game's own models and textures straight from its files, for the Asset Browser
// browser (AssetBrowser.cs). Engine-independent: compiles for the tool's Mono (.NET 3.5) and for modern .NET (tests).
//
// Where the game keeps them (Tiny Town VR_Data, next to the tool's own Workshop_Data):
//   - resources.assets: every built-in model as a "TextAsset" - a block of bytes in the very same format the tool
//     writes for a Workshop item (data.bytes: a SerializableMeshData) - and one more, "MeshList", the game's list of
//     models (name, category, display name, and which DLC pack, if any, the model comes from).
//   - sharedassets0.assets: the textures, as four "texture arrays" (stacks of square pictures, DXT1-compressed):
//     Shared_256, Shared_512, Shared_1024 and Shared_Characters. A model's materialIndex (0 to 3) says which stack,
//     and the alpha of its vertex colour which picture in it (alpha x 256). Many models share one picture (an atlas).
//   - the DLC packs (fantasy, military, zombie, in the game folder) are ordinary Unity asset bundles with the same
//     four stacks of their own; those are opened with Unity itself (AssetBrowser.cs), not here.
// Both .assets files are Unity 2017.3 "serialized files" (format 17) without type information, so the two kinds of
// object needed are read by their known layout. Anything unexpected throws, with a message saying what.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TTVRPlus
{
    public class AssetFile
    {
        public const int TextAssetClass = 49, Texture2DArrayClass = 187;
        public struct Entry { public long Start; public int Size; public int ClassId; }
        public string Path;
        public List<Entry> Objects = new List<Entry>();

        /// reads the file's table of contents (only its first part is read: the objects themselves are read on demand)
        public static AssetFile Open(string path)
        {
            var f = new AssetFile { Path = path };
            using (var s = File.OpenRead(path))
            {
                var head = ReadExactly(s, 20);
                uint metaSize = BigEndian(head, 0), version = BigEndian(head, 8), dataOffset = BigEndian(head, 12);
                if (version != 17 || head[16] != 0) throw new Exception(System.IO.Path.GetFileName(path) + " is not in the expected format (" + version + "); the game may have been updated");
                var meta = ReadExactly(s, (int)metaSize);
                var r = new Reader(meta, 0);
                r.CString();                        // the Unity version (2017.3.0p3)
                r.Int();                            // the platform
                if (r.Byte() != 0) throw new Exception(System.IO.Path.GetFileName(path) + " has type information (not expected)");
                int types = r.Int();
                var classes = new int[types];
                for (int i = 0; i < types; i++)
                {
                    int id = r.Int();
                    r.Skip(1 + 2);                  // stripped?, script type index
                    if (id < 0 || id == 114) r.Skip(16);
                    r.Skip(16);                     // the type's hash
                    classes[i] = id;
                }
                int count = r.Int();
                for (int i = 0; i < count; i++)
                {
                    r.Align();
                    long pathId = r.Long();
                    uint start = r.UInt(); int size = r.Int(); int type = r.Int();
                    f.Objects.Add(new Entry { Start = dataOffset + start, Size = size, ClassId = type >= 0 && type < types ? classes[type] : -1 });
                }
            }
            return f;
        }

        public struct Blob { public long Offset; public int Length; }

        /// every TextAsset in the file: its name, and where its bytes are
        public Dictionary<string, Blob> TextAssets()
        {
            var list = new Dictionary<string, Blob>();
            using (var s = File.OpenRead(Path))
                foreach (var o in Objects)
                {
                    if (o.ClassId != TextAssetClass) continue;
                    s.Position = o.Start;
                    var b = ReadExactly(s, Math.Min(o.Size, 4 + 256 + 8));
                    var r = new Reader(b, 0);
                    string name = r.String();
                    int length = r.Int();
                    if (length < 0 || length > o.Size) continue;
                    list[name] = new Blob { Offset = o.Start + r.Position, Length = length };
                }
            return list;
        }

        public class TextureArray
        {
            public string Name; public int Width, Height, Depth, Format, Mips; public long DataOffset; public long DataSize;
            /// the bytes of one picture's full size (mip 0) in the stack, still compressed
            public long LayerOffset(int layer) { return DataOffset + layer * (DataSize / Depth); }
            public int LayerBytes { get { return Dxt.Size(Width, Height, Format); } }
        }

        /// every texture array in the file (name, size, format, where its pictures are)
        public List<TextureArray> TextureArrays()
        {
            var list = new List<TextureArray>();
            using (var s = File.OpenRead(Path))
                foreach (var o in Objects)
                {
                    if (o.ClassId != Texture2DArrayClass) continue;
                    s.Position = o.Start;
                    var r = new Reader(ReadExactly(s, Math.Min(o.Size, 512)), 0);
                    var t = new TextureArray { Name = r.String() };
                    r.Int(); r.Byte(); r.Align();                                   // fallback format, downscale fallback
                    t.Width = r.Int(); t.Height = r.Int(); t.Depth = r.Int(); t.Format = r.Int(); t.Mips = r.Int();
                    t.DataSize = r.UInt();
                    r.Skip(6 * 4);                                                  // filter, aniso, mip bias, wrap U/V/W
                    r.Int(); r.Byte(); r.Align();                                   // colour space, readable
                    long length = r.UInt();
                    if (length != t.DataSize || t.Depth <= 0) throw new Exception("the texture " + t.Name + " is not laid out as expected");
                    t.DataOffset = o.Start + r.Position;
                    list.Add(t);
                }
            return list;
        }

        public static byte[] ReadBytes(string path, long offset, int length)
        {
            using (var s = File.OpenRead(path)) { s.Position = offset; return ReadExactly(s, length); }
        }

        static byte[] ReadExactly(Stream s, int n)
        {
            var b = new byte[n]; int got = 0;
            while (got < n) { int k = s.Read(b, got, n - got); if (k <= 0) throw new EndOfStreamException(); got += k; }
            return b;
        }
        static uint BigEndian(byte[] b, int p) { return (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); }

        /// little-endian reading, with Unity's 4-byte alignment (counted from the start of the object, which is itself aligned)
        class Reader
        {
            readonly byte[] b; int p;
            public Reader(byte[] bytes, int at) { b = bytes; p = at; }
            public int Position { get { return p; } }
            public byte Byte() { return b[p++]; }
            public int Int() { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
            public uint UInt() { uint v = BitConverter.ToUInt32(b, p); p += 4; return v; }
            public long Long() { long v = BitConverter.ToInt64(b, p); p += 8; return v; }
            public void Skip(int n) { p += n; }
            public void Align() { p = (p + 3) & ~3; }
            public string String() { int n = Int(); string v = Encoding.UTF8.GetString(b, p, n); p += n; Align(); return v; }
            public string CString() { int e = Array.IndexOf(b, (byte)0, p); string v = Encoding.UTF8.GetString(b, p, e - p); p = e + 1; return v; }
        }
    }

    /// DXT1 (format 10) and DXT5 (format 12) pictures to plain RGBA, rows bottom first as Unity keeps them
    public static class Dxt
    {
        public const int DXT1 = 10, DXT5 = 12;
        public static bool Supported(int format) { return format == DXT1 || format == DXT5; }
        public static int Size(int w, int h, int format) { return ((w + 3) / 4) * ((h + 3) / 4) * (format == DXT1 ? 8 : 16); }

        public static byte[] Decode(byte[] src, int at, int w, int h, int format)
        {
            if (!Supported(format)) throw new Exception("texture format " + format + " is not supported");
            var rgba = new byte[w * h * 4];
            var cols = new byte[16]; var alpha = new byte[8];
            int p = at;
            for (int by = 0; by < h; by += 4)
                for (int bx = 0; bx < w; bx += 4)
                {
                    ulong alphaBits = 0;
                    if (format == DXT5)
                    {
                        alpha[0] = src[p]; alpha[1] = src[p + 1];
                        if (alpha[0] > alpha[1]) for (int i = 1; i < 7; i++) alpha[i + 1] = (byte)(((7 - i) * alpha[0] + i * alpha[1]) / 7);
                        else { for (int i = 1; i < 5; i++) alpha[i + 1] = (byte)(((5 - i) * alpha[0] + i * alpha[1]) / 5); alpha[6] = 0; alpha[7] = 255; }
                        for (int i = 0; i < 6; i++) alphaBits |= (ulong)src[p + 2 + i] << (8 * i);
                        p += 8;
                    }
                    int c0 = src[p] | src[p + 1] << 8, c1 = src[p + 2] | src[p + 3] << 8;
                    uint bits = (uint)(src[p + 4] | src[p + 5] << 8 | src[p + 6] << 16 | src[p + 7] << 24);
                    p += 8;
                    Rgb565(c0, cols, 0); Rgb565(c1, cols, 4);
                    bool four = c0 > c1 || format == DXT5;
                    for (int k = 0; k < 3; k++)
                    {
                        int a = cols[k], b = cols[4 + k];
                        cols[8 + k] = (byte)(four ? (2 * a + b) / 3 : (a + b) / 2);
                        cols[12 + k] = (byte)(four ? (a + 2 * b) / 3 : 0);
                    }
                    cols[3] = cols[7] = cols[11] = 255; cols[15] = (byte)(four ? 255 : 0);
                    for (int j = 0; j < 16; j++)
                    {
                        int x = bx + (j & 3), y = by + (j >> 2);
                        if (x >= w || y >= h) continue;
                        int c = (int)((bits >> (2 * j)) & 3) * 4, q = (y * w + x) * 4;
                        rgba[q] = cols[c]; rgba[q + 1] = cols[c + 1]; rgba[q + 2] = cols[c + 2];
                        rgba[q + 3] = format == DXT5 ? alpha[(int)((alphaBits >> (3 * j)) & 7)] : cols[c + 3];
                    }
                }
            return rgba;
        }

        static void Rgb565(int c, byte[] o, int at)
        {
            o[at] = (byte)(((c >> 11) & 31) * 255 / 31);
            o[at + 1] = (byte)(((c >> 5) & 63) * 255 / 63);
            o[at + 2] = (byte)((c & 31) * 255 / 31);
        }
    }

    /// RGBA pixels (rows bottom first, as Unity keeps them) as a PNG file. Written here rather than with Unity's own
    /// EncodeToPNG so that it can run away from the main thread (the window stays responsive while exporting).
    public static class Png
    {
        static uint[] crcTable;

        public static byte[] Encode(byte[] rgba, int w, int h)
        {
            // the picture: each row (top first) with a filter byte in front (0: none)
            var raw = new byte[(w * 4 + 1) * h];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w * 4, dst = y * (w * 4 + 1);
                raw[dst] = 0;
                Buffer.BlockCopy(rgba, src, raw, dst + 1, w * 4);
            }
            byte[] packed;
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);                     // zlib header
                using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true)) z.Write(raw, 0, raw.Length);
                uint adler = Adler32(raw);
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                packed = ms.ToArray();
            }
            using (var o = new MemoryStream())
            {
                o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var head = new byte[13];
                Put(head, 0, (uint)w); Put(head, 4, (uint)h);
                head[8] = 8; head[9] = 6; head[10] = 0; head[11] = 0; head[12] = 0;     // 8 bits, RGBA, deflate, no filter set, no interlace
                Chunk(o, "IHDR", head);
                Chunk(o, "IDAT", packed);
                Chunk(o, "IEND", new byte[0]);
                return o.ToArray();
            }
        }

        static void Put(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        static void Chunk(Stream o, string type, byte[] data)
        {
            var len = new byte[4]; Put(len, 0, (uint)data.Length); o.Write(len, 0, 4);
            var t = Encoding.ASCII.GetBytes(type); o.Write(t, 0, 4);
            o.Write(data, 0, data.Length);
            uint crc = Crc(Crc(0xFFFFFFFFu, t), data) ^ 0xFFFFFFFFu;
            var c = new byte[4]; Put(c, 0, crc); o.Write(c, 0, 4);
        }

        static uint Crc(uint crc, byte[] data)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; }
                crcTable = table;
            }
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }
    }

    /// one line of the game's model list (MeshList)
    public class GameModel
    {
        public string Name, Category, Filter, Display;
        public int Pack = -1;                       // -1: the game itself; 0, 1, 2: the fantasy, military and zombie packs
        public static readonly string[] PackFiles = { "fantasy", "military", "zombie" };
        public static readonly string[] PackNames = { "Fantasy pack", "Military pack", "Zombie pack" };
        public string PackName { get { return Pack < 0 ? "Base game" : PackNames[Pack]; } }
        /// a name for its files: the model's own, with the pack's added for a pack's model (names can repeat across packs)
        public string FileName { get { return Safe(Pack < 0 ? Name : Name + "_" + PackFiles[Pack]); } }
        public static string Safe(string s)
        {
            var b = new StringBuilder(s.Length);
            foreach (char c in s) b.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ' ? '_' : c);
            return b.ToString();
        }

        /// the list: ID,Name,Category,Filter,Ignore,Display Name,Rotation,SnapBoost,AssetBundle,BoundsOffset
        public static List<GameModel> ParseList(string text)
        {
            var list = new List<GameModel>();
            var lines = text.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].TrimEnd('\r').Split(',');
                if (c.Length < 9 || c[1].Length == 0) continue;
                int pack;
                if (!int.TryParse(c[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out pack) || pack >= PackFiles.Length) pack = -1;
                list.Add(new GameModel { Name = c[1], Category = c[2], Filter = c[3], Display = c[5].Length > 0 ? c[5] : c[1], Pack = pack < 0 ? -1 : pack });
            }
            return list;
        }
    }

    /// a model as an OBJ file (with its MTL), the way 3D programs expect: Unity's left-handed positions are mirrored
    /// in X (and the triangles turned round) so the model is the right way round in Blender and the like
    public static class ObjWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(string objPath, string name, float[] v, float[] n, float[] uv, int[] tris, string texture)
        {
            string dir = Path.GetDirectoryName(objPath), baseName = Path.GetFileNameWithoutExtension(objPath);
            string mtl = baseName + ".mtl";
            var s = new StringBuilder(v.Length * 40);
            s.Append("# ").Append(name).Append(" - exported from Tiny Town VR by Workshop Plus (for your own use)\n");
            s.Append("mtllib ").Append(mtl).Append('\n').Append("o ").Append(baseName).Append('\n');
            int count = v.Length / 3;
            for (int i = 0; i < count; i++) s.Append("v ").Append(F(-v[i * 3])).Append(' ').Append(F(v[i * 3 + 1])).Append(' ').Append(F(v[i * 3 + 2])).Append('\n');
            bool hasUv = uv != null && uv.Length >= count * 2, hasN = n != null && n.Length >= count * 3;
            if (hasUv) for (int i = 0; i < count; i++) s.Append("vt ").Append(F(uv[i * 2])).Append(' ').Append(F(uv[i * 2 + 1])).Append('\n');
            if (hasN) for (int i = 0; i < count; i++) s.Append("vn ").Append(F(-n[i * 3])).Append(' ').Append(F(n[i * 3 + 1])).Append(' ').Append(F(n[i * 3 + 2])).Append('\n');
            s.Append("usemtl ").Append(baseName).Append('\n');
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                s.Append('f');
                foreach (int k in new[] { tris[t], tris[t + 2], tris[t + 1] })        // (turned round, for the mirroring)
                {
                    int i = k + 1;
                    s.Append(' ').Append(i);
                    if (hasUv || hasN) s.Append('/').Append(hasUv ? i.ToString(Inv) : "");
                    if (hasN) s.Append('/').Append(i);
                }
                s.Append('\n');
            }
            File.WriteAllText(objPath, s.ToString());
            var m = new StringBuilder();
            m.Append("newmtl ").Append(baseName).Append("\nKa 0 0 0\nKd 1 1 1\nKs 0 0 0\nd 1\nillum 1\n");
            if (!string.IsNullOrEmpty(texture)) m.Append("map_Kd ").Append(texture).Append('\n');
            File.WriteAllText(Path.Combine(dir, mtl), m.ToString());
        }
        static string F(float f) { return f.ToString("0.######", Inv); }
    }
}
