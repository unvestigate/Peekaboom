using System.Buffers.Binary;
using System.Numerics;

using Peekaboom.Protocol;

namespace Peekaboom.Model.Physics;

public enum OverlayKind
{
    Contacts,
    Joints,
    SceneQueries,
    DebugRender,
    Bounds,
    Axes,
    Velocities,
}

public struct OverlayLine
{
    public Vector3 A;
    public Vector3 B;
    public uint Color;
    public OverlayKind Kind;
    public ulong Owner0;
    public ulong Owner1;
    public ulong Owner2;
}

public static class OverlayBuilder
{
    public const float MARK_SIZE = 0.05f;
    public const float CONTACT_NORMAL_LENGTH = 0.25f;
    public const float JOINT_AXIS_LENGTH = 0.2f;
    public const float MAX_RAY_LENGTH = 10000.0f;

    public static readonly uint ContactColor = Rgba(255, 220, 60);
    public static readonly uint ContactNormalColor = Rgba(255, 120, 60);
    public static readonly uint JointLinkColor = Rgba(230, 230, 230);
    public static readonly uint RayColor = Rgba(220, 70, 70);
    public static readonly uint RayHitColor = Rgba(80, 220, 120);
    public static readonly uint SweepColor = Rgba(255, 160, 40);
    public static readonly uint OverlapColor = Rgba(190, 110, 255);
    public static readonly uint HitColor = Rgba(255, 255, 255);
    public static readonly uint AxisX = Rgba(230, 70, 70);
    public static readonly uint AxisY = Rgba(70, 220, 70);
    public static readonly uint AxisZ = Rgba(80, 120, 255);

    private const byte RENDER_SET_INSTANCE = 1;
    private const byte RENDER_POINTS = 2;
    private const byte RENDER_LINES = 3;
    private const byte RENDER_TRIANGLES = 4;
    private const byte RENDER_JOINT_FRAMES = 5;
    private const byte RENDER_LINEAR_LIMIT = 6;
    private const byte RENDER_ANGULAR_LIMIT = 7;
    private const byte RENDER_LIMIT_CONE = 8;
    private const byte RENDER_DOUBLE_CONE = 9;
    private const byte RENDER_TEXT = 10;
    private const byte RENDER_DEBUG = 11;
    private const int TRANSFORM_SIZE = 28;

    public static uint Rgba(byte r, byte g, byte b)
    {
        return r | ((uint)g << 8) | ((uint)b << 16) | (0xFFu << 24);
    }

    public static void Build(ObjectDb db, SceneSnapshot snapshot, List<OverlayLine> output)
    {
        ObjectInstance? scene = snapshot.Scene;
        if (scene == null)
        {
            return;
        }

        var shapeToActor = new Dictionary<ulong, ulong>();
        foreach (ActorInfo actor in snapshot.Actors)
        {
            foreach (ShapeInfo shape in actor.Shapes)
            {
                shapeToActor[shape.Instance.Id] = actor.Instance.Id;
            }
        }

        AddContacts(scene, shapeToActor, output);
        AddJoints(db, scene, output);
        AddQueries(scene, PhysicsNames.SCENE_QUERIES, output);
        AddQueries(scene, PhysicsNames.BATCHED_QUERIES, output);
        AddDebugRender(db, scene, output);
    }

    public static void AddLine(List<OverlayLine> output, OverlayKind kind, Vector3 a, Vector3 b, uint color, ulong owner0, ulong owner1, ulong owner2)
    {
        output.Add(new OverlayLine { A = a, B = b, Color = color, Kind = kind, Owner0 = owner0, Owner1 = owner1, Owner2 = owner2 });
    }

    public static void AddCross(List<OverlayLine> output, OverlayKind kind, Vector3 p, float size, uint color, ulong owner0, ulong owner1, ulong owner2)
    {
        AddLine(output, kind, p - new Vector3(size, 0, 0), p + new Vector3(size, 0, 0), color, owner0, owner1, owner2);
        AddLine(output, kind, p - new Vector3(0, size, 0), p + new Vector3(0, size, 0), color, owner0, owner1, owner2);
        AddLine(output, kind, p - new Vector3(0, 0, size), p + new Vector3(0, 0, size), color, owner0, owner1, owner2);
    }

