using FluentAssertions;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Host.Tests;

public sealed partial class CellStyleDiffPlannerTests
{
    [Fact]
    public void CellStylePreset_Normal_ClearsSupportedStyleFields()
    {
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Normal);

        diff.Should().Be(CellStyleDiffPlanner.ClearFormatsDiff());
    }

    [Fact]
    public void CellStylePreset_Normal_AppliesDefaultsToVisibleAndProtectionFields()
    {
        var styled = new CellStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            DoubleUnderline = true,
            Strikethrough = true,
            Superscript = true,
            FontName = "Aptos",
            FontSize = 18,
            FontColor = new CellColor(12, 34, 56),
            FillColor = new CellColor(90, 91, 92),
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = CellHAlign.Center,
            VerticalAlignment = CellVAlign.Top,
            WrapText = true,
            ShrinkToFit = true,
            IndentLevel = 3,
            TextRotation = 45,
            BorderTop = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
            BorderRight = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
            BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
            BorderLeft = new CellBorder(BorderStyle.Thick, new CellColor(1, 2, 3)),
            Locked = false
        };

        var result = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Normal).ApplyTo(styled);

        result.Should().Be(CellStyle.Default);
    }
}
