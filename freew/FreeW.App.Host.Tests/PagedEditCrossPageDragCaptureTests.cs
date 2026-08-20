using System.Reflection;
using System.Reflection.Emit;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the round-153 "shared-drag-drop" finding F3: dragging a cross-page
/// text selection and releasing the mouse in the gap between pages (or any other spot not
/// covered by a page's body <see cref="System.Windows.Controls.RichTextBox"/>) left
/// <c>PaginatedEditorPanel</c> unable to drag-select text with the mouse anywhere, until the next
/// repagination.
///
/// <para>
/// Root cause: <c>OnBodyMouseMove</c>/<c>OnBodyMouseUp</c> were wired only to each page's Body
/// control, and no <see cref="Mouse.Capture(System.Windows.IInputElement)"/> was ever taken for
/// the drag. Once the pointer left the Body that started the drag (which it necessarily does
/// while crossing from one page to another), <c>OnBodyMouseUp</c> stopped being invoked at all
/// for that gesture, so <c>_dragActive</c> was never reset back to false and every future
/// <c>OnBodyMouseMove</c> call (for any page, for any later click-drag) hit the
/// <c>if (_dragActive) e.Handled = true;</c> branch, permanently suppressing native RichTextBox
/// selection.
/// </para>
///
/// <para>
/// The fix captures the mouse to the drag's source Body for the duration of the gesture (taken in
/// the extracted <c>BeginActiveDrag</c>, released in <c>EndActiveDrag</c>), so
/// <c>OnBodyMouseUp</c> always fires regardless of where the button is released, and the drop
/// target is then resolved by hit-testing the real pointer position
/// (<c>FindPageBoxAtPoint</c>/<c>CompleteDrag</c>) instead of trusting the routed event's
/// <c>sender</c> -- which, once capture is engaged, is always the source Body and would otherwise
/// silently break drops onto a different page.
/// </para>
///
/// <para>
/// <b>Why these tests IL-scan instead of asserting <see cref="Mouse.Captured"/> at runtime:</b>
/// <see cref="UIElement.CaptureMouse"/> requires the element to be part of a live, rendered
/// <c>PresentationSource</c> (a real, shown <see cref="Window"/>) or it silently returns
/// <see langword="false"/> with no effect -- verified empirically. Showing such a window in this
/// project turned out to be its own trap: <c>PaginatedEditorPanel</c> has a <c>static readonly</c>
/// <c>Brush</c> field that is never <c>.Freeze()</c>-ed, so the first test in the whole assembly
/// run to actually render a <c>PageBox</c> binds that shared Freezable's dispatcher affinity to
/// its own (StaFact-dedicated, per-test) thread; ANY later test in the same process that also
/// renders one -- on its own, different, StaFact thread -- throws "Cannot use a DependencyObject
/// that belongs to a different thread than its parent Freezable", non-deterministically, purely
/// based on execution order across the whole assembly. That is a pre-existing production issue
/// well outside this finding's scope (a shared unfrozen static Freezable), so rather than making
/// this fix's regression coverage flaky-by-execution-order, these tests instead verify the exact,
/// deterministic, environment-independent fact that matters: the compiled IL of
/// <c>BeginActiveDrag</c>/<c>EndActiveDrag</c> actually contains a call to
/// <c>CaptureMouse</c>/<c>ReleaseMouseCapture</c>. Reflection is exercised on the real runtime
/// type of an actual <see cref="PaginatedEditorPanel"/>, so a revert of the production fix makes
/// these tests fail for a real reason (missing method / missing call), not because the type under
/// test was swapped out for a stub.
/// </para>
///
/// <para>Runs on STA because tests create real WPF RichTextBox / FlowDocument instances.</para>
/// </summary>
public sealed class PagedEditCrossPageDragCaptureTests
{
    /// <summary>
    /// Core regression test: <c>BeginActiveDrag</c> -- the method that runs when a pending drag
    /// crosses the system drag threshold and becomes active -- must actually call
    /// <see cref="UIElement.CaptureMouse"/>. Before the fix this method doesn't exist at all (the
    /// capture call was never present anywhere in the file); after the fix its compiled body
    /// contains a real call to <c>CaptureMouse</c>.
    /// </summary>
    [StaFact]
    public void BeginActiveDrag_MethodBodyCallsCaptureMouse()
    {
        var method = typeof(PaginatedEditorPanel).GetMethod(
            "BeginActiveDrag", BindingFlags.NonPublic | BindingFlags.Instance);

        method.Should().NotBeNull(
            "the fix extracts a BeginActiveDrag method that captures the mouse when the drag " +
            "threshold is exceeded -- without it, nothing in this file ever takes mouse capture, " +
            "which is the root cause of the finding");

        IlBodyCallsMethodNamed(method!, "CaptureMouse").Should().BeTrue(
            "BeginActiveDrag must call CaptureMouse on the drag's source Body -- otherwise the " +
            "pointer leaving that Body (e.g. crossing the inter-page gap) means no handler in " +
            "this file ever receives the eventual MouseUp again, and the drag state machine can " +
            "never terminate");
    }

