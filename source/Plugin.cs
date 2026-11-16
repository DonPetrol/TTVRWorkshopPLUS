// TTVR Workshop Plus - the BepInEx plugin: where Workshop Plus is joined to the Workshop tool.
// Nothing of the tool's is replaced on disk. When the tool starts, BepInEx loads this plugin, and the routines of the
// tool that Workshop Plus takes over are patched in memory (with Harmony) to call Workshop Plus instead:
//   - a "prefix" runs before one of the tool's routines, and can stand in for it (by returning false);
//   - a "transpiler" changes a routine's instructions as it is loaded - used here only to change a few words the tool
//     shows in its file dialogs, and to send a few calls inside a routine somewhere else.
// The list in Join below is the whole join: each line names the tool's routine and what it now does.
// The plugin only loads in Workshop.exe (the BepInProcess line); the game itself is not touched.
//
// The rest of the source reads the tool's private parts directly (w.defaultMaterial and so on). It is compiled
// against a copy of the tool's code in which those are marked public; the line below ("SkipVerification") is what
// has the tool's runtime allow that with the tool's real, unchanged code.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Tacticsoft;

[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace TTVRPlus
{
    [BepInPlugin("donpetrol.workshopplus", "Tiny Town Workshop Plus", WorkshopPlus.Version)]
    [BepInProcess("Workshop.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        static readonly Type W = typeof(Workshop), Me = typeof(Plugin);
        static Harmony harmony;

        void Awake()
        {
            Log = Logger;
            harmony = new Harmony("donpetrol.workshopplus");
            try { Join(); }
            catch (Exception e) { failed++; Log.LogError("joining to the tool: " + e); }

            // The replacements lean on each other (the list expects Workshop Plus's start-up, and so on), so a tool
            // that is only partly joined would misbehave. If anything failed, everything is undone and the tool
            // runs exactly as it does without Workshop Plus.
            if (failed > 0)
            {
                try { harmony.UnpatchSelf(); }
                catch (Exception e) { Log.LogError("undoing the patches: " + e); }
                Log.LogError("Workshop Plus " + WorkshopPlus.Version + " is NOT active: " + failed + " of its patches could not be made (see the errors above; "
                             + "the tool may have been updated). The Workshop tool runs as the original.");
                return;
            }
            Log.LogInfo("Workshop Plus " + WorkshopPlus.Version + " joined to the Workshop tool (" + joined + " patches)");
        }

        void Join()
        {
            // ---- the tool's routines that Workshop Plus stands in for
            Instead(W, "Start", "Start");                                   // the window first, item details fetched in parallel
            Instead(W, "LoadObject", "LoadObject");                         // OBJ, glTF and GLB models; the editor's extra controls
            Instead(W, "LoadTexture", "LoadTexture");                       // DDS and TGA too
            Instead(W, "ActivateListView", "ActivateListView");             // the list: search, sort, "Last Published"
            Instead(typeof(DynamicHeightTableViewController), "GetCellForRowInTableView", "GetCell");
            Instead(W, "OnPublishButtonClicked", "OnPublishButtonClicked"); // publishing without freezing the window
            Instead(typeof(AverageNormalCalculator), "ComputeAverageNormals", "ComputeAverageNormals");
            Instead(W, "SetErrorMessage", "SetErrorMessage");               // errors red, notes amber
            Instead(W, "UpdateHelpText", "UpdateHelpText");                 // the Help page

            // ---- routines that run as they were, with something of Workshop Plus's first
            Before(W, "OnDeleteItemConfirmed", "OnDeleteItemConfirmed");    // deleting several items uses the same "are you sure"
            Before(W, "OnDeleteItemCancelled", "OnDeleteItemCancelled");
            foreach (string name in new[] { "OnNewButtonClicked", "OnObjFileBrowseButtonClick", "OnTextureBrowseButtonClick", "OnTextureClearButtonClick" })
                Before(W, name, "MarkImport");                              // (so that texture size notes only show after an import)
            // on the Worlds tab, + shows the game's saved worlds as pictures to pick from (WorldPicker.cs); its "Select File..." runs the tool's own +
            Before(W, "OnNewButtonClicked", "NewButton");
            // a world's preview picture has to be a PNG or JPG (the texture dialog below also offers DDS and TGA)
            Before(W, "OnTextureBrowseButtonClick", "RememberPreview");
            Before(W, "LoadPreviewImageIntoSquareTexture", "LoadPreviewImageIntoSquareTexture");

            // ---- words in the file dialogs, and a few calls sent elsewhere
            Words(W, "OnTextureBrowseButtonClick", "JPG and PNG Files", "Image Files (PNG, JPG, DDS, TGA) ", "jpg", "jpg; *.jpeg; *.dds; *.tga");
            foreach (string name in new[] { "OnNewButtonClicked", "OnObjFileBrowseButtonClick" })
                Words(W, name, "OBJ Files", "3D Models (OBJ, glTF, GLB) ", "obj", "obj; *.gltf; *.glb");
            // choosing a texture for an item that already has one builds the model again, rather than swapping the picture;
            // and where the tool builds the model again itself, Workshop Plus does it (so that Flip X is kept)
            MethodInfo rebuild = AccessTools.Method(typeof(WorkshopPlus), "Rebuild");
            Call(W, "OnTextureBrowseButtonClick", "AssignTexture", AccessTools.Method(typeof(WorkshopPlus), "BrowseAssignTexture"), "ActivateDetailView", rebuild);
            Call(W, "OnTextureClearButtonClick", "ActivateDetailView", rebuild);
            // publishing asks ThumbPicker for the thumbnail (it may be one you chose)
            Call(W, "GenerateObjectData", "GenerateThumbnail", AccessTools.Method(typeof(ThumbPicker), "GenerateThumbnail"));
        }

        // ---- the patches themselves
        static int joined, failed;

        static MethodInfo Find(Type type, string name)
        {
            MethodInfo m = null;
            try { m = AccessTools.Method(type, name); }
            catch (Exception e) { Log.LogError(type.Name + "." + name + ": " + e.Message); }      // (more than one routine of that name)
            if (m == null) { failed++; Log.LogError("not found in the tool: " + type.Name + "." + name); }
            return m;
        }

        // the tool's routine is not run: the routine of that name in the "Routines" list below is, in its place
        static void Instead(Type type, string name, string mine) { Patch(type, name, mine); }
        // the tool's routine runs after ours (unless ours says not to)
        static void Before(Type type, string name, string mine) { Patch(type, name, mine); }
        // (both are the same kind of patch: which of the two it is depends on what the routine in "Routines" gives back)
        static void Patch(Type type, string name, string mine)
        {
            MethodInfo m = Find(type, name);
            if (m == null) return;
            try { harmony.Patch(m, new HarmonyMethod(typeof(Routines), mine)); joined++; }
            catch (Exception e) { failed++; Log.LogError(type.Name + "." + name + ": " + e); }
        }

        // words to change in a routine: pairs of (the tool's, ours)
        static readonly Dictionary<MethodBase, string[]> words = new Dictionary<MethodBase, string[]>();
        static void Words(Type type, string name, params string[] pairs)
        {
            MethodInfo m = Find(type, name);
            if (m == null) return;
            words[m] = pairs;
            try { harmony.Patch(m, null, null, new HarmonyMethod(Me, "ChangeWords")); joined++; }
            catch (Exception e) { failed++; Log.LogError(type.Name + "." + name + ": " + e); }
        }
        static IEnumerable<CodeInstruction> ChangeWords(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            string[] pairs = words[original]; int changed = 0;
            foreach (CodeInstruction i in instructions)
            {
                if (i.opcode == OpCodes.Ldstr)
                    for (int p = 0; p < pairs.Length; p += 2)
                        if ((string)i.operand == pairs[p]) { i.operand = pairs[p + 1]; changed++; break; }
                yield return i;
            }
            if (changed != pairs.Length / 2) Log.LogWarning(original.Name + ": " + changed + " of " + pairs.Length / 2 + " words found to change");
        }

        // calls inside a routine sent elsewhere: pairs of (the name of what the tool calls, what is called in its place -
        // which takes the object the call was made on as its first input, then the same inputs)
        static readonly Dictionary<MethodBase, object[]> calls = new Dictionary<MethodBase, object[]>();
        static void Call(Type type, string name, params object[] pairs)
        {
            MethodInfo m = Find(type, name);
            if (m == null) return;
            for (int p = 0; p < pairs.Length; p += 2)
                if (!(pairs[p + 1] is MethodInfo)) { failed++; Log.LogError("no replacement for the call to " + pairs[p] + " in " + name); return; }
            calls[m] = pairs;
            try { harmony.Patch(m, null, null, new HarmonyMethod(Me, "ChangeCall")); joined++; }
            catch (Exception e) { failed++; Log.LogError(type.Name + "." + name + ": " + e); }
        }
        static IEnumerable<CodeInstruction> ChangeCall(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            object[] pairs = calls[original]; int changed = 0;
            foreach (CodeInstruction i in instructions)
            {
                var method = i.operand as MethodInfo;
                if ((i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && method != null)
                    for (int p = 0; p < pairs.Length; p += 2)
                        if (method.Name == (string)pairs[p]) { i.opcode = OpCodes.Call; i.operand = pairs[p + 1]; changed++; break; }
                yield return i;
            }
            if (changed != pairs.Length / 2) Log.LogWarning(original.Name + ": " + changed + " calls changed (expected " + pairs.Length / 2 + ")");
        }
    }

    /// What runs before (or in place of) the tool's routines: each hands over to Workshop Plus. Returning false means
    /// "the tool's own routine is not to run"; `__instance` is the tool's object, `__result` what the routine gives back.
    static class Routines
    {
        static bool Start(Workshop __instance) { WorkshopPlus.Start(__instance); return false; }
        static bool LoadObject(Workshop __instance, string path, string textureFile, ref string err, ref GameObject __result)
        {
            __result = WorkshopPlus.LoadObject(__instance, path, textureFile, out err);
            return false;
        }
        static bool LoadTexture(Workshop __instance, string path, ref Texture2D __result) { __result = WorkshopPlus.LoadTexture(__instance, path); return false; }
        static bool ActivateListView(Workshop __instance) { WorkshopList.ActivateListView(__instance); return false; }
        static bool GetCell(DynamicHeightTableViewController __instance, TableView tableView, int row, ref TableViewCell __result)
        {
            __result = WorkshopList.GetCell(__instance, tableView, row);
            return false;
        }
        static bool OnPublishButtonClicked(Workshop __instance) { Publisher.OnPublishButtonClicked(__instance); return false; }
        static bool ComputeAverageNormals(Mesh mesh, float textureCoordinate, ref Color[] __result) { __result = Publisher.ComputeAverageNormals(mesh, textureCoordinate); return false; }
        static bool SetErrorMessage(Workshop __instance, string err) { WorkshopPlus.SetMessage(__instance, err); return false; }
        static bool UpdateHelpText(Workshop __instance) { Help.UpdateHelpText(__instance); return false; }

        static bool OnDeleteItemConfirmed(Workshop __instance) { return !WorkshopList.OnDeleteConfirmed(__instance); }     // (true from Workshop Plus: it has dealt with it)
        static void OnDeleteItemCancelled(Workshop __instance) { WorkshopList.OnDeleteCancelled(__instance); }
        static void MarkImport() { WorkshopPlus.MarkImport(); }
        static bool NewButton(Workshop __instance) { return !WorldPicker.OnNew(__instance); }      // (true from the picker: it has dealt with it)
        static void RememberPreview(Workshop __instance) { WorkshopPlus.RememberPreview(__instance); }
        // the tool's routine still runs, but for a picture it can't read it is given the one the world had before
        static void LoadPreviewImageIntoSquareTexture(Workshop __instance, ref string path) { WorkshopPlus.KeepReadablePreview(__instance, ref path); }
    }
}
