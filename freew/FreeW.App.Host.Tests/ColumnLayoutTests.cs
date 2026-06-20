using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies the editor flows body text into the page's multi-column layout: the rendered
/// <see cref="FlowDocument"/> picks up the column gap, the "line between" rule, and a finite column width
/// (so WPF lays out more than one column). Also checks that applying column settings mutates the model.
/// Runs on STA (WPF FlowDocument).
/// </summary>
public sealed class ColumnLayoutTests
{
    private static DocumentView ViewWith(PageSettings page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text that flows across the configured columns."));
        doc.Page.ColumnCount = page.ColumnCount;
        doc.Page.ColumnSpacingPt = page.ColumnSpacingPt;
        doc.Page.ColumnsLineBetween = page.ColumnsLineBetween;
        doc.Page.ColumnWidthsPt = page.ColumnWidthsPt;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void SingleColumn_LeavesFlowSpanningFullWidth()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 1 });

        Assert.True(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.Equal(0, view.Document.ColumnRuleWidth);
    }

    [StaFact]
    public void TwoColumns_FlowGetsFiniteWidthAndGap()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 2, ColumnSpacingPt = 24 });

        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.True(view.Document.ColumnWidth > 0);
        Assert.True(view.Document.ColumnGap > 0);
    }

    [StaFact]
    public void LineBetween_AddsAColumnRule()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 2, ColumnsLineBetween = true });

        Assert.True(view.Document.ColumnRuleWidth > 0);
    }

    [StaFact]
    public void ApplyPageSettings_UpdatesModelColumns()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 1 });

        view.ApplyPageSettings(page =>
        {
            page.ColumnCount = 3;
            page.ColumnSpacingPt = 18;
            page.ColumnsLineBetween = true;
        });

        Assert.Equal(3, view.Model.Page.ColumnCount);
        Assert.Equal(18, view.Model.Page.ColumnSpacingPt);
        Assert.True(view.Model.Page.ColumnsLineBetween);
        // The re-render picked up the new layout.
        Assert.True(view.Document.ColumnRuleWidth > 0);
        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
    }

    [StaFact]
    public void UnequalWidths_RenderMultipleColumns()
    {
        var view = ViewWith(new PageSettings { ColumnCount = 2, ColumnWidthsPt = [108.0, 360.0] });

        // The narrowest width drives the flexible column width so both columns fit the content area.
        Assert.False(double.IsPositiveInfinity(view.Document.ColumnWidth));
        Assert.True(view.Document.ColumnWidth > 0);
    }
}
