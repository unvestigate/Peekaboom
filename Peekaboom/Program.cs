using System.Diagnostics;
using System.Globalization;

using Avalonia;

namespace Peekaboom;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        if (Environment.GetEnvironmentVariable("PEEKABOOM_TRACE") != null)
        {
            Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
            Trace.AutoFlush = true;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software },
            })
            .WithInterFont()
            .LogToTrace();
    }
}
