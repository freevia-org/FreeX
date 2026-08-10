using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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

    internal static void Configure<TField>(
        Window window,
        DialogSurfaceSpec<TField> surface,
        double width)
        where TField : struct, Enum
    {
        Configure(window, surface.Title, width);
        AutomationProperties.SetAutomationId(window, surface.AutomationId);
        AutomationProperties.SetName(window, surface.AutomationName);
    }

    internal static void ApplySurface<TField>(Control control, DialogFieldSurfaceSpec<TField> field)
        where TField : struct, Enum
    {
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        AutomationProperties.SetName(control, field.AutomationName);
    }

    internal static void ApplyValidation<TField>(Control control, DialogSurfaceSpec<TField> surface)
        where TField : struct, Enum
    {
        if (surface.ValidationAutomationId is { } automationId)
            AutomationProperties.SetAutomationId(control, automationId);
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
    private readonly ColumnsDialogSession _session;
    private readonly ComboBox _preset;
    private readonly TextBox _count;
    private readonly TextBox _spacing;
    private readonly CheckBox _lineBetween;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public ColumnsDialog(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var surface = ColumnsDialogPlanner.Surface;
        _session = new ColumnsDialogSession(page, DialogCulture);
        PageLayoutDialogChrome.Configure(this, surface, 340);
        var state = _session.InitialState;
        _preset = PageLayoutDialogChrome.Combo(_session.Presets.Select(item => item.Label), state.PresetIndex);
        _count = PageLayoutDialogChrome.NumberBox(state.CountText);
        _spacing = PageLayoutDialogChrome.NumberBox(state.SpacingText);
        _lineBetween = new CheckBox { Content = surface.Field(ColumnsDialogField.LineBetween).Label, IsChecked = state.LineBetween, Margin = new Thickness(0, 8, 0, 0) };
        AvaloniaCompactDialogChrome.ApplyCheckBox(_lineBetween, PageLayoutDialogChrome.Style);
        PageLayoutDialogChrome.ApplySurface(_preset, surface.Field(ColumnsDialogField.Preset));
        PageLayoutDialogChrome.ApplySurface(_count, surface.Field(ColumnsDialogField.Count));
        PageLayoutDialogChrome.ApplySurface(_spacing, surface.Field(ColumnsDialogField.Spacing));
        PageLayoutDialogChrome.ApplySurface(_lineBetween, surface.Field(ColumnsDialogField.LineBetween));
        PageLayoutDialogChrome.ApplyValidation(_status, surface);
        _preset.SelectionChanged += (_, _) =>
            _count.Text = _session.CountTextForPreset(_preset.SelectedIndex);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(ColumnsDialogField.Preset).Label, _preset));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(ColumnsDialogField.Count).Label, _count));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(ColumnsDialogField.Spacing).Label, _spacing));
        content.Children.Add(_lineBetween);
        content.Children.Add(_status);
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_count);
        PageLayoutDialogChrome.WireEscape<ColumnsDialogResult?>(this);
    }

    private void Accept()
    {
        var acceptance = _session.PlanAcceptance(
            _preset.SelectedIndex,
            _count.Text,
            _spacing.Text,
            _lineBetween.IsChecked == true);
        if (!acceptance.IsAccepted)
        {
            PageLayoutDialogChrome.ShowError(_status, acceptance.ValidationMessage!);
            return;
        }
        Close(acceptance.Result);
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
    private readonly CustomParagraphSpacingDialogSession _session;
    private readonly TextBox _before;
    private readonly TextBox _after;
    private readonly TextBox _line;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public DocumentParagraphSpacingSet? Result { get; private set; }

    public CustomParagraphSpacingDialog(DocumentParagraphSpacingSet? current)
    {
        var surface = CustomParagraphSpacingDialogPlanner.Surface;
        _session = new CustomParagraphSpacingDialogSession(current, DialogCulture);
        PageLayoutDialogChrome.Configure(this, surface, 380);
        var state = _session.InitialState;
        _before = PageLayoutDialogChrome.NumberBox(state.SpaceBeforeText);
        _after = PageLayoutDialogChrome.NumberBox(state.SpaceAfterText);
        _line = PageLayoutDialogChrome.NumberBox(state.LineSpacingText);
        PageLayoutDialogChrome.ApplySurface(_before, surface.Field(CustomParagraphSpacingDialogField.SpaceBefore));
        PageLayoutDialogChrome.ApplySurface(_after, surface.Field(CustomParagraphSpacingDialogField.SpaceAfter));
        PageLayoutDialogChrome.ApplySurface(_line, surface.Field(CustomParagraphSpacingDialogField.LineSpacing));
        PageLayoutDialogChrome.ApplyValidation(_status, surface);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock
        {
            Text = surface.SupportingText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(CustomParagraphSpacingDialogField.SpaceBefore).Label, _before));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(CustomParagraphSpacingDialogField.SpaceAfter).Label, _after));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(CustomParagraphSpacingDialogField.LineSpacing).Label, _line));
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
        var acceptance = _session.PlanAcceptance(
            new CustomParagraphSpacingDialogInput(_before.Text, _after.Text, _line.Text));
        if (!acceptance.IsAccepted)
        {
            PageLayoutDialogChrome.ShowError(
                _status,
                acceptance.Validation?.Message ?? CustomParagraphSpacingDialogPlanner.LineSpacingValidationMessage);
            var target = acceptance.Validation?.Field switch
            {
                CustomParagraphSpacingDialogField.SpaceAfter => _after,
                CustomParagraphSpacingDialogField.LineSpacing => _line,
                _ => _before
            };
            PageLayoutDialogChrome.FocusAndSelect(target);
            return false;
        }

        Result = acceptance.Result;
        if (closeOnSuccess)
            Close(acceptance.Result);
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
    private readonly DropCapOptionsDialogSession _session;
    private readonly RadioButton _none;
    private readonly RadioButton _dropped;
    private readonly RadioButton _inMargin;
    private readonly ComboBox _font;
    private readonly TextBox _lines;
    private readonly TextBox _distance;

    public DropCapOptionsDialog()
    {
        var surface = DropCapOptionsDialogPlanner.Surface;
        _session = new DropCapOptionsDialogSession(DialogCulture);
        PageLayoutDialogChrome.Configure(this, surface, 340);
        var state = _session.InitialState;
        _none = PositionButton(surface.Field(DropCapOptionsDialogField.None));
        _dropped = PositionButton(surface.Field(DropCapOptionsDialogField.Dropped));
        _inMargin = PositionButton(surface.Field(DropCapOptionsDialogField.InMargin));
        new[] { _none, _dropped, _inMargin }[state.PositionIndex].IsChecked = true;
        _font = PageLayoutDialogChrome.Combo(_session.FontNames, state.FontIndex, 170);
        _font.IsEditable = true;
        _lines = PageLayoutDialogChrome.NumberBox(state.LinesToDropText, 70);
        _distance = PageLayoutDialogChrome.NumberBox(state.DistanceFromTextText, 70);
        PageLayoutDialogChrome.ApplySurface(_font, surface.Field(DropCapOptionsDialogField.Font));
        PageLayoutDialogChrome.ApplySurface(_lines, surface.Field(DropCapOptionsDialogField.LinesToDrop));
        PageLayoutDialogChrome.ApplySurface(_distance, surface.Field(DropCapOptionsDialogField.DistanceFromText));

        var positions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        positions.Children.Add(_none);
        positions.Children.Add(_dropped);
        positions.Children.Add(_inMargin);
        var content = new StackPanel { Margin = new Thickness(16) };
        var positionHeading = new TextBlock
        {
            Text = surface.Field(DropCapOptionsDialogField.Position).Label,
            FontWeight = FontWeight.SemiBold,
        };
        PageLayoutDialogChrome.ApplySurface(
            positionHeading,
            surface.Field(DropCapOptionsDialogField.Position));
        content.Children.Add(positionHeading);
        content.Children.Add(positions);
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(DropCapOptionsDialogField.Font).Label, _font));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(DropCapOptionsDialogField.LinesToDrop).Label, _lines));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(DropCapOptionsDialogField.DistanceFromText).Label, _distance));
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_lines);
        PageLayoutDialogChrome.WireEscape<DropCapOptionsDialogResult?>(this);
    }

    private static RadioButton PositionButton(DialogFieldSurfaceSpec<DropCapOptionsDialogField> field)
    {
        var button = new RadioButton { Content = field.Label, GroupName = "DropCapPosition", Margin = new Thickness(0, 0, 14, 0) };
        AvaloniaCompactDialogChrome.ApplyRadioButton(button, PageLayoutDialogChrome.Style);
        PageLayoutDialogChrome.ApplySurface(button, field);
        return button;
    }

    private void Accept()
    {
        var index = _none.IsChecked == true
            ? (int)DropCapDialogPosition.None
            : _inMargin.IsChecked == true
                ? (int)DropCapDialogPosition.InMargin
                : (int)DropCapDialogPosition.Dropped;
        Close(_session.PlanAcceptance(
            new DropCapOptionsDialogInput(index, _font.Text, _lines.Text, _distance.Text)));
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
    private readonly HyphenationOptionsDialogSession _session;
    private readonly CheckBox _automatic;
    private readonly TextBox _zone;
    private readonly TextBox _limit;
    private readonly CheckBox _caps;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public HyphenationOptionsDialog(PageSettings page)
    {
        var surface = HyphenationOptionsDialogPlanner.Surface;
        _session = new HyphenationOptionsDialogSession(page, DialogCulture);
        PageLayoutDialogChrome.Configure(this, surface, 410);
        var state = _session.InitialState;
        _automatic = Check(surface.Field(HyphenationOptionsDialogField.Automatic).Label, state.AutoHyphenation);
        _zone = PageLayoutDialogChrome.NumberBox(state.ZoneText);
        _limit = PageLayoutDialogChrome.NumberBox(state.ConsecutiveLimitText);
        _caps = Check(surface.Field(HyphenationOptionsDialogField.HyphenateCaps).Label, state.HyphenateCaps);
        PageLayoutDialogChrome.ApplySurface(_automatic, surface.Field(HyphenationOptionsDialogField.Automatic));
        PageLayoutDialogChrome.ApplySurface(_zone, surface.Field(HyphenationOptionsDialogField.Zone));
        PageLayoutDialogChrome.ApplySurface(_limit, surface.Field(HyphenationOptionsDialogField.ConsecutiveLimit));
        PageLayoutDialogChrome.ApplySurface(_caps, surface.Field(HyphenationOptionsDialogField.HyphenateCaps));
        PageLayoutDialogChrome.ApplyValidation(_status, surface);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(_automatic);
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(HyphenationOptionsDialogField.Zone).Label, _zone));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(HyphenationOptionsDialogField.ConsecutiveLimit).Label, _limit));
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
        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            PageLayoutDialogChrome.ShowError(_status, acceptance.ValidationMessage!);
            return;
        }
        Close(acceptance.Result);
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
    private readonly ManualHyphenationDialogSession _session;
    private readonly ComboBox _choices;

    public ManualHyphenationDialog(ManualHyphenationCandidate candidate)
    {
        var surface = ManualHyphenationPlanner.AvaloniaSurface;
        _session = new ManualHyphenationDialogSession(candidate);
        PageLayoutDialogChrome.Configure(this, surface, 380);

        _choices = new ComboBox { SelectedIndex = 0, MinWidth = 230 };
        foreach (var option in _session.Options)
            _choices.Items.Add(option.DisplayText);
        AvaloniaCompactDialogChrome.ApplyComboBox(_choices, PageLayoutDialogChrome.Style);
        PageLayoutDialogChrome.ApplySurface(
            _choices,
            surface.Field(ManualHyphenationDialogField.Choices));

        var yes = Button(surface.Field(ManualHyphenationDialogField.Yes), isDefault: true, () =>
        {
            var result = _session.PlanAcceptance(_choices.SelectedIndex);
            if (result is not null)
                Close(result);
        });
        var no = Button(surface.Field(ManualHyphenationDialogField.No), isDefault: false, () => Close(_session.PlanSkip()));
        var cancel = Button(surface.Field(ManualHyphenationDialogField.Cancel), isDefault: false, () => Close(_session.PlanCancel()));
        cancel.IsCancel = true;

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [yes, no, cancel],
            new Thickness(0, 16, 0, 0));

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = _session.CandidateLabel, Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock { Text = _session.Candidate.Word, FontWeight = FontWeight.SemiBold, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = surface.Field(ManualHyphenationDialogField.Choices).Label, Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(_choices);
        content.Children.Add(buttons);
        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(_session.PlanCancel());
            e.Handled = true;
        };
    }

    private static Button Button(
        DialogFieldSurfaceSpec<ManualHyphenationDialogField> field,
        bool isDefault,
        Action action)
    {
        var button = new Button { Content = field.Label, MinWidth = 72, IsDefault = isDefault };
        button.Click += (_, _) => action();
        AvaloniaCompactDialogChrome.ApplyButton(button, PageLayoutDialogChrome.Style, 72, isDefault);
        PageLayoutDialogChrome.ApplySurface(button, field);
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
            report(ManualHyphenationPlanner.NoCandidatesMessage);
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
        report(ManualHyphenationPlanner.FormatSummary(session.AcceptedCount));
        editor.Focus();
    }
}

