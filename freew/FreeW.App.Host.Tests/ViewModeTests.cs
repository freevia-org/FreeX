using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the View &gt; Views document view modes (Print Layout / Web Layout / Draft) on the live
/// editing surface. Web Layout and Draft are continuous, full-width views with no page chrome (no page
/// sheet/margins/shadow/page-break markers); Print Layout restores the Word-style page sheet. Switching is
/// purely visual — the model is never mutated — and the three modes are mutually exclusive. Runs on STA
/// because it builds the real WPF <see cref="DocumentView"/>.
/// </summary>
public sealed class ViewModeTests
{
    private static DocumentView NewEditor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text"));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void Default_IsPrintLayout_WithPageChrome()
    {
        var view = NewEditor();

        view.ViewMode.Should().Be(DocumentViewMode.PrintLayout);
        view.PrintLayoutEnabled.Should().BeTrue();

        // Print Layout shows the page sheet: a fixed page width, a centred page, and the page drop shadow.
        view.Width.Should().BeGreaterThan(0, "Print Layout sizes the surface to the page width");
        double.IsNaN(view.Width).Should().BeFalse();
        view.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        view.Effect.Should().NotBeNull("the page sheet has a drop shadow in Print Layout");
    }

    [StaFact]
    public void PrintLayout_HonorsHiddenPageBoundariesWithoutChangingHorizontalMargins()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DoNotDisplayPageBoundaries = true;
        doc.Page.MarginLeftPt = 54;
        doc.Page.MarginRightPt = 63;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 81;

        var view = new DocumentView();
        view.LoadModel(doc);

        view.Padding.Left.Should().BeApproximately(72, 0.01);
        view.Padding.Right.Should().BeApproximately(84, 0.01);
        view.Padding.Top.Should().Be(0);
        view.Padding.Bottom.Should().Be(0);

        view.SetViewMode(DocumentViewMode.Draft);
        view.SetViewMode(DocumentViewMode.PrintLayout);
        view.Padding.Top.Should().Be(0);
        view.Padding.Bottom.Should().Be(0);
    }

    [StaFact]
    public void WebLayout_HidesPageChrome()
    {
        var view = NewEditor();

        view.SetViewMode(DocumentViewMode.WebLayout);

        view.ViewMode.Should().Be(DocumentViewMode.WebLayout);
        view.PrintLayoutEnabled.Should().BeFalse("Web Layout drops the page presentation");

        // No page sheet: the surface stretches full width (no width cap) and carries no page drop shadow.
        double.IsNaN(view.Width).Should().BeTrue("Web Layout lets the editor fill the window width");
        view.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);
        view.Effect.Should().BeNull("there is no page sheet to lift in Web Layout");
    }

    [StaFact]
    public void Draft_HidesPageChrome()
    {
        var view = NewEditor();

        view.SetViewMode(DocumentViewMode.Draft);

        view.ViewMode.Should().Be(DocumentViewMode.Draft);
        view.PrintLayoutEnabled.Should().BeFalse("Draft drops the page presentation");

        double.IsNaN(view.Width).Should().BeTrue("Draft lets the editor fill the window width");
        view.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);
        view.Effect.Should().BeNull("there is no page sheet to lift in Draft");
    }

    [StaFact]
    public void TogglingBackToPrintLayout_RestoresThePageSheet()
    {
        var view = NewEditor();

        view.SetViewMode(DocumentViewMode.WebLayout);
        view.PrintLayoutEnabled.Should().BeFalse();

        view.SetViewMode(DocumentViewMode.PrintLayout);

        view.ViewMode.Should().Be(DocumentViewMode.PrintLayout);
        view.PrintLayoutEnabled.Should().BeTrue("Print Layout restores the page sheet");
        double.IsNaN(view.Width).Should().BeFalse("the page width returns when Print Layout is restored");
        view.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        view.Effect.Should().NotBeNull("the page drop shadow returns with Print Layout");
    }

    [StaFact]
    public void ViewModes_AreMutuallyExclusive()
    {
        var view = NewEditor();

        // Each switch lands on exactly one mode — never a blend of two.
        foreach (var mode in new[]
                 {
                     DocumentViewMode.WebLayout,
                     DocumentViewMode.Draft,
                     DocumentViewMode.PrintLayout
                 })
        {
            view.SetViewMode(mode);
            view.ViewMode.Should().Be(mode);
            view.PrintLayoutEnabled.Should().Be(mode == DocumentViewMode.PrintLayout);
        }
    }

    [StaFact]
    public void SwitchingViewMode_DoesNotMutateTheModel()
    {
        var view = NewEditor();
        var before = view.Model.Blocks.Count;

        view.SetViewMode(DocumentViewMode.WebLayout);
        view.SetViewMode(DocumentViewMode.Draft);
        view.SetViewMode(DocumentViewMode.PrintLayout);

        view.CommitToModel();
        view.Model.Blocks.Count.Should().Be(before, "switching views is purely visual");
    }

    [StaFact]
    public void SetViewMode_SwitchesBetweenPrintLayoutAndDraft()
    {
        var view = NewEditor();

        view.SetViewMode(DocumentViewMode.Draft);
        view.PrintLayoutEnabled.Should().BeFalse();
        view.ViewMode.Should().Be(DocumentViewMode.Draft);

        view.SetViewMode(DocumentViewMode.PrintLayout);
        view.PrintLayoutEnabled.Should().BeTrue();
        view.ViewMode.Should().Be(DocumentViewMode.PrintLayout);
    }
}
