using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FreeX.Core.IO;
using FreeX.App.UI;

namespace FreeX.App.Host;

/// <summary>
/// Application entry point and composition root.
/// Configures DI, Serilog, and shows the main window.
/// </summary>
public partial class App : Application
{
    private static FreeXOptions? _startupOptions;

    public static ServiceProvider Services { get; private set; } = null!;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var options = FreeXOptions.Load();
        AppLocalization.ApplyAppLanguage(options.AppLanguage);
        AppLocalization.ApplyCurrentCultureToWpf();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File("logs/FreeX-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("FreeX starting up");

        // Configure DI
        var serviceCollection = new ServiceCollection();
        _startupOptions = options;
        try
        {
            ConfigureServices(serviceCollection);
        }
        finally
        {
            _startupOptions = null;
        }
        Services = serviceCollection.BuildServiceProvider();
        var crashAnalytics = Services.GetRequiredService<ICrashAnalytics>();
        var crashAnalyticsOptions = Services.GetRequiredService<AppCrashAnalyticsOptions>();
        var diagnosticsMetadata = Services.GetRequiredService<AppDiagnosticsMetadata>();
        PromptForCrashAnalyticsConsentIfNeeded(options, crashAnalyticsOptions);
        if (options.CrashAnalyticsEnabled != crashAnalyticsOptions.IsEnabled)
        {
            crashAnalyticsOptions = AppCrashAnalyticsOptions.CreateDefault(options.CrashAnalyticsEnabled);
        }

        crashAnalytics.Initialize(crashAnalyticsOptions, diagnosticsMetadata);
        var diagnostics = Services.GetRequiredService<IAppDiagnostics>();
        RegisterCrashHandlers(diagnostics);
        diagnostics.RecordEvent("app_start");

        // Show main window
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        diagnostics.RecordEvent("app_ready");
        Log.Information("FreeX ready");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        var options = _startupOptions ?? FreeXOptions.Load();
        services.AddSingleton(options);

        // Local tester diagnostics. No network upload; files stay under LocalAppData.
        services.AddSingleton(AppDiagnosticsOptions.CreateDefault());
        services.AddSingleton(AppCrashAnalyticsOptions.CreateDefault(options.CrashAnalyticsEnabled));
        services.AddSingleton(AppDiagnosticsMetadata.Create(AppInfo.VersionText));
        services.AddSingleton<AppDiagnosticsFileStore>();
        services.AddSingleton<ICrashAnalytics, SentryCrashAnalytics>();
        services.AddSingleton<IAppDiagnostics, AppDiagnostics>();

        // Core services
        services.AddSingleton<DependencyGraph>();
        services.AddSingleton<FormulaEvaluator>();
        services.AddSingleton<RecalcEngine>();
        services.AddSingleton<IViewportService, ViewportService>();
        services.AddSingleton<IFileAdapter, XlsxFileAdapter>();
        services.AddSingleton<IFileAdapter, LegacyXlsFileAdapter>();
        services.AddSingleton<IFileAdapter, CsvFileAdapter>();
        services.AddSingleton<IFileAdapter>(_ => new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t'));
        services.AddSingleton<IFileAdapter>(_ => new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t'));
        services.AddSingleton<IFileAdapter>(_ => new DelimitedTextFileAdapter(".tab", "Tab-delimited text", '\t'));
        services.AddSingleton<IFileAdapter, SpreadsheetXmlFileAdapter>();
        services.AddSingleton<IFileAdapter, NativeJsonAdapter>();

        // Workbook (single workbook for now, will expand later)
        services.AddSingleton(sp =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            return workbook;
        });

        // Mutable reference wrapper — updated whenever a new file is loaded.
        services.AddSingleton(sp =>
            new WorkbookRef { Current = sp.GetRequiredService<Workbook>() });

        // Command bus always resolves through WorkbookRef so it sees the current workbook.
        services.AddSingleton<ICommandBus>(sp =>
        {
            var wbRef = sp.GetRequiredService<WorkbookRef>();
            return new CommandBus(_ => new WorkbookCommandContext(wbRef.Current));
        });

        // Message service
        services.AddSingleton<IUserMessageService, WpfUserMessageService>();

        // UI
        services.AddTransient<MainWindow>();
    }

    private static void RegisterCrashHandlers(IAppDiagnostics diagnostics)
    {
        Current.DispatcherUnhandledException += (_, args) =>
        {
            diagnostics.RecordCrash(args.Exception, "dispatcher");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                diagnostics.RecordCrash(exception, "appdomain");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            diagnostics.RecordCrash(args.Exception, "task");
        };
    }

    private static void PromptForCrashAnalyticsConsentIfNeeded(
        FreeXOptions options,
        AppCrashAnalyticsOptions crashAnalyticsOptions)
    {
        if (!CrashAnalyticsConsentPlanner.ShouldPrompt(options, crashAnalyticsOptions))
            return;

        // Use MessageBox directly here: IUserMessageService is not yet available at this early
        // startup point (before the main window is shown), so we fall back to a raw call.
        var result = MessageBox.Show(
            UiText.Get("Startup_CrashReportsConsentPrompt"),
            UiText.Get("Startup_CrashReportsTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        CrashAnalyticsConsentPlanner.ApplyConsent(options, result == MessageBoxResult.Yes);
        options.Save();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.GetService<IAppDiagnostics>()?.RecordEvent("app_exit", new Dictionary<string, string?>
        {
            ["status"] = e.ApplicationExitCode.ToString()
        });
        Log.Information("FreeX shutting down");
        Log.CloseAndFlush();
        Services.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Mutable holder for the active workbook, updated on file open.</summary>
public sealed class WorkbookRef
{
    public Workbook Current { get; set; } = null!;
}

/// <summary>Simple command context that provides access to the workbook.</summary>
internal sealed class WorkbookCommandContext : ICommandContext
{
    public Workbook Workbook { get; }

    public WorkbookCommandContext(Workbook workbook) => Workbook = workbook;

    public Sheet GetSheet(SheetId sheetId) =>
        Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
}