    public static void AddAxes(List<OverlayLine> output, OverlayKind kind, Matrix4x4 frame, float length, ulong owner0, ulong owner1, ulong owner2)
    {
        Vector3 origin = frame.Translation;
        AddLine(output, kind, origin, Vector3.Transform(new Vector3(length, 0, 0), frame), AxisX, owner0, owner1, owner2);
        AddLine(output, kind, origin, Vector3.Transform(new Vector3(0, length, 0), frame), AxisY, owner0, owner1, owner2);
        AddLine(output, kind, origin, Vector3.Transform(new Vector3(0, 0, length), frame), AxisZ, owner0, owner1, owner2);
    }

    private static Vector3 ReadVector3(PropertyValue value, int item, int offset)
    {
        return new Vector3(value.ReadF32(item, offset), value.ReadF32(item, offset + 4), value.ReadF32(item, offset + 8));
    }

    private static Pose ReadPose(PropertyValue value, int item, int offset)
    {
        var q = new Quaternion(value.ReadF32(item, offset), value.ReadF32(item, offset + 4), value.ReadF32(item, offset + 8), value.ReadF32(item, offset + 12));
        return new Pose(PhysicsValues.SafeQuaternion(q), ReadVector3(value, item, offset + 16));
    }

    private static void AddContacts(ObjectInstance scene, Dictionary<ulong, ulong> shapeToActor, List<OverlayLine> output)
    {
        PropertyValue? contacts = scene.GetValue(PhysicsNames.CONTACTS);
        if (contacts == null || contacts.Count == 0)
        {
            return;
        }

        ClassDef type = contacts.Type;
        if (!PhysicsValues.TryFieldOffset(type, PhysicsNames.CONTACT_POINT, out int pointOffset, out _)
            || !PhysicsValues.TryFieldOffset(type, PhysicsNames.CONTACT_AXIS, out int axisOffset, out _))
        {
            return;
        }

        bool hasShapes = PhysicsValues.TryFieldOffset(type, PhysicsNames.CONTACT_SHAPE0, out int shape0Offset, out _)
            & PhysicsValues.TryFieldOffset(type, PhysicsNames.CONTACT_SHAPE1, out int shape1Offset, out _);

        for (int i = 0; i < contacts.Count; i++)
        {
            Vector3 point = ReadVector3(contacts, i, pointOffset);
            Vector3 axis = ReadVector3(contacts, i, axisOffset);
            ulong shape0 = hasShapes ? contacts.ReadPointer(i, shape0Offset) : 0;
            ulong shape1 = hasShapes ? contacts.ReadPointer(i, shape1Offset) : 0;
            shapeToActor.TryGetValue(shape0, out ulong actor0);
            shapeToActor.TryGetValue(shape1, out ulong actor1);

            AddCross(output, OverlayKind.Contacts, point, MARK_SIZE, ContactColor, actor0, actor1, shape0 != 0 ? shape0 : shape1);
            AddLine(output, OverlayKind.Contacts, point, point + axis * CONTACT_NORMAL_LENGTH, ContactNormalColor, actor0, actor1, shape1);
        }
    }

    private static Matrix4x4 ActorFrame(ObjectDb db, ulong actorId)
    {
        ObjectInstance? actor = actorId != 0 ? db.Find(actorId) : null;
        if (actor != null && PhysicsValues.TryGetPose(actor, PhysicsNames.GLOBAL_POSE, out Pose pose))
        {
            return pose.ToMatrix();
        }

        return Matrix4x4.Identity;
    }

