using System;
using System.Collections.Generic;
using System.Linq;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Services
{
    internal sealed class ViewItem
    {
        public ElementId Id { get; set; }
        public string Name { get; set; }
        public string ViewType { get; set; }
        public bool IsTemplate { get; set; }
        /// <summary>Name of the template that controls this view's workset visibility, or null.</summary>
        public string ControllingTemplate { get; set; }
        public ElementId ControllingTemplateId { get; set; }
    }

    /// <summary>Workset visibility in views and view templates, and the workset 3D views.</summary>
    internal static class ViewService
    {
        private static readonly ElementId WorksetsTemplateParameter = new ElementId(BuiltInParameter.VIS_GRAPHICS_WORKSETS);

        /// <summary>View types whose Visibility/Graphics has a Worksets tab.</summary>
        private static readonly HashSet<ViewType> Graphical = new HashSet<ViewType>
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan,
            ViewType.Elevation, ViewType.Section, ViewType.Detail, ViewType.ThreeD, ViewType.Walkthrough
        };

        public static List<ViewItem> Collect(Document doc)
        {
            var items = new List<ViewItem>();
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
            {
                try
                {
                    if (!Graphical.Contains(v.ViewType)) continue;
                    var item = new ViewItem { Id = v.Id, Name = v.Name, ViewType = TypeLabel(v.ViewType), IsTemplate = v.IsTemplate };
                    if (!v.IsTemplate)
                    {
                        View template = ControllingTemplate(doc, v);
                        if (template != null) { item.ControllingTemplate = template.Name; item.ControllingTemplateId = template.Id; }
                    }
                    items.Add(item);
                }
                catch (Exception ex) { Log.Warn("View " + Compat.IdValue(v.Id) + " skipped: " + ex.Message); }
            }
            return items.OrderBy(i => i.IsTemplate).ThenBy(i => i.ViewType).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string TypeLabel(ViewType t)
        {
            switch (t)
            {
                case ViewType.FloorPlan: return "Floor Plan";
                case ViewType.CeilingPlan: return "Ceiling Plan";
                case ViewType.EngineeringPlan: return "Structural Plan";
                case ViewType.AreaPlan: return "Area Plan";
                case ViewType.ThreeD: return "3D View";
                default: return t.ToString();
            }
        }

        /// <summary>The template applied to the view, when it controls the Worksets tab; otherwise null.</summary>
        public static View ControllingTemplate(Document doc, View view)
        {
            try
            {
                if (view.IsTemplate || !Compat.IsValid(view.ViewTemplateId)) return null;
                var template = doc.GetElement(view.ViewTemplateId) as View;
                if (template == null) return null;
                return TemplateControlsWorksets(template) ? template : null;
            }
            catch { return null; }
        }

        public static bool TemplateControlsWorksets(View template)
        {
            try
            {
                return template.GetTemplateParameterIds().Contains(WorksetsTemplateParameter) &&
                       !template.GetNonControlledTemplateParameterIds().Contains(WorksetsTemplateParameter);
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ set visibility

        public enum TemplateHandling { SkipView, ChangeTemplate }

        public static void SetVisibility(Document doc, IList<ElementId> viewIds, IList<WorksetId> worksets, WorksetVisibility visibility,
                                         TemplateHandling handling, Report report)
        {
            // Work out the real targets first: views, or the templates that govern them.
            var targets = new List<View>();
            var seen = new HashSet<long>();
            var skipped = new List<string>();
            foreach (ElementId id in viewIds)
            {
                var view = doc.GetElement(id) as View;
                if (view == null) continue;
                View governing = ControllingTemplate(doc, view);
                if (governing != null)
                {
                    if (handling == TemplateHandling.SkipView) { skipped.Add(view.Name + "  (template \"" + governing.Name + "\")"); continue; }
                    if (seen.Add(Compat.IdValue(governing.Id)))
                    {
                        targets.Add(governing);
                        report.Note("View \"" + view.Name + "\" is governed by template \"" + governing.Name + "\" - the template is changed instead.");
                    }
                    continue;
                }
                if (seen.Add(Compat.IdValue(view.Id))) targets.Add(view);
            }
            report.SkippedGroup("their view template controls worksets (tick the template itself, or choose \"change the template\")", skipped);
            if (targets.Count == 0) { report.Note("No view to change."); return; }

            string state = visibility == WorksetVisibility.Visible ? "Show" : visibility == WorksetVisibility.Hidden ? "Hide" : "Use global setting";
            ModelChange.Run(doc, "Workset visibility", report, () =>
            {
                foreach (View v in targets)
                {
                    int changed = 0;
                    foreach (WorksetId ws in worksets)
                    {
                        if (v.GetWorksetVisibility(ws) == visibility) continue;
                        v.SetWorksetVisibility(ws, visibility);
                        changed++;
                    }
                    report.Done((v.IsTemplate ? "Template" : "View") + " \"" + v.Name + "\": " + state + " - " +
                                (changed == 0 ? "already set" : changed + " workset(s) changed") + ".");
                }
            });
        }

        // ------------------------------------------------------------------ status

        public static string CellText(Document doc, View view, WorksetId ws, WorksetDefaultVisibilitySettings defaults)
        {
            try
            {
                View source = ControllingTemplate(doc, view) ?? view;
                WorksetVisibility setting = source.GetWorksetVisibility(ws);
                switch (setting)
                {
                    case WorksetVisibility.Visible: return "Visible";
                    case WorksetVisibility.Hidden: return "Hidden";
                    default:
                        bool global = defaults.IsWorksetVisible(ws);
                        return "Global (" + (global ? "Visible" : "Hidden") + ")";
                }
            }
            catch { return "?"; }
        }

        // ------------------------------------------------------------------ 3D views

        public static readonly string[] Tokens = { "Workset" };

        /// <summary>
        /// Creates (or updates, when a 3D view with that name exists) a 3D view showing exactly the given
        /// worksets: those Visible, every other user workset Hidden.
        /// </summary>
        public static void Create3D(Document doc, IList<KeyValuePair<string, List<WorksetId>>> views, Report report)
        {
            ViewFamilyType vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null) { report.Error("The model has no 3D view type."); return; }

            List<Workset> all = Worksets.User(doc);
            var closed = all.Where(w => !w.IsOpen).Select(w => w.Name).ToList();

            ModelChange.Run(doc, "Workset 3D views", report, () =>
            {
                var existing = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                    .Where(v => !v.IsTemplate).ToList();

                foreach (var request in views)
                {
                    string name = WorksetNames.Sanitize(request.Key);
                    if (name.Length == 0) { report.Skipped("A view with an empty name was not created."); continue; }

                    View3D view = existing.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
                    bool updated = view != null;
                    if (view == null)
                    {
                        view = View3D.CreateIsometric(doc, vft.Id);
                        try { view.Name = name; }
                        catch (Exception)
                        {
                            string unique = NameTemplate.Unique(name, n => ViewNameTaken(doc, n));
                            view.Name = unique;
                            report.Note("\"" + name + "\" is used by another view, so the new view is \"" + unique + "\".");
                            name = unique;
                        }
                        existing.Add(view);
                    }

                    // A template that controls worksets would override what we set: take it off this view.
                    View governing = ControllingTemplate(doc, view);
                    if (governing != null)
                    {
                        view.ViewTemplateId = ElementId.InvalidElementId;
                        report.Note("\"" + name + "\": view template \"" + governing.Name + "\" removed from the view, because it controls worksets.");
                    }

                    var show = new HashSet<int>(request.Value.Select(i => i.IntegerValue));
                    foreach (Workset w in all)
                        view.SetWorksetVisibility(w.Id, show.Contains(w.Id.IntegerValue) ? WorksetVisibility.Visible : WorksetVisibility.Hidden);

                    report.Done((updated ? "Updated" : "Created") + " 3D view \"" + name + "\" showing " + show.Count + " workset(s).");
                    var closedShown = request.Value.Select(i => Worksets.NameOf(doc, i)).Where(n => closed.Contains(n)).ToList();
                    if (closedShown.Count > 0)
                        report.Note("\"" + name + "\" shows worksets that are closed in your session (" + string.Join(", ", closedShown) + ") - open them in Manage Worksets to see their elements.");
                }
            });
        }

        private static bool ViewNameTaken(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View>().Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
