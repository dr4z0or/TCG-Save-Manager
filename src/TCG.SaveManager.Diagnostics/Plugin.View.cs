using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;
using HarmonyLib;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Diagnostics
{
    public sealed partial class Plugin
    {
        private GUIStyle? titleStyle, bodyStyle, nameStyle, mutedStyle, fieldStyle, blueButton, goldButton, quietButton, cardStyle, activeCardStyle;
        private GUIStyle? subtitleStyle, diagnosticStyle;
        private readonly List<Texture2D> viewTextures = new List<Texture2D>();
        private readonly List<Behaviour> blockedRaycasters = new List<Behaviour>();
        private readonly Dictionary<string, string> saveDates = new Dictionary<string, string>();
        private bool createDialog;
        private bool importDialog;
        private int[] importSlots = Array.Empty<int>();
        private int importSlot;
        private string importName = "";
        private readonly Dictionary<int, string> importPreviews = new Dictionary<int, string>();
        [Serializable]
        private sealed class ImportData
        {
            public string m_PlayerName = "";
            public int m_CurrentDay = -1;
            public int m_ShopLevel = -1;
            public double m_CoinAmountDouble = 0;
            public float m_CoinAmount = 0;
        }
        private Campaign? historyCampaign;
        private bool confirmDeleteCampaign;
        private readonly Dictionary<string, SnapshotInfo> newestSaves = new Dictionary<string, SnapshotInfo>();
        // Cached at Refresh: OnGUI runs every frame and must not read the disk.
        private readonly Dictionary<string, SnapshotInfo> latestSaves = new Dictionary<string, SnapshotInfo>();
        private readonly Dictionary<string, SaveSlot[]> slotsCache = new Dictionary<string, SaveSlot[]>();
        private readonly Dictionary<string, KeyValuePair<string, DateTime>> previousAutos = new Dictionary<string, KeyValuePair<string, DateTime>>();
        private readonly Dictionary<string, long> campaignSizes = new Dictionary<string, long>();
        private readonly Dictionary<string, string[]> missingMods = new Dictionary<string, string[]>();
        private string activeCampaignId = "";
        private readonly string[] slotNames = new string[SaveSettingsDefaults.SlotCount];
        private string slotNamesFor = "";
        private int confirmSlot;
        private int renamingSlot;
        private string renameSlotText = "";
        private string search = "";
        private readonly Color ink = new Color(0.12f, 0.2f, 0.27f);

        private Texture2D Rounded(Color color)
        {
            const int size = 32, radius = 10;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var cx = Math.Max(radius, Math.Min(size - radius - 1, x));
                var cy = Math.Max(radius, Math.Min(size - radius - 1, y));
                pixels[y * size + x] = (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius ? color : Color.clear;
            }
            texture.SetPixels(pixels); texture.Apply(); viewTextures.Add(texture); return texture;
        }
        private GUIStyle Text(int size, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = true };
            style.normal.textColor = color; return style;
        }
        private GUIStyle Button(Color color, Color text)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold, border = new RectOffset(11, 11, 11, 11), padding = new RectOffset(10, 10, 8, 8) };
            style.normal.background = Rounded(color); style.normal.textColor = text;
            style.hover.background = Rounded(Color.Lerp(color, Color.white, 0.16f)); style.hover.textColor = text;
            style.active.background = Rounded(Color.Lerp(color, Color.black, 0.15f)); style.active.textColor = text;
            style.focused.background = style.hover.background; style.focused.textColor = text;
            return style;
        }
        private void EnsureTheme()
        {
            if (titleStyle != null) return;
            titleStyle = Text(32, Color.white, true); nameStyle = Text(22, ink, true);
            subtitleStyle = Text(17, new Color(0.8f, 0.92f, 0.98f)); diagnosticStyle = Text(15, Color.white);
            bodyStyle = Text(16, ink); mutedStyle = Text(14, new Color(0.32f, 0.4f, 0.45f));
            blueButton = Button(new Color(0.05f, 0.53f, 0.8f), Color.white);
            goldButton = Button(new Color(1f, 0.67f, 0.08f), ink);
            quietButton = Button(new Color(0.81f, 0.87f, 0.9f), ink);
            cardStyle = new GUIStyle(GUI.skin.box) { border = new RectOffset(11, 11, 11, 11) };
            cardStyle.normal.background = Rounded(new Color(1f, 0.97f, 0.89f));
            activeCardStyle = new GUIStyle(cardStyle); activeCardStyle.normal.background = Rounded(new Color(0.79f, 0.91f, 0.97f));
            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, padding = new RectOffset(12, 12, 9, 9), border = new RectOffset(11, 11, 11, 11) };
            fieldStyle.normal.background = Rounded(Color.white); fieldStyle.normal.textColor = ink;
            fieldStyle.focused.background = fieldStyle.normal.background; fieldStyle.focused.textColor = ink;
        }
        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }
        private string DisplayName(Campaign campaign) => campaigns.Count(c => string.Equals(c.Name, campaign.Name, StringComparison.OrdinalIgnoreCase)) > 1 ? Label(campaign) : campaign.Name;
        private string SuggestedName()
        {
            var number = 1;
            while (campaigns.Any(c => string.Equals(c.Name, F("Boutique {0}", number), StringComparison.OrdinalIgnoreCase))) number++;
            return F("Boutique {0}", number);
        }
        private string SaveDate(Campaign campaign, string kind)
            => saveDates.TryGetValue(campaign.Id + "/" + kind, out var date) ? date : T("Aucune sauvegarde");
        private void RefreshSaveDates()
        {
            saveDates.Clear(); newestSaves.Clear(); latestSaves.Clear(); slotsCache.Clear(); previousAutos.Clear(); campaignSizes.Clear(); missingMods.Clear();
            var loadedMods = BepInEx.Bootstrap.Chainloader.PluginInfos.Keys.ToArray();
            activeCampaignId = workspace!.Active?.CampaignId ?? "";
            slotNamesFor = "";
            foreach (var campaign in campaigns)
            {
                var candidates = new List<SnapshotInfo>();
                var auto = workspace.Describe(campaign.Id, "auto");
                if (auto != null) { latestSaves[campaign.Id + "/auto"] = auto; saveDates[campaign.Id + "/auto"] = When(auto.SavedUtc); candidates.Add(auto); }
                var autos = workspace.Snapshots(campaign.Id, "auto");
                if (autos.Length > 1) previousAutos[campaign.Id] = new KeyValuePair<string, DateTime>(Path.GetFileName(autos[1]), LabStore.SavedTime(autos[1]));
                var slots = workspace.Slots(campaign.Id);
                slotsCache[campaign.Id] = slots;
                candidates.AddRange(slots.Where(s => s.Current != null).Select(s => s.Current!));
                var manual = slots.Where(s => s.Current != null).Select(s => s.Current!).OrderByDescending(s => s.SavedUtc).FirstOrDefault();
                if (manual != null) { latestSaves[campaign.Id + "/manual"] = manual; saveDates[campaign.Id + "/manual"] = When(manual.SavedUtc); }
                var newest = candidates.OrderByDescending(c => c.SavedUtc).FirstOrDefault();
                if (newest != null) newestSaves[campaign.Id] = newest;
                try { campaignSizes[campaign.Id] = workspace.CampaignSize(campaign.Id); } catch (IOException) { }
                try { var missing = workspace.MissingMods(campaign.Id, loadedMods); if (missing.Length > 0) missingMods[campaign.Id] = missing; } catch (Exception) { }
            }
        }
        private static string When(DateTime utc)
        {
            var local = utc.ToLocalTime();
            if (local.Date == DateTime.Today) return F("aujourd'hui à {0}", local.ToString("HH:mm"));
            if (local.Date == DateTime.Today.AddDays(-1)) return F("hier à {0}", local.ToString("HH:mm"));
            return local.ToString(French ? "dd/MM/yyyy · HH:mm" : "yyyy-MM-dd · HH:mm");
        }
        private static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return F("{0:0.0} Go", bytes / (1024.0 * 1024 * 1024));
            if (bytes >= 1024L * 1024) return F("{0:0} Mo", Math.Round(bytes / (1024.0 * 1024)));
            return F("{0} Ko", Math.Max(1, (long)Math.Round(bytes / 1024.0)));
        }
        private static string Money(double value)
            => French ? value.ToString("#,0.00", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ").Replace('.', ',') + " $"
                : "$" + value.ToString("#,0.00", System.Globalization.CultureInfo.InvariantCulture);
        private static string Summary(SnapshotInfo info)
        {
            var parts = new List<string>();
            if (info.Day > 0) parts.Add(F("Jour {0}", info.Day));
            if (info.Level > 0) parts.Add(F("Niveau {0}", info.Level));
            if (info.HasMoney) parts.Add(Money(info.Money));
            return parts.Count == 0 ? T("Détails indisponibles") : string.Join(" · ", parts.ToArray());
        }
        private static string KindName(string kind) => kind == "auto" ? T("Automatique") : T("Manuelle");
        private void CloseView()
        {
            if (nativeScreen != null) Run(() => AccessTools.Method(nativeScreen.GetType(), "CloseScreen").Invoke(nativeScreen, null));
            visible = false; nativeSaveMode = false; createDialog = false; importDialog = false; renameId = null; historyCampaign = null; showInventory = false;
            confirmDeleteCampaign = false; confirmSlot = 0; renamingSlot = 0;
        }

        private void OnGUI()
        {
            if (!visible) return;
            if (!guiConfirmed) { guiConfirmed = true; Logger.LogInfo("Interface boutiques : OnGUI actif."); }
            EnsureTheme();
            var originalMatrix = GUI.matrix; var originalColor = GUI.color; var originalEnabled = GUI.enabled; var originalDepth = GUI.depth;
            GUI.depth = -1000; GUI.color = Color.white; GUI.enabled = true;
            try
            {
                var current = Array.Find(campaigns, c => c.Id == workspace?.Active?.CampaignId);
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.65f));
                var scale = Math.Min(1.25f, Math.Min((Screen.width - 32f) / 1000f, (Screen.height - 32f) / 700f));
                scale = Math.Max(0.1f, scale);
                GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1000 * scale) / 2, (Screen.height - 700 * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
                GUI.Box(new Rect(0, 0, 1000, 700), GUIContent.none, goldButton);
                GUI.Box(new Rect(5, 5, 990, 690), GUIContent.none, activeCardStyle);
                Fill(new Rect(18, 18, 964, 100), new Color(0.12f, 0.23f, 0.3f));
                // The game may close its native screen during SaveGameData.
                // Keep our view context until the user leaves the confirmation screen.
                var saving = nativeSaveMode;
                GUI.enabled = !createDialog && !importDialog && renameId == null && !showInventory && historyCampaign == null;
                GUI.Label(new Rect(38, 30, 850, 44), saving ? T("Sauvegarder ma boutique") : T("Mes boutiques"), titleStyle);
                GUI.Label(new Rect(40, 79, 865, 30), current == null ? T("Choisissez une boutique pour commencer.") : F("Boutique active : {0}", DisplayName(current)), subtitleStyle);
                if (GUI.Button(new Rect(930, 33, 42, 42), "×", quietButton)) CloseView();
                if (workspace == null) { GUI.Label(new Rect(40, 155, 900, 70), T("Save Manager est inactif : son préchargeur (BepInEx/patchers) n'est pas installé ou a refusé de démarrer. Voir LogOutput.log."), bodyStyle); return; }
                if (saving) DrawSaveScreen(current);
                else DrawCampaignScreen(current);
                if (status != "")
                {
                    GUI.Box(new Rect(28, 574, 944, 60), GUIContent.none, cardStyle);
                    GUI.Label(new Rect(42, 582, 916, 44), status, bodyStyle);
                }
                if (GUI.Button(new Rect(30, 648, 165, 36), T("Retour"), blueButton)) CloseView();
                if (!saving && GUI.Button(new Rect(220, 648, 230, 36), T("Importer une sauvegarde"), quietButton)) Run(OpenImportDialog);
                if (GUI.Button(new Rect(785, 648, 185, 36), showInventory ? T("Masquer le diagnostic") : T("Diagnostic"), quietButton)) showInventory = !showInventory;
                GUI.enabled = true;
                if (showInventory) DrawDiagnostics();
                if (createDialog || renameId != null) DrawNameDialog();
                if (historyCampaign != null) DrawSlots();
                if (importDialog) DrawImportDialog();
            }
            finally { GUI.matrix = originalMatrix; GUI.color = originalColor; GUI.enabled = originalEnabled; GUI.depth = originalDepth; }
        }

        private SaveSlot[] SlotsOf(string id) => slotsCache.TryGetValue(id, out var slots) ? slots : Enumerable.Range(1, SaveSettingsDefaults.SlotCount).Select(n => new SaveSlot { Number = n }).ToArray();
        private void DrawSaveScreen(Campaign? current)
        {
            if (current == null) return;
            var slots = SlotsOf(current.Id);
            if (slotNamesFor != current.Id)
            {
                for (var i = 0; i < slotNames.Length; i++) slotNames[i] = slots[i].Current != null ? slots[i].Name : "";
                slotNamesFor = current.Id; confirmSlot = 0;
            }
            GUI.Label(new Rect(42, 140, 920, 35), DisplayName(current), nameStyle);
            latestSaves.TryGetValue(current.Id + "/auto", out var auto);
            GUI.Label(new Rect(42, 178, 920, 30), auto == null ? T("Sauvegarde automatique : aucune pour l'instant (faite par le jeu à la fin de chaque journée).") :
                F("Sauvegarde automatique : {0} · {1} (faite par le jeu)", Summary(auto), When(auto.SavedUtc)), mutedStyle);
            var field = new GUIStyle(fieldStyle) { fontSize = 15, padding = new RectOffset(10, 10, 6, 6) };
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i]; var x = 32 + i * 318; const float y = 214;
                var filled = slot.Current != null;
                GUI.Box(new Rect(x, y, 300, 350), GUIContent.none, filled ? activeCardStyle : cardStyle);
                GUI.Label(new Rect(x + 18, y + 12, 264, 32), F("Emplacement {0}", slot.Number), nameStyle);
                GUI.Label(new Rect(x + 18, y + 48, 264, 26), new GUIContent(filled ? F("« {0} »", slot.Name) : T("(vide)"), filled ? slot.Name : ""), new GUIStyle(bodyStyle) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip });
                if (filled)
                {
                    GUI.Label(new Rect(x + 18, y + 76, 264, 24), Summary(slot.Current!), mutedStyle);
                    GUI.Label(new Rect(x + 18, y + 100, 264, 24), When(slot.Current!.SavedUtc), mutedStyle);
                }
                GUI.Label(new Rect(x + 18, y + 150, 264, 24), T("Nom de la sauvegarde"), mutedStyle);
                slotNames[i] = GUI.TextField(new Rect(x + 18, y + 176, 264, 34), slotNames[i] ?? "", 40, field);
                var number = slot.Number;
                if (!filled)
                {
                    if (GUI.Button(new Rect(x + 18, y + 232, 264, 46), T("Sauvegarder"), goldButton)) Run(() => SaveManual(number, slotNames[number - 1]));
                }
                else if (confirmSlot != number)
                {
                    if (GUI.Button(new Rect(x + 18, y + 232, 264, 46), T("Sauvegarder"), goldButton)) { confirmSlot = number; status = F("Écraser « {0} » ? La version actuelle sera gardée en secours.", slot.Name); }
                }
                else
                {
                    if (GUI.Button(new Rect(x + 18, y + 232, 264, 46), T("Confirmer l'écrasement"), blueButton)) Run(() => { confirmSlot = 0; SaveManual(number, slotNames[number - 1]); });
                    if (GUI.Button(new Rect(x + 18, y + 286, 264, 36), T("Annuler"), quietButton)) { confirmSlot = 0; status = ""; }
                }
            }
        }
        private void DrawCampaignScreen(Campaign? current)
        {
            GUI.Label(new Rect(32, 132, 150, 25), T("Rechercher"), mutedStyle);
            search = GUI.TextField(new Rect(32, 159, 620, 42), search, fieldStyle);
            if (GUI.Button(new Rect(680, 159, 288, 42), T("+ Nouvelle boutique"), goldButton)) { newName = SuggestedName(); createDialog = true; }
            var entries = campaigns.Where(c => search.Trim() == "" || c.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(c => c.Id == current?.Id).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var contentHeight = Math.Max(340, ((entries.Length + 1) / 2) * 180);
            scroll = GUI.BeginScrollView(new Rect(28, 218, 944, 344), scroll, new Rect(0, 0, 920, contentHeight));
            try
            {
                if (entries.Length == 0) GUI.Label(new Rect(18, 24, 850, 60), T("Aucune boutique trouvée. Créez votre première boutique pour commencer."), bodyStyle);
                for (var index = 0; index < entries.Length; index++)
                {
                    var campaign = entries[index]; var x = index % 2 * 466; var y = index / 2 * 180;
                    var active = campaign.Id == current?.Id;
                    GUI.Box(new Rect(x, y, 452, 166), GUIContent.none, active ? activeCardStyle : cardStyle);
                    var display = DisplayName(campaign);
                    GUI.Label(new Rect(x + 16, y + 12, 324, 35), new GUIContent(display.Length > 25 ? display.Substring(0, 24) + "…" : display, display), nameStyle);
                    if (GUI.Button(new Rect(x + 342, y + 13, 96, 30), T("Renommer"), new GUIStyle(quietButton) { fontSize = 13, padding = new RectOffset(4, 4, 4, 4) })) { renameId = campaign.Id; renameName = campaign.Name; }
                    GUI.Label(new Rect(x + 16, y + 48, 420, 24), active ? T("BOUTIQUE ACTIVE") : T("Partie indépendante"), mutedStyle);
                    if (!newestSaves.TryGetValue(campaign.Id, out var newest))
                    {
                        GUI.Label(new Rect(x + 16, y + 80, 420, 25), T("Une nouvelle aventure vous attend."), bodyStyle);
                        if (GUI.Button(new Rect(x + 16, y + 114, 420, 38), T("Commencer cette boutique"), goldButton)) Run(() => ChooseCampaign(campaign.Id, "new"));
                    }
                    else
                    {
                        var details = Summary(newest) + " · " + When(newest.SavedUtc);
                        // 1.4 : a shop whose saves need mods that are not loaded says so instead of its details.
                        if (missingMods.TryGetValue(campaign.Id, out var absent))
                        {
                            var warning = F("Mods absents : {0}", string.Join(", ", absent));
                            var red = new GUIStyle(bodyStyle) { wordWrap = false, clipping = TextClipping.Clip, fontStyle = FontStyle.Bold };
                            red.normal.textColor = new Color(0.75f, 0.18f, 0.1f);
                            GUI.Label(new Rect(x + 16, y + 80, 420, 26), new GUIContent(warning, details + "\n" + T("Installez ces mods pour charger cette boutique.")), red);
                        }
                        else GUI.Label(new Rect(x + 16, y + 80, 420, 26), new GUIContent(details, newest.Kind == "auto" ? T("Sauvegarde automatique") : F("Emplacement {0} · {1}", newest.Slot, newest.Note)), new GUIStyle(bodyStyle) { wordWrap = false, clipping = TextClipping.Clip });
                        var enabled = GUI.enabled;
                        GUI.enabled = enabled && pendingTransition == null;
                        // Continuer resumes the most recent save of either kind, like the game's own button.
                        if (GUI.Button(new Rect(x + 16, y + 114, 250, 40), T("Continuer"), goldButton)) Run(() => ChooseCampaign(campaign.Id, newest.Kind, newest.Id));
                        GUI.enabled = enabled;
                        if (GUI.Button(new Rect(x + 276, y + 114, 160, 40), T("Sauvegardes…"), quietButton)) Run(() => OpenSlots(campaign));
                    }
                }
            }
            finally { GUI.EndScrollView(); }
        }
        private void OpenSlots(Campaign campaign)
        {
            historyCampaign = campaign; confirmDeleteCampaign = false; renamingSlot = 0; status = "";
        }
        private void DrawSlots()
        {
            var campaign = historyCampaign!;
            var atTitle = IsAtTitle();
            Fill(new Rect(0, 0, 1000, 700), new Color(0, 0, 0, 0.72f));
            GUI.Box(new Rect(90, 60, 820, 585), GUIContent.none, cardStyle);
            GUI.Label(new Rect(115, 76, 770, 40), F("Sauvegardes · {0}", DisplayName(campaign)), nameStyle);
            GUI.Label(new Rect(115, 114, 770, 26), T("1 sauvegarde automatique et 3 emplacements à écraser, comme dans le jeu") + (campaignSizes.TryGetValue(campaign.Id, out var bytes) ? F(" · {0} sur le disque", Size(bytes)) : ""), mutedStyle);
            var small = new GUIStyle(quietButton) { fontSize = 13, padding = new RectOffset(6, 6, 4, 4) };
            var smallGold = new GUIStyle(goldButton) { fontSize = 14, padding = new RectOffset(6, 6, 4, 4) };
            var line = new GUIStyle(bodyStyle) { wordWrap = false, clipping = TextClipping.Clip };
            var muted = new GUIStyle(mutedStyle) { wordWrap = false, clipping = TextClipping.Clip };
            var canLoad = GUI.enabled && atTitle && pendingTransition == null;
            void LoadButton(float x, float y, float w, string label, GUIStyle style, string kind, string id)
            {
                var enabled = GUI.enabled; GUI.enabled = canLoad;
                if (GUI.Button(new Rect(x, y, w, 38), label, style)) Run(() => ChooseCampaign(campaign.Id, kind, id));
                GUI.enabled = enabled;
            }
            // Automatic save.
            latestSaves.TryGetValue(campaign.Id + "/auto", out var auto);
            GUI.Box(new Rect(115, 148, 770, 80), GUIContent.none, quietButton);
            GUI.Label(new Rect(130, 156, 400, 26), T("Sauvegarde automatique"), line);
            GUI.Label(new Rect(130, 184, 400, 24), auto == null ? T("Aucune pour l'instant") : Summary(auto) + " · " + When(auto.SavedUtc), muted);
            if (auto != null) LoadButton(530, 168, 96, T("Charger"), smallGold, "auto", auto.Id);
            if (previousAutos.TryGetValue(campaign.Id, out var previousAuto))
                LoadButton(732, 168, 150, T("Version précédente"), small, "auto", previousAuto.Key);
            // Manual slots.
            var slots = SlotsOf(campaign.Id);
            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i]; var y = 240 + i * 90f; var number = slot.Number;
                GUI.Box(new Rect(115, y, 770, 82), GUIContent.none, slot.Current != null ? activeCardStyle : cardStyle);
                if (renamingSlot == number)
                {
                    renameSlotText = GUI.TextField(new Rect(130, y + 8, 390, 32), renameSlotText, 40, new GUIStyle(fieldStyle) { fontSize = 15, padding = new RectOffset(8, 8, 5, 5) });
                    if (GUI.Button(new Rect(530, y + 22, 96, 38), "OK", smallGold)) Run(() => { workspace!.RenameSlot(campaign.Id, number, renameSlotText); renamingSlot = 0; Refresh(); });
                    if (GUI.Button(new Rect(632, y + 22, 100, 38), T("Annuler"), small)) renamingSlot = 0;
                }
                else GUI.Label(new Rect(130, y + 8, 390, 26), number + " · " + (slot.Current != null ? slot.Name : T("(vide)")), line);
                GUI.Label(new Rect(130, y + 40, 390, 24), slot.Current == null ? T("Sauvegardez en jeu pour remplir cet emplacement.") : Summary(slot.Current) + " · " + When(slot.Current.SavedUtc), muted);
                if (renamingSlot == number || slot.Current == null) continue;
                LoadButton(530, y + 22, 96, T("Charger"), smallGold, "manual", slot.Current.Id);
                if (GUI.Button(new Rect(632, y + 22, 94, 38), T("Renommer"), small)) { renamingSlot = number; renameSlotText = slot.Name; }
                if (slot.Previous != null) LoadButton(732, y + 22, 150, T("Version précédente"), small, "manual", slot.Previous.Id);
            }
            var message = !atTitle ? T("Revenez au menu principal pour charger une sauvegarde.") : status;
            if (message != "") GUI.Label(new Rect(115, 512, 770, 36), message, mutedStyle);
            if (GUI.Button(new Rect(115, 552, 220, 42), T("Retour aux boutiques"), blueButton)) { historyCampaign = null; confirmDeleteCampaign = false; renamingSlot = 0; status = ""; }
            var isActive = campaign.Id == activeCampaignId;
            var deleteEnabled = GUI.enabled;
            GUI.enabled = deleteEnabled && !isActive && atTitle && pendingTransition == null;
            if (GUI.Button(new Rect(585, 552, 300, 42), confirmDeleteCampaign ? T("Confirmer : supprimer définitivement") : T("Supprimer la boutique"), confirmDeleteCampaign ? goldButton : quietButton))
            {
                if (!confirmDeleteCampaign) { confirmDeleteCampaign = true; status = T("La boutique et toutes ses sauvegardes seront effacées définitivement. Cliquez à nouveau pour confirmer."); }
                else Run(() => { workspace!.DeleteCampaign(campaign.Id); historyCampaign = null; confirmDeleteCampaign = false; Refresh(); status = F("Boutique « {0} » supprimée.", campaign.Name); });
            }
            GUI.enabled = deleteEnabled;
            if (isActive) GUI.Label(new Rect(585, 598, 300, 40), T("Boutique active : chargez-en une autre pour la supprimer."), new GUIStyle(mutedStyle) { fontSize = 12 });
        }
        private void DrawNameDialog()
        {
            var creating = createDialog;
            var actionY = creating ? 465 : 405;
            Fill(new Rect(0, 0, 1000, 700), new Color(0, 0, 0, 0.72f));
            GUI.Box(new Rect(225, 200, 550, creating ? 340 : 280), GUIContent.none, cardStyle);
            GUI.Label(new Rect(250, 222, 500, 40), createDialog ? T("Une nouvelle boutique") : T("Renommer la boutique"), nameStyle);
            GUI.Label(new Rect(250, 270, 500, 30), T("Choisissez un nom pour la retrouver facilement."), bodyStyle);
            if (createDialog) newName = GUI.TextField(new Rect(250, 311, 500, 46), newName, 80, fieldStyle);
            else renameName = GUI.TextField(new Rect(250, 311, 500, 46), renameName, 80, fieldStyle);
            if (creating && GUI.Button(new Rect(250, 370, 500, 40), T("Importer une sauvegarde existante"), blueButton))
            {
                Run(OpenImportDialog);
                if (importDialog) return;
            }
            if (GUI.Button(new Rect(250, actionY, 170, 44), T("Annuler"), quietButton)) { createDialog = false; renameId = null; }
            if (GUI.Button(new Rect(440, actionY, 310, 44), createDialog ? T("Créer ma boutique") : T("Enregistrer le nom"), goldButton)) Run(() =>
            {
                if (createDialog)
                {
                    workspace!.ArchiveCurrent();
                    var campaign = workspace.Create(newName);
                    Refresh(); createDialog = false;
                    status = T("Boutique créée. Choisissez Commencer cette boutique pour jouer.");
                }
                else { workspace!.Rename(renameId!, renameName); renameId = null; Refresh(); status = T("Le nom de votre boutique a été mis à jour."); }
            });
            if (status.StartsWith(T("Opération impossible"), StringComparison.Ordinal)) GUI.Label(new Rect(250, creating ? 417 : 362, 500, 40), status, bodyStyle);
        }
        private void OpenImportDialog()
        {
            if (!IsAtTitle()) throw new InvalidOperationException(T("Revenez au menu principal pour importer."));
            // 1.4 : the dialog always opens; without saves at the default place, another folder can be chosen.
            workspace!.CustomImportSource = null;
            importName = T("Boutique importée");
            LoadImportSource();
            status = ""; createDialog = false; folderBrowser = false; importDialog = true;
        }
        private string Preview(string file)
        {
            {
                var date = File.GetLastWriteTime(file).ToString(French ? "dd/MM/yyyy · HH:mm" : "yyyy-MM-dd · HH:mm");
                try
                {
                    if (Path.GetExtension(file) != ".json") return date + "\n" + T("Fichier binaire : aperçu indisponible.");
                    var json = File.ReadAllText(file);
                    var data = JsonUtility.FromJson<ImportData>(json);
                    bool Has(string field) => System.Text.RegularExpressions.Regex.IsMatch(json, "\"" + field + "\"\\s*:");
                    var day = Has("m_CurrentDay") ? (data.m_CurrentDay + 1).ToString() : "?";
                    var level = Has("m_ShopLevel") ? (data.m_ShopLevel + 1).ToString() : "?";
                    var money = Has("m_CoinAmountDouble") ? data.m_CoinAmountDouble.ToString("N2") : Has("m_CoinAmount") ? data.m_CoinAmount.ToString("N2") : "?";
                    return (string.IsNullOrWhiteSpace(data.m_PlayerName) ? T("Nom indisponible") : data.m_PlayerName) + " · " + date +
                        "\n" + F("Jour {0}     Niveau {1}     Solde : {2}", day, level, money);
                }
                catch { return date + "\n" + T("Aperçu illisible : contenu à vérifier avant import."); }
            }
        }
        private void DrawImportDialog()
        {
            Fill(new Rect(0, 0, 1000, 700), new Color(0, 0, 0, 0.72f));
            if (folderBrowser) { DrawFolderBrowser(); return; }
            GUI.Box(new Rect(180, 145, 640, 510), GUIContent.none, cardStyle);
            GUI.Label(new Rect(205, 160, 590, 40), T("Importer une sauvegarde"), nameStyle);
            var source = F("Source : {0}", ImportSourceLabel());
            GUI.Label(new Rect(205, 204, 400, 40), new GUIContent(source, source), new GUIStyle(mutedStyle) { wordWrap = true, clipping = TextClipping.Clip });
            if (GUI.Button(new Rect(615, 204, 180, 36), T("Autre dossier…"), new GUIStyle(quietButton) { fontSize = 14 })) Run(OpenFolderBrowser);
            GUI.Label(new Rect(205, 248, 590, 36), T("Chaque emplacement devient une boutique distincte. La source n'est jamais modifiée."), bodyStyle);
            if (importSlots.Length == 0)
            {
                GUI.Box(new Rect(205, 290, 590, 145), GUIContent.none, activeCardStyle);
                GUI.Label(new Rect(220, 302, 560, 120), importSourceError + "\n" + T("Choisissez « Autre dossier… » : une clé USB, une copie de sauvegarde ou le dossier d'un autre PC."), bodyStyle);
            }
            for (var index = 0; index < importSlots.Length; index++)
            {
                var slot = importSlots[index];
                if (GUI.Button(new Rect(205 + index * 148, 290, 138, 42), slot == 0 ? T("Auto") : F("Manuel {0}", slot),
                    slot == importSlot ? blueButton : quietButton)) importSlot = slot;
            }
            var hasSlots = importSlots.Length > 0;
            if (hasSlots)
            {
                GUI.Box(new Rect(205, 345, 590, 90), GUIContent.none, activeCardStyle);
                GUI.Label(new Rect(220, 357, 560, 68), importPreviews.TryGetValue(importSlot, out var preview) ? preview : T("Aperçu indisponible"), bodyStyle);
            }
            importName = GUI.TextField(new Rect(205, 445, 590, 40), importName, 80, fieldStyle);
            var needs = hasSlots && importNeeds.TryGetValue(importSlot, out var need) ? need + " " : "";
            GUI.Label(new Rect(205, 490, 590, 62), needs + T("Les données des autres mods ne sont pas importées."), new GUIStyle(mutedStyle) { fontSize = 13 });
            if (GUI.Button(new Rect(205, 590, 170, 42), T("Annuler"), quietButton)) importDialog = false;
            var enabled = GUI.enabled;
            GUI.enabled = enabled && hasSlots;
            if (GUI.Button(new Rect(405, 590, 390, 42), T("Importer dans une nouvelle boutique"), goldButton)) Run(() =>
            {
                if (!IsAtTitle()) throw new InvalidOperationException(T("Revenez au menu principal pour importer."));
                var campaign = workspace!.Import(importName, importSlot);
                Refresh(); importDialog = false;
                status = F("Boutique importée : {0}. Choisissez sa sauvegarde pour jouer.", campaign.Name);
            });
            GUI.enabled = enabled;
            if (status.StartsWith(T("Opération impossible"), StringComparison.Ordinal)) GUI.Label(new Rect(205, 550, 590, 38), status, mutedStyle);
        }
        private void DrawDiagnostics()
        {
            Fill(new Rect(0, 0, 1000, 640), new Color(0, 0, 0, 0.9f));
            GUI.Label(new Rect(35, 30, 920, 44), T("Diagnostic technique"), titleStyle);
            GUI.Label(new Rect(35, 85, 930, 510), F("{0}/4 interceptions", patched) + "\n\n" + inventory, diagnosticStyle);
            if (GUI.Button(new Rect(720, 580, 245, 42), T("Revenir aux boutiques"), blueButton)) showInventory = false;
        }
        private void SyncViewInput()
        {
            if (visible)
            {
                if (blockedRaycasters.Count != 0) return;
                var type = AccessTools.TypeByName("UnityEngine.UI.GraphicRaycaster");
                if (type == null) return;
                foreach (Behaviour raycaster in UnityEngine.Object.FindObjectsOfType(type))
                    if (raycaster.enabled) { blockedRaycasters.Add(raycaster); raycaster.enabled = false; }
            }
            else RestoreViewInput();
        }
        private void RestoreViewInput()
        {
            foreach (var raycaster in blockedRaycasters) if (raycaster != null) raycaster.enabled = true;
            blockedRaycasters.Clear();
        }
    }
}
