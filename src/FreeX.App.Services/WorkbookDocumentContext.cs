using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Owns the command infrastructure shared by every view of one workbook document.
/// View-local selection and viewport state remain in <see cref="WorkbookSession"/>.
/// </summary>
public sealed class WorkbookDocumentContext
{
    private readonly WorkbookRef _workbookRef;
    private readonly ICommandBus _commandBus;
    private readonly ICommandStackChangeNotifier? _stackChangeNotifier;

    private WorkbookDocumentContext(WorkbookRef workbookRef, ICommandBus commandBus)
    {
        _workbookRef = workbookRef;
        _commandBus = commandBus;
        _stackChangeNotifier = commandBus as ICommandStackChangeNotifier;
    }

    public Workbook CurrentWorkbook => _workbookRef.Current;

    public event EventHandler<CommandStackChangedEventArgs>? CommandStackChanged
    {
        add
        {
            if (_stackChangeNotifier is not null)
                _stackChangeNotifier.StackChanged += value;
        }
        remove
        {
            if (_stackChangeNotifier is not null)
                _stackChangeNotifier.StackChanged -= value;
        }
    }

    public static WorkbookDocumentContext Create(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var workbookRef = new WorkbookRef { Current = workbook };
        var commandBus = new CommandBus(
            _ => new WorkbookCommandContext(workbookRef.Current),
            (workbookId, context) =>
                XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(context.Workbook, out _));
        return new WorkbookDocumentContext(workbookRef, commandBus);
    }

    /// <summary>
    /// Adopts host-provided command infrastructure during incremental renderer migration.
    /// </summary>
    public static WorkbookDocumentContext Attach(
        WorkbookRef workbookRef,
        ICommandBus commandBus,
        Workbook expectedWorkbook)
    {
        ArgumentNullException.ThrowIfNull(workbookRef);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(expectedWorkbook);
        if (!ReferenceEquals(workbookRef.Current, expectedWorkbook))
        {
            throw new ArgumentException(
                "The workbook reference must target the supplied workbook.",
                nameof(workbookRef));
        }

        return new WorkbookDocumentContext(workbookRef, commandBus);
    }

    /// <summary>
    /// Creates an independent document context before one sibling view opens or creates another
    /// workbook. The existing context remains alive for the other sibling views.
    /// </summary>
    public WorkbookDocumentContext CreateDetached() => Create(CurrentWorkbook);

    public void SetCurrentWorkbook(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        _workbookRef.Current = workbook;
    }

    /// <summary>
    /// Creates a view-local session over this context's command history and workbook target.
    /// </summary>
    public WorkbookSession CreateHostOwnedSession(
        WorkbookSessionFactory sessionFactory,
        StartupWorkbookLoadResult source,
        RecalcEngine recalcEngine,
        IViewportService viewportService,
        IEnumerable<IFileAdapter> adapters,
        WorkbookDocumentState? documentState,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recalcEngine);
        ArgumentNullException.ThrowIfNull(viewportService);
        ArgumentNullException.ThrowIfNull(adapters);

        SetCurrentWorkbook(source.Workbook);
        return sessionFactory.CreateHostOwned(
            source,
            _commandBus,
            recalcEngine,
            viewportService,
            adapters,
            documentState ?? new WorkbookDocumentState(),
            viewportHeight,
            viewportWidth,
            includeObjects);
    }
}

/// <summary>
/// Mutable workbook target shared by a document context's command bus and renderer views.
/// </summary>
public sealed class WorkbookRef
{
    public Workbook Current { get; set; } = null!;
}
