using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Addin.Services;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>Tabs that move model content between worksets: Grids / Levels, Revit / CAD, Families.</summary>
    internal partial class ManagerWindow
    {
        // ================================================================== Grids / Levels

        private List<DatumRow> _glRows;

        private void LoadDatums()
        {
            var ticked = new HashSet<long>(_glRows?.Where(r => r.IsChecked).Select(r => Compat.IdValue(r.Item.Id)) ?? Enumerable.Empty<long>());
            _glRows = Datums.Collect(_session.Doc, GlScope.IsChecked == true, GlRefPlanes.IsChecked == true).Select(i => new DatumRow(i)).ToList();
            foreach (DatumRow r in _glRows) r.IsChecked = ticked.Contains(Compat.IdValue(r.Item.Id));
            GlGrid.ItemsSource = _glRows;
            ApplyGlFilter();
            UpdateCounts();
        }

        private void GlOptions_Click(object sender, RoutedEventArgs e) => Safe("Grids / Levels", LoadDatums);
        private void GlSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyGlFilter();

        private void ApplyGlFilter()
        {
            if (_glRows == null) return;
            string text = GlSearch.Text.Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(GlGrid.ItemsSource);
            if (view != null) view.Filter = o => text.Length == 0 || Contains(((DatumRow)o).Name, text) || Contains(((DatumRow)o).Workset, text);
        }

        private void GlTickAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (DatumRow r in _glRows) r.IsChecked = r.Item.Kind == DatumKind.Grid || r.Item.Kind == DatumKind.Level;
        }

        private void GlTickShown_Click(object sender, RoutedEventArgs e) => TickShown(GlGrid, true);
        private void GlUntick_Click(object sender, RoutedEventArgs e) { foreach (DatumRow r in _glRows) r.IsChecked = false; }

        private void GlSelection_Click(object sender, RoutedEventArgs e) => GlTickIds(_session.CurrentSelection(), true);

        private void GlPick_Click(object sender, RoutedEventArgs e)
        {
            bool scope = GlScope.IsChecked == true, planes = GlRefPlanes.IsChecked == true;
            PickThen(() => _session.Pick(el =>
            {
                DatumKind? k = Datums.KindOf(el);
                return k == DatumKind.Grid || k == DatumKind.Level || (scope && k == DatumKind.ScopeBox) || (planes && k == DatumKind.ReferencePlane);
            }, "Pick grids and levels, then press Finish (Esc cancels)"), ids => GlTickIds(ids, true));
        }

        private void GlTickIds(List<ElementId> ids, bool tell)
        {
            var wanted = new HashSet<long>(ids.Select(Compat.IdValue));
            int found = 0;
            foreach (DatumRow r in _glRows)
                if (wanted.Contains(Compat.IdValue(r.Item.Id))) { r.IsChecked = r.CanTick; found++; }
            if (tell && found == 0) Tell("None of the selected elements is a grid or level in this list." +
                                         (ids.Count > 0 ? " (Scope boxes and reference planes appear when their option is ticked.)" : " Nothing is selected in Revit."));
        }

        private void GlDefault_Click(object sender, RoutedEventArgs e)
        {
            WorksetInfo shared = _session.Worksets.FirstOrDefault(w => WorksetNames.Comparer.Equals(w.Name, Worksets.SharedLevelsAndGrids));
            if (shared == null) { Tell("This model has no workset called \"" + Worksets.SharedLevelsAndGrids + "\". Choose the target in the list."); return; }
            GlTarget.SelectedItem = shared;
        }

        private void GlMove_Click(object sender, RoutedEventArgs e)
        {
            var target = Selected<WorksetInfo>(GlTarget);
            if (target == null) { Tell("Choose the target workset."); return; }
            var requests = _glRows.Where(r => r.IsChecked).Select(r => new KeyValuePair<ElementId, string>(r.Item.Id, target.Name)).ToList();
            MoveWithPreview("Move grids and levels", requests, false);
        }

        // ================================================================== Revit / CAD links

        private List<LinkRow> _lkRows;

        private void LoadLinks()
        {
            var previous = _lkRows?.ToDictionary(r => Compat.IdValue(r.Item.InstanceId));
            _lkRows = Links.Collect(_session.Doc).Select(i => new LinkRow(i, Links.WorksetNameFor(i, LkRvtTemplate.Text, LkCadTemplate.Text))).ToList();
            if (previous != null)
                foreach (LinkRow r in _lkRows)
                    if (previous.TryGetValue(Compat.IdValue(r.Item.InstanceId), out LinkRow old)) { r.IsChecked = old.IsChecked && r.CanTick; r.NewWorkset = old.NewWorkset; }
            LkGrid.ItemsSource = _lkRows;
            UpdateCounts();
            if (_lkRows.Count == 0) LkCount.Text = "This model has no Revit links or CAD files.";
        }

        private void LkRefresh_Click(object sender, RoutedEventArgs e) => Safe("Revit / CAD", LoadLinks);
        private void LkTickAll_Click(object sender, RoutedEventArgs e) { foreach (LinkRow r in _lkRows) r.IsChecked = r.CanTick; }
        private void LkUntick_Click(object sender, RoutedEventArgs e) { foreach (LinkRow r in _lkRows) r.IsChecked = false; }
        private void LkSelection_Click(object sender, RoutedEventArgs e) => LkTickIds(_session.CurrentSelection());

        private void LkPick_Click(object sender, RoutedEventArgs e)
        {
            PickThen(() => _session.Pick(el => el is RevitLinkInstance || el is ImportInstance,
                "Pick Revit links and CAD files, then press Finish (Esc cancels)"), LkTickIds);
        }

        private void LkTickIds(List<ElementId> ids)
        {
            var wanted = new HashSet<long>(ids.Select(Compat.IdValue));
            int found = 0;
            foreach (LinkRow r in _lkRows)
                if (wanted.Contains(Compat.IdValue(r.Item.InstanceId))) { r.IsChecked = r.CanTick; found++; }
            if (found == 0) Tell(ids.Count == 0 ? "Nothing is selected in Revit." : "None of the selected elements is a Revit link or CAD file.");
        }

        private void LkApplyNames_Click(object sender, RoutedEventArgs e)
        {
            var unknown = NameTemplate.UnknownTokens(LkRvtTemplate.Text, Links.Tokens).Concat(NameTemplate.UnknownTokens(LkCadTemplate.Text, Links.Tokens)).Distinct().ToList();
            if (unknown.Count > 0 && !Confirm("Not a known token: {" + string.Join("}, {", unknown) + "}. Known: {FileName}, {Kind}, {Type}." + Environment.NewLine + "Use the names anyway?")) return;
            var rows = _lkRows.Any(r => r.IsChecked) ? _lkRows.Where(r => r.IsChecked) : _lkRows;
            foreach (LinkRow r in rows) r.NewWorkset = Links.WorksetNameFor(r.Item, LkRvtTemplate.Text, LkCadTemplate.Text);
        }

        private void LkMove_Click(object sender, RoutedEventArgs e)
        {
            var target = Selected<WorksetInfo>(LkTarget);
            if (target == null) { Tell("Choose the target workset."); return; }
            LkGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var requests = Links.Requests(_lkRows.Where(r => r.IsChecked).Select(r => new KeyValuePair<LinkItem, string>(r.Item, target.Name)));
            MoveWithPreview("Move links", requests, false);
        }

        private void LkCreatePer_Click(object sender, RoutedEventArgs e)
        {
            LkGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var ticked = _lkRows.Where(r => r.IsChecked).ToList();
            var bad = ticked.Where(r => WorksetNames.Problem(WorksetNames.Tidy(r.NewWorkset)) != null).ToList();
            if (bad.Count > 0)
            {
                Tell("These rows have no valid workset name: " + string.Join(", ", bad.Take(5).Select(r => r.FileName)) +
                     Environment.NewLine + WorksetNames.Problem(WorksetNames.Tidy(bad[0].NewWorkset)));
                return;
            }
            var requests = Links.Requests(ticked.Select(r => new KeyValuePair<LinkItem, string>(r.Item, WorksetNames.Tidy(r.NewWorkset))));
            MoveWithPreview("Create link worksets and move", requests, true);
        }

        // ================================================================== Families (model elements)

        private const string AllCategories = "(all categories)";
        private const string AllWorksets = "(all worksets)";
        private List<ElementRow> _elRows;

        private void LoadElements()
        {
            List<ElementGroup> groups = ElementCatalog.Collect(_session.Doc);
            _elRows = groups.Select(g => new ElementRow(g)).ToList();
            ElGrid.ItemsSource = _elRows;

            string cat = ElCategory.SelectedItem as string, src = ElSource.SelectedItem as string;
            ElCategory.ItemsSource = new[] { AllCategories }.Concat(groups.Select(g => g.Category).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)).ToList();
            ElSource.ItemsSource = new[] { AllWorksets }.Concat(_session.Worksets.Select(w => w.Name)).ToList();
            ElCategory.SelectedItem = cat != null && ((List<string>)ElCategory.ItemsSource).Contains(cat) ? cat : AllCategories;
            ElSource.SelectedItem = src != null && ((List<string>)ElSource.ItemsSource).Contains(src) ? src : AllWorksets;
            ApplyElFilter();
            UpdateCounts();
        }

        private void ElRefresh_Click(object sender, RoutedEventArgs e) => Safe("Families", () => { using (Busy()) LoadElements(); });
        private void ElFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyElFilter();
        private void ElSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyElFilter();

        private void ApplyElFilter()
        {
            if (_elRows == null || ElGrid.ItemsSource == null) return;
            string cat = ElCategory.SelectedItem as string, src = ElSource.SelectedItem as string, text = ElSearch.Text.Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(ElGrid.ItemsSource);
            view.Filter = o =>
            {
                var r = (ElementRow)o;
                if (cat != null && cat != AllCategories && r.Category != cat) return false;
                if (src != null && src != AllWorksets && r.Workset != src) return false;
                return text.Length == 0 || Contains(r.Family, text) || Contains(r.Type, text) || Contains(r.Category, text) || Contains(r.Workset, text);
            };
        }

        private void ElTickShown_Click(object sender, RoutedEventArgs e) => TickShown(ElGrid, true);
        private void ElUntick_Click(object sender, RoutedEventArgs e) { foreach (ElementRow r in _elRows) r.IsChecked = false; }

        private void ElMove_Click(object sender, RoutedEventArgs e)
        {
            var target = Selected<WorksetInfo>(ElTarget);
            if (target == null) { Tell("Choose the target workset."); return; }
            var requests = _elRows.Where(r => r.IsChecked).SelectMany(r => r.Group.Ids)
                .Select(id => new KeyValuePair<ElementId, string>(id, target.Name)).ToList();
            MoveWithPreview("Move elements", requests, false);
        }

        private void ElPick_Click(object sender, RoutedEventArgs e)
        {
            var target = Selected<WorksetInfo>(ElTarget);
            if (target == null) { Tell("Choose the target workset first, then pick."); return; }
            string targetName = target.Name;
            PickThen(() => _session.Pick(el => el.Category != null && el.Category.CategoryType == CategoryType.Model && !(el is RevitLinkInstance),
                    "Pick the elements to move to \"" + targetName + "\", then press Finish (Esc cancels)"),
                ids => MoveWithPreview("Move picked elements", ids.Select(id => new KeyValuePair<ElementId, string>(id, targetName)).ToList(), false));
        }

        // ================================================================== shared helpers

        private static bool Contains(string value, string text) =>
            value != null && value.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;

        private static void TickShown(DataGrid grid, bool value)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (view == null) return;
            foreach (object o in view)
                if (o is Row r) r.IsChecked = value && r.CanTick;
        }
    }
}
