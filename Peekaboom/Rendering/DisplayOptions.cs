using Peekaboom.Model.Physics;
using Peekaboom.Protocol;

namespace Peekaboom.Rendering;

public enum OverlayMode
{
    None,
    Selected,
    All,
}

public sealed class DisplayOptions
{
    public bool ShowStatic { get; set; } = true;

    public bool ShowDynamic { get; set; } = true;

    public bool ShowKinematic { get; set; } = true;

    public bool ShowArticulationLinks { get; set; } = true;

    public bool ShowAwake { get; set; } = true;

    public bool ShowSleeping { get; set; } = true;

    public bool ShowBoxes { get; set; } = true;

    public bool ShowSpheres { get; set; } = true;

    public bool ShowCapsules { get; set; } = true;

    public bool ShowPlanes { get; set; } = true;

    public bool ShowConvexMeshes { get; set; } = true;

    public bool ShowTriangleMeshes { get; set; } = true;

    public bool ShowHeightFields { get; set; } = true;

    public bool ShowTetrahedronMeshes { get; set; } = true;

    public bool ShowSimulationShapes { get; set; } = true;

    public bool ShowQueryShapes { get; set; } = true;

    public bool ShowTriggerShapes { get; set; } = true;

    public string NameFilter { get; set; } = "";

    public OverlayMode Contacts { get; set; } = OverlayMode.All;

    public OverlayMode Joints { get; set; } = OverlayMode.Selected;

    public OverlayMode SceneQueries { get; set; } = OverlayMode.All;

    public OverlayMode DebugRender { get; set; } = OverlayMode.All;

    public OverlayMode Bounds { get; set; } = OverlayMode.Selected;

    public OverlayMode Axes { get; set; } = OverlayMode.Selected;

    public OverlayMode Velocities { get; set; } = OverlayMode.None;

    public DisplayOptions Clone()
    {
        return (DisplayOptions)MemberwiseClone();
    }

    public OverlayMode ModeOf(OverlayKind kind)
    {
        switch (kind)
        {
            case OverlayKind.Contacts:
                return Contacts;
            case OverlayKind.Joints:
                return Joints;
            case OverlayKind.SceneQueries:
                return SceneQueries;
            case OverlayKind.DebugRender:
                return DebugRender;
            case OverlayKind.Bounds:
                return Bounds;
            case OverlayKind.Axes:
                return Axes;
            default:
                return Velocities;
        }
    }

    public bool IsVisible(in DrawItem item)
    {
        bool actorVisible = item.ActorKind switch
        {
            ActorKind.Static => ShowStatic,
            ActorKind.Kinematic => ShowKinematic,
            ActorKind.ArticulationLink => ShowArticulationLinks,
            _ => ShowDynamic,
        };

        if (!actorVisible)
        {
            return false;
        }

        if (item.ActorKind != ActorKind.Static && !(item.Sleeping ? ShowSleeping : ShowAwake))
        {
            return false;
        }

        bool geometryVisible = item.Geometry switch
        {
            GeometryKind.Box => ShowBoxes,
            GeometryKind.Sphere => ShowSpheres,
            GeometryKind.Capsule => ShowCapsules,
            GeometryKind.Plane => ShowPlanes,
            GeometryKind.ConvexMesh => ShowConvexMeshes,
            GeometryKind.TriangleMesh => ShowTriangleMeshes,
            GeometryKind.HeightField => ShowHeightFields,
            GeometryKind.TetrahedronMesh => ShowTetrahedronMeshes,
            _ => true,
        };

        if (!geometryVisible)
        {
            return false;
        }

        bool categoryVisible = item.Category switch
        {
            ShapeCategory.Simulation => ShowSimulationShapes,
            ShapeCategory.QueryOnly => ShowQueryShapes,
            ShapeCategory.Trigger => ShowTriggerShapes,
            _ => true,
        };

        if (!categoryVisible)
        {
            return false;
        }

        return NameFilter.Length == 0 || MatchesName(item);
    }

    private bool MatchesName(in DrawItem item)
    {
        string filter = NameFilter;
        if ((item.ActorName ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (item.ShapeName ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ValueFormatter.FormatId(item.ActorId).Contains(filter, StringComparison.OrdinalIgnoreCase)
            || ValueFormatter.FormatId(item.ShapeId).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
