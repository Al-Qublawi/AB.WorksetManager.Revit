using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AB.WorksetManager.Core
{
    /// <summary>
    /// Names built from a pattern such as "Z-Link-RVT-{FileName}" or "3D - WS - {Workset}".
    /// Tokens are case-insensitive; UnknownTokens lists typing mistakes so the window can say so.
    /// The result is always a name Revit accepts (prohibited characters become '-').
    /// </summary>
    public static class NameTemplate
    {
        private static readonly Regex Token = new Regex(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

        public static string Expand(string template, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
                foreach (var pair in values) lookup[pair.Key] = pair.Value ?? string.Empty;

            string expanded = Token.Replace(template, m => lookup.TryGetValue(m.Groups[1].Value, out string v) ? v : m.Value);
            return WorksetNames.Sanitize(expanded);
        }

        /// <summary>Tokens in a template that are not among the known ones (compared without case).</summary>
        public static List<string> UnknownTokens(string template, IEnumerable<string> known)
        {
            var set = new HashSet<string>(known ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var unknown = new List<string>();
            if (string.IsNullOrEmpty(template)) return unknown;
            foreach (Match m in Token.Matches(template))
                if (!set.Contains(m.Groups[1].Value) && !unknown.Contains(m.Groups[1].Value)) unknown.Add(m.Groups[1].Value);
            return unknown;
        }

        /// <summary>"Arch-Model.rvt" -> "Arch-Model"; "Site plan.dwg" -> "Site plan"; names without an extension unchanged.</summary>
        public static string FileStem(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            string name = fileName.Trim();
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0) name = name.Substring(slash + 1);
            int dot = name.LastIndexOf('.');
            if (dot > 0)
            {
                string ext = name.Substring(dot + 1);
                if (ext.Length >= 2 && ext.Length <= 4 && IsAlphanumeric(ext)) name = name.Substring(0, dot);
            }
            return name.Trim();
        }

        /// <summary>
        /// A name not yet in use: the name itself, else "name (2)", "name (3)"...
        /// </summary>
        public static string Unique(string name, Func<string, bool> inUse)
        {
            if (inUse == null || !inUse(name)) return name;
            for (int i = 2; i < 10000; i++)
            {
                string candidate = name + " (" + i + ")";
                if (!inUse(candidate)) return candidate;
            }
            return name + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")";
        }

        private static bool IsAlphanumeric(string s)
        {
            foreach (char c in s) if (!char.IsLetterOrDigit(c)) return false;
            return true;
        }
    }

    /// <summary>CSV text Excel opens directly (UTF-8 with BOM, comma separated, quoted where needed, names Excel would misread guarded).</summary>
    public static class Csv
    {
        public static string Line(IEnumerable<string> cells)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (string cell in cells)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Escape(cell));
            }
            return sb.ToString();
        }

        public static string Escape(string cell)
        {
            string value = cell ?? string.Empty;
            // Excel would turn "+ Link_A" into a formula (#NAME?), "01" into 1 and "3-4" into a date: such values
            // are written as ="..." - Excel shows the text, and NameList reads it back unchanged.
            if (ExcelText.NeedsGuard(value)) value = ExcelText.Guard(value);
            bool quote = value.IndexOfAny(new[] { ',', ';', '"', '\r', '\n', '\t' }) >= 0 || value.StartsWith(" ") || value.EndsWith(" ");
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static void Write(string path, IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows) sb.Append(Line(row)).Append("\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
