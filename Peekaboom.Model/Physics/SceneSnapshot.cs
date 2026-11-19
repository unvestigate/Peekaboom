using System.Numerics;

namespace Peekaboom.Model.Physics;

public enum ActorKind
{
    Static,
    Dynamic,
    Kinematic,
    ArticulationLink,
}

public enum ShapeCategory
{
    Simulation,
    QueryOnly,
    Trigger,
    Other,
}

public enum GeometryKind
{
    Box,
    Sphere,
    Capsule,
    Plane,
    ConvexMesh,
    TriangleMesh,
    HeightField,
    TetrahedronMesh,
    Custom,
    Unknown,
}

public sealed class GeometryInfo
{
    public GeometryInfo(ObjectInstance instance, GeometryKind kind)
    {
        Instance = instance;
        Kind = kind;
    }

    public ObjectInstance Instance { get; }

    public GeometryKind Kind { get; }

    public Vector3 HalfExtents { get; internal set; }

    public float Radius { get; internal set; }

    public float HalfHeight { get; internal set; }

    public Vector3 MeshScale { get; internal set; } = Vector3.One;

    public Quaternion MeshScaleRotation { get; internal set; } = Quaternion.Identity;

    public float HeightScale { get; internal set; } = 1.0f;

    public float RowScale { get; internal set; } = 1.0f;

    public float ColumnScale { get; internal set; } = 1.0f;

    public ObjectInstance? Mesh { get; internal set; }

    public Matrix4x4 MeshScaleMatrix()
    {
        Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(MeshScaleRotation);
        return rotation * Matrix4x4.CreateScale(MeshScale) * Matrix4x4.Transpose(rotation);
    }
}

public sealed class ShapeInfo
{
    public ShapeInfo(ObjectInstance instance, Pose localPose, ulong flags, GeometryInfo? geometry)
    {
        Instance = instance;
        LocalPose = localPose;
        Flags = flags;
        Geometry = geometry;
    }

    public ObjectInstance Instance { get; }

    public Pose LocalPose { get; }

    public ulong Flags { get; }

    public string Name { get; internal set; } = "";

    public ShapeCategory Category { get; internal set; }

    public GeometryInfo? Geometry { get; }
}

public sealed class ActorInfo
{
    public ActorInfo(ObjectInstance instance, ActorKind kind, bool isSleeping, Pose globalPose)
    {
        Instance = instance;
        Kind = kind;
        IsSleeping = isSleeping;
        GlobalPose = globalPose;
    }

    public ObjectInstance Instance { get; }

    public ActorKind Kind { get; }

    public bool IsSleeping { get; }

    public Pose GlobalPose { get; }

    public string Name { get; internal set; } = "";

    public Vector3 CenterOfMass { get; internal set; }

    public Vector3 LinearVelocity { get; internal set; }

    public Vector3 AngularVelocity { get; internal set; }

    public List<ShapeInfo> Shapes { get; } = new();
}

public sealed class SceneSnapshot
{
    private readonly HashSet<ulong> mVisited = new();

    private SceneSnapshot(ObjectInstance? scene)
    {
        Scene = scene;
    }

    public ObjectInstance? Scene { get; }

    public List<ActorInfo> Actors { get; } = new();

    public static SceneSnapshot Build(ObjectDb db, ulong sceneId)
    {
        ObjectInstance? scene = db.Find(sceneId);
        var snapshot = new SceneSnapshot(scene);
        if (scene == null)
        {
            return snapshot;
        }

        foreach (ulong id in scene.GetRefs(PhysicsNames.RIGID_STATICS))
        {
            snapshot.AddActor(db, id);
        }

        foreach (ulong id in scene.GetRefs(PhysicsNames.RIGID_DYNAMICS))
        {
            snapshot.AddActor(db, id);
        }

        foreach (ulong id in scene.GetRefs(PhysicsNames.ARTICULATIONS))
        {
            ObjectInstance? articulation = db.Find(id);
            if (articulation == null)
            {
                continue;
            }

            foreach (ulong link in articulation.GetRefs(PhysicsNames.LINKS))
            {
                snapshot.AddLinkTree(db, link);
            }
        }

        return snapshot;
    }

