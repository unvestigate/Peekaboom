namespace Peekaboom.Protocol;

public sealed class StreamSession
{
    private const int MAX_DISTINCT_WARNINGS = 2000;

    private readonly StreamSink mSink;
    private readonly Dictionary<ulong, ClassDef> mInstances = new();
    private readonly Dictionary<ulong, StreamState> mStreams = new();
    private readonly HashSet<string> mWarned = new();
    private readonly bool mIsReplay;
    private MetadataDb? mMetadata;
    private bool mWarningLimitReported;

    public StreamSession(StreamSink sink)
    {
        mSink = sink;
    }

    public StreamSession(StreamSink sink, StreamSession source)
    {
        mSink = sink;
        mIsReplay = true;
        mMetadata = source.Metadata;
        Strings = source.Strings;
        Initialization = source.Initialization;
    }

    public bool IsReplay => mIsReplay;

    public bool IsQuiescent
    {
        get
        {
            foreach (StreamState state in mStreams.Values)
            {
                if (state.InSet || state.InGroup)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void ClearInstances()
    {
        mInstances.Clear();
    }

    public void SetInstance(ulong instance, ClassDef cls)
    {
        mInstances[instance] = cls;
    }

    public StreamInitialization? Initialization { get; private set; }

    public MetadataDb Metadata => mMetadata ?? throw new InvalidOperationException("No stream has started.");

    public bool HasStarted => mMetadata != null;

    public StringTable Strings { get; private set; } = new();

    public ProtocolStats Stats { get; } = new();

    public PvdEvent? CurrentEvent { get; private set; }

    public IReadOnlyDictionary<ulong, ClassDef> Instances => mInstances;

    public int PointerSize => mMetadata?.PointerSize ?? 8;

    public void Begin(StreamInitialization initialization)
    {
        if (initialization.Version != LegacyFormat.STREAM_VERSION)
        {
            Warn("Stream version " + initialization.Version + " is not the expected " + LegacyFormat.STREAM_VERSION + ".");
        }

        Initialization = initialization;
        mMetadata = new MetadataDb(initialization.PointerSize);
        Strings = new StringTable();
        mInstances.Clear();
        mStreams.Clear();
        CurrentEvent = null;
        Stats.Sessions++;
        mSink.OnStreamStart(this, initialization);
    }

    public void ReportDecodeError(string message)
    {
        Stats.DecodeErrors++;
        Warn(message);
    }

    public void Warn(string message)
    {
        Stats.Warnings++;

        if (mWarned.Count >= MAX_DISTINCT_WARNINGS)
        {
            if (!mWarningLimitReported)
            {
                mWarningLimitReported = true;
                mSink.OnWarning("Too many distinct warnings, further ones are only counted.");
            }

            return;
        }

        if (mWarned.Add(message))
        {
            mSink.OnWarning(message);
        }
    }

    public string Resolve(uint handle)
    {
        if (!Strings.TryGet(handle, out string value))
        {
            Warn("String handle " + handle + " was never defined.");
        }

        return value;
    }

    public NamespacedName Resolve(NameHandles handles)
    {
        return new NamespacedName(Resolve(handles.Namespace), Resolve(handles.Name));
    }

    public ClassDef? FindInstanceClass(ulong instance)
    {
        mInstances.TryGetValue(instance, out ClassDef? cls);
        return cls;
    }

    public void Process(PvdEvent evt)
    {
        if (mMetadata == null)
        {
            throw new ProtocolException("Event " + evt.Type + " arrived before the stream initialization.");
        }

        CurrentEvent = evt;
        Stats.EventCounts[(int)evt.Type]++;
        Stats.StreamIds.Add(evt.StreamId);
        if (Stats.FirstTimestamp == 0 || evt.Timestamp < Stats.FirstTimestamp)
        {
            Stats.FirstTimestamp = evt.Timestamp;
        }

        if (evt.Timestamp > Stats.LastTimestamp)
        {
            Stats.LastTimestamp = evt.Timestamp;
        }

        if (mIsReplay && IsMetadataEvent(evt.Type))
        {
            return;
        }

        switch (evt)
        {
            case StringHandleEvent e:
                Strings.Set(e.Handle, e.Value);
                break;
            case CreateClassEvent e:
                mMetadata.GetOrCreateClass(Resolve(e.Class), out _);
                break;
            case DeriveClassEvent e:
                WarnIfError(mMetadata.DeriveClass(Resolve(e.Parent), Resolve(e.Child)));
                break;
            case CreatePropertyEvent e:
                CreateProperty(e);
                break;
            case CreatePropertyMessageEvent e:
                CreatePropertyMessage(e);
                break;
            case CreateInstanceEvent e:
                CreateInstance(e);
                break;
            case DestroyInstanceEvent e:
                DestroyInstance(e);
                break;
            case SetPropertyValueEvent e:
                SetPropertyValue(e);
                break;
            case BeginSetPropertyValueEvent e:
                BeginSetPropertyValue(e);
                break;
            case AppendPropertyValueDataEvent e:
                AppendPropertyValueData(e);
                break;
            case EndSetPropertyValueEvent:
                EndSetPropertyValue(evt.StreamId);
                break;
            case SetPropertyMessageEvent e:
                SetPropertyMessage(e);
                break;
            case BeginPropertyMessageGroupEvent e:
                BeginPropertyMessageGroup(e);
                break;
            case SendPropertyMessageFromGroupEvent e:
                SendPropertyMessageFromGroup(e);
                break;
            case EndPropertyMessageGroupEvent:
                EndPropertyMessageGroup(evt.StreamId);
                break;
            case ObjectRefEvent e:
                mSink.OnObjectRef(e.Instance, FindInstanceClass(e.Instance), Resolve(e.Property), e.ObjectRef, e.IsPushBack);
                break;
            case SectionEvent e:
                mSink.OnSection(e.SectionId, Resolve(e.Name), e.SectionTimestamp, e.IsBegin);
                break;
            case SetIsTopLevelEvent e:
                mSink.OnSetIsTopLevel(e.Instance, e.IsTopLevel);
                break;
            case SetCameraEvent e:
                mSink.OnSetCamera(e.Name, e.Position, e.Up, e.Target);
                break;
            case ErrorMessageEvent e:
                mSink.OnErrorMessage(e.Code, e.Message, e.File, e.Line);
                break;
            case OriginShiftEvent e:
                mSink.OnOriginShift(e.Scene, e.Shift);
                break;
        }
    }

    private static bool IsMetadataEvent(EventType type)
    {
        return type == EventType.StringHandle || type == EventType.CreateClass || type == EventType.DeriveClass
            || type == EventType.CreateProperty || type == EventType.CreatePropertyMessage;
    }

    private void WarnIfError(string? error)
    {
        if (error != null)
        {
            Warn(error);
        }
    }

    private StreamState GetStream(ulong streamId)
    {
        if (!mStreams.TryGetValue(streamId, out StreamState? state))
        {
            state = new StreamState();
            mStreams[streamId] = state;
        }

        return state;
    }

    private void CreateProperty(CreatePropertyEvent e)
    {
        ClassDef cls = Metadata.GetOrCreateClass(Resolve(e.Class), out bool created);
        if (created)
        {
            Warn("Property defined on undeclared class " + cls.Name + ".");
        }

        string name = Resolve(e.Name);
        string semantic = Resolve(e.Semantic);
        NamespacedName dataTypeName = Resolve(e.DataType);
        ClassDef? dataType = Metadata.FindClass(dataTypeName);
        if (dataType == null)
        {
            Warn("Property " + cls.Name + "." + name + " has unknown datatype " + dataTypeName + ".");
            return;
        }

        if (e.PropertyType != 1 && e.PropertyType != 2)
        {
            Warn("Property " + cls.Name + "." + name + " has property type " + e.PropertyType + ".");
        }

        var values = new NamedValue[e.Values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new NamedValue(Resolve(e.Values[i].Name), e.Values[i].Value);
        }

        Metadata.CreateProperty(cls, name, semantic, dataType, e.IsArray, values, out string? error);
        WarnIfError(error);
    }

    private void CreatePropertyMessage(CreatePropertyMessageEvent e)
    {
        var specs = new MessageEntrySpec[e.Entries.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            MessageArg arg = e.Entries[i];
            specs[i] = new MessageEntrySpec(Resolve(arg.Property), Resolve(arg.DataType), (int)arg.Offset, (int)arg.ByteSize);
        }

        Metadata.CreateMessage(Resolve(e.Class), Resolve(e.Message), specs, (int)e.ByteSize, out string? error);
        WarnIfError(error);
    }

    private void CreateInstance(CreateInstanceEvent e)
    {
        ClassDef cls = Metadata.GetOrCreateClass(Resolve(e.Class), out bool created);
        if (created)
        {
            Warn("Instance created of undeclared class " + cls.Name + ".");
        }

        mInstances[e.Instance] = cls;
        mSink.OnCreateInstance(e.Instance, cls);
    }

    private void DestroyInstance(DestroyInstanceEvent e)
    {
        if (mInstances.TryGetValue(e.Instance, out ClassDef? cls))
        {
            mInstances.Remove(e.Instance);
        }
        else
        {
            Warn("DestroyInstance of an unknown instance.");
        }

        mSink.OnDestroyInstance(e.Instance, cls);
    }

    private void SetPropertyValue(SetPropertyValueEvent e)
    {
        ClassDef? instanceClass = FindInstanceClass(e.Instance);
        if (LegacyFormat.IsDroppedDebuggerClass(instanceClass))
        {
            Stats.DroppedBlobs++;
            Stats.DroppedBlobBytes += e.Data.Length;
            return;
        }

        string name = Resolve(e.Property);
        NamespacedName typeName = Resolve(e.IncomingType);
        ClassDef? type = Metadata.FindClass(typeName);
        if (type == null)
        {
            Warn("SetPropertyValue " + name + " has unknown type " + typeName + ".");
            return;
        }

        PropertyValue? value = PropertyValue.Decode(type, e.NumItems, e.Data, PointerSize, out string? error);
        WarnIfError(error);
        if (value == null)
        {
            return;
        }

        value.Origin = e.Offset;
        Deliver(e.Instance, instanceClass, name, value);
    }

    private void BeginSetPropertyValue(BeginSetPropertyValueEvent e)
    {
        StreamState state = GetStream(e.StreamId);
        if (state.InSet)
        {
            Warn("BeginSetPropertyValue inside an open one on the same stream.");
        }

        state.InSet = true;
        state.SetInstance = e.Instance;
        state.SetName = Resolve(e.Property);
        state.SetParts.Clear();

        NamespacedName typeName = Resolve(e.IncomingType);
        state.SetType = Metadata.FindClass(typeName);
        if (state.SetType == null)
        {
            Warn("BeginSetPropertyValue " + state.SetName + " has unknown type " + typeName + ".");
        }
    }

    private void AppendPropertyValueData(AppendPropertyValueDataEvent e)
    {
        StreamState state = GetStream(e.StreamId);
        if (!state.InSet)
        {
            Warn("AppendPropertyValueData without BeginSetPropertyValue.");
            return;
        }

        if (state.SetType == null)
        {
            return;
        }

        PropertyValue? value = PropertyValue.Decode(state.SetType, e.NumItems, e.Data, PointerSize, out string? error);
        WarnIfError(error);
        if (value != null)
        {
            value.Origin = e.Offset;
            state.SetParts.Add(value);
        }
    }

    private void EndSetPropertyValue(ulong streamId)
    {
        StreamState state = GetStream(streamId);
        if (!state.InSet)
        {
            Warn("EndSetPropertyValue without BeginSetPropertyValue.");
            return;
        }

        state.InSet = false;
        ClassDef? type = state.SetType;
        if (type == null)
        {
            return;
        }

        ClassDef? instanceClass = FindInstanceClass(state.SetInstance);
        PropertyValue value = state.SetParts.Count == 0
            ? new PropertyValue(type, 0, ReadOnlyMemory<byte>.Empty, null, PointerSize) { Origin = CurrentEvent?.Offset ?? 0 }
            : PropertyValue.Concat(type, state.SetParts, PointerSize);
        state.SetParts.Clear();

        Deliver(state.SetInstance, instanceClass, state.SetName, value);
    }

    private void SetPropertyMessage(SetPropertyMessageEvent e)
    {
        NamespacedName name = Resolve(e.Message);
        PropertyMessageDef? message = Metadata.FindMessage(name);
        if (message == null)
        {
            Warn("SetPropertyMessage uses undefined message " + name + ".");
            return;
        }

        DeliverMessage(e.Instance, message, e.Data);
    }

    private void BeginPropertyMessageGroup(BeginPropertyMessageGroupEvent e)
    {
        StreamState state = GetStream(e.StreamId);
        if (state.InGroup)
        {
            Warn("BeginPropertyMessageGroup inside an open group on the same stream.");
        }

        state.InGroup = true;
        state.GroupName = Resolve(e.Message);
        state.GroupMessage = Metadata.FindMessage(state.GroupName);
        if (state.GroupMessage == null)
        {
            Warn("Property message group uses undefined message " + state.GroupName + ".");
        }
    }

    private void SendPropertyMessageFromGroup(SendPropertyMessageFromGroupEvent e)
    {
        StreamState state = GetStream(e.StreamId);
        if (!state.InGroup)
        {
            Warn("SendPropertyMessageFromGroup outside a group.");
            return;
        }

        if (state.GroupMessage != null)
        {
            DeliverMessage(e.Instance, state.GroupMessage, e.Data);
        }
    }

    private void EndPropertyMessageGroup(ulong streamId)
    {
        StreamState state = GetStream(streamId);
        if (!state.InGroup)
        {
            Warn("EndPropertyMessageGroup outside a group.");
        }

        state.InGroup = false;
        state.GroupMessage = null;
    }

    private void Deliver(ulong instance, ClassDef? instanceClass, string name, PropertyValue value)
    {
        PropertyDef? property = null;
        if (instanceClass != null)
        {
            property = instanceClass.FindProperty(name);
            if (property == null)
            {
                Warn("Property " + instanceClass.Name + "." + name + " was never declared.");
            }
        }
        else
        {
            Warn("Property set on an unknown instance.");
        }

        Stats.PropertyValues++;
        if (instanceClass != null && instanceClass.Name.Name == LegacyFormat.USER_RENDERER_CLASS)
        {
            Stats.RenderBlobs++;
            Stats.RenderBlobBytes += value.Data.Length;
        }

        mSink.OnSetProperty(instance, instanceClass, name, property, value);
    }

    private void DeliverMessage(ulong instance, PropertyMessageDef message, ReadOnlyMemory<byte> data)
    {
        if (data.Length < message.ByteSize)
        {
            Warn("Property message " + message.Name + " has " + data.Length + " bytes, expected " + message.ByteSize + ".");
            return;
        }

        ReadOnlyMemory<byte> raw = data.Slice(0, message.ByteSize);
        string[]? strings = null;

        if (message.StringCount > 0)
        {
            strings = new string[message.StringCount];
            ReadOnlySpan<byte> tail = data.Span.Slice(message.ByteSize);
            int position = 0;
            for (int i = 0; i < strings.Length; i++)
            {
                int length = tail.Slice(position).IndexOf((byte)0);
                if (length < 0)
                {
                    Warn("Property message " + message.Name + " has an unterminated string.");
                    return;
                }

                strings[i] = System.Text.Encoding.UTF8.GetString(tail.Slice(position, length));
                position += length + 1;
            }
        }
        else if (data.Length > message.ByteSize)
        {
            Warn("Property message " + message.Name + " has " + (data.Length - message.ByteSize) + " extra bytes.");
        }

        ClassDef? instanceClass = FindInstanceClass(instance);
        if (instanceClass == null)
        {
            Warn("Property message sent to an unknown instance.");
        }
        else if (!instanceClass.IsA(message.Class))
        {
            Warn("Property message " + message.Name + " sent to an instance of " + instanceClass.Name + ".");
        }

        Stats.MessageValues += message.Entries.Length;
        mSink.OnSetPropertyMessage(instance, instanceClass, message, raw, strings, PointerSize);
    }

    private sealed class StreamState
    {
        public bool InSet;
        public ulong SetInstance;
        public string SetName = "";
        public ClassDef? SetType;
        public readonly List<PropertyValue> SetParts = new();

        public bool InGroup;
        public NamespacedName GroupName;
        public PropertyMessageDef? GroupMessage;
    }
}
