using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies a checkbox content control renders a visible ☒/☐ glyph in a symbol font (synthesised from the
/// checked state), rather than the body-font run text that showed nothing. Runs on STA.
/// </summary>
public sealed class CheckBoxRenderTests
{
    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
            {
                if (d is T t)
                    result.Add(t);
                result.AddRange(LogicalDescendants<T>(d));
            }
        return result;
    }

    private static System.Windows.Documents.Run? RenderedRunWithText(string text, bool @checked)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(Run.CheckBoxControl(@checked));
        doc.Blocks.Add(p);
        var view = new DocumentView();
        view.LoadModel(doc);
        return LogicalDescendants<System.Windows.Documents.Run>(view.Document)
            .FirstOrDefault(r => r.Text == text);
    }

    [StaFact]
    public void CheckedBox_RendersCrossedGlyphInSymbolFont()
    {
        var run = RenderedRunWithText(ContentControl.CheckedGlyph, @checked: true);
        Assert.NotNull(run);
        Assert.Equal("Segoe UI Symbol", run!.FontFamily.Source);
    }

    [StaFact]
    public void UncheckedBox_RendersEmptyGlyph()
    {
        var run = RenderedRunWithText(ContentControl.UncheckedGlyph, @checked: false);
        Assert.NotNull(run);
    }
}
