using Avalonia;
using Microsoft.Extensions.Logging;
using System;

namespace Anagnostes;

class Program
{
    internal static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(
        builder => builder
            .AddDebug()
#if DEBUG
            .AddConsole()
#endif
            .SetMinimumLevel(LogLevel.Information)
    );

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var logger = LoggerFactory.CreateLogger<Program>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled application exception.");

        logger.LogInformation("Application starting.");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        logger.LogInformation("Application stopped.");
        LoggerFactory.Dispose();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