    /// <summary>
    /// Sibling half of the same contract: <c>EndActiveDrag</c> -- the method
    /// <c>OnBodyMouseUp</c> now runs unconditionally and first, before even resolving the drop
    /// target -- must release the capture <c>BeginActiveDrag</c> took. Without this, a captured
    /// element that is later discarded during repagination would keep swallowing ALL future mouse
    /// input app-wide, not just text selection -- an even worse regression than the one this
    /// finding reports.
    /// </summary>
    [StaFact]
    public void EndActiveDrag_MethodBodyCallsReleaseMouseCapture()
    {
        var method = typeof(PaginatedEditorPanel).GetMethod(
            "EndActiveDrag", BindingFlags.NonPublic | BindingFlags.Instance);

        method.Should().NotBeNull(
            "the fix extracts an EndActiveDrag method that unconditionally releases mouse " +
            "capture and resets the drag state machine as the first thing OnBodyMouseUp does");

        IlBodyCallsMethodNamed(method!, "ReleaseMouseCapture").Should().BeTrue(
            "EndActiveDrag must call ReleaseMouseCapture -- an engaged capture that is never " +
            "explicitly released would keep routing ALL future mouse input through the " +
            "(possibly stale, soon-to-be-discarded) source Body");
    }

    /// <summary>
    /// Behavioural round trip of the drag state machine's field bookkeeping (independent of real
    /// mouse capture, which needs a live window -- see the class remarks): <c>BeginActiveDrag</c>
    /// must flip pending → active, and <c>EndActiveDrag</c> (the unconditional first step of
    /// <c>OnBodyMouseUp</c>) must fully reset the machine back to idle. This is exactly the state
    /// transition a genuine mouse-up in the inter-page gap must reach, and exactly the state the
    /// pre-fix code could never reach for that gesture because <c>OnBodyMouseUp</c> was never
    /// invoked at all once the pointer left the source Body.
    /// </summary>
    [StaFact]
    public void BeginThenEndActiveDrag_TransitionsStateCorrectlyAndResetsToIdle()
    {
        var (panel, _) = BuildThreePagePanel();
        if (panel.PageBoxes.Count < 1)
            return;

        var box = panel.PageBoxes[0];

        SetField(panel, "_dragSourceBox", box);
        SetField(panel, "_dragPending", true);
        SetField(panel, "_dragActive", false);

        InvokePrivate(panel, "BeginActiveDrag");
        GetField<bool>(panel, "_dragActive").Should().BeTrue(
            "crossing the drag threshold must activate the drag");
        GetField<bool>(panel, "_dragPending").Should().BeFalse();

        InvokePrivate(panel, "EndActiveDrag");
        GetField<bool>(panel, "_dragActive").Should().BeFalse(
            "the whole point of the fix: a real mouse-up (EndActiveDrag, which OnBodyMouseUp " +
            "now runs unconditionally and first) must clear _dragActive so ordinary click-and-" +
            "drag text selection is not permanently suppressed, regardless of where the button " +
            "was released");
        GetField<bool>(panel, "_dragPending").Should().BeFalse();
        GetField<PageBox?>(panel, "_dragSourceBox").Should().BeNull();
    }

