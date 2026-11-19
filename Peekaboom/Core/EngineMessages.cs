using Peekaboom.Logging;
using Peekaboom.Model;
using Peekaboom.Model.Physics;
using Peekaboom.Rendering;

namespace Peekaboom.Core;

public sealed class FrameRequest
{
    public int SceneOrdinal { get; init; }

    public int FrameIndex { get; init; }

    public bool PreSimulation { get; init; }

    public bool Latest => FrameIndex < 0;
}

public sealed class FrameResult
{
    public int Generation { get; init; }

    public int SceneOrdinal { get; init; }

    public int FrameIndex { get; init; }

    public bool PreSimulation { get; init; }

    public ObjectDb? State { get; init; }

    public SceneSnapshot? Snapshot { get; init; }

    public SceneDrawList? DrawList { get; init; }
}

public sealed class SceneUpdate
{
    public int Ordinal { get; init; }

    public ulong Id { get; init; }

    public string Name { get; init; } = "";

    public bool IsAlive { get; init; }

    public double[] NewTimes { get; init; } = Array.Empty<double>();
}

public sealed class TimelineUpdate
{
    public int Generation { get; init; }

    public bool Reset { get; init; }

    public bool IsLive { get; init; }

    public string Source { get; init; } = "";

    public List<SceneUpdate> Scenes { get; init; } = new();
}

public sealed class EngineStatus
{
    public bool Listening { get; init; }

    public int Port { get; init; }

    public bool Connected { get; init; }

    public string Remote { get; init; } = "";

    public long BytesReceived { get; init; }

    public string Source { get; init; } = "";

    public bool Loading { get; init; }
}

public readonly record struct EngineMessage(LogSeverity Severity, string Text);
