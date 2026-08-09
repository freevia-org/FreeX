using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Localization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal static class PageLayoutDialogChrome
{
    internal static readonly AvaloniaCompactDialogChromeStyle Style = AvaloniaCompactDialogChrome.WindowsStyle;

    internal static TextBox NumberBox(
        string text,
        double width = 110,
        AvaloniaCompactDialogChromeStyle? style = null,
        bool stretch = false)
    {
        var box = stretch
            ? new TextBox { Text = text, MinWidth = width }
            : new TextBox { Text = text, Width = width };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, style ?? Style);
        return box;
    }

    internal static ComboBox Combo(
        IEnumerable<string> items,
        int selectedIndex,
        double minWidth = 150,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        var combo = new ComboBox
        {
            ItemsSource = items.ToArray(),
            SelectedIndex = selectedIndex,
            MinWidth = minWidth
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(combo, style ?? Style);
        return combo;
    }

    internal static TextBlock Status() => new() { IsVisible = false };

    internal static Control Actions(
        Action accept,
        Action cancel,
        AvaloniaCompactDialogChromeStyle? style = null,
        double buttonWidth = 84)
    {
        style ??= Style;
        return AvaloniaDialogButtonRowFactory.CreateOkCancel(
            accept,
            cancel,
            buttonWidth,
            new Thickness(0, 14, 0, 0),
            style);
    }

    internal static void Configure(Window window, string title, double width)
    {
        window.Title = title;
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.CanResize = false;
        window.ShowInTaskbar = false;
    }

    internal static void WireEscape<TResult>(Window window)
    {
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            window.Close(default(TResult));
            e.Handled = true;
        };
    }

    internal static Control Row(string label, Control field)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(field, 1);
        grid.Children.Add(text);
        grid.Children.Add(field);
        return grid;
    }

    internal static void ShowError(TextBlock status, string message)
    {
        status.Text = message;
        AvaloniaCompactDialogChrome.ApplyValidationStatus(status, Style, new Thickness(0, 8, 0, 0));
        status.IsVisible = true;
    }

    internal static void FocusAndSelect(TextBox box)
    {
        box.Focus();
        box.SelectionStart = 0;
        box.SelectionEnd = box.Text?.Length ?? 0;
    }
}

public sealed class ColumnsDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private readonly ComboBox _preset;
    private readonly TextBox _count;
    private readonly TextBox _spacing;
    private readonly CheckBox _lineBetween;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();
    private readonly double _contentWidthPt;

    public ColumnsDialog(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);
        PageLayoutDialogChrome.Configure(this, ColumnsDialogPlanner.Title, 340);
        var state = ColumnsDialogPlanner.BuildInitialState(page, DialogCulture);
        _contentWidthPt = state.ContentWidthPt;
        _preset = PageLayoutDialogChrome.Combo(ColumnsDialogPlanner.Presets.Select(item => item.Label), state.PresetIndex);
        _count = PageLayoutDialogChrome.NumberBox(state.CountText);
        _spacing = PageLayoutDialogChrome.NumberBox(state.SpacingText);
        _lineBetween = new CheckBox { Content = ColumnsDialogPlanner.LineBetweenLabel, IsChecked = state.LineBetween, Margin = new Thickness(0, 8, 0, 0) };
        AvaloniaCompactDialogChrome.ApplyCheckBox(_lineBetween, PageLayoutDialogChrome.Style);
        _preset.SelectionChanged += (_, _) =>
            _count.Text = ColumnsDialogPlanner.ColumnCountForPreset(_preset.SelectedIndex).ToString(DialogCulture);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(PageLayoutDialogChrome.Row(ColumnsDialogPlanner.PresetsLabel, _preset));
        content.Children.Add(PageLayoutDialogChrome.Row(ColumnsDialogPlanner.CountLabel, _count));
        content.Children.Add(PageLayoutDialogChrome.Row(ColumnsDialogPlanner.SpacingLabel, _spacing));
        content.Children.Add(_lineBetween);
        content.Children.Add(_status);
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_count);
        PageLayoutDialogChrome.WireEscape<ColumnsDialogResult?>(this);
    }

    private void Accept()
    {
        var input = new ColumnsDialogInput(
            _preset.SelectedIndex,
            _count.Text,
            _spacing.Text,
            _lineBetween.IsChecked == true,
            _contentWidthPt);
        if (!ColumnsDialogPlanner.TryBuildResult(input, DialogCulture, out var result, out var error))
        {
            PageLayoutDialogChrome.ShowError(_status, error ?? ColumnsDialogPlanner.ValidationMessage);
            return;
        }
        Close(result);
    }

    public static void ApplyResult(DocumentView editor, ColumnsDialogResult result) =>
        editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyColumnsResult(page, result));

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        var result = await new ColumnsDialog(editor.Document.Page).ShowDialog<ColumnsDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
        editor.Focus();
    }
}