    private void AddLinkTree(ObjectDb db, ulong id)
    {
        if (!AddActor(db, id))
        {
            return;
        }

        ObjectInstance? link = db.Find(id);
        if (link == null)
        {
            return;
        }

        foreach (ulong child in link.GetRefs(PhysicsNames.LINKS))
        {
            AddLinkTree(db, child);
        }
    }

    private bool AddActor(ObjectDb db, ulong id)
    {
        if (!mVisited.Add(id))
        {
            return false;
        }

        ObjectInstance? instance = db.Find(id);
        if (instance == null)
        {
            return false;
        }

        ActorKind kind;
        if (PhysicsValues.IsClass(instance.Class, PhysicsNames.RIGID_STATIC))
        {
            kind = ActorKind.Static;
        }
        else if (PhysicsValues.IsClass(instance.Class, PhysicsNames.ARTICULATION_LINK))
        {
            kind = ActorKind.ArticulationLink;
        }
        else if (PhysicsValues.IsClass(instance.Class, PhysicsNames.RIGID_BODY))
        {
            ulong kinematicFlag = PhysicsValues.FlagValue(instance, PhysicsNames.RIGID_BODY_FLAGS, PhysicsNames.KINEMATIC_FLAG, 1);
            ulong flags = PhysicsValues.GetInteger(instance, PhysicsNames.RIGID_BODY_FLAGS, 0);
            kind = (flags & kinematicFlag) != 0 ? ActorKind.Kinematic : ActorKind.Dynamic;
        }
        else
        {
            return false;
        }

        PhysicsValues.TryGetPose(instance, PhysicsNames.GLOBAL_POSE, out Pose globalPose);
        bool sleeping = PhysicsValues.GetBool(instance, PhysicsNames.IS_SLEEPING, false);
        var actor = new ActorInfo(instance, kind, sleeping, globalPose)
        {
            Name = PhysicsValues.GetString(instance, PhysicsNames.NAME),
        };

        Vector3 centerOfMass = globalPose.Position;
        if (PhysicsValues.TryGetPose(instance, PhysicsNames.CMASS_LOCAL_POSE, out Pose massPose))
        {
            centerOfMass = Vector3.Transform(massPose.Position, globalPose.ToMatrix());
        }

        actor.CenterOfMass = centerOfMass;
        PhysicsValues.TryGetVector3(instance, PhysicsNames.LINEAR_VELOCITY, out Vector3 linear);
        PhysicsValues.TryGetVector3(instance, PhysicsNames.ANGULAR_VELOCITY, out Vector3 angular);
        actor.LinearVelocity = linear;
        actor.AngularVelocity = angular;

        foreach (ulong shapeId in instance.GetRefs(PhysicsNames.SHAPES))
        {
            ObjectInstance? shape = db.Find(shapeId);
            if (shape == null)
            {
                continue;
            }

            PhysicsValues.TryGetPose(shape, PhysicsNames.LOCAL_POSE, out Pose localPose);
            ulong flags = PhysicsValues.GetInteger(shape, PhysicsNames.SHAPE_FLAGS, 0);
            ObjectInstance? geometry = db.Find(PhysicsValues.GetRef(shape, PhysicsNames.GEOMETRY));
            actor.Shapes.Add(new ShapeInfo(shape, localPose, flags, geometry != null ? ReadGeometry(db, geometry) : null)
            {
                Name = PhysicsValues.GetString(shape, PhysicsNames.NAME),
                Category = CategoryOf(shape, flags),
            });
        }

        Actors.Add(actor);
        return true;
    }

    private static ShapeCategory CategoryOf(ObjectInstance shape, ulong flags)
    {
        ulong trigger = PhysicsValues.FlagValue(shape, PhysicsNames.SHAPE_FLAGS, PhysicsNames.TRIGGER_FLAG, 4);
        ulong simulation = PhysicsValues.FlagValue(shape, PhysicsNames.SHAPE_FLAGS, PhysicsNames.SIMULATION_FLAG, 1);
        ulong query = PhysicsValues.FlagValue(shape, PhysicsNames.SHAPE_FLAGS, PhysicsNames.QUERY_FLAG, 2);

        if ((flags & trigger) != 0)
        {
            return ShapeCategory.Trigger;
        }

        if ((flags & simulation) != 0)
        {
            return ShapeCategory.Simulation;
        }

        return (flags & query) != 0 ? ShapeCategory.QueryOnly : ShapeCategory.Other;
    }

