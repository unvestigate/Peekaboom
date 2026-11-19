using System.Numerics;

using Peekaboom.Model.Physics;

namespace Peekaboom.Rendering;

public enum DrawShape
{
    Box,
    Capsule,
    Plane,
    Mesh,
}

public enum RenderMode
{
    Solid,
    Wireframe,
    SolidWireframe,
}

public struct DrawItem
{
    public DrawShape Shape;
    public Matrix4x4 World;
    public Vector4 Color;
    public float Radius;
    public float HalfHeight;
    public MeshKey Key;
    public MeshData? Mesh;
    public ulong ActorId;
    public ulong ShapeId;
    public ulong GeometryId;
    public ulong MeshId;
    public bool HasBounds;
    public Vector3 BoundsMin;
    public Vector3 BoundsMax;
    public ActorKind ActorKind;
    public GeometryKind Geometry;
    public ShapeCategory Category;
    public bool Sleeping;
    public string? ActorName;
    public string? ShapeName;

    public bool Matches(IReadOnlySet<ulong> ids)
    {
        return ids.Contains(ActorId) || ids.Contains(ShapeId) || ids.Contains(GeometryId) || (MeshId != 0 && ids.Contains(MeshId));
    }
}

public sealed class PickRequest
{
    public PickRequest(int x, int y, bool selectShape)
    {
        X = x;
        Y = y;
        SelectShape = selectShape;
    }

    public int X { get; }

    public int Y { get; }

    public bool SelectShape { get; }

    public bool Completed { get; set; }

    public int ItemIndex { get; set; } = -1;

    public bool HasPoint { get; set; }

    public Vector3 WorldPoint { get; set; }
}

public struct ActorOverlay
{
    public ulong ActorId;
    public ActorKind Kind;
    public Matrix4x4 Pose;
    public Vector3 CenterOfMass;
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
}

public sealed class SceneDrawList
{
    public List<DrawItem> Items { get; } = new();

    public List<OverlayLine> Lines { get; } = new();

    public List<ActorOverlay> Actors { get; } = new();

    public bool HasBounds { get; private set; }

    public Vector3 BoundsMin { get; private set; }

    public Vector3 BoundsMax { get; private set; }

    public void AddBounds(ref DrawItem item, Vector3 localMin, Vector3 localMax, Matrix4x4 world)
    {
        Vector3 min = Vector3.Zero;
        Vector3 max = Vector3.Zero;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) != 0 ? localMax.X : localMin.X,
                (i & 2) != 0 ? localMax.Y : localMin.Y,
                (i & 4) != 0 ? localMax.Z : localMin.Z);
            Vector3 point = Vector3.Transform(corner, world);

            min = i == 0 ? point : Vector3.Min(min, point);
            max = i == 0 ? point : Vector3.Max(max, point);
        }

        item.HasBounds = true;
        item.BoundsMin = min;
        item.BoundsMax = max;

        if (!HasBounds)
        {
            BoundsMin = min;
            BoundsMax = max;
            HasBounds = true;
        }
        else
        {
            BoundsMin = Vector3.Min(BoundsMin, min);
            BoundsMax = Vector3.Max(BoundsMax, max);
        }
    }

    public bool TryGetBounds(IReadOnlySet<ulong> ids, out Vector3 min, out Vector3 max)
    {
        min = Vector3.Zero;
        max = Vector3.Zero;
        bool found = false;

        foreach (DrawItem item in Items)
        {
            if (!item.HasBounds || !item.Matches(ids))
            {
                continue;
            }

            min = found ? Vector3.Min(min, item.BoundsMin) : item.BoundsMin;
            max = found ? Vector3.Max(max, item.BoundsMax) : item.BoundsMax;
            found = true;
        }

        return found;
    }
}

public sealed class ViewportFrame
{
    public Matrix4x4 ViewProjection { get; init; }

    public Vector3 CameraPosition { get; init; }

    public SceneDrawList? DrawList { get; init; }

    public RenderMode Mode { get; init; }

    public bool ShowGrid { get; init; }

    public bool LeftHanded { get; init; }

    public IReadOnlySet<ulong>? Selection { get; init; }

    public PickRequest? Pick { get; init; }

    public DisplayOptions? Display { get; init; }
}
