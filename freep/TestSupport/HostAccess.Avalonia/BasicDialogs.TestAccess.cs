using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed partial class OptionsDialog
{
    internal TextBox RecentFilesCapForTest => _recentFilesCap;
    internal ComboBox DefaultFormatForTest => _defaultFormat;
    internal TextBox UiLanguageForTest => _uiLanguage;
    internal TextBlock StatusForTest => _status;
    internal void AcceptForTest() => Accept();
}

internal sealed partial class RotationOptionsDialog
{
    internal void SetRotationForTests(string text) => _rotationBox.Text = text;
    internal bool ApplyForTests() => Apply();
}

internal sealed partial class HyperlinkDialog
{
    internal bool ApplyInputForTests(
        HyperlinkDialogTargetKind targetKind,
        string url,
        int selectedSlideIndex,
        string tooltip)
    {
        var state = _session.SetInput(targetKind, url, selectedSlideIndex, tooltip);
        RenderInputState(state);
        return Apply();
    }
}

internal sealed partial class FindReplaceDialog
{
    internal string StatusText => _statusText.Text ?? string.Empty;

    internal FindReplaceWorkflowPlan SetInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        _findBox.Text = query ?? string.Empty;
        _replaceBox.Text = replacement ?? string.Empty;
        _matchCaseCheck.IsChecked = matchCase;
        _wholeWordCheck.IsChecked = wholeWord;
        return ApplyWorkflowPlan(_session.SetInput(query, replacement, matchCase, wholeWord));
    }

    internal FindReplaceWorkflowPlan NavigateForTests(int direction) =>
        ApplyWorkflowPlan(_session.Dispatch(
            direction < 0
                ? FindReplaceDialogAction.FindPrevious
                : FindReplaceDialogAction.FindNext));

    internal FindReplaceWorkflowPlan ReplaceAllForTests() =>
        ApplyWorkflowPlan(_session.Dispatch(FindReplaceDialogAction.ReplaceAll));
}
