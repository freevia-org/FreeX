using FluentAssertions;
using FreeX.App.Presentation.CustomViews;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.CustomViews;

public sealed class CustomViewsPlannerTests
{
    private static Workbook NewWorkbook()
    {
        var wb = new Workbook("test");
        wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        return wb;
    }

    [Fact]
    public void BuildRows_ProjectsViewsInOrderWithSheetCountAndFlags()
    {
        var wb = NewWorkbook();
        CustomViewsPlanner.BuildSaveCommand("Print Layout", includePrintSettings: true, includeHiddenRowsColumnsAndFilterSettings: false)
            .Apply(Ctx(wb));
        CustomViewsPlanner.BuildSaveCommand("Review", includePrintSettings: false, includeHiddenRowsColumnsAndFilterSettings: true)
            .Apply(Ctx(wb));

        var rows = CustomViewsPlanner.BuildRows(wb);

        rows.Select(r => r.Name).Should().Equal("Print Layout", "Review");
        rows.Should().OnlyContain(r => r.SheetCount == 2);
        rows[0].IncludePrintSettings.Should().BeTrue();
        rows[0].IncludeHiddenRowsColumnsAndFilterSettings.Should().BeFalse();
        rows[1].IncludePrintSettings.Should().BeFalse();
        rows[1].IncludeHiddenRowsColumnsAndFilterSettings.Should().BeTrue();
    }

    [Fact]
    public void BuildDialogRows_UsesSuppliedIncludedIndicators()
    {
        var wb = NewWorkbook();
        CustomViewsPlanner.BuildSaveCommand("Print Layout", includePrintSettings: true, includeHiddenRowsColumnsAndFilterSettings: false)
            .Apply(Ctx(wb));

        var rows = CustomViewsPlanner.BuildDialogRows(wb, "Yes", "No");

        rows.Should().ContainSingle().Which.Should().Be(new CustomViewsPlanner.DialogRow(
            "Print Layout",
            2,
            "Yes",
            "No"));
    }

    [Fact]
    public void SuggestDefaultName_CountsExistingViews()
    {
        var wb = NewWorkbook();
        CustomViewsPlanner.SuggestDefaultName(wb, "View {0}").Should().Be("View 1");

        CustomViewsPlanner.BuildSaveCommand("View 1").Apply(Ctx(wb));
        CustomViewsPlanner.SuggestDefaultName(wb, "View {0}").Should().Be("View 2");
    }

    [Fact]
    public void SuggestDefaultName_FromCountUsesFormat()
    {
        CustomViewsPlanner.SuggestDefaultName(2, "Custom View {0}")
            .Should()
            .Be("Custom View 3");
    }

    [Fact]
    public void CreateNameSubmission_TrimsViewNameAndKeepsIncludeFlags()
    {
        CustomViewsPlanner.CreateNameSubmission(
                "  Quarter Close  ",
                includePrintSettings: false,
                includeHiddenRowsColumnsAndFilterSettings: true)
            .Should()
            .Be(new CustomViewsPlanner.NameSubmission(
                "Quarter Close",
                IncludePrintSettings: false,
                IncludeHiddenRowsColumnsAndFilterSettings: true));
    }

