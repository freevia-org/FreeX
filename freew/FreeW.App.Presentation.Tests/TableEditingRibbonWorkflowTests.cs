using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class TableEditingRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersEveryOwnedActionAndPreparesEachExecution()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events));

        var commands = builder.Build().Commands;
        TableEditingRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(41);
        TableEditingRibbonWorkflow.Actions.Should().OnlyContain(
            action => FreeWRibbonEditorExecutionProfile.TableActions.Contains(action));

        foreach (var action in TableEditingRibbonWorkflow.Actions)
        {
            commands.Should().ContainKey(action);
            commands[action].Execute(RibbonCommandContext.Empty);
        }

        events.Count(entry => entry == "prepare").Should().Be(TableEditingRibbonWorkflow.Actions.Count);
    }

    [Fact]
    public void SharedWorkflowOwnsAutoFitAlignmentAndTextDirectionMappings()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events));
        var commands = builder.Build().Commands;

        Execute(commands, FreeWRibbonCommandAction.TableAutofitContents);
        Execute(commands, FreeWRibbonCommandAction.TableAutofitWindow);
        Execute(commands, FreeWRibbonCommandAction.TableAutofitFixed);
        Execute(commands, FreeWRibbonCommandAction.CellAlignTopLeft);
        Execute(commands, FreeWRibbonCommandAction.CellAlignMiddleCenter);
        Execute(commands, FreeWRibbonCommandAction.CellAlignBottomRight);
        Execute(commands, FreeWRibbonCommandAction.CellTextDirectionHorizontal);
        Execute(commands, FreeWRibbonCommandAction.CellTextDirectionRotate90);
        Execute(commands, FreeWRibbonCommandAction.CellTextDirectionRotate270);

        events.Where(entry => entry != "prepare").Should().Equal(
            "autofit:Contents",
            "autofit:Window",
            "autofit:Fixed",
            "align:Top:Left",
            "align:Center:Center",
            "align:Bottom:Right",
            "direction:Horizontal",
            "direction:Rotate90",
            "direction:Rotate270");
    }

    [Fact]
    public void SharedWorkflowOwnsInsertMergeSplitShadingAndBordersCommands()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events));
        var commands = builder.Build().Commands;

        Execute(commands, FreeWRibbonCommandAction.TableInsertBelow);
        Execute(commands, FreeWRibbonCommandAction.TableInsertColRight);
        Execute(commands, FreeWRibbonCommandAction.TableMergeCells);
        Execute(commands, FreeWRibbonCommandAction.TableSplitCell);
        Execute(commands, FreeWRibbonCommandAction.TableShading);
        Execute(commands, FreeWRibbonCommandAction.TableBorders);

        events.Should().Equal(
            "prepare", "insert-row-below",
            "prepare", "insert-column-right",
            "prepare", "merge-cells",
            "prepare", "split-cell",
            "prepare", "shading",
            "prepare", "borders");
    }

    [Theory]
    [InlineData(FreeWRibbonCommandAction.TableHeaderRow, true)]
    [InlineData(FreeWRibbonCommandAction.TableBandedRows, false)]
    [InlineData(FreeWRibbonCommandAction.TableLastRow, true)]
    [InlineData(FreeWRibbonCommandAction.TableFirstColumn, false)]
    [InlineData(FreeWRibbonCommandAction.TableLastColumn, true)]
    [InlineData(FreeWRibbonCommandAction.TableBandedCols, false)]
    [InlineData(FreeWRibbonCommandAction.TableViewGridlines, true)]
    [InlineData(FreeWRibbonCommandAction.TableRepeatHeader, true)]
    public void TableTogglesPublishLiveCheckedStateAndDisableOutsideTables(
        FreeWRibbonCommandAction action,
        bool expectedChecked)
    {
        var events = new List<string>();
        var state = new TableToggleStateSource
        {
            Formatting = new TableFormatting
            {
                HeaderRow = true,
                BandedRows = false,
                LastRow = true,
                FirstColumn = false,
                LastColumn = true,
                BandedColumns = false,
                RepeatHeaderRow = true,
            },
            ViewGridlines = true,
        };
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events, state));
        var command = builder.Build().Commands[action]
            .Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;

        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: expectedChecked));

        state.Formatting = null;

        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: false, IsChecked: false));
    }

    [Fact]
    public void SharedWorkflowOwnsEveryFixedBorderPresetAndClearSemantics()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events));
        var commands = builder.Build().AdapterCommands!;

        foreach (var commandId in new[]
                 {
                     "freew.table-borders.all",
                     "freew.table-borders.outside",
                     "freew.table-borders.inside",
                     "freew.table-borders.none",
                     "freew.table-borders.top",
                     "freew.table-borders.bottom",
                     "freew.table-borders.left",
                     "freew.table-borders.right",
                 })
        {
            commands[new RibbonCommandId(commandId)].Execute(RibbonCommandContext.Empty);
        }

        events.Should().Equal(
            "prepare", "border:All:False",
            "prepare", "border:Outside:False",
            "prepare", "border:Inside:False",
            "prepare", "border:All:True",
            "prepare", "border:Top:False",
            "prepare", "border:Bottom:False",
            "prepare", "border:Left:False",
            "prepare", "border:Right:False");
    }

    [Fact]
    public void TableStyleWorkflowOwnsCatalogPreviewCancelAndCommitLifecycle()
    {
        var events = new List<string>();
        var registry = new RibbonCommandRegistry();
        TableStyleRibbonWorkflow.Register(
            registry,
            new TableStyleRibbonPorts(
                style => events.Add($"preview:{style.WordStyleId}"),
                () => events.Add("cancel"),
                style => events.Add($"commit:{style.WordStyleId}")));

        registry.TryGet(TableStyleRibbonWorkflow.ParentCommandId, out var parent).Should().BeTrue();
        parent.Should().NotBeNull();
        for (var index = 0; index < DocumentTableStyle.Catalog.Count; index++)
        {
            var style = DocumentTableStyle.Catalog[index];
            registry.TryGet(FreeWContextMenuPlanner.TableStylesPrefix + index, out var command)
                .Should().BeTrue();
            var preview = command.Should().BeAssignableTo<IRibbonPreviewCommand>().Subject;
            preview.BeginPreview(RibbonCommandContext.Empty);
            preview.CancelPreview();
            preview.Execute(RibbonCommandContext.Empty);
            events.TakeLast(3).Should().Equal(
                $"preview:{style.WordStyleId}",
                "cancel",
                $"commit:{style.WordStyleId}");
        }
    }

    [Fact]
    public void BothRenderersDelegateTableEditingPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        var wpfEditor = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));
        var avaloniaEditor = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));
        var wpfGallery = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "TableStylesGallery.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableEditingRibbonWorkflow.Register(");
            source.Should().Contain("CreateTableEditingPorts(");
            source.Should().Contain("CurrentTableFormatting: () => editor.CaretTableContext()?.Table.Formatting");
            source.Should().Contain("ViewGridlines: () => editor.");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableHeaderRow");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableSelectTable");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableAutofitContents");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.CellAlignTopLeft");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.CellTextDirectionHorizontal");
            source.Should().NotContain("RegisterTableBorderCommands");
            source.Should().NotContain("freew.table-borders.all");
        }

        wpf.Should().NotContain("freew.table-insert-row")
            .And.NotContain("freew.table-insert-col\"")
            .And.NotContain("freew.merge-cells")
            .And.NotContain("freew.split-cell")
            .And.NotContain("freew.cell-shading")
            .And.NotContain("freew.cell-borders");
        avalonia.Should().NotContain("tableCommands.Register(\"freew.table-insert-below\"")
            .And.NotContain("tableCommands.Register(\"freew.table-insert-col-right\"")
            .And.NotContain("tableCommands.Register(\"freew.table-merge-cells\"")
            .And.NotContain("tableCommands.Register(\"freew.table-split-cell\"")
            .And.NotContain("tableCommands.Register(\"freew.table-shading\"")
            .And.NotContain("tableCommands.Register(\"freew.table-borders\"");
        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableStyleRibbonWorkflow.Register(")
                .And.Contain("editor.PreviewTableStyle")
                .And.Contain("editor.CommitTableStylePreview");
        }
        wpfEditor.Should().Contain("_editingSession.TableStylePreview")
            .And.NotContain("_tableStyleSnapshot");
        avaloniaEditor.Should().Contain("_editingSession.TableStylePreview");
        avalonia.Should().NotContain("new ActionRibbonCommand(() => editor.ApplyTableStyle(style))");
        wpfGallery.Should().Contain("IRibbonPreviewCommand")
            .And.Contain("preview.BeginPreview(RibbonCommandContext.Empty)")
            .And.Contain("preview.CancelPreview()")
            .And.Contain("command.Execute(RibbonCommandContext.Empty)");
    }

    private static void Execute(
        IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand> commands,
        FreeWRibbonCommandAction action) =>
        commands[action].Execute(RibbonCommandContext.Empty);

    private static TableEditingRibbonPorts CreatePorts(
        ICollection<string> events,
        TableToggleStateSource? state = null) =>
        new(
            PrepareExecution: () => events.Add("prepare"),
            CurrentTableFormatting: () => state?.Formatting,
            ViewGridlines: () => state?.ViewGridlines ?? false,
            ToggleHeaderRow: () => events.Add("toggle-header-row"),
            ToggleBandedRows: () => events.Add("toggle-banded-rows"),
            ToggleLastRow: () => events.Add("toggle-last-row"),
            ToggleFirstColumn: () => events.Add("toggle-first-column"),
            ToggleLastColumn: () => events.Add("toggle-last-column"),
            ToggleBandedColumns: () => events.Add("toggle-banded-columns"),
            ToggleGridlines: () => events.Add("toggle-gridlines"),
            SelectTable: () => events.Add("select-table"),
            SelectRow: () => events.Add("select-row"),
            SelectColumn: () => events.Add("select-column"),
            SelectCell: () => events.Add("select-cell"),
            InsertRowAbove: () => events.Add("insert-row-above"),
            InsertRowBelow: () => events.Add("insert-row-below"),
            InsertColumnLeft: () => events.Add("insert-column-left"),
            InsertColumnRight: () => events.Add("insert-column-right"),
            MergeCells: () => events.Add("merge-cells"),
            SplitCell: new ActionRibbonCommand(() => events.Add("split-cell")),
            Shading: new ActionRibbonCommand(() => events.Add("shading")),
            Borders: new ActionRibbonCommand(() => events.Add("borders")),
            DeleteRow: () => events.Add("delete-row"),
            DeleteColumn: () => events.Add("delete-column"),
            DeleteTable: () => events.Add("delete-table"),
            SplitTable: () => events.Add("split-table"),
            DistributeRows: () => events.Add("distribute-rows"),
            DistributeColumns: () => events.Add("distribute-columns"),
            SetAutoFit: mode => events.Add($"autofit:{mode}"),
            SetCellAlignment: (vertical, horizontal) => events.Add($"align:{vertical}:{horizontal}"),
            SetCellTextDirection: direction => events.Add($"direction:{direction}"),
            SetCellBorders: (edges, clearEdges) => events.Add($"border:{edges}:{clearEdges}"),
            ToggleRepeatHeaderRow: () => events.Add("toggle-repeat-header"));

    private sealed class TableToggleStateSource
    {
        public TableFormatting? Formatting { get; set; }
        public bool ViewGridlines { get; set; }
    }
}
