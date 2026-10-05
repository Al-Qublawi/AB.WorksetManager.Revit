using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Core;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Services
{
    /// <summary>Create, rename, delete, default visibility and the active workset.</summary>
    internal static class WorksetService
    {
        public const string OwnershipNote =
            "Revit gives you ownership of worksets you create or rename. Synchronize with Central (with \"Relinquish\" ticked) so others can edit them.";

        // ------------------------------------------------------------------ create

        public static void Create(Document doc, IList<KeyValuePair<string, bool>> namesAndVisibility, Report report)
        {
            // Checked again against the model as it is now - the list may have been checked minutes ago.
            var rows = namesAndVisibility.Select((p, i) => new NameRow(i + 1, p.Key, string.Empty, p.Value)).ToList();
            var checks = CreatePlan.Check(rows, Worksets.User(doc).Select(w => w.Name));
            foreach (CreateCheck c in checks.Where(c => !c.CanCreate)) report.Skipped("\"" + c.Name + "\": " + c.Message);
            var ready = checks.Where(c => c.CanCreate).ToList();
            if (ready.Count == 0) { report.Note("Nothing to create."); return; }

            bool ok = ModelChange.Run(doc, "Create worksets", report, () =>
            {
                WorksetDefaultVisibilitySettings defaults = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                foreach (CreateCheck c in ready)
                {
                    if (Worksets.NameInUse(doc, c.Name)) { report.Skipped("\"" + c.Name + "\": already in the model."); continue; }
                    Workset w = Workset.Create(doc, c.Name);
                    bool visible = c.Row.Visible ?? true;
                    if (!visible) defaults.SetWorksetVisibility(w.Id, false);
                    report.Done("Created \"" + c.Name + "\"" + (visible ? string.Empty : " (not visible in all views)") + ".");
                }
            });
            if (ok && DocumentGuard.UsesCentral(doc)) report.Note(OwnershipNote);
        }

        // ------------------------------------------------------------------ rename

        public static void Rename(Document doc, IList<NameRow> rows, Report report)
        {
            List<Workset> all = Worksets.User(doc);
            var names = all.Select(w => w.Name).ToList();

            // Worksets another user has checked out cannot be renamed; drop them, then re-check the rest,
            // so a chain that depended on them is refused too.
            var blocked = new HashSet<string>(WorksetNames.Comparer);
            foreach (Workset w in all)
            {
                string other = Worksets.OtherOwner(doc, w);
                if (other != null) blocked.Add(w.Name);
            }
            var usable = new List<NameRow>();
            foreach (NameRow row in rows)
            {
                if (blocked.Contains(row.Name) && !WorksetNames.IsBlank(row.NewName) && row.Name != row.NewName)
                    report.Skipped("\"" + row.Name + "\": checked out by " + Worksets.OtherOwner(doc, Worksets.Find(doc, row.Name)) + ".");
                else usable.Add(row);
            }

            List<RenameCheck> checks = RenamePlan.Check(usable, names);
            foreach (RenameCheck c in checks.Where(c => !c.CanRename && c.Status != RenameStatus.Unchanged))
                report.Skipped("\"" + c.Row.Name + "\" -> \"" + c.NewName + "\": " + c.Message);
            List<RenameStep> steps = RenamePlan.Steps(checks, n => Worksets.NameInUse(doc, n));
            if (steps.Count == 0) { report.Note("Nothing to rename."); return; }

            // All renames in one transaction: if any fails, none happen (no temporary name is ever left behind).
            bool ok = ModelChange.Run(doc, "Rename worksets", report, () =>
            {
                foreach (RenameStep step in steps)
                {
                    Workset w = Worksets.Find(doc, step.From);
                    if (w == null) throw new InvalidOperationException("Workset \"" + step.From + "\" was not found.");
                    WorksetTable.RenameWorkset(doc, w.Id, step.To);
                }
                foreach (RenameCheck c in checks.Where(c => c.CanRename))
                    report.Done("\"" + c.CurrentName + "\" renamed to \"" + c.NewName + "\".");
            });
            if (ok && DocumentGuard.UsesCentral(doc)) report.Note(OwnershipNote);
        }

        // ------------------------------------------------------------------ default visibility, active

        public static void SetVisibleByDefault(Document doc, IList<WorksetId> ids, bool visible, Report report)
        {
            ModelChange.Run(doc, visible ? "Visible in all views" : "Not visible in all views", report, () =>
            {
                WorksetDefaultVisibilitySettings defaults = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);
                foreach (WorksetId id in ids)
                {
                    defaults.SetWorksetVisibility(id, visible);
                    report.Done("\"" + Worksets.NameOf(doc, id) + "\": " + (visible ? "visible" : "hidden") + " in all views by default.");
                }
            });
        }

        public static bool SetActive(Document doc, WorksetId id, Report report)
        {
            try
            {
                doc.GetWorksetTable().SetActiveWorksetId(id);
                report.Done("Active workset is now \"" + Worksets.NameOf(doc, id) + "\".");
                return true;
            }
            catch (Exception ex)
            {
                report.Error("Could not make \"" + Worksets.NameOf(doc, id) + "\" active: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ delete

        internal sealed class DeleteCheck
        {
            public Workset Workset { get; set; }
            public int ElementCount { get; set; }
            /// <summary>Null when the workset can be deleted.</summary>
            public string Problem { get; set; }
        }

        public static List<DeleteCheck> CheckDelete(Document doc, IList<WorksetId> ids, WorksetId target)
        {
            var checks = new List<DeleteCheck>();
            bool central = DocumentGuard.UsesCentral(doc);
            foreach (WorksetId id in ids)
            {
                Workset w = doc.GetWorksetTable().GetWorkset(id);
                var check = new DeleteCheck { Workset = w, ElementCount = Worksets.CountElements(doc, id) };
                checks.Add(check);
                if (w == null || w.Kind != WorksetKind.UserWorkset) { check.Problem = "Not a user workset."; continue; }
                if (id == target) { check.Problem = "It is the target workset."; continue; }
                string other = Worksets.OtherOwner(doc, w);
                if (other != null) { check.Problem = "Checked out by " + other + "."; continue; }
                if (central)
                {
                    var owners = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    int borrowed = 0;
                    foreach (ElementId e in new FilteredElementCollector(doc).WherePasses(new ElementWorksetFilter(id, false)).ToElementIds())
                    {
                        string owner;
                        if (WorksharingUtils.GetCheckoutStatus(doc, e, out owner) == CheckoutStatus.OwnedByOtherUser)
                        {
                            borrowed++;
                            if (!string.IsNullOrEmpty(owner)) owners.Add(owner);
                        }
                    }
                    if (borrowed > 0) check.Problem = borrowed + " element(s) borrowed by " + (owners.Count > 0 ? string.Join(", ", owners) : "other users") + ".";
                }
            }
            return checks;
        }

        public static void Delete(Document doc, IList<WorksetId> ids, WorksetId target, Report report)
        {
            string targetName = Worksets.NameOf(doc, target);
            var ok = new List<DeleteCheck>();
            foreach (DeleteCheck c in CheckDelete(doc, ids, target))
            {
                if (c.Problem == null) ok.Add(c);
                else report.Skipped("\"" + (c.Workset != null ? c.Workset.Name : "?") + "\": " + c.Problem);
            }
            if (ok.Count == 0) { report.Note("Nothing to delete."); return; }

            // Revit needs the worksets checked out before it deletes them.
            if (DocumentGuard.UsesCentral(doc))
            {
                ICollection<WorksetId> owned;
                try { owned = WorksharingUtils.CheckoutWorksets(doc, ok.Select(c => c.Workset.Id).ToList()); }
                catch (Exception ex)
                {
                    report.Error("Could not reach the central model to check out the worksets, so nothing was deleted. " + ex.Message);
                    Log.Error("CheckoutWorksets failed.", ex);
                    return;
                }
                var ownedSet = new HashSet<int>(owned.Select(i => i.IntegerValue));
                foreach (DeleteCheck c in ok.Where(c => !ownedSet.Contains(c.Workset.Id.IntegerValue)))
                    report.Skipped("\"" + c.Workset.Name + "\": could not be checked out from central.");
                ok = ok.Where(c => ownedSet.Contains(c.Workset.Id.IntegerValue)).ToList();
            }

            // The active workset cannot be deleted: hand that role to the target first.
            WorksetId active = doc.GetWorksetTable().GetActiveWorksetId();
            if (ok.Any(c => c.Workset.Id == active) && !SetActive(doc, target, report)) return;

            var names = ok.ToDictionary(c => c.Workset.Id.IntegerValue, c => c.Workset.Name);
            ModelChange.Run(doc, "Delete worksets", report, () =>
            {
                foreach (DeleteCheck c in ok)
                {
                    string name = names[c.Workset.Id.IntegerValue];
                    try
                    {
                        string refused = DeleteOne(doc, c.Workset.Id, target);
                        if (refused != null) { report.Skipped("\"" + name + "\": " + refused); continue; }
                    }
                    catch (Exception ex) when (ex is MissingMethodException || ex is TypeLoadException)
                    {
                        throw new InvalidOperationException("Deleting worksets needs Revit 2022.1 or later. Install the latest Revit 2022 update.");
                    }
                    report.Done("Deleted \"" + name + "\" - its " + c.ElementCount + " element(s) moved to \"" + targetName + "\".");
                }
            });
        }

        /// <summary>Kept apart so Revit 2022.0 (no DeleteWorkset yet) fails here with a MissingMethodException we can explain.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string DeleteOne(Document doc, WorksetId id, WorksetId target)
        {
            var settings = new DeleteWorksetSettings(DeleteWorksetOption.MoveElementsToWorkset, target);
            if (!WorksetTable.CanDeleteWorkset(doc, id, settings))
                return "Revit says it cannot be deleted now (it, or elements on it, may be in use by another user).";
            WorksetTable.DeleteWorkset(doc, id, settings);
            return null;
        }
    }
}