public sealed class CustomParagraphSpacingDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private readonly TextBox _before;
    private readonly TextBox _after;
    private readonly TextBox _line;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public DocumentParagraphSpacingSet? Result { get; private set; }

    public CustomParagraphSpacingDialog(DocumentParagraphSpacingSet? current)
    {
        PageLayoutDialogChrome.Configure(this, CustomParagraphSpacingDialogPlanner.Title, 380);
        var state = CustomParagraphSpacingDialogPlanner.BuildInitialState(current, DialogCulture);
        _before = PageLayoutDialogChrome.NumberBox(state.SpaceBeforeText);
        _after = PageLayoutDialogChrome.NumberBox(state.SpaceAfterText);
        _line = PageLayoutDialogChrome.NumberBox(state.LineSpacingText);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock
        {
            Text = CustomParagraphSpacingDialogPlanner.Hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(PageLayoutDialogChrome.Row(CustomParagraphSpacingDialogPlanner.SpaceBeforeLabel, _before));
        content.Children.Add(PageLayoutDialogChrome.Row(CustomParagraphSpacingDialogPlanner.SpaceAfterLabel, _after));
        content.Children.Add(PageLayoutDialogChrome.Row(CustomParagraphSpacingDialogPlanner.LineSpacingLabel, _line));
        content.Children.Add(_status);
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_before);
        PageLayoutDialogChrome.WireEscape<DocumentParagraphSpacingSet?>(this);
    }

    internal bool AcceptForTests() => TryAccept(closeOnSuccess: false);

    private void Accept() => TryAccept(closeOnSuccess: true);

    private bool TryAccept(bool closeOnSuccess)
    {
        if (!CustomParagraphSpacingDialogPlanner.TryBuildResult(
                new CustomParagraphSpacingDialogInput(_before.Text, _after.Text, _line.Text),
                DialogCulture,
                out var result,
                out var validation))
        {
            PageLayoutDialogChrome.ShowError(_status, validation?.Message ?? CustomParagraphSpacingDialogPlanner.LineSpacingValidationMessage);
            var target = validation?.Field switch
            {
                CustomParagraphSpacingDialogField.SpaceAfter => _after,
                CustomParagraphSpacingDialogField.LineSpacing => _line,
                _ => _before
            };
            PageLayoutDialogChrome.FocusAndSelect(target);
            return false;
        }

        Result = result;
        if (closeOnSuccess)
            Close(result);
        return true;
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        var result = await new CustomParagraphSpacingDialog(DocumentParagraphSpacingSet.Default)
            .ShowDialog<DocumentParagraphSpacingSet?>(owner);
        if (result is not null)
            editor.ApplyParagraphSpacingSet(result);
        editor.Focus();
    }
}

