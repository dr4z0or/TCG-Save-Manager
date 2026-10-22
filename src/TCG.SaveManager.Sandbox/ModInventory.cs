using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;

internal static class ModInventory
{
    public static void Write(string plugins, string output)
    {
        plugins = Path.GetFullPath(plugins);
        if (File.Exists(output)) throw new IOException("Inventaire existant : choisir un nouveau fichier.");
        var entries = new List<object>();
        var failures = new List<object>();
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(Directory.GetParent(plugins)!.FullName, "core"));
        resolver.AddSearchDirectory(Path.Combine(Directory.GetParent(Directory.GetParent(plugins)!.FullName)!.FullName, "Card Shop Simulator_Data", "Managed"));
        foreach (var path in Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(plugins, path);
            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
                using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                foreach (var type in Types(assembly.MainModule.Types))
                {
                    var metadata = type.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                    if (metadata == null) continue;
                    var dependencies = type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency").Select(a =>
                    {
                        var second = a.ConstructorArguments.Count > 1 ? a.ConstructorArguments[1] : default;
                        var flags = second.Type?.FullName == "System.String" ? 1 : second.Value == null ? 1 : Convert.ToInt32(second.Value);
                        return new { Id = a.ConstructorArguments[0].Value?.ToString(), Required = (flags & 1) != 0,
                            MinimumVersion = second.Type?.FullName == "System.String" ? second.Value?.ToString() : null };
                    }).ToArray();
                    entries.Add(new { Id = metadata.ConstructorArguments[0].Value?.ToString(), Name = metadata.ConstructorArguments[1].Value?.ToString(),
                        Version = metadata.ConstructorArguments[2].Value?.ToString(), Assembly = relative, Type = type.FullName, Sha256 = hash,
                        Dependencies = dependencies,
                        Incompatibilities = type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInIncompatibility")
                            .Select(a => a.ConstructorArguments[0].Value?.ToString()).ToArray(),
                        Processes = type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInProcess")
                            .Select(a => a.ConstructorArguments[0].Value?.ToString()).ToArray(),
                        Essential = metadata.ConstructorArguments[0].Value?.ToString() == "local.tcg.savemanager.diagnostics" });
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is IOException || ex is ArgumentException)
            { failures.Add(new { Assembly = relative, Error = ex.Message }); }
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { SchemaVersion = 1, Plugins = entries, Unreadable = failures }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Inventaire statique : {entries.Count} plugins, {failures.Count} DLL illisibles. Aucun plugin exécuté ou modifié.");
    }
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
    }
}
