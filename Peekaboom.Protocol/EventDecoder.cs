namespace Peekaboom.Protocol;

public static class EventDecoder
{
    public static string? DecodeGroup(GroupHeader header, byte[] body, long offset, List<PvdEvent> output)
    {
        var reader = new BodyReader(body, 0, body.Length);

        for (uint i = 0; i < header.NumEvents; i++)
        {
            if (reader.Remaining == 0)
            {
                return "Group at " + offset + " announces " + header.NumEvents + " events but holds " + i + ".";
            }

            byte type = reader.ReadU8();
            if (type == (byte)EventType.Unknown || type >= (byte)EventType.Last)
            {
                return "Unknown event type " + type + " in group at " + offset + ", " + body.Length + " bytes skipped.";
            }

            PvdEvent evt;
            try
            {
                evt = DecodeEvent((EventType)type, ref reader);
            }
            catch (ProtocolException exception)
            {
                return (EventType)type + " at " + offset + ". " + exception.Message;
            }

            evt.StreamId = header.StreamId;
            evt.Timestamp = header.Timestamp;
            evt.Offset = offset;
            output.Add(evt);
        }

        if (reader.Remaining != 0)
        {
            return "Group at " + offset + " has " + reader.Remaining + " of " + body.Length + " bytes left after its events.";
        }

        return null;
    }

