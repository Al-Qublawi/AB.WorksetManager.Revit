using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AB.WorksetManager.Addin.Infrastructure
{
    /// <summary>
    /// The user's own choices (naming templates, last tab, window size) in
    /// %LOCALAPPDATA%\AB Adv Tools\WorksetManager\settings.ini. A missing or broken file means defaults.
    /// </summary>
    internal static class UserSettings
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AB Adv Tools", "WorksetManager");
        private static string FilePath => Path.Combine(Folder, "settings.ini");

        private static Dictionary<string, string> _values;

        public const string RevitLinkTemplate = "RevitLinkTemplate";
        public const string CadLinkTemplate = "CadLinkTemplate";
        public const string SingleViewTemplate = "SingleViewTemplate";
        public const string CombinedViewName = "CombinedViewName";
        public const string LastTab = "LastTab";
        public const string WindowWidth = "WindowWidth";
        public const string WindowHeight = "WindowHeight";
        public const string LastFolder = "LastFolder";

        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { RevitLinkTemplate, "Z-Link-RVT-{FileName}" },
            { CadLinkTemplate, "Z-Link-CAD-{FileName}" },
            { SingleViewTemplate, "3D - WS - {Workset}" },
            { CombinedViewName, "3D - WS - Combined" },
            { LastTab, "0" }
        };

        public static string Get(string key)
        {
            Load();
            if (_values.TryGetValue(key, out string v) && v.Length > 0) return v;
            return Defaults.TryGetValue(key, out string d) ? d : string.Empty;
        }

        public static string Default(string key) => Defaults.TryGetValue(key, out string d) ? d : string.Empty;

        public static int GetInt(string key, int fallback) => int.TryParse(Get(key), out int n) ? n : fallback;

        public static void Set(string key, string value)
        {
            Load();
            _values[key] = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }

        public static void Save()
        {
            try
            {
                Load();
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder("# AB Worksets Manager - your settings. Delete this file to return to the defaults.\r\n");
                foreach (var pair in _values.OrderBy(p => p.Key)) sb.Append(pair.Key).Append('=').Append(pair.Value).Append("\r\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn("Settings not saved: " + ex.Message); }
        }

        private static void Load()
        {
            if (_values != null) return;
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq > 0) _values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Log.Warn("Settings not read: " + ex.Message); }
        }
    }
}
