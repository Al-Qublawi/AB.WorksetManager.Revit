using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AB.WorksetManager.Core
{
    /// <summary>
    /// Excel workbooks (.xlsx) written and read with no library (a zip of a few XML parts) - add-ins that bring
    /// their own spreadsheet library clash with others that bring another version.
    ///
    /// Every cell is written as TEXT and every column is formatted as Text ("@"), so Excel never turns a
    /// workset name into a formula ("+ Link_..." -> #NAME?), a number ("01" -> 1) or a date ("3-4" -> 3 Apr) -
    /// not even a new name the user types into the sheet afterwards.
    /// </summary>
    public static class Xlsx
    {
        public static void Write(string path, string sheetName, IList<string> headers, IEnumerable<IList<string>> rows)
        {
            var data = rows.ToList();
            using (FileStream stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Part(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");
                Part(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Part(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets><sheet name=\"" + Escape(SheetName(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Part(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                // Style 1 = Text format (numFmt 49 "@"); style 2 = Text + bold header on a light fill.
                Part(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");
                Part(zip, "xl/worksheets/sheet1.xml", SheetXml(headers, data));
            }
        }

        private static string SheetXml(IList<string> headers, List<IList<string>> rows)
        {
            int columns = Math.Max(headers.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
               .Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView></sheetViews>");
            xml.Append("<sheetFormatPr defaultRowHeight=\"15\"/><cols>");
            for (int c = 0; c < columns; c++)
            {
                int longest = c < headers.Count ? (headers[c] ?? "").Length : 0;
                foreach (IList<string> r in rows) if (c < r.Count && r[c] != null) longest = Math.Max(longest, r[c].Length);
                double width = Math.Max(12, Math.Min(80, longest + 3));
                // style="1": the whole column is Text, so names typed later stay text too.
                xml.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1).Append("\" width=\"")
                   .Append(width.ToString("0.#", CultureInfo.InvariantCulture)).Append("\" style=\"1\" customWidth=\"1\"/>");
            }
            xml.Append("</cols><sheetData>");
            WriteRow(xml, 1, headers, 2);
            for (int r = 0; r < rows.Count; r++) WriteRow(xml, r + 2, rows[r], 1);
            xml.Append("</sheetData>");
            if (columns > 0) xml.Append("<autoFilter ref=\"A1:").Append(Column(columns - 1)).Append(rows.Count + 1).Append("\"/>");
            xml.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            xml.Append("</worksheet>");
            return xml.ToString();
        }

        private static void WriteRow(StringBuilder xml, int row, IList<string> cells, int style)
        {
            xml.Append("<row r=\"").Append(row).Append("\">");
            for (int c = 0; c < cells.Count; c++)
            {
                string text = Clean(cells[c]);
                xml.Append("<c r=\"").Append(Column(c)).Append(row).Append("\" s=\"").Append(style).Append("\" t=\"inlineStr\"><is><t");
                if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[text.Length - 1]))) xml.Append(" xml:space=\"preserve\"");
                xml.Append('>').Append(Escape(text)).Append("</t></is></c>");
            }
            xml.Append("</row>");
        }

        // ------------------------------------------------------------------ reading

        /// <summary>The first worksheet as rows of cell text (1-based row number, cells by column). Empty rows are left out.</summary>
        public static List<KeyValuePair<int, List<string>>> Read(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                List<string> shared = ReadSharedStrings(zip);
                string sheetPath = FirstSheetPath(zip);
                ZipArchiveEntry sheet = zip.GetEntry(sheetPath) ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
                if (sheet == null) throw new InvalidDataException("The workbook has no worksheet.");

                var rows = new List<KeyValuePair<int, List<string>>>();
                var doc = Load(sheet);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                int fallbackRow = 0;
                foreach (XmlElement row in doc.SelectNodes("//m:sheetData/m:row", ns))
                {
                    fallbackRow++;
                    int rowNumber = int.TryParse(row.GetAttribute("r"), out int rn) ? rn : fallbackRow;
                    fallbackRow = rowNumber;
                    var cells = new List<string>();
                    int next = 0;
                    foreach (XmlElement c in row.SelectNodes("m:c", ns))
                    {
                        int col = ColumnIndex(c.GetAttribute("r"));
                        if (col < 0) col = next;
                        while (cells.Count < col) cells.Add(string.Empty);
                        cells.Add(CellText(c, shared, ns));
                        next = col + 1;
                    }
                    if (cells.Any(s => !string.IsNullOrWhiteSpace(s))) rows.Add(new KeyValuePair<int, List<string>>(rowNumber, cells));
                }
                return rows;
            }
        }

        private static string CellText(XmlElement c, List<string> shared, XmlNamespaceManager ns)
        {
            string type = c.GetAttribute("t");
            if (type == "inlineStr")
                return string.Concat(c.SelectNodes(".//m:t", ns).Cast<XmlNode>().Select(n => n.InnerText));
            string value = c.SelectSingleNode("m:v", ns)?.InnerText ?? string.Empty;
            if (type == "s" && int.TryParse(value, out int index)) return index >= 0 && index < shared.Count ? shared[index] : string.Empty;
            if (type == "b") return value == "1" ? "TRUE" : "FALSE";
            return value;   // "str" (formula text result), "n" numbers, "e" errors as Excel stored them
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            ZipArchiveEntry entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;
            XmlDocument doc = Load(entry);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            foreach (XmlElement si in doc.SelectNodes("//m:si", ns))
                // Plain <t>, or rich text runs <r><t>; phonetic hints <rPh> are not part of the text.
                list.Add(string.Concat(si.SelectNodes("m:t | m:r/m:t", ns).Cast<XmlNode>().Select(n => n.InnerText)));
            return list;
        }

        private static string FirstSheetPath(ZipArchive zip)
        {
            try
            {
                XmlDocument wb = Load(zip.GetEntry("xl/workbook.xml"));
                var ns = new XmlNamespaceManager(wb.NameTable);
                ns.AddNamespace("m", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                ns.AddNamespace("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                var first = (XmlElement)wb.SelectSingleNode("//m:sheets/m:sheet", ns);
                string rid = first?.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                XmlDocument rels = Load(zip.GetEntry("xl/_rels/workbook.xml.rels"));
                foreach (XmlElement rel in rels.DocumentElement.ChildNodes.OfType<XmlElement>())
                    if (rel.GetAttribute("Id") == rid)
                    {
                        string target = rel.GetAttribute("Target").Replace('\\', '/');
                        return target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                    }
            }
            catch { }
            return "xl/worksheets/sheet1.xml";
        }

        private static XmlDocument Load(ZipArchiveEntry entry)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (Stream s = entry.Open())
            using (XmlReader reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);
            return doc;
        }

        private static int ColumnIndex(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return -1;
            int index = 0, i = 0;
            for (; i < reference.Length && char.IsLetter(reference[i]); i++)
                index = index * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
            return i == 0 ? -1 : index - 1;
        }

        public static string Column(int index)
        {
            var name = new StringBuilder();
            for (int n = index + 1; n > 0; n = (n - 1) / 26) name.Insert(0, (char)('A' + (n - 1) % 26));
            return name.ToString();
        }

        private static string SheetName(string name)
        {
            var clean = new StringBuilder();
            foreach (char ch in name ?? "Sheet1") clean.Append("[]:*?/\\".IndexOf(ch) >= 0 ? ' ' : ch);
            string result = clean.ToString().Trim();
            if (result.Length == 0) result = "Sheet1";
            return result.Length > 31 ? result.Substring(0, 31) : result;
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var clean = new StringBuilder(text.Length);
            foreach (char ch in text) if (ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20) clean.Append(ch);
            return clean.ToString();
        }

        private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static void Part(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(content);
        }
    }

    /// <summary>Reading a workset list from any file the user may have: .xlsx, .csv or .txt.</summary>
    public static class ListFile
    {
        public const string OpenFilter = "Workset lists (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Excel workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|Text (*.txt)|*.txt";
        public const string SaveFilter = "Excel workbook (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv";

        public static List<NameRow> Read(string path)
        {
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
                return NameList.FromCells(Xlsx.Read(path));
            return NameList.Parse(ReadText(path), NameList.FormatForFile(path));
        }

        /// <summary>Excel writes CSV in the PC's ANSI code page unless "CSV UTF-8" is chosen; invalid UTF-8 means ANSI.</summary>
        public static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { return Encoding.Default.GetString(bytes); }
        }

        /// <summary>Writes .xlsx, or CSV when the path ends in .csv.</summary>
        public static void Write(string path, string sheetName, IList<string> headers, IEnumerable<IList<string>> rows)
        {
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                Csv.Write(path, new[] { (IEnumerable<string>)headers }.Concat(rows.Select(r => (IEnumerable<string>)r)));
            else
                Xlsx.Write(path, sheetName, headers, rows);
        }
    }

    internal static class ExcelText
    {
        private static readonly Regex NumberOrDate = new Regex(@"^\s*[0-9][0-9\s.,/:-]*\s*$|^\s*[0-9.]+[eE][+-]?[0-9]+\s*$", RegexOptions.Compiled);

        /// <summary>True when Excel would not keep the text as typed: formulas (= + - @), numbers, dates.</summary>
        public static bool NeedsGuard(string value) =>
            !string.IsNullOrEmpty(value) && ("=+-@".IndexOf(value[0]) >= 0 || NumberOrDate.IsMatch(value));

        /// <summary>="text" - Excel shows exactly the text; the reader below turns it back.</summary>
        public static string Guard(string value) => "=\"" + value.Replace("\"", "\"\"") + "\"";

        public static string Unguard(string value)
        {
            if (value == null || value.Length < 3 || value[0] != '=' || value[1] != '"' || value[value.Length - 1] != '"') return value;
            string inner = value.Substring(2, value.Length - 3);
            // Only a plain string literal ("" for a quote inside); anything else really is a formula - keep it.
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] != '"') continue;
                if (i + 1 < inner.Length && inner[i + 1] == '"') { i++; continue; }
                return value;
            }
            return inner.Replace("\"\"", "\"");
        }
    }
}
