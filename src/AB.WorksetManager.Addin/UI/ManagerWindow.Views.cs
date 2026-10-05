using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AB.WorksetManager.Addin.Services;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>Tabs about views: 3D Views and Visibility.</summary>
    internal partial class ManagerWindow
    {
        // ================================================================== 3D views

        private List<WorksetRow> _v3Rows;

        private void Load3D()
        {
            var ticked = new HashSet<int>(_v3Rows?.Where(r => r.IsChecked).Select(r => r.Info.Key) ?? Enumerable.Empty<int>());
            _v3Rows = _session.Worksets.Select(w => new WorksetRow(w)).ToList();
            foreach (WorksetRow r in _v3Rows) r.IsChecked = ticked.Contains(r.Info.Key);
            V3Grid.ItemsSource = _v3Rows;
            UpdateCounts();
        }

        private void V3TickAll_Click(object sender, RoutedEventArgs e) { foreach (WorksetRow r in _v3Rows) r.IsChecked = true; }
        private void V3Untick_Click(object sender, RoutedEventArgs e) { foreach (WorksetRow r in _v3Rows) r.IsChecked = false; }

        private void V3Combined_Click(object sender, RoutedEventArgs e)
        {
            var ticked = _v3Rows.Where(r => r.IsChecked).ToList();
            if (ticked.Count == 0) { Tell("Tick the worksets the view should show."); return; }
            string name = WorksetNames.Sanitize(V3CombinedName.Text);
            if (name.Length == 0) { Tell("Type a name for the combined view."); return; }
            if (!Confirm("Create (or update) the 3D view \"" + name + "\" showing " + ticked.Count + " workset(s) and hiding the others?")) return;
            var request = new List<KeyValuePair<string, List<WorksetId>>> { new KeyValuePair<string, List<WorksetId>>(name, ticked.Select(r => r.Info.Id).ToList()) };
            Run("Combined 3D view", report => ViewService.Create3D(_session.Doc, request, report));
        }

        private void V3Single_Click(object sender, RoutedEventArgs e)
        {
            var ticked = _v3Rows.Where(r => r.IsChecked).ToList();
            if (ticked.Count == 0) { Tell("Tick the worksets that get a view each."); return; }
            var unknown = NameTemplate.UnknownTokens(V3SingleName.Text, ViewService.Tokens);
            if (unknown.Count > 0 && !Confirm("Not a known token: {" + string.Join("}, {", unknown) + "}. Known: {Workset}." + Environment.NewLine + "Use the name anyway?")) return;
            if (V3SingleName.Text.IndexOf("{Workset}", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Tell("The view name needs {Workset} in it, so that each workset gets its own view.");
                return;
            }
            var requests = ticked.Select(r => new KeyValuePair<string, List<WorksetId>>(
                NameTemplate.Expand(V3SingleName.Text, new Dictionary<string, string> { { "Workset", r.Name } }),
                new List<WorksetId> { r.Info.Id })).ToList();
            if (!Confirm("Create (or update) " + requests.Count + " 3D view(s), each showing one workset?" + Environment.NewLine + Environment.NewLine +
                         string.Join(Environment.NewLine, requests.Take(12).Select(r => "  " + r.Key)) + (requests.Count > 12 ? Environment.NewLine + "  ..." : ""))) return;
            Run("3D view per workset", report => ViewService.Create3D(_session.Doc, requests, report));
        }

        // ================================================================== Visibility

        private const string KindAll = "Views and templates";
        private const string KindViews = "Views only";
        private const string KindTemplates = "Templates only";

        private List<ViewRow> _viViewRows;
        private List<WorksetRow> _viWsRows;

        private void InitViewFilters()
        {
            ViKind.ItemsSource = new[] { KindAll, KindViews, KindTemplates };
            ViKind.SelectedIndex = 0;
        }

        private void LoadVisibility()
        {
            var tickedViews = new HashSet<long>(_viViewRows?.Where(r => r.IsChecked).Select(r => Infrastructure.Compat.IdValue(r.Item.Id)) ?? Enumerable.Empty<long>());
            _viViewRows = ViewService.Collect(_session.Doc).Select(i => new ViewRow(i)).ToList();
            foreach (ViewRow r in _viViewRows) r.IsChecked = tickedViews.Contains(Infrastructure.Compat.IdValue(r.Item.Id));
            ViViews.ItemsSource = _viViewRows;

            var tickedWs = new HashSet<int>(_viWsRows?.Where(r => r.IsChecked).Select(r => r.Info.Key) ?? Enumerable.Empty<int>());
            _viWsRows = _session.Worksets.Select(w => new WorksetRow(w)).ToList();
            foreach (WorksetRow r in _viWsRows) r.IsChecked = tickedWs.Contains(r.Info.Key);
            ViWorksets.ItemsSource = _viWsRows;
            ApplyViFilter();
            UpdateCounts();
        }

        private void ViFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyViFilter();
        private void ViSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyViFilter();

        private void ApplyViFilter()
        {
            if (_viViewRows == null || ViViews.ItemsSource == null) return;
            string kind = ViKind.SelectedItem as string ?? KindAll, text = ViSearch.Text.Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(ViViews.ItemsSource);
            view.Filter = o =>
            {
                var r = (ViewRow)o;
                if (kind == KindViews && r.Item.IsTemplate) return false;
                if (kind == KindTemplates && !r.Item.IsTemplate) return false;
                return text.Length == 0 || Contains(r.Item.Name, text) || Contains(r.ViewType, text);
            };
        }

        private void ViTickViews_Click(object sender, RoutedEventArgs e) => TickShown(ViViews, true);
        private void ViUntickViews_Click(object sender, RoutedEventArgs e) { foreach (ViewRow r in _viViewRows) r.IsChecked = false; }
        private void ViTickWs_Click(object sender, RoutedEventArgs e) { foreach (WorksetRow r in _viWsRows) r.IsChecked = true; }
        private void ViUntickWs_Click(object sender, RoutedEventArgs e) { foreach (WorksetRow r in _viWsRows) r.IsChecked = false; }

        private void ViApply_Click(object sender, RoutedEventArgs e)
        {
            var views = _viViewRows.Where(r => r.IsChecked).ToList();
            var worksets = _viWsRows.Where(r => r.IsChecked).ToList();
            if (views.Count == 0 || worksets.Count == 0) { Tell("Tick at least one view (or template) and one workset."); return; }
            WorksetVisibility state = ViHide.IsChecked == true ? WorksetVisibility.Hidden : ViGlobal.IsChecked == true ? WorksetVisibility.UseGlobalSetting : WorksetVisibility.Visible;
            var handling = ViChangeTemplate.IsChecked == true ? ViewService.TemplateHandling.ChangeTemplate : ViewService.TemplateHandling.SkipView;
            int governed = views.Count(v => v.Item.ControllingTemplate != null);
            string stateText = state == WorksetVisibility.Visible ? "Show" : state == WorksetVisibility.Hidden ? "Hide" : "Use global setting for";
            string question = stateText + " " + worksets.Count + " workset(s) in " + views.Count + " view(s)/template(s)?";
            if (governed > 0)
                question += Environment.NewLine + Environment.NewLine + governed + " of the views take their worksets from a view template - " +
                            (handling == ViewService.TemplateHandling.SkipView ? "they will be skipped." : "their templates will be changed, which affects every view using them.");
            if (!Confirm(question)) return;
            Run("Workset visibility", report => ViewService.SetVisibility(_session.Doc, views.Select(v => v.Item.Id).ToList(),
                worksets.Select(w => w.Info.Id).ToList(), state, handling, report));
        }

        private void ViGlobal_Set(bool on)
        {
            var worksets = _viWsRows.Where(r => r.IsChecked).ToList();
            if (worksets.Count == 0) { Tell("Tick the worksets first."); return; }
            if (!Confirm("Set 'Visible in all views' " + (on ? "ON" : "OFF") + " for " + worksets.Count + " workset(s)?" + Environment.NewLine +
                         "This is the default for every view that does not set the workset itself.")) return;
            Run("Visible in all views", report => WorksetService.SetVisibleByDefault(_session.Doc, worksets.Select(w => w.Info.Id).ToList(), on, report));
        }

        private void ViGlobalOn_Click(object sender, RoutedEventArgs e) => ViGlobal_Set(true);
        private void ViGlobalOff_Click(object sender, RoutedEventArgs e) => ViGlobal_Set(false);

        private void ViStatus_Click(object sender, RoutedEventArgs e)
        {
            var views = _viViewRows.Where(r => r.IsChecked).ToList();
            if (views.Count == 0) views = CollectionViewSource.GetDefaultView(ViViews.ItemsSource).Cast<ViewRow>().ToList();
            var worksets = _viWsRows.Where(r => r.IsChecked).ToList();
            if (worksets.Count == 0) worksets = _viWsRows.ToList();
            if (views.Count == 0) { Tell("No views to check."); return; }
            if (views.Count > 400 && !Confirm(views.Count + " views - the table may take a moment. Continue? (Tick fewer views to narrow it.)")) return;

            Safe("Check status", () =>
            {
                DataTable table;
                using (Busy()) table = BuildStatusTable(views, worksets);
                new StatusWindow(table, this) { Owner = this }.ShowDialog();
            });
        }

        private DataTable BuildStatusTable(List<ViewRow> views, List<WorksetRow> worksets)
        {
            // Columns get plain names (C0, C1...) and carry the display text as Caption: workset names may hold
            // characters that a WPF binding path would misread.
            var table = new DataTable("Workset visibility");
            table.Columns.Add(new DataColumn("C0") { Caption = "View / template" });
            table.Columns.Add(new DataColumn("C1") { Caption = "Type" });
            table.Columns.Add(new DataColumn("C2") { Caption = "Worksets set by" });
            for (int i = 0; i < worksets.Count; i++)
                table.Columns.Add(new DataColumn("C" + (3 + i)) { Caption = worksets[i].Name });

            WorksetDefaultVisibilitySettings defaults = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(_session.Doc);
            foreach (ViewRow vr in views)
            {
                var view = _session.Doc.GetElement(vr.Item.Id) as View;
                if (view == null) continue;
                DataRow row = table.NewRow();
                row[0] = vr.Item.Name;
                row[1] = vr.Item.IsTemplate ? "Template (" + vr.ViewType + ")" : vr.ViewType;
                row[2] = vr.Item.ControllingTemplate != null ? "template: " + vr.Item.ControllingTemplate : "the view";
                for (int i = 0; i < worksets.Count; i++)
                    row[3 + i] = ViewService.CellText(_session.Doc, view, worksets[i].Info.Id, defaults);
                table.Rows.Add(row);
            }
            return table;
        }

        internal void ExportStatus(DataTable table)
        {
            var rows = new List<IEnumerable<string>> { table.Columns.Cast<DataColumn>().Select(c => c.Caption).ToList() };
            rows.AddRange(table.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => Convert.ToString(v)).ToList()));
            ExportCsv("Export visibility status", _session.Doc.Title + " - workset visibility.csv", rows);
        }
    }
}
