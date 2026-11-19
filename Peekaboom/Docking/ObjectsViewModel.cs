using System.Collections.ObjectModel;
using System.ComponentModel;

using Avalonia;
using Avalonia.Media;

using Dock.Model.Mvvm.Controls;

using Peekaboom.Model;
using Peekaboom.Protocol;
using Peekaboom.UI;

namespace Peekaboom.Docking;

public sealed class ObjectNode : INotifyPropertyChanged
{
    private const double INDENT = 14.0;

    private static readonly IBrush ObjectBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA));
    private static readonly IBrush GroupBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));

    private string mLabel = "";
    private bool mIsExpanded;
    private bool mHasChildren;

    public ObjectNode(string key, ulong id, long serial, bool isGroup, ObjectNode? parent)
    {
        Key = key;
        Id = id;
        Serial = serial;
        IsGroup = isGroup;
        Parent = parent;
        Depth = parent != null ? parent.Depth + 1 : 0;
        Indent = new Thickness(Depth * INDENT, 0, 0, 0);
    }

    public string Key { get; }

    public ulong Id { get; }

    public long Serial { get; }

    public bool IsGroup { get; }

    public ObjectNode? Parent { get; }

    public int Depth { get; }

    public Thickness Indent { get; }

    public List<ObjectNode> Children { get; } = new();

    public IBrush Foreground => IsGroup ? GroupBrush : ObjectBrush;

    public Geometry? ExpanderIcon => mHasChildren ? (mIsExpanded ? Icons.ChevronDown : Icons.ChevronRight) : null;

    public string Label
    {
        get => mLabel;
        set
        {
            if (mLabel != value)
            {
                mLabel = value;
                Notify(nameof(Label));
            }
        }
    }

    public bool IsExpanded
    {
        get => mIsExpanded;
        set
        {
            if (mIsExpanded != value)
            {
                mIsExpanded = value;
                Notify(nameof(IsExpanded));
                Notify(nameof(ExpanderIcon));
            }
        }
    }

    public bool HasChildren
    {
        get => mHasChildren;
        set
        {
            if (mHasChildren != value)
            {
                mHasChildren = value;
                Notify(nameof(HasChildren));
                Notify(nameof(ExpanderIcon));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ObjectsViewModel : Tool
{
    private const string CHILDREN_SEMANTIC = "children";
    private const string OTHER_GROUP = "Other";
    private const string SCENES_GROUP = "Scenes";

    private readonly Dictionary<ulong, string> mSceneNames = new();
    private readonly List<ObjectNode> mRoots = new();
    private readonly List<ObjectNode> mFlat = new();
    private ObjectDb? mState;
    private ObjectNode? mSelectedNode;
    private string mFilter = "";

    public ObservableCollection<ObjectNode> Rows { get; } = new();

    public event Action<ObjectNode?>? NodeSelected;

    public event Action<ObjectNode?>? NodeToSelect;

    public void SetSceneName(ulong id, string name)
    {
        mSceneNames[id] = name;
    }

    public void ClearSceneNames()
    {
        mSceneNames.Clear();
    }

    public void SetFilter(string filter)
    {
        mFilter = filter.Trim();
        Refresh();
    }

    public void Update(ObjectDb? state)
    {
        mState = state;
        Refresh();
    }

    public void ReportSelected(ObjectNode? node)
    {
        mSelectedNode = node;
        NodeSelected?.Invoke(node);
    }

    public void Toggle(ObjectNode node)
    {
        if (!node.HasChildren)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        RebuildRows();
    }

    public void SetExpanded(ObjectNode node, bool expanded)
    {
        if (node.HasChildren && node.IsExpanded != expanded)
        {
            node.IsExpanded = expanded;
            RebuildRows();
        }
    }

    public void SelectId(ulong id)
    {
        if (id != 0 && mSelectedNode != null && !mSelectedNode.IsGroup && mSelectedNode.Id == id && Rows.Contains(mSelectedNode))
        {
            NodeToSelect?.Invoke(mSelectedNode);
            return;
        }

        ObjectNode? node = id != 0 ? Find(mRoots, id) : null;
        mSelectedNode = node;
        bool expanded = false;
        for (ObjectNode? parent = node?.Parent; parent != null; parent = parent.Parent)
        {
            if (!parent.IsExpanded)
            {
                parent.IsExpanded = true;
                expanded = true;
            }
        }

        if (expanded)
        {
            RebuildRows();
        }

        NodeToSelect?.Invoke(node);
    }

    private static ObjectNode? Find(List<ObjectNode> nodes, ulong id)
    {
        foreach (ObjectNode node in nodes)
        {
            if (!node.IsGroup && node.Id == id)
            {
                return node;
            }
        }

        foreach (ObjectNode node in nodes)
        {
            ObjectNode? found = Find(node.Children, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void Refresh()
    {
        var specs = new List<NodeSpec>();
        ObjectDb? state = mState;

        if (state != null)
        {
            var reached = new HashSet<ulong>();
            var path = new HashSet<long>();
            List<ObjectInstance> sorted = state.SortedBySerial();

            foreach (ObjectInstance instance in sorted)
            {
                if (instance.IsTopLevel)
                {
                    specs.Add(BuildInstance(state, instance, reached, path, true));
                }
            }

            var other = new NodeSpec("group:" + OTHER_GROUP, 0, -1, true, false);
            foreach (ObjectInstance instance in sorted)
            {
                if (!reached.Contains(instance.Id) && instance.Class.Name.Namespace != LegacyFormat.DEBUGGER_NAMESPACE)
                {
                    other.Children.Add(BuildInstance(state, instance, reached, path, false));
                }
            }

            if (other.Children.Count > 0)
            {
                other.Label = OTHER_GROUP + " (" + other.Children.Count + ")";
                specs.Add(other);
            }

            if (mFilter.Length > 0)
            {
                specs = ApplyFilter(specs);
            }
        }

        Reconcile(mRoots, specs, null, mFilter.Length > 0);
        RebuildRows();
    }

    private NodeSpec BuildInstance(ObjectDb state, ObjectInstance instance, HashSet<ulong> reached, HashSet<long> path, bool expanded)
    {
        reached.Add(instance.Id);
        var spec = new NodeSpec("object:" + instance.Serial, instance.Id, instance.Serial, false, expanded)
        {
            Label = LabelOf(instance),
        };

        if (!path.Add(instance.Serial))
        {
            return spec;
        }

        var properties = new List<PropertyDef>();
        instance.Class.CollectProperties(properties);

        foreach (PropertyDef property in properties)
        {
            if (property.Semantic != CHILDREN_SEMANTIC)
            {
                continue;
            }

            if (property.IsArray)
            {
                IReadOnlyList<ulong> refs = instance.GetRefs(property.Name);
                if (refs.Count == 0)
                {
                    continue;
                }

                var group = new NodeSpec("group:" + property.Name, 0, -1, true, property.Name == SCENES_GROUP);
                foreach (ulong id in refs)
                {
                    ObjectInstance? child = state.Find(id);
                    if (child != null)
                    {
                        group.Children.Add(BuildInstance(state, child, reached, path, LegacyFormat.IsScene(child.Class)));
                    }
                }

                group.Label = property.Name + " (" + group.Children.Count + ")";
                spec.Children.Add(group);
            }
            else
            {
                PropertyValue? value = instance.GetValue(property.Name);
                if (value != null && value.Count > 0 && value.Type.IsObjectRef)
                {
                    ObjectInstance? child = state.Find(value.ReadPointer(0, 0));
                    if (child != null)
                    {
                        spec.Children.Add(BuildInstance(state, child, reached, path, false));
                    }
                }
            }
        }

        path.Remove(instance.Serial);
        return spec;
    }

    private string LabelOf(ObjectInstance instance)
    {
        string className = instance.Class.Name.Name;

        if (mSceneNames.TryGetValue(instance.Id, out string? sceneName) && LegacyFormat.IsScene(instance.Class))
        {
            return sceneName + "  " + className;
        }

        PropertyValue? name = instance.GetValue(LegacyFormat.NAME_PROPERTY);
        if (name != null && name.Count > 0 && name.Type.IsString)
        {
            string text = name.GetString(0, 0);
            if (text.Length > 0)
            {
                return text + "  " + className;
            }
        }

        return className + " " + ValueFormatter.FormatId(instance.Id);
    }

    private List<NodeSpec> ApplyFilter(List<NodeSpec> specs)
    {
        var result = new List<NodeSpec>();
        foreach (NodeSpec spec in specs)
        {
            NodeSpec? filtered = Filter(spec);
            if (filtered != null)
            {
                result.Add(filtered);
            }
        }

        return result;
    }

    private NodeSpec? Filter(NodeSpec spec)
    {
        bool matches = !spec.IsGroup && spec.Label.Contains(mFilter, StringComparison.OrdinalIgnoreCase);
        var children = new List<NodeSpec>();

        foreach (NodeSpec child in spec.Children)
        {
            NodeSpec? filtered = Filter(child);
            if (filtered != null)
            {
                children.Add(filtered);
            }
        }

        if (!matches && children.Count == 0)
        {
            return null;
        }

        var copy = new NodeSpec(spec.Key, spec.Id, spec.Serial, spec.IsGroup, true) { Label = spec.Label };
        copy.Children.AddRange(children);
        return copy;
    }

    private static void Reconcile(List<ObjectNode> nodes, List<NodeSpec> specs, ObjectNode? parent, bool forceExpand)
    {
        var existing = new Dictionary<string, ObjectNode>(nodes.Count);
        foreach (ObjectNode node in nodes)
        {
            existing.TryAdd(node.Key, node);
        }

        var result = new List<ObjectNode>(specs.Count);
        foreach (NodeSpec spec in specs)
        {
            if (existing.TryGetValue(spec.Key, out ObjectNode? node))
            {
                existing.Remove(spec.Key);
            }
            else
            {
                node = new ObjectNode(spec.Key, spec.Id, spec.Serial, spec.IsGroup, parent) { IsExpanded = spec.Expanded };
            }

            node.Label = spec.Label;
            if (forceExpand)
            {
                node.IsExpanded = true;
            }

            Reconcile(node.Children, spec.Children, node, forceExpand);
            node.HasChildren = node.Children.Count > 0;
            result.Add(node);
        }

        nodes.Clear();
        nodes.AddRange(result);
    }

    private void RebuildRows()
    {
        mFlat.Clear();
        Flatten(mRoots, mFlat);

        int prefix = 0;
        int oldCount = Rows.Count;
        int newCount = mFlat.Count;
        while (prefix < oldCount && prefix < newCount && Rows[prefix] == mFlat[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < oldCount - prefix && suffix < newCount - prefix && Rows[oldCount - 1 - suffix] == mFlat[newCount - 1 - suffix])
        {
            suffix++;
        }

        for (int i = oldCount - suffix - 1; i >= prefix; i--)
        {
            Rows.RemoveAt(i);
        }

        for (int i = prefix; i < newCount - suffix; i++)
        {
            Rows.Insert(i, mFlat[i]);
        }
    }

    private static void Flatten(List<ObjectNode> nodes, List<ObjectNode> output)
    {
        foreach (ObjectNode node in nodes)
        {
            output.Add(node);
            if (node.IsExpanded)
            {
                Flatten(node.Children, output);
            }
        }
    }

    private sealed class NodeSpec
    {
        public NodeSpec(string key, ulong id, long serial, bool isGroup, bool expanded)
        {
            Key = key;
            Id = id;
            Serial = serial;
            IsGroup = isGroup;
            Expanded = expanded;
        }

        public string Key { get; }

        public ulong Id { get; }

        public long Serial { get; }

        public bool IsGroup { get; }

        public bool Expanded { get; }

        public string Label { get; set; } = "";

        public List<NodeSpec> Children { get; } = new();
    }
}
