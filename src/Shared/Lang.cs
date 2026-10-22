using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace TCG.Shared
{
    // Interface language of the TCG mods: English, or French when the game is in French.
    // Texts are written in French in the code: T("…") and F("… {0}", value). Each source file
    // Foo.cs lists their English version in Foo.en.cs (a string[] field named En_…, French then
    // English, pair after pair). A text missing from the tables stays in French.
    // Every assembly compiles its own copy of this file; the choice is shared through the AppDomain.
    internal static partial class Lang
    {
        public const string SettingKey = "TCG.Language";
        static Dictionary<string, string>? english;
        static bool french, known;
        static int checkedAt;
        static PropertyInfo? gameLanguage;
        static bool gameSearched;

        // "auto" (game language), "en" or "fr". Set by Save Manager (or any TCG mod) from its configuration.
        public static string Setting
        {
            get => AppDomain.CurrentDomain.GetData(SettingKey) as string ?? Environment.GetEnvironmentVariable("TCG_LANGUAGE") ?? "auto";
            set { AppDomain.CurrentDomain.SetData(SettingKey, value); known = false; }
        }

        public static bool French
        {
            get
            {
                var now = Environment.TickCount;
                if (!known || unchecked(now - checkedAt) > 1000) { french = Detect(); checkedAt = now; known = true; }
                return french;
            }
        }

        public static CultureInfo Culture => French ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.GetCultureInfo("en-US");

        public static string T(string french)
        {
            if (French || french == null) return french!;
            return English().TryGetValue(french, out var text) ? text : french;
        }

        public static string F(string french, params object?[] values) => string.Format(Culture, T(french), values);

        // Game language as I2 Localization reports it ("fr", "en-US"…); empty before the game has loaded it.
        public static string GameLanguage()
        {
            try
            {
                if (!gameSearched)
                {
                    gameSearched = true;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var type = assembly.GetType("I2.Loc.LocalizationManager", false);
                        if (type == null) continue;
                        gameLanguage = type.GetProperty("CurrentLanguageCode", BindingFlags.Public | BindingFlags.Static);
                        break;
                    }
                }
                return gameLanguage?.GetValue(null, null) as string ?? "";
            }
            catch (Exception) { return ""; }
        }

        static bool Detect()
        {
            var setting = (Setting ?? "auto").Trim().ToLowerInvariant();
            if (setting.StartsWith("fr", StringComparison.Ordinal)) return true;
            if (setting.StartsWith("en", StringComparison.Ordinal)) return false;
            // The search for I2 is repeated until the game assembly is loaded.
            if (gameLanguage == null) gameSearched = false;
            return GameLanguage().StartsWith("fr", StringComparison.OrdinalIgnoreCase);
        }

        static Dictionary<string, string> English()
        {
            var table = english;
            if (table != null) return table;
            table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in typeof(Lang).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string[]) || !field.Name.StartsWith("En_", StringComparison.Ordinal)) continue;
                if (!(field.GetValue(null) is string[] pairs)) continue;
                for (var i = 0; i + 1 < pairs.Length; i += 2) table[pairs[i]] = pairs[i + 1];
            }
            return english = table;
        }
    }
}
