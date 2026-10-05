using System;
using System.Collections.Generic;
using System.Diagnostics;
using ABAdvTools;
using Autodesk.Revit.DB;

namespace AB.WorksetManager.Addin.Infrastructure
{
    /// <summary>Every Revit API difference between 2022 and 2027 this add-in meets, in one place.</summary>
    internal static class Compat
    {
        public static long IdValue(ElementId id)
        {
            if (id == null) return -1;
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static bool IsValid(ElementId id) => id != null && id != ElementId.InvalidElementId;

        public static BuiltInCategory BuiltInCategoryOf(Category category)
        {
            if (category == null) return BuiltInCategory.INVALID;
#if REVIT2023_OR_GREATER
            try { return category.BuiltInCategory; }
            catch (Exception) { return BuiltInCategory.INVALID; }
#else
            try
            {
                int raw = category.Id.IntegerValue;
                return Enum.IsDefined(typeof(BuiltInCategory), raw) ? (BuiltInCategory)raw : BuiltInCategory.INVALID;
            }
            catch (Exception) { return BuiltInCategory.INVALID; }
#endif
        }
    }

    /// <summary>Logging through the AB Adv Tools kit: %LOCALAPPDATA%\AB Adv Tools\Logs.</summary>
    internal static class Log
    {
        public static void Info(string message) => Safe(() => AdvToolsLog.Info("[Worksets] " + message));
        public static void Warn(string message) => Safe(() => AdvToolsLog.Warn("[Worksets] " + message));
        public static void Error(string message, Exception ex) => Safe(() => AdvToolsLog.Error("[Worksets] " + message, ex));

        public static string Folder
        {
            get
            {
                try { return AdvToolsLog.Directory; }
                catch { return string.Empty; }
            }
        }

        public static IDisposable Time(string what) => new Timer(what);

        private sealed class Timer : IDisposable
        {
            private readonly string _what;
            private readonly Stopwatch _watch = Stopwatch.StartNew();
            public Timer(string what) { _what = what; }
            public void Dispose() => Info(_what + " took " + _watch.ElapsedMilliseconds + " ms.");
        }

        private static void Safe(Action write)
        {
            try { write(); }
            catch { /* logging must never break Revit */ }
        }
    }

    /// <summary>
    /// Keeps Revit's own dialogs out of a batch: warnings are recorded and dismissed. Any error rolls
    /// the whole step back - this add-in never accepts a "resolution" Revit offers (those can delete
    /// elements), so the model is either changed as previewed or not at all.
    /// </summary>
    internal sealed class Failures : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public static Failures Attach(Transaction transaction)
        {
            var failures = new Failures();
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(failures);
            options.SetClearAfterRollback(true);
            options.SetForcedModalHandling(false);
            transaction.SetFailureHandlingOptions(options);
            return failures;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            bool error = false;
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                string text = SafeText(failure);
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    if (!Warnings.Contains(text)) Warnings.Add(text);
                    accessor.DeleteWarning(failure);
                }
                else
                {
                    if (!Errors.Contains(text)) Errors.Add(text);
                    error = true;
                }
            }
            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        private static string SafeText(FailureMessageAccessor failure)
        {
            try { return failure.GetDescriptionText(); }
            catch { return "(Revit message)"; }
        }
    }
}
