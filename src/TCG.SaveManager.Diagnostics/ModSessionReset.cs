using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using System.IO;
using HarmonyLib;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Diagnostics
{
    internal static class ModSessionReset
    {
        // Resolve the complete plan before any files or in-memory state are changed.
        public static Action Prepare(Action<string> log)
        {
            var actions = new List<MethodInfo>();
            var resets = new List<Action>();
            Add("com.qolmods.shoplotc", "1.5.4", "ShopLotC.ShopCWorldBuilder", "ShopLotC.ShopCState");
            Add("com.qolmods.upstairs", "1.4.2", "Upstairs.UpWorld", "Upstairs.UpState");
            if (Chainloader.PluginInfos.TryGetValue("com.qolmods.staffoverhaul", out var staff))
            {
                if (staff.Metadata.Version != new Version("2.1.1") || staff.Instance == null) throw new InvalidOperationException(T("Version Staff Overhaul non prise en charge."));
                var reset = staff.Instance.GetType().Assembly.GetType("StaffOverhaul.Staff")?.GetMethod("Reset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (reset == null || reset.ReturnType != typeof(void)) throw new MissingMethodException(T("Réinitialisation Staff indisponible."));
                actions.Add(reset);
            }
            if (Chainloader.PluginInfos.TryGetValue("com.qolmods.furnitureoverhaul", out var furniture))
            {
                if (furniture.Metadata.Version != new Version("1.6.1") || furniture.Instance == null) throw new InvalidOperationException(T("Version Furniture non prise en charge."));
                var assembly = furniture.Instance.GetType().Assembly;
                var chosen = assembly.GetType("FurnitureOverhaul.FinishOrder")?.GetField("Chosen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (chosen == null || !typeof(System.Collections.IDictionary).IsAssignableFrom(chosen.FieldType))
                    throw new MissingFieldException(T("Sélection de couleurs Furniture inconnue."));
                resets.Add(() => ((System.Collections.IDictionary)chosen.GetValue(null)).Clear());
                foreach (var step in new[] {
                    ("CheckoutDisplay", "_loaded"), ("CheckoutDisplay", "_captured"), ("CheckoutDisplay", "_captureError"),
                    ("ClawSave", "_loaded"), ("PaintSave", "_captured"), ("PaintSave", "_pending"), ("PaintSave", "_pendingFrom") })
                {
                    var field = assembly.GetType("FurnitureOverhaul." + step.Item1)?.GetField(step.Item2, BindingFlags.NonPublic | BindingFlags.Static);
                    if (field == null || field.FieldType.IsValueType) throw new MissingFieldException(F("Cache Furniture inconnu : {0}.{1}", step.Item1, step.Item2));
                    resets.Add(() => field.SetValue(null, null));
                }
                foreach (var step in new[] { ("ClawSave", "_slot", (object)0), ("DisplayOnly", "_slot", (object)0), ("DisplayOnly", "_restorePending", (object)false) })
                {
                    var field = assembly.GetType("FurnitureOverhaul." + step.Item1)?.GetField(step.Item2, BindingFlags.NonPublic | BindingFlags.Static);
                    if (field == null || field.FieldType != step.Item3.GetType()) throw new MissingFieldException(T("État Furniture inconnu."));
                    resets.Add(() => field.SetValue(null, step.Item3));
                }
                foreach (var step in new[] { ("Plugin", "ForgetShelves"), ("TagLayer", "Forget"), ("SaveWatch", "Forget"), ("BoxLabel", "Forget"),
                    ("RugResize", "Forget"), ("Refinish", "Forget"), ("RugStack", "Forget"), ("FinishOrder", "Forget"), ("FinishPreview", "Release") })
                {
                    var method = assembly.GetType("FurnitureOverhaul." + step.Item1)?.GetMethod(step.Item2, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(T("Nettoyage Furniture inconnu."));
                    actions.Add(method);
                }
                var sweep = furniture.Instance.GetType().GetField("_sweepDone", BindingFlags.Instance | BindingFlags.NonPublic);
                if (sweep == null || sweep.FieldType != typeof(bool)) throw new MissingFieldException(T("État de reconstruction Furniture inconnu."));
                resets.Add(() => sweep.SetValue(furniture.Instance, false));
            }
            // 1.2 : Enhanced Prefab Loader only reads its file when it exists, otherwise it keeps the cards
            // of the shop loaded before. Clearing its data first makes each shop start from its own file.
            Action? eplReset = null;
            if (Chainloader.PluginInfos.TryGetValue("EnhancedPrefabLoader", out var epl) && epl.Instance != null)
            {
                try
                {
                    var runtime = epl.Instance.GetType().Assembly.GetType("EnhancedPrefabLoader.Core.EplRuntimeData", false);
                    var services = runtime?.GetProperty("Services", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var manager = services?.PropertyType.GetProperty("SaveDataManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var clear = manager?.PropertyType.GetMethod("ResetAllSaveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (services == null || manager == null || clear == null) log(F("Enhanced Prefab Loader : réinitialisation indisponible pour cette version ({0}).", epl.Metadata.Version));
                    else eplReset = () =>
                    {
                        var target = manager.GetValue(services.GetValue(null));
                        if (target != null) clear.Invoke(target, null);
                    };
                }
                catch (Exception ex) { log(F("Enhanced Prefab Loader : réinitialisation indisponible : {0}", ex.Message)); }
            }
            return () =>
            {
                if (eplReset != null)
                {
                    try { eplReset(); log(T("Cartes Enhanced Prefab Loader vidées avant le chargement de la boutique.")); }
                    catch (Exception ex) { log(F("Enhanced Prefab Loader : réinitialisation impossible : {0}", ex.InnerException?.Message ?? ex.Message)); }
                }
                foreach (var method in actions)
                {
                    method.Invoke(null, null);
                    log(F("État du mod réinitialisé : {0}.{1}", method.DeclaringType!.FullName, method.Name));
                }
                foreach (var reset in resets) reset();
                if (resets.Count > 0) log(T("Caches Furniture réinitialisés avant le chargement natif."));
            };
            void Add(string id, string version, string world, string state)
            {
                if (!Chainloader.PluginInfos.TryGetValue(id, out var plugin)) return;
                if (plugin.Metadata.Version != new Version(version) || plugin.Instance == null)
                    throw new InvalidOperationException(F("Version du mod non prise en charge : {0}", id));
                var assembly = plugin.Instance.GetType().Assembly;
                foreach (var step in new[] { (Type: world, Method: "Teardown"), (Type: state, Method: "Reset") })
                {
                    var type = assembly.GetType(step.Type, false);
                    var method = type?.GetMethod(step.Method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (method == null || method.ReturnType != typeof(void))
                        throw new MissingMethodException(F("Réinitialisation du mod indisponible : {0}.{1}", step.Type, step.Method));
                    actions.Add(method);
                }
            }
        }
        // 1.4.2 : a large save (8 MB) takes about a second to parse. The same files are hashed several
        // times per archive (fingerprints, then capture), which froze the menu for several seconds:
        // each file is now parsed once per change, keyed by path, size and modification time.
        private static readonly Dictionary<string, (long Length, long Ticks, string Hash)> gameHashes = new Dictionary<string, (long, long, string)>(StringComparer.OrdinalIgnoreCase);
        public static string GameHash(string file)
        {
            var info = new FileInfo(file);
            var length = info.Length; var ticks = info.LastWriteTimeUtc.Ticks;
            if (gameHashes.TryGetValue(info.FullName, out var known) && known.Length == length && known.Ticks == ticks) return known.Hash;
            var hash = ComputeGameHash(file);
            // Stamp taken before reading: a file rewritten meanwhile is simply parsed again next time.
            if (gameHashes.Count > 64) gameHashes.Clear();
            gameHashes[info.FullName] = (length, ticks, hash);
            return hash;
        }
        private static string ComputeGameHash(string file)
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.qolmods.furnitureoverhaul", out var plugin))
            {
                if (!Chainloader.PluginInfos.TryGetValue("com.qolmods.staffoverhaul", out var staff)) return TCG.SaveManager.Core.LabStore.Hash(file).ToLowerInvariant();
                // Any version exposing the same fingerprint method is accepted: an incompatible change
                // only makes the capture refuse the companion files, never pairs wrong ones.
                if (staff.Instance == null) throw new InvalidOperationException(T("Version Staff Overhaul non prise en charge."));
                var staffGameType = AccessTools.TypeByName("CGameData") ?? throw new MissingMemberException("CGameData absent.");
                var hash = staff.Instance.GetType().Assembly.GetType("StaffOverhaul.StaffStore")?.GetMethod("GameHash", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { staffGameType }, null)
                    ?? throw new MissingMethodException(T("Empreinte Staff indisponible."));
                var staffData = ReadGame(file, staffGameType);
                return staffData == null ? TCG.SaveManager.Core.LabStore.Hash(file).ToLowerInvariant() : (string)hash.Invoke(null, new[] { staffData });
            }
            if (plugin.Instance == null) throw new InvalidOperationException(T("Version Furniture non prise en charge."));
            var gameType = AccessTools.TypeByName("CGameData") ?? throw new MissingMemberException("CGameData absent.");
            var data = ReadGame(file, gameType);
            // 1.4.1 : a file that is not a game save (damaged backup, test data) only gets its raw
            // fingerprint, so no companion file matches it; it must not block archiving the shop.
            if (data == null) return TCG.SaveManager.Core.LabStore.Hash(file).ToLowerInvariant();
            var method = plugin.Instance.GetType().Assembly.GetType("FurnitureOverhaul.ClawSave")?.GetMethod("Hash", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { gameType }, null)
                ?? throw new MissingMethodException(T("Normalisation Furniture indisponible."));
            return (string)method.Invoke(null, new[] { data });
        }
        private static object? ReadGame(string file, Type type)
        {
            try { return JsonUtility.FromJson(File.ReadAllText(file), type); }
            catch (ArgumentException) { return null; }
        }
    }
}
