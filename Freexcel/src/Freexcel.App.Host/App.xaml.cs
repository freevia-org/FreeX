using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Freexcel.Core.IO;

namespace Freexcel.App.Host;

/// <summary>
/// Application entry point and composition root.
/// Configures DI, Serilog, and shows the main window.
/// </summary>
public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/Freexcel-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("Freexcel starting up");

        // Configure DI
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        // Show main window
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        Log.Information("Freexcel ready");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        // Core services
        services.AddSingleton<DependencyGraph>();
        services.AddSingleton<FormulaEvaluator>();
        services.AddSingleton<RecalcEngine>();
        services.AddSingleton<IViewportService, ViewportService>();
        services.AddSingleton<IFileAdapter, XlsxFileAdapter>();
        services.AddSingleton<IFileAdapter, NativeJsonAdapter>();

        // Workbook (single workbook for now, will expand later)
        services.AddSingleton(sp =>
        {
            var workbook = new Workbook("Untitled");
            workbook.AddSheet("Sheet1");
            return workbook;
        });

        // Command bus
        services.AddSingleton<ICommandBus>(sp =>
        {
            var workbook = sp.GetRequiredService<Workbook>();
            return new CommandBus(wbId => new WorkbookCommandContext(workbook));
        });

        // UI
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Freexcel shutting down");
        Log.CloseAndFlush();
        Services.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Simple command context that provides access to the workbook.</summary>
internal sealed class WorkbookCommandContext : ICommandContext
{
    public Workbook Workbook { get; }

    public WorkbookCommandContext(Workbook workbook) => Workbook = workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
}
