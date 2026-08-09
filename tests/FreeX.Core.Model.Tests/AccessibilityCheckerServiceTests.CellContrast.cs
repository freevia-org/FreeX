using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WithExplicitFontAndFill()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 3, 2);
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(120, 120, 120),
            FillColor = new CellColor(130, 130, 130)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Projected revenue"),
            StyleId = lowContrastStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.SheetId.Should().Be(sheet.Id);
        issue.SheetName.Should().Be("Sales");
        issue.Location.Should().Be("B3");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_ForDisplayedNonTextValues()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Values");
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(120, 120, 120),
            FillColor = new CellColor(130, 130, 130)
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new NumberValue(42),
            StyleId = lowContrastStyle
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell
        {
            Value = new BoolValue(true),
            StyleId = lowContrastStyle
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new Cell
        {
            Value = ErrorValue.DivByZero,
            StyleId = lowContrastStyle
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(i => i.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Should().HaveCount(3);
        issues.Select(i => i.Location).Should().BeEquivalentTo(new[] { "A1", "B1", "C1" });
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WithDefaultWhiteBackground()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 1, 1);
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(245, 245, 245)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Low contrast on no fill"),
            StyleId = lowContrastStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WithThemeTintFontAndFill()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(245, 245, 245))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(230, 230, 230))
        };
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 4, 2);
        var themedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0.1),
            FillColor = CellColor.White,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Theme-derived warning"),
            StyleId = themedStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B4");
    }

    [Fact]
    public void FindIssues_IgnoresCellRgbFallbackWhenThemeColorsHaveSufficientContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 5, 2);
        var themedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(245, 245, 245),
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            FillColor = CellColor.White,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1)
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Readable themed text"),
            StyleId = themedStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenPatternForegroundIsLowContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        var patternedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FillColor = CellColor.White,
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = CellColor.Black
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Patterned exception note"),
            StyleId = patternedStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenGrayPatternBlendIsLowContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 3, 2);
        var patternedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.White,
            FillColor = CellColor.Black,
            FillPatternStyle = CellFillPatternStyle.DarkGray,
            FillPatternColor = CellColor.White
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Patterned risk note"),
            StyleId = patternedStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B3");
    }

    [Fact]
    public void FindIssues_IgnoresPatternedCellTextWithSufficientBaseAndPatternContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var patternedStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FillColor = CellColor.White,
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(230, 230, 230)
        });
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new Cell
        {
            Value = new TextValue("Readable patterned note"),
            StyleId = patternedStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Theory]
    [InlineData(18, false)]
    [InlineData(14, true)]
    public void FindIssues_UsesLowerContrastThresholdForLargeCellText(double fontSize, bool bold)
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var largeTextStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(120, 120, 120),
            FillColor = CellColor.White,
            FontSize = fontSize,
            Bold = bold
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new TextValue("Large readable heading"),
            StyleId = largeTextStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_WhenGradientFillStopsAreLowContrast()
    {
        // R131 (c): a cell with a gradient fill (FillColor/FillThemeColor both null, GradientFill
        // populated) previously fell through to a fabricated CellColor.White background regardless
        // of what the gradient actually contains. Here the font color is close in luminance to
        // BOTH gradient stops, so it is illegible against the real on-screen gradient even though
        // it would read fine against the old fabricated white fallback.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 1, 1);
        var gradientStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(50, 50, 50),
            GradientFill = new CellGradientFill
            {
                Stops =
                [
                    new CellGradientStop(0, new CellColor(60, 60, 60)),
                    new CellGradientStop(1, new CellColor(70, 70, 70))
                ]
            }
        });
        sheet.SetCell(address, new Cell
        {
            Value = new TextValue("Gradient banner text"),
            StyleId = gradientStyle
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_IgnoresGradientFillCellTextWithSufficientContrastAtEveryStop()
    {
        // Sibling no-regression: a gradient fill where the text clears the contrast bar against
        // EVERY stop must not be flagged -- the worst-stop rule should not over-correct into
        // flagging every gradient-filled cell.
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var gradientStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            GradientFill = new CellGradientFill
            {
                Stops =
                [
                    new CellGradientStop(0, new CellColor(250, 250, 250)),
                    new CellGradientStop(1, new CellColor(240, 240, 240))
                ]
            }
        });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            Value = new TextValue("Readable gradient text"),
            StyleId = gradientStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_IgnoresBlankCellsAndSufficientContrast()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var lowContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(245, 245, 245)
        });
        var sufficientContrastStyle = workbook.RegisterStyle(new CellStyle
        {
            FontColor = CellColor.Black,
            FillColor = CellColor.White
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = BlankValue.Instance,
            StyleId = lowContrastStyle
        });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell
        {
            Value = new TextValue("Readable text"),
            StyleId = sufficientContrastStyle
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }
}
