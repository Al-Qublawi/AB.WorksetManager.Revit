using System.Collections.Generic;
using System.Linq;

namespace AB.WorksetManager.Core.Tests
{
    internal static class NameTests
    {
        [Test]
        public static void ProhibitedCharactersAreReported()
        {
            Assert.True(WorksetNames.Problem("MEP - Pipes") == null, "plain name is fine");
            Assert.True(WorksetNames.Problem("Arch, Shell & Core (L1-L5)") == null, "commas, ampersand and brackets are fine");
            Assert.True(WorksetNames.Problem("A:B") != null, "colon refused");
            Assert.True(WorksetNames.Problem("A[1]") != null, "square brackets refused");
            Assert.True(WorksetNames.Problem("   ") != null, "blank refused");
            Assert.Equal("{}", WorksetNames.BadCharacters("a{b}c{"), "each bad character once");
        }

        [Test]
        public static void SanitizeMakesAnyTextAcceptable()
        {
            Assert.Equal("Z-Link-RVT-Arch-Model", WorksetNames.Sanitize("Z-Link-RVT-Arch:Model"), "colon -> dash");
            Assert.Equal("a b", WorksetNames.Sanitize("  a    b  "), "spaces collapse");
            Assert.True(WorksetNames.Problem(WorksetNames.Sanitize("x\\y{z}|;<>?`~")) == null, "result always valid");
        }
    }

    internal static class ListTests
    {
        [Test]
        public static void PlainTextIsOneNamePerLine()
        {
            var rows = NameList.Parse("MEP - Pipes, Drainage\r\n\r\n# comment\r\n  HVAC  \n// also comment\nElectrical", ListFormat.Text);
            Assert.Equal(3, rows.Count, "rows");
            Assert.Equal("MEP - Pipes, Drainage", rows[0].Name, "comma stays in a text name");
            Assert.Equal("HVAC", rows[1].Name, "trimmed");
            Assert.Equal(4, rows[1].Line, "line number kept");
        }

        [Test]
        public static void ExcelPasteUsesTabsAndHeaders()
        {
            var rows = NameList.Parse("Current Name\tNew Name\nWorkset1\tARC - Walls\nShared Levels and Grids\t", ListFormat.Text);
            Assert.Equal(2, rows.Count, "header skipped");
            Assert.Equal("Workset1", rows[0].Name, "name");
            Assert.Equal("ARC - Walls", rows[0].NewName, "new name");
            Assert.Equal("", rows[1].NewName, "empty new name");
        }

        [Test]
        public static void CsvWithQuotesAndSemicolons()
        {
            var rows = NameList.Parse("Name;Visible\n\"MEP; Pipes\";No\nHVAC;yes\n", ListFormat.Csv);
            Assert.Equal(2, rows.Count, "rows");
            Assert.Equal("MEP; Pipes", rows[0].Name, "quoted delimiter");
            Assert.Equal(false, rows[0].Visible.Value, "visible no");
            Assert.Equal(true, rows[1].Visible.Value, "visible yes");
        }

        [Test]
        public static void SecondColumnOfYesNoIsVisibility()
        {
            var rows = NameList.Parse("A,Yes\nB,No\nC", ListFormat.Csv);
            Assert.Equal(3, rows.Count, "rows");
            Assert.Equal("", rows[0].NewName, "not a new name");
            Assert.Equal(true, rows[0].Visible.Value, "visible");
            Assert.True(!rows[2].Visible.HasValue, "unspecified");
        }

        [Test]
        public static void ByteOrderMarkIsIgnored()
        {
            var rows = NameList.Parse("\uFEFFName,New Name\r\nA,B\r\n", ListFormat.Csv);
            Assert.Equal(1, rows.Count, "header recognised despite BOM");
            Assert.Equal("B", rows[0].NewName, "new name");
        }

