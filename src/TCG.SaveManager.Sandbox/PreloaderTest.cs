using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TCG.SaveManager.Preloader;

internal static class PreloaderTest
{
    public static void Run(string managed, string saves, string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Nouveau dossier de résultats requis.");
        Directory.CreateDirectory(output);
        foreach (var name in Patcher.TargetDLLs)
        {
            var source = Path.Combine(managed, name);
            var before = Hash(source);
            using (var assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { InMemory = true }))
            {
                try { Patcher.PatchIsolated(assembly, "relative/saves"); throw new Exception("Chemin relatif accepté."); }
                catch (InvalidOperationException) { }
                Patcher.PatchIsolated(assembly, saves);
                assembly.Write(Path.Combine(output, name));
            }
            using (var patched = AssemblyDefinition.ReadAssembly(Path.Combine(output, name)))
            {
                if (name == "UnityEngine.CoreModule.dll")
                {
                    var getter = patched.MainModule.GetType("UnityEngine.Application").Methods.Single(m => m.Name == "get_persistentDataPath");
                    if (getter.IsInternalCall || getter.Body.Instructions.Count != 2 ||
                        getter.Body.Instructions[0].OpCode != OpCodes.Ldstr || (string)getter.Body.Instructions[0].Operand != Path.GetFullPath(saves))
                        throw new Exception("Redirection invalide.");
                }
                else foreach (var method in patched.MainModule.GetType("CGameManager").Methods.Where(m => m.Name == "SaveGameToCloud" || m.Name == "LoadGameFromCloud"))
                    if (method.Body.Instructions.Count != 1 || method.Body.Instructions[0].OpCode != OpCodes.Ret) throw new Exception("Cloud non neutralisé.");
            }
            if (before != Hash(source)) throw new Exception("Source modifiée.");
            Console.WriteLine("OK : transformation " + name + ", source inchangée");
        }
        Console.WriteLine("Préchargeur vérifié hors jeu. Copies de contrôle uniquement : " + output);
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
