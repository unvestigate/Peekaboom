using Avalonia.Platform;

namespace Peekaboom.Rendering;

public interface IViewportTargets : IDisposable
{
    int Width { get; }

    int Height { get; }

    IReadOnlyList<IntPtr> Handles { get; }
}

public interface IViewportRenderer : IDisposable
{
    string HandleType { get; }

    PlatformGraphicsExternalImageFormat ImageFormat { get; }

    bool IsLost { get; }

    IViewportTargets? CreateTargets(int width, int height);

    bool Render(IViewportTargets targets, int index, ViewportFrame? frame);
}
