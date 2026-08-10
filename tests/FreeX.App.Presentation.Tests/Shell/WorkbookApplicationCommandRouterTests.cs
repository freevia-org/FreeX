using System.Linq.Expressions;
using System.Reflection;
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
        var calls = new List<EndpointCall>();
        var bindings = CreateWorkareaBindings(calls);
        var workareaIntents = Enum.GetValues<WorkbookApplicationCommandIntent>()
            .Except(FrameIntents)
            .ToArray();

        foreach (var intent in workareaIntents)
            (await bindings.TryExecuteAsync(Route(intent))).Handled.Should().BeTrue();

        bindings.Count.Should().Be(workareaIntents.Length);
        calls.Select(call => call.EndpointName)
            .Should().Equal(workareaIntents.Select(ExpectedEndpointName));
        calls.Select(call => call.EndpointName).Distinct()
            .Should().BeEquivalentTo(
                typeof(WorkbookApplicationWorkareaCommandEndpointProfile)
                    .GetProperties()
                    .Select(property => property.Name));
    }

    [Fact]
    public async Task WorkareaBinder_BuildsSourceTargetAndNavigationPolicy()
    {
        var calls = new List<EndpointCall>();
        var bindings = CreateWorkareaBindings(calls);
        var sheetId = new SheetId(Guid.NewGuid());
        var target = new CellAddress(sheetId, 7, 11);

        await bindings.TryExecuteAsync(
            Route(
                WorkbookApplicationCommandIntent.ToggleBold,
                WorkbookApplicationCommandSource.QuickAccessToolbar),
            target);
        calls[^1].EndpointName.Should().Be(nameof(WorkbookApplicationWorkareaCommandEndpointProfile.ToggleBold));
        calls[^1].Arguments[1].Should().Be(WorkbookApplicationCommandVariant.QuickAccessToolbar);

        await bindings.TryExecuteAsync(
            Route(
                WorkbookApplicationCommandIntent.ReapplyFilter,
                WorkbookApplicationCommandSource.KeyboardShortcut),
            target);
        calls[^1].Arguments.Should().Equal(WorkbookApplicationCommandVariant.KeyboardShortcut);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.InsertRowBelow), target);
        calls[^1].EndpointName.Should().Be(nameof(WorkbookApplicationWorkareaCommandEndpointProfile.InsertRow));
        calls[^1].Arguments.Should().Equal((uint)8);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.InsertColumnRight), target);
        calls[^1].EndpointName.Should().Be(nameof(WorkbookApplicationWorkareaCommandEndpointProfile.InsertColumn));
        calls[^1].Arguments.Should().Equal((uint)12);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.ResolveThreadedComment), target);
        calls[^1].EndpointName.Should().Be(
            nameof(WorkbookApplicationWorkareaCommandEndpointProfile.SetThreadedCommentResolution));
        calls[^1].Arguments.Should().Equal(target, true);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.UnresolveThreadedComment), target);
        calls[^1].Arguments.Should().Equal(target, false);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.ActivatePreviousSheet), target);
        calls[^1].EndpointName.Should().Be(
            nameof(WorkbookApplicationWorkareaCommandEndpointProfile.ActivateAdjacentSheet));
        calls[^1].Arguments.Should().Equal(-1);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.SelectNextSheetGroup), target);
        calls[^1].EndpointName.Should().Be(
            nameof(WorkbookApplicationWorkareaCommandEndpointProfile.SelectAdjacentSheetGroup));
        calls[^1].Arguments.Should().Equal(1);

        await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.NumberFormatCurrency), target);
        calls[^1].EndpointName.Should().Be(
            nameof(WorkbookApplicationWorkareaCommandEndpointProfile.ApplyNumberFormat));
        calls[^1].Arguments.Should().Equal(NumberFormatShortcut.Currency);
    }

    [Fact]
    public async Task WorkareaBinder_SuppressesFillEffectsForDrawingSelections()
    {
        var calls = new List<EndpointCall>();
        var bindings = CreateWorkareaBindings(calls, hasSelectedDrawingObject: true);

        var result = await bindings.TryExecuteAsync(Route(WorkbookApplicationCommandIntent.FillDown));

        result.Should().Be(new WorkbookApplicationCommandExecutionResult(IsBound: true, Handled: true));
        calls.Should().BeEmpty();
    }

    [Fact]
    public void WorkareaIntentDispatchIsOwnedOnceByPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var dispatcher = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Presentation",
            "Shell",
            "WorkbookApplicationWorkareaCommandEndpoint.cs"));
        var binder = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Presentation",
            "Shell",
            "WorkbookApplicationWorkareaCommandBinder.cs"));

        dispatcher.Split("request.Intent switch", StringSplitOptions.None).Should().HaveCount(2);
        foreach (var intent in Enum.GetValues<WorkbookApplicationCommandIntent>())
            dispatcher.Should().Contain($"WorkbookApplicationCommandIntent.{intent}");

        binder.Should().Contain(
            "WorkbookApplicationWorkareaCommandDispatcher.DispatchAsync(request, handlers.Endpoints)");
        foreach (var renderer in new[] { "FreeX.App.Host", "FreeX.App.Avalonia" })
        {
            var source = File.ReadAllText(Path.Combine(
                root,
                "src",
                renderer,
                "MainWindow.ApplicationCommandRouting.cs"));

            source.Should().Contain("new WorkbookApplicationWorkareaCommandEndpointProfile")
                .And.NotContain("ExecuteWorkbookApplicationWorkareaCommandAsync")
                .And.NotContain("case WorkbookApplicationCommandIntent.")
                .And.NotContain("WorkbookApplicationCommandIntent.");
            foreach (var endpoint in typeof(WorkbookApplicationWorkareaCommandEndpointProfile).GetProperties())
                source.Should().Contain($"{endpoint.Name} =");
        }
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
        ICollection<EndpointCall> calls,
        bool hasSelectedDrawingObject = false)
    {
        var fallbackTarget = new CellAddress(new SheetId(Guid.NewGuid()), 1, 1);
        var bindings = new WorkbookApplicationCommandBindings();
        WorkbookApplicationWorkareaCommandBinder.Bind(
            bindings,
            new WorkbookApplicationWorkareaCommandHandlers(
                CreateRecordingEndpointProfile(calls),
                invocation => invocation.TargetAddress ?? fallbackTarget,
                () => hasSelectedDrawingObject));
        return bindings;
    }

    private static WorkbookApplicationWorkareaCommandEndpointProfile CreateRecordingEndpointProfile(
        ICollection<EndpointCall> calls)
    {
        var profile = new WorkbookApplicationWorkareaCommandEndpointProfile();
        var recordMethod = typeof(WorkbookApplicationCommandRouterTests).GetMethod(
            nameof(RecordEndpointCall),
            BindingFlags.Static | BindingFlags.NonPublic)!;

        foreach (var property in typeof(WorkbookApplicationWorkareaCommandEndpointProfile).GetProperties())
        {
            var invoke = property.PropertyType.GetMethod(nameof(Action.Invoke))!;
            var parameters = invoke.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            var arguments = Expression.NewArrayInit(
                typeof(object),
                parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));
            var body = Expression.Call(
                recordMethod,
                Expression.Constant(calls),
                Expression.Constant(property.Name),
                arguments);
            var endpoint = Expression.Lambda(property.PropertyType, body, parameters).Compile();
            property.SetValue(profile, endpoint);
        }

        return profile;
    }

    private static ValueTask<bool> RecordEndpointCall(
        ICollection<EndpointCall> calls,
        string endpointName,
        object?[] arguments)
    {
        calls.Add(new EndpointCall(endpointName, arguments));
        return ValueTask.FromResult(true);
    }

    private static string ExpectedEndpointName(WorkbookApplicationCommandIntent intent) =>
        intent switch
        {
            WorkbookApplicationCommandIntent.InsertRowAbove
                or WorkbookApplicationCommandIntent.InsertRowBelow =>
                nameof(WorkbookApplicationWorkareaCommandEndpointProfile.InsertRow),
            WorkbookApplicationCommandIntent.InsertColumnLeft
                or WorkbookApplicationCommandIntent.InsertColumnRight =>
                nameof(WorkbookApplicationWorkareaCommandEndpointProfile.InsertColumn),
            WorkbookApplicationCommandIntent.ResolveThreadedComment
                or WorkbookApplicationCommandIntent.UnresolveThreadedComment =>
                nameof(WorkbookApplicationWorkareaCommandEndpointProfile.SetThreadedCommentResolution),
            WorkbookApplicationCommandIntent.ActivatePreviousSheet
                or WorkbookApplicationCommandIntent.ActivateNextSheet =>
                nameof(WorkbookApplicationWorkareaCommandEndpointProfile.ActivateAdjacentSheet),
            WorkbookApplicationCommandIntent.SelectPreviousSheetGroup
                or WorkbookApplicationCommandIntent.SelectNextSheetGroup =>
                nameof(WorkbookApplicationWorkareaCommandEndpointProfile.SelectAdjacentSheetGroup),
            WorkbookApplicationCommandIntent.NumberFormatGeneral
                or WorkbookApplicationCommandIntent.NumberFormatNumber
                or WorkbookApplicationCommandIntent.NumberFormatTime
                or WorkbookApplicationCommandIntent.NumberFormatDate
                or WorkbookApplicationCommandIntent.NumberFormatCurrency
                or WorkbookApplicationCommandIntent.NumberFormatPercentage
                or WorkbookApplicationCommandIntent.NumberFormatScientific =>
                nameof(WorkbookApplicationWorkareaCommandEndpointProfile.ApplyNumberFormat),
            _ => intent.ToString(),
        };

    private sealed record EndpointCall(string EndpointName, object?[] Arguments);

    private static WorkbookApplicationCommandRoute Route(
        WorkbookApplicationCommandIntent intent,
        WorkbookApplicationCommandSource source = WorkbookApplicationCommandSource.QuickAccessToolbar) =>
        new(
            source,
            intent.ToString(),
            intent,
            WorkbookApplicationCommandAvailability.Always);
}
