using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TCG.SaveManager.Core;
using UnityEngine;
using static TCG.Shared.Lang;

namespace TCG.SaveManager.Diagnostics
{
    // 1.4 : import from any folder (USB key, backup…) through a small folder browser, and the mods
    // each save needs, shown before importing. The chosen folder is only read.
    public sealed partial class Plugin
    {
        private sealed class BrowseEntry { public string Label = "", Path = ""; public int Saves; }
        private bool folderBrowser;
        private string browsePath = "", browseInput = "", browseMessage = "";
        private readonly List<BrowseEntry> browseEntries = new List<BrowseEntry>();
        private int[] browseSlots = Array.Empty<int>();
        private Vector2 browseScroll;
        private string importSourceError = "";
        private readonly Dictionary<int, string> importNeeds = new Dictionary<int, string>();

        private string RecentFoldersFile => Path.Combine(Directory.GetParent(Application.persistentDataPath)!.FullName, "import-folders.txt");
        private string[] RecentFolders()
        {
            try { return File.Exists(RecentFoldersFile) ? File.ReadAllLines(RecentFoldersFile).Where(l => l.Trim() != "").Take(5).ToArray() : Array.Empty<string>(); }
            catch (Exception) { return Array.Empty<string>(); }
        }
        private void RememberFolder(string folder)
        {
            try { File.WriteAllLines(RecentFoldersFile, new[] { folder }.Concat(RecentFolders().Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))).Take(5).ToArray()); }
            catch (Exception ex) { Logger.LogWarning(ex.Message); }
        }

        // Slots, previews and needed mods of the current import source.
        private void LoadImportSource()
        {
            importSlots = Array.Empty<int>(); importPreviews.Clear(); importNeeds.Clear(); importSourceError = "";
            try
            {
                importSlots = workspace!.ImportSlots();
                if (importSlots.Length == 0)
                    importSourceError = workspace.ImportFromGameFolder ? T("Aucune sauvegarde du jeu trouvée sur ce PC.") : T("Aucune sauvegarde dans ce dossier.");
            }
            catch (Exception ex) { importSourceError = ex.InnerException?.Message ?? ex.Message; return; }
            importSlot = importSlots.Length > 0 ? importSlots[0] : 0;
            var loaded = BepInEx.Bootstrap.Chainloader.PluginInfos.Keys.ToArray();
            foreach (var slot in importSlots)
            {
                importPreviews[slot] = Preview(workspace!.ImportPreviewFile(slot));
                try
                {
                    var needs = workspace.ImportRequirements(slot, loaded);
                    importNeeds[slot] = needs.Length == 0 ? T("Aucune donnée de mod connue dans cette sauvegarde.")
                        : F("Mods utilisés : {0}.", string.Join(" · ", needs.Select(n => n.Loaded ? F("{0} (installé)", n.Name) : F("{0} (ABSENT)", n.Name))))
                          + (needs.Any(n => !n.Loaded) ? " " + T("L'import reste possible ; installez les mods absents pour charger cette boutique.") : "");
                }
                catch (Exception ex) { importNeeds[slot] = F("Mods utilisés : vérification impossible ({0}).", ex.Message); }
            }
        }

        private string ImportSourceLabel()
        {
            if (workspace!.CustomImportSource != null) return workspace.CustomImportSource;
            return workspace.ImportFromGameFolder ? T("sauvegardes du jeu sur ce PC") : T("copie de sécurité du laboratoire");
        }

        private void OpenFolderBrowser()
        {
            var start = workspace!.CustomImportSource ?? RecentFolders().FirstOrDefault(Directory.Exists) ?? "";
            Navigate(start);
            folderBrowser = true;
        }

        private void Navigate(string path)
        {
            browseEntries.Clear(); browseScroll = Vector2.zero; browseMessage = ""; browseSlots = Array.Empty<int>();
            if (path.Trim() == "")
            {
                browsePath = ""; browseInput = "";
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.DriveType == DriveType.CDRom) continue;
                        var kind = drive.DriveType == DriveType.Removable ? T("amovible") : drive.DriveType == DriveType.Network ? T("réseau") : "";
                        var label = drive.VolumeLabel;
                        browseEntries.Add(new BrowseEntry { Path = drive.RootDirectory.FullName,
                            Label = drive.Name + (label != "" ? "  " + label : "") + (kind != "" ? "  (" + kind + ")" : "") });
                    }
                    catch (Exception) { }
                }
                if (workspace!.NativeSaves != null && Directory.Exists(workspace.NativeSaves))
                    browseEntries.Add(new BrowseEntry { Path = workspace.NativeSaves, Label = T("Sauvegardes du jeu sur ce PC"), Saves = CampaignWorkspace.SlotsIn(workspace.NativeSaves).Length });
                foreach (var recent in RecentFolders().Where(Directory.Exists))
                    browseEntries.Add(new BrowseEntry { Path = recent, Label = F("Récent : {0}", recent), Saves = CampaignWorkspace.SlotsIn(recent).Length });
                return;
            }
            try
            {
                var full = Path.GetFullPath(path.Trim().Trim('"'));
                if (!Directory.Exists(full)) { Navigate(""); browseMessage = T("Dossier introuvable."); return; }
                browsePath = full; browseInput = full;
                browseSlots = CampaignWorkspace.SlotsIn(full);
                var subfolders = Directory.GetDirectories(full).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var dir in subfolders.Take(300))
                {
                    try
                    {
                        var attributes = File.GetAttributes(dir);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        browseEntries.Add(new BrowseEntry { Path = dir, Label = Path.GetFileName(dir), Saves = CampaignWorkspace.SlotsIn(dir).Length });
                    }
                    catch (Exception) { }
                }
                if (subfolders.Length > 300) browseMessage = F("{0} dossiers : seuls les 300 premiers sont affichés.", subfolders.Length);
            }
            catch (Exception ex) { browseMessage = F("Dossier illisible : {0}", ex.Message); }
        }

        private void DrawFolderBrowser()
        {
            GUI.Box(new Rect(180, 145, 640, 510), GUIContent.none, cardStyle);
            GUI.Label(new Rect(205, 160, 590, 40), T("Choisir un dossier de sauvegardes"), nameStyle);
            var small = new GUIStyle(quietButton) { fontSize = 13, padding = new RectOffset(6, 6, 4, 4) };
            browseInput = GUI.TextField(new Rect(205, 204, 470, 36), browseInput, 260, new GUIStyle(fieldStyle) { fontSize = 14, padding = new RectOffset(8, 8, 6, 6) });
            if (GUI.Button(new Rect(685, 204, 110, 36), T("Aller"), small)) Navigate(browseInput);
            var enabled = GUI.enabled;
            GUI.enabled = enabled && browsePath != "";
            if (GUI.Button(new Rect(205, 248, 190, 32), T("Dossier parent"), small))
                Navigate(Directory.GetParent(browsePath)?.FullName ?? "");
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(405, 248, 190, 32), T("Lecteurs et récents"), small)) Navigate("");
            var rows = Math.Max(1, browseEntries.Count);
            browseScroll = GUI.BeginScrollView(new Rect(205, 288, 590, 236), browseScroll, new Rect(0, 0, 566, rows * 36));
            try
            {
                if (browseEntries.Count == 0) GUI.Label(new Rect(6, 4, 556, 30), T("Aucun sous-dossier."), mutedStyle);
                var row = new GUIStyle(quietButton) { fontSize = 14, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 4, 4), wordWrap = false, clipping = TextClipping.Clip };
                var withSaves = new GUIStyle(blueButton) { fontSize = 14, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 4, 4), wordWrap = false, clipping = TextClipping.Clip };
                for (var i = 0; i < browseEntries.Count; i++)
                {
                    var entry = browseEntries[i];
                    var text = entry.Label + (entry.Saves > 0 ? "   " + F("— {0} sauvegarde(s)", entry.Saves) : "");
                    if (GUI.Button(new Rect(0, i * 36, 566, 32), new GUIContent(text, entry.Path), entry.Saves > 0 ? withSaves : row)) { Navigate(entry.Path); break; }
                }
            }
            finally { GUI.EndScrollView(); }
            var message = browseMessage != "" ? browseMessage
                : browsePath == "" ? T("Choisissez un lecteur (clé USB comprise) ou un dossier récent.")
                : browseSlots.Length > 0 ? F("{0} sauvegarde(s) dans ce dossier : il peut être utilisé.", browseSlots.Length)
                : T("Aucune sauvegarde dans ce dossier. Les dossiers en bleu en contiennent.");
            GUI.Label(new Rect(205, 530, 590, 50), message, mutedStyle);
            if (GUI.Button(new Rect(205, 590, 170, 42), T("Annuler"), quietButton)) folderBrowser = false;
            GUI.enabled = enabled && browseSlots.Length > 0;
            if (GUI.Button(new Rect(405, 590, 390, 42), T("Utiliser ce dossier"), goldButton))
            {
                workspace!.CustomImportSource = browsePath;
                RememberFolder(browsePath);
                LoadImportSource();
                folderBrowser = false;
            }
            GUI.enabled = enabled;
        }
    }
}
