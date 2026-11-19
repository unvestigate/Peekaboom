using Dock.Model.Mvvm.Controls;

using Peekaboom.Logging;
using Peekaboom.Rendering;

namespace Peekaboom.Docking;

public sealed class ViewportViewModel : Document
{
    public LogViewModel? Log { get; set; }

    public SceneDrawList? DrawList { get; private set; }

    public RenderMode RenderMode { get; private set; } = RenderMode.Solid;

    public bool ShowGrid { get; private set; } = true;

    public bool LeftHanded { get; private set; }

    public IReadOnlySet<ulong> Selection { get; private set; } = new HashSet<ulong>();

    public DisplayOptions Display { get; private set; } = new();

    public void SetDisplay(DisplayOptions display)
    {
        Display = display;
        Changed?.Invoke();
    }

    public event Action? Changed;

    public event Action<DrawItem?, bool>? Picked;

    public void SetSelection(IReadOnlySet<ulong> selection)
    {
        Selection = selection;
        Changed?.Invoke();
    }

    public void ReportPick(DrawItem? item, bool selectShape)
    {
        Picked?.Invoke(item, selectShape);
    }

    public event Action? FrameSceneRequested;

    public void SetDrawList(SceneDrawList? drawList)
    {
        DrawList = drawList;
        Changed?.Invoke();
    }

    public void SetRenderMode(RenderMode mode)
    {
        RenderMode = mode;
        Changed?.Invoke();
    }

    public void SetShowGrid(bool show)
    {
        ShowGrid = show;
        Changed?.Invoke();
    }

    public void SetLeftHanded(bool leftHanded)
    {
        LeftHanded = leftHanded;
        Changed?.Invoke();
    }

    public void FrameScene()
    {
        FrameSceneRequested?.Invoke();
    }
}
