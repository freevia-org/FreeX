using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// r170. RefreshInlineEditorPosition (MainWindow.Editing.cs) runs on every scroll pass, and its
/// "anchor is no longer in the viewport" branch force-committed the in-progress edit. That is the
/// right answer for an ordinary value edit, but it destroys the standard formula gesture: type
/// '=', scroll away, click a distant cell. The scroll itself ended the formula, so the click that
/// followed overwrote a cell instead of appending its reference.
///
/// The fix suspends the in-cell overlay during formula range entry -- the formula bar carries the
/// text and keeps point mode alive -- and restores it when the anchor scrolls back. This pins the
/// guard, because the branch is private WPF-host code with no reachable seam.
/// </summary>
public sealed class R170_InlineEditorOffscreenAnchorSourceHygieneTests
{
    [Fact]
    public void OffscreenAnchorBranch_SuspendsFormulaRangeEntryInsteadOfCommitting()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        source.Should().Contain(
            "if (IsFormulaReferenceHighlightActive(_inlineEditor))",
            "the offscreen-anchor branch must recognise an in-progress formula BEFORE committing");
        source.Should().Contain("SuspendInlineEditorForOffscreenAnchor();");
        source.Should().Contain("private void SuspendInlineEditorForOffscreenAnchor()");
        source.Should().Contain("private void RestoreInlineEditorAfterOffscreenAnchor()");
    }

    [Fact]
    public void SuspendedEditor_KeepsTheEditAliveRatherThanEndingIt()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");
        var suspend = ExtractMethod(source, "private void SuspendInlineEditorForOffscreenAnchor()");

        // Suspending must not run any of the sequences that END an edit: the whole point is that
        // _formulaEditCell and the text stay live so the next click still appends a reference.
        suspend.Should().NotContain("CommitEdit()");
        suspend.Should().NotContain("HideInlineEditor(");
        suspend.Should().NotContain("_formulaEditCell = null");

        // and it must hand the text to the formula bar, which is what keeps point mode active
        // (GetFormulaRangeEntryEditor falls back to FormulaBar when the inline editor is hidden).
        suspend.Should().Contain("FormulaBar.Text = _inlineEditor.Text;");
    }

    [Fact]
    public void OffscreenAnchorBranch_StillCommitsAnOrdinaryValueEdit()
    {
        // No-regression sibling: the original behaviour must survive for a non-formula edit.
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        source.Should().Contain("HideInlineEditor(commit: true);");
        source.Should().Contain("CommitEdit();");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "{0} must exist", signature);

        var depth = 0;
        var seenOpen = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
                seenOpen = true;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (seenOpen && depth == 0)
                    return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"unterminated method body for {signature}");
    }
}
