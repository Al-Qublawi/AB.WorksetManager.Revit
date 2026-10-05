using System;
using System.Collections.Generic;
using System.Linq;

namespace AB.WorksetManager.Core
{
    public enum CreateStatus
    {
        /// <summary>Not in the model; can be created.</summary>
        Ready,
        /// <summary>A workset with exactly this name is in the model.</summary>
        Exists,
        /// <summary>The model has the name with different capitals - Revit treats it as the same name.</summary>
        ExistsOtherCase,
        /// <summary>The list names it more than once; only the first is used.</summary>
        Duplicate,
        /// <summary>Empty, or holds characters Revit does not allow.</summary>
        Invalid
    }

    public sealed class CreateCheck
    {
        public CreateCheck(NameRow row, CreateStatus status, string message, string existingName)
        {
            Row = row;
            Status = status;
            Message = message;
            ExistingName = existingName;
        }

        public NameRow Row { get; }
        public string Name => Row.Name;
        public CreateStatus Status { get; }
        public string Message { get; }
        /// <summary>The model's spelling when the name exists.</summary>
        public string ExistingName { get; }
        public bool CanCreate => Status == CreateStatus.Ready;
    }

    /// <summary>Checks a list of names against the worksets already in the model.</summary>
    public static class CreatePlan
    {
        public static List<CreateCheck> Check(IEnumerable<NameRow> rows, IEnumerable<string> existing)
        {
            var model = new Dictionary<string, string>(WorksetNames.Comparer);
            foreach (string e in existing ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(e) && !model.ContainsKey(e)) model[e] = e;

            var seen = new Dictionary<string, int>(WorksetNames.Comparer);
            var result = new List<CreateCheck>();
            foreach (NameRow row in rows ?? Enumerable.Empty<NameRow>())
            {
                string problem = WorksetNames.Problem(row.Name);
                if (problem != null)
                {
                    result.Add(new CreateCheck(row, CreateStatus.Invalid, problem, null));
                    continue;
                }
                if (seen.TryGetValue(row.Name, out int firstLine))
                {
                    result.Add(new CreateCheck(row, CreateStatus.Duplicate, "Already listed on line " + firstLine + ".", null));
                    continue;
                }
                seen[row.Name] = row.Line;

                if (model.TryGetValue(row.Name, out string existingName))
                {
                    bool sameCase = string.Equals(existingName, row.Name, StringComparison.Ordinal);
                    result.Add(sameCase
                        ? new CreateCheck(row, CreateStatus.Exists, "Already in the model.", existingName)
                        : new CreateCheck(row, CreateStatus.ExistsOtherCase, "In the model as \"" + existingName + "\" (Revit ignores capitals).", existingName));
                    continue;
                }
                result.Add(new CreateCheck(row, CreateStatus.Ready, "Not in the model - can be created.", null));
            }
            return result;
        }

