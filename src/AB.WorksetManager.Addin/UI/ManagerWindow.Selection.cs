using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>
    /// Ticking many rows at once, the same way in every list:
    ///   - Shift+click a tick box: every row between the last box clicked and this one gets the same tick;
    ///   - select rows (click, Shift+click, Ctrl+click, Ctrl+A), then tick one of their boxes: all of them follow;
    ///   - select rows and press Space: they are ticked (or unticked, when all were already ticked).
    /// Rows that cannot be ticked (locked, already in the model...) are left as they are.
    /// </summary>
    internal partial class ManagerWindow
    {
        private readonly Dictionary<DataGrid, Row> _lastTicked = new Dictionary<DataGrid, Row>();
        private bool _applyingTicks;

        private void HookMultiTick()
        {
            foreach (DataGrid grid in new[] { CrGrid, GlGrid, LkGrid, V3Grid, ElGrid, ViViews, ViWorksets, DlGrid })
            {
                grid.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnTickBoxClick));
                grid.PreviewKeyDown += OnGridKeyDown;
            }
        }

        private void OnTickBoxClick(object sender, RoutedEventArgs e)
        {
            if (_applyingTicks) return;
            var grid = sender as DataGrid;
            var box = e.OriginalSource as CheckBox;
            if (grid == null || box == null || !"Tick".Equals(box.Tag) || !(box.DataContext is Row row)) return;

            try
            {
                _applyingTicks = true;
                bool value = row.IsChecked;
                List<Row> shown = grid.Items.OfType<Row>().ToList();   // in the order shown, filter applied

                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 &&
                    _lastTicked.TryGetValue(grid, out Row anchor) && anchor != row)
                {
                    int from = shown.IndexOf(anchor), to = shown.IndexOf(row);
                    if (from >= 0 && to >= 0)
                        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                            if (shown[i].CanTick) shown[i].IsChecked = value;
                }
                else if (grid.SelectedItems.Count > 1 && grid.SelectedItems.Contains(row))
                {
                    foreach (Row selected in grid.SelectedItems.OfType<Row>())
                        if (selected.CanTick) selected.IsChecked = value;
                }
                _lastTicked[grid] = row;
            }
            finally
            {
                _applyingTicks = false;
                UpdateCounts();
            }
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            var grid = sender as DataGrid;
            if (grid == null || e.Key != Key.Space || e.OriginalSource is TextBox) return;
            List<Row> selected = grid.SelectedItems.OfType<Row>().Where(r => r.CanTick).ToList();
            if (selected.Count == 0) return;
            bool value = !selected.All(r => r.IsChecked);
            foreach (Row r in selected) r.IsChecked = value;
            _lastTicked[grid] = selected.Last();
            UpdateCounts();
            e.Handled = true;
        }
    }
}
