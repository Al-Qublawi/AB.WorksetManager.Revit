using System;
using System.Collections.Generic;
using System.Linq;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Services
{
    /// <summary>One user workset as the window shows it.</summary>
    internal sealed class WorksetInfo
    {
        public WorksetId Id { get; set; }
        public string Name { get; set; }
        public bool IsOpen { get; set; }
        public bool IsEditable { get; set; }
        public string Owner { get; set; }
        public bool VisibleByDefault { get; set; }
        public bool IsActive { get; set; }
        /// <summary>Model elements (not types) on the workset; -1 until counted.</summary>
        public int ElementCount { get; set; } = -1;

        public int Key => Id.IntegerValue;
        public override string ToString() => Name;
    }

    /// <summary>Reading the user worksets of a document.</summary>
    internal static class Worksets
    {
        public const string SharedLevelsAndGrids = "Shared Levels and Grids";

        public static List<Workset> User(Document doc) =>
            new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets()
                .OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        private static List<Workset> ToWorksets(this FilteredWorksetCollector collector)
        {
            var list = new List<Workset>();
            foreach (Workset w in collector) list.Add(w);
            return list;
        }

        public static List<WorksetInfo> Describe(Document doc, bool countElements)
        {
            WorksetTable table = doc.GetWorksetTable();
            WorksetId active = table.GetActiveWorksetId();
            WorksetDefaultVisibilitySettings defaults = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
            var result = new List<WorksetInfo>();
            foreach (Workset w in User(doc))
            {
                var info = new WorksetInfo
                {
                    Id = w.Id,
                    Name = w.Name,
                    IsOpen = w.IsOpen,
                    IsEditable = w.IsEditable,
                    Owner = w.Owner ?? string.Empty,
                    IsActive = w.Id == active
                };
                try { info.VisibleByDefault = defaults.IsWorksetVisible(w.Id); }
                catch { info.VisibleByDefault = w.IsVisibleByDefault; }
                if (countElements) info.ElementCount = CountElements(doc, w.Id);
                result.Add(info);
            }
            return result;
        }

        public static int CountElements(Document doc, WorksetId id)
        {
            try
            {
                return new FilteredElementCollector(doc).WherePasses(new ElementWorksetFilter(id, false))
                    .WhereElementIsNotElementType().GetElementCount();
            }
            catch (Exception ex)
            {
                Log.Warn("Counting elements on workset " + id.IntegerValue + " failed: " + ex.Message);
                return -1;
            }
        }

        public static Workset Find(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string wanted = name.Trim();
            return User(doc).FirstOrDefault(w => WorksetNames.Comparer.Equals(w.Name, wanted));
        }

        public static string NameOf(Document doc, WorksetId id)
        {
            try
            {
                Workset w = doc.GetWorksetTable().GetWorkset(id);
                return w != null ? w.Name : "(unknown workset)";
            }
            catch { return "(unknown workset)"; }
        }

        public static bool NameInUse(Document doc, string name)
        {
            try { return !WorksetTable.IsWorksetNameUnique(doc, name); }
            catch { return Find(doc, name) != null; }
        }

        /// <summary>The user's name as Revit records ownership.</summary>
        public static string CurrentUser(Document doc)
        {
            try { return doc.Application.Username ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>Someone else's name when another user has the whole workset checked out; otherwise null.</summary>
        public static string OtherOwner(Document doc, Workset w)
        {
            string owner = w.Owner;
            if (string.IsNullOrEmpty(owner)) return null;
            return string.Equals(owner, CurrentUser(doc), StringComparison.OrdinalIgnoreCase) ? null : owner;
        }
    }
}
