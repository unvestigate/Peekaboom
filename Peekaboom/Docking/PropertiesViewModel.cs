using System.Collections.ObjectModel;
using System.ComponentModel;

using Avalonia;
using Avalonia.Media;

using Dock.Model.Mvvm.Controls;

using Peekaboom.Model;
using Peekaboom.Protocol;

namespace Peekaboom.Docking;

public sealed class PropertyRow : INotifyPropertyChanged
{
    private static readonly IBrush NameBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xC8, 0xFF));
    private static readonly IBrush ValueBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA));
    private static readonly IBrush RefBrush = new SolidColorBrush(Color.FromRgb(0x7E, 0xC8, 0xE8));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

    private string mValue = "";

    public PropertyRow(string key, string name, bool isHeader, int indent, ulong refId, bool missing)
    {
        Key = key;
        Name = name;
        IsHeader = isHeader;
        RefId = refId;
        NameMargin = new Thickness(4 + indent * 14, 0, 4, 0);
        NameForeground = isHeader ? HeaderBrush : NameBrush;
        ValueForeground = missing ? MissingBrush : refId != 0 ? RefBrush : ValueBrush;
        Weight = isHeader ? FontWeight.SemiBold : FontWeight.Normal;
    }

    public string Key { get; }

    public string Name { get; }

    public bool IsHeader { get; }

    public ulong RefId { get; }

    public Thickness NameMargin { get; }

    public IBrush NameForeground { get; }

    public IBrush ValueForeground { get; }

    public FontWeight Weight { get; }

    public string Value
    {
        get => mValue;
        set
        {
            if (mValue != value)
            {
                mValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PropertiesViewModel : Tool
{
    private const int MAX_ITEMS = 8;

    public ObservableCollection<PropertyRow> Rows { get; } = new();

    public string Heading { get; private set; } = "Nothing selected";

    public event Action? HeadingChanged;

    public event Action<ulong>? RefClicked;

    public void ReportRefClicked(ulong id)
    {
        RefClicked?.Invoke(id);
    }

    public void Update(ObjectDb? state, ulong id, long serial)
    {
        var rows = new List<PropertyRow>();
        string heading;

        ObjectInstance? instance = state != null && id != 0 ? state.Find(id) : null;
        if (id == 0)
        {
            heading = "Nothing selected";
        }
        else if (instance == null || (serial >= 0 && instance.Serial != serial))
        {
            heading = ValueFormatter.FormatId(id) + " does not exist at this frame";
        }
        else
        {
            heading = instance.Class.Name + "  " + ValueFormatter.FormatId(instance.Id) + "  #" + instance.Serial;
            BuildRows(state!, instance, rows);
        }

        if (heading != Heading)
        {
            Heading = heading;
            HeadingChanged?.Invoke();
        }

        ApplyRows(rows);
    }

    private static void BuildRows(ObjectDb state, ObjectInstance instance, List<PropertyRow> rows)
    {
        var chain = new List<ClassDef>();
        for (ClassDef? cls = instance.Class; cls != null; cls = cls.Parent)
        {
            chain.Insert(0, cls);
        }

        var shown = new HashSet<string>();
        foreach (ClassDef cls in chain)
        {
            if (cls.Properties.Count == 0)
            {
                continue;
            }

            rows.Add(Header("class:" + cls.Name, cls.Name.Name));
            foreach (PropertyDef property in cls.Properties)
            {
                shown.Add(property.Name);
                AddProperty(state, instance, property.Name, property, rows);
            }
        }

        var extras = new List<string>();
        foreach (KeyValuePair<string, PropertySlot> pair in instance.Values)
        {
            if (!shown.Contains(pair.Key))
            {
                extras.Add(pair.Key);
            }
        }

        foreach (string name in instance.RefNames)
        {
            if (!shown.Contains(name) && !extras.Contains(name))
            {
                extras.Add(name);
            }
        }

        if (extras.Count == 0)
        {
            return;
        }

        extras.Sort(StringComparer.Ordinal);
        rows.Add(Header("class:undeclared", "Undeclared"));
        foreach (string name in extras)
        {
            AddProperty(state, instance, name, null, rows);
        }
    }

    private static PropertyRow Header(string key, string name)
    {
        return new PropertyRow(key, name, true, 0, 0, false);
    }

    private static void AddProperty(ObjectDb state, ObjectInstance instance, string name, PropertyDef? property, List<PropertyRow> rows)
    {
        string key = "property:" + name;

        if (instance.HasRefs(name) || (property != null && property.IsArray && property.DataType.IsObjectRef))
        {
            IReadOnlyList<ulong> refs = instance.GetRefs(name);
            var row = new PropertyRow(key, name, false, 1, 0, refs.Count == 0) { Value = "[" + refs.Count + "]" };
            rows.Add(row);

            for (int i = 0; i < refs.Count; i++)
            {
                ulong target = refs[i];
                var item = new PropertyRow(key + "[" + i + "]", "[" + i + "]", false, 2, target, false)
                {
                    Value = RefText(state, target),
                };
                rows.Add(item);
            }

            return;
        }

        PropertyValue? value = instance.GetValue(name);
        if (value == null)
        {
            rows.Add(new PropertyRow(key, name, false, 1, 0, true) { Value = "not sent" });
            return;
        }

        ulong refId = 0;
        string text;
        if (value.Type.IsObjectRef && value.Count == 1)
        {
            refId = value.ReadPointer(0, 0);
            text = RefText(state, refId);
        }
        else
        {
            text = ValueFormatter.Format(value, property, MAX_ITEMS);
        }

        rows.Add(new PropertyRow(key, name, false, 1, refId, false) { Value = text });
    }

    private static string RefText(ObjectDb state, ulong id)
    {
        if (id == 0)
        {
            return "null";
        }

        ObjectInstance? target = state.Find(id);
        return ValueFormatter.FormatId(id) + (target != null ? "  " + target.Class.Name.Name : "  missing");
    }

    private void ApplyRows(List<PropertyRow> rows)
    {
        bool same = rows.Count == Rows.Count;
        for (int i = 0; same && i < rows.Count; i++)
        {
            same = rows[i].Key == Rows[i].Key && rows[i].RefId == Rows[i].RefId;
        }

        if (same)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Rows[i].Value = rows[i].Value;
            }

            return;
        }

        Rows.Clear();
        foreach (PropertyRow row in rows)
        {
            Rows.Add(row);
        }
    }
}
