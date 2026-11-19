using System.Buffers.Binary;
using System.Numerics;

using Peekaboom.Protocol;

namespace Peekaboom.Model.Physics;

public readonly record struct Pose(Quaternion Rotation, Vector3 Position)
{
    public static readonly Pose Identity = new(Quaternion.Identity, Vector3.Zero);

    public Matrix4x4 ToMatrix()
    {
        Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(Rotation);
        matrix.Translation = Position;
        return matrix;
    }
}

public static class PhysicsValues
{
    public static bool IsClass(ClassDef? cls, string name)
    {
        for (ClassDef? current = cls; current != null; current = current.Parent)
        {
            if (current.Name.Name == name && current.Name.Namespace == LegacyFormat.BASE_NAMESPACE)
            {
                return true;
            }
        }

        return false;
    }

    public static Quaternion SafeQuaternion(Quaternion q)
    {
        float length = q.Length();
        if (length < 1e-6f || float.IsNaN(length))
        {
            return Quaternion.Identity;
        }

        return Quaternion.Normalize(q);
    }

    public static bool TryGetPose(ObjectInstance instance, string name, out Pose pose)
    {
        pose = Pose.Identity;
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || value.Type.BaseType != BaseType.Transform)
        {
            return false;
        }

        var q = new Quaternion(value.ReadF32(0, 0), value.ReadF32(0, 4), value.ReadF32(0, 8), value.ReadF32(0, 12));
        var p = new Vector3(value.ReadF32(0, 16), value.ReadF32(0, 20), value.ReadF32(0, 24));
        pose = new Pose(SafeQuaternion(q), p);
        return true;
    }

    public static bool TryGetVector3(ObjectInstance instance, string name, out Vector3 vector)
    {
        vector = Vector3.Zero;
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || value.Type.BaseType != BaseType.Vec3)
        {
            return false;
        }

        vector = ReadVector3(value, 0);
        return true;
    }

    public static bool TryGetQuaternion(ObjectInstance instance, string name, out Quaternion quaternion)
    {
        quaternion = Quaternion.Identity;
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || value.Type.BaseType != BaseType.Quat)
        {
            return false;
        }

        quaternion = SafeQuaternion(new Quaternion(value.ReadF32(0, 0), value.ReadF32(0, 4), value.ReadF32(0, 8), value.ReadF32(0, 12)));
        return true;
    }

    public static Vector3 ReadVector3(PropertyValue value, int item)
    {
        return new Vector3(value.ReadF32(item, 0), value.ReadF32(item, 4), value.ReadF32(item, 8));
    }

    public static float GetFloat(ObjectInstance instance, string name, float fallback)
    {
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0)
        {
            return fallback;
        }

        if (value.Type.BaseType == BaseType.F32)
        {
            return value.ReadF32(0, 0);
        }

        if (value.Type.BaseType == BaseType.F64)
        {
            return (float)BinaryPrimitives.ReadDoubleLittleEndian(value.Item(0));
        }

        return fallback;
    }

    public static bool GetBool(ObjectInstance instance, string name, bool fallback)
    {
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || !IsInteger(value.Type.BaseType))
        {
            return fallback;
        }

        return ReadInteger(value, 0, 0, value.Type.BaseType) != 0;
    }

    public static ulong GetInteger(ObjectInstance instance, string name, ulong fallback)
    {
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || !IsInteger(value.Type.BaseType))
        {
            return fallback;
        }

        return ReadInteger(value, 0, 0, value.Type.BaseType);
    }

    public static string GetString(ObjectInstance instance, string name)
    {
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || !value.Type.IsString)
        {
            return "";
        }

        return value.GetString(0, 0);
    }

    public static ulong GetRef(ObjectInstance instance, string name)
    {
        PropertyValue? value = instance.GetValue(name);
        if (value == null || value.Count == 0 || !value.Type.IsObjectRef)
        {
            IReadOnlyList<ulong> refs = instance.GetRefs(name);
            return refs.Count > 0 ? refs[0] : 0;
        }

        return value.ReadPointer(0, 0);
    }

    public static ulong FlagValue(ObjectInstance instance, string property, string flag, ulong fallback)
    {
        PropertyDef? definition = instance.Class.FindProperty(property);
        if (definition != null)
        {
            foreach (NamedValue named in definition.Values)
            {
                if (named.Name == flag)
                {
                    return named.Value;
                }
            }
        }

        return fallback;
    }

    public static bool IsInteger(BaseType type)
    {
        return type >= BaseType.I8 && type <= BaseType.U64;
    }

    public static ulong ReadInteger(PropertyValue value, int item, int offset, BaseType type)
    {
        ReadOnlySpan<byte> data = value.Item(item).Slice(offset);
        switch (type)
        {
            case BaseType.I8:
                return (ulong)(long)(sbyte)data[0];
            case BaseType.U8:
            case BaseType.Bool:
                return data[0];
            case BaseType.I16:
                return (ulong)(long)BinaryPrimitives.ReadInt16LittleEndian(data);
            case BaseType.U16:
                return BinaryPrimitives.ReadUInt16LittleEndian(data);
            case BaseType.I32:
                return (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(data);
            case BaseType.U32:
                return BinaryPrimitives.ReadUInt32LittleEndian(data);
            case BaseType.I64:
            case BaseType.U64:
                return BinaryPrimitives.ReadUInt64LittleEndian(data);
            default:
                return 0;
        }
    }

    public static bool TryFieldOffset(ClassDef type, string name, out int offset, out BaseType baseType)
    {
        PropertyDef? property = type.FindProperty(name);
        if (property == null || property.IsArray)
        {
            offset = 0;
            baseType = BaseType.None;
            return false;
        }

        offset = property.Offset;
        baseType = property.DataType.BaseType;
        return true;
    }
}
