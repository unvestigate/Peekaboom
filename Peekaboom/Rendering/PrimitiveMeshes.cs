using System.Numerics;

namespace Peekaboom.Rendering;

public static class PrimitiveMeshes
{
    private const int CAPSULE_RINGS = 16;
    private const int CAPSULE_SEGMENTS = 24;

    public static void Box(out Vector4[] vertices, out uint[] indices)
    {
        vertices = new Vector4[8];
        for (int i = 0; i < 8; i++)
        {
            vertices[i] = new Vector4((i & 1) != 0 ? 1.0f : -1.0f, (i & 2) != 0 ? 1.0f : -1.0f, (i & 4) != 0 ? 1.0f : -1.0f, 0.0f);
        }

        indices = new uint[]
        {
            0, 2, 3, 0, 3, 1,
            4, 5, 7, 4, 7, 6,
            0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,
            0, 4, 6, 0, 6, 2,
            1, 3, 7, 1, 7, 5,
        };
    }

    public static void Plane(out Vector4[] vertices, out uint[] indices)
    {
        vertices = new Vector4[]
        {
            new(0.0f, -1.0f, -1.0f, 0.0f),
            new(0.0f, 1.0f, -1.0f, 0.0f),
            new(0.0f, 1.0f, 1.0f, 0.0f),
            new(0.0f, -1.0f, 1.0f, 0.0f),
        };

        indices = new uint[] { 0, 1, 2, 0, 2, 3 };
    }

    public static void Capsule(out Vector4[] vertices, out uint[] indices)
    {
        var rings = new List<(float X, float Radius, float Side)>();
        for (int i = 0; i <= CAPSULE_RINGS; i++)
        {
            double angle = Math.PI * i / CAPSULE_RINGS;
            float x = (float)Math.Cos(angle);
            float radius = (float)Math.Sin(angle);

            if (i * 2 == CAPSULE_RINGS)
            {
                rings.Add((0.0f, 1.0f, 1.0f));
                rings.Add((0.0f, 1.0f, -1.0f));
            }
            else
            {
                rings.Add((x, radius, i * 2 < CAPSULE_RINGS ? 1.0f : -1.0f));
            }
        }

        var vertexList = new List<Vector4>();
        foreach ((float x, float radius, float side) in rings)
        {
            for (int s = 0; s < CAPSULE_SEGMENTS; s++)
            {
                double angle = 2.0 * Math.PI * s / CAPSULE_SEGMENTS;
                vertexList.Add(new Vector4(x, radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle), side));
            }
        }

        var indexList = new List<uint>();
        for (int r = 0; r + 1 < rings.Count; r++)
        {
            for (int s = 0; s < CAPSULE_SEGMENTS; s++)
            {
                uint a = (uint)(r * CAPSULE_SEGMENTS + s);
                uint b = (uint)(r * CAPSULE_SEGMENTS + (s + 1) % CAPSULE_SEGMENTS);
                uint c = a + CAPSULE_SEGMENTS;
                uint d = b + CAPSULE_SEGMENTS;
                indexList.Add(a);
                indexList.Add(c);
                indexList.Add(b);
                indexList.Add(b);
                indexList.Add(c);
                indexList.Add(d);
            }
        }

        vertices = vertexList.ToArray();
        indices = indexList.ToArray();
    }

    public static Vector4[] FromPositions(Vector3[] positions)
    {
        var vertices = new Vector4[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertices[i] = new Vector4(positions[i], 0.0f);
        }

        return vertices;
    }
}