    private static void AddJoints(ObjectDb db, ObjectInstance scene, List<OverlayLine> output)
    {
        foreach (ulong jointId in scene.GetRefs(PhysicsNames.JOINTS))
        {
            ObjectInstance? joint = db.Find(jointId);
            if (joint == null)
            {
                continue;
            }

            ulong actor0 = PhysicsValues.GetRef(joint, PhysicsNames.JOINT_ACTOR0);
            ulong actor1 = PhysicsValues.GetRef(joint, PhysicsNames.JOINT_ACTOR1);
            PhysicsValues.TryGetPose(joint, PhysicsNames.JOINT_LOCAL_POSE0, out Pose local0);
            PhysicsValues.TryGetPose(joint, PhysicsNames.JOINT_LOCAL_POSE1, out Pose local1);

            Matrix4x4 frame0 = local0.ToMatrix() * ActorFrame(db, actor0);
            Matrix4x4 frame1 = local1.ToMatrix() * ActorFrame(db, actor1);

            AddAxes(output, OverlayKind.Joints, frame0, JOINT_AXIS_LENGTH, actor0, actor1, jointId);
            AddAxes(output, OverlayKind.Joints, frame1, JOINT_AXIS_LENGTH * 0.6f, actor0, actor1, jointId);
            AddLine(output, OverlayKind.Joints, frame0.Translation, frame1.Translation, JointLinkColor, actor0, actor1, jointId);
        }
    }

    private static void AddQueries(ObjectInstance scene, string prefix, List<OverlayLine> output)
    {
        PropertyValue? hits = scene.GetValue(prefix + PhysicsNames.QUERY_HITS);
        PropertyValue? poses = scene.GetValue(prefix + PhysicsNames.QUERY_POSES);

        PropertyValue? raycasts = scene.GetValue(prefix + PhysicsNames.QUERY_RAYCASTS);
        if (raycasts != null && raycasts.Count > 0)
        {
            ClassDef type = raycasts.Type;
            if (PhysicsValues.TryFieldOffset(type, PhysicsNames.QUERY_ORIGIN, out int originOffset, out _)
                && PhysicsValues.TryFieldOffset(type, PhysicsNames.QUERY_DIRECTION, out int directionOffset, out _)
                && PhysicsValues.TryFieldOffset(type, PhysicsNames.QUERY_DISTANCE, out int distanceOffset, out _))
            {
                for (int i = 0; i < raycasts.Count; i++)
                {
                    Vector3 origin = ReadVector3(raycasts, i, originOffset);
                    Vector3 direction = ReadVector3(raycasts, i, directionOffset);
                    float distance = Math.Min(raycasts.ReadF32(i, distanceOffset), MAX_RAY_LENGTH);
                    int hitCount = AddHits(raycasts, i, hits, output);
                    AddLine(output, OverlayKind.SceneQueries, origin, origin + direction * distance, hitCount > 0 ? RayHitColor : RayColor, 0, 0, 0);
                }
            }
        }

        PropertyValue? sweeps = scene.GetValue(prefix + PhysicsNames.QUERY_SWEEPS);
        if (sweeps != null && sweeps.Count > 0 && poses != null && poses.Type.BaseType == BaseType.Transform)
        {
            ClassDef type = sweeps.Type;
            if (PhysicsValues.TryFieldOffset(type, PhysicsNames.QUERY_DIRECTION, out int directionOffset, out _)
                && PhysicsValues.TryFieldOffset(type, PhysicsNames.QUERY_DISTANCE, out int distanceOffset, out _)
                && PhysicsValues.TryFieldOffset(type, PhysicsNames.QUERY_POSE_BASE, out int poseOffset, out BaseType poseType))
            {
                for (int i = 0; i < sweeps.Count; i++)
                {
                    int poseIndex = (int)PhysicsValues.ReadInteger(sweeps, i, poseOffset, poseType);
                    if (poseIndex < 0 || poseIndex >= poses.Count)
                    {
                        continue;
                    }

                    Vector3 start = ReadPose(poses, poseIndex, 0).Position;
                    Vector3 direction = ReadVector3(sweeps, i, directionOffset);
                    float distance = Math.Min(sweeps.ReadF32(i, distanceOffset), MAX_RAY_LENGTH);
                    AddHits(sweeps, i, hits, output);
                    AddCross(output, OverlayKind.SceneQueries, start, MARK_SIZE * 2.0f, SweepColor, 0, 0, 0);
                    AddLine(output, OverlayKind.SceneQueries, start, start + direction * distance, SweepColor, 0, 0, 0);
                }
            }
        }

        PropertyValue? overlaps = scene.GetValue(prefix + PhysicsNames.QUERY_OVERLAPS);
        if (overlaps != null && overlaps.Count > 0
            && PhysicsValues.TryFieldOffset(overlaps.Type, PhysicsNames.QUERY_POSE, out int overlapPoseOffset, out BaseType overlapPoseType)
            && overlapPoseType == BaseType.Transform)
        {
            for (int i = 0; i < overlaps.Count; i++)
            {
                Pose pose = ReadPose(overlaps, i, overlapPoseOffset);
                AddHits(overlaps, i, hits, output);
                AddAxes(output, OverlayKind.SceneQueries, pose.ToMatrix(), MARK_SIZE * 4.0f, 0, 0, 0);
                AddCross(output, OverlayKind.SceneQueries, pose.Position, MARK_SIZE * 2.0f, OverlapColor, 0, 0, 0);
            }
        }
    }

