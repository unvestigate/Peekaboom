using System.Diagnostics;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

using Peekaboom.Core;

namespace Peekaboom.UI;

public partial class TimelineBar : UserControl
{
    private static readonly double[] Speeds = { 0.1, 0.25, 0.5, 1.0, 2.0, 4.0 };
    private static readonly TimeSpan PlayTick = TimeSpan.FromMilliseconds(15.0);

    private readonly List<SceneTimeline> mScenes = new();
    private readonly List<string> mSceneNames = new();
    private readonly DispatcherTimer mPlayTimer;
    private readonly Stopwatch mPlayClock = Stopwatch.StartNew();

    private int mGeneration = -1;
    private bool mIsLive;
    private int mSceneOrdinal = -1;
    private int mFrameIndex = -1;
    private int mShownIndex = -1;
    private bool mFollow = true;
    private bool mPlaying;
    private double mPlayTime;
    private double mLastTick;
    private double mSpeed = 1.0;
    private bool mPreSimulation;
    private bool mUpdating;

    public TimelineBar()
    {
        InitializeComponent();

        var speedNames = new List<string>();
        foreach (double speed in Speeds)
        {
            speedNames.Add(speed.ToString("0.##", CultureInfo.InvariantCulture) + "x");
        }

        SpeedBox.ItemsSource = speedNames;

        mPlayTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = PlayTick };
        mPlayTimer.Tick += (_, _) => OnPlayTick();