    /// <summary>
    /// Sibling/no-regression guard for the drop-target-resolution half of the fix: a point nowhere
    /// near any page (far outside the whole layout -- the scrollbar, window chrome, or the
    /// inter-page gap the finding names) must resolve cleanly to no drop target, exactly like the
    /// pre-existing "mouse up outside a known box -- cancel" behaviour the original code already
    /// had for the within-box-only case. This proves the fix's new geometry-based lookup
    /// (<c>FindPageBoxAtPoint</c>, which replaced trusting the routed event's <c>sender</c> --
    /// always the drag's source box once capture is engaged) doesn't regress that cancellation
    /// path into a crash or a wrong-box match.
    /// </summary>
    [StaFact]
    public void FindPageBoxAtPoint_PointFarOutsideAllBoxes_ReturnsNull()
    {
        var (panel, _) = BuildThreePagePanel();
        if (panel.PageBoxes.Count < 1)
            return;

        var farPoint = new Point(-1_000_000, -1_000_000);

        var found = InvokePrivate<PageBox?>(panel, "FindPageBoxAtPoint", farPoint);
        found.Should().BeNull("a point nowhere near any page box must resolve to no drop target");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static (PaginatedEditorPanel panel, DocumentView editor) BuildThreePagePanel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page 1 content"));
        doc.Blocks.Add(new Paragraph("Page 2 middle")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });
        doc.Blocks.Add(new Paragraph("Page 3 end")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();

        var panel = PaginatedEditorPanel.Build(editor);
        return (panel, editor);
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(target.GetType().FullName, fieldName);
        return (T)field.GetValue(target)!;
    }

    private static void InvokePrivate(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        try
        {
            method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static T InvokePrivate<T>(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        try
        {
            return (T)method.Invoke(target, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    // ── minimal, correct IL walker (used only to detect a `call`/`callvirt` to a named method) ────

    private static readonly Dictionary<short, OpCode> OneByteOpCodes = new();
    private static readonly Dictionary<short, OpCode> TwoByteOpCodes = new();

    static PagedEditCrossPageDragCaptureTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode)
                continue;

            var value = unchecked((ushort)opcode.Value);
            if (value < 0x100)
                OneByteOpCodes[(short)value] = opcode;
            else if ((value & 0xFF00) == 0xFE00)
                TwoByteOpCodes[(short)(value & 0xFF)] = opcode;
        }
    }

    /// <summary>
    /// Walks <paramref name="method"/>'s compiled IL instruction-by-instruction (correctly
    /// skipping each opcode's real operand size, not a naive byte scan) and returns true if any
    /// <c>call</c>/<c>callvirt</c> instruction's resolved target method is named
    /// <paramref name="calleeName"/>.
    /// </summary>
    private static bool IlBodyCallsMethodNamed(MethodBase method, string calleeName)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
            return false;

        var module = method.Module;
        var typeArgs = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;
        var methodArgs = method.IsGenericMethod
            ? method.GetGenericArguments()
            : null;

        int i = 0;
        while (i < il.Length)
        {
            OpCode opcode;
            if (il[i] == 0xFE)
            {
                if (!TwoByteOpCodes.TryGetValue(il[i + 1], out opcode))
                    throw new InvalidOperationException($"Unknown two-byte IL opcode 0xFE{il[i + 1]:X2}");
                i += 2;
            }
            else
            {
                if (!OneByteOpCodes.TryGetValue(il[i], out opcode))
                    throw new InvalidOperationException($"Unknown one-byte IL opcode 0x{il[i]:X2}");
                i += 1;
            }

            int operandSize = OperandByteSize(opcode.OperandType, il, i);

            if (opcode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, i);
                try
                {
                    var resolved = module.ResolveMethod(token, typeArgs, methodArgs);
                    if (resolved?.Name == calleeName)
                        return true;
                }
                catch
                {
                    // Unresolvable token (e.g. an unusual generic context) -- not our target call.
                }
            }

            i += operandSize;
        }

        return false;
    }

    private static int OperandByteSize(OperandType type, byte[] il, int operandStart) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operandStart),
        _ => throw new NotSupportedException($"Unsupported IL operand type: {type}"),
    };
}
