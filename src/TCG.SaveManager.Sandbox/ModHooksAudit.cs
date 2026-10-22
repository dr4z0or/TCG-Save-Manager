using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Mono.Cecil;

internal static class ModHooksAudit
{
    public static void Write(string plugins, string output)
    {
        using var writer = new StreamWriter(output, false);
        writer.WriteLine("Static IL inspection only; no assembly code executed. Runtime compatibility not validated.");
        foreach (var relative in new[] { "ShopCExpansion/ShopCExpansion.dll", "Upstairs/Upstairs.dll", "FurnitureOverhaul/FurnitureOverhaul.dll", "StaffOverhaul/StaffOverhaul.dll" })
        {
            var path = Path.Combine(plugins, relative.Replace('/', Path.DirectorySeparatorChar));
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true });
            writer.WriteLine("\nASSEMBLY " + relative + " " + assembly.Name.Version);
            using (var stream = File.OpenRead(path)) writer.WriteLine("SHA256 " + Convert.ToHexString(SHA256.HashData(stream)));
            foreach (var reference in assembly.MainModule.AssemblyReferences) writer.WriteLine("DEPENDENCY " + reference.FullName);
            foreach (var type in AllTypes(assembly.MainModule.Types))
            {
                foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName.Contains("Harmony") || a.AttributeType.Name.StartsWith("BepIn")))
                    writer.WriteLine("TYPE ATTRIBUTE " + type.FullName + " " + Attribute(attribute));
                foreach (var field in type.Fields.Where(f => f.IsStatic)) writer.WriteLine("STATIC FIELD " + field.FullName);
                var patchedType = type.CustomAttributes.Any(a => a.AttributeType.FullName.Contains("HarmonyPatch"));
                foreach (var method in type.Methods)
                {
                    var patchedMethod = method.CustomAttributes.Any(a => a.AttributeType.FullName.Contains("Harmony"));
                    var lifecycle = new[] { "Load", "Reset", "Clear", "NewGame", "OnLevel", "Scene", "Bootstrap", "OnDestroy" }.Any(n => method.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                    var saveStateType = new[] { "ShopCWorldBuilder", "UpWorld", "CheckoutDisplay", "ClawSave", "PaintSave", "DisplayOnly", "FinishOrder", "FinishPreview", "Refinish", "FurnitureShopScreen", "FurnitureShopHook", "CoopBridge" }.Any(n => type.Name == n);
                    var entry = type.Name == "Plugin";
                    if (assembly.Name.Name != "StaffOverhaul" && !patchedType && !patchedMethod && !lifecycle && !saveStateType && !entry) continue;
                    writer.WriteLine("\nMETHOD " + method.FullName);
                    foreach (var attribute in method.CustomAttributes) writer.WriteLine("  ATTRIBUTE " + Attribute(attribute));
                    if (method.HasBody) foreach (var instruction in method.Body.Instructions) writer.WriteLine("  " + instruction);
                }
            }
        }
        Console.WriteLine("Inspection statique des hooks écrite : " + Path.GetFullPath(output));
    }
    private static string Attribute(CustomAttribute attribute) => attribute.AttributeType.FullName + "(" +
        string.Join(", ", attribute.ConstructorArguments.Select(a => a.Value?.ToString() ?? "null")) + ")";
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
    {
        foreach (var type in roots) { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; }
    }
}