        [Test]
        public static void ExportedCsvReadsBack()
        {
            var names = new[] { "Plain", "With, comma", "With \"quotes\"", "-Leading dash", "=Equals", "+ Link_DG-DSQ-311-513-AEC-DDE-FP-AB1-10", "01", "3-4", "@at" };
            string text = "Name,New Name\r\n" + string.Join("\r\n", names.Select(n => Csv.Line(new[] { n, n + " X" })));
            var rows = NameList.Parse(text, ListFormat.Csv);
            for (int i = 0; i < names.Length; i++)
            {
                Assert.Equal(names[i], rows[i].Name, "name " + i);
                Assert.Equal(names[i] + " X", rows[i].NewName, "new name " + i);
            }
        }
    }

    internal static class ExcelTests
    {
        private static readonly string[] Tricky = { "+ Link_DG-DSQ-311-513-AEC-DDE-FP-AB1-10", "+ Link_Hotel 1 model", "=Equals", "-Dash", "@At", "01", "3-4", "1.5", "Plain, comma", "Quote \"q\"", " spaced " };

        [Test]
        public static void CsvGuardsWhatExcelWouldMisread()
        {
            Assert.Equal("\"=\"\"+ Link_A\"\"\"", Csv.Escape("+ Link_A"), "formula-like name written as =\"...\"");
            Assert.Equal("Plain", Csv.Escape("Plain"), "plain name untouched");
            Assert.Equal("=\"a\"&B1", ExcelText.Unguard("=\"a\"&B1"), "a real formula is not unwrapped");
            Assert.Equal("+ x", ExcelText.Unguard("=\"+ x\""), "guard removed on reading");
        }