    private static int AddHits(PropertyValue queries, int item, PropertyValue? hits, List<OverlayLine> output)
    {
        if (hits == null
            || !PhysicsValues.TryFieldOffset(queries.Type, PhysicsNames.QUERY_HITS_BASE, out int baseOffset, out BaseType baseType)
            || !PhysicsValues.TryFieldOffset(queries.Type, PhysicsNames.QUERY_HITS_COUNT, out int countOffset, out BaseType countType)
            || !PhysicsValues.TryFieldOffset(hits.Type, PhysicsNames.HIT_IMPACT, out int impactOffset, out _)
            || !PhysicsValues.TryFieldOffset(hits.Type, PhysicsNames.HIT_NORMAL, out int normalOffset, out _))
        {
            return 0;
        }

        bool hasOwners = PhysicsValues.TryFieldOffset(hits.Type, PhysicsNames.HIT_SHAPE, out int shapeOffset, out _)
            & PhysicsValues.TryFieldOffset(hits.Type, PhysicsNames.HIT_ACTOR, out int actorOffset, out _);

        int first = (int)PhysicsValues.ReadInteger(queries, item, baseOffset, baseType);
        int count = (int)PhysicsValues.ReadInteger(queries, item, countOffset, countType);
        int added = 0;

        for (int i = first; i < first + count && i >= 0 && i < hits.Count; i++)
        {
            Vector3 impact = ReadVector3(hits, i, impactOffset);
            Vector3 normal = ReadVector3(hits, i, normalOffset);
            ulong shape = hasOwners ? hits.ReadPointer(i, shapeOffset) : 0;
            ulong actor = hasOwners ? hits.ReadPointer(i, actorOffset) : 0;
            AddCross(output, OverlayKind.SceneQueries, impact, MARK_SIZE, HitColor, actor, shape, 0);
            AddLine(output, OverlayKind.SceneQueries, impact, impact + normal * CONTACT_NORMAL_LENGTH, HitColor, actor, shape, 0);
            added++;
        }

        return added;
    }

    private static void AddDebugRender(ObjectDb db, ObjectInstance scene, List<OverlayLine> output)
    {
        foreach (ulong rendererId in scene.GetRefs(ObjectDbSink.RENDERER_REF))
        {
            ObjectInstance? renderer = db.Find(rendererId);
            if (renderer == null)
            {
                continue;
            }

            ulong instance = 0;
            foreach (PropertyValue chunk in renderer.RenderChunks)
            {
                DecodeRenderChunk(chunk.Data.Span, ref instance, output);
            }
        }
    }

