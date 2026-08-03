using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Services.Tests;

public sealed class FormatCellsDialogPlannerTests
{
    [Fact]
    public void AlignmentLayout_UsesWpfDialogSpacingContract()
    {
        FormatCellsDialogAlignmentLayout.ContentInset.Should().Be(8);
        FormatCellsDialogAlignmentLayout.LabelTopMargin.Should().Be(4);
        FormatCellsDialogAlignmentLayout.LabelBottomMargin.Should().Be(2);
        FormatCellsDialogAlignmentLayout.FollowupLabelTopMargin.Should().Be(8);
        FormatCellsDialogAlignmentLayout.CheckBoxTopMargin.Should().Be(8);
        FormatCellsDialogAlignmentLayout.FollowupCheckBoxTopMargin.Should().Be(6);
        FormatCellsDialogAlignmentLayout.CheckBoxHeight.Should().Be(16);
        FormatCellsDialogAlignmentLayout.ControlHeight.Should().Be(24);
    }

    [Fact]
    public void TryCreateResult_BuildsStyleDiffBorderSelectionAndMergeDecision()
    {
        var input = ValidInput() with
        {
            Border = ValidInput().Border with
            {
                ClearPresetRequested = false,
                OutlinePreset = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
                InsidePreset = new CellBorder(BorderStyle.Dashed, new CellColor(4, 5, 6))
            }
        };

        FormatCellsDialogPlanner.TryCreateResult(new CellStyle(), input, out var result, out var validation)
            .Should()
            .BeTrue();

        validation.Should().BeNull();
        result.Should().NotBeNull();
        result!.Diff.NumberFormat.Should().Be("EUR#,##0.000;(EUR#,##0.000)");
        result.Diff.FontName.Should().Be("Verdana");
        result.Diff.FontSize.Should().Be(13.5);
        result.Diff.Bold.Should().BeTrue();
        result.Diff.Italic.Should().BeTrue();
        result.Diff.Underline.Should().BeFalse();
        result.Diff.DoubleUnderline.Should().BeTrue();
        result.Diff.FontColor.Should().Be(new CellColor(192, 0, 0));
        result.Diff.FillColor.Should().Be(new CellColor(0, 176, 80));
        result.Diff.FillPatternStyle.Should().Be(CellFillPatternStyle.DarkGrid);
        result.Diff.FillPatternColor.Should().Be(new CellColor(91, 155, 213));
        result.Diff.HAlign.Should().Be(CellHAlign.Right);
        result.Diff.VAlign.Should().Be(CellVAlign.Center);
        result.Diff.WrapText.Should().BeTrue();
        result.Diff.ShrinkToFit.Should().BeTrue();
        result.Diff.IndentLevel.Should().Be(7);
        result.Diff.TextRotation.Should().Be(-45);
        result.Diff.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(255, 192, 0)));
        result.Diff.BorderRight.Should().Be(new CellBorder(BorderStyle.Medium, new CellColor(68, 114, 196)));
        result.Diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(112, 173, 71)));
        result.Diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(237, 125, 49)));
        result.Diff.Locked.Should().BeFalse();
        result.Diff.Hidden.Should().BeTrue();
        result.BorderSelection.Outline.Should().Be(new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)));
        result.BorderSelection.Inside.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(4, 5, 6)));
        result.MergeCells.Should().BeTrue();
    }

    [Fact]
    public void TryCreateResult_ReportsValidationTargetsForInvalidDialogInputs()
    {
        AssertValidation(
            ValidInput() with { Font = ValidInput().Font with { FontColorText = "not-a-color" } },
            FormatCellsDialogPlannerTab.Font,
            FormatCellsDialogValidationTarget.FontColor,
            "FormatCells_InvalidFontColorMessage");

        AssertValidation(
            ValidInput() with { Fill = ValidInput().Fill with { FillColorText = "not-a-color" } },
            FormatCellsDialogPlannerTab.Fill,
            FormatCellsDialogValidationTarget.FillColor,
            "FormatCells_InvalidFillColorMessage");

        AssertValidation(
            ValidInput() with { Number = ValidInput().Number with { DecimalPlacesText = "31" } },
            FormatCellsDialogPlannerTab.Number,
            FormatCellsDialogValidationTarget.NumberDecimalPlaces,
            "FormatCells_InvalidDecimalPlacesMessage");

        AssertValidation(
            ValidInput() with { Number = ValidInput().Number with { Category = "Custom", FormatText = "[bad" } },
            FormatCellsDialogPlannerTab.Number,
            FormatCellsDialogValidationTarget.NumberFormat,
            "FormatCells_InvalidCustomNumberFormatMessage");

        AssertValidation(
            ValidInput() with { Font = ValidInput().Font with { FontSizeText = "0" } },
            FormatCellsDialogPlannerTab.Font,
            FormatCellsDialogValidationTarget.FontSize,
            "FormatCells_InvalidFontSizeMessage");

        AssertValidation(
            ValidInput() with { Alignment = ValidInput().Alignment with { TextRotationText = "91" } },
            FormatCellsDialogPlannerTab.Alignment,
            FormatCellsDialogValidationTarget.TextRotation,
            "FormatCells_InvalidTextRotationMessage");

        AssertValidation(
            ValidInput() with
            {
                Border = ValidInput().Border with
                {
                    Right = ValidInput().Border.Right with { ColorText = "not-a-color" }
                }
            },
            FormatCellsDialogPlannerTab.Border,
            FormatCellsDialogValidationTarget.BorderRightColor,
            "FormatCells_InvalidRightBorderColorMessage");
    }

    [Fact]
    public void TryCreateResult_AllowsBlankSideColorsWhenBorderSideIsNone()
    {
        var current = new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(1, 2, 3))
        };
        var input = ValidInput() with
        {
            Border = ValidInput().Border with
            {
                Top = new FormatCellsDialogBorderSideInput(nameof(BorderStyle.None), "")
            }
        };

        FormatCellsDialogPlanner.TryCreateResult(current, input, out var result, out var validation)
            .Should()
            .BeTrue();

        validation.Should().BeNull();
        result!.Diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None, new CellColor(1, 2, 3)));
    }

    [Fact]
    public void ChoiceHelpers_NormalizeFontFillAndBorderSelections()
    {
        var labels = Labels;
        FormatCellsDialogPlanner.FontStyleLabel(bold: true, italic: true, labels).Should().Be("Bold Italic");
        FormatCellsDialogPlanner.IsSingleUnderlineSelected("Single Accounting", labels).Should().BeTrue();
        FormatCellsDialogPlanner.IsDoubleUnderlineSelected("Double Accounting", labels).Should().BeTrue();
        FormatCellsDialogPlanner.ResolveSelectedFontName("  Typed Font  ", "Selected").Should().Be("Typed Font");

        var fillChoices = FormatCellsDialogPlanner.CreateFillPatternDisplayChoices(key =>
            key == "FormatCells_FillPatternDarkGrid" ? "Diagonal Crosshatch" : key);
        FormatCellsDialogPlanner.ResolveFillPatternStyle("Diagonal Crosshatch", fillChoices)
            .Should()
            .Be(CellFillPatternStyle.DarkGrid);
        FormatCellsDialogPlanner.GetFillPatternResourceKey(CellFillPatternStyle.DarkGrid)
            .Should()
            .Be("FormatCells_FillPatternDarkGrid");

        FormatCellsDialogPlanner.NextBorderSideStyle(nameof(BorderStyle.Thin), nameof(BorderStyle.Dashed))
            .Should()
            .Be(BorderStyle.None);
        FormatCellsDialogPlanner.NextBorderSideStyle(nameof(BorderStyle.None), nameof(BorderStyle.Dashed))
            .Should()
            .Be(BorderStyle.Dashed);
        FormatCellsDialogPlanner.CreateSelectedBorderLine("not-real", "#7030A0")
            .Should()
            .Be(new CellBorder(BorderStyle.Thin, new CellColor(112, 48, 160)));
    }

    private static void AssertValidation(
        FormatCellsDialogInput input,
        FormatCellsDialogPlannerTab expectedTab,
        FormatCellsDialogValidationTarget expectedTarget,
        string expectedMessageResourceKey)
    {
        FormatCellsDialogPlanner.TryCreateResult(new CellStyle(), input, out var result, out var validation)
            .Should()
            .BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new FormatCellsDialogValidation(
            expectedTab,
            expectedTarget,
            expectedMessageResourceKey));
    }

    private static FormatCellsDialogInput ValidInput() =>
        new(
            Number: new FormatCellsDialogNumberInput(
                Category: "Currency",
                FormatText: "Currency ($#,##0.00)",
                FormatSelectedIndex: 0,
                DecimalPlacesText: "3",
                Symbol: "EUR",
                NegativeIndex: 2),
            Font: new FormatCellsDialogFontInput(
                Labels,
                FontNameText: "  Verdana  ",
                SelectedFontName: null,
                FontSizeText: "13.5",
                FontStyleLabel: "Bold Italic",
                UnderlineLabel: "Double Accounting",
                DoubleUnderline: false,
                Strikethrough: true,
                Superscript: true,
                Subscript: false,
                FontColorText: "#C00000"),
            Fill: new FormatCellsDialogFillInput(
                FillColorText: "#00B050",
                FillPatternColorText: "#5B9BD5",
                FillPatternStyle: CellFillPatternStyle.DarkGrid,
                ClearFill: false),
            Alignment: new FormatCellsDialogAlignmentInput(
                HorizontalAlignmentText: nameof(CellHAlign.Right),
                VerticalAlignmentText: nameof(CellVAlign.Center),
                WrapText: true,
                ShrinkToFit: true,
                IndentLevelText: "7",
                TextRotationText: "-45",
                InitialMergeCells: false,
                MergeCells: true),
            Border: new FormatCellsDialogBorderInput(
                LineColorText: "#7030A0",
                Top: new FormatCellsDialogBorderSideInput(nameof(BorderStyle.Thin), "#FFC000"),
                Right: new FormatCellsDialogBorderSideInput(nameof(BorderStyle.Medium), "#4472C4"),
                Bottom: new FormatCellsDialogBorderSideInput(nameof(BorderStyle.Dashed), "#70AD47"),
                Left: new FormatCellsDialogBorderSideInput(nameof(BorderStyle.Double), "#ED7D31"),
                ClearPresetRequested: false,
                OutlinePreset: null,
                InsidePreset: null),
            Protection: new FormatCellsDialogProtectionInput(
                Locked: false,
                Hidden: true));

    private static FormatCellsDialogFontLabels Labels { get; } =
        new(
            Regular: "Regular",
            Italic: "Italic",
            Bold: "Bold",
            BoldItalic: "Bold Italic",
            UnderlineNone: "None",
            UnderlineSingle: "Single",
            UnderlineDouble: "Double",
            UnderlineSingleAccounting: "Single Accounting",
            UnderlineDoubleAccounting: "Double Accounting");
}
