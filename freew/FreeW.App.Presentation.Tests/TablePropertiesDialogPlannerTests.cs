using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class TablePropertiesDialogPlannerTests
{
    [Fact]
    public void TabKinds_AreInRendererOrder()
    {
        Enum.GetValues<TablePropertiesDialogTabKind>().Should().Equal(
            TablePropertiesDialogTabKind.Table,
            TablePropertiesDialogTabKind.Row,
            TablePropertiesDialogTabKind.Column,
            TablePropertiesDialogTabKind.Cell);
    }

    [Fact]
    public void VisualMetrics_PreserveThePairedWpfAuthorityGeometry()
    {
        TablePropertiesDialogPlanner.VisualMetrics.Should().Be(
            new TablePropertiesDialogVisualMetrics(
                DialogWidth: 440,
                OuterInset: 14,
                ActionTopInset: 12,
                ActionBottomInset: 12,
                ActionButtonWidth: 72,
                ActionSpacing: 14,
                ContentInset: 14,
                SectionHeaderTopInset: 10,
                SectionHeaderBottomInset: 4,
                RowVerticalInset: 4,
                LabelRightInset: 8,
                NumberFieldMinWidth: 120,
                ChoiceFieldMinWidth: 180,
                SecondarySectionTopInset: 8,
                ExpanderContentInset: 8,
                AvaloniaTabPaneHorizontalCompensation: -12,
                AvaloniaMainLabelColumnWidth: 137,
                AvaloniaRowLabelColumnWidth: 131,
                AvaloniaMarginLabelColumnWidth: 54,
                AvaloniaCellSpacingLabelColumnWidth: 203,
                AvaloniaPositionGridRightInset: 4));
    }

    [Fact]
    public void BuildInitialState_SeedsAllTabsFromCaretTableContext()
    {
        var table = Table.Create(1, 1);
        table.PreferredWidthPt = 300;
        table.Alignment = TableAlignment.Right;
        table.TextWrapping = true;
        table.FloatingTableAllowsOverlap = false;
        table.FloatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            HorizontalAlignment: TableHorizontalPositionAlignment.Outside,
            VerticalOffsetPt: -18,
            LeftFromTextPt: 3,
            RightFromTextPt: 4,
            TopFromTextPt: 5,
            BottomFromTextPt: 6);
        table.IndentFromLeftPt = 12;
        table.CellSpacingPt = 2;
        table.Formatting = table.Formatting with { RepeatHeaderRow = true };
        var row = table.Rows[0];
        row.HeightPt = 36;
        row.HeightRule = TableRowHeightRule.Exact;
        row.AllowBreakAcrossPages = false;
        var cell = row.Cells[0];
        cell.WidthPt = 150;
        cell.VerticalAlignment = TableCellVerticalAlignment.Bottom;
        cell.Margins = new TableCellMargins(1, 7, 1, 7);
        cell.WrapText = false;
        cell.FitText = true;

        var state = TablePropertiesDialogPlanner.BuildInitialState(
            new ModelTableContext(table, row, cell),
            CultureInfo.InvariantCulture);

        state.PreferredWidthText.Should().Be("300");
        state.PreferredWidthOn.Should().BeTrue();
        state.AlignmentIndex.Should().Be(2);
        state.WrappingIndex.Should().Be(1);
        state.FloatingTableAllowsOverlap.Should().BeFalse();
        state.FloatingHorizontalAnchorIndex.Should().Be(2);
        state.FloatingHorizontalModeIndex.Should().Be(5);
        state.FloatingVerticalAnchorIndex.Should().Be(1);
        state.FloatingVerticalModeIndex.Should().Be(0);
        state.FloatingVerticalOffsetText.Should().Be("-18");
        state.FloatingDistanceLeftText.Should().Be("3");
        state.IndentText.Should().Be("12");
        state.CellSpacingOn.Should().BeTrue();
        state.RowHeightText.Should().Be("36");
        state.RowRuleIndex.Should().Be(1);
        state.AllowRowBreak.Should().BeFalse();
        state.RepeatHeaderRow.Should().BeTrue();
        state.ColumnWidthText.Should().Be("150");
        state.CellVerticalAlignmentIndex.Should().Be(2);
        state.CellMarginsSameAsTable.Should().BeFalse();
        state.CellMarginLeftText.Should().Be("7");
        state.CellWrapText.Should().BeFalse();
        state.CellFitText.Should().BeTrue();
    }

    [Fact]
    public void TryBuildResult_ConstructsTableRowColumnAndCellValues()
    {
        var input = ValidInput() with
        {
            AlignmentIndex = 1,
            WrappingIndex = 1,
            RowRuleIndex = 1,
            CellVerticalAlignmentIndex = 1,
            CellMarginsSameAsTable = false,
            CellWrapText = false,
            CellFitText = true,
            FloatingTableAllowsOverlap = false,
            FloatingHorizontalAnchorIndex = 2,
            FloatingHorizontalModeIndex = 5,
            FloatingHorizontalOffsetText = "ignored",
            FloatingVerticalAnchorIndex = 1,
            FloatingVerticalModeIndex = 0,
            FloatingVerticalOffsetText = "-18",
            FloatingDistanceTopText = "5",
            FloatingDistanceLeftText = "3",
            FloatingDistanceBottomText = "6",
            FloatingDistanceRightText = "4",
        };

        TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.PreferredWidthPt.Should().Be(300);
        result.Alignment.Should().Be(TableAlignment.Center);
        result.TextWrapping.Should().BeTrue();
        result.FloatingTableAllowsOverlap.Should().BeFalse();
        result.FloatingPosition.Should().Be(new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            VerticalOffsetPt: -18,
            HorizontalAlignment: TableHorizontalPositionAlignment.Outside,
            LeftFromTextPt: 3,
            RightFromTextPt: 4,
            TopFromTextPt: 5,
            BottomFromTextPt: 6));
        result.IndentFromLeftPt.Should().Be(12);
        result.DefaultCellMargins!.LeftPt.Should().Be(6);
        result.CellSpacingPt.Should().Be(2);
        result.RowHeightPt.Should().Be(36);
        result.RowHeightRule.Should().Be(TableRowHeightRule.Exact);
        result.AllowRowBreak.Should().BeFalse();
        result.RepeatHeaderRow.Should().BeTrue();
        result.ColumnWidthPt.Should().Be(120);
        result.CellPreferredWidthPt.Should().Be(140);
        result.CellVerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        result.CellMargins!.LeftPt.Should().Be(8);
        result.CellWrapText.Should().BeFalse();
        result.CellFitText.Should().BeTrue();
    }

    [Fact]
    public void TryBuildResult_UncheckedOptionalFieldsIgnoreInvalidTextAndUseAutoRowRule()
    {
        var input = ValidInput() with
        {
            PreferredWidthOn = false,
            PreferredWidthText = "wide",
            CellSpacingOn = false,
            CellSpacingText = "spaced",
            RowHeightOn = false,
            RowHeightText = "tall",
            ColumnWidthOn = false,
            ColumnWidthText = "column",
            CellWidthOn = false,
            CellWidthText = "cell",
            CellMarginsSameAsTable = true,
        };

        TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();

        result!.PreferredWidthPt.Should().BeNull();
        result.CellSpacingPt.Should().BeNull();
        result.RowHeightPt.Should().BeNull();
        result.RowHeightRule.Should().Be(TableRowHeightRule.Auto);
        result.ColumnWidthPt.Should().BeNull();
        result.CellPreferredWidthPt.Should().BeNull();
        result.CellMargins.Should().BeNull();
    }

    [Fact]
    public void TryBuildResult_RejectsNegativeRequiredMeasurementsWithPreservedMessage()
    {
        var input = ValidInput() with { DefaultCellMarginLeftText = "-1" };

        TablePropertiesDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(TablePropertiesDialogPlanner.ValidationMessage);
    }

    [Fact]
    public void TryBuildResult_AllowsSignedPositionButRejectsNegativeTextDistance()
    {
        var signedPosition = ValidInput() with
        {
            WrappingIndex = 1,
            FloatingHorizontalAnchorIndex = 0,
            FloatingHorizontalModeIndex = 0,
            FloatingHorizontalOffsetText = "-12.5",
            FloatingVerticalAnchorIndex = 0,
            FloatingVerticalModeIndex = 0,
            FloatingVerticalOffsetText = "18.25",
            FloatingDistanceTopText = "0",
            FloatingDistanceLeftText = "3",
            FloatingDistanceBottomText = "0",
            FloatingDistanceRightText = "4",
        };

        TablePropertiesDialogPlanner.TryBuildResult(
                signedPosition,
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();
        result!.FloatingPosition!.HorizontalOffsetPt.Should().Be(-12.5);
        result.FloatingPosition.VerticalOffsetPt.Should().Be(18.25);

        TablePropertiesDialogPlanner.TryBuildResult(
                signedPosition with { FloatingDistanceLeftText = "-1" },
                CultureInfo.InvariantCulture,
                out _,
                out var error)
            .Should().BeFalse();
        error.Should().Be(TablePropertiesDialogPlanner.ValidationMessage);
    }

    [Fact]
    public void Session_OwnsCatalogsEnabledStateValidationAndAcceptance()
    {
        var table = Table.Create(1, 1);
        var session = new TablePropertiesDialogSession(
            new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]),
            CultureInfo.InvariantCulture);

        session.AlignmentNames.Should().Equal("Left", "Center", "Right");
        session.WrappingNames.Should().Equal("None", "Around");
        session.PlanEnabledState(wrappingIndex: 0, horizontalModeIndex: 0, verticalModeIndex: 0)
            .Should().Be(new TablePropertiesDialogEnabledState(false, false, false));
        session.PlanEnabledState(wrappingIndex: 1, horizontalModeIndex: 0, verticalModeIndex: 2)
            .Should().Be(new TablePropertiesDialogEnabledState(true, true, false));

        session.PlanAcceptance(ValidInput() with { DefaultCellMarginLeftText = "-1" })
            .ValidationMessage.Should().Be(TablePropertiesDialogPlanner.ValidationMessage);
        session.PlanAcceptance(ValidInput()).Result.Should().NotBeNull();
    }

    [Fact]
    public void CaptureInput_UsesTheSharedSemanticFieldProtocol()
    {
        var requested = new List<string>();
        var input = TablePropertiesDialogInput.Capture(
            id =>
            {
                requested.Add(id);
                return id == TablePropertiesDialogPlanner.AllowOverlapAutomationId ? null : true;
            },
            id =>
            {
                requested.Add(id);
                return id;
            },
            id =>
            {
                requested.Add(id);
                return id == TablePropertiesDialogPlanner.WrappingAutomationId ? 1 : 0;
            });

        requested.Should().OnlyHaveUniqueItems();
        input.PreferredWidthText.Should().Be(TablePropertiesDialogPlanner.PreferredWidthAutomationId);
        input.WrappingIndex.Should().Be(1);
        input.CellWrapText.Should().BeTrue();
        input.FloatingTableAllowsOverlap.Should().BeNull();
    }

    [Theory]
    [InlineData(TablePropertiesDialogTabKind.Table, TablePropertiesDialogTabKind.Table, TablePropertiesDialogPlanner.PreferredWidthAutomationId)]
    [InlineData(TablePropertiesDialogTabKind.Row, TablePropertiesDialogTabKind.Row, TablePropertiesDialogPlanner.RowHeightAutomationId)]
    [InlineData(TablePropertiesDialogTabKind.Column, TablePropertiesDialogTabKind.Column, TablePropertiesDialogPlanner.ColumnWidthAutomationId)]
    [InlineData(TablePropertiesDialogTabKind.Cell, TablePropertiesDialogTabKind.Cell, TablePropertiesDialogPlanner.CellWidthAutomationId)]
    [InlineData((TablePropertiesDialogTabKind)99, TablePropertiesDialogTabKind.Table, TablePropertiesDialogPlanner.PreferredWidthAutomationId)]
    public void Session_OwnsTabDefaultingAndFocusAutomationTarget(
        TablePropertiesDialogTabKind requestedTab,
        TablePropertiesDialogTabKind expectedTab,
        string expectedAutomationId)
    {
        var table = Table.Create(1, 1);
        var session = new TablePropertiesDialogSession(
            new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]),
            CultureInfo.InvariantCulture,
            requestedTab);

        session.InitialFocusPlan.Should().Be(new TablePropertiesDialogFocusPlan(
            expectedTab,
            expectedAutomationId,
            SelectAllOnFocus: true));
        session.PlanFocus(requestedTab).Should().Be(session.InitialFocusPlan);
    }

    [Fact]
    public void ApplyValues_AppliesTableRowColumnAndCellFields()
    {
        var table = Table.Create(2, 2);
        var row = table.Rows[0];
        var cell = row.Cells[1];
        var values = new TablePropertiesValues(
            PreferredWidthPt: 300,
            Alignment: TableAlignment.Right,
            TextWrapping: true,
            IndentFromLeftPt: 12,
            DefaultCellMargins: new TableCellMargins(0, 6, 0, 6),
            CellSpacingPt: 2,
            RowHeightPt: 36,
            RowHeightRule: TableRowHeightRule.Exact,
            AllowRowBreak: false,
            RepeatHeaderRow: true,
            ColumnWidthPt: 120,
            CellPreferredWidthPt: 140,
            CellVerticalAlignment: TableCellVerticalAlignment.Center,
            CellMargins: new TableCellMargins(2, 8, 2, 8),
            CellWrapText: false,
            CellFitText: true,
            FloatingTableAllowsOverlap: false,
            FloatingPosition: new TableFloatingPosition(
                HorizontalAnchor: TableHorizontalAnchor.Page,
                VerticalAnchor: TableVerticalAnchor.Margin,
                HorizontalAlignment: TableHorizontalPositionAlignment.Right,
                VerticalOffsetPt: -9));

        TablePropertiesDialogPlanner.ApplyValues(new ModelTableContext(table, row, cell), values);

        table.PreferredWidthPt.Should().Be(300);
        table.Alignment.Should().Be(TableAlignment.Right);
        table.TextWrapping.Should().BeTrue();
        table.FloatingTableAllowsOverlap.Should().BeFalse();
        table.FloatingPosition.Should().Be(values.FloatingPosition);
        table.IndentFromLeftPt.Should().Be(12);
        table.DefaultCellMargins.Should().Be(new TableCellMargins(0, 6, 0, 6));
        table.CellSpacingPt.Should().Be(2);
        table.Formatting.RepeatHeaderRow.Should().BeTrue();
        row.HeightPt.Should().Be(36);
        row.HeightRule.Should().Be(TableRowHeightRule.Exact);
        row.AllowBreakAcrossPages.Should().BeFalse();
        table.Rows[1].Cells[1].WidthPt.Should().Be(120);
        cell.WidthPt.Should().Be(140);
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        cell.Margins.Should().Be(new TableCellMargins(2, 8, 2, 8));
        cell.WrapText.Should().BeFalse();
        cell.FitText.Should().BeTrue();
    }

    [Fact]
    public void ApplyTablePropertiesCommand_UndoRedoRestoresCompleteMutationFootprint()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(2, 2);
        table.ColumnWidthsPt.AddRange([90, 110]);
        var floatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            HorizontalOffsetPt: -12);
        table.FloatingPosition = floatingPosition;
        table.FloatingTableAllowsOverlap = false;
        document.Blocks.Add(table);
        var row = table.Rows[0];
        var cell = row.Cells[1];
        var values = new TablePropertiesValues(
            PreferredWidthPt: 300,
            Alignment: TableAlignment.Right,
            TextWrapping: false,
            IndentFromLeftPt: 12,
            DefaultCellMargins: new TableCellMargins(0, 6, 0, 6),
            CellSpacingPt: 2,
            RowHeightPt: 36,
            RowHeightRule: TableRowHeightRule.Exact,
            AllowRowBreak: false,
            RepeatHeaderRow: true,
            ColumnWidthPt: 120,
            CellPreferredWidthPt: 140,
            CellVerticalAlignment: TableCellVerticalAlignment.Center,
            CellMargins: new TableCellMargins(2, 8, 2, 8),
            CellWrapText: false,
            CellFitText: true);
        var bus = new DocumentCommandBus(new CommandContext(document));

        bus.Execute(new ApplyTablePropertiesCommand(0, 0, 1, values));
        table.FloatingPosition.Should().BeNull();
        table.FloatingTableAllowsOverlap.Should().BeNull();
        cell.WidthPt.Should().Be(140);
        cell.WrapText.Should().BeFalse();
        cell.FitText.Should().BeTrue();
        table.Rows[1].Cells[1].WidthPt.Should().Be(120);

        bus.Undo().Should().BeTrue();
        table.PreferredWidthPt.Should().BeNull();
        table.Alignment.Should().Be(TableAlignment.Left);
        table.FloatingPosition.Should().Be(floatingPosition);
        table.FloatingTableAllowsOverlap.Should().BeFalse();
        table.ColumnWidthsPt.Should().Equal(90, 110);
        table.Rows.SelectMany(candidate => candidate.Cells).Should().OnlyContain(candidate => candidate.WidthPt == null);
        row.HeightPt.Should().BeNull();
        row.AllowBreakAcrossPages.Should().BeTrue();
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Top);
        cell.Margins.Should().BeNull();
        cell.WrapText.Should().BeTrue();
        cell.FitText.Should().BeFalse();

        bus.Redo().Should().BeTrue();
        table.PreferredWidthPt.Should().Be(300);
        table.FloatingPosition.Should().BeNull();
        table.FloatingTableAllowsOverlap.Should().BeNull();
        cell.WidthPt.Should().Be(140);
        cell.WrapText.Should().BeFalse();
        cell.FitText.Should().BeTrue();
        table.Rows[1].Cells[1].WidthPt.Should().Be(120);
    }

    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static TablePropertiesDialogInput ValidInput() => new(
        PreferredWidthOn: true,
        PreferredWidthText: "300",
        AlignmentIndex: 0,
        WrappingIndex: 0,
        IndentText: "12",
        DefaultCellMarginTopText: "0",
        DefaultCellMarginLeftText: "6",
        DefaultCellMarginBottomText: "0",
        DefaultCellMarginRightText: "6",
        CellSpacingOn: true,
        CellSpacingText: "2",
        RowHeightOn: true,
        RowHeightText: "36",
        RowRuleIndex: 0,
        AllowRowBreak: false,
        RepeatHeaderRow: true,
        ColumnWidthOn: true,
        ColumnWidthText: "120",
        CellWidthOn: true,
        CellWidthText: "140",
        CellVerticalAlignmentIndex: 0,
        CellMarginsSameAsTable: true,
        CellMarginTopText: "2",
        CellMarginLeftText: "8",
        CellMarginBottomText: "2",
        CellMarginRightText: "8",
        CellWrapText: true,
        CellFitText: false);
}

