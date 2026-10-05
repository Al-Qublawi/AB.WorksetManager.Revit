using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AB.WorksetManager.Core
{
    /// <summary>One line of a workset list: a name, and optionally a new name and a default visibility.</summary>
    public sealed class NameRow
    {
        public NameRow(int line, string name, string newName, bool? visible)
        {
            Line = line;
            Name = WorksetNames.Tidy(name);
            NewName = WorksetNames.Tidy(newName);
            Visible = visible;
        }

        /// <summary>1-based line in the source text, for messages.</summary>
        public int Line { get; }
        public string Name { get; }
        public string NewName { get; }
        /// <summary>"Visible in all views" from the list, or null when the list does not say.</summary>
        public bool? Visible { get; }

        public override string ToString() => Line + ": " + Name + (NewName.Length > 0 ? " -> " + NewName : string.Empty);
    }

    /// <summary>How columns are separated in the text being read.</summary>
    public enum ListFormat
    {
        /// <summary>
        /// Plain text or text pasted from Excel: one name per line, a TAB starts the next column.
        /// Commas and semicolons stay part of the name.
        /// </summary>
        Text,

        /// <summary>A .csv file: comma- or semicolon-separated (whichever the first line uses), quotes honoured.</summary>
        Csv
    }

    /// <summary>
    /// Reads a list of worksets from a .txt file, a .csv file or text pasted from Excel.
    ///
    ///   Column 1  workset name (current name, for Rename)
    ///   Column 2  new name (Rename) - or Yes/No for "Visible in all views" (Create)
    ///   Column 3  Yes/No for "Visible in all views"
    ///
    /// A first line whose cells are column titles (Name, New Name, Visible...) is recognised and skipped,
    /// and then decides which column is which. Blank lines and lines starting with # or // are ignored.
    /// </summary>
    public static class NameList
    {
        private static readonly string[] NameTitles = { "name", "workset", "workset name", "worksets", "current name", "current", "old name", "from" };
        private static readonly string[] NewNameTitles = { "new name", "new", "new workset name", "rename to", "to", "renamed" };
        private static readonly string[] VisibleTitles = { "visible", "visible in all views", "visible by default", "visibility", "default visibility" };

        public static ListFormat FormatForFile(string path) =>
            path != null && path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ListFormat.Csv : ListFormat.Text;

        public static List<NameRow> Parse(string text, ListFormat format)
        {
            var rows = new List<NameRow>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            char delimiter = format == ListFormat.Csv ? CsvDelimiter(lines) : '\t';

            int nameCol = 0, newCol = 1, visibleCol = 2;
            bool headerSeen = false;
            bool columnTwoIsVisibility = false;
            var parsed = new List<KeyValuePair<int, List<string>>>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                List<string> cells = format == ListFormat.Csv ? SplitCsv(line, delimiter) : line.Split('\t').ToList();

                if (!headerSeen && parsed.Count == 0 && LooksLikeHeader(cells))
                {
                    headerSeen = true;
                    nameCol = IndexOf(cells, NameTitles, 0);
                    newCol = IndexOf(cells, NewNameTitles, -1);
                    visibleCol = IndexOf(cells, VisibleTitles, -1);
                    continue;
                }
                parsed.Add(new KeyValuePair<int, List<string>>(i + 1, cells));
            }

            // Without titles, a second column holding only Yes/No values is the visibility of a Create list.
            if (!headerSeen)
            {
                var second = parsed.Where(p => p.Value.Count > 1 && !string.IsNullOrWhiteSpace(p.Value[1])).Select(p => p.Value[1]).ToList();
                if (second.Count > 0 && second.All(v => ParseYesNo(v).HasValue))
                {
                    columnTwoIsVisibility = true;
                    newCol = -1;
                    visibleCol = 1;
                }
            }

            foreach (var p in parsed)
            {
                List<string> cells = p.Value;
                string name = Cell(cells, nameCol);
                string newName = Cell(cells, newCol);
                bool? visible = ParseYesNo(Cell(cells, visibleCol));
                if (columnTwoIsVisibility) newName = string.Empty;
                rows.Add(new NameRow(p.Key, name, newName, visible));
            }
            return rows;
        }

        /// <summary>Yes/No, True/False, Y/N, 1/0, On/Off, Visible/Hidden; null for anything else.</summary>
        public static bool? ParseYesNo(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            switch (value.Trim().ToLowerInvariant())
            {
                case "yes": case "y": case "true": case "1": case "on": case "visible": case "x": return true;
                case "no": case "n": case "false": case "0": case "off": case "hidden": return false;
                default: return null;
            }
        }

        private static string Cell(List<string> cells, int index) =>
            index >= 0 && index < cells.Count ? cells[index] ?? string.Empty : string.Empty;

        private static bool LooksLikeHeader(List<string> cells)
        {
            if (cells.Count == 0) return false;
            string first = Norm(cells[0]);
            if (NameTitles.Contains(first)) return true;
            // "New Name" or "Visible" anywhere, with a recognised first cell or a single known title row.
            return cells.Skip(1).Any(c => NewNameTitles.Contains(Norm(c)) || VisibleTitles.Contains(Norm(c))) &&
                   (first.Length == 0 || NameTitles.Contains(first));
        }

        private static int IndexOf(List<string> cells, string[] titles, int fallback)
        {
            for (int i = 0; i < cells.Count; i++)
                if (titles.Contains(Norm(cells[i]))) return i;
            return fallback;
        }

        private static string Norm(string s) => (s ?? string.Empty).Trim().Trim('"').Trim().ToLowerInvariant();

        private static char CsvDelimiter(string[] lines)
        {
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int commas = 0, semis = 0, tabs = 0;
                bool quoted = false;
                foreach (char c in line)
                {
                    if (c == '"') quoted = !quoted;
                    else if (!quoted && c == ',') commas++;
                    else if (!quoted && c == ';') semis++;
                    else if (!quoted && c == '\t') tabs++;
                }
                if (tabs > 0 && tabs >= commas && tabs >= semis) return '\t';
                return semis > commas ? ';' : ',';
            }
            return ',';
        }

        /// <summary>Splits one CSV line; "a ""quoted"" value" and delimiters inside quotes are honoured.</summary>
        public static List<string> SplitCsv(string line, char delimiter)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"' && cell.ToString().Trim().Length == 0)
                {
                    cell.Clear();
                    quoted = true;
                }
                else if (c == delimiter)
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }
    }
}
