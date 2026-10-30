using System;
using System.Reflection;
using System.IO;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TCG.SaveManager.Core;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Diagnostics
{
    [BepInPlugin("local.tcg.savemanager.diagnostics", "TCG Save Manager", "1.4.4")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private Harmony? harmony;
        private static ManualLogSource? log;
        // 1.4.2 : true once a shop has been loaded or started in this game session. Before that,
        // the mods hold no shop state, so switching needs no reset (and never a restart).
        private static bool shopPlayedThisSession;
        private bool visible = false;
        private bool updateConfirmed;
        private bool guiConfirmed;
        private string inventory = "";
        private Vector2 scroll;
        private int patched;
        private CampaignWorkspace? workspace;
        private Campaign[] campaigns = Array.Empty<Campaign>();
        private static Plugin? instance;
        private string newName = "";
        private string status = "";
        private float nextArchive;
        private bool showInventory;
        private string? renameId;
        private string renameName = "";
        private object? nativeScreen;
        private bool nativeSaveMode;
        private Action? pendingTransition;
        private ConfigEntry<string>? languageSetting;
        private ConfigEntry<KeyboardShortcut>? toggleKey;
        private bool IsAtTitle()
        {
            try
            {
                var game = GameInstance("CGameManager");
                var title = GameInstance("TitleScreen");
                return !(bool)AccessTools.Field(game.GetType(), "m_IsGameLevel").GetValue(game) &&
                    !(bool)AccessTools.Field(title.GetType(), "m_IsOpeningLevel").GetValue(title);
            }
            catch { return false; }
        }
        private bool InGameLevel()
        {
            try
            {
                var game = GameInstance("CGameManager");
                return (bool)AccessTools.Field(game.GetType(), "m_IsGameLevel").GetValue(game);
            }
            catch { return false; }
        }
        private void ChooseCampaign(string id, string kind, string snapshotId = "")
        {
            if (!IsAtTitle())
            {
                status = T("Revenir au menu principal, puis sélectionner cette boutique pour la charger sans relancer le jeu.");
                return;
            }
            if (pendingTransition != null) return;
            pendingTransition = () =>
            {
                if (!IsAtTitle()) throw new InvalidOperationException(T("Changement autorisé uniquement au menu principal."));
                var title = GameInstance("TitleScreen");
                var game = GameInstance("CGameManager");
                var start = AccessTools.Method(title.GetType(), kind == "new" ? "OnPressStartGame" : "OnPressLoadGame", Type.EmptyTypes)
                    ?? throw new MissingMethodException("Démarrage natif indisponible.");
                workspace!.CheckRequiredMods(id, kind, snapshotId, BepInEx.Bootstrap.Chainloader.PluginInfos.Keys);
                Action resetMods;
                if (!shopPlayedThisSession)
                    resetMods = () => Logger.LogInfo("Première boutique de la session : aucun état de mod à réinitialiser.");
                else try { resetMods = ModSessionReset.Prepare(message => Logger.LogInfo(message)); }
                catch (Exception ex) when (!(ex is IOException))
                {
                    // A mod version without a known reset: switching in the running game could mix
                    // two shops. The choice is applied at the next start of the game instead.
                    workspace!.Request(id, kind, snapshotId);
                    var reason = ex.InnerException?.Message ?? ex.Message;
                    status = F("Changement sans redémarrage impossible avec vos mods ({0}). La boutique choisie sera chargée au prochain démarrage : quittez puis relancez le jeu.", reason);
                    Logger.LogWarning(status);
                    return;
                }
                if (nativeScreen != null) AccessTools.Method(nativeScreen.GetType(), "CloseScreen").Invoke(nativeScreen, null);
                workspace!.Request(id, kind, snapshotId);
                Logger.LogInfo(workspace.PrepareLaunch());
                resetMods();
                AccessTools.Field(game.GetType(), "m_CurrentSaveLoadSlotSelectedIndex").SetValue(game, 0);
                AccessTools.Field(game.GetType(), "m_IsManualSaveLoad").SetValue(game, false);
                var screen = GameInstance("SaveLoadGameSlotSelectScreen");
                AccessTools.Field(screen.GetType(), "m_IsDataLoaded").SetValue(screen, false);
                Refresh();
                status = T("Chargement de la boutique sélectionnée…");
                visible = false;
                nativeSaveMode = false;
                historyCampaign = null;
                start.Invoke(title, null);
                Logger.LogInfo("Changement de boutique sans redémarrage : " + id + " / " + kind);
            };
        }
        private readonly System.Collections.Generic.List<GameObject> hiddenPanels = new System.Collections.Generic.List<GameObject>();
        private string Label(Campaign campaign) => campaign.Name + " [" + campaign.Id.Substring(0, 6) + "]";

        private void Awake()
        {
            log = Logger;
            instance = this;
            // Shared by every TCG mod (Game Modes, Mod Profiles…): their menus follow this setting.
            languageSetting = Config.Bind("Interface", "Language", "auto", new ConfigDescription(
                "Language of the TCG mods' menus: auto (game language), en or fr. / Langue des menus des mods TCG : auto (langue du jeu), en ou fr.",
                new AcceptableValueList<string>("auto", "en", "fr")));
            TCG.Shared.Lang.Setting = languageSetting.Value;
            languageSetting.SettingChanged += (_, __) => TCG.Shared.Lang.Setting = languageSetting.Value;
            // The game's own menus open the manager; a shortcut is optional (none by default).
            toggleKey = Config.Bind("Interface", "ToggleKey", KeyboardShortcut.Empty,
                "Optional shortcut to open or close the shop manager, none by default: it opens from the game's Load and Save menus. / Raccourci facultatif du gestionnaire de boutiques, aucun par défaut : il s'ouvre par les menus Charger et Sauvegarder du jeu.");
            Logger.LogInfo("Chemin effectif des sauvegardes : " + Application.persistentDataPath);
            try
            {
                workspace = new CampaignWorkspace(Application.persistentDataPath, ModSessionReset.GameHash);
                // 1.4.4 : saves older than the installed game build keep the fingerprints recorded by their mods.
                var gameCode = Path.Combine(BepInEx.Paths.ManagedPath, "Assembly-CSharp.dll");
                if (File.Exists(gameCode)) TCG.SaveManager.Core.SlotFiles.GameBuildUtc = File.GetLastWriteTimeUtc(gameCode);
                // Unity's native save folder: %USERPROFILE%\AppData\LocalLow\<company>\<product>.
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                    workspace.NativeSaves = Path.Combine(Directory.GetParent(local)!.FullName, "LocalLow", Application.companyName, Application.productName);
            }
            catch (Exception ex) { Logger.LogWarning(F("Gestion des boutiques inactive : {0}", ex.Message)); }
            var dataRoot = Directory.GetParent(Application.persistentDataPath)!.FullName;
            var autonomous = File.Exists(Path.Combine(dataRoot, "preloader-test-profile.json")) || File.Exists(Path.Combine(dataRoot, ".tcg-managed-instance"));
            if (autonomous)
            {
                try
                {
                    if (workspace == null) throw new InvalidOperationException(T("Espace isolé indisponible."));
                    Logger.LogInfo("Initialisation autonome : " + workspace.RecoverInterrupted());
                    var request = workspace.Pending;
                    // 1.4.4 : a shop that needs missing mods is not loaded, but the game still starts
                    // with the current shop instead of closing.
                    if (request != null)
                        try { workspace.CheckRequiredMods(request.CampaignId, request.Kind, request.SnapshotId, BepInEx.Bootstrap.Chainloader.PluginInfos.Keys); }
                        catch (Exception ex) when (!(ex is IOException))
                        {
                            workspace.CancelRequest();
                            status = F("Boutique demandée non chargée, la boutique active est conservée : {0}", ex.Message);
                            Logger.LogWarning(status);
                        }
                    var prepared = workspace.PrepareStartup();
                    Logger.LogInfo("Initialisation autonome : " + prepared);
                    if (prepared.StartsWith(T("Archivage"), StringComparison.Ordinal)) { status = prepared; Logger.LogWarning(prepared); }
                }
                catch (Exception ex)
                {
                    Logger.LogError(F("Initialisation autonome refusée. Arrêt pour conserver les fichiers de récupération : {0}", ex));
                    try { File.WriteAllText(Path.Combine(Directory.GetParent(Application.persistentDataPath)!.FullName, "startup-error.txt"), DateTime.UtcNow.ToString("O") + "\n" + ex); }
                    catch (Exception reportError) { Logger.LogError("Rapport de démarrage indisponible : " + reportError.Message); }
                    enabled = false;
                    Application.Quit();
                    return;
                }
            }
            // 1.1 : like the game, only the latest automatic save and its backups are kept.
            if (workspace != null) Run(() => { var removed = workspace.PruneAllAutos(); if (removed > 0) Logger.LogInfo("Anciennes sauvegardes automatiques retirées : " + removed + "."); }, false);
            Refresh();
            harmony = new Harmony("local.tcg.savemanager.diagnostics");
            try
            {
                var type = AccessTools.TypeByName("CSaveLoad") ?? throw new InvalidOperationException("CSaveLoad absent.");
                Attach(type, "Save", new[] { typeof(int), typeof(bool) }, nameof(SavePrefix), null);
                Attach(type, "Load", new[] { typeof(int) }, nameof(LoadPrefix), nameof(LoadPostfix));
                Attach(type, "LoadBackup", new[] { typeof(int) }, nameof(LoadPrefix), nameof(LoadPostfix));
                Attach(type, "AutoSaveMoveToEmptySaveSlot", Type.EmptyTypes, nameof(MovePrefix), null);
                var screen = AccessTools.TypeByName("SaveLoadGameSlotSelectScreen") ?? throw new MissingMemberException("Menu absent.");
                harmony.Patch(AccessTools.Method(screen, "OpenScreen", new[] { typeof(bool) }), postfix: new HarmonyMethod(typeof(Plugin), nameof(MenuOpened)));
                harmony.Patch(AccessTools.Method(screen, "CloseScreen", Type.EmptyTypes), postfix: new HarmonyMethod(typeof(Plugin), nameof(MenuClosed)));
                var title = AccessTools.TypeByName("TitleScreen") ?? throw new MissingMemberException("Menu principal absent.");
                harmony.Patch(AccessTools.Method(title, "OnPressStartGame", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(Plugin), nameof(NewGamePressed)));
                Logger.LogInfo($"Diagnostic prêt : {patched}/4 points interceptés. Menu principal : Charger la partie.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                patched = 0;
                Logger.LogError("Diagnostic désactivé : " + ex);
            }
        }

        private void Attach(Type type, string name, Type[] signature, string prefix, string? postfix)
        {
            var target = AccessTools.Method(type, name, signature) ?? throw new MissingMethodException(type.FullName, name);
            harmony!.Patch(target, new HarmonyMethod(typeof(Plugin), prefix), postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
            patched++;
            Logger.LogInfo("Point confirmé : " + target);
        }

        // Void prefixes observe only: no argument, result or game state changes.
        private static void SavePrefix(int __0, bool __1) => log?.LogInfo($"[SaveManager] Save slot={__0}, skipJSONSave={__1}");
        private static void LoadPrefix(MethodBase __originalMethod, int __0)
        {
            shopPlayedThisSession = true;
            log?.LogInfo($"[SaveManager] {__originalMethod.Name} slot={__0}");
        }
        private static void LoadPostfix(MethodBase __originalMethod, bool __result) => log?.LogInfo($"[SaveManager] {__originalMethod.Name} résultat={__result}");
        private static bool MovePrefix()
        {
            var allow = instance?.workspace?.Active == null;
            log?.LogInfo("[SaveManager] AutoSaveMoveToEmptySaveSlot autorisé=" + allow);
            return allow;
        }

        private static object GameInstance(string typeName)
        {
            var type = AccessTools.TypeByName(typeName) ?? throw new MissingMemberException(typeName);
            var getter = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetGetMethod(true);
            return getter?.Invoke(null, null) ?? UnityEngine.Object.FindObjectOfType(type) ?? throw new InvalidOperationException(typeName + " indisponible.");
        }
        private static bool NewGamePressed()
        {
            var plugin = instance;
            if (plugin?.workspace == null) return true;
            // A newly prepared, empty campaign may enter the game's normal new-game flow.
            var prepared = plugin.workspace.Active;
            var empty = true;
            for (var slot = 0; slot < 4; slot++)
                foreach (var file in SaveInventory.NativeFiles(Application.persistentDataPath, slot)) empty = false;
            if (prepared?.LoadedKind == "new" && empty) { plugin.visible = false; shopPlayedThisSession = true; return true; }
            plugin.visible = true;
            plugin.nativeSaveMode = false;
            plugin.createDialog = true;
            plugin.newName = plugin.SuggestedName();
            plugin.Refresh();
            plugin.status = T("Choisissez un nom pour votre nouvelle boutique.");
            plugin.Logger.LogInfo("Nouvelle partie : ouverture de la création de boutique.");
            return false;
        }
        private static void MenuOpened(bool __0)
        {
            var plugin = instance;
            if (plugin?.workspace == null) return;
            plugin.Run(() =>
            {
                plugin.RestorePanels();
                plugin.nativeScreen = GameInstance("SaveLoadGameSlotSelectScreen");
                plugin.nativeSaveMode = __0;
                var panels = (IEnumerable)AccessTools.Field(plugin.nativeScreen.GetType(), "m_SaveLoadSlotPanelUIList").GetValue(plugin.nativeScreen);
                foreach (Component panel in panels)
                    if (panel.gameObject.activeSelf) { plugin.hiddenPanels.Add(panel.gameObject); panel.gameObject.SetActive(false); }
                plugin.visible = true;
                plugin.Refresh();
                plugin.Logger.LogInfo("Menu boutiques intégré : " + (__0 ? "sauvegarder" : "charger"));
            }, true, true);
        }
        private static void MenuClosed() { instance?.RestorePanels(); if (instance != null) instance.nativeScreen = null; }
        private void RestorePanels() { foreach (var panel in hiddenPanels) if (panel != null) panel.SetActive(true); hiddenPanels.Clear(); }
        // Saves the running game into one of the shop's manual slots (1 to 3).
        private void SaveManual(int slot, string name)
        {
            var game = GameInstance("CGameManager");
            if (!(bool)AccessTools.Field(game.GetType(), "m_IsGameLevel").GetValue(game)) throw new InvalidOperationException(T("Aucune partie en cours."));
            var shelves = GameInstance("ShelfManager");
            AccessTools.Method(shelves.GetType(), "SaveInteractableObjectData", new[] { typeof(bool) }).Invoke(shelves, new object[] { false });
            AccessTools.Field(game.GetType(), "m_IsManualSaveLoad").SetValue(game, true);
            AccessTools.Field(game.GetType(), "m_CurrentSaveLoadSlotSelectedIndex").SetValue(game, 1);
            var started = DateTime.UtcNow;
            AccessTools.Method(game.GetType(), "SaveGameData", new[] { typeof(int) }).Invoke(game, new object[] { 1 });
            var saved = Path.Combine(Application.persistentDataPath, "savedGames_Release1.json");
            if (!File.Exists(saved) || new FileInfo(saved).Length == 0 || File.GetLastWriteTimeUtc(saved) < started)
                throw new IOException(T("La sauvegarde JSON n'a pas été actualisée. Sauvegarde non confirmée."));
            if (!(bool)AccessTools.Method(AccessTools.TypeByName("CSaveLoad"), "ValidateSavedSlotData", new[] { typeof(string) }).Invoke(null, new object[] { saved }))
                throw new IOException(T("Le jeu refuse la validation du fichier sauvegardé."));
            workspace!.ArchiveCurrent(true);
            var captured = workspace.LastManualCapture ?? throw new IOException(T("La sauvegarde n'a pas pu être archivée. Réessayez."));
            workspace.AssignSlot(workspace.Active!.CampaignId, captured, slot, name);
            Refresh();
            status = F("SAUVEGARDE CONFIRMÉE dans l'emplacement {0} à {1}. Cliquer sur Retour pour reprendre la partie.", slot, DateTime.Now.ToString("HH:mm:ss"));
            Logger.LogInfo(status);
        }

        private void Refresh()
        {
            try { inventory = SaveInventory.Describe(Application.persistentDataPath); campaigns = workspace?.List() ?? Array.Empty<Campaign>(); RefreshSaveDates(); }
            catch (Exception ex) { inventory = F("Inventaire indisponible : {0}", ex.Message); }
        }
        private void Update()
        {
            if (pendingTransition != null)
            {
                var transition = pendingTransition;
                pendingTransition = null;
                Run(transition);
            }
            if (!updateConfirmed)
            {
                updateConfirmed = true; Logger.LogInfo("Diagnostic : Update actif.");
                Run(() => { ModSessionReset.Prepare(message => Logger.LogInfo(message)); Logger.LogInfo("Plan de réinitialisation des mods résolu, sans exécution."); }, false);
            }
            if (workspace != null && Time.realtimeSinceStartup >= nextArchive)
            {
                nextArchive = Time.realtimeSinceStartup + 5;
                if (!shopPlayedThisSession && InGameLevel()) shopPlayedThisSession = true;
                // Hashing and Furniture's JsonUtility normalization run on Unity's thread.
                // Keep these operations out of gameplay; native saves still write normally.
                if (visible || IsAtTitle())
                    Run(() =>
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        if (workspace.ArchiveIfChanged()) Refresh();
                        if (timer.ElapsedMilliseconds >= 100)
                            Logger.LogInfo("Archivage au menu : " + timer.ElapsedMilliseconds + " ms.");
                    }, false);
            }
            if (toggleKey!.Value.IsDown())
            {
                visible = !visible;
                Logger.LogInfo("Diagnostic : raccourci reçu, panneau visible=" + visible);
                if (visible)
                {
                    nativeSaveMode = (bool)AccessTools.Field(GameInstance("CGameManager").GetType(), "m_IsGameLevel").GetValue(GameInstance("CGameManager"));
                    Refresh();
                }
                else nativeSaveMode = false;
            }
            SyncViewInput();
        }
        private void Run(Action action, bool report = true, bool resetMenu = false)
        {
            try { action(); }
            catch (Exception ex)
            {
                if (report)
                {
                    status = F("Opération impossible : {0}", ex.InnerException?.Message ?? ex.Message);
                    if (resetMenu) { RestorePanels(); nativeScreen = null; }
                }
                Logger.LogError(ex);
            }
        }
        private void OnDisable() { Logger.LogWarning("Diagnostic : composant désactivé."); }
        private void OnDestroy()
        {
            RestoreViewInput(); RestorePanels();
            foreach (var texture in viewTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
            harmony?.UnpatchSelf(); log = null; instance = null;
        }
    }
}

