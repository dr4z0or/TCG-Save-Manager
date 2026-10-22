using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Checks the English tables of the TCG mods: every T("…")/F("…") text of Foo.cs has its English
// version in Foo.en.cs with the same {n} placeholders, no table keeps unused texts, and every
// project compiling Foo.cs also compiles Foo.en.cs and Shared/Lang.cs.
static class Program
{
    static readonly Regex Call = new Regex(@"(?<![\w.])(T|F)\(\s*(\$?)""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    static readonly Regex Literal = new Regex(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    static readonly Regex Slot = new Regex(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);
    static readonly Regex Table = new Regex(@"static\s+readonly\s+string\[\]\s+En_\w+\s*=\s*\{(.*?)\};", RegexOptions.Compiled | RegexOptions.Singleline);
    static int errors, texts;

    static int Main(string[] args)
    {
        var src = Path.GetFullPath(args.Length > 0 ? args[0] : "src");
        RuntimeCheck();
        foreach (var file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (file.EndsWith(".en.cs", StringComparison.Ordinal) || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            if (file.EndsWith(Path.Combine("Shared", "Lang.cs"), StringComparison.Ordinal) || file.Contains("TCG.Lang.Check")) continue;
            CheckFile(src, file);
        }
        foreach (var project in Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories))
            if (!project.Contains("TCG.Lang.Check")) CheckProject(src, project);
        Console.WriteLine(errors == 0 ? $"Textes anglais : {texts} textes vérifiés, aucune erreur." : $"Textes anglais : {errors} erreur(s).");
        return errors == 0 ? 0 : 1;
    }

    // Lang itself, with one real table compiled into this program.
    static void RuntimeCheck()
    {
        TCG.Shared.Lang.Setting = "en";
        if (TCG.Shared.Lang.T("Annuler") != "Cancel" || TCG.Shared.Lang.F("Boutique {0}", 3) != "Shop 3" || TCG.Shared.Lang.T("absent du dictionnaire") != "absent du dictionnaire")
            Error("Lang : anglais incorrect.");
        TCG.Shared.Lang.Setting = "fr";
        if (TCG.Shared.Lang.T("Annuler") != "Annuler" || TCG.Shared.Lang.F("{0:0.0} Go", 1.5) != "1,5 Go" || !TCG.Shared.Lang.French)
            Error("Lang : français incorrect.");
        TCG.Shared.Lang.Setting = "auto";
        if (TCG.Shared.Lang.French) Error("Lang : sans le jeu, auto doit donner l'anglais.");
    }

    static void Error(string message) { errors++; Console.WriteLine("ERREUR : " + message); }

    static void CheckFile(string src, string file)
    {
        var code = File.ReadAllText(file);
        var used = new List<string>();
        foreach (Match m in Call.Matches(code))
        {
            if (m.Groups[2].Value == "$") { Error($"{Rel(src, file)} : texte interpolé dans {m.Groups[1].Value}(), utiliser F(\"… {{0}}\", valeur)."); continue; }
            used.Add(Unescape(m.Groups[3].Value));
        }
        var tablePath = Path.ChangeExtension(file, null) + ".en.cs";
        if (used.Count == 0) { if (File.Exists(tablePath)) Error($"{Rel(src, tablePath)} : aucun texte à traduire dans {Path.GetFileName(file)}."); return; }
        if (!File.Exists(tablePath)) { Error($"{Rel(src, file)} : {used.Distinct().Count()} texte(s) sans table {Path.GetFileName(tablePath)}."); return; }
        var table = Pairs(src, tablePath);
        foreach (var text in used.Distinct())
        {
            texts++;
            if (!table.TryGetValue(text, out var english)) { Error($"{Rel(src, file)} : sans anglais : \"{text}\""); continue; }
            var a = Slot.Matches(text).Cast<Match>().Select(x => x.Value).OrderBy(x => x).ToArray();
            var b = Slot.Matches(english).Cast<Match>().Select(x => x.Value).OrderBy(x => x).ToArray();
            if (!a.SequenceEqual(b)) Error($"{Rel(src, tablePath)} : emplacements différents pour \"{text}\"");
            if (english.Trim().Length == 0) Error($"{Rel(src, tablePath)} : anglais vide pour \"{text}\"");
        }
        foreach (var unused in table.Keys.Except(used)) Error($"{Rel(src, tablePath)} : texte inutilisé \"{unused}\"");
    }

    static Dictionary<string, string> Pairs(string src, string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var tables = Table.Matches(File.ReadAllText(path));
        if (tables.Count != 1) { Error($"{Rel(src, path)} : une table En_… attendue."); return result; }
        var items = Literal.Matches(tables[0].Groups[1].Value).Cast<Match>().Select(m => Unescape(m.Groups[1].Value)).ToList();
        if (items.Count % 2 != 0) Error($"{Rel(src, path)} : nombre impair de textes.");
        for (var i = 0; i + 1 < items.Count; i += 2)
            if (!result.ContainsKey(items[i])) result[items[i]] = items[i + 1];
            else Error($"{Rel(src, path)} : texte en double \"{items[i]}\"");
        return result;
    }

    static void CheckProject(string src, string project)
    {
        var dir = Path.GetDirectoryName(project)!;
        var xml = File.ReadAllText(project).Replace('\\', '/');
        var compiled = Directory.GetFiles(dir, "*.cs").Select(Path.GetFullPath).ToList();
        foreach (Match m in Regex.Matches(xml, @"<Compile\s+Include=""([^""]+)"""))
        {
            var include = Path.GetFullPath(Path.Combine(dir, m.Groups[1].Value));
            if (Path.GetFileName(include).Contains("*"))
                compiled.AddRange(Directory.GetFiles(Path.GetDirectoryName(include)!, Path.GetFileName(include)).Select(Path.GetFullPath));
            else compiled.Add(include);
        }
        var usesLang = false;
        foreach (var file in compiled.Where(f => !f.EndsWith(".en.cs", StringComparison.Ordinal)).Distinct())
        {
            if (!File.Exists(file) || file.EndsWith(Path.Combine("Shared", "Lang.cs"), StringComparison.Ordinal)) continue;
            if (Call.IsMatch(File.ReadAllText(file))) usesLang = true;
            var table = Path.ChangeExtension(file, null) + ".en.cs";
            if (File.Exists(table) && !compiled.Contains(Path.GetFullPath(table)))
                Error($"{Rel(src, project)} : compile {Path.GetFileName(file)} sans {Path.GetFileName(table)}.");
        }
        if (usesLang && !compiled.Any(f => f.EndsWith(Path.Combine("Shared", "Lang.cs"), StringComparison.Ordinal)))
            Error($"{Rel(src, project)} : Shared/Lang.cs non compilé.");
    }

    static string Rel(string src, string path) => path.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar);

    static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            var c = s[++i];
            switch (c)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '0': sb.Append('\0'); break;
                case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