    private static GeometryInfo ReadGeometry(ObjectDb db, ObjectInstance geometry)
    {
        GeometryKind kind = KindOf(geometry);
        var info = new GeometryInfo(geometry, kind);

        switch (kind)
        {
            case GeometryKind.Box:
                PhysicsValues.TryGetVector3(geometry, PhysicsNames.HALF_EXTENTS, out Vector3 halfExtents);
                info.HalfExtents = halfExtents;
                break;
            case GeometryKind.Sphere:
                info.Radius = PhysicsValues.GetFloat(geometry, PhysicsNames.RADIUS, 0.0f);
                break;
            case GeometryKind.Capsule:
                info.Radius = PhysicsValues.GetFloat(geometry, PhysicsNames.RADIUS, 0.0f);
                info.HalfHeight = PhysicsValues.GetFloat(geometry, PhysicsNames.HALF_HEIGHT, 0.0f);
                break;
            case GeometryKind.ConvexMesh:
                ReadMeshScale(geometry, info);
                info.Mesh = db.Find(PhysicsValues.GetRef(geometry, PhysicsNames.CONVEX_MESH));
                break;
            case GeometryKind.TriangleMesh:
                ReadMeshScale(geometry, info);
                info.Mesh = db.Find(PhysicsValues.GetRef(geometry, PhysicsNames.TRIANGLE_MESH));
                break;
            case GeometryKind.HeightField:
                info.HeightScale = PhysicsValues.GetFloat(geometry, PhysicsNames.HEIGHT_SCALE, 1.0f);
                info.RowScale = PhysicsValues.GetFloat(geometry, PhysicsNames.ROW_SCALE, 1.0f);
                info.ColumnScale = PhysicsValues.GetFloat(geometry, PhysicsNames.COLUMN_SCALE, 1.0f);
                info.Mesh = db.Find(PhysicsValues.GetRef(geometry, PhysicsNames.HEIGHTFIELD));
                break;
            case GeometryKind.TetrahedronMesh:
                info.Mesh = db.Find(PhysicsValues.GetRef(geometry, PhysicsNames.TETRAHEDRON_MESH));
                break;
        }

        return info;
    }

    private static void ReadMeshScale(ObjectInstance geometry, GeometryInfo info)
    {
        if (PhysicsValues.TryGetVector3(geometry, PhysicsNames.MESH_SCALE, out Vector3 scale))
        {
            info.MeshScale = scale;
        }

        if (PhysicsValues.TryGetQuaternion(geometry, PhysicsNames.MESH_SCALE_ROTATION, out Quaternion rotation))
        {
            info.MeshScaleRotation = rotation;
        }
    }

    private static GeometryKind KindOf(ObjectInstance geometry)
    {
        string name = geometry.Class.Name.Name;
        switch (name)
        {
            case PhysicsNames.BOX_GEOMETRY:
                return GeometryKind.Box;
            case PhysicsNames.SPHERE_GEOMETRY:
                return GeometryKind.Sphere;
            case PhysicsNames.CAPSULE_GEOMETRY:
                return GeometryKind.Capsule;
            case PhysicsNames.PLANE_GEOMETRY:
                return GeometryKind.Plane;
            case PhysicsNames.CONVEX_GEOMETRY:
                return GeometryKind.ConvexMesh;
            case PhysicsNames.TRIANGLE_GEOMETRY:
                return GeometryKind.TriangleMesh;
            case PhysicsNames.HEIGHTFIELD_GEOMETRY:
                return GeometryKind.HeightField;
            case PhysicsNames.TETRAHEDRON_GEOMETRY:
                return GeometryKind.TetrahedronMesh;
            case PhysicsNames.CUSTOM_GEOMETRY:
                return GeometryKind.Custom;
            default:
                return GeometryKind.Unknown;
        }
    }
}