public sealed class TablePropertiesDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("FreeW.App.Host", "TablePropertiesDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "TableDialogs.cs")]
    public void RenderersDelegateTablePropertiesLifetimeToSession(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", project, fileName));

        source.Should().Contain("TablePropertiesDialogSession");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.PlanEnabledState(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().Contain("_session.CaptureInput(");
        source.Should().NotContain("new TablePropertiesDialogInput(");
        source.Should().Contain("_session.InitialFocusPlan");
        source.Should().Contain("ResolveFocusTarget(");
        source.Should().NotContain("TablePropertiesDialogPlanner.BuildInitialState(");
        source.Should().NotContain("TablePropertiesDialogPlanner.TryBuildResult(");
        source.Should().NotContain("Title = \"Table Properties\"");
        source.Should().NotContain("Content = \"Allow overlap\"");
        source.Should().NotContain("\"TablePropertiesAllowOverlapCheckBox\"");
        source.Should().Contain("TablePropertiesDialogTabKind initialTab");
        source.Should().NotContain("enum TablePropertiesDialogTab");
        source.Should().NotContain("internal enum Tab");
    }

    [Fact]
    public void RenderersConsumeSharedVisualMetricsAndAvaloniaMatchesWpfRowCheckSpacing()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Host", "TablePropertiesDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "TableDialogs.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TablePropertiesDialogPlanner.VisualMetrics");
            source.Should().Contain("Layout.DialogWidth");
            source.Should().Contain("Layout.OuterInset");
            source.Should().Contain("Layout.ActionButtonWidth");
            source.Should().Contain("Layout.ContentInset");
            source.Should().Contain("Layout.RowVerticalInset");
            source.Should().Contain("Layout.NumberFieldMinWidth");
            source.Should().Contain("Layout.ChoiceFieldMinWidth");
        }

        wpf.Should().NotContain("Width = 440;");
        wpf.Should().NotContain("MinWidth = 120");
        wpf.Should().NotContain("MinWidth = 180");
        avalonia.Should().NotContain("Width = 440;");
        avalonia.Should().NotContain("TwoColumnGrid(4, 137)");
        avalonia.Should().NotContain("TwoColumnGrid(2, 131)");
        avalonia.Should().Contain("_allowRowBreak.Margin = new Thickness(0);");
        avalonia.Should().Contain(
            "_repeatHeader.Margin = new Thickness(0, Layout.RowVerticalInset, 0, 0);");
    }

    [Fact]
    public void ProductionRenderersUseOwnedWarningDialogsForInvalidTableProperties()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Host", "TablePropertiesDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "TableDialogs.cs"));

        wpf.Should().Contain("DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage)");
        avalonia.Should().Contain("IUserMessageService? messageService = null");
        avalonia.Should().Contain("messageService ?? new AvaloniaUserMessageService(this)");
        avalonia.Should().Contain("_messageService.ShowWarningAsync(");
        avalonia.Should().Contain("acceptance.ValidationMessage ?? TablePropertiesDialogPlanner.ValidationMessage");
        avalonia.Should().Contain("private TablePropertiesDialogAcceptance CaptureAcceptance()");
        avalonia.Should().Contain("private TablePropertiesValues? TryAccept(bool close)");
    }
}
