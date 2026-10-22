using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace TCG.SaveManager.Preloader
{
    // BepInEx 5 applies these changes to assemblies in memory before plugins load.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new[] { "UnityEngine.CoreModule.dll", "Assembly-CSharp.dll" };
        public static void Patch(AssemblyDefinition assembly)
        {
            var config = Path.Combine(Path.GetDirectoryName(typeof(Patcher).Assembly.Location)!, "TCGSaveManager.path.txt");
            try
            {
                var saves = File.Exists(config) ? File.ReadAllText(config).Trim() :
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TCGSM", "saves");
                InitializeSpace(saves);
                PatchIsolated(assembly, saves);
            }
            catch (Exception ex)
            {
                // BepInEx may otherwise skip a failed patcher and run with original paths.
                Console.Error.WriteLine("TCG Save Manager: the isolated save folder was refused, the game is closed to protect your saves. " + ex);
                Environment.Exit(1);
                throw;
            }
        }
        public static void InitializeSpace(string saves)
        {
            if (!Path.IsPathRooted(saves) || Path.GetFileName(saves) != "saves") throw new InvalidOperationException("An absolute saves folder is required.");
            var root = Directory.GetParent(Path.GetFullPath(saves))!.FullName;
            for (var d = new DirectoryInfo(root); d != null; d = d.Parent)
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Folder links are refused.");
            if (File.Exists(Path.Combine(root, ".tcg-test-instance")) && File.Exists(Path.Combine(saves, ".tcg-sandbox"))) return;
            if (!File.Exists(Path.Combine(root, ".tcg-managed-instance")))
            {
                if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("The folder " + root + " already exists and was not created by Save Manager: rename or empty it.");
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, ".tcg-managed-instance"), "TCG Save Manager isolated workspace v1");
            }
            Directory.CreateDirectory(saves);
            File.WriteAllText(Path.Combine(saves, ".tcg-managed-saves"), "TCG Save Manager isolated saves v1");
        }
        public static void PatchIsolated(AssemblyDefinition assembly, string saves)
        {
            if (!Path.IsPathRooted(saves)) throw new InvalidOperationException("An absolute path is required.");
            saves = Path.GetFullPath(saves);
            var root = Directory.GetParent(saves)!.FullName;
            var sandbox = File.Exists(Path.Combine(root, ".tcg-test-instance")) && File.Exists(Path.Combine(saves, ".tcg-sandbox"));
            var managed = File.Exists(Path.Combine(root, ".tcg-managed-instance")) && File.Exists(Path.Combine(saves, ".tcg-managed-saves"));
            if (Path.GetFileName(saves) != "saves" || (!sandbox && !managed)) throw new InvalidOperationException("Isolated save folder not initialised.");
            for (var directory = new DirectoryInfo(saves); directory != null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Folder links are refused.");
            var module = assembly.MainModule;
            if (assembly.Name.Name == "UnityEngine.CoreModule")
            {
                var getter = module.GetType("UnityEngine.Application")?.Methods.SingleOrDefault(m => m.Name == "get_persistentDataPath" && m.Parameters.Count == 0)
                    ?? throw new MissingMethodException("Unity save path not found.");
                if (getter.ReturnType.FullName != "System.String") throw new InvalidOperationException("Unknown Unity signature.");
                getter.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
                getter.Body = new MethodBody(getter);
                getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, saves));
                getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            }
            else if (assembly.Name.Name == "Assembly-CSharp")
            {
                var type = module.GetType("CGameManager") ?? throw new MissingMemberException("CGameManager absent.");
                var methods = new[] { "SaveGameToCloud", "LoadGameFromCloud" }.Select(name =>
                    type.Methods.SingleOrDefault(m => m.Name == name && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void")
                    ?? throw new MissingMethodException("Unknown cloud save signature: " + name)).ToArray();
                foreach (var method in methods)
                {
                    method.Body = new MethodBody(method);
                    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                }
            }
            else throw new InvalidOperationException("Assembly outside the preloader scope.");
        }
    }
}