public sealed class LineNumberOptionsDialog : FreeWDialogWindow
{
    private static readonly CultureInfo DialogCulture = CultureInfo.CurrentCulture;
    private readonly LineNumberOptionsDialogSession _session;
    private readonly TextBox _startAt;
    private readonly TextBox _countBy;
    private readonly ComboBox _mode;
    private readonly TextBlock _status = PageLayoutDialogChrome.Status();

    public LineNumberOptionsDialog(PageSettings page)
    {
        var surface = LineNumberOptionsDialogPlanner.Surface;
        _session = new LineNumberOptionsDialogSession(page, DialogCulture);
        PageLayoutDialogChrome.Configure(this, surface, 340);
        var state = _session.InitialState;
        _startAt = PageLayoutDialogChrome.NumberBox(state.StartAtText);
        _countBy = PageLayoutDialogChrome.NumberBox(state.CountByText);
        _mode = PageLayoutDialogChrome.Combo(_session.ModeLabels, state.ModeIndex);
        PageLayoutDialogChrome.ApplySurface(_startAt, surface.Field(LineNumberOptionsDialogField.StartAt));
        PageLayoutDialogChrome.ApplySurface(_countBy, surface.Field(LineNumberOptionsDialogField.CountBy));
        PageLayoutDialogChrome.ApplySurface(_mode, surface.Field(LineNumberOptionsDialogField.Numbering));
        PageLayoutDialogChrome.ApplyValidation(_status, surface);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(LineNumberOptionsDialogField.StartAt).Label, _startAt));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(LineNumberOptionsDialogField.CountBy).Label, _countBy));
        content.Children.Add(PageLayoutDialogChrome.Row(surface.Field(LineNumberOptionsDialogField.Numbering).Label, _mode));
        content.Children.Add(_status);
        content.Children.Add(PageLayoutDialogChrome.Actions(Accept, () => Close(null)));
        Content = content;

        Opened += (_, _) => PageLayoutDialogChrome.FocusAndSelect(_startAt);
        PageLayoutDialogChrome.WireEscape<LineNumberOptionsDialogResult?>(this);
    }

    private void Accept()
    {
        var input = new LineNumberOptionsDialogInput(_startAt.Text, _countBy.Text, _mode.SelectedIndex);
        var acceptance = _session.PlanAcceptance(input);
        if (!acceptance.IsAccepted)
        {
            PageLayoutDialogChrome.ShowError(_status, acceptance.ValidationMessage!);
            return;
        }
        Close(acceptance.Result);
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
