// TTVR Workshop Plus - Steam Workshop preview images as list thumbnails.
// Downloads each published item's preview (the image Workshop.exe uploads when publishing), shrinks it,
// and caches it on disk per item + publish time, so later starts load instantly.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

namespace TTVRPlus
{
    public static class Thumbnails
    {
        const int Size = 128, MaxInFlight = 4;
        const float DownloadTimeout = 30f;      // seconds to wait for Steam before giving a download up

        static readonly Dictionary<ulong, UGCHandle_t> handles = new Dictionary<ulong, UGCHandle_t>();
        static readonly Dictionary<ulong, uint> stamps = new Dictionary<ulong, uint>();
        static readonly Dictionary<ulong, Texture2D> cache = new Dictionary<ulong, Texture2D>();
        static readonly HashSet<ulong> failed = new HashSet<ulong>(), queuedSet = new HashSet<ulong>();
        static readonly List<ulong> queue = new List<ulong>();
        static string dir;
        static bool pumping;
        public static int Version;

        // one download in progress
        class Request { public ulong Id; public CallResult<RemoteStorageDownloadUGCResult_t> Call; public float Started; public bool Done; }

        public static void Init(string devDir) { dir = Path.Combine(devDir, "workshopplus_thumbs"); }

        public static void SetSource(ulong id, UGCHandle_t preview, uint updated)
        {
            if (id == 0 || preview == UGCHandle_t.Invalid) return;
            handles[id] = preview; stamps[id] = updated;
        }

        static string FileFor(ulong id)
        {
            uint s; if (dir == null || !stamps.TryGetValue(id, out s)) return null;
            return Path.Combine(dir, id + "_" + s + ".png");
        }

        /// thumbnail if ready (null otherwise; it is then fetched in the background)
        public static Texture2D Get(ulong id)
        {
            if (id == 0) return null;
            Texture2D t;
            if (cache.TryGetValue(id, out t)) return t;
            if (failed.Contains(id)) return null;
            string f = FileFor(id);
            if (f != null && FileCache.Exists(f))
            {
                try
                {
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(File.ReadAllBytes(f))) { t.wrapMode = TextureWrapMode.Clamp; cache[id] = t; return t; }
                    UnityEngine.Object.Destroy(t);
                }
                catch (Exception) { }
                // a damaged cache file: remove it so it isn't read again for every row, and fetch the picture from Steam again below.
                // If it can't be removed, give this item up until the next start.
                try { File.Delete(f); FileCache.Forget(f); }
                catch (Exception) { failed.Add(id); return null; }
            }
            if (!queuedSet.Contains(id) && handles.ContainsKey(id)) { queuedSet.Add(id); queue.Add(id); }
            return null;
        }

        // ---- local image files (a world's preview.jpg, an unpublished item's custom thumbnail), loaded a few per frame
        static readonly Dictionary<string, Texture2D> local = new Dictionary<string, Texture2D>();
        static readonly List<string> localQueue = new List<string>();
        static readonly HashSet<string> localQueued = new HashSet<string>(), localFailed = new HashSet<string>();

        static string LocalKey(string path)
        {
            return path + "|" + FileCache.Ticks(path);
        }

        public static Texture2D GetLocal(string path)
        {
            if (string.IsNullOrEmpty(path) || !FileCache.Exists(path)) return null;
            string key = LocalKey(path);
            if (key == null || localFailed.Contains(key)) return null;
            Texture2D t;
            if (local.TryGetValue(key, out t)) return t;
            if (!localQueued.Contains(key)) { localQueued.Add(key); localQueue.Add(key); }
            return null;
        }

        /// from the list follower: load up to 2 queued local images per frame
        public static void PumpLocal()
        {
            for (int n = 0; n < 2 && localQueue.Count > 0; n++)
            {
                string key = localQueue[0]; localQueue.RemoveAt(0); localQueued.Remove(key);
                string path = key.Substring(0, key.LastIndexOf('|'));
                try
                {
                    var t = Shrink(File.ReadAllBytes(path));
                    if (t == null) { localFailed.Add(key); continue; }
                    // the same file was shown before and has changed since: its older picture is no longer needed
                    string samePath = path + "|";
                    var stale = new List<string>();
                    foreach (var k in local.Keys) if (k != key && k.StartsWith(samePath)) stale.Add(k);
                    foreach (var k in stale) { UnityEngine.Object.Destroy(local[k]); local.Remove(k); }
                    local[key] = t; Version++;
                }
                catch (Exception) { localFailed.Add(key); }
            }
        }

