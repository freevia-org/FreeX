using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record DateTimeDialogFormatChoice(string Label, string Text)
{
    public override string ToString() => Text;
}

public sealed record DateTimeDialogState(
    int SelectedIndex,
    bool UpdateAutomatically);

public sealed record DateTimeDialogResult(
    string Text,
    bool IsField,
    string? FieldInstruction);

/// <summary>
/// Owns the renderer-neutral format projection, selection state, and DATE/TIME field result
/// construction for the paired WPF and Avalonia Date and Time dialogs.
/// </summary>
public sealed class DateTimeDialogSession
{
    private readonly CultureInfo _culture;
    private DateTimeDialogState _state = new(SelectedIndex: 0, UpdateAutomatically: false);

    public DateTimeDialogSession(DateTime moment, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        _culture = culture;
        Formats = DateTimeFormats.Build(moment, culture)
            .Select(format => new DateTimeDialogFormatChoice(format.Label, format.Text))
            .ToArray();
    }

    public static string Title => FreeWUiTextCatalog.DateTimeTitle;
    public static string FormatsLabel => FreeWUiTextCatalog.DateTimeFormatsLabel;
    public static string UpdateAutomaticallyLabel => FreeWUiTextCatalog.DateTimeUpdateAutomatically;
    public static string UpdateAutomaticallyToolTip => FreeWUiTextCatalog.DateTimeUpdateAutomaticallyToolTip;

    public IReadOnlyList<DateTimeDialogFormatChoice> Formats { get; }

    public DateTimeDialogState State => _state;

    public DateTimeDialogState UpdateSelection(int selectedIndex)
    {
        _state = _state with { SelectedIndex = selectedIndex };
        return _state;
    }

    public DateTimeDialogState UpdateAutomatically(bool updateAutomatically)
    {
        _state = _state with { UpdateAutomatically = updateAutomatically };
        return _state;
    }

    public DateTimeDialogResult? PlanAcceptance()
    {
        if (_state.SelectedIndex < 0 || _state.SelectedIndex >= Formats.Count)
            return null;

        var selected = Formats[_state.SelectedIndex];
        if (!_state.UpdateAutomatically)
            return new DateTimeDialogResult(selected.Text, IsField: false, FieldInstruction: null);

        var keyword = _state.SelectedIndex is 2 or 3 ? "TIME" : "DATE";
        var picture = DateTimeFormats.BuildFieldPicture(_state.SelectedIndex, _culture);
        return new DateTimeDialogResult(
            selected.Text,
            IsField: true,
            FieldInstruction: $@" {keyword} \@ ""{picture}"" ");
    }
}
