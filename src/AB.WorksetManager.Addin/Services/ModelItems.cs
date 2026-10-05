using System;
using System.Collections.Generic;
using System.Linq;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Services
{
    // ====================================================================== grids, levels

    internal enum DatumKind { Grid, Level, ScopeBox, ReferencePlane }

    internal sealed class DatumItem
    {
        public ElementId Id { get; set; }
        public DatumKind Kind { get; set; }
        public string Name { get; set; }
        public string Workset { get; set; }
        public string Locked { get; set; }

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case DatumKind.Grid: return "Grid";
                    case DatumKind.Level: return "Level";
                    case DatumKind.ScopeBox: return "Scope Box";
                    default: return "Reference Plane";
                }
            }
        }
    }

    internal static class Datums
    {
        public static List<DatumItem> Collect(Document doc, bool scopeBoxes, bool referencePlanes)
        {
            var items = new List<DatumItem>();
            Add(doc, items, DatumKind.Grid, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Grids).WhereElementIsNotElementType());
            Add(doc, items, DatumKind.Level, new FilteredElementCollector(doc).OfClass(typeof(Level)));
            if (scopeBoxes) Add(doc, items, DatumKind.ScopeBox, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType());
            if (referencePlanes) Add(doc, items, DatumKind.ReferencePlane, new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)));
            return items.OrderBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void Add(Document doc, List<DatumItem> items, DatumKind kind, FilteredElementCollector collector)
        {
            foreach (Element e in collector)
            {
                try
                {
                    // Reference planes drawn inside a view (or unnamed sketch planes) are not datums worth listing.
                    if (kind == DatumKind.ReferencePlane && e.ViewSpecific) continue;
                    Parameter p = ElementMover.WorksetParameter(e);
                    items.Add(new DatumItem
                    {
                        Id = e.Id,
                        Kind = kind,
                        Name = string.IsNullOrEmpty(e.Name) ? "(unnamed) [" + Compat.IdValue(e.Id) + "]" : e.Name,
                        Workset = Worksets.NameOf(doc, e.WorksetId),
                        Locked = p == null || p.IsReadOnly ? ElementMover.LockedReason(e, p) : null
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn("Datum " + Compat.IdValue(e.Id) + " skipped: " + ex.Message);
                }
            }
        }

        public static DatumKind? KindOf(Element e)
        {
            if (e == null) return null;
            if (e is Level) return DatumKind.Level;
            if (e is ReferencePlane) return DatumKind.ReferencePlane;
            BuiltInCategory c = Compat.BuiltInCategoryOf(e.Category);
            if (c == BuiltInCategory.OST_Grids) return DatumKind.Grid;
            if (c == BuiltInCategory.OST_VolumeOfInterest) return DatumKind.ScopeBox;
            return null;
        }
    }

    // ====================================================================== Revit and CAD links

    internal enum LinkKind { RevitLink, CadLink, CadImport }

    internal sealed class LinkItem
    {
        public ElementId InstanceId { get; set; }
        public ElementId TypeId { get; set; }
        public LinkKind Kind { get; set; }
        /// <summary>The file name as Revit shows it ("Arch.rvt", "Site.dwg").</summary>
        public string FileName { get; set; }
        public string InstanceName { get; set; }
        public string InstanceWorkset { get; set; }
        public string TypeWorkset { get; set; }
        public string Locked { get; set; }
        public bool TypeMovable { get; set; }

        public string KindText => Kind == LinkKind.RevitLink ? "Revit link" : Kind == LinkKind.CadLink ? "CAD link" : "CAD import";
        public string KindToken => Kind == LinkKind.RevitLink ? "RVT" : "CAD";
    }

    internal static class Links
    {
        public static List<LinkItem> Collect(Document doc)
        {
            var items = new List<LinkItem>();
            foreach (RevitLinkInstance li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                try
                {
                    var type = doc.GetElement(li.GetTypeId()) as RevitLinkType;
                    var item = new LinkItem
                    {
                        InstanceId = li.Id,
                        TypeId = type?.Id,
                        Kind = LinkKind.RevitLink,
                        FileName = type != null ? type.Name : li.Name,
                        InstanceName = li.Name,
                        InstanceWorkset = Worksets.NameOf(doc, li.WorksetId),
                        TypeWorkset = type != null ? Worksets.NameOf(doc, type.WorksetId) : string.Empty
                    };
                    if (type != null && type.IsNestedLink) item.Locked = "Nested link - it follows its parent link";
                    else if (!ElementMover.CanChangeWorkset(li)) item.Locked = ElementMover.LockedReason(li, ElementMover.WorksetParameter(li));
                    item.TypeMovable = type != null && ElementMover.CanChangeWorkset(type);
                    items.Add(item);
                }
                catch (Exception ex) { Log.Warn("Revit link " + Compat.IdValue(li.Id) + " skipped: " + ex.Message); }
            }

            foreach (ImportInstance ii in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
            {
                try
                {
                    Element type = doc.GetElement(ii.GetTypeId());
                    var item = new LinkItem
                    {
                        InstanceId = ii.Id,
                        TypeId = type?.Id,
                        Kind = ii.IsLinked ? LinkKind.CadLink : LinkKind.CadImport,
                        FileName = type != null ? type.Name : ii.Name,
                        InstanceName = ii.Name,
                        InstanceWorkset = Worksets.NameOf(doc, ii.WorksetId),
                        TypeWorkset = type != null ? Worksets.NameOf(doc, type.WorksetId) : string.Empty
                    };
                    if (!ElementMover.CanChangeWorkset(ii)) item.Locked = ElementMover.LockedReason(ii, ElementMover.WorksetParameter(ii));
                    item.TypeMovable = type != null && ElementMover.CanChangeWorkset(type);
                    items.Add(item);
                }
                catch (Exception ex) { Log.Warn("CAD " + Compat.IdValue(ii.Id) + " skipped: " + ex.Message); }
            }
            return items.OrderBy(i => i.Kind).ThenBy(i => i.FileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string WorksetNameFor(LinkItem item, string revitTemplate, string cadTemplate)
        {
            string template = item.Kind == LinkKind.RevitLink ? revitTemplate : cadTemplate;
            var values = new Dictionary<string, string>
            {
                { "FileName", NameTemplate.FileStem(item.FileName) },
                { "Kind", item.KindToken },
                { "Type", item.KindText }
            };
            return NameTemplate.Expand(template, values);
        }

        public static readonly string[] Tokens = { "FileName", "Kind", "Type" };

        /// <summary>
        /// Instance and (for links) the type move together - the type's workset is what decides whether a
        /// Revit link loads when that workset is closed.
        /// </summary>
        public static List<KeyValuePair<ElementId, string>> Requests(IEnumerable<KeyValuePair<LinkItem, string>> targets)
        {
            var list = new List<KeyValuePair<ElementId, string>>();
            var typesDone = new HashSet<long>();
            foreach (var pair in targets)
            {
                list.Add(new KeyValuePair<ElementId, string>(pair.Key.InstanceId, pair.Value));
                if (pair.Key.TypeId != null && pair.Key.TypeMovable && pair.Key.Kind != LinkKind.CadImport &&
                    typesDone.Add(Compat.IdValue(pair.Key.TypeId)))
                    list.Add(new KeyValuePair<ElementId, string>(pair.Key.TypeId, pair.Value));
            }
            return list;
        }
    }

    // ====================================================================== model elements by category / family / type

    internal sealed class ElementGroup
    {
        public string Category { get; set; }
        public string Family { get; set; }
        public string Type { get; set; }
        public string Workset { get; set; }
        public List<ElementId> Ids { get; } = new List<ElementId>();
        public int Count => Ids.Count;
    }

    internal static class ElementCatalog
    {
        /// <summary>
        /// Every model element (not types) on a user workset, grouped by category, family, type and workset.
        /// Links, CAD, grids and levels have their own tabs and are left out; nested family parts and
        /// view-specific elements cannot change workset on their own and are left out too.
        /// </summary>
        public static List<ElementGroup> Collect(Document doc)
        {
            var worksets = Worksets.User(doc).ToDictionary(w => w.Id.IntegerValue, w => w.Name);
            var groups = new Dictionary<string, ElementGroup>();
            var typeNames = new Dictionary<long, Tuple<string, string>>();

            var filters = worksets.Keys.Select(k => (ElementFilter)new ElementWorksetFilter(new WorksetId(k), false)).ToList();
            if (filters.Count == 0) return new List<ElementGroup>();
            ElementFilter onUserWorksets = filters.Count == 1 ? filters[0] : new LogicalOrFilter(filters);

            using (Log.Time("Element catalog"))
            {
                foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(onUserWorksets))
                {
                    try
                    {
                        Category cat = e.Category;
                        if (cat == null || cat.CategoryType != CategoryType.Model) continue;
                        // Sub-category elements are line styles: sketch lines of floors, stairs, area boundaries.
                        // They belong to their sketch and are not "families" anyone moves on purpose.
                        if (cat.Parent != null) continue;
                        if (e is SketchPlane || e is Sketch) continue;
                        if (e is RevitLinkInstance || e is ImportInstance) continue;
                        BuiltInCategory bic = Compat.BuiltInCategoryOf(cat);
                        if (bic == BuiltInCategory.OST_Grids || bic == BuiltInCategory.OST_Levels || bic == BuiltInCategory.OST_VolumeOfInterest ||
                            bic == BuiltInCategory.OST_RvtLinks || bic == BuiltInCategory.OST_Cameras || bic == BuiltInCategory.OST_IOSSketchGrid) continue;
                        if (e.ViewSpecific) continue;
                        if (e is FamilyInstance fi && fi.SuperComponent != null) continue;

                        if (!worksets.TryGetValue(e.WorksetId.IntegerValue, out string wsName)) continue;

                        ElementId typeId = e.GetTypeId();
                        long typeKey = Compat.IdValue(typeId);
                        if (!typeNames.TryGetValue(typeKey, out Tuple<string, string> names))
                        {
                            var type = Compat.IsValid(typeId) ? doc.GetElement(typeId) as ElementType : null;
                            // Elements without a type (rooms, areas, spaces) group by category alone.
                            names = type == null
                                ? Tuple.Create("(no family)", "(no type)")
                                : Tuple.Create(string.IsNullOrEmpty(type.FamilyName) ? cat.Name : type.FamilyName, type.Name);
                            typeNames[typeKey] = names;
                        }

                        string key = cat.Name + "\u0001" + names.Item1 + "\u0001" + names.Item2 + "\u0001" + wsName;
                        if (!groups.TryGetValue(key, out ElementGroup g))
                            groups[key] = g = new ElementGroup { Category = cat.Name, Family = names.Item1, Type = names.Item2, Workset = wsName };
                        g.Ids.Add(e.Id);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Catalog skipped " + Compat.IdValue(e.Id) + ": " + ex.Message);
                    }
                }
            }
            return groups.Values
                .OrderBy(g => g.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Family, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Type, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Workset, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