        [Test]
        public static void XlsxRoundTrip()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abwm-test-" + System.Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                ListFile.Write(path, "Rename", new[] { "Current Name", "New Name" }, Tricky.Select(n => (IList<string>)new[] { n, n + " X" }).ToList());
                var rows = ListFile.Read(path);
                Assert.Equal(Tricky.Length, rows.Count, "rows");
                for (int i = 0; i < Tricky.Length; i++)
                {
                    Assert.Equal(Tricky[i].Trim(), rows[i].Name, "name " + i);
                    Assert.Equal((Tricky[i] + " X").Trim(), rows[i].NewName, "new name " + i);
                }
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        [Test]
        public static void CsvFileRoundTrip()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abwm-test-" + System.Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                ListFile.Write(path, "Rename", new[] { "Current Name", "New Name" }, Tricky.Select(n => (IList<string>)new[] { n, "" }).ToList());
                var rows = ListFile.Read(path);
                Assert.Equal(Tricky.Length, rows.Count, "rows");
                for (int i = 0; i < Tricky.Length; i++) Assert.Equal(Tricky[i].Trim(), rows[i].Name, "name " + i);
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }
    }

    internal static class CreatePlanTests
    {
        [Test]
        public static void StatusesAgainstTheModel()
        {
            var rows = NameList.Parse("MEP\nmep\nhvac\nHVAC\nBad:Name\nNew One", ListFormat.Text);
            var checks = CreatePlan.Check(rows, new[] { "MEP", "Workset1" });
            Assert.Equal(CreateStatus.Exists, checks[0].Status, "exact");
            Assert.Equal(CreateStatus.Duplicate, checks[1].Status, "repeated in list (case ignored)");
            Assert.Equal(CreateStatus.Ready, checks[2].Status, "new");
            Assert.Equal(CreateStatus.Duplicate, checks[3].Status, "dup");
            Assert.Equal(CreateStatus.Invalid, checks[4].Status, "invalid");
            Assert.Equal(CreateStatus.Ready, checks[5].Status, "new");
            Assert.Equal("Workset1", string.Join("|", CreatePlan.NotInList(rows, new[] { "MEP", "Workset1" })), "not in list");
        }

        [Test]
        public static void OtherCaseIsFlagged()
        {
            var checks = CreatePlan.Check(NameList.Parse("mep", ListFormat.Text), new[] { "MEP" });
            Assert.Equal(CreateStatus.ExistsOtherCase, checks[0].Status, "case");
            Assert.Equal("MEP", checks[0].ExistingName, "model spelling");
        }
    }

    internal static class RenamePlanTests
    {
        private static List<RenameCheck> Check(string text, params string[] model) =>
            RenamePlan.Check(NameList.Parse(text, ListFormat.Text), model);

        [Test]
        public static void SimpleRenames()
        {
            var c = Check("A\tA1\nB\t\nC\tC\nZ\tZ1\nD\tBad|Name", "A", "B", "C", "D");
            Assert.Equal(RenameStatus.Ready, c[0].Status, "A");
            Assert.Equal(RenameStatus.Unchanged, c[1].Status, "blank");
            Assert.Equal(RenameStatus.Unchanged, c[2].Status, "same");
            Assert.Equal(RenameStatus.NotFound, c[3].Status, "missing");
            Assert.Equal(RenameStatus.Invalid, c[4].Status, "invalid");
        }

        [Test]
        public static void TakenAndDuplicateTargets()
        {
            var c = Check("A\tB\nC\tX\nD\tX\nA\tQ", "A", "B", "C", "D");
            Assert.Equal(RenameStatus.NameTaken, c[0].Status, "B keeps its name");
            Assert.Equal(RenameStatus.DuplicateTarget, c[1].Status, "X twice");
            Assert.Equal(RenameStatus.DuplicateTarget, c[2].Status, "X twice");
            Assert.Equal(RenameStatus.DuplicateSource, c[3].Status, "A twice");
        }

        [Test]
        public static void SwapGoesThroughTemporaryNames()
        {
            var c = Check("A\tB\nB\tA", "A", "B");
            Assert.True(c.All(x => x.CanRename), "swap allowed");
            var steps = RenamePlan.Steps(c, n => false);
            Assert.Equal(4, steps.Count, "two temp + two final");
            Assert.True(steps[0].IsTemporary && steps[1].IsTemporary, "temporaries first");
            Assert.Equal("B", steps[2].To, "A ends as B");
            Assert.Equal("A", steps[3].To, "B ends as A");
            Assert.True(steps.All(st => WorksetNames.Problem(st.To) == null), "temporary names are names Revit accepts");
        }

        [Test]
        public static void ChainBreaksWhenALinkFails()
        {
            // A->B needs B to move away, but B's new name is invalid, so A cannot be renamed either.
            var c = Check("A\tB\nB\tBad:Name", "A", "B");
            Assert.Equal(RenameStatus.NameTaken, c[0].Status, "A blocked");
            Assert.Equal(RenameStatus.Invalid, c[1].Status, "B invalid");
        }

        [Test]
        public static void CapitalsOnlyRename()
        {
            var c = Check("mep\tMEP - Pipes\nhvac\tHVAC", "mep", "hvac");
            Assert.True(c.All(x => x.CanRename), "allowed");
            var steps = RenamePlan.Steps(c, n => false);
            Assert.Equal("MEP - Pipes", steps[0].To, "direct");
            Assert.True(steps[1].IsTemporary, "capitals-only goes through a temporary name");
            Assert.Equal("HVAC", steps.Last().To, "final");
        }
    }

    internal static class TemplateTests
    {
        [Test]
        public static void ExpandsTokens()
        {
            var values = new Dictionary<string, string> { { "FileName", "Arch:Model" }, { "Kind", "RVT" } };
            Assert.Equal("Z-Link-RVT-Arch-Model", NameTemplate.Expand("Z-Link-{kind}-{FILENAME}", values), "tokens, case-insensitive, sanitised");
            Assert.Equal("Unknown", string.Join("|", NameTemplate.UnknownTokens("A {FileName} {Unknown}", new[] { "filename" })), "unknown token reported");
        }

        [Test]
        public static void FileStems()
        {
            Assert.Equal("Arch-Model", NameTemplate.FileStem("Arch-Model.rvt"), "rvt");
            Assert.Equal("Site plan", NameTemplate.FileStem(@"C:\x\Site plan.dwg"), "path + dwg");
            Assert.Equal("Model v1.2 final", NameTemplate.FileStem("Model v1.2 final"), "no extension");
        }

        [Test]
        public static void UniqueNames()
        {
            var used = new HashSet<string>(WorksetNames.Comparer) { "A", "A (2)" };
            Assert.Equal("A (3)", NameTemplate.Unique("A", used.Contains), "next free");
            Assert.Equal("B", NameTemplate.Unique("B", used.Contains), "free");
        }
    }
}
