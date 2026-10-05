using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Addin.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace AB.WorksetManager.Addin.UI
{
    /// <summary>What every tab shares: the document, its worksets, picking, and the results panel.</summary>
    internal sealed class ManagerSession
    {
        private Action _pendingPick;
        private Action _afterPick;
        private readonly List<string> _messages = new List<string>();

        public ManagerSession(UIApplication uiapp, UIDocument uidoc)
        {
            UiApp = uiapp;
            UiDoc = uidoc;
            Doc = uidoc.Document;
            RefreshWorksets(true);
        }

        public UIApplication UiApp { get; }
        public UIDocument UiDoc { get; }
        public Document Doc { get; }

        public List<WorksetInfo> Worksets { get; private set; } = new List<WorksetInfo>();

        /// <summary>Raised after the worksets were read again (names, counts, owners may have changed).</summary>
        public event Action WorksetsChanged;

        /// <summary>Raised for every finished action, for the results panel.</summary>
        public event Action<Report> Reported;

        public void RefreshWorksets(bool counts)
        {
            try { Worksets = Services.Worksets.Describe(Doc, counts); }
            catch (Exception ex) { Log.Error("Reading worksets failed.", ex); }
            WorksetsChanged?.Invoke();
        }

        public void Publish(Report report)
        {
            Log.Info(report.ToText().TrimEnd());
            Reported?.Invoke(report);
            if (report.ModelChanged) RefreshWorksets(true);
        }

        public void AddMessage(string text)
        {
            lock (_messages) _messages.Add(text);
        }

        public List<string> TakeMessages()
        {
            lock (_messages)
            {
                var copy = _messages.ToList();
                _messages.Clear();
                return copy;
            }
        }

        // ------------------------------------------------------------------ picking in the model

        /// <summary>The window hides; the command runs <paramref name="pick"/>, then <paramref name="after"/>, then shows the window again.</summary>
        public void RequestPick(Action pick, Action after)
        {
            _pendingPick = pick;
            _afterPick = after;
        }

        public bool HasPendingPick => _pendingPick != null;

        public Action TakePendingPick()
        {
            Action pick = _pendingPick;
            _pendingPick = null;
            return pick;
        }

        public void RunAfterPick()
        {
            Action after = _afterPick;
            _afterPick = null;
            after?.Invoke();
        }

        public List<ElementId> CurrentSelection()
        {
            try { return UiDoc.Selection.GetElementIds().ToList(); }
            catch { return new List<ElementId>(); }
        }

        public List<ElementId> Pick(Func<Element, bool> allow, string prompt)
        {
            IList<Reference> refs = UiDoc.Selection.PickObjects(ObjectType.Element, new Filter(allow), prompt);
            return refs.Select(r => r.ElementId).ToList();
        }

        private sealed class Filter : ISelectionFilter
        {
            private readonly Func<Element, bool> _allow;
            public Filter(Func<Element, bool> allow) { _allow = allow; }

            public bool AllowElement(Element elem)
            {
                try { return elem != null && _allow(elem); }
                catch { return false; }
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }

    /// <summary>A grid row with a tick box.</summary>
    internal abstract class Row : INotifyPropertyChanged
    {
        private bool _isChecked;
        private string _status;

        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value && CanTick; OnChanged(); TickChanged?.Invoke(); } }
        }

        public virtual bool CanTick => true;

        public string Status
        {
            get => _status;
            set { _status = value; OnChanged(); }
        }

        /// <summary>Lets the tab update its "n ticked" text.</summary>
        public static event Action TickChanged;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