public sealed class DropCapOptionsDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private readonly RadioButton _none;
    private readonly RadioButton _dropped;
    private readonly RadioButton _inMargin;
    private readonly ComboBox _font;
    private readonly TextBox _lines;
    private readonly TextBox _distance;

    public DropCapOptionsDialog()
    {
        PageLayoutDialogChrome.Configure(this, DropCapOptionsDialogPlanner.Title, 340);
        var state = DropCapOptionsDialogPlanner.BuildInitialState(DialogCulture);
        _none = PositionButton(DropCapOptionsDialogPlanner.NoneLabel);
        _dropped = PositionButton(DropCapOptionsDialogPlanner.DroppedLabel);
        _inMargin = PositionButton(DropCapOptionsDialogPlanner.InMarginLabel);
        new[] { _none, _dropped, _inMargin }[state.PositionIndex].IsChecked = true;
        _font = PageLayoutDialogChrome.Combo(DropCapOptionsDialogPlanner.FontNames, state.FontIndex, 170);
        _font.IsEditable = true;
        _lines = PageLayoutDialogChrome.NumberBox(state.LinesToDropText, 70);
        _distance = PageLayoutDialogChrome.NumberBox(state.DistanceFromTextText, 70);

        var positions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        positions.Children.Add(_none);
        positions.Children.Add(_dropped);
        positions.Children.Add(_inMargin);
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = DropCapOptionsDialogPlanner.PositionLabel, FontWeight = FontWeight.SemiBold });
        content.Children.Add(positions);
        content.Children.Add(PageLayoutDialogChrome.Row(DropCapOptionsDialogPlanner.FontLabel, _font));
        content.Children.Add(PageLayoutDialogChrome.Row(DropCapOptionsDialogPlanner.LinesToDropLabel, _lines));
        content.Children.Add(PageLayoutDialogChrome.Row(DropCapOptionsDialogPlanner.DistanceFromTextLabel, _distance));
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_lines);
        PageLayoutDialogChrome.WireEscape<DropCapOptionsDialogResult?>(this);
    }

    private static RadioButton PositionButton(string label)
    {
        var button = new RadioButton { Content = label, GroupName = "DropCapPosition", Margin = new Thickness(0, 0, 14, 0) };
        AvaloniaCompactDialogChrome.ApplyRadioButton(button, PageLayoutDialogChrome.Style);
        return button;
    }

    private void Accept()
    {
        var index = _none.IsChecked == true
            ? (int)DropCapDialogPosition.None
            : _inMargin.IsChecked == true
                ? (int)DropCapDialogPosition.InMargin
                : (int)DropCapDialogPosition.Dropped;
        Close(DropCapOptionsDialogPlanner.BuildResult(
            new DropCapOptionsDialogInput(index, _font.Text, _lines.Text, _distance.Text),
            DialogCulture));
    }

    public static void ApplyResult(DocumentView editor, DropCapOptionsDialogResult result)
    {
        if (result.Position == DropCapDialogPosition.None)
            editor.ClearDropCap();
        else
            editor.ApplyDropCap(result.ModelPosition, result.SizePt, result.LinesToDrop, result.DistanceFromTextPt);
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        var result = await new DropCapOptionsDialog().ShowDialog<DropCapOptionsDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
        editor.Focus();
    }
}

public sealed class HyphenationOptionsDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private readonly CheckBox _automatic;
    private readonly TextBox _zone;
    private readonly TextBox _limit;
    private readonly CheckBox _caps;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public HyphenationOptionsDialog(PageSettings page)
    {
        PageLayoutDialogChrome.Configure(this, HyphenationOptionsDialogPlanner.Title, 410);
        var state = HyphenationOptionsDialogPlanner.BuildInitialState(page, DialogCulture);
        _automatic = Check(HyphenationOptionsDialogPlanner.AutomaticLabel, state.AutoHyphenation);
        _zone = PageLayoutDialogChrome.NumberBox(state.ZoneText);
        _limit = PageLayoutDialogChrome.NumberBox(state.ConsecutiveLimitText);
        _caps = Check(HyphenationOptionsDialogPlanner.HyphenateCapsLabel, state.HyphenateCaps);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(_automatic);
        content.Children.Add(PageLayoutDialogChrome.Row(HyphenationOptionsDialogPlanner.ZoneLabel, _zone));
        content.Children.Add(PageLayoutDialogChrome.Row(HyphenationOptionsDialogPlanner.ConsecutiveLimitLabel, _limit));
        content.Children.Add(_caps);
        content.Children.Add(_status);
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_zone);
        PageLayoutDialogChrome.WireEscape<HyphenationOptionsDialogResult?>(this);
    }

    private static CheckBox Check(string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 0) };
        AvaloniaCompactDialogChrome.ApplyCheckBox(box, PageLayoutDialogChrome.Style);
        return box;
    }

    private void Accept()
    {
        var input = new HyphenationOptionsDialogInput(
            _automatic.IsChecked == true,
            _zone.Text,
            _limit.Text,
            _caps.IsChecked == true);
        if (!HyphenationOptionsDialogPlanner.TryBuildResult(input, DialogCulture, out var result, out var error))
        {
            PageLayoutDialogChrome.ShowError(_status, error ?? HyphenationOptionsDialogPlanner.ValidationMessage);
            return;
        }
        Close(result);
    }

    public static void ApplyResult(DocumentView editor, HyphenationOptionsDialogResult result) =>
        editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyHyphenationOptions(page, result));

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        var result = await new HyphenationOptionsDialog(editor.Document.Page)
            .ShowDialog<HyphenationOptionsDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
        editor.Focus();
    }
}

public sealed class ManualHyphenationDialog : FreeWDialogWindow
{
    private readonly ComboBox _choices;

