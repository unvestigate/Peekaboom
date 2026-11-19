using System.Numerics;

using Peekaboom.Protocol;

namespace Peekaboom.Model.Physics;

public readonly record struct MeshKey(GeometryKind Kind, long Serial, long Origin0, long Origin1, long Origin2);

public sealed class MeshData
{
    public MeshData(Vector3[] positions, uint[] indices)
    {
        Positions = positions;
        Indices = indices;

        if (positions.Length == 0)
        {
            return;
        }

        Vector3 min = positions[0];
        Vector3 max = positions[0];
        foreach (Vector3 position in positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        BoundsMin = min;
        BoundsMax = max;
    }

    public Vector3[] Positions { get; }

    public uint[] Indices { get; }

    public Vector3 BoundsMin { get; }

    public Vector3 BoundsMax { get; }
}

public static class MeshBuilder
{
    public static MeshKey? KeyOf(GeometryInfo geometry)
    {
        ObjectInstance? mesh = geometry.Mesh;
        if (mesh == null)
        {
            return null;
        }

        switch (geometry.Kind)
        {
            case GeometryKind.ConvexMesh:
                return new MeshKey(geometry.Kind, mesh.Serial, Origin(mesh, PhysicsNames.POINTS),
                    Origin(mesh, PhysicsNames.HULL_POLYGONS), Origin(mesh, PhysicsNames.POLYGON_INDEXES));
            case GeometryKind.TriangleMesh:
                return new MeshKey(geometry.Kind, mesh.Serial, Origin(mesh, PhysicsNames.POINTS),
                    Origin(mesh, PhysicsNames.TRIANGLES), 0);
            case GeometryKind.HeightField:
                return new MeshKey(geometry.Kind, mesh.Serial, Origin(mesh, PhysicsNames.SAMPLES),
                    Origin(mesh, PhysicsNames.NB_ROWS), Origin(mesh, PhysicsNames.NB_COLUMNS));
            case GeometryKind.TetrahedronMesh:
                return new MeshKey(geometry.Kind, mesh.Serial, Origin(mesh, PhysicsNames.VERTICES),
                    Origin(mesh, PhysicsNames.TETRAHEDRONS), 0);
            default:
                return null;
        }
    }

    public static MeshData? Build(GeometryInfo geometry)
    {
        ObjectInstance? mesh = geometry.Mesh;
        if (mesh == null)
        {
            return null;
        }

        switch (geometry.Kind)
        {
            case GeometryKind.ConvexMesh:
                return BuildConvex(mesh);
            case GeometryKind.TriangleMesh:
                return BuildTriangleMesh(mesh);
            case GeometryKind.HeightField:
                return BuildHeightField(mesh);
            case GeometryKind.TetrahedronMesh:
                return BuildTetrahedronMesh(mesh);
            default:
                return null;
        }
    }

    private static long Origin(ObjectInstance instance, string name)
    {
        if (!instance.TryGetSlot(name, out PropertySlot slot))
        {
            return -1;
        }

        return slot.Value != null ? slot.Value.Origin : slot.Payload!.Origin;
    }

    private static Vector3[]? ReadPoints(ObjectInstance mesh, string name)
    {
        PropertyValue? value = mesh.GetValue(name);
        if (value == null || value.Type.BaseType != BaseType.Vec3)
        {
            return null;
        }

        var points = new Vector3[value.Count];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = PhysicsValues.ReadVector3(value, i);
        }

        return points;
    }

    private static uint[]? ReadIndices(ObjectInstance mesh, string name)
    {
        PropertyValue? value = mesh.GetValue(name);
        if (value == null || !PhysicsValues.IsInteger(value.Type.BaseType))
        {
            return null;
        }

        BaseType type = value.Type.BaseType;
        var indices = new uint[value.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = (uint)PhysicsValues.ReadInteger(value, i, 0, type);
        }

        return indices;
    }

    private static void AddTriangle(List<uint> output, uint a, uint b, uint c, int vertexCount)
    {
        if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
        {
            return;
        }

        output.Add(a);
        output.Add(b);
        output.Add(c);
    }

