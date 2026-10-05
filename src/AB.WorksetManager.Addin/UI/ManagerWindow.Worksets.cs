using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Addin.Services;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>Tabs that work on the worksets themselves: Overview, Create, Rename, Delete.</summary>
    internal partial class ManagerWindow
    {
        // ================================================================== Overview

        private void LoadOverview()
        {
            List<WorksetInfo> list = _session.Worksets;
            OvGrid.ItemsSource = list.Select(w => new WorksetRow(w)).ToList();
            WorksetInfo active = list.FirstOrDefault(w => w.IsActive);
            OvSummary.Text = list.Count + " user worksets, " + list.Where(w => w.ElementCount > 0).Sum(w => w.ElementCount).ToString("N0") +
                             " elements. Active: " + (active != null ? active.Name : "-") + ".";
        }

        private void OvRefresh_Click(object sender, RoutedEventArgs e)
        {
            Safe("Refresh", () =>
            {
                using (Busy()) _session.RefreshWorksets(true);
            });
        }

        private void OvExport_Click(object sender, RoutedEventArgs e)
        {
            var rows = new List<IEnumerable<string>> { new[] { "Name", "New Name", "Elements", "Open", "Editable", "Owner", "Visible in all views", "Active" } };
            rows.AddRange(_session.Worksets.Select(w => new[]
            {
                w.Name, "", w.ElementCount.ToString(), w.IsOpen ? "Yes" : "No", w.IsEditable ? "Yes" : "No", w.Owner,
                w.VisibleByDefault ? "Yes" : "No", w.IsActive ? "Yes" : ""
            }));
            ExportCsv("Export worksets", _session.Doc.Title + " - worksets.csv", rows);
        }

        private void OvMakeActive_Click(object sender, RoutedEventArgs e)
        {
            var row = OvGrid.SelectedItem as WorksetRow;
            if (row == null) { Tell("Select a workset in the list first."); return; }
            Run("Make active", report =>
            {
                if (WorksetService.SetActive(_session.Doc, row.Info.Id, report)) report.ModelChanged = true;
            });
        }

        // ================================================================== Create

        private List<CreateRow> _crRows;

        private void CrLoad_Click(object sender, RoutedEventArgs e)
        {
            string path = AskOpen("Load a list of worksets");
            if (path == null) return;
            Safe("Load list", () =>
            {
                List<NameRow> rows = NameList.Parse(ReadText(path), NameList.FormatForFile(path));
                // Show the list as plain lines (tab before the visibility), so it can be edited before checking.
                CrText.Text = string.Join(Environment.NewLine, rows.Select(r => r.Name + (r.Visible.HasValue ? "\t" + (r.Visible.Value ? "Yes" : "No") : "")));
                CrCheck();
            });
        }

        private void CrCheck_Click(object sender, RoutedEventArgs e) => Safe("Check", CrCheck);

        private void CrCheck()
        {
            List<NameRow> rows = NameList.Parse(CrText.Text, ListFormat.Text);
            List<string> existing = Worksets.User(_session.Doc).Select(w => w.Name).ToList();
            List<CreateCheck> checks = CreatePlan.Check(rows, existing);
            _crRows = checks.Select(c => new CreateRow(c)).ToList();
            CrGrid.ItemsSource = _crRows;

            int ready = checks.Count(c => c.CanCreate);
            int exist = checks.Count(c => c.Status == CreateStatus.Exists || c.Status == CreateStatus.ExistsOtherCase);
            int bad = checks.Count - ready - exist;
            List<string> notListed = CreatePlan.NotInList(rows, existing);
            CrSummary.Text = rows.Count + " name(s) in the list: " + ready + " not in the model yet, " + exist + " already in the model" +
                             (bad > 0 ? ", " + bad + " with a problem" : "") + "." +
                             (notListed.Count > 0 ? Environment.NewLine + "In the model but not in your list (" + notListed.Count + "): " + string.Join(", ", notListed.Take(25)) + (notListed.Count > 25 ? ", ..." : "") : "");
        }

        private void CrTickAll_Click(object sender, RoutedEventArgs e) { if (_crRows != null) foreach (CreateRow r in _crRows) r.IsChecked = r.CanTick; }
        private void CrUntick_Click(object sender, RoutedEventArgs e) { if (_crRows != null) foreach (CreateRow r in _crRows) r.IsChecked = false; }

        private void CrCreate_Click(object sender, RoutedEventArgs e)
        {
            if (_crRows == null) { Tell("Type or load the names, then press Check."); return; }
            var ticked = _crRows.Where(r => r.IsChecked).ToList();
            if (ticked.Count == 0) { Tell("Nothing is ticked."); return; }
            if (!Confirm("Create " + ticked.Count + " workset(s)?" + Environment.NewLine + Environment.NewLine +
                         string.Join(Environment.NewLine, ticked.Take(15).Select(r => "  " + r.Name + (r.Visible ? "" : "  (not visible in all views)"))) +
                         (ticked.Count > 15 ? Environment.NewLine + "  ..." : ""))) return;
            Run("Create worksets", report =>
                WorksetService.Create(_session.Doc, ticked.Select(r => new KeyValuePair<string, bool>(r.Name, r.Visible)).ToList(), report));
            CrCheck();
        }

        // ================================================================== Rename

        private List<RenameRow> _rnRows;

        private void LoadRename()
        {
            var previous = _rnRows?.ToDictionary(r => r.Name, r => r.NewName, WorksetNames.Comparer);
            _rnRows = _session.Worksets.Select(w => new RenameRow(w.Name)).ToList();
            // Keep typed new names for worksets that still exist (e.g. after another tab changed the model).
            if (previous != null)
                foreach (RenameRow r in _rnRows)
                    if (previous.TryGetValue(r.Name, out string n) && !WorksetNames.Comparer.Equals(n, r.Name)) r.NewName = n;
            RnGrid.ItemsSource = _rnRows;
            RnSummary.Text = string.Empty;
        }

        private void RnLoad_Click(object sender, RoutedEventArgs e)
        {
            string path = AskOpen("Load a rename list (current name, new name)");
            if (path == null) return;
            Safe("Load rename list", () =>
            {
                List<NameRow> rows = NameList.Parse(ReadText(path), NameList.FormatForFile(path));
                var byName = _rnRows.ToDictionary(r => r.Name, WorksetNames.Comparer);
                int matched = 0;
                var missing = new List<string>();
                foreach (NameRow row in rows)
                {
                    if (byName.TryGetValue(row.Name, out RenameRow target)) { target.NewName = row.NewName; matched++; }
                    else if (!WorksetNames.IsBlank(row.Name)) missing.Add(row.Name);
                }
                RnSummary.Text = matched + " workset(s) matched." + (missing.Count > 0 ? " Not in the model: " + string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? ", ..." : "") : "");
                RnCheck();
            });
        }

        private void RnExport_Click(object sender, RoutedEventArgs e)
        {
            var rows = new List<IEnumerable<string>> { new[] { "Current Name", "New Name" } };
            rows.AddRange(_rnRows.Select(r => new[] { r.Name, r.NewName }));
            ExportCsv("Export rename list", _session.Doc.Title + " - rename worksets.csv", rows);
        }

        private void RnClear_Click(object sender, RoutedEventArgs e) { foreach (RenameRow r in _rnRows) r.NewName = string.Empty; RnSummary.Text = string.Empty; }

        private void RnCheck_Click(object sender, RoutedEventArgs e) => Safe("Check", () => RnCheck());

        private List<RenameCheck> RnCheck()
        {
            RnGrid.CommitEdit(DataGridEditingUnit.Row, true);
            List<NameRow> rows = _rnRows.Select((r, i) => new NameRow(i + 1, r.Name, r.NewName, null)).ToList();
            List<RenameCheck> checks = RenamePlan.Check(rows, Worksets.User(_session.Doc).Select(w => w.Name));
            for (int i = 0; i < checks.Count; i++)
                _rnRows[i].Status = checks[i].Status == RenameStatus.Unchanged ? "" : checks[i].Message;
            int ready = checks.Count(c => c.CanRename);
            int problems = checks.Count(c => !c.CanRename && c.Status != RenameStatus.Unchanged);
            RnSummary.Text = ready + " ready to rename" + (problems > 0 ? ", " + problems + " with a problem" : "") + ".";
            return checks;
        }

        private void RnRename_Click(object sender, RoutedEventArgs e)
        {
            List<RenameCheck> checks = null;
            Safe("Check", () => checks = RnCheck());
            if (checks == null) return;
            var ready = checks.Where(c => c.CanRename).ToList();
            if (ready.Count == 0) { Tell("Nothing is ready to rename. Type new names, then press Rename."); return; }
            if (!Confirm("Rename " + ready.Count + " workset(s)?" + Environment.NewLine + Environment.NewLine +
                         string.Join(Environment.NewLine, ready.Take(15).Select(c => "  " + c.CurrentName + "  ->  " + c.NewName)) +
                         (ready.Count > 15 ? Environment.NewLine + "  ..." : ""))) return;
            List<NameRow> rows = _rnRows.Select((r, i) => new NameRow(i + 1, r.Name, r.NewName, null)).ToList();
            Run("Rename worksets", report => WorksetService.Rename(_session.Doc, rows, report));
        }

        // ================================================================== Delete

        private List<DeleteRow> _dlRows;

        internal sealed class DeleteRow : Row
        {
            public DeleteRow(WorksetInfo info) { Info = info; }
            public WorksetInfo Info { get; }
            public string Name => Info.Name;
            public string Elements => Info.ElementCount < 0 ? "" : Info.ElementCount.ToString("N0");
            public string Owner => Info.Owner;
        }

        private void LoadDelete()
        {
            var ticked = new HashSet<int>(_dlRows?.Where(r => r.IsChecked).Select(r => r.Info.Key) ?? Enumerable.Empty<int>());
            _dlRows = _session.Worksets.Select(w => new DeleteRow(w)).ToList();
            foreach (DeleteRow r in _dlRows) r.IsChecked = ticked.Contains(r.Info.Key);
            DlGrid.ItemsSource = _dlRows;
            UpdateCounts();
        }

        private void DlUntick_Click(object sender, RoutedEventArgs e) { foreach (DeleteRow r in _dlRows) r.IsChecked = false; }

        private void DlTarget_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_dlRows == null) return;
            foreach (DeleteRow r in _dlRows) r.Status = string.Empty;
        }

        private List<WorksetService.DeleteCheck> DlCheck()
        {
            var target = Selected<WorksetInfo>(DlTarget);
            if (target == null) { Tell("Choose the workset that receives the elements."); return null; }
            var ticked = _dlRows.Where(r => r.IsChecked).ToList();
            if (ticked.Count == 0) { Tell("Tick the worksets to delete."); return null; }
            List<WorksetService.DeleteCheck> checks;
            using (Busy()) checks = WorksetService.CheckDelete(_session.Doc, ticked.Select(r => r.Info.Id).ToList(), target.Id);
            foreach (DeleteRow r in _dlRows) r.Status = string.Empty;
            foreach (var c in checks)
            {
                DeleteRow row = _dlRows.FirstOrDefault(r => c.Workset != null && r.Info.Key == c.Workset.Id.IntegerValue);
                if (row != null) row.Status = c.Problem ?? ("Ready - " + c.ElementCount.ToString("N0") + " element(s) will move to \"" + target.Name + "\"");
            }
            return checks;
        }

        private void DlCheck_Click(object sender, RoutedEventArgs e) => Safe("Check", () => DlCheck());

        private void DlDelete_Click(object sender, RoutedEventArgs e)
        {
            List<WorksetService.DeleteCheck> checks = null;
            Safe("Check", () => checks = DlCheck());
            if (checks == null) return;
            var ready = checks.Where(c => c.Problem == null).ToList();
            if (ready.Count == 0) { Tell("None of the ticked worksets can be deleted now - see the Check column."); return; }
            var target = Selected<WorksetInfo>(DlTarget);

            var dialog = new ConfirmDeleteWindow(ready.Select(c => c.Workset.Name + "  (" + c.ElementCount.ToString("N0") + " elements)").ToList(), target.Name) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var ids = ready.Select(c => c.Workset.Id).ToList();
            Run("Delete worksets", report =>
            {
                WorksetService.Delete(_session.Doc, ids, target.Id, report);
                if (report.ModelChanged) report.Note("Synchronize with Central so the deletion reaches the central model.");
            });
        }
    }
}
