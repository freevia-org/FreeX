using FreeX.App.Presentation.Shell;
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
}
