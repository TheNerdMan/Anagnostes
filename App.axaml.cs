using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;

namespace Anagnostes;

public partial class App : Application
{
    private readonly ILogger<App> _logger = Program.LoggerFactory.CreateLogger<App>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _logger.LogInformation("Desktop lifetime initialized.");
            desktop.MainWindow = new MainWindow(Program.LoggerFactory);
        }

        base.OnFrameworkInitializationCompleted();
    }
}