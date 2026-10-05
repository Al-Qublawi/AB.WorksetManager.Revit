using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>Delete confirmation: lists what goes, and needs the word DELETE typed.</summary>
    internal sealed class ConfirmDeleteWindow : Window
    {
        private readonly TextBox _box = new TextBox { Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(3, 2, 3, 2) };
        private readonly Button _ok;

        public ConfirmDeleteWindow(IList<string> worksets, string target)
        {
            Title = App.ProductName + " - delete worksets";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Delete " + worksets.Count + " workset(s)?", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(new TextBlock { Text = "Their elements move to \"" + target + "\" first. Then the empty worksets are deleted.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var list = new ListBox { MaxHeight = 180, Margin = new Thickness(0, 0, 0, 10) };
            foreach (string w in worksets) list.Items.Add(w);
            panel.Children.Add(list);
            panel.Children.Add(new TextBlock { Text = "You can undo it with Ctrl+Z until you synchronize. Type DELETE to confirm:", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(_box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _ok = new Button { Content = "Delete", MinWidth = 90, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false,
                               Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)), Foreground = Brushes.White };
            var cancel = new Button { Content = "Cancel", MinWidth = 90, Padding = new Thickness(10, 3, 10, 3), IsCancel = true, IsDefault = true };
            _ok.Click += (s, e) => { DialogResult = true; };
            _box.TextChanged += (s, e) => _ok.IsEnabled = _box.Text.Trim() == "DELETE";
            buttons.Children.Add(_ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += (s, e) => _box.Focus();
        }
    }

    /// <summary>The view x workset visibility table, with export.</summary>
    internal sealed class StatusWindow : Window
    {
        public StatusWindow(DataTable table, ManagerWindow owner)
        {
            Title = App.ProductName + " - workset visibility";
            Width = 1000;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;

            var dock = new DockPanel { Margin = new Thickness(10) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var export = new Button { Content = "Export to Excel...", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            export.Click += (s, e) => owner.ExportStatus(table);
            var close = new Button { Content = "Close", Padding = new Thickness(10, 3, 10, 3), IsCancel = true };
            close.Click += (s, e) => Close();
            bar.Children.Add(export);
            bar.Children.Add(close);
            bar.Children.Add(new TextBlock
            {
                Text = "Visible / Hidden = set in the view or its template.  Global (...) = the view follows 'Visible in all views'.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x78)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            });
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);

            var grid = new DataGrid
            {
                ItemsSource = table.DefaultView,
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                FrozenColumnCount = 1,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };
            grid.AutoGeneratingColumn += (s, e) =>
            {
                DataColumn column = table.Columns[e.PropertyName];
                if (column != null) e.Column.Header = column.Caption;
            };
            dock.Children.Add(grid);
            Content = dock;
        }
    }
}