    private static uint FromArgb(uint argb)
    {
        return Rgba((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 8)));
    }

    private static Matrix4x4 ReadTransform(ReadOnlySpan<byte> data, int offset)
    {
        var q = new Quaternion(
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 8)),
            BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 12)));
        return new Pose(PhysicsValues.SafeQuaternion(q), ReadVector3(data, offset + 16)).ToMatrix();
    }

    private static bool TryCount(ReadOnlySpan<byte> data, ref int position, int itemSize, out int count)
    {
        count = 0;
        if (position + 4 > data.Length)
        {
            return false;
        }

        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position));
        position += 4;
        if ((long)raw * itemSize > data.Length - position)
        {
            return false;
        }

        count = (int)raw;
        return true;
    }

    private static bool DecodePoints(ReadOnlySpan<byte> data, ref int position, ulong instance, List<OverlayLine> output)
    {
        if (!TryCount(data, ref position, 16, out int count))
        {
            return false;
        }

        for (int i = 0; i < count; i++, position += 16)
        {
            uint color = FromArgb(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 12)));
            AddCross(output, OverlayKind.DebugRender, ReadVector3(data, position), MARK_SIZE * 0.5f, color, instance, 0, 0);
        }

        return true;
    }

    private static bool DecodeLines(ReadOnlySpan<byte> data, ref int position, ulong instance, List<OverlayLine> output)
    {
        if (!TryCount(data, ref position, 32, out int count))
        {
            return false;
        }

        for (int i = 0; i < count; i++, position += 32)
        {
            uint color = FromArgb(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 12)));
            AddLine(output, OverlayKind.DebugRender, ReadVector3(data, position), ReadVector3(data, position + 16), color, instance, 0, 0);
        }

        return true;
    }

    private static bool DecodeTriangles(ReadOnlySpan<byte> data, ref int position, ulong instance, List<OverlayLine> output)
    {
        if (!TryCount(data, ref position, 48, out int count))
        {
            return false;
        }

        for (int i = 0; i < count; i++, position += 48)
        {
            uint color = FromArgb(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 12)));
            Vector3 a = ReadVector3(data, position);
            Vector3 b = ReadVector3(data, position + 16);
            Vector3 c = ReadVector3(data, position + 32);
            AddLine(output, OverlayKind.DebugRender, a, b, color, instance, 0, 0);
            AddLine(output, OverlayKind.DebugRender, b, c, color, instance, 0, 0);
            AddLine(output, OverlayKind.DebugRender, c, a, color, instance, 0, 0);
        }

        return true;
    }

    private static void DecodeRenderChunk(ReadOnlySpan<byte> data, ref ulong instance, List<OverlayLine> output)
    {
        int position = 0;
        while (position < data.Length)
        {
            byte type = data[position++];
            bool ok;

            switch (type)
            {
                case RENDER_SET_INSTANCE:
                    ok = position + 8 <= data.Length;
                    if (ok)
                    {
                        instance = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position));
                        position += 8;
                    }

                    break;
                case RENDER_POINTS:
                    ok = DecodePoints(data, ref position, instance, output);
                    break;
                case RENDER_LINES:
                    ok = DecodeLines(data, ref position, instance, output);
                    break;
                case RENDER_TRIANGLES:
                    ok = DecodeTriangles(data, ref position, instance, output);
                    break;
                case RENDER_JOINT_FRAMES:
                    ok = position + TRANSFORM_SIZE * 2 <= data.Length;
                    if (ok)
                    {
                        AddAxes(output, OverlayKind.DebugRender, ReadTransform(data, position), JOINT_AXIS_LENGTH, instance, 0, 0);
                        AddAxes(output, OverlayKind.DebugRender, ReadTransform(data, position + TRANSFORM_SIZE), JOINT_AXIS_LENGTH * 0.6f, instance, 0, 0);
                        position += TRANSFORM_SIZE * 2;
                    }

                    break;
                case RENDER_LINEAR_LIMIT:
                    ok = Skip(data, ref position, TRANSFORM_SIZE * 2 + 5);
                    break;
                case RENDER_ANGULAR_LIMIT:
                case RENDER_LIMIT_CONE:
                    ok = Skip(data, ref position, TRANSFORM_SIZE + 9);
                    break;
                case RENDER_DOUBLE_CONE:
                    ok = Skip(data, ref position, TRANSFORM_SIZE + 5);
                    break;
                case RENDER_TEXT:
                    ok = Skip(data, ref position, 20);
                    if (ok && position + 4 <= data.Length)
                    {
                        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position));
                        position += 4;
                        ok = Skip(data, ref position, (int)Math.Min(length, int.MaxValue));
                    }
                    else
                    {
                        ok = false;
                    }

                    break;
                case RENDER_DEBUG:
                    ok = DecodePoints(data, ref position, instance, output)
                        && DecodeLines(data, ref position, instance, output)
                        && DecodeTriangles(data, ref position, instance, output);
                    break;
                default:
                    ok = false;
                    break;
            }

            if (!ok)
            {
                return;
            }
        }
    }

    private static bool Skip(ReadOnlySpan<byte> data, ref int position, int count)
    {
        if (count < 0 || position + count > data.Length)
        {
            return false;
        }

        position += count;
        return true;
    }
}
