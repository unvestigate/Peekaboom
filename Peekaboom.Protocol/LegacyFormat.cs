namespace Peekaboom.Protocol;

public static class LegacyFormat
{
    public const uint MAGIC = 0x31E5E882;
    public const uint MAGIC_SWAPPED = 0x82E8E531;
    public const uint STREAM_VERSION = 1;
    public const uint FLAG_64BIT_POINTERS = 1;

    public const int INIT_SIZE = 28;
    public const int GROUP_HEADER_SIZE = 24;
    public const uint MAX_GROUP_SIZE = 1u << 30;

    public const string BASE_NAMESPACE = "physx3";
    public const string INTERNAL_NAMESPACE = "physx3_debugger_internal";
    public const string DEBUGGER_NAMESPACE = "_debugger_";

    public const string FRAME_SECTION = "frame";

    public const string SCENE_CLASS = "PxScene";
    public const string NAME_PROPERTY = "Name";

    public const string PROFILE_ZONE_CLASS = "PxProfileZone";
    public const string MEMORY_BUFFER_CLASS = "PxProfileMemoryEventBuffer";
    public const string USER_RENDERER_CLASS = "PvdUserRenderer";
    public const string EVENTS_PROPERTY = "events";

    public const string SEMANTIC_ENUMERATION = "Enumeration Value";
    public const string SEMANTIC_BITFLAG = "Bitflag";

    public static void CreateBaseTypes(MetadataDb db, int pointerSize)
    {
        db.CreateBaseType(INTERNAL_NAMESPACE, "ArrayData", BaseType.ArrayData, pointerSize * 3, pointerSize);

        db.CreateBaseType(BASE_NAMESPACE, "PvdI8", BaseType.I8, 1, 1);
        db.CreateBaseType(BASE_NAMESPACE, "PvdU8", BaseType.U8, 1, 1);
        db.CreateBaseType(BASE_NAMESPACE, "PvdBool", BaseType.Bool, 1, 1);
        db.CreateBaseType(BASE_NAMESPACE, "PvdI16", BaseType.I16, 2, 2);
        db.CreateBaseType(BASE_NAMESPACE, "PvdU16", BaseType.U16, 2, 2);
        db.CreateBaseType(BASE_NAMESPACE, "PvdI32", BaseType.I32, 4, 4);
        db.CreateBaseType(BASE_NAMESPACE, "PvdU32", BaseType.U32, 4, 4);
        db.CreateBaseType(BASE_NAMESPACE, "PvdI64", BaseType.I64, 8, 8);
        db.CreateBaseType(BASE_NAMESPACE, "PvdU64", BaseType.U64, 8, 8);
        db.CreateBaseType(BASE_NAMESPACE, "PvdF32", BaseType.F32, 4, 4);
        db.CreateBaseType(BASE_NAMESPACE, "PvdF64", BaseType.F64, 8, 8);

        db.CreateBaseType(BASE_NAMESPACE, "String", BaseType.String, pointerSize, pointerSize).AddPtrOffset(PtrOffsetKind.String, 0);
        db.CreateBaseType(BASE_NAMESPACE, "VoidPtr", BaseType.VoidPtr, pointerSize, pointerSize).AddPtrOffset(PtrOffsetKind.VoidPtr, 0);
        db.CreateBaseType(BASE_NAMESPACE, "StringHandle", BaseType.StringHandle, pointerSize, pointerSize).AddPtrOffset(PtrOffsetKind.String, 0);
        db.CreateBaseType(BASE_NAMESPACE, "ObjectRef", BaseType.ObjectRef, pointerSize, pointerSize).AddPtrOffset(PtrOffsetKind.VoidPtr, 0);

        ClassDef u8 = db.FindBaseType(BaseType.U8);
        ClassDef u32 = db.FindBaseType(BaseType.U32);
        ClassDef f32 = db.FindBaseType(BaseType.F32);

        ClassDef color = db.CreateStructBaseType(BASE_NAMESPACE, "PvdColor", BaseType.Color);
        db.AddBaseProperty(color, "r", u8);
        db.AddBaseProperty(color, "g", u8);
        db.AddBaseProperty(color, "b", u8);
        db.AddBaseProperty(color, "a", u8);

        ClassDef vec2 = db.CreateStructBaseType(BASE_NAMESPACE, "PxVec2", BaseType.Vec2);
        db.AddBaseProperty(vec2, "x", f32);
        db.AddBaseProperty(vec2, "y", f32);

        ClassDef vec3 = db.CreateStructBaseType(BASE_NAMESPACE, "PxVec3", BaseType.Vec3);
        db.AddBaseProperty(vec3, "x", f32);
        db.AddBaseProperty(vec3, "y", f32);
        db.AddBaseProperty(vec3, "z", f32);

        ClassDef vec4 = db.CreateStructBaseType(BASE_NAMESPACE, "PxVec4", BaseType.Vec4);
        db.AddBaseProperty(vec4, "x", f32);
        db.AddBaseProperty(vec4, "y", f32);
        db.AddBaseProperty(vec4, "z", f32);
        db.AddBaseProperty(vec4, "w", f32);

        ClassDef quat = db.CreateStructBaseType(BASE_NAMESPACE, "PxQuat", BaseType.Quat);
        db.AddBaseProperty(quat, "x", f32);
        db.AddBaseProperty(quat, "y", f32);
        db.AddBaseProperty(quat, "z", f32);
        db.AddBaseProperty(quat, "w", f32);

        ClassDef bounds = db.CreateStructBaseType(BASE_NAMESPACE, "PxBounds3", BaseType.Bounds3);
        db.AddBaseProperty(bounds, "minimum", vec3);
        db.AddBaseProperty(bounds, "maximum", vec3);

        ClassDef transform = db.CreateStructBaseType(BASE_NAMESPACE, "PxTransform", BaseType.Transform);
        db.AddBaseProperty(transform, "q", quat);
        db.AddBaseProperty(transform, "p", vec3);

        ClassDef mat33 = db.CreateStructBaseType(BASE_NAMESPACE, "PxMat33", BaseType.Mat33);
        db.AddBaseProperty(mat33, "column0", vec3);
        db.AddBaseProperty(mat33, "column1", vec3);
        db.AddBaseProperty(mat33, "column2", vec3);

        ClassDef mat44 = db.CreateStructBaseType(BASE_NAMESPACE, "PxMat44", BaseType.Mat44);
        db.AddBaseProperty(mat44, "column0", vec4);
        db.AddBaseProperty(mat44, "column1", vec4);
        db.AddBaseProperty(mat44, "column2", vec4);
        db.AddBaseProperty(mat44, "column3", vec4);

        ClassDef array4 = db.CreateStructBaseType(BASE_NAMESPACE, "U32Array4", BaseType.U32Array4);
        db.AddBaseProperty(array4, "d0", u32);
        db.AddBaseProperty(array4, "d1", u32);
        db.AddBaseProperty(array4, "d2", u32);
        db.AddBaseProperty(array4, "d3", u32);

        db.LockAll();
    }

    public static bool IsScene(ClassDef? cls)
    {
        return cls != null && cls.Name.Namespace == BASE_NAMESPACE && cls.Name.Name == SCENE_CLASS;
    }

    public static bool IsDroppedDebuggerClass(ClassDef? cls)
    {
        if (cls == null || cls.Name.Namespace != DEBUGGER_NAMESPACE)
        {
            return false;
        }

        return cls.Name.Name == PROFILE_ZONE_CLASS || cls.Name.Name == MEMORY_BUFFER_CLASS;
    }
}
