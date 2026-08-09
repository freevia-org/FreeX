using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookApplicationCommandRouterTests
{
    [Fact]
    public void CoversEveryQuickAccessCommand()
    {
        WorkbookApplicationCommandRouter.QuickAccessRoutes.Should().HaveCount(37);
        WorkbookApplicationCommandRouter.QuickAccessRoutes
            .Select(route => route.SourceKey)
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public void CoversEveryGenericWorksheetContextMenuAction()
    {
        WorkbookApplicationCommandRouter.WorksheetContextMenuRoutes.Should().HaveCount(58);
        WorkbookApplicationCommandRouter.WorksheetContextMenuRoutes
            .Select(route => route.SourceKey)
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public void CoversEveryPortableKeyboardShortcutRoute()
    {
        foreach (var shortcut in Enum.GetValues<WorkbookShortcutRoute>())
        {
            WorkbookApplicationCommandRouter.TryRouteShortcut(shortcut, out var route).Should().BeTrue();
            route.Source.Should().Be(WorkbookApplicationCommandSource.KeyboardShortcut);
        }
    }

    [Theory]
    [InlineData("Copy", WorkbookApplicationCommandIntent.Copy)]
    [InlineData("FormatCells", WorkbookApplicationCommandIntent.OpenFormatCells)]
    [InlineData("SortAscending", WorkbookApplicationCommandIntent.SortAscending)]
    [InlineData("DataValidation", WorkbookApplicationCommandIntent.OpenDataValidation)]
    public void QuickAccessAndWorksheetActionsShareApplicationIntents(
        string sourceKey,
        WorkbookApplicationCommandIntent expectedIntent)
    {
        WorkbookApplicationCommandRouter.TryRouteQuickAccess(sourceKey, out var quickAccess).Should().BeTrue();
        WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu(sourceKey, out var worksheet).Should().BeTrue();

        quickAccess.Intent.Should().Be(expectedIntent);
        worksheet.Intent.Should().Be(expectedIntent);
    }

    [Fact]
    public void QuickAccessAvailabilityUsesPortableWorkbookContext()
    {
        WorkbookApplicationCommandRouter.TryRouteQuickAccess("Undo", out var undo).Should().BeTrue();
        WorkbookApplicationCommandRouter.TryRouteQuickAccess("Copy", out var copy).Should().BeTrue();

        WorkbookApplicationCommandRouter.CanExecute(
                undo,
                new WorkbookApplicationCommandContext(
                    CanUndo: false,
                    CanRedo: true,
                    HasActiveWorksheet: true,
                    HasSelection: true))
            .Should()
            .BeFalse();
        WorkbookApplicationCommandRouter.CanExecute(
                copy,
                new WorkbookApplicationCommandContext(
                    CanUndo: false,
                    CanRedo: false,
                    HasActiveWorksheet: true,
                    HasSelection: true))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task BindingsDispatchByPortableIntent()
    {
        var calls = new List<WorkbookApplicationCommandInvocation>();
        var bindings = new WorkbookApplicationCommandBindings();
        bindings.Bind(WorkbookApplicationCommandIntent.Copy, calls.Add);
        WorkbookApplicationCommandRouter.TryRouteQuickAccess("Copy", out var route).Should().BeTrue();

        var result = await bindings.TryExecuteAsync(route, nativeSource: "renderer");

        result.Should().Be(new WorkbookApplicationCommandExecutionResult(IsBound: true, Handled: true));
        calls.Should().ContainSingle();
        calls[0].Route.Should().BeSameAs(route);
        calls[0].NativeSource.Should().Be("renderer");
    }

    [Fact]
    public void BindingsReportMissingPortableIntents()
    {
        var bindings = new WorkbookApplicationCommandBindings();
        bindings.Bind(WorkbookApplicationCommandIntent.Copy, _ => { });

        var act = () => bindings.EnsureBound([
            new WorkbookApplicationCommandRoute(
                WorkbookApplicationCommandSource.QuickAccessToolbar,
                "Copy",
                WorkbookApplicationCommandIntent.Copy,
                WorkbookApplicationCommandAvailability.Selection),
            new WorkbookApplicationCommandRoute(
                WorkbookApplicationCommandSource.QuickAccessToolbar,
                "Paste",
                WorkbookApplicationCommandIntent.Paste,
                WorkbookApplicationCommandAvailability.Selection)
        ]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Paste*");
    }

    [Fact]
    public async Task ApplicationFrameBinder_OwnsPortableIntentRegistration()
    {
        var calls = new List<WorkbookApplicationCommandIntent>();
        var bindings = new WorkbookApplicationCommandBindings();
        Task Record(WorkbookApplicationCommandInvocation invocation)
        {
            calls.Add(invocation.Route.Intent);
            return Task.CompletedTask;
        }

        WorkbookApplicationFrameCommandBinder.Bind(
            bindings,
            new WorkbookApplicationFrameCommandHandlers(Record, Record, Record, Record, Record, Record));

        var routes = new[]
        {
            Route(WorkbookApplicationCommandIntent.NewWorkbook),
            Route(WorkbookApplicationCommandIntent.OpenWorkbook),
            Route(WorkbookApplicationCommandIntent.SaveWorkbook),
            Route(WorkbookApplicationCommandIntent.SaveWorkbookAs),
            Route(WorkbookApplicationCommandIntent.PrintWorkbook),
            Route(WorkbookApplicationCommandIntent.ExportPdfXps),
        };

        foreach (var route in routes)
            (await bindings.TryExecuteAsync(route)).Handled.Should().BeTrue();

        bindings.Count.Should().Be(6);
        calls.Should().Equal(routes.Select(route => route.Intent));
    }

    [Fact]
    public async Task ApplicationFrameBinder_OwnsPrintSourcePolicy()
    {
        var calls = new List<string>();
        var bindings = new WorkbookApplicationCommandBindings();
        Task Ignore(WorkbookApplicationCommandInvocation _) => Task.CompletedTask;

        WorkbookApplicationFrameCommandBinder.Bind(
            bindings,
            new WorkbookApplicationFrameCommandHandlers(
                Ignore,
                Ignore,
                Ignore,
                Ignore,
                _ =>
                {
                    calls.Add("print");
                    return Task.CompletedTask;
                },
                Ignore,
                _ =>
                {
                    calls.Add("backstage");
                    return Task.CompletedTask;
                }));

        await bindings.TryExecuteAsync(Route(
            WorkbookApplicationCommandIntent.PrintWorkbook,
            WorkbookApplicationCommandSource.QuickAccessToolbar));
        await bindings.TryExecuteAsync(Route(
            WorkbookApplicationCommandIntent.PrintWorkbook,
            WorkbookApplicationCommandSource.KeyboardShortcut));

        calls.Should().Equal("print", "backstage");
    }

    [Fact]
    public async Task WorkareaBinder_OwnsEveryNonFrameIntentRegistration()
    {
        var requests = new List<WorkbookApplicationWorkareaCommandRequest>();
        var bindings = CreateWorkareaBindings(requests);
        var workareaIntents = Enum.GetValues<WorkbookApplicationCommandIntent>()
            .Except(FrameIntents)
            .ToArray();

        foreach (var intent in workareaIntents)
            (await bindings.TryExecuteAsync(Route(intent))).Handled.Should().BeTrue();

        bindings.Count.Should().Be(workareaIntents.Length);
        requests.Select(request => request.Intent).Should().Equal(workareaIntents);
    }

    [Fact]
    public async Task WorkareaBinder_BuildsSourceTargetAndNavigationPolicy()
    {
        var requests = new List<WorkbookApplicationWorkareaCommandRequest>();
        var bindings = CreateWorkareaBindings(requests);
        var sheetId = new SheetId(Guid.NewGuid());
        var target = new CellAddress(sheetId, 7, 11);

        await bindings.TryExecuteAsync(
            Route(
                WorkbookApplicationCommandIntent.ToggleBold,
                WorkbookApplicationCommandSource.QuickAccessToolbar),
            target);
        requests[^1].Variant.Should().Be(WorkbookApplicationCommandVariant.QuickAccessToolbar);

        await bindings.TryExecuteAsync(
            Route(
                WorkbookApplicationCommandIntent.ReapplyFilter,
                WorkbookApplicationCommandSource.KeyboardShortcut),
            target);
        requests[^1].Variant.Should().Be(WorkbookApplicationCommandVariant.KeyboardShortcut);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.InsertRowBelow), target);
        requests[^1].Index.Should().Be(8);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.InsertColumnRight), target);
        requests[^1].Index.Should().Be(12);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.ResolveThreadedComment), target);
        requests[^1].TargetAddress.Should().Be(target);
        requests[^1].State.Should().BeTrue();

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.UnresolveThreadedComment), target);
        requests[^1].State.Should().BeFalse();

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.ActivatePreviousSheet), target);
        requests[^1].Direction.Should().Be(-1);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.SelectNextSheetGroup), target);
        requests[^1].Direction.Should().Be(1);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.NumberFormatCurrency), target);
        requests[^1].NumberFormat.Should().Be(NumberFormatShortcut.Currency);
    }

    [Fact]
    public async Task WorkareaBinder_SuppressesFillEffectsForDrawingSelections()
    {
        var requests = new List<WorkbookApplicationWorkareaCommandRequest>();
        var bindings = CreateWorkareaBindings(requests, hasSelectedDrawingObject: true);

        var result = await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.FillDown));

        result.Should().Be(new WorkbookApplicationCommandExecutionResult(IsBound: true, Handled: true));
        requests.Should().BeEmpty();
    }

    private static readonly WorkbookApplicationCommandIntent[] FrameIntents =
    [
        WorkbookApplicationCommandIntent.NewWorkbook,
        WorkbookApplicationCommandIntent.OpenWorkbook,
        WorkbookApplicationCommandIntent.SaveWorkbook,
        WorkbookApplicationCommandIntent.SaveWorkbookAs,
        WorkbookApplicationCommandIntent.PrintWorkbook,
        WorkbookApplicationCommandIntent.ExportPdfXps
    ];

    private static WorkbookApplicationCommandBindings CreateWorkareaBindings(
        ICollection<WorkbookApplicationWorkareaCommandRequest> requests,
        bool hasSelectedDrawingObject = false)
    {
        var fallbackTarget = new CellAddress(new SheetId(Guid.NewGuid()), 1, 1);
        var bindings = new WorkbookApplicationCommandBindings();
        WorkbookApplicationWorkareaCommandBinder.Bind(
            bindings,
            new WorkbookApplicationWorkareaCommandHandlers(
                request =>
                {
                    requests.Add(request);
                    return ValueTask.FromResult(true);
                },
                invocation => invocation.TargetAddress ?? fallbackTarget,
                () => hasSelectedDrawingObject));
        return bindings;
    }

    private static WorkbookApplicationCommandRoute Route(
        WorkbookApplicationCommandIntent intent,
        WorkbookApplicationCommandSource source = WorkbookApplicationCommandSource.QuickAccessToolbar) =>
        new(
            source,
            intent.ToString(),
            intent,
            WorkbookApplicationCommandAvailability.Always);
}
