using System;
using System.Collections.Generic;
using System.Text;

namespace AB.WorksetManager.Core
{
    /// <summary>
    /// What makes a workset name acceptable to Revit, checked before Revit is asked, so a list is
    /// reported line by line instead of failing on the first bad name.
    /// </summary>
    public static class WorksetNames
    {
        /// <summary>
        /// Characters Revit refuses in workset names (the same set it refuses in view and element
        /// names). Checked live against Workset.Create - see CLAUDE.md.
        /// </summary>
        public const string ProhibitedCharacters = "\\:{}[]|;<>?`~";

        /// <summary>Revit compares workset names without regard to case.</summary>
        public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        public static bool IsBlank(string name) => string.IsNullOrWhiteSpace(name);

        /// <summary>Leading and trailing spaces removed; never null.</summary>
        public static string Tidy(string name) => (name ?? string.Empty).Trim();

        /// <summary>The prohibited characters a name holds, each once, in order; empty when none.</summary>
        public static string BadCharacters(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var found = new StringBuilder();
            foreach (char c in name)
            {
                bool bad = ProhibitedCharacters.IndexOf(c) >= 0 || char.IsControl(c);
                if (bad && found.ToString().IndexOf(c) < 0) found.Append(c);
            }
            return found.ToString();
        }

        /// <summary>Null when the name is fine, otherwise why not, in words for the user.</summary>
        public static string Problem(string name)
        {
            if (IsBlank(name)) return "The name is empty.";
            string bad = BadCharacters(name);
            if (bad.Length > 0)
            {
                var shown = new List<string>();
                foreach (char c in bad) shown.Add(char.IsControl(c) ? "(tab or control character)" : c.ToString());
                return "Revit does not allow " + string.Join(" ", shown) + " in a workset name.";
            }
            return null;
        }

        /// <summary>
        /// Makes any text into an acceptable name: prohibited characters become '-', runs of spaces
        /// collapse to one, and the ends are trimmed. Empty input gives an empty string.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            bool lastSpace = false;
            foreach (char raw in text)
            {
                char c = ProhibitedCharacters.IndexOf(raw) >= 0 || char.IsControl(raw) ? '-' : raw;
                if (char.IsWhiteSpace(c))
                {
                    if (lastSpace) continue;
                    lastSpace = true;
                    sb.Append(' ');
                    continue;
                }
                lastSpace = false;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