    [Fact]
    public void ValidateName_RejectsBlankTooLongAndDuplicate()
    {
        var wb = NewWorkbook();
        CustomViewsPlanner.BuildSaveCommand("Existing").Apply(Ctx(wb));

        CustomViewsPlanner.ValidateName(wb, "  ").Error.Should().Be(CustomViewsPlanner.NameError.Blank);
        CustomViewsPlanner.ValidateName(wb, new string('x', CustomViewsPlanner.MaxNameLength + 1)).Error
            .Should().Be(CustomViewsPlanner.NameError.TooLong);
        // Duplicate match is case-insensitive (matches the Core replace-by-name semantics).
        CustomViewsPlanner.ValidateName(wb, "existing").Error.Should().Be(CustomViewsPlanner.NameError.Duplicate);
        CustomViewsPlanner.ValidateName(wb, "Fresh").IsValid.Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_ReturnTheCoreCommandsThatCaptureApplyAndDelete()
    {
        CustomViewsPlanner.BuildSaveCommand("v").Should().BeOfType<SaveCustomViewCommand>();
        CustomViewsPlanner.BuildApplyCommand("v").Should().BeOfType<ApplyCustomViewCommand>();
        CustomViewsPlanner.BuildDeleteCommand("v").Should().BeOfType<DeleteCustomViewCommand>();
    }

    [Fact]
    public void SaveThenApply_CapturesAndRestoresPerSheetViewState()
    {
        var wb = NewWorkbook();
        var sheet = wb.GetSheet("Sheet1")!;
        sheet.ZoomPercent = 75;
        sheet.ShowGridlines = false;
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 1;

        CustomViewsPlanner.BuildSaveCommand("Snapshot").Apply(Ctx(wb));

        // Mutate the live view state, then restore the saved view.
        sheet.ZoomPercent = 130;
        sheet.ShowGridlines = true;
        sheet.FrozenRows = 0;
        sheet.FrozenCols = 0;

        CustomViewsPlanner.BuildApplyCommand("Snapshot").Apply(Ctx(wb));

        sheet.ZoomPercent.Should().Be(75);
        sheet.ShowGridlines.Should().BeFalse();
        sheet.FrozenRows.Should().Be(2u);
        sheet.FrozenCols.Should().Be(1u);
    }

    [Fact]
    public void DeleteCommand_RemovesTheView()
    {
        var wb = NewWorkbook();
        CustomViewsPlanner.BuildSaveCommand("Doomed").Apply(Ctx(wb));
        CustomViewsPlanner.BuildRows(wb).Should().ContainSingle();

        CustomViewsPlanner.BuildDeleteCommand("Doomed").Apply(Ctx(wb));
        CustomViewsPlanner.BuildRows(wb).Should().BeEmpty();
    }

    [Fact]
    public void CustomViewsDialogPlanning_IsSharedAndHostOnlyAdaptsLocalization()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var plannerSource = File.ReadAllText(Path.Combine(presentationRoot, "CustomViews", "CustomViewsPlanner.cs"));
        var hostDialogSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "CustomViewsDialog.xaml.cs"));
        var hostNameDialogSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "CustomViewNameDialog.cs"));

        plannerSource.Should().Contain("public readonly record struct DialogRow");
        plannerSource.Should().Contain("public static IReadOnlyList<DialogRow> BuildDialogRows");
        plannerSource.Should().Contain("public static string SuggestDefaultName(int customViewCount");
        plannerSource.Should().Contain("public readonly record struct NameSubmission");
        plannerSource.Should().Contain("public static NameSubmission CreateNameSubmission(");
        plannerSource.Should().NotContain("UiText");

        hostDialogSource.Should().Contain("CustomViewsPlanner.BuildDialogRows(");
        hostDialogSource.Should().Contain("UiText.Get(\"CustomViews_Included\")");
        hostDialogSource.Should().Contain("UiText.Get(\"CustomViews_NotIncluded\")");
        hostDialogSource.Should().Contain("CustomViewsPlanner.SuggestDefaultName(");
        hostDialogSource.Should().NotContain("GetIncludedIndicator");
        hostDialogSource.Should().NotContain("CustomViewViewModel");

        hostDialogSource.Should().Contain("CustomViewsPlanner.BuildApplyCommand(vm.Name)");
        hostDialogSource.Should().Contain("CustomViewsPlanner.BuildSaveCommand(");
        hostDialogSource.Should().Contain("CustomViewsPlanner.BuildDeleteCommand(vm.Name)");
        hostDialogSource.Should().NotContain("new SaveCustomViewCommand(");
        hostDialogSource.Should().NotContain("new ApplyCustomViewCommand(");
        hostDialogSource.Should().NotContain("new DeleteCustomViewCommand(");

        hostNameDialogSource.Should().Contain("CustomViewsPlanner.CreateNameSubmission(");
        hostNameDialogSource.Should().NotContain("viewName.Trim()");
    }

    private static ICommandContext Ctx(Workbook workbook) => new TestCommandContext(workbook);

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
