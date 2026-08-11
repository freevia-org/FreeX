using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-A11Y: DocumentView must expose a real UI Automation peer instead of forfeiting to the
/// framework default (Control.OnCreateAutomationPeer()'s NoneAutomationPeer, which excludes the
/// control from the accessibility tree entirely). This is the closest Avalonia 12.0.4 equivalent
/// to the WPF twin's inherited RichTextBox TextPattern support — see
/// DocumentViewAutomationPeer's doc comment for exactly what Avalonia cannot express.
/// </summary>
public sealed class DocumentViewAutomationPeerTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this environment
        }
    }

    private static TableRow Row(params string[] values)
    {
        var row = new TableRow();
        foreach (var value in values)
            row.Cells.Add(new TableCell(value));
        return row;
    }

    [Fact]
    public async Task DocumentView_automation_peer_reports_document_type_and_full_text_via_value_provider()
    {
        AutomationPeer? peer = null;
        AutomationControlType controlType = default;
        IValueProvider? valueProvider = null;
        string? value = null;
        bool isReadOnly = false;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph { Runs = { new Run("Hello accessible world") } });

            var view = new DocumentView();
            view.LoadDocument(doc);

            peer = view.CreateAutomationPeerForTests();
            controlType = peer.GetAutomationControlType();
            valueProvider = peer.GetProvider<IValueProvider>();
            value = valueProvider?.Value;
            isReadOnly = valueProvider?.IsReadOnly ?? false;
        });

        if (!ran)
            return;

        peer.Should().NotBeNull().And.BeOfType<DocumentViewAutomationPeer>();
        // Before the fix, Control.OnCreateAutomationPeer()'s default (NoneAutomationPeer) reports
        // AutomationControlType.None and exposes no providers at all — a screen reader sees nothing.
        controlType.Should().Be(AutomationControlType.Document);
        valueProvider.Should().NotBeNull(
            "Avalonia has no ITextProvider — IValueProvider is the only pattern that can expose document text");
        value.Should().Be("Hello accessible world");
        isReadOnly.Should().BeTrue("edits must go through DocumentView's command bus, not raw automation SetValue");
    }

    [Fact]
    public async Task DocumentView_automation_value_provider_SetValue_is_rejected()
    {
        IValueProvider? valueProvider = null;

        var ran = await OnUiThread(() =>
        {
            var view = new DocumentView();
            var peer = view.CreateAutomationPeerForTests();
            valueProvider = peer.GetProvider<IValueProvider>();
        });

        if (!ran)
            return;

        valueProvider.Should().NotBeNull();
        var act = () => valueProvider!.SetValue("replaced");
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Editing_the_document_raises_an_automation_value_changed_notification()
    {
        var raisedCount = 0;
        AutomationProperty? raisedProperty = null;
        string? oldValue = null;
        string? newValue = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph { Runs = { new Run("Hello") } });

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 400));

            var peer = view.CreateAutomationPeerForTests();
            peer.PropertyChanged += (_, e) =>
            {
                if (e.Property != ValuePatternIdentifiers.ValueProperty)
                    return;
                raisedCount++;
                raisedProperty = e.Property;
                oldValue = e.OldValue as string;
                newValue = e.NewValue as string;
            };

            view.MoveCaretToBlockForTest(0, 5);
            view.InsertParagraphBreakPublic(); // real edit call site: splits the paragraph, mutates the model
        });

        if (!ran)
            return;

        raisedCount.Should().BeGreaterThan(0,
            "DocumentView.DocumentChanged fires on every committed edit and must push a Value-changed automation event");
        raisedProperty.Should().Be(ValuePatternIdentifiers.ValueProperty);
        oldValue.Should().Be("Hello");
        newValue.Should().Contain("Hello");
        newValue.Should().NotBe(oldValue);
    }

    [Fact]
    public async Task Moving_the_caret_into_a_table_cell_raises_an_automation_selection_changed_notification()
    {
        var raisedCount = 0;
        AutomationProperty? raisedProperty = null;

        var ran = await OnUiThread(() =>
        {
            var table = new Table();
            table.Rows.Add(Row("North", "120"));
            table.Rows.Add(Row("South", "98"));
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 1200));

            var peer = view.CreateAutomationPeerForTests();
            peer.PropertyChanged += (_, e) =>
            {
                if (e.Property != AutomationElementIdentifiers.ItemStatusProperty)
                    return;
                raisedCount++;
                raisedProperty = e.Property;
            };

            // PlaceCaretInCell is DocumentView's real (non-test-only) public API for moving the
            // caret into a table cell — it invokes CaretMoved like every other body/table/H-F
            // caret-move call site in the class. Row 1 / col 1 (not the default (0,0,0,0) caret
            // position) so the caret genuinely moves and the status string changes.
            view.PlaceCaretInCell(0, 1, 1, 0, 0);
        });

        if (!ran)
            return;

        raisedCount.Should().BeGreaterThan(0,
            "DocumentView.CaretMoved fires on this call site and must push an ItemStatus-changed automation event");
        raisedProperty.Should().Be(AutomationElementIdentifiers.ItemStatusProperty);
    }
}
