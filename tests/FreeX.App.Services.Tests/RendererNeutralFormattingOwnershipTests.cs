using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class RendererNeutralFormattingOwnershipTests
{
    [Fact]
    public void SpreadsheetDisplayFormatter_FormatsGridRangesAndInvalidDatesInvariantly()
    {
        var sheetId = SheetId.New();
        var start = new CellAddress(sheetId, 2, 2);
        var end = new CellAddress(sheetId, 3, 4);

        SpreadsheetDisplayFormatter.FormatRangeReference(
                new GridRange(start, end),
                useR1C1ReferenceStyle: false)
            .Should()
            .Be("B2:D3");
        SpreadsheetDisplayFormatter.FormatCellValue(new DateTimeValue(double.MaxValue))
            .Should()
            .Be(double.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SpreadsheetDisplayFormatter_PreservesExplicitScalarContextProfiles()
    {
        var date = new DateTimeValue(2);

        SpreadsheetDisplayFormatter.FormatScalarValue(
                date,
                SpreadsheetScalarFormatProfile.InvariantScalar)
            .Should()
            .Be("2");
        SpreadsheetDisplayFormatter.FormatScalarValue(
                new ErrorValue("#VALUE!"),
                SpreadsheetScalarFormatProfile.DefinedNameLabel)
            .Should()
            .BeEmpty();
        SpreadsheetDisplayFormatter.FormatScalarValue(
                new TextValue("Label"),
                SpreadsheetScalarFormatProfile.DefinedNameLabel)
            .Should()
            .Be("Label");
    }

    [Fact]
    public void ScalarFormattingConsumers_DelegateToSpreadsheetDisplayFormatter()
    {
        foreach (var path in new[]
        {
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.cs" },
            new[] { "src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs" },
            new[] { "src", "FreeX.App.Presentation", "DefinedNames", "DefinedNamesSession.cs" },
            new[] { "src", "FreeX.App.Services", "CellMergePlanner.cs" }
        })
        {
            var source = ReadSource(path);
            source.Should().Contain("SpreadsheetDisplayFormatter.FormatScalarValue(")
                .And.NotContain("private static string FormatScalarValue")
                .And.NotContain("private static string DefinedNameLabelText");
        }
    }

    [Fact]
    public void ServiceAndAvaloniaCallers_DelegateColorAndScalarFormattingToPresentationOwners()
    {
        ReadSource("src", "FreeX.App.Services", "FormatCellsDialogPlanner.cs")
            .Should().Contain("ColorInputParser.TryParseColorText(");
        ReadSource("src", "FreeX.App.Services", "SortDialogPlanner.cs")
            .Should().Contain("ColorInputParser.TryParseColorText(");
        ReadSource("src", "FreeX.App.Services", "CellColorPalettePlanner.cs")
            .Should().Contain("ColorInputParser.TryParseHexColor(")
            .And.Contain("ColorInputParser.FormatHexColor(");
        ReadSource("src", "FreeX.App.Avalonia", "FormatCellsFillEditor.cs")
            .Should().Contain("ColorInputParser.TryParseColorText(")
            .And.Contain("ColorInputParser.FormatRgbColor(");

        ReadSource("src", "FreeX.App.Services", "RemoveDuplicatesPlanner.cs")
            .Should().Contain("SpreadsheetDisplayFormatter.FormatCellValue(");
        ReadSource("src", "FreeX.App.Services", "DataValidationDropdownPlanner.cs")
            .Should().Contain("SpreadsheetDisplayFormatter.FormatCellValue(");
        ReadSource("src", "FreeX.App.Presentation", "Hyperlinks", "HyperlinkDialogPlanner.cs")
            .Should().Contain("SpreadsheetDisplayFormatter.FormatCellValue(")
            .And.NotContain("private static string FormatDisplayText(ScalarValue? value) => value switch");
        ReadSource("src", "FreeX.App.Services", "DataValidationDisplayTextPlanner.cs")
            .Should().Contain("SpreadsheetDisplayFormatter.FormatRangeReference(");
    }

    private static string ReadSource(params string[] path) =>
        File.ReadAllText(RepositoryFileLocator.Find(path));
}
