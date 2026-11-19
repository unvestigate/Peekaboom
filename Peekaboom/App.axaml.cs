using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Peekaboom;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            string[]? args = desktop.Args;
            if (args != null && args.Length > 0 && File.Exists(args[0]))
            {
                window.OpenCapture(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