    public ManualHyphenationDialog(ManualHyphenationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        PageLayoutDialogChrome.Configure(this, "Manual Hyphenation", 380);

        _choices = new ComboBox { SelectedIndex = 0, MinWidth = 230 };
        foreach (var option in candidate.Options)
            _choices.Items.Add(new ComboBoxItem { Content = option.DisplayText, Tag = option });
        AvaloniaCompactDialogChrome.ApplyComboBox(_choices, PageLayoutDialogChrome.Style);

        var yes = Button("Yes", isDefault: true, () =>
        {
            if (_choices.SelectedItem is ComboBoxItem { Tag: ManualHyphenationOption option })
                Close(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Accept, option.BreakPoint));
        });
        var no = Button("No", isDefault: false, () =>
            Close(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Skip)));
        var cancel = Button("Cancel", isDefault: false, () =>
            Close(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Cancel)));
        cancel.IsCancel = true;

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [yes, no, cancel],
            new Thickness(0, 16, 0, 0));

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = $"Word {candidate.Number}", Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock { Text = candidate.Word, FontWeight = FontWeight.SemiBold, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = "Hyphenate at:", Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(_choices);
        content.Children.Add(buttons);
        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Cancel));
            e.Handled = true;
        };
    }

    private static Button Button(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, MinWidth = 72, IsDefault = isDefault };
        button.Click += (_, _) => action();
        AvaloniaCompactDialogChrome.ApplyButton(button, PageLayoutDialogChrome.Style, 72, isDefault);
        return button;
    }

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(report);

        var session = ManualHyphenationPlanner.CreateSession(editor.Document);
        if (session.CandidateCount == 0)
        {
            report("Manual hyphenation found no words to review.");
            editor.Focus();
            return;
        }

        while (!session.IsComplete)
        {
            var result = await new ManualHyphenationDialog(session.Current!)
                .ShowDialog<ManualHyphenationDialogResult?>(owner);
            if (result is null || result.Action == ManualHyphenationDialogAction.Cancel)
                break;
            if (result.Action == ManualHyphenationDialogAction.Accept && result.BreakPoint is int breakPoint)
                session.Accept(breakPoint);
            else
                session.Skip();
        }

        editor.ApplyManualHyphenation(session.Edits);
        report(session.AcceptedCount == 0
            ? "Manual hyphenation made no changes."
            : $"Manual hyphenation inserted breaks in {session.AcceptedCount} word(s).");
        editor.Focus();
    }
}

public sealed class LineNumberOptionsDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private readonly TextBox _startAt;
    private readonly TextBox _countBy;
    private readonly ComboBox _mode;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public LineNumberOptionsDialog(PageSettings page)
    {
        PageLayoutDialogChrome.Configure(this, LineNumberOptionsDialogPlanner.Title, 340);
        var initialMode = page.LineNumberMode == LineNumberMode.None
            ? LineNumberMode.RestartEachPage
            : page.LineNumberMode;
        var state = LineNumberOptionsDialogPlanner.BuildInitialState(
            page.LineNumberStartAt,
            page.LineNumberCountBy,
            initialMode,
            DialogCulture);
        _startAt = PageLayoutDialogChrome.NumberBox(state.StartAtText);
        _countBy = PageLayoutDialogChrome.NumberBox(state.CountByText);
        _mode = PageLayoutDialogChrome.Combo(LineNumberOptionsDialogPlanner.ModeLabels, state.ModeIndex);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(PageLayoutDialogChrome.Row(LineNumberOptionsDialogPlanner.StartAtLabel, _startAt));
        content.Children.Add(PageLayoutDialogChrome.Row(LineNumberOptionsDialogPlanner.CountByLabel, _countBy));
        content.Children.Add(PageLayoutDialogChrome.Row(LineNumberOptionsDialogPlanner.NumberingLabel, _mode));
        content.Children.Add(_status);
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_startAt);
        PageLayoutDialogChrome.WireEscape<LineNumberOptionsDialogResult?>(this);
    }

    private void Accept()
    {
        var input = new LineNumberOptionsDialogInput(_startAt.Text, _countBy.Text, _mode.SelectedIndex);
        if (!LineNumberOptionsDialogPlanner.TryBuildResult(input, DialogCulture, out var result, out var error))
        {
            PageLayoutDialogChrome.ShowError(_status, error ?? LineNumberOptionsDialogPlanner.StartAtValidationMessage);
            return;
        }
        Close(result);
    }

    public static void ApplyResult(DocumentView editor, LineNumberOptionsDialogResult result) =>
        editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyLineNumberOptions(page, result));

    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        var result = await new LineNumberOptionsDialog(editor.Document.Page)
            .ShowDialog<LineNumberOptionsDialogResult?>(owner);
        if (result is not null)
            ApplyResult(editor, result);
        editor.Focus();
    }
}
