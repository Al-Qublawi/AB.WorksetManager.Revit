using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Addin.Services;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>
    /// The Worksets Manager window. Each tab's code lives in its own partial file
    /// (ManagerWindow.Worksets.cs, .Model.cs, .Views.cs). Tabs load their data the first time they are
    /// shown, and again after a change to the model.
    /// </summary>
    internal partial class ManagerWindow : Window
    {
        private readonly ManagerSession _session;
        private readonly HashSet<int> _loadedTabs = new HashSet<int>();
        private bool _ready;

        public ManagerWindow(ManagerSession session)
        {
            _session = session;
            InitializeComponent();

            Title = App.ProductName + " " + App.Version;
            ModelText.Text = session.Doc.Title + (DocumentGuard.UsesCentral(session.Doc) ? "  (local copy)" : session.Doc.IsDetached ? "  (detached)" : "  (central)");
            UserText.Text = "Signed in as " + Worksets.CurrentUser(session.Doc);
            CopyrightRun.Text = ABAdvTools.AdvToolsBrand.Copyright;
            LinkedInRun.Text = "LinkedIn: " + ABAdvTools.AdvToolsBrand.LinkedInUrl.Replace("https://www.", "").TrimEnd('/');

            Width = Math.Max(MinWidth, UserSettings.GetInt(UserSettings.WindowWidth, (int)Width));
            Height = Math.Max(MinHeight, UserSettings.GetInt(UserSettings.WindowHeight, (int)Height));
            LkRvtTemplate.Text = UserSettings.Get(UserSettings.RevitLinkTemplate);
            LkCadTemplate.Text = UserSettings.Get(UserSettings.CadLinkTemplate);
            V3SingleName.Text = UserSettings.Get(UserSettings.SingleViewTemplate);
            V3CombinedName.Text = UserSettings.Get(UserSettings.CombinedViewName);

            session.Reported += OnReported;
            session.WorksetsChanged += OnWorksetsChanged;
            Row.TickChanged += OnTickChanged;

            InitViewFilters();
            HookMultiTick();
            // Grids with no rows yet still lay out their columns.
            foreach (DataGrid grid in new[] { CrGrid, RnGrid, GlGrid, LkGrid, V3Grid, ElGrid, ViViews, ViWorksets, DlGrid, OvGrid })
                grid.ItemsSource = new List<object>();
            FillWorksetCombos();
            _ready = true;

            int tab = UserSettings.GetInt(UserSettings.LastTab, 0);
            Tabs.SelectedIndex = tab >= 0 && tab < Tabs.Items.Count ? tab : 0;
            LoadTab(Tabs.SelectedIndex);
        }

        // ------------------------------------------------------------------ lifecycle

        public void SaveSettings()
        {
            try
            {
                session_Unhook();
                UserSettings.Set(UserSettings.LastTab, Tabs.SelectedIndex.ToString());
                UserSettings.Set(UserSettings.WindowWidth, ((int)ActualWidth).ToString());
                UserSettings.Set(UserSettings.WindowHeight, ((int)ActualHeight).ToString());
                UserSettings.Set(UserSettings.RevitLinkTemplate, LkRvtTemplate.Text);
                UserSettings.Set(UserSettings.CadLinkTemplate, LkCadTemplate.Text);
                UserSettings.Set(UserSettings.SingleViewTemplate, V3SingleName.Text);
                UserSettings.Set(UserSettings.CombinedViewName, V3CombinedName.Text);
                UserSettings.Save();
            }
            catch (Exception ex) { Log.Warn("Saving window settings failed: " + ex.Message); }
        }

        private void session_Unhook()
        {
            _session.Reported -= OnReported;
            _session.WorksetsChanged -= OnWorksetsChanged;
            Row.TickChanged -= OnTickChanged;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            // Closing for good (not hiding for a pick) - remember the size while it is still known.
            if (!_session.HasPendingPick)
            {
                UserSettings.Set(UserSettings.WindowWidth, ((int)ActualWidth).ToString());
                UserSettings.Set(UserSettings.WindowHeight, ((int)ActualHeight).ToString());
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LinkedIn_Click(object sender, RoutedEventArgs e)
        {
            if (!ABAdvTools.AdvToolsBrand.OpenUrl(ABAdvTools.AdvToolsBrand.LinkedInUrl))
                Tell(ABAdvTools.AdvToolsBrand.LinkedInUrl);
        }

        // ------------------------------------------------------------------ tabs

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || !ReferenceEquals(e.OriginalSource, Tabs)) return;
            LoadTab(Tabs.SelectedIndex);
        }

        private void LoadTab(int index)
        {
            if (_loadedTabs.Contains(index)) return;
            Safe("Loading the tab", () =>
            {
                using (Busy())
                {
                    switch (index)
                    {
                        case 0: LoadOverview(); break;
                        case 1: CrGrid.ItemsSource = (System.Collections.IEnumerable)_crRows ?? new List<CreateRow>(); break;
                        case 2: LoadRename(); break;
                        case 3: LoadDatums(); break;
                        case 4: LoadLinks(); break;
                        case 5: Load3D(); break;
                        case 6: LoadElements(); break;
                        case 7: LoadVisibility(); break;
                        case 8: LoadDelete(); break;
                    }
                }
                _loadedTabs.Add(index);
            });
        }

#if DEBUG
        internal void LoadTabForTest(int index) => LoadTab(index);
#endif

        private void OnWorksetsChanged()
        {
            // The model changed: every tab reloads the next time it is shown; the open one reloads now.
            _loadedTabs.Clear();
            FillWorksetCombos();
            if (_ready) LoadTab(Tabs.SelectedIndex);
        }

        private void FillWorksetCombos()
        {
            foreach (ComboBox combo in new[] { GlTarget, LkTarget, ElTarget, DlTarget })
            {
                var previous = combo.SelectedItem as WorksetInfo;
                combo.ItemsSource = _session.Worksets.ToList();
                WorksetInfo again = previous == null ? null : _session.Worksets.FirstOrDefault(w => w.Key == previous.Key);
                combo.SelectedItem = again;
            }
            if (GlTarget.SelectedItem == null)
                GlTarget.SelectedItem = _session.Worksets.FirstOrDefault(w => WorksetNames.Comparer.Equals(w.Name, Worksets.SharedLevelsAndGrids));
        }

        private void OnTickChanged()
        {
            try { UpdateCounts(); }
            catch { }
        }

        private void UpdateCounts()
        {
            if (_glRows != null) GlCount.Text = _glRows.Count(r => r.IsChecked) + " ticked of " + _glRows.Count;
            if (_lkRows != null) LkCount.Text = _lkRows.Count(r => r.IsChecked) + " ticked of " + _lkRows.Count;
            if (_v3Rows != null) V3Count.Text = _v3Rows.Count(r => r.IsChecked) + " workset(s) ticked";
            if (_elRows != null) ElCount.Text = _elRows.Where(r => r.IsChecked).Sum(r => r.Count).ToString("N0") + " element(s) ticked";
            if (_viViewRows != null) ViViewCount.Text = _viViewRows.Count(r => r.IsChecked) + " view(s) / template(s) ticked";
            if (_viWsRows != null) ViWsCount.Text = _viWsRows.Count(r => r.IsChecked) + " workset(s) ticked";
            if (_dlRows != null) DlCount.Text = _dlRows.Count(r => r.IsChecked) + " ticked";
        }

        // ------------------------------------------------------------------ running actions

        /// <summary>
        /// Runs one action and shows what it did in the results panel. Nothing escapes to Revit: any
        /// exception becomes a line in the results (the model change itself is rolled back by ModelChange).
        /// </summary>
        private void Run(string title, Action<Report> action)
        {
            var report = new Report(title);
            try
            {
                using (Busy()) action(report);
            }
            catch (Exception ex)
            {
                report.Error("Stopped: " + ex.Message);
                Log.Error(title + " failed.", ex);
            }
            _session.Publish(report);
        }

        /// <summary>For UI-only work (loading lists, files): errors are shown, never thrown.</summary>
        private void Safe(string what, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log.Error(what + " failed.", ex);
                var report = new Report(what);
                report.Error(ex.Message);
                _session.Publish(report);
            }
        }

        private void OnReported(Report report)
        {
            var text = new StringBuilder(report.ToText());
            if (ResultsBox.Text.Length > 0) text.AppendLine();
            ResultsBox.Text = text + ResultsBox.Text;
            ResultsBox.ScrollToHome();
        }

        private void ResultsCopy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(ResultsBox.Text ?? string.Empty); }
            catch (Exception ex) { Log.Warn("Copy failed: " + ex.Message); }
        }

        private void ResultsClear_Click(object sender, RoutedEventArgs e) => ResultsBox.Clear();

        private static IDisposable Busy() => new BusyCursor();

        private sealed class BusyCursor : IDisposable
        {
            private readonly Cursor _previous = Mouse.OverrideCursor;
            public BusyCursor() { Mouse.OverrideCursor = Cursors.Wait; }
            public void Dispose() { Mouse.OverrideCursor = _previous; }
        }

        private bool Confirm(string question)
        {
            using (new NormalCursor())
                return MessageBox.Show(this, question, App.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void Tell(string text)
        {
            using (new NormalCursor())
                MessageBox.Show(this, text, App.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private sealed class NormalCursor : IDisposable
        {
            private readonly Cursor _previous = Mouse.OverrideCursor;
            public NormalCursor() { Mouse.OverrideCursor = null; }
            public void Dispose() { Mouse.OverrideCursor = _previous; }
        }

        /// <summary>Moves elements after showing what will happen. Returns true when something moved.</summary>
        private void MoveWithPreview(string title, List<KeyValuePair<ElementId, string>> requests, bool allowCreate)
        {
            if (requests.Count == 0) { Tell("Nothing is ticked."); return; }
            MovePlan plan;
            using (Busy()) plan = ElementMover.Plan(_session.Doc, requests, allowCreate);
            if (plan.Items.Count == 0 && plan.NewWorksets.Count == 0)
            {
                Run(title, report => ElementMover.Apply(_session.Doc, plan, report, title));
                Tell("Nothing to move." + Environment.NewLine + Environment.NewLine + plan.Summary());
                return;
            }
            if (!Confirm(plan.Summary() + Environment.NewLine + Environment.NewLine + "Go ahead?")) return;
            Run(title, report => ElementMover.Apply(_session.Doc, plan, report, title));
        }

        // ------------------------------------------------------------------ picking

        /// <summary>Hides the window so the user can pick in the model; <paramref name="after"/> runs once the window is back.</summary>
        private void PickThen(Func<List<ElementId>> pick, Action<List<ElementId>> after)
        {
            List<ElementId> picked = null;
            _session.RequestPick(() => picked = pick(), () =>
            {
                if (picked != null && picked.Count > 0) after(picked);
            });
            Hide();
        }

        /// <summary>Called by the command after a pick, just before the window is shown again.</summary>
        public void AfterPick()
        {
            foreach (string message in _session.TakeMessages())
            {
                var report = new Report("Pick in model");
                report.Error(message);
                _session.Publish(report);
            }
            // Run once the window is visible again (ShowDialog's loop is running): dialogs then have their owner.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Safe("After picking", () => _session.RunAfterPick())));
        }

        // ------------------------------------------------------------------ files

        private string AskOpen(string title)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = ListFile.OpenFilter + "|All files (*.*)|*.*",
                InitialDirectory = LastFolder()
            };
            if (dialog.ShowDialog(this) != true) return null;
            UserSettings.Set(UserSettings.LastFolder, Path.GetDirectoryName(dialog.FileName));
            return dialog.FileName;
        }

        private string AskSave(string title, string fileName)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                Filter = ListFile.SaveFilter,
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName = fileName,
                InitialDirectory = LastFolder()
            };
            if (dialog.ShowDialog(this) != true) return null;
            UserSettings.Set(UserSettings.LastFolder, Path.GetDirectoryName(dialog.FileName));
            return dialog.FileName;
        }

        private static string LastFolder()
        {
            string folder = UserSettings.Get(UserSettings.LastFolder);
            return Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private string SafeFileName(string text)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
            return text;
        }

        /// <summary>
        /// Saves a table for Excel. The first row is the header. .xlsx by default (every cell text, so Excel
        /// never turns a name like "+ Link_A" into a formula); CSV when the user picks it.
        /// </summary>
        private void Export(string title, string fileStem, IList<IList<string>> rows)
        {
            string path = AskSave(title, SafeFileName(fileStem) + ".xlsx");
            if (path == null) return;
            Safe("Export", () =>
            {
                using (Busy()) ListFile.Write(path, title.Replace("Export ", ""), rows[0], rows.Skip(1).ToList());
                var report = new Report(title);
                report.Done("Saved " + path);
                _session.Publish(report);
            });
        }

        private static T Selected<T>(ComboBox combo) where T : class => combo.SelectedItem as T;
    }
}
