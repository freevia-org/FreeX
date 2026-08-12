using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal sealed partial class OptionsDialog
{
    internal TextBox RecentFilesCapForTest => _recentFilesCap;
    internal ComboBox DefaultFormatForTest => _defaultFormat;
    internal TextBox UiLanguageForTest => _uiLanguage;
    internal TextBlock StatusForTest => _status;
    internal void AcceptForTest() => Accept();
}

public sealed partial class RotationOptionsDialog
{
    internal void SetRotationForTests(string text) => _rotationBox.Text = text;
    internal bool ApplyForTests() => Apply(showValidation: false);
}

public sealed partial class HyperlinkDialog
{
    internal bool ApplyInputForTests(
        HyperlinkDialogTargetKind targetKind,
        string url,
        int selectedSlideIndex,
        string tooltip)
    {
        var state = _session.SetInput(targetKind, url, selectedSlideIndex, tooltip);
        RenderInputState(state);
        return Apply(showValidationDialog: false);
    }
}

public sealed partial class FindReplaceDialog
{
    internal FindReplaceWorkflowPlan LastWorkflowPlan => _session.LastWorkflowPlan;
    internal bool ShowReplace => _session.ShowReplace;
    internal string StatusText => _statusText.Text;

    internal FindReplaceWorkflowPlan SetInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        _findBox.Text = query ?? string.Empty;
        _replaceBox.Text = replacement ?? string.Empty;
        _matchCaseBox.IsChecked = matchCase;
        _wholeWordBox.IsChecked = wholeWord;
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
