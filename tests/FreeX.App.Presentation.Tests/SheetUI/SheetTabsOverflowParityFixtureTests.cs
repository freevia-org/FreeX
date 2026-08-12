using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.SheetUI;

public sealed class SheetTabsOverflowParityFixtureTests
{
    [Fact]
    public void Prepare_CreatesTwentyValidSheetsAndActivatesTheTail()
    {
        var workbook = new Workbook("Parity Demo");
        var demoSheet = workbook.AddSheet("Demo");

        var activeSheetId = SheetTabsOverflowParityFixture.Prepare(workbook);

        workbook.Sheets.Should().HaveCount(SheetTabsOverflowParityFixture.TargetSheetCount);
        workbook.Sheets.Select(sheet => sheet.Id).Should().OnlyHaveUniqueItems();
        workbook.Sheets.Should().OnlyContain(sheet => sheet.Id.Value != Guid.Empty);
        workbook.Sheets[0].Should().BeSameAs(demoSheet);
        activeSheetId.Should().Be(workbook.Sheets[^1].Id);
        workbook.ActiveSheetIndex.Should().Be(workbook.Sheets.Count - 1);
    }

    [Fact]
    public void Prepare_IsIdempotentOnceTheOverflowFixtureExists()
    {
        var workbook = new Workbook("Parity Demo");
        workbook.AddSheet("Demo");
        SheetTabsOverflowParityFixture.Prepare(workbook);
        var sheetIds = workbook.Sheets.Select(sheet => sheet.Id).ToArray();

        SheetTabsOverflowParityFixture.Prepare(workbook).Should().Be(sheetIds[^1]);

        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(sheetIds);
    }

    [Fact]
    public void Prepare_RejectsAnEmptySheetIdentityBeforeMutatingTheWorkbook()
    {
        var workbook = new Workbook("Parity Demo");
        workbook.InsertSheet(0, new Sheet(default, "Invalid"));

        var action = () => SheetTabsOverflowParityFixture.Prepare(workbook);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("workbook")
            .WithMessage("*non-empty sheet identities*");
        workbook.Sheets.Should().ContainSingle();
    }

    [Fact]
    public void WpfParityCapture_PreparesValidFixturesWithoutInvokingUserCommands()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var capture = File.ReadAllText(Path.Combine(repoRoot, "tools", "FreeX.ParityCapture.Wpf", "Capture", "ParityCapture.cs"));
        var backstage = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var startup = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.Startup.cs"));
        var adoptionIndex = capture.IndexOf(
            "window.AdoptWorkbookForParityCapture(ParityDemoWorkbookFactory.Create())",
            StringComparison.Ordinal);
        var showIndex = capture.IndexOf("window.Show();", StringComparison.Ordinal);

        capture.Should().Contain("SheetTabsOverflowParityFixture.Prepare(overflowWorkbook)");
        capture.Should().NotContain("InvokePrivate(window, \"InsertNewSheet\")");
        adoptionIndex.Should().BeGreaterThanOrEqualTo(0);
        showIndex.Should().BeGreaterThan(adoptionIndex);
        backstage.Should().Contain("sheet.Id.Value == Guid.Empty");
        backstage.Should().Contain("_parityCaptureWorkbookPrepared = true;");
        startup.Should().Contain("else if (!_parityCaptureWorkbookPrepared)");
    }
}