        SceneBox.SelectionChanged += (_, _) => OnSceneSelected();
        SpeedBox.SelectionChanged += (_, _) => OnSpeedSelected();
        FirstButton.Click += (_, _) => StepTo(0);
        StepBackButton.Click += (_, _) => StepTo(mFrameIndex - 1);
        PlayButton.Click += (_, _) => TogglePlay();
        StepForwardButton.Click += (_, _) => StepTo(mFrameIndex + 1);
        LastButton.Click += (_, _) => StepTo(int.MaxValue);
        FollowButton.Click += (_, _) => SetFollow(FollowButton.IsChecked == true);
        PreSimulationBox.Click += (_, _) => OnPreSimulationChanged();
        FrameSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                OnSliderMoved();
            }
        };

        ApplySettings(1.0, false);
        UpdateControls();
    }

    public event Action<FrameRequest>? FrameRequested;

    public event Action? SettingsChanged;

    public double Speed => mSpeed;

    public bool PreSimulation => mPreSimulation;

    public void ApplySettings(double speed, bool preSimulation)
    {
        mUpdating = true;

        int speedIndex = Array.IndexOf(Speeds, 1.0);
        for (int i = 0; i < Speeds.Length; i++)
        {
            if (Math.Abs(Speeds[i] - speed) < 1e-6)
            {
                speedIndex = i;
            }
        }

        mSpeed = Speeds[speedIndex];
        SpeedBox.SelectedIndex = speedIndex;
        mPreSimulation = preSimulation;
        PreSimulationBox.IsChecked = preSimulation;

        mUpdating = false;
    }

    public void ApplyTimeline(TimelineUpdate update)
    {
        if (update.Reset || update.Generation != mGeneration)
        {
            mGeneration = update.Generation;
            mScenes.Clear();
            mSceneOrdinal = -1;
            mFrameIndex = -1;
            mShownIndex = -1;
            StopPlaying();
            mFollow = update.IsLive;
        }

        mIsLive = update.IsLive;
        int selectedCount = SelectedScene?.Times.Count ?? 0;

        foreach (SceneUpdate sceneUpdate in update.Scenes)
        {
            while (mScenes.Count <= sceneUpdate.Ordinal)
            {
                mScenes.Add(new SceneTimeline());
            }

            SceneTimeline scene = mScenes[sceneUpdate.Ordinal];
            scene.Name = sceneUpdate.Name;
            scene.IsAlive = sceneUpdate.IsAlive;
            scene.Times.AddRange(sceneUpdate.NewTimes);
        }

        RefreshSceneBox();

        if (mSceneOrdinal < 0 && mScenes.Count > 0)
        {
            mSceneOrdinal = 0;
            SetSceneBoxSelection();
            Request(0, mIsLive && mFollow);
        }
        else if (mIsLive && mFollow && (SelectedScene?.Times.Count ?? 0) != selectedCount)
        {
            Request(0, true);
        }

        UpdateControls();
    }

    public void ShowFrame(FrameResult result)
    {
        if (result.Generation != mGeneration || result.SceneOrdinal != mSceneOrdinal)
        {
            return;
        }

        mShownIndex = result.FrameIndex;
        if (mFollow && mIsLive && result.FrameIndex >= 0)
        {
            mFrameIndex = result.FrameIndex;
        }

        UpdateControls();
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                return true;
            case Key.Left:
                StepTo(mFrameIndex - 1);
                return true;
            case Key.Right:
                StepTo(mFrameIndex + 1);
                return true;
            case Key.Home:
                StepTo(0);
                return true;
            case Key.End:
                StepTo(int.MaxValue);
                return true;
            default:
                return false;
        }
    }

    private SceneTimeline? SelectedScene => mSceneOrdinal >= 0 && mSceneOrdinal < mScenes.Count ? mScenes[mSceneOrdinal] : null;

    private void RefreshSceneBox()
    {
        bool changed = mSceneNames.Count != mScenes.Count;
        for (int i = 0; !changed && i < mScenes.Count; i++)
        {
            changed = mSceneNames[i] != SceneLabel(mScenes[i]);
        }

        if (!changed)
        {
            return;
        }

        mSceneNames.Clear();
        foreach (SceneTimeline scene in mScenes)
        {
            mSceneNames.Add(SceneLabel(scene));
        }

        mUpdating = true;
        SceneBox.ItemsSource = new List<string>(mSceneNames);
        SceneBox.SelectedIndex = mSceneOrdinal;
        mUpdating = false;
    }

    private static string SceneLabel(SceneTimeline scene)
    {
        return scene.Name;
    }

    private void SetSceneBoxSelection()
    {
        mUpdating = true;
        SceneBox.SelectedIndex = mSceneOrdinal;
        mUpdating = false;
    }

    private void Request(int index, bool latest)
    {
        SceneTimeline? scene = SelectedScene;
        if (scene == null)
        {
            return;
        }

        int count = scene.Times.Count;
        mFrameIndex = count == 0 ? -1 : latest ? count - 1 : Math.Clamp(index, 0, count - 1);

        FrameRequested?.Invoke(new FrameRequest
        {
            SceneOrdinal = mSceneOrdinal,
            FrameIndex = latest ? -1 : Math.Max(0, mFrameIndex),
            PreSimulation = mPreSimulation,
        });

        UpdateControls();
    }

    private void StepTo(int index)
    {
        SceneTimeline? scene = SelectedScene;
        if (scene == null || scene.Times.Count == 0)
        {
            return;
        }

        StopPlaying();
        SetFollow(false);
        Request(index, false);
    }

    private void SetFollow(bool follow)
    {
        if (mFollow == follow && FollowButton.IsChecked == follow)
        {
            return;
        }

        mFollow = follow;
        mUpdating = true;
        FollowButton.IsChecked = mIsLive && follow;
        mUpdating = false;

        if (follow)
        {
            StopPlaying();
            Request(0, true);
        }
    }

    private void TogglePlay()
    {
        if (mPlaying)
        {
            StopPlaying();
            return;
        }

        SceneTimeline? scene = SelectedScene;
        if (scene == null || scene.Times.Count < 2)
        {
            return;
        }

        SetFollow(false);

        if (mFrameIndex >= scene.Times.Count - 1 && !mIsLive)
        {
            Request(0, false);
        }

        mPlayTime = scene.Times[Math.Max(0, mFrameIndex)];
        mLastTick = mPlayClock.Elapsed.TotalSeconds;
        mPlaying = true;
        PlayIcon.Data = Icons.Pause;
        mPlayTimer.Start();
    }

    private void StopPlaying()
    {
        if (!mPlaying)
        {
            return;
        }

        mPlaying = false;
        mPlayTimer.Stop();
        PlayIcon.Data = Icons.Play;
    }

    private void OnPlayTick()
    {
        SceneTimeline? scene = SelectedScene;
        if (!mPlaying || scene == null || scene.Times.Count == 0)
        {
            StopPlaying();
            return;
        }

        double now = mPlayClock.Elapsed.TotalSeconds;
        mPlayTime += (now - mLastTick) * mSpeed;
        mLastTick = now;

        List<double> times = scene.Times;
        int index = FindFrame(times, mPlayTime);
        if (index >= times.Count - 1)
        {
            index = times.Count - 1;
            if (!mIsLive)
            {
                StopPlaying();
            }
        }

        if (index != mFrameIndex)
        {
            Request(index, false);
        }
    }

    private static int FindFrame(List<double> times, double time)
    {
        int low = 0;
        int high = times.Count - 1;

        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (times[middle] <= time)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private void OnSceneSelected()
    {
        if (mUpdating || SceneBox.SelectedIndex < 0 || SceneBox.SelectedIndex == mSceneOrdinal)
        {
            return;
        }

        double time = CurrentTime();
        mSceneOrdinal = SceneBox.SelectedIndex;
        mShownIndex = -1;

        SceneTimeline? scene = SelectedScene;
        if (scene == null)
        {
            return;
        }

        if (mIsLive && mFollow)
        {
            Request(0, true);
        }
        else
        {
            int index = scene.Times.Count > 0 ? FindFrame(scene.Times, time) : 0;
            Request(index, false);
            if (mPlaying)
            {
                mPlayTime = scene.Times.Count > 0 ? scene.Times[index] : 0.0;
            }
        }
    }

    private double CurrentTime()
    {
        SceneTimeline? scene = SelectedScene;
        if (scene == null || mFrameIndex < 0 || mFrameIndex >= scene.Times.Count)
        {
            return 0.0;
        }

        return scene.Times[mFrameIndex];
    }

    private void OnSpeedSelected()
    {
        if (mUpdating || SpeedBox.SelectedIndex < 0)
        {
            return;
        }

        mSpeed = Speeds[SpeedBox.SelectedIndex];
        SettingsChanged?.Invoke();
    }

    private void OnPreSimulationChanged()
    {
        mPreSimulation = PreSimulationBox.IsChecked == true;
        SettingsChanged?.Invoke();
        Request(mFrameIndex, mIsLive && mFollow);
    }

    private void OnSliderMoved()
    {
        if (mUpdating)
        {
            return;
        }

        int index = (int)Math.Round(FrameSlider.Value);
        if (index == mFrameIndex)
        {
            return;
        }

        SetFollow(false);
        Request(index, false);

        SceneTimeline? scene = SelectedScene;
        if (mPlaying && scene != null && mFrameIndex >= 0)
        {
            mPlayTime = scene.Times[mFrameIndex];
        }
    }

    private void UpdateControls()
    {
        SceneTimeline? scene = SelectedScene;
        int count = scene?.Times.Count ?? 0;

        mUpdating = true;
        FrameSlider.Maximum = Math.Max(0, count - 1);
        FrameSlider.Value = Math.Max(0, mFrameIndex);
        mUpdating = false;

        mUpdating = true;
        FollowButton.IsEnabled = mIsLive;
        FollowButton.IsChecked = mIsLive && mFollow;
        mUpdating = false;

        bool hasFrames = count > 0;
        FrameSlider.IsEnabled = count > 1;
        FirstButton.IsEnabled = hasFrames;
        StepBackButton.IsEnabled = hasFrames;
        PlayButton.IsEnabled = count > 1;
        StepForwardButton.IsEnabled = hasFrames;
        LastButton.IsEnabled = hasFrames;
        SceneBox.IsEnabled = mScenes.Count > 0;

        if (scene == null || count == 0)
        {
            FrameText.Text = mScenes.Count == 0 ? "No frames" : "No frames in this scene";
            return;
        }

        int shown = mShownIndex >= 0 ? mShownIndex : mFrameIndex;
        string time = shown >= 0 && shown < count ? scene.Times[shown].ToString("0.000", CultureInfo.InvariantCulture) : "-";
        FrameText.Text = "Frame " + Math.Max(0, shown) + " / " + (count - 1) + "   " + time + " s" + (mIsLive ? "   live" : "");
    }

    private sealed class SceneTimeline
    {
        public string Name = "";
        public bool IsAlive = true;
        public readonly List<double> Times = new();
    }
}
