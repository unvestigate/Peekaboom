using System.Numerics;

using Peekaboom.Model;
using Peekaboom.Model.Physics;

namespace Peekaboom.Rendering;

public sealed class SceneDrawListBuilder
{
    private const float PLANE_EXTENT = 200.0f;

    private static readonly Vector4 StaticColor = new(0.68f, 0.68f, 0.66f, 1.0f);
    private static readonly Vector4 DynamicColor = new(0.56f, 0.84f, 0.56f, 1.0f);
    private static readonly Vector4 DynamicSleepingColor = new(0.22f, 0.45f, 0.24f, 1.0f);
    private static readonly Vector4 KinematicColor = new(0.56f, 0.70f, 0.94f, 1.0f);
    private static readonly Vector4 KinematicSleepingColor = new(0.22f, 0.32f, 0.56f, 1.0f);
    private static readonly Vector4 LinkColor = new(0.62f, 0.86f, 0.46f, 1.0f);
    private static readonly Vector4 LinkSleepingColor = new(0.28f, 0.46f, 0.20f, 1.0f);

    private readonly Dictionary<MeshKey, MeshData?> mMeshes = new();

    public void Clear()
    {
        mMeshes.Clear();
    }

    public SceneDrawList Build(ObjectDb state, SceneSnapshot snapshot)
    {
        var list = new SceneDrawList();

        foreach (ActorInfo actor in snapshot.Actors)
        {
            Matrix4x4 actorWorld = actor.GlobalPose.ToMatrix();
            Vector4 color = ColorOf(actor);

            list.Actors.Add(new ActorOverlay
            {
                ActorId = actor.Instance.Id,
                Kind = actor.Kind,
                Pose = actorWorld,
                CenterOfMass = actor.CenterOfMass,
                LinearVelocity = actor.LinearVelocity,
                AngularVelocity = actor.AngularVelocity,
            });

            foreach (ShapeInfo shape in actor.Shapes)
            {
                if (shape.Geometry != null)
                {
                    var item = new DrawItem
                    {
                        Color = color,
                        ActorId = actor.Instance.Id,
                        ShapeId = shape.Instance.Id,
                        GeometryId = shape.Geometry.Instance.Id,
                        MeshId = shape.Geometry.Mesh?.Id ?? 0,
                        ActorKind = actor.Kind,
                        Geometry = shape.Geometry.Kind,
                        Category = shape.Category,
                        Sleeping = actor.IsSleeping,
                        ActorName = actor.Name,
                        ShapeName = shape.Name,
                    };
                    AddShape(list, item, shape.Geometry, shape.LocalPose.ToMatrix() * actorWorld);
                }
            }
        }

        OverlayBuilder.Build(state, snapshot, list.Lines);
        return list;
    }

    private static Vector4 ColorOf(ActorInfo actor)
    {
        switch (actor.Kind)
        {
            case ActorKind.Static:
                return StaticColor;
            case ActorKind.Kinematic:
                return actor.IsSleeping ? KinematicSleepingColor : KinematicColor;
            case ActorKind.ArticulationLink:
                return actor.IsSleeping ? LinkSleepingColor : LinkColor;
            default:
                return actor.IsSleeping ? DynamicSleepingColor : DynamicColor;
        }
    }

    private void AddShape(SceneDrawList list, DrawItem item, GeometryInfo geometry, Matrix4x4 shapeWorld)
    {
        switch (geometry.Kind)
        {
            case GeometryKind.Box:
            {
                Vector3 halfExtents = geometry.HalfExtents;
                item.Shape = DrawShape.Box;
                item.World = Matrix4x4.CreateScale(halfExtents) * shapeWorld;
                list.AddBounds(ref item, -halfExtents, halfExtents, shapeWorld);
                break;
            }

            case GeometryKind.Sphere:
            case GeometryKind.Capsule:
            {
                float radius = geometry.Radius;
                float halfHeight = geometry.Kind == GeometryKind.Capsule ? geometry.HalfHeight : 0.0f;
                item.Shape = DrawShape.Capsule;
                item.World = shapeWorld;
                item.Radius = radius;
                item.HalfHeight = halfHeight;
                var extent = new Vector3(halfHeight + radius, radius, radius);
                list.AddBounds(ref item, -extent, extent, shapeWorld);
                break;
            }

            case GeometryKind.Plane:
                item.Shape = DrawShape.Plane;
                item.World = Matrix4x4.CreateScale(1.0f, PLANE_EXTENT, PLANE_EXTENT) * shapeWorld;
                break;

            case GeometryKind.ConvexMesh:
            case GeometryKind.TriangleMesh:
            case GeometryKind.TetrahedronMesh:
            case GeometryKind.HeightField:
            {
                MeshKey? key = MeshBuilder.KeyOf(geometry);
                if (key == null)
                {
                    return;
                }

                MeshData? mesh = GetMesh(key.Value, geometry);
                if (mesh == null || mesh.Indices.Length == 0)
                {
                    return;
                }

                Matrix4x4 local = geometry.Kind == GeometryKind.HeightField
                    ? Matrix4x4.CreateScale(geometry.RowScale, geometry.HeightScale, geometry.ColumnScale)
                    : geometry.Kind == GeometryKind.TetrahedronMesh ? Matrix4x4.Identity : geometry.MeshScaleMatrix();

                item.Shape = DrawShape.Mesh;
                item.World = local * shapeWorld;
                item.Key = key.Value;
                item.Mesh = mesh;
                list.AddBounds(ref item, mesh.BoundsMin, mesh.BoundsMax, item.World);
                break;
            }

            default:
                return;
        }

        list.Items.Add(item);
    }

    private MeshData? GetMesh(MeshKey key, GeometryInfo geometry)
    {
        if (!mMeshes.TryGetValue(key, out MeshData? mesh))
        {
            mesh = MeshBuilder.Build(geometry);
            mMeshes[key] = mesh;
        }

        return mesh;
    }
}
