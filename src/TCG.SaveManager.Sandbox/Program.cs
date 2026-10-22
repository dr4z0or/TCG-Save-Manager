using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

try
{
    if (args.Length == 3 && args[0] == "mod-inventory")
    {
        ModInventory.Write(args[1], args[2]);
        return 0;
    }
    if (args.Length == 4 && args[0] == "preloader-test")
    {
        PreloaderTest.Run(args[1], args[2], args[3]);
        return 0;
    }
    if (args.Length == 3 && args[0] == "mod-hooks")
    {
        ModHooksAudit.Write(args[1], args[2]);
        return 0;
    }
    if (args.Length != 3 || (args[0] != "patch" && args[0] != "verify"))
        throw new ArgumentException("patch|verify <instance copiée> <sauvegardes isolées>");
    var game = Path.GetFullPath(args[1]);
    var saves = Path.GetFullPath(args[2]);
    var instance = Directory.GetParent(game)!.FullName;
    if (!File.Exists(Path.Combine(instance, ".tcg-test-instance")) ||
        game != Path.Combine(instance, "game") || saves != Path.Combine(instance, "saves"))
        throw new InvalidOperationException("Les chemins doivent désigner game et saves dans une instance marquée.");
    foreach (var path in new[] { instance, game, saves })
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Les liens de dossiers sont refusés.");
    var managed = Path.Combine(game, "Card Shop Simulator_Data", "Managed");
    if (args[0] == "patch")
    {
        Modify("UnityEngine.CoreModule.dll", assembly =>
        {
            var getter = Method(assembly, "UnityEngine.Application", "get_persistentDataPath");
            // Replace the native getter in the COPY, before any managed game code runs.
            getter.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
            getter.Body = new MethodBody(getter);
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, saves));
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        });
        Modify("Assembly-CSharp.dll", assembly =>
        {
            foreach (var name in new[] { "SaveGameToCloud", "LoadGameFromCloud" })
                VoidReturn(Method(assembly, "CGameManager", name));
            VoidReturn(Method(assembly, "AchievementManager", "UnlockAchievement"));
        });
        Modify("com.rlabrecque.steamworks.net.dll", assembly =>
        {
            foreach (var name in new[] { "Init", "RestartAppIfNecessary", "IsSteamRunning" })
                FalseReturn(Method(assembly, "Steamworks.SteamAPI", name));
            foreach (var name in new[] { "Shutdown", "RunCallbacks", "ReleaseCurrentThreadMemory" })
                VoidReturn(Method(assembly, "Steamworks.SteamAPI", name));
            FalseReturn(Method(assembly, "Steamworks.SteamUtils", "IsSteamRunningOnSteamDeck"));
        });
    }
    using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "UnityEngine.CoreModule.dll")))
    {
        var getter = Method(assembly, "UnityEngine.Application", "get_persistentDataPath");
        if (getter.IsInternalCall || !getter.HasBody || getter.Body.Instructions.Count != 2 ||
            getter.Body.Instructions[0].OpCode != OpCodes.Ldstr || (string)getter.Body.Instructions[0].Operand != saves ||
            getter.Body.Instructions[1].OpCode != OpCodes.Ret)
            throw new InvalidOperationException("Redirection des sauvegardes non confirmée.");
    }
    using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll")))
        foreach (var name in new[] { "SaveGameToCloud", "LoadGameFromCloud" })
            VerifyReturn(Method(assembly, "CGameManager", name), false);
    using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "com.rlabrecque.steamworks.net.dll")))
    {
        foreach (var name in new[] { "Init", "RestartAppIfNecessary", "IsSteamRunning" })
            VerifyReturn(Method(assembly, "Steamworks.SteamAPI", name), true);
        VerifyReturn(Method(assembly, "Steamworks.SteamUtils", "IsSteamRunningOnSteamDeck"), true);
    }
    if (Directory.GetFiles(game, "*steam_api*.dll", SearchOption.AllDirectories).Length != 0)
        throw new InvalidOperationException("Une bibliothèque Steam native reste présente.");
    Console.WriteLine("OK : chemin isolé " + saves);
    Console.WriteLine("OK : méthodes cloud neutralisées, initialisation Steam refusée, bibliothèque Steam native absente.");
    return 0;

    void Modify(string file, Action<AssemblyDefinition> change)
    {
        var path = Path.Combine(managed, file);
        using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
        change(assembly);
        var pending = path + ".sandbox-pending";
        assembly.Write(pending);
        File.Move(pending, path, true);
    }
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

static MethodDefinition Method(AssemblyDefinition assembly, string type, string name) =>
    assembly.MainModule.Types.Single(t => t.FullName == type).Methods.Single(m => m.Name == name);
static void VoidReturn(MethodDefinition method)
{
    if (method.ReturnType.FullName != "System.Void") throw new InvalidOperationException("Signature inattendue.");
    method.Body = new MethodBody(method);
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
}
static void FalseReturn(MethodDefinition method)
{
    if (method.ReturnType.FullName != "System.Boolean") throw new InvalidOperationException("Signature inattendue.");
    method.Body = new MethodBody(method);
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
}
static void VerifyReturn(MethodDefinition method, bool boolean)
{
    var code = method.Body.Instructions;
    if (code.Count != (boolean ? 2 : 1) || code.Last().OpCode != OpCodes.Ret || (boolean && code[0].OpCode != OpCodes.Ldc_I4_0))
        throw new InvalidOperationException("Protection cloud/Steam non confirmée : " + method.FullName);
}
