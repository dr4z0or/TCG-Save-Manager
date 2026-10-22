using System;
using System.Collections.Generic;
using System.IO;

using static TCG.Shared.Lang;
namespace TCG.SaveManager.Core
{
    public static class SaveInventory
    {
        // Explicit native paths only. Mod files require separate, versioned adapters.
        public static IEnumerable<string> NativeFiles(string root, int slot)
        {
            if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot));
            foreach (var name in new[] { $"savedGames_Release{slot}.json", $"savedGames_Release{slot}.gd", $"savedGames_ReleaseBackupFile{slot}.json" })
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path)) yield return path;
            }
        }

        public static string Describe(string root)
        {
            var lines = new List<string> { F("Dossier : {0}", root) };
            for (var slot = 0; slot <= 3; slot++)
            {
                lines.Add(F("Emplacement {0}", slot));
                foreach (var path in NativeFiles(root, slot))
                {
                    var file = new FileInfo(path);
                    lines.Add("  " + F("{0} : {1} octets, UTC {2:O}", file.Name, file.Length, file.LastWriteTimeUtc));
                }
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
