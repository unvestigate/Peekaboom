using System.Numerics;

namespace Peekaboom.UI;

public sealed class ViewportCamera
{
    private const float NEAR_PLANE = 0.05f;
    private const float MIN_DISTANCE = 0.05f;
    private const float MAX_PITCH = 1.55f;
    private const float FRAME_MARGIN = 1.15f;

    public Vector3 Target { get; set; } = Vector3.Zero;

    public float Yaw { get; set; } = MathF.PI * 0.25f;

    public float Pitch { get; set; } = 0.45f;

    public float Distance { get; set; } = 25.0f;

    public float FieldOfView { get; set; } = MathF.PI / 3.0f;

    public Vector3 Forward
    {
        get
        {
            float cosPitch = MathF.Cos(Pitch);
            return new Vector3(-MathF.Sin(Yaw) * cosPitch, -MathF.Sin(Pitch), -MathF.Cos(Yaw) * cosPitch);
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

    public Vector3 Up => Vector3.Cross(Right, Forward);

    public Vector3 Position => Target - Forward * Distance;

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    public Matrix4x4 Projection(float aspect)
    {
        float focal = 1.0f / MathF.Tan(FieldOfView * 0.5f);
        var projection = new Matrix4x4();
        projection.M11 = focal / Math.Max(aspect, 1e-4f);
        projection.M22 = focal;
        projection.M34 = -1.0f;
        projection.M43 = NEAR_PLANE;
        return projection;
    }

    public void Orbit(float yawDelta, float pitchDelta)
    {
        Yaw += yawDelta;
        Pitch = Math.Clamp(Pitch + pitchDelta, -MAX_PITCH, MAX_PITCH);
    }

    public void OrbitAround(Vector3 pivot, float yawDelta, float pitchDelta)
    {
        Vector3 offset = Position - pivot;
        float right = Vector3.Dot(offset, Right);
        float up = Vector3.Dot(offset, Up);
        float forward = Vector3.Dot(offset, Forward);

        Orbit(yawDelta, pitchDelta);

        Vector3 position = pivot + Right * right + Up * up + Forward * forward;
        Target = position + Forward * Distance;
    }

    public float DepthOf(Vector3 point)
    {
        return Vector3.Dot(point - Position, Forward);
    }

    public void Look(float yawDelta, float pitchDelta)
    {
        Vector3 position = Position;
        Orbit(yawDelta, pitchDelta);
        Target = position + Forward * Distance;
    }

    public void Pan(float dx, float dy, float viewHeight, float depth)
    {
        float unitsPerPixel = 2.0f * Math.Max(depth, MIN_DISTANCE) * MathF.Tan(FieldOfView * 0.5f) / Math.Max(viewHeight, 1.0f);
        Target += (-Right * dx + Up * dy) * unitsPerPixel;
    }

    public void Dolly(float steps)
    {
        Distance = Math.Max(MIN_DISTANCE, Distance * MathF.Pow(0.85f, steps));
    }

    public void Move(Vector3 local, float amount)
    {
        Vector3 delta = Right * local.X + Vector3.UnitY * local.Y + Forward * local.Z;
        Target += delta * amount;
    }

    public void MirrorZ()
    {
        Target = new Vector3(Target.X, Target.Y, -Target.Z);
        Yaw = MathF.PI - Yaw;
    }

    public void Frame(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        float radius = Math.Max((max - min).Length() * 0.5f, 0.5f);
        Target = center;
        Distance = radius / MathF.Sin(FieldOfView * 0.5f) * FRAME_MARGIN;
    }
}
