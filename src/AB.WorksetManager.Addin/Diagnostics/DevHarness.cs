#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Addin.Services;
using AB.WorksetManager.Addin.UI;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AB.WorksetManager.Addin.Diagnostics
{
    /// <summary>
    /// Live checks run through the Revit MCP bridge (tools/revit-bridge) on a background LOCAL copy of a
    /// central model - never on the user's open model. Debug builds only.
    /// </summary>
    public static class DevHarness
    {
        private static StringBuilder _out;

        private static void Say(string text) => _out.AppendLine(text);

        private static void Check(bool ok, string what)
        {
            _out.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) _failures++;
        }

        private static int _failures;

        private static void Show(Report r)
        {
            foreach (ReportLine line in r.Lines) _out.AppendLine("      " + line);
        }

        private static Workset Ws(Document doc, string name) => Worksets.Find(doc, name);

        private static int CountOn(Document doc, string name)
        {
            Workset w = Ws(doc, name);
            return w == null ? -1 : Worksets.CountElements(doc, w.Id);
        }

        /// <summary>Runs every tab's action on the document and checks the outcome. Returns a text report.</summary>
        public static string RunAll(Document doc)
        {
            _out = new StringBuilder();
            _failures = 0;
            try
            {
                Say("Document " + doc.Title + " workshared " + doc.IsWorkshared + " central " + DocumentGuard.UsesCentral(doc));
                Check(DocumentGuard.ProblemForUse(doc) == null, "document usable");

                // ---------------------------------------------------------- Create
                Say("== Create");
                var rows = NameList.Parse("MEP - Pipes\nMEP - Ducts\tNo\nARC - Walls\nBad:Name\nmep - pipes", ListFormat.Text);
                var checks = CreatePlan.Check(rows, Worksets.User(doc).Select(w => w.Name));
                Check(checks.Count(c => c.CanCreate) == 2, "2 of 5 ready (exists, invalid, duplicate refused)");
                var r = new Report("create");
                WorksetService.Create(doc, checks.Where(c => c.CanCreate).Select(c => new KeyValuePair<string, bool>(c.Name, c.Row.Visible ?? true)).ToList(), r);
                Show(r);
                Check(Ws(doc, "MEP - Pipes") != null && Ws(doc, "MEP - Ducts") != null, "both created");
                Check(!WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc).IsWorksetVisible(Ws(doc, "MEP - Ducts").Id), "MEP - Ducts not visible by default");
                r = new Report("create again");
                WorksetService.Create(doc, new[] { new KeyValuePair<string, bool>("MEP - Pipes", true) }.ToList(), r);
                Check(!r.ModelChanged, "creating an existing name changes nothing");

                // ---------------------------------------------------------- Rename (plain, swap, capitals only)
                Say("== Rename");
                string doors = Ws(doc, "ARC - Doors X") != null ? "ARC - Doors X" : "ARC - Doors";
                int wallsBefore = CountOn(doc, "ARC - Walls"), doorsBefore = CountOn(doc, doors);
                r = new Report("rename");
                WorksetService.Rename(doc, NameList.Parse("MEP - Pipes\tMEP - Piping\nARC - Walls\t" + doors + "\n" + doors + "\tARC - Walls\nOld Furniture\tOLD FURNITURE\nWorkset1\tBad|Name", ListFormat.Text), r);
                Show(r);
                Check(Ws(doc, "MEP - Piping") != null && Ws(doc, "MEP - Pipes") == null, "plain rename");
                Check(CountOn(doc, doors) == wallsBefore && CountOn(doc, "ARC - Walls") == doorsBefore, "swap: names exchanged, elements stay");
                Check(Ws(doc, "OLD FURNITURE") != null && Ws(doc, "OLD FURNITURE").Name == "OLD FURNITURE", "capitals-only rename");
                Check(Worksets.User(doc).All(w => !w.Name.StartsWith("ABWM-TEMP")), "no temporary names left");
                // put the swap back so names match their content
                r = new Report("swap back");
                WorksetService.Rename(doc, NameList.Parse("ARC - Walls\t" + doors + "\n" + doors + "\tARC - Walls", ListFormat.Text), r);

                // ---------------------------------------------------------- Grids / Levels
                Say("== Grids / Levels");
                var datums = Datums.Collect(doc, true, false);
                Say("  datums " + datums.Count + " (" + string.Join(", ", datums.GroupBy(d => d.Kind).Select(g => g.Key + " " + g.Count())) + ")");
                var gl = datums.Where(d => d.Kind == DatumKind.Grid || d.Kind == DatumKind.Level).Select(d => new KeyValuePair<ElementId, string>(d.Id, "Workset1")).ToList();
                MovePlan plan = ElementMover.Plan(doc, gl, false);
                Say("  plan: " + plan.Summary().Replace(Environment.NewLine, " | "));
                r = new Report("datums to Workset1");
                ElementMover.Apply(doc, plan, r, "Move grids and levels");
                Show(r);
                var back = datums.Where(d => d.Kind == DatumKind.Grid || d.Kind == DatumKind.Level).Select(d => new KeyValuePair<ElementId, string>(d.Id, Worksets.SharedLevelsAndGrids)).ToList();
                r = new Report("datums back");
                ElementMover.Apply(doc, ElementMover.Plan(doc, back, false), r, "Move grids and levels");
                Check(Datums.Collect(doc, false, false).Where(d => d.Locked == null).All(d => d.Workset == Worksets.SharedLevelsAndGrids), "all grids and levels on Shared Levels and Grids");

                // ---------------------------------------------------------- Links
                Say("== Revit / CAD");
                var links = Links.Collect(doc);
                Say("  links " + links.Count + ": " + string.Join(" | ", links.Take(8).Select(l => l.KindText + " " + l.FileName + " -> " + Links.WorksetNameFor(l, "Z-Link-RVT-{FileName}", "Z-Link-CAD-{FileName}") + (l.Locked != null ? " [" + l.Locked + "]" : ""))));
                var linkReq = Links.Requests(links.Where(l => l.Locked == null).Select(l => new KeyValuePair<LinkItem, string>(l, Links.WorksetNameFor(l, "Z-Link-RVT-{FileName}", "Z-Link-CAD-{FileName}"))));
                plan = ElementMover.Plan(doc, linkReq, true);
                Say("  plan: " + plan.Summary().Replace(Environment.NewLine, " | "));
                r = new Report("links per workset");
                ElementMover.Apply(doc, plan, r, "Create link worksets and move");
                Show(r);
                var after = Links.Collect(doc);
                Check(after.Where(l => l.Locked == null && l.Kind == LinkKind.RevitLink).All(l => l.InstanceWorkset == l.TypeWorkset && l.InstanceWorkset.StartsWith("Z-Link-RVT-")), "every Revit link instance and type on its own Z-Link-RVT workset");
                r = new Report("links again");
                ElementMover.Apply(doc, ElementMover.Plan(doc, linkReq, true), r, "again");
                Check(!r.ModelChanged || r.DoneCount == 0, "running it again changes nothing");

                // ---------------------------------------------------------- Families (with a grouped element)
                Say("== Families");
                var catalog = ElementCatalog.Collect(doc);
                Say("  catalog rows " + catalog.Count + ", elements " + catalog.Sum(g => g.Count) + ", categories " + catalog.Select(g => g.Category).Distinct().Count());
                var furniture = catalog.Where(g => g.Category == "Furniture" && g.Workset == "OLD FURNITURE").SelectMany(g => g.Ids).ToList();
                plan = ElementMover.Plan(doc, furniture.Select(id => new KeyValuePair<ElementId, string>(id, "MEP - Ducts")), false);
                r = new Report("furniture");
                ElementMover.Apply(doc, plan, r, "Move elements");
                Show(r);
                Check(CountOn(doc, "OLD FURNITURE") == 0 || catalog.Where(g => g.Workset == "OLD FURNITURE").Sum(g => g.Count) > furniture.Count, "furniture moved off OLD FURNITURE");
                Group group = new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>().FirstOrDefault(g => g.GroupId == ElementId.InvalidElementId && g.GetMemberIds().Count > 1);
                if (group != null)
                {
                    ElementId member = group.GetMemberIds().First();
                    plan = ElementMover.Plan(doc, new[] { new KeyValuePair<ElementId, string>(member, "MEP - Piping") }, false);
                    Say("  grouped member plan: " + plan.Summary().Replace(Environment.NewLine, " | "));
                    Check(plan.Items.Count == 1 && Compat.IdValue(plan.Items[0].Id) == Compat.IdValue(group.Id), "grouped element -> its group moves");
                    r = new Report("group");
                    ElementMover.Apply(doc, plan, r, "Move elements");
                    Check(doc.GetElement(member).WorksetId == Ws(doc, "MEP - Piping").Id, "member followed its group");
                }

                // ---------------------------------------------------------- 3D views
                Say("== 3D views");
                var ids = new List<WorksetId> { Ws(doc, "ARC - Walls").Id, Ws(doc, doors).Id };
                r = new Report("3D");
                ViewService.Create3D(doc, new List<KeyValuePair<string, List<WorksetId>>>
                {
                    new KeyValuePair<string, List<WorksetId>>("3D - WS - Combined", ids),
                    new KeyValuePair<string, List<WorksetId>>("3D - WS - ARC - Walls", new List<WorksetId> { ids[0] })
                }, r);
                Show(r);
                var v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().First(v => v.Name == "3D - WS - Combined");
                Check(v3.GetWorksetVisibility(ids[0]) == WorksetVisibility.Visible && v3.GetWorksetVisibility(Ws(doc, "Workset1").Id) == WorksetVisibility.Hidden, "combined view shows ticked, hides others");
                int count3d = new FilteredElementCollector(doc).OfClass(typeof(View3D)).GetElementCount();
                r = new Report("3D again");
                ViewService.Create3D(doc, new List<KeyValuePair<string, List<WorksetId>>> { new KeyValuePair<string, List<WorksetId>>("3d - ws - combined", new List<WorksetId> { ids[1] }) }, r);
                Show(r);
                Check(new FilteredElementCollector(doc).OfClass(typeof(View3D)).GetElementCount() == count3d, "same name (other capitals) updates, no duplicate");
                Check(v3.GetWorksetVisibility(ids[0]) == WorksetVisibility.Hidden && v3.GetWorksetVisibility(ids[1]) == WorksetVisibility.Visible, "updated view follows the new ticks");

                // ---------------------------------------------------------- Visibility
                Say("== Visibility");
                var views = ViewService.Collect(doc);
                ViewItem governed = views.FirstOrDefault(v => !v.IsTemplate && v.ControllingTemplate != null);
                ViewItem free = views.FirstOrDefault(v => !v.IsTemplate && v.ControllingTemplate == null && v.ViewType == "Floor Plan");
                Say("  views " + views.Count(v => !v.IsTemplate) + ", templates " + views.Count(v => v.IsTemplate) + ", governed " + views.Count(v => v.ControllingTemplate != null));
                WorksetId pip = Ws(doc, "MEP - Piping").Id;
                r = new Report("hide skip");
                ViewService.SetVisibility(doc, new[] { governed.Id, free.Id }, new[] { pip }, WorksetVisibility.Hidden, ViewService.TemplateHandling.SkipView, r);
                Show(r);
                Check(((View)doc.GetElement(free.Id)).GetWorksetVisibility(pip) == WorksetVisibility.Hidden, "free view: hidden");
                Check(((View)doc.GetElement(governed.ControllingTemplateId)).GetWorksetVisibility(pip) != WorksetVisibility.Hidden, "governed view skipped, template untouched");
                r = new Report("hide template");
                ViewService.SetVisibility(doc, new[] { governed.Id }, new[] { pip }, WorksetVisibility.Hidden, ViewService.TemplateHandling.ChangeTemplate, r);
                Show(r);
                Check(((View)doc.GetElement(governed.ControllingTemplateId)).GetWorksetVisibility(pip) == WorksetVisibility.Hidden, "template changed instead");
                var defaults = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                Say("  status cells: free=" + ViewService.CellText(doc, (View)doc.GetElement(free.Id), pip, defaults) + " governed=" +
                    ViewService.CellText(doc, (View)doc.GetElement(governed.Id), pip, defaults) + " other=" + ViewService.CellText(doc, (View)doc.GetElement(free.Id), Ws(doc, "Workset1").Id, defaults));
                r = new Report("global");
                WorksetService.SetVisibleByDefault(doc, new[] { Ws(doc, "MEP - Ducts").Id }, true, r);
                Check(defaults.IsWorksetVisible(Ws(doc, "MEP - Ducts").Id), "global default switched on");

                // ---------------------------------------------------------- Delete
                Say("== Delete");
                int toDelete = CountOn(doc, "To Delete A"), receiving = CountOn(doc, "OLD FURNITURE");
                WorksetId target = Ws(doc, "OLD FURNITURE").Id;
                doc.GetWorksetTable().SetActiveWorksetId(Ws(doc, "To Delete A").Id);
                var dc = WorksetService.CheckDelete(doc, new[] { Ws(doc, "To Delete A").Id, target }, target);
                Check(dc.Count(c => c.Problem == null) == 1, "target itself refused, the other ready");
                r = new Report("delete");
                WorksetService.Delete(doc, new[] { Ws(doc, "To Delete A").Id }, target, r);
                Show(r);
                Check(Ws(doc, "To Delete A") == null, "deleted");
                Check(CountOn(doc, "OLD FURNITURE") == receiving + toDelete, "its " + toDelete + " elements moved to the target");
                Check(doc.GetWorksetTable().GetActiveWorksetId() == target, "active workset handed to the target first");

                Say("== Overview");
                foreach (WorksetInfo w in Worksets.Describe(doc, true))
                    Say("  " + (w.IsActive ? "*" : " ") + " " + w.Name + "  " + w.ElementCount + "  owner '" + w.Owner + "' visible " + w.VisibleByDefault);
            }
            catch (Exception ex)
            {
                _failures++;
                Say("EXCEPTION " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
            Say(_failures == 0 ? "ALL PASSED" : _failures + " FAILED");
            return _out.ToString();
        }

        /// <summary>Renders each tab of the window for a document to PNG files (never shown on screen).</summary>
        public static string RenderTabs(UIApplication uiapp, Document doc, string folder)
        {
            Directory.CreateDirectory(folder);
            var session = new ManagerSession(uiapp, new UIDocument(doc));
            var window = new ManagerWindow(session);
            var sb = new StringBuilder();
            try
            {
                var tabs = window.Tabs;
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                var host = new System.Windows.Controls.Border { Child = content, Width = 1080, Height = 740, Background = System.Windows.Media.Brushes.White };
                host.Resources = window.Resources;
                for (int i = 0; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    window.LoadTabForTest(i);
                    for (int pass = 0; pass < 3; pass++)
                    {
                        host.Measure(new Size(1080, 740));
                        host.Arrange(new Rect(0, 0, 1080, 740));
                        host.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
                    }
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1080, 740, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    string png = Path.Combine(folder, "tab" + i + ".png");
                    using (FileStream stream = File.Create(png)) encoder.Save(stream);
                    sb.AppendLine(png);
                }
            }
            finally { window.Close(); }
            return sb.ToString();
        }

        /// <summary>
        /// Proves the pick loop: a modal window hidden from inside ShowDialog returns from it, and the same
        /// window can be shown modally again. Shows a small window for about a second.
        /// </summary>
        public static string ModalHideTest()
        {
            var w = new Window { Width = 200, Height = 80, Title = "AB Worksets - modal test", WindowStartupLocation = WindowStartupLocation.CenterScreen };
            int round = 0;
            var log = new StringBuilder();
            w.IsVisibleChanged += (s, e) =>
            {
                if (!w.IsVisible) return;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                t.Tick += (s2, e2) => { t.Stop(); if (round == 0) w.Hide(); else w.Close(); };
                t.Start();
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            w.ShowDialog();
            log.AppendLine("first ShowDialog returned after hide: " + watch.ElapsedMilliseconds + " ms, visible " + w.IsVisible);
            round = 1;
            w.ShowDialog();
            log.AppendLine("second ShowDialog returned after close: " + watch.ElapsedMilliseconds + " ms");
            return log.ToString();
        }
    }
}
#endif
