using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Infrastructure
{
    internal enum ReportKind { Done, Skipped, Note, Error }

    internal sealed class ReportLine
    {
        public ReportLine(ReportKind kind, string text) { Kind = kind; Text = text; }
        public ReportKind Kind { get; }
        public string Text { get; }

        public override string ToString()
        {
            switch (Kind)
            {
                case ReportKind.Done: return "  OK    " + Text;
                case ReportKind.Skipped: return "  SKIP  " + Text;
                case ReportKind.Error: return "  ERROR " + Text;
                default: return "        " + Text;
            }
        }
    }

    /// <summary>
    /// What one action did, line by line, for the results panel. Lines written while a transaction is
    /// open are held back and dropped if Revit rolls it back, so the report never claims a change
    /// the model does not have.
    /// </summary>
    internal sealed class Report
    {
        private readonly List<ReportLine> _lines = new List<ReportLine>();
        private List<ReportLine> _pending;

        public Report(string title) { Title = title; Started = DateTime.Now; }

        public string Title { get; }
        public DateTime Started { get; }
        public IReadOnlyList<ReportLine> Lines => _lines;
        public int DoneCount => _lines.Count(l => l.Kind == ReportKind.Done);
        public bool HasErrors => _lines.Any(l => l.Kind == ReportKind.Error);
        /// <summary>Set by an action when the model was changed.</summary>
        public bool ModelChanged { get; set; }

        public void Done(string text) => Add(ReportKind.Done, text);
        public void Skipped(string text) => Add(ReportKind.Skipped, text);
        public void Note(string text) => Add(ReportKind.Note, text);
        public void Error(string text) => Add(ReportKind.Error, text);

        /// <summary>"3 skipped - reason:" followed by up to 40 element labels.</summary>
        public void SkippedGroup(string reason, IList<string> labels)
        {
            if (labels == null || labels.Count == 0) return;
            Add(ReportKind.Skipped, labels.Count + " skipped - " + reason + ":");
            foreach (string label in labels.Take(40)) Add(ReportKind.Note, "    " + label);
            if (labels.Count > 40) Add(ReportKind.Note, "    ... and " + (labels.Count - 40) + " more");
        }

        private void Add(ReportKind kind, string text)
        {
            (_pending ?? _lines).Add(new ReportLine(kind, text));
        }

        internal void BeginPending() => _pending = new List<ReportLine>();

        internal void EndPending(bool keep)
        {
            if (_pending != null && keep) _lines.AddRange(_pending);
            _pending = null;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Started.ToString("HH:mm:ss") + "  " + Title);
            foreach (ReportLine line in _lines) sb.AppendLine(line.ToString());
            return sb.ToString();
        }
    }

    /// <summary>Runs one change to the model as one Revit undo step, all or nothing.</summary>
    internal static class ModelChange
    {
        public const string Prefix = "AB Worksets - ";

        /// <summary>
        /// Runs <paramref name="work"/> inside a transaction. Returns true when Revit committed it.
        /// Any exception or Revit error rolls everything back and is written to the report.
        /// </summary>
        public static bool Run(Document doc, string name, Report report, Action work)
        {
            string problem = DocumentGuard.ProblemForChange(doc);
            if (problem != null)
            {
                report.Error(problem);
                return false;
            }

            using (var t = new Transaction(doc, Prefix + name))
            {
                Failures failures = Failures.Attach(t);
                report.BeginPending();
                try
                {
                    if (t.Start() != TransactionStatus.Started)
                    {
                        report.EndPending(false);
                        report.Error("Revit did not allow a change to the model right now. Nothing was changed.");
                        return false;
                    }

                    work();

                    TransactionStatus status = t.Commit();
                    bool ok = status == TransactionStatus.Committed;
                    report.EndPending(ok);
                    foreach (string w in failures.Warnings.Take(10)) report.Note("Revit warning (dismissed): " + w);
                    if (!ok)
                    {
                        string why = failures.Errors.Count > 0 ? string.Join(" ", failures.Errors.Take(5)) : "status " + status;
                        report.Error("Revit cancelled the change, so nothing was changed. " + why);
                        Log.Warn(name + " rolled back: " + why);
                        return false;
                    }
                    report.ModelChanged = true;
                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    }
                    catch (Exception rollbackEx)
                    {
                        Log.Error("Rollback of " + name + " failed.", rollbackEx);
                    }
                    report.EndPending(false);
                    report.Error("Stopped, and nothing was changed: " + ex.Message);
                    Log.Error(name + " failed and was rolled back.", ex);
                    return false;
                }
            }
        }
    }

    /// <summary>Whether this add-in can work on a document at all.</summary>
    internal static class DocumentGuard
    {
        /// <summary>Null when the document is a workshared project this add-in can work on.</summary>
        public static string ProblemForUse(Document doc)
        {
            if (doc == null) return "Open a project first.";
            if (doc.IsFamilyDocument) return "Worksets exist in projects only. Open a project, not a family.";
            if (doc.IsLinked) return "This is a linked model. Open it on its own to manage its worksets.";
            if (!doc.IsWorkshared)
                return "This model is not workshared, so it has no worksets." + Environment.NewLine + Environment.NewLine +
                       "To use worksets, enable worksharing in Revit (Collaborate > Worksets), then save the model as the central model.";
            return null;
        }

        /// <summary>Null when the model can be changed now.</summary>
        public static string ProblemForChange(Document doc)
        {
            string problem = ProblemForUse(doc);
            if (problem != null) return problem;
            if (doc.IsReadOnly) return "The model is read-only, so nothing can be changed.";
            if (doc.IsModifiable) return "Revit is in the middle of another change. Finish or cancel it, then try again.";
            return null;
        }

        /// <summary>A local copy of a central model (file or cloud) - ownership has to be obtained from central.</summary>
        public static bool UsesCentral(Document doc)
        {
            try { return doc.IsWorkshared && !doc.IsDetached; }
            catch { return false; }
        }
    }
}