        static Texture2D Shrink(byte[] bytes)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(bytes)) { UnityEngine.Object.Destroy(src); return null; }
            int w = src.width, h = src.height;
            int tw = w >= h ? Size : Mathf.Max(1, Size * w / h), th = h >= w ? Size : Mathf.Max(1, Size * h / w);
            byte[] raw = Pixels.Of(src);
            UnityEngine.Object.Destroy(src);
            byte[] small = Atlas.Resize(new TextureSource { Width = w, Height = h, Rgba = raw }, tw, th);
            var t = Pixels.Texture(small, tw, th, false, true);
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        public static void StartPump(MonoBehaviour host)
        {
            if (pumping) return;
            pumping = true;
            host.StartCoroutine(Pump());
        }

        static IEnumerator Pump()
        {
            var inFlight = new List<Request>();
            while (true)
            {
                while (inFlight.Count < MaxInFlight && queue.Count > 0 && SteamManager.Initialized)
                {
                    ulong id = queue[0]; queue.RemoveAt(0);
                    UGCHandle_t h = handles[id];
                    var req = new Request { Id = id, Started = Time.realtimeSinceStartup };
                    req.Call = CallResult<RemoteStorageDownloadUGCResult_t>.Create(
                        delegate (RemoteStorageDownloadUGCResult_t r, bool io)
                        {
                            req.Done = true;            // (disposed by the loop below, not here inside its own callback)
                            try
                            {
                                if (io || r.m_eResult != EResult.k_EResultOK || r.m_nSizeInBytes <= 0) { failed.Add(id); return; }
                                var bytes = new byte[r.m_nSizeInBytes];
                                SteamRemoteStorage.UGCRead(r.m_hFile, bytes, r.m_nSizeInBytes, 0u, EUGCReadAction.k_EUGCRead_Close);
                                Store(id, bytes);
                            }
                            catch (Exception e) { failed.Add(id); Debug.Log("[WorkshopPlus] thumbnail " + id + ": " + e.Message); }
                        });
                    req.Call.Set(SteamRemoteStorage.UGCDownload(h, 0u));
                    inFlight.Add(req);
                }
                // (no SteamAPI.RunCallbacks() here: the tool's SteamManager already runs it every frame)
                // finished downloads are cleared away; so are ones Steam never answered (it went away), otherwise
                // they would fill every slot and no thumbnail would load again
                for (int i = inFlight.Count - 1; i >= 0; i--)
                {
                    var req = inFlight[i];
                    bool timedOut = !req.Done && Time.realtimeSinceStartup - req.Started > DownloadTimeout;
                    if (!req.Done && !timedOut) continue;
                    req.Call.Dispose();
                    inFlight.RemoveAt(i);
                    if (timedOut) queuedSet.Remove(req.Id);     // may be asked for again the next time its row is shown
                }
                yield return null;
            }
        }

        static void Store(ulong id, byte[] bytes)
        {
            var t = Shrink(bytes);
            if (t == null) { failed.Add(id); return; }
            cache[id] = t;
            Version++;
            try
            {
                string f = FileFor(id);
                if (f != null)
                {
                    Directory.CreateDirectory(dir);
                    foreach (var old in Directory.GetFiles(dir, id + "_*.png")) if (old != f) File.Delete(old);   // older publishes
                    File.WriteAllBytes(f, t.EncodeToPNG());
                }
            }
            catch (Exception) { }
        }

        /// after a publish: show the picture that was just uploaded (JPG or PNG bytes) for this item straight away.
        /// Only kept in memory: the next start downloads it from Steam under its new publish time, as usual.
        public static void SetPublished(ulong id, byte[] preview)
        {
            if (id == 0 || preview == null) return;
            try
            {
                var t = Shrink(preview);
                if (t == null) return;
                Texture2D before;
                if (cache.TryGetValue(id, out before) && before != null) UnityEngine.Object.Destroy(before);
                cache[id] = t; failed.Remove(id);
                Version++;                              // the list rows pick the new picture up
            }
            catch (Exception e) { Debug.Log("[WorkshopPlus] thumbnail " + id + ": " + e.Message); }
        }

        /// an item was deleted: remove its cached thumbnail files (any publish time)
        public static void DeleteFiles(ulong id)
        {
            if (id == 0 || dir == null) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, id + "_*.png")) File.Delete(f);
            }
            catch (Exception) { }
        }
    }
}