    private static PvdEvent DecodeEvent(EventType type, ref BodyReader reader)
    {
        switch (type)
        {
            case EventType.StringHandle:
            {
                string value = reader.ReadWireString();
                uint handle = reader.ReadU32();
                return new StringHandleEvent { Value = value, Handle = handle };
            }

            case EventType.CreateClass:
                return new CreateClassEvent { Class = reader.ReadNames() };

            case EventType.DeriveClass:
            {
                NameHandles parent = reader.ReadNames();
                NameHandles child = reader.ReadNames();
                return new DeriveClassEvent { Parent = parent, Child = child };
            }

            case EventType.CreateProperty:
            {
                NameHandles cls = reader.ReadNames();
                uint name = reader.ReadU32();
                uint semantic = reader.ReadU32();
                NameHandles dataType = reader.ReadNames();
                byte propertyType = reader.ReadU8();
                uint count = reader.ReadU32();
                CheckCount(count, 8, ref reader);
                var values = new NameHandleValue[count];
                for (int i = 0; i < values.Length; i++)
                {
                    uint valueName = reader.ReadU32();
                    uint value = reader.ReadU32();
                    values[i] = new NameHandleValue(valueName, value);
                }

                return new CreatePropertyEvent
                {
                    Class = cls,
                    Name = name,
                    Semantic = semantic,
                    DataType = dataType,
                    PropertyType = propertyType,
                    Values = values,
                };
            }

            case EventType.CreatePropertyMessage:
            {
                NameHandles cls = reader.ReadNames();
                NameHandles message = reader.ReadNames();
                uint count = reader.ReadU32();
                CheckCount(count, 20, ref reader);
                var entries = new MessageArg[count];
                for (int i = 0; i < entries.Length; i++)
                {
                    uint property = reader.ReadU32();
                    NameHandles dataType = reader.ReadNames();
                    uint entryOffset = reader.ReadU32();
                    uint byteSize = reader.ReadU32();
                    entries[i] = new MessageArg(property, dataType, entryOffset, byteSize);
                }

                uint messageSize = reader.ReadU32();
                return new CreatePropertyMessageEvent { Class = cls, Message = message, Entries = entries, ByteSize = messageSize };
            }

            case EventType.CreateInstance:
            {
                NameHandles cls = reader.ReadNames();
                ulong instance = reader.ReadU64();
                return new CreateInstanceEvent { Class = cls, Instance = instance };
            }

            case EventType.SetPropertyValue:
            {
                ulong instance = reader.ReadU64();
                uint property = reader.ReadU32();
                NameHandles incoming = reader.ReadNames();
                uint numItems = reader.ReadU32();
                ReadOnlyMemory<byte> data = reader.ReadBlob();
                return new SetPropertyValueEvent
                {
                    Instance = instance,
                    Property = property,
                    IncomingType = incoming,
                    NumItems = numItems,
                    Data = data,
                };
            }

            case EventType.BeginSetPropertyValue:
            {
                ulong instance = reader.ReadU64();
                uint property = reader.ReadU32();
                NameHandles incoming = reader.ReadNames();
                return new BeginSetPropertyValueEvent { Instance = instance, Property = property, IncomingType = incoming };
            }

            case EventType.AppendPropertyValueData:
            {
                ReadOnlyMemory<byte> data = reader.ReadBlob();
                uint numItems = reader.ReadU32();
                return new AppendPropertyValueDataEvent { Data = data, NumItems = numItems };
            }

            case EventType.EndSetPropertyValue:
                return new EndSetPropertyValueEvent();

            case EventType.SetPropertyMessage:
            {
                ulong instance = reader.ReadU64();
                NameHandles message = reader.ReadNames();
                ReadOnlyMemory<byte> data = reader.ReadBlob();
                return new SetPropertyMessageEvent { Instance = instance, Message = message, Data = data };
            }

            case EventType.BeginPropertyMessageGroup:
                return new BeginPropertyMessageGroupEvent { Message = reader.ReadNames() };

            case EventType.SendPropertyMessageFromGroup:
            {
                ulong instance = reader.ReadU64();
                ReadOnlyMemory<byte> data = reader.ReadBlob();
                return new SendPropertyMessageFromGroupEvent { Instance = instance, Data = data };
            }

            case EventType.EndPropertyMessageGroup:
                return new EndPropertyMessageGroupEvent();

            case EventType.DestroyInstance:
                return new DestroyInstanceEvent { Instance = reader.ReadU64() };

            case EventType.PushBackObjectRef:
            case EventType.RemoveObjectRef:
            {
                ulong instance = reader.ReadU64();
                uint property = reader.ReadU32();
                ulong objectRef = reader.ReadU64();
                return new ObjectRefEvent(type == EventType.PushBackObjectRef) { Instance = instance, Property = property, ObjectRef = objectRef };
            }

            case EventType.BeginSection:
            case EventType.EndSection:
            {
                ulong sectionId = reader.ReadU64();
                uint name = reader.ReadU32();
                ulong timestamp = reader.ReadU64();
                return new SectionEvent(type == EventType.BeginSection) { SectionId = sectionId, Name = name, SectionTimestamp = timestamp };
            }

            case EventType.SetPickable:
            {
                ulong instance = reader.ReadU64();
                bool pickable = reader.ReadBool();
                return new SetPickableEvent { Instance = instance, Pickable = pickable };
            }

            case EventType.SetColor:
            {
                ulong instance = reader.ReadU64();
                uint rgba = reader.ReadU32();
                return new SetColorEvent { Instance = instance, Rgba = rgba };
            }

            case EventType.SetIsTopLevel:
            {
                ulong instance = reader.ReadU64();
                bool isTopLevel = reader.ReadBool();
                return new SetIsTopLevelEvent { Instance = instance, IsTopLevel = isTopLevel };
            }

            case EventType.SetCamera:
            {
                string name = reader.ReadWireString();
                var position = reader.ReadVec3();
                var up = reader.ReadVec3();
                var target = reader.ReadVec3();
                return new SetCameraEvent { Name = name, Position = position, Up = up, Target = target };
            }

            case EventType.AddProfileZone:
            {
                ulong instance = reader.ReadU64();
                string name = reader.ReadWireString();
                return new AddProfileZoneEvent { Instance = instance, Name = name };
            }

            case EventType.AddProfileZoneEvent:
            {
                ulong zone = reader.ReadU64();
                string name = reader.ReadWireString();
                ushort eventId = reader.ReadU16();
                bool enabled = reader.ReadBool();
                return new AddProfileZoneNameEvent { Zone = zone, Name = name, EventId = eventId, CompileTimeEnabled = enabled };
            }

            case EventType.StreamEnd:
                return new StreamEndEvent { Name = reader.ReadWireString() };

            case EventType.ErrorMessage:
            {
                uint code = reader.ReadU32();
                string message = reader.ReadWireString();
                string file = reader.ReadWireString();
                uint line = reader.ReadU32();
                return new ErrorMessageEvent { Code = code, Message = message, File = file, Line = line };
            }

            case EventType.OriginShift:
            {
                ulong scene = reader.ReadU64();
                var shift = reader.ReadVec3();
                return new OriginShiftEvent { Scene = scene, Shift = shift };
            }

            default:
                throw new ProtocolException("No decoder for event type " + type + ".");
        }
    }

    private static void CheckCount(uint count, int itemSize, ref BodyReader reader)
    {
        if ((ulong)count * (ulong)itemSize > (ulong)reader.Remaining)
        {
            throw new ProtocolException("Record count " + count + " exceeds the event body.");
        }
    }
}
