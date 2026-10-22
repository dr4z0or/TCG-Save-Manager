using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TCG.SaveManager.Core;

internal static class ModAudit
{
    public static void SelfTest(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Le test exige un nouveau dossier.");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".tcg-test-instance"), "synthetic mod restore test");
        var source = Path.Combine(root, "original-saves-copy");
        Directory.CreateDirectory(source);
        Put("savedGames_Release3.json", "{\"fixture\":3}");
        Put("savedGames_Release3.gd", "synthetic binary");
        Put("savedGames_ReleaseBackupFile3.json", "older-native");
        Put("ShopLotC_Save3.json", "shop-c");
        Put("Upstairs/Upstairs_3.json", "upstairs");
        Put("Upstairs_Save3.json", "legacy");
        Put("FurnitureOverhaulPaint/slot3.json", "paint");
        Put("FurnitureOverhaulPaint/slot3.backup.json", "paint-backup");
        Put("FurnitureOverhaulDisplayOnly/slot3.json", "display");
        var hash = LabStore.Hash(Path.Combine(source, "savedGames_Release3.json")).ToLowerInvariant();
        foreach (var mod in new[] { "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" })
        {
            Put(mod + "/slot3.json", "{\"mainHash\":\"" + hash + "\"}");
            Put(mod + "/" + hash + ".json", "{\"mainHash\":\"" + hash + "\"}");
        }
        var sourceHashes = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(p => p, LabStore.Hash);
        var export = Path.Combine(root, "export");
        Export(source, 3, export);
        var destination = Path.Combine(root, "restored-slot1");
        Restore(export, 1, destination);
        Check(File.ReadAllText(Path.Combine(destination, "ShopLotC_Save1.json")) == "shop-c" &&
            File.ReadAllText(Path.Combine(destination, "Upstairs", "Upstairs_1.json")) == "upstairs" &&
            File.ReadAllText(Path.Combine(destination, "Upstairs_Save1.json")) == "legacy", "Shop C et Upstairs renommés vers le slot demandé");
        Check(File.ReadAllText(Path.Combine(destination, "FurnitureOverhaulPaint", "slot1.backup.json")) == "paint-backup" &&
            File.ReadAllText(Path.Combine(destination, "FurnitureOverhaulDisplayOnly", "slot1.json")) == "display", "Paint, backup et DisplayOnly conservés");
        Check(File.Exists(Path.Combine(destination, "FurnitureOverhaulClaw", hash + ".json")) &&
            File.Exists(Path.Combine(destination, "FurnitureOverhaulCheckout", hash + ".json")), "noms par empreinte inchangés");
        Check(!File.Exists(Path.Combine(destination, "savedGames_Release3.json")) && File.Exists(Path.Combine(destination, "savedGames_Release1.gd")), "slot source absent, JSON et GD remappés");
        Reject(() => Restore(export, 1, Path.Combine(root, "saves")), "dossier saves interdit");
        Reject(() => Restore(export, 1, destination), "écrasement d'un résultat interdit");
        var manifestPath = Path.Combine(export, "manifest.json");
        var manifest = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, manifest.Replace("savedGames_Release3.gd", "../outside.gd"));
        var unsafeTarget = Path.Combine(root, "unsafe-result");
        Reject(() => Restore(export, 1, unsafeTarget), "chemin hors adaptateur refusé");
        Check(!Directory.Exists(unsafeTarget) && !Directory.Exists(unsafeTarget + ".pending"), "requête invalide refusée avant création");
        File.WriteAllText(manifestPath, manifest);
        var native = Path.Combine(export, "files", "savedGames_Release3.json");
        var original = File.ReadAllText(native);
        File.WriteAllText(native, "tampered");
        Reject(() => Restore(export, 0, Path.Combine(root, "tampered-result")), "export altéré refusé");
        File.WriteAllText(native, original);
        var linked = Path.Combine(export, "files", "FurnitureOverhaulClaw", hash + ".json");
        File.Move(linked, linked + ".missing");
        Reject(() => Restore(export, 0, Path.Combine(root, "missing-result")), "snapshot lié manquant refusé");
        Check(sourceHashes.All(p => LabStore.Hash(p.Key) == p.Value), "sources inchangées après les essais");
        Console.WriteLine("Tous les tests de restauration de mods ont réussi : " + root);
        void Put(string relative, string value)
        {
            var path = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value);
        }
        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("ÉCHEC : " + label);
            Console.WriteLine("OK : " + label);
        }
        static void Reject(Action action, string label)
        {
            try { action(); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException) { Console.WriteLine("OK : " + label); return; }
            throw new Exception("ÉCHEC : " + label);
        }
    }

    // Offline rehearsal only: never accepts an existing live save directory.
    public static void Restore(string export, int targetSlot, string destination)
    {
        export = Path.GetFullPath(export);
        destination = Path.GetFullPath(destination);
        if (targetSlot < 0 || targetSlot > 3) throw new ArgumentOutOfRangeException(nameof(targetSlot));
        var parent = Directory.GetParent(destination)!.FullName;
        if (!File.Exists(Path.Combine(parent, ".tcg-test-instance")) || Path.GetFileName(destination) == "saves" || Path.GetFileName(destination) == "original-saves-copy")
            throw new InvalidOperationException("La restauration d'essai exige un nouveau dossier directement dans une instance marquée, hors saves.");
        if (Directory.Exists(destination) || File.Exists(destination) || Directory.Exists(destination + ".pending")) throw new IOException("Le dossier de sortie doit être nouveau.");
        foreach (var directory in new[] { parent, export, Path.Combine(export, "files") })
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Lien de dossier refusé.");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(export, "manifest.json")));
        var manifest = document.RootElement;
        if (manifest.GetProperty("SchemaVersion").GetInt32() != 1) throw new InvalidDataException("Version de manifeste inconnue.");
        var sourceSlot = manifest.GetProperty("SourceSlot").GetInt32();
        if (sourceSlot < 0 || sourceSlot > 3) throw new InvalidDataException("Slot source invalide.");
        var copies = new List<(string Source, string Target, string Hash)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.GetProperty("Files").EnumerateArray())
        {
            var relative = (entry.GetProperty("RelativePath").GetString() ?? "").Replace('\\', '/');
            var mapped = MapPath(relative, sourceSlot, targetSlot);
            if (!seen.Add(mapped)) throw new InvalidDataException("Destination dupliquée.");
            var source = Path.Combine(export, "files", relative.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(source)!;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Lien de fichier refusé.");
            var hash = entry.GetProperty("Sha256").GetString() ?? "";
            if (new FileInfo(source).Length != entry.GetProperty("Length").GetInt64() || LabStore.Hash(source) != hash)
                throw new IOException("Export incomplet ou altéré : " + relative);
            copies.Add((source, mapped, hash));
        }
        if (!seen.Contains($"savedGames_Release{targetSlot}.json") && !seen.Contains($"savedGames_Release{targetSlot}.gd"))
            throw new InvalidDataException("Sauvegarde native principale absente.");
        // Validate associations before creating anything, preserving immutable hash-named files.
        foreach (var association in manifest.GetProperty("Associations").EnumerateArray())
        {
            var mod = association.GetProperty("Mod").GetString();
            var hash = association.GetProperty("MainHash").GetString() ?? "";
            if ((mod != "FurnitureOverhaulCheckout" && mod != "FurnitureOverhaulClaw") || !Regex.IsMatch(hash, "^[a-f0-9]{64}$"))
                throw new InvalidDataException("Association invalide.");
            if (!seen.Contains($"{mod}/{hash}.json")) throw new InvalidDataException("Snapshot lié manquant : " + mod);
            foreach (var relative in new[] { $"{mod}/slot{sourceSlot}.json", $"{mod}/{hash}.json" })
            {
                using var companion = JsonDocument.Parse(File.ReadAllText(Path.Combine(export, "files", relative.Replace('/', Path.DirectorySeparatorChar))));
                if (companion.RootElement.GetProperty("mainHash").GetString() != hash) throw new InvalidDataException("Empreinte interne incohérente.");
            }
        }
        var pending = destination + ".pending";
        Directory.CreateDirectory(pending);
        foreach (var copy in copies)
        {
            var target = Path.Combine(pending, copy.Target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(copy.Source, target, false);
            if (LabStore.Hash(target) != copy.Hash || LabStore.Hash(copy.Source) != copy.Hash) throw new IOException("Copie instable.");
        }
        File.WriteAllText(Path.Combine(pending, ".tcg-mod-restore-lab"), "Offline rehearsal; runtime compatibility NOT validated.");
        File.WriteAllText(Path.Combine(pending, "rapport.md"), $"# Restauration hors jeu\n\n{copies.Count} fichiers vérifiés ; slot {sourceSlot} → {targetSlot}.\n\nNoms par slot adaptés, noms par empreinte conservés. Aucune injection dans saves. Les caches, les hooks, les backups historiques et la normalisation Unity restent à valider.\n");
        Directory.Move(pending, destination);
        Console.WriteLine($"Restauration hors jeu vérifiée : {destination} ({copies.Count} fichiers). Aucun mod activé.");
    }

    private static string MapPath(string relative, int source, int target)
    {
        var fixedPaths = new[] { $"savedGames_Release{source}.json", $"savedGames_Release{source}.gd", $"savedGames_ReleaseBackupFile{source}.json",
            $"ShopLotC_Save{source}.json", $"Upstairs/Upstairs_{source}.json", $"Upstairs_Save{source}.json" };
        if (fixedPaths.Contains(relative, StringComparer.Ordinal))
            return relative.Replace($"ReleaseBackupFile{source}.", $"ReleaseBackupFile{target}.").Replace($"Release{source}.", $"Release{target}.")
                .Replace($"_Save{source}.", $"_Save{target}.").Replace($"Upstairs_{source}.", $"Upstairs_{target}.");
        foreach (var mod in new[] { "FurnitureOverhaulPaint", "FurnitureOverhaulDisplayOnly", "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" })
        {
            if (relative == $"{mod}/slot{source}.json" || relative == $"{mod}/slot{source}.backup.json") return relative.Replace($"/slot{source}.", $"/slot{target}.");
            if ((mod == "FurnitureOverhaulCheckout" || mod == "FurnitureOverhaulClaw") && Regex.IsMatch(relative, "^" + mod + "/[a-f0-9]{64}\\.json$")) return relative;
        }
        throw new InvalidDataException("Fichier hors adaptateurs connus : " + relative);
    }

    public static void Export(string source, int slot, string destination)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        if (Path.GetFileName(source) != "original-saves-copy" || !File.Exists(Path.Combine(Directory.GetParent(source)!.FullName, ".tcg-test-instance")))
            throw new InvalidOperationException("L'audit exige une copie de sauvegardes d'une instance de test.");
        if (Directory.Exists(destination) || Directory.Exists(destination + ".pending")) throw new IOException("Choisir un nouveau dossier de sortie.");
        var pending = destination + ".pending";
        Directory.CreateDirectory(pending);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        var associations = new List<object>();
        foreach (var file in SaveInventory.NativeFiles(source, slot)) files.Add(file);
        Add($"ShopLotC_Save{slot}.json", "Shop C");
        Add($"Upstairs/Upstairs_{slot}.json", "Upstairs");
        Add($"Upstairs_Save{slot}.json", "Upstairs legacy");
        foreach (var directory in new[] { "FurnitureOverhaulPaint", "FurnitureOverhaulDisplayOnly", "FurnitureOverhaulCheckout", "FurnitureOverhaulClaw" })
        {
            var companion = Add($"{directory}/slot{slot}.json", directory);
            Add($"{directory}/slot{slot}.backup.json", directory + " backup");
            if (directory != "FurnitureOverhaulCheckout" && directory != "FurnitureOverhaulClaw") continue;
            if (companion == null) { notes.Add(directory + " : aucun compagnon de slot ; association non déterminée."); continue; }
            using var json = JsonDocument.Parse(File.ReadAllText(companion));
            if (!json.RootElement.TryGetProperty("mainHash", out var field)) { notes.Add(directory + " : mainHash absent."); continue; }
            var hash = field.GetString() ?? "";
            if (!Regex.IsMatch(hash, "^[a-f0-9]{64}$")) throw new InvalidDataException("Empreinte de compagnon invalide : " + directory);
            var linked = Add($"{directory}/{hash}.json", directory + " snapshot lié");
            var primary = Path.Combine(source, $"savedGames_Release{slot}.json");
            var rawMatches = File.Exists(primary) && string.Equals(LabStore.Hash(primary), hash, StringComparison.OrdinalIgnoreCase);
            associations.Add(new { Mod = directory, Slot = slot, MainHash = hash, LinkedFilePresent = linked != null, RawMainJsonHashMatches = rawMatches });
            if (linked != null)
            {
                using var linkedJson = JsonDocument.Parse(File.ReadAllText(linked));
                if (!linkedJson.RootElement.TryGetProperty("mainHash", out var ownHash) || ownHash.GetString() != hash)
                    throw new InvalidDataException("Snapshot lié incohérent : " + directory);
            }
            notes.Add(directory + " : snapshot lié " + (linked != null ? "présent et empreinte interne cohérente" : "MANQUANT") +
                "; SHA-256 du JSON brut " + (rawMatches ? "identique" : "différent/non disponible") + ".");
            notes.Add(directory + " : le mod normalise via Unity JsonUtility avant calcul ; la cohérence avec la partie doit encore être validée dans Unity.");
        }
        var manifest = new List<object>();
        foreach (var file in files.OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(pending, "files", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var hash = LabStore.Hash(file);
            File.Copy(file, target, false);
            if (hash != LabStore.Hash(target) || hash != LabStore.Hash(file)) throw new IOException("Fichier modifié pendant l'export : " + relative);
            manifest.Add(new { RelativePath = relative, Sha256 = hash, Length = new FileInfo(file).Length });
        }
        File.WriteAllText(Path.Combine(pending, "manifest.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, SourceSlot = slot, NativeAndKnownModFilesOnly = true,
            RuntimeCompatibilityValidated = false, Files = manifest, Associations = associations, Notes = notes
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(pending, "rapport.md"), "# Audit des fichiers de mods — slot " + slot +
            "\n\nExport hors jeu, non validé pour une réinjection. " + files.Count + " fichiers copiés avec contrôle SHA-256.\n\n" +
            string.Join("\n", notes.Select(n => "- " + n)) +
            "\n\nLes données de mods non recensés, les caches et l'ordre des hooks ne sont pas couverts par cet export.\n");
        Directory.Move(pending, destination);
        Console.WriteLine("Audit exporté : " + destination + " (" + files.Count + " fichiers). Compatibilité en jeu non validée.");

        string? Add(string relative, string label)
        {
            var path = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { notes.Add(label + " : " + relative + " absent (peut être normal si non utilisé)."); return null; }
            files.Add(path);
            return path;
        }
    }
}