    private static MeshData? BuildConvex(ObjectInstance mesh)
    {
        Vector3[]? points = ReadPoints(mesh, PhysicsNames.POINTS);
        uint[]? indexes = ReadIndices(mesh, PhysicsNames.POLYGON_INDEXES);
        PropertyValue? polygons = mesh.GetValue(PhysicsNames.HULL_POLYGONS);
        if (points == null || indexes == null || polygons == null)
        {
            return null;
        }

        if (!PhysicsValues.TryFieldOffset(polygons.Type, PhysicsNames.NUM_VERTICES, out int countOffset, out BaseType countType)
            || !PhysicsValues.TryFieldOffset(polygons.Type, PhysicsNames.INDEX_BASE, out int baseOffset, out BaseType baseType))
        {
            return null;
        }

        var triangles = new List<uint>();
        for (int p = 0; p < polygons.Count; p++)
        {
            int count = (int)PhysicsValues.ReadInteger(polygons, p, countOffset, countType);
            int first = (int)PhysicsValues.ReadInteger(polygons, p, baseOffset, baseType);
            if (count < 3 || first + count > indexes.Length)
            {
                continue;
            }

            uint v0 = indexes[first];
            for (int k = 1; k + 1 < count; k++)
            {
                AddTriangle(triangles, v0, indexes[first + k], indexes[first + k + 1], points.Length);
            }
        }

        return new MeshData(points, triangles.ToArray());
    }

    private static MeshData? BuildTriangleMesh(ObjectInstance mesh)
    {
        Vector3[]? points = ReadPoints(mesh, PhysicsNames.POINTS);
        uint[]? indices = ReadIndices(mesh, PhysicsNames.TRIANGLES);
        if (points == null || indices == null)
        {
            return null;
        }

        var triangles = new List<uint>(indices.Length);
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            AddTriangle(triangles, indices[i], indices[i + 1], indices[i + 2], points.Length);
        }

        return new MeshData(points, triangles.ToArray());
    }

    private static MeshData? BuildTetrahedronMesh(ObjectInstance mesh)
    {
        Vector3[]? points = ReadPoints(mesh, PhysicsNames.VERTICES) ?? ReadPoints(mesh, PhysicsNames.POINTS);
        uint[]? indices = ReadIndices(mesh, PhysicsNames.TETRAHEDRONS);
        if (points == null || indices == null)
        {
            return null;
        }

        var triangles = new List<uint>(indices.Length * 3);
        for (int i = 0; i + 3 < indices.Length; i += 4)
        {
            uint a = indices[i];
            uint b = indices[i + 1];
            uint c = indices[i + 2];
            uint d = indices[i + 3];
            AddTriangle(triangles, a, b, c, points.Length);
            AddTriangle(triangles, a, b, d, points.Length);
            AddTriangle(triangles, a, c, d, points.Length);
            AddTriangle(triangles, b, c, d, points.Length);
        }

        return new MeshData(points, triangles.ToArray());
    }

    private static MeshData? BuildHeightField(ObjectInstance mesh)
    {
        int rows = (int)PhysicsValues.GetInteger(mesh, PhysicsNames.NB_ROWS, 0);
        int columns = (int)PhysicsValues.GetInteger(mesh, PhysicsNames.NB_COLUMNS, 0);
        PropertyValue? samples = mesh.GetValue(PhysicsNames.SAMPLES);
        if (samples == null || rows < 2 || columns < 2 || (long)rows * columns > samples.Count)
        {
            return null;
        }

        if (!PhysicsValues.TryFieldOffset(samples.Type, PhysicsNames.SAMPLE_HEIGHT, out int heightOffset, out _)
            || !PhysicsValues.TryFieldOffset(samples.Type, PhysicsNames.SAMPLE_MATERIAL0, out int material0Offset, out _)
            || !PhysicsValues.TryFieldOffset(samples.Type, PhysicsNames.SAMPLE_MATERIAL1, out int material1Offset, out _))
        {
            return null;
        }

        int count = rows * columns;
        var positions = new Vector3[count];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int index = r * columns + c;
                short height = (short)PhysicsValues.ReadInteger(samples, index, heightOffset, BaseType.U16);
                positions[index] = new Vector3(r, height, c);
            }
        }

        var triangles = new List<uint>((rows - 1) * (columns - 1) * 6);
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < columns - 1; c++)
            {
                uint cell = (uint)(r * columns + c);
                uint below = cell + (uint)columns;
                ReadOnlySpan<byte> sample = samples.Item((int)cell);
                byte material0 = sample[material0Offset];
                byte material1 = sample[material1Offset];
                bool tessellated = (material0 & 0x80) != 0;
                bool firstHole = (material0 & 0x7F) == PhysicsNames.HEIGHTFIELD_HOLE;
                bool secondHole = (material1 & 0x7F) == PhysicsNames.HEIGHTFIELD_HOLE;

                if (tessellated)
                {
                    if (!firstHole)
                    {
                        AddTriangle(triangles, below, cell, below + 1, count);
                    }

                    if (!secondHole)
                    {
                        AddTriangle(triangles, cell + 1, below + 1, cell, count);
                    }
                }
                else
                {
                    if (!firstHole)
                    {
                        AddTriangle(triangles, cell, cell + 1, below, count);
                    }

                    if (!secondHole)
                    {
                        AddTriangle(triangles, below + 1, below, cell + 1, count);
                    }
                }
            }
        }

        return new MeshData(positions, triangles.ToArray());
    }
}
