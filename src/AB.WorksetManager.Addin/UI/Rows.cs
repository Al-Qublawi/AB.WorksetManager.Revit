using AB.WorksetManager.Addin.Services;
using AB.WorksetManager.Core;

namespace AB.WorksetManager.Addin.UI
{
    internal sealed class WorksetRow : Row
    {
        public WorksetRow(WorksetInfo info) { Info = info; }
        public WorksetInfo Info { get; }
        public string Name => Info.Name;
        public string Elements => Info.ElementCount < 0 ? "" : Info.ElementCount.ToString("N0");
        public string Open => Info.IsOpen ? "Yes" : "Closed";
        public string Editable => Info.IsEditable ? "Yes" : "No";
        public string Owner => Info.Owner;
        public string Visible => Info.VisibleByDefault ? "Yes" : "No";
        public string Active => Info.IsActive ? "★" : "";
    }

    internal sealed class CreateRow : Row
    {
        private bool _visible;

        public CreateRow(CreateCheck check)
        {
            Check = check;
            _visible = check.Row.Visible ?? true;
            Status = check.Message;
            IsChecked = check.CanCreate;
        }

        public CreateCheck Check { get; }
        public int Line => Check.Row.Line;
        public string Name => Check.Name;
        public override bool CanTick => Check.CanCreate;

        public bool Visible
        {
            get => _visible;
            set { _visible = value; OnChanged(); }
        }
    }

    internal sealed class RenameRow : Row
    {
        private string _newName;

        public RenameRow(string name) { Name = name; _newName = string.Empty; }

        public string Name { get; }

        public string NewName
        {
            get => _newName;
            set { _newName = value ?? string.Empty; Status = string.Empty; OnChanged(); }
        }
    }

    internal sealed class DatumRow : Row
    {
        public DatumRow(DatumItem item) { Item = item; Status = item.Locked; }
        public DatumItem Item { get; }
        public string Kind => Item.KindText;
        public string Name => Item.Name;
        public string Workset => Item.Workset;
        public override bool CanTick => Item.Locked == null;
    }

    internal sealed class LinkRow : Row
    {
        private string _newWorkset;

        public LinkRow(LinkItem item, string newWorkset)
        {
            Item = item;
            _newWorkset = newWorkset;
            Status = item.Locked;
        }

        public LinkItem Item { get; }
        public string Kind => Item.KindText;
        public string FileName => Item.FileName;
        public string InstanceWorkset => Item.InstanceWorkset;
        public string TypeWorkset => Item.Kind == LinkKind.CadImport ? "" : Item.TypeWorkset;
        public override bool CanTick => Item.Locked == null;

        public string NewWorkset
        {
            get => _newWorkset;
            set { _newWorkset = value ?? string.Empty; OnChanged(); }
        }
    }

    internal sealed class ElementRow : Row
    {
        public ElementRow(ElementGroup group) { Group = group; }
        public ElementGroup Group { get; }
        public string Category => Group.Category;
        public string Family => Group.Family;
        public string Type => Group.Type;
        public string Workset => Group.Workset;
        public int Count => Group.Count;
    }

    internal sealed class ViewRow : Row
    {
        public ViewRow(ViewItem item) { Item = item; }
        public ViewItem Item { get; }
        public string Name => Item.IsTemplate ? Item.Name + "  (template)" : Item.Name;
        public string ViewType => Item.ViewType;
        public string Template => Item.ControllingTemplate ?? "";
    }
}