        /// <summary>Worksets in the model that the list does not mention.</summary>
        public static List<string> NotInList(IEnumerable<NameRow> rows, IEnumerable<string> existing)
        {
            var listed = new HashSet<string>((rows ?? Enumerable.Empty<NameRow>()).Select(r => r.Name), WorksetNames.Comparer);
            return (existing ?? Enumerable.Empty<string>()).Where(e => !listed.Contains(e)).OrderBy(e => e, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public enum RenameStatus
    {
        Ready,
        /// <summary>No new name, or the same name: nothing to do.</summary>
        Unchanged,
        /// <summary>No workset with the current name.</summary>
        NotFound,
        /// <summary>The new name is empty or holds characters Revit does not allow.</summary>
        Invalid,
        /// <summary>Another workset, not renamed by this list, already has the new name.</summary>
        NameTaken,
        /// <summary>Two rows give the same new name.</summary>
        DuplicateTarget,
        /// <summary>The same workset is listed twice.</summary>
        DuplicateSource
    }

    public sealed class RenameCheck
    {
        public RenameCheck(NameRow row, string currentName, RenameStatus status, string message)
        {
            Row = row;
            CurrentName = currentName;
            Status = status;
            Message = message;
        }

        public NameRow Row { get; }
        /// <summary>The model's spelling of the workset being renamed (null when not found).</summary>
        public string CurrentName { get; }
        public string NewName => Row.NewName;
        public RenameStatus Status { get; }
        public string Message { get; }
        public bool CanRename => Status == RenameStatus.Ready;
    }

    /// <summary>One rename Revit performs. Swaps and chains (A->B, B->A) go through a temporary name.</summary>
    public sealed class RenameStep
    {
        public RenameStep(string from, string to, bool isTemporary)
        {
            From = from;
            To = to;
            IsTemporary = isTemporary;
        }

        public string From { get; }
        public string To { get; }
        public bool IsTemporary { get; }
        public override string ToString() => From + " -> " + To;
    }

    public static class RenamePlan
    {
        public static List<RenameCheck> Check(IEnumerable<NameRow> rows, IEnumerable<string> existing)
        {
            var model = new Dictionary<string, string>(WorksetNames.Comparer);
            foreach (string e in existing ?? Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(e) && !model.ContainsKey(e)) model[e] = e;

            var list = (rows ?? Enumerable.Empty<NameRow>()).ToList();

            // Which worksets this list renames away (first row per workset only, with a real change).
            var sources = new Dictionary<string, NameRow>(WorksetNames.Comparer);
            foreach (NameRow row in list)
                if (model.ContainsKey(row.Name) && !sources.ContainsKey(row.Name) &&
                    !WorksetNames.IsBlank(row.NewName) && !string.Equals(row.Name, row.NewName, StringComparison.Ordinal) &&
                    WorksetNames.Problem(row.NewName) == null)
                    sources[row.Name] = row;

            var targetCount = new Dictionary<string, int>(WorksetNames.Comparer);
            foreach (NameRow row in sources.Values)
                targetCount[row.NewName] = targetCount.TryGetValue(row.NewName, out int n) ? n + 1 : 1;

            var seenSource = new HashSet<string>(WorksetNames.Comparer);
            var result = new List<RenameCheck>();
            foreach (NameRow row in list)
            {
                if (WorksetNames.IsBlank(row.Name))
                {
                    result.Add(new RenameCheck(row, null, RenameStatus.NotFound, "No current name."));
                    continue;
                }
                if (!model.TryGetValue(row.Name, out string current))
                {
                    result.Add(new RenameCheck(row, null, RenameStatus.NotFound, "No workset called \"" + row.Name + "\" in the model."));
                    continue;
                }
                if (!seenSource.Add(current))
                {
                    result.Add(new RenameCheck(row, current, RenameStatus.DuplicateSource, "This workset is already listed above."));
                    continue;
                }
                if (WorksetNames.IsBlank(row.NewName) || string.Equals(current, row.NewName, StringComparison.Ordinal))
                {
                    result.Add(new RenameCheck(row, current, RenameStatus.Unchanged, "No new name - left as it is."));
                    continue;
                }
                string problem = WorksetNames.Problem(row.NewName);
                if (problem != null)
                {
                    result.Add(new RenameCheck(row, current, RenameStatus.Invalid, problem));
                    continue;
                }
                if (targetCount.TryGetValue(row.NewName, out int count) && count > 1)
                {
                    result.Add(new RenameCheck(row, current, RenameStatus.DuplicateTarget, "Another row gives the same new name."));
                    continue;
                }
                // Taken by a workset that keeps its name? (Renaming only the capitals of itself is fine.)
                if (model.TryGetValue(row.NewName, out string holder) &&
                    !WorksetNames.Comparer.Equals(holder, current) && !sources.ContainsKey(holder))
                {
                    result.Add(new RenameCheck(row, current, RenameStatus.NameTaken, "\"" + holder + "\" already has this name."));
                    continue;
                }
                result.Add(new RenameCheck(row, current, RenameStatus.Ready, "Will be renamed."));
            }

            // A chain whose next link failed must fail too: A->B is only possible when B is free or moves away.
            bool changed = true;
            while (changed)
            {
                changed = false;
                var ready = new HashSet<string>(result.Where(r => r.CanRename).Select(r => r.CurrentName), WorksetNames.Comparer);
                for (int i = 0; i < result.Count; i++)
                {
                    RenameCheck r = result[i];
                    if (!r.CanRename) continue;
                    if (model.TryGetValue(r.NewName, out string holder) && !WorksetNames.Comparer.Equals(holder, r.CurrentName) && !ready.Contains(holder))
                    {
                        result[i] = new RenameCheck(r.Row, r.CurrentName, RenameStatus.NameTaken, "\"" + holder + "\" has this name and is not renamed.");
                        changed = true;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// The renames to perform, in order. Direct when no new name is held by another workset of the
        /// list (or by the workset itself in other capitals); otherwise those go through a temporary name first.
        /// </summary>
        public static List<RenameStep> Steps(IEnumerable<RenameCheck> checks, Func<string, bool> nameInUse)
        {
            var ready = (checks ?? Enumerable.Empty<RenameCheck>()).Where(c => c.CanRename).ToList();
            var currentNames = new HashSet<string>(ready.Select(c => c.CurrentName), WorksetNames.Comparer);
            var steps = new List<RenameStep>();
            var second = new List<RenameStep>();
            int counter = 0;

            foreach (RenameCheck c in ready)
            {
                if (!currentNames.Contains(c.NewName))
                {
                    steps.Add(new RenameStep(c.CurrentName, c.NewName, false));
                    continue;
                }
                string temp;
                do { temp = "ABWM-TEMP-" + (++counter) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }
                while (nameInUse != null && nameInUse(temp));
                steps.Add(new RenameStep(c.CurrentName, temp, true));
                second.Add(new RenameStep(temp, c.NewName, false));
            }
            steps.AddRange(second);
            return steps;
        }
    }
}
