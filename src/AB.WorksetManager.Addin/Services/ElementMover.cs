using System;
using System.Collections.Generic;
using System.Linq;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Services
{
    /// <summary>One element going to one workset (named, so a workset can be created on the way).</summary>
    internal sealed class MoveItem
    {
        public ElementId Id { get; set; }
        public string Label { get; set; }
        public string FromName { get; set; }
        public string TargetName { get; set; }
    }

    /// <summary>What a move will do, worked out before anything changes.</summary>
    internal sealed class MovePlan
    {
        public List<MoveItem> Items { get; } = new List<MoveItem>();
        public Dictionary<string, List<string>> Skipped { get; } = new Dictionary<string, List<string>>();
        public int AlreadyThere { get; set; }
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Target worksets that do not exist yet and will be created.</summary>
        public List<string> NewWorksets { get; } = new List<string>();

        public int SkippedCount => Skipped.Values.Sum(l => l.Count);

        public void Skip(string reason, string label)
        {
            if (!Skipped.TryGetValue(reason, out List<string> list)) Skipped[reason] = list = new List<string>();
            list.Add(label);
        }

        /// <summary>Short text for a confirmation box.</summary>
        public string Summary()
        {
            var lines = new List<string>();
            foreach (var g in Items.GroupBy(i => i.TargetName, WorksetNames.Comparer))
                lines.Add(g.Count() + " element(s) will move to \"" + g.Key + "\"" + (NewWorksets.Contains(g.Key, WorksetNames.Comparer) ? " (new workset)" : string.Empty) + ".");
            if (Items.Count == 0) lines.Add("Nothing needs to move.");
            if (AlreadyThere > 0) lines.Add(AlreadyThere + " already on the target workset.");
            foreach (var pair in Skipped) lines.Add(pair.Value.Count + " skipped - " + pair.Key + ".");
            lines.AddRange(Notes.Take(5));
            return string.Join(Environment.NewLine, lines);
        }

        public void WriteSkips(Report report)
        {
            foreach (var pair in Skipped) report.SkippedGroup(pair.Key, pair.Value);
            foreach (string note in Notes) report.Note(note);
            if (AlreadyThere > 0) report.Note(AlreadyThere + " element(s) were already on the target workset.");
        }
    }

    /// <summary>
    /// Changes the workset of elements, safely:
    ///   - elements inside a model group move with their whole group (members cannot move on their own),
    ///   - elements Revit keeps on a fixed workset (view-specific, nested, types) are listed, not forced,
    ///   - elements borrowed by someone else, or changed in central since the last reload, are left alone,
    ///   - the rest are borrowed from central first, then moved in one transaction (one Undo).
    /// </summary>
    internal static class ElementMover
    {
        public static MovePlan Plan(Document doc, IEnumerable<KeyValuePair<ElementId, string>> requests, bool allowCreate)
        {
            var plan = new MovePlan();
            var existing = Worksets.User(doc).ToDictionary(w => w.Name, w => w.Id, WorksetNames.Comparer);
            bool central = DocumentGuard.UsesCentral(doc);
            var seen = new HashSet<long>();
            var groupsNoted = new HashSet<long>();

            foreach (var request in requests)
            {
                Element e = SafeGet(doc, request.Key);
                if (e == null) { plan.Skip("no longer in the model", "Id " + Compat.IdValue(request.Key)); continue; }

                string target = WorksetNames.Tidy(request.Value);
                if (WorksetNames.Problem(target) != null) { plan.Skip("no valid target workset name", Label(doc, e)); continue; }

                // A grouped element moves with its outermost group.
                Element top = TopGroup(doc, e);
                if (top.Id != e.Id && groupsNoted.Add(Compat.IdValue(top.Id)))
                    plan.Notes.Add("Model group \"" + top.Name + "\" moves as a whole, because it contains selected elements.");
                e = top;
                if (!seen.Add(Compat.IdValue(e.Id))) continue;

                Parameter p = WorksetParameter(e);
                if (p == null || p.IsReadOnly) { plan.Skip(LockedReason(e, p), Label(doc, e)); continue; }

                bool targetExists = existing.TryGetValue(target, out WorksetId targetId);
                if (!targetExists && !allowCreate) { plan.Skip("the target workset is not in the model", Label(doc, e)); continue; }
                if (targetExists && e.WorksetId == targetId) { plan.AlreadyThere++; continue; }

                if (central)
                {
                    string block = BlockReason(doc, e.Id);
                    if (block != null) { plan.Skip(block, Label(doc, e)); continue; }
                }

                if (!targetExists && !plan.NewWorksets.Contains(target, WorksetNames.Comparer)) plan.NewWorksets.Add(target);
                plan.Items.Add(new MoveItem
                {
                    Id = e.Id,
                    Label = Label(doc, e),
                    FromName = Worksets.NameOf(doc, e.WorksetId),
                    TargetName = targetExists ? Worksets.NameOf(doc, targetId) : target
                });
            }
            return plan;
        }

        /// <summary>Borrows and moves. Returns false when nothing could be done (the report says why).</summary>
        public static bool Apply(Document doc, MovePlan plan, Report report, string actionName)
        {
            plan.WriteSkips(report);
            if (plan.Items.Count == 0 && plan.NewWorksets.Count == 0)
            {
                report.Note("Nothing needed to change.");
                return true;
            }

            List<MoveItem> items = plan.Items;
            if (DocumentGuard.UsesCentral(doc) && items.Count > 0)
            {
                ICollection<ElementId> obtained;
                try
                {
                    obtained = WorksharingUtils.CheckoutElements(doc, items.Select(i => i.Id).ToList());
                }
                catch (Exception ex)
                {
                    report.Error("Could not reach the central model to borrow the elements, so nothing was changed. " + ex.Message);
                    Log.Error("CheckoutElements failed.", ex);
                    return false;
                }
                var owned = new HashSet<long>(obtained.Select(Compat.IdValue));
                var refused = items.Where(i => !owned.Contains(Compat.IdValue(i.Id))).ToList();
                if (refused.Count > 0)
                    report.SkippedGroup("could not be borrowed from central (another user is editing them)", refused.Select(i => i.Label).ToList());
                items = items.Where(i => owned.Contains(Compat.IdValue(i.Id))).ToList();
            }

            return ModelChange.Run(doc, actionName, report, () =>
            {
                var ids = Worksets.User(doc).ToDictionary(w => w.Name, w => w.Id, WorksetNames.Comparer);
                foreach (string name in plan.NewWorksets)
                {
                    if (ids.ContainsKey(name)) continue;
                    ids[name] = Workset.Create(doc, name).Id;
                    report.Done("Created workset \"" + name + "\".");
                }

                var moved = new Dictionary<string, int>(WorksetNames.Comparer);
                var failed = new List<string>();
                foreach (MoveItem item in items)
                {
                    try
                    {
                        Element e = doc.GetElement(item.Id);
                        Parameter p = e == null ? null : WorksetParameter(e);
                        if (p == null || p.IsReadOnly || !p.Set(ids[item.TargetName].IntegerValue)) { failed.Add(item.Label); continue; }
                        moved[item.TargetName] = moved.TryGetValue(item.TargetName, out int n) ? n + 1 : 1;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(item.Label + " (" + ex.Message + ")");
                    }
                }
                foreach (var pair in moved) report.Done("Moved " + pair.Value + " element(s) to \"" + pair.Key + "\".");
                report.SkippedGroup("Revit did not accept the change", failed);
            });
        }

        // ------------------------------------------------------------------ helpers

        public static Parameter WorksetParameter(Element e)
        {
            try { return e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); }
            catch { return null; }
        }

        public static bool CanChangeWorkset(Element e)
        {
            Parameter p = WorksetParameter(e);
            return p != null && !p.IsReadOnly;
        }

        public static string LockedReason(Element e, Parameter p)
        {
            if (p == null) return "has no workset";
            try
            {
                if (e.ViewSpecific) return "drawn in one view only - it stays on that view's workset";
                if (e is FamilyInstance fi && fi.SuperComponent != null) return "part of a nested family - it follows its host";
                if (e is ElementType && !(e is RevitLinkType)) return "a type - Revit keeps types on their own worksets";
                if (e is RevitLinkType rlt && rlt.IsNestedLink) return "a nested link - it follows its parent link";
                if (e is RevitLinkInstance rli && TypeOf(rli) is RevitLinkType t && t.IsNestedLink) return "a nested link - it follows its parent link";
            }
            catch { }
            return "Revit does not allow changing its workset";
        }

        private static Element TypeOf(RevitLinkInstance i)
        {
            try { return i.Document.GetElement(i.GetTypeId()); }
            catch { return null; }
        }

        /// <summary>Null when the element can be edited by this user now; otherwise why not.</summary>
        public static string BlockReason(Document doc, ElementId id)
        {
            try
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return "borrowed by " + (string.IsNullOrEmpty(owner) ? "another user" : owner) + " - ask them to Synchronize with Central and relinquish";
                ModelUpdatesStatus updates = WorksharingUtils.GetModelUpdatesStatus(doc, id);
                if (updates == ModelUpdatesStatus.UpdatedInCentral)
                    return "changed in the central model since your last reload - use Reload Latest first";
                if (updates == ModelUpdatesStatus.DeletedInCentral)
                    return "deleted in the central model - use Reload Latest first";
            }
            catch (Exception ex)
            {
                Log.Warn("Ownership check of " + Compat.IdValue(id) + " failed: " + ex.Message);
            }
            return null;
        }

        public static Element TopGroup(Document doc, Element e)
        {
            Element current = e;
            for (int depth = 0; depth < 32; depth++)
            {
                ElementId groupId;
                try { groupId = current.GroupId; }
                catch { break; }
                if (!Compat.IsValid(groupId)) break;
                Element group = SafeGet(doc, groupId);
                if (group == null) break;
                current = group;
            }
            return current;
        }

        public static Element SafeGet(Document doc, ElementId id)
        {
            try { return Compat.IsValid(id) ? doc.GetElement(id) : null; }
            catch { return null; }
        }

        /// <summary>"Walls: Basic Wall: Generic 200 [123456]".</summary>
        public static string Label(Document doc, Element e)
        {
            try
            {
                string category = e.Category != null ? e.Category.Name : e.GetType().Name;
                string name = e.Name;
                if (!(e is ElementType))
                {
                    ElementType type = SafeGet(doc, e.GetTypeId()) as ElementType;
                    if (type != null)
                        name = string.IsNullOrEmpty(type.FamilyName) ? type.Name : type.FamilyName + ": " + type.Name;
                }
                return category + ": " + name + " [" + Compat.IdValue(e.Id) + "]";
            }
            catch
            {
                return "Element [" + Compat.IdValue(e.Id) + "]";
            }
        }
    }
}
