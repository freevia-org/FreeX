using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using Xunit;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R156 freew-document-protection F1: picking an item from a drop-down-list/combo-box content control's
/// popup menu, or a relative date from a date-picker content control's popup menu, must route through
/// <see cref="DocumentView"/>'s protection-revalidating, undo-tracked model command
/// (ApplyContentControlMenuCommand -&gt; ApplyContentControlInteraction -&gt; ReplaceContentControlRunCommand
/// via the command bus) on every reachable protection state, not only Word's "Filling in Forms"
/// restriction. Before the fix, the click handler's <c>if (owner.RestrictEditingPolicy
/// .IsFormFieldEditingOnly &amp;&amp; ...)</c> gate meant an unprotected document -- the common case -- fell
/// through to a hand-written fallback that writes the WPF run's text directly and re-derives the whole
/// model via <see cref="DocumentView.CommitToModel"/>, which performs no undo/redo tracking at all (it
/// just clears and rebuilds <c>_model.Blocks</c> from the rendered document). The distinguishing,
/// observable symptom is therefore <see cref="DocumentView.CanUndo"/>: the command-bus path leaves an
/// undo entry, the bypassed fallback does not. Mirrors the real click plumbing (<c>MouseLeftButtonUp</c>
/// -&gt; OnListControlClicked/OnDatePickerClicked -&gt; OpenContentControlMenu -&gt; a real WPF ContextMenu ->
/// a real MenuItem.Click), exactly as <see cref="CheckBoxContentControlTests"/> does for the sibling
/// checkbox gesture the same class of bug was already fixed for (round 153). Runs on an STA thread
/// (<c>[StaFact]</c>, via Xunit.StaFact) because the RichTextBox/FlowDocument/ContextMenu need STA.
/// </summary>
public sealed class DropDownContentControlMenuTests
{
    private static DocumentView LoadDropDown(out WpfRun wpf, ProtectionMode protection = ProtectionMode.None)
    {
        var items = new[]
        {
            new ContentControlListItem("Red", "red"),
            new ContentControlListItem("Green", "green"),
        };
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.DropDownListControl(items, tag: "Color"));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);
        if (protection != ProtectionMode.None)
            view.SetProtection(protection);

        wpf = SingleContentControlRun(view);
        return view;
    }

    private static DocumentView LoadDatePicker(out WpfRun wpf)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.DatePickerControl("2020-01-01", tag: "Signed", dateFormat: "yyyy-MM-dd"));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);

        wpf = SingleContentControlRun(view);
        return view;
    }

    private static WpfRun SingleContentControlRun(DocumentView view) =>
        view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<WpfRun>()
            .Single();

    // A real left-button-up on the control run, exactly the event OnListControlClicked/
    // OnDatePickerClicked are wired to via `wpf.MouseLeftButtonUp += ...`.
    private static void Click(WpfRun wpf) =>
        wpf.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });

    // Every open MenuItem reachable from the current thread's PresentationSources (OpenContentControlMenu
    // opens the ContextMenu with `menu.IsOpen = true` rather than assigning it to a
    // FrameworkElement.ContextMenu property, so it is only reachable this way). Xunit.StaFact runs each
    // [StaFact] on its own STA thread, and PresentationSource.CurrentSources is a process-wide static list
    // that keeps entries from earlier tests' now-dead threads around -- CheckAccess() filters those out so
    // walking their stale visual trees does not throw "different thread owns it".
    private static System.Collections.Generic.List<MenuItem> OpenMenuItems()
    {
        var found = new System.Collections.Generic.List<MenuItem>();
        foreach (var source in PresentationSource.CurrentSources.OfType<PresentationSource>())
        {
            if (source.RootVisual is Visual root && root.CheckAccess())
                Walk(root, found);
        }
        return found;
    }

    // Finds the real WPF MenuItem the click opened. See <see cref="OpenMenuItems"/>.
    private static MenuItem FindOpenMenuItem(string header) =>
        OpenMenuItems().Single(item => item.Header?.ToString() == header);

    private static void Walk(DependencyObject node, System.Collections.Generic.List<MenuItem> found)
    {
        if (node is MenuItem mi)
            found.Add(mi);
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(node, i), found);
    }

    private static string RunTextAt(DocumentView view, int blockIndex, int runIndex) =>
        ((Paragraph)view.Model.Blocks[blockIndex]).Runs[runIndex].Text;

    [StaFact]
    public void DropDownMenuSelection_OnUnprotectedDocument_RoutesThroughUndoTrackedCommandBus()
    {
        var view = LoadDropDown(out var wpf);
        view.ProtectionMode.Should().Be(ProtectionMode.None, "the document must be unprotected for this to be the ordinary gesture");
        view.CanUndo.Should().BeFalse("no edits have happened yet");

        Click(wpf);
        FindOpenMenuItem("Green").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        RunTextAt(view, 0, 0).Should().Be("Green", "the selection must still update the field's displayed text");
        view.CanUndo.Should().BeTrue(
            "an ordinary click on an unprotected document must route through the undo-tracked command bus " +
            "(ApplyContentControlMenuCommand), not the hand-written CommitToModel fallback, which records no undo entry");

        view.Undo();
        RunTextAt(view, 0, 0).Should().Be("Red", "undo must cleanly revert the command-bus-tracked selection");
    }

    [StaFact]
    public void DatePickerMenuSelection_OnUnprotectedDocument_RoutesThroughUndoTrackedCommandBus()
    {
        var view = LoadDatePicker(out var wpf);
        view.ProtectionMode.Should().Be(ProtectionMode.None);
        view.CanUndo.Should().BeFalse();

        Click(wpf);
        var today = System.DateTime.Today.ToString("yyyy-MM-dd");
        FindOpenMenuItem($"Today ({today})").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        RunTextAt(view, 0, 0).Should().Be(today);
        view.CanUndo.Should().BeTrue(
            "an ordinary click on an unprotected document's date picker must also route through the " +
            "undo-tracked command bus, matching the drop-down/combo-box case");
    }

    [StaFact]
    public void DropDownMenuSelection_OnFillingFormsProtectedDocument_StillRoutesThroughUndoTrackedCommandBus()
    {
        // Sibling no-regression: the one reachable state that already worked before this fix (Word's
        // "Filling in Forms" restriction) must keep working exactly as before.
        var view = LoadDropDown(out var wpf, ProtectionMode.FillingForms);
        view.RestrictEditingPolicy.IsFormFieldEditingOnly.Should().BeTrue();

        Click(wpf);
        FindOpenMenuItem("Green").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        RunTextAt(view, 0, 0).Should().Be("Green");
        view.CanUndo.Should().BeTrue("Filling Forms already routed through the command bus before this fix and must still do so");
    }

    [StaFact]
    public void DropDownMenuSelection_OnTrackChangesOnlyProtectedDocument_RoutesThroughUndoTrackedCommandBus()
    {
        // The finding's other named reachable state besides "unprotected": a document restricted to
        // "Track changes only" allows FormFieldEdit (RestrictEditingEnforcementPolicy.TrackChangesDecision
        // does not require BodyTextEdit-style tracking for FormFieldEdit), so this must also route through
        // the command bus, not the untracked fallback.
        var view = LoadDropDown(out var wpf, ProtectionMode.TrackChangesOnly);
        view.RestrictEditingPolicy.IsFormFieldEditingOnly.Should().BeFalse(
            "Track Changes Only is not Filling-in-Forms -- this must not depend on that flag");
        view.RestrictEditingPolicy.Allows(RestrictEditingOperationKind.FormFieldEdit).Should().BeTrue();

        Click(wpf);
        FindOpenMenuItem("Green").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        RunTextAt(view, 0, 0).Should().Be("Green");
        view.CanUndo.Should().BeTrue(
            "Track Changes Only allows the field edit and must route through the undo-tracked command bus");
    }

    [StaFact]
    public void DropDownMenuSelection_OnReadOnlyProtectedDocument_NeverOpensTheMenu()
    {
        // Adjacent case: a protection state that blocks FormFieldEdit entirely must still block it after
        // removing the IsFormFieldEditingOnly gate -- this is enforced by the same
        // AllowsContentControlInteraction/CanEditExistingContentControl check at the top of
        // OpenContentControlMenu (unrelated to the gate this fix removed), so the click must not even
        // open a menu, let alone apply an edit through either path.
        var view = LoadDropDown(out var wpf, ProtectionMode.ReadOnly);
        view.RestrictEditingPolicy.Allows(RestrictEditingOperationKind.FormFieldEdit).Should().BeFalse();

        Click(wpf);

        OpenMenuItems().Should().BeEmpty("a document that blocks FormFieldEdit must not open the content-control popup menu at all");
        RunTextAt(view, 0, 0).Should().Be("Red");
        view.CanUndo.Should().BeFalse();
    }
}
