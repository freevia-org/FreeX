using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class CompareDocumentsDialog : FreeWDialogWindow
{
    private const string ReviewerValidationMessage =
        "Enter a reviewer name to label the tracked changes.";

    private readonly string _originalPath;
    private readonly TextBox _authorBox = new()
    {
        MinWidth = 220,
        MaxWidth = 260,
    };

    private readonly CheckBox _insertions = MakeCheckBox("Insertions and deletions", true);
    private readonly CheckBox _deletions = MakeCheckBox("Deletions", true);
    private readonly CheckBox _moves = MakeCheckBox("Moves", true);
    private readonly CheckBox _comments = MakeCheckBox("Comments", true);
    private readonly CheckBox _formatting = MakeCheckBox("Formatting", true);
    private readonly CheckBox _caseChanges = MakeCheckBox("Case changes", true);
    private readonly CheckBox _whitespace = MakeCheckBox("White space", true);
    private readonly RadioButton _showNew = MakeRadio("New document", true);
    private readonly RadioButton _showOriginal = MakeRadio("Original document");
    private readonly RadioButton _showRevised = MakeRadio("Revised document");
    private readonly Expander _moreExpander;
    private readonly TextBlock _validation = new()
    {
        Foreground = Brushes.Maroon,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.CompareDocuments;

    public CompareDocumentsDialogResult? Result { get; private set; }

    private CompareDocumentsDialog(string originalPath, CompareDocumentsPromptState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentNullException.ThrowIfNull(state);

        _originalPath = originalPath;
        _authorBox.Text = state.DefaultAuthor;

        Title = "Compare Documents";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AvaloniaCompactDialogChrome.ApplyTextBox(_authorBox, InsertDialogLayout.ChromeStyle);
        ApplyCheckBoxChrome(_insertions, _deletions, _moves, _comments, _formatting, _caseChanges, _whitespace);
        ApplyRadioChrome(_showNew, _showOriginal, _showRevised);

        AutomationProperties.SetAutomationId(this, "CompareDocumentsDialog");
        AutomationProperties.SetAutomationId(_authorBox, FocusPlan.InitialFocusTargetAutomationId);
        AutomationProperties.SetAutomationId(_validation, "CompareDocumentsValidationText");

        var grid = DialogGrid(rows: 7);
        AddReadOnlyRow(grid, 0, "Original:", ReviewCompareCombineWorkflow.TruncatePathForDialog(originalPath));
        AddReadOnlyRow(grid, 1, "Revised:", string.IsNullOrWhiteSpace(state.RevisedTitle) ? "(current document)" : state.RevisedTitle);

        var separator = new Separator { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(separator, 2);
        Grid.SetColumnSpan(separator, 2);
        grid.Children.Add(separator);

        AddFieldRow(grid, 3, "Label revisions with:", _authorBox);

        _moreExpander = new Expander
        {
            Header = "More",
            Content = BuildCompareSettingsPanel(),
            IsExpanded = false,
            Margin = new Thickness(0, 6, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyWpfExpander(_moreExpander, InsertDialogLayout.ChromeStyle);
        Grid.SetRow(_moreExpander, 4);
        Grid.SetColumnSpan(_moreExpander, 2);
        grid.Children.Add(_moreExpander);

        Grid.SetRow(_validation, 5);
        Grid.SetColumn(_validation, 1);
        grid.Children.Add(_validation);

        var buttons = InsertDialogLayout.OkCancelRow(Accept, Close);
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        _authorBox.KeyDown += (_, e) => InsertDialogLayout.HandleEnterEscape(e, buttons);
        Opened += (_, _) => FocusAuthor();
    }

    public static async Task<CompareDocumentsDialogResult?> ShowAsync(
        Window owner,
        string originalPath,
        CompareDocumentsPromptState state)
    {
        var dialog = new CompareDocumentsDialog(originalPath, state);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }

    private Control BuildCompareSettingsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = "Mark up which changes:",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(_insertions);
        panel.Children.Add(_deletions);
        panel.Children.Add(_moves);
        panel.Children.Add(_comments);
        panel.Children.Add(_formatting);
        panel.Children.Add(_caseChanges);
        panel.Children.Add(_whitespace);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
        panel.Children.Add(new TextBlock
        {
            Text = "Show changes in:",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(_showNew);
        panel.Children.Add(_showOriginal);
        panel.Children.Add(_showRevised);
        return panel;
    }

    internal static CompareDocumentsDialog CreateForTest(
        string originalPath,
        CompareDocumentsPromptState state) =>
        new(originalPath, state);

    internal TextBox AuthorBoxForTest => _authorBox;
    internal TextBlock ValidationForTest => _validation;
    internal Expander MoreExpanderForTest => _moreExpander;

    internal CompareDocumentsDialogResult? AcceptForTest(string? author)
    {
        _authorBox.Text = author;
        TryAccept(close: false);
        return Result;
    }

    private void Accept() => TryAccept(close: true);

    private void TryAccept(bool close)
    {
        var author = _authorBox.Text?.Trim();
        if (string.IsNullOrEmpty(author))
        {
            _validation.Text = ReviewerValidationMessage;
            _validation.IsVisible = true;
            FocusAuthor();
            return;
        }

        _validation.IsVisible = false;

        var showIn = _showOriginal.IsChecked == true ? CompareShowChangesIn.Original
            : _showRevised.IsChecked == true ? CompareShowChangesIn.Revised
            : CompareShowChangesIn.NewDocument;

        Result = new CompareDocumentsDialogResult(
            _originalPath,
            author,
            new CompareSettings
            {
                Insertions = _insertions.IsChecked == true,
                Deletions = _deletions.IsChecked == true,
                Moves = _moves.IsChecked == true,
                Comments = _comments.IsChecked == true,
                Formatting = _formatting.IsChecked == true,
                CaseChanges = _caseChanges.IsChecked == true,
                Whitespace = _whitespace.IsChecked == true,
                ShowChangesIn = showIn,
            });
        if (close)
            Close();
    }

    private void FocusAuthor()
    {
        if (FocusPlan.SelectAllOnFocus)
            AvaloniaCompactDialogChrome.FocusAndSelect(_authorBox);
        else
            _authorBox.Focus();
    }

    private static CheckBox MakeCheckBox(string content, bool isChecked) =>
        new() { Content = content, IsChecked = isChecked, Margin = new Thickness(0, 0, 0, 2) };

    private static RadioButton MakeRadio(string content, bool isChecked = false) =>
        new()
        {
            Content = content,
            IsChecked = isChecked,
            GroupName = "FreeWCompareShowChangesIn",
            Margin = new Thickness(0, 0, 0, 2),
        };

    private static void ApplyCheckBoxChrome(params CheckBox[] boxes)
    {
        foreach (var box in boxes)
            AvaloniaCompactDialogChrome.ApplyCheckBox(box, InsertDialogLayout.ChromeStyle);
    }

    private static void ApplyRadioChrome(params RadioButton[] buttons)
    {
        foreach (var button in buttons)
            AvaloniaCompactDialogChrome.ApplyRadioButton(button, InsertDialogLayout.ChromeStyle);
    }

    internal static Grid DialogGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    internal static void AddReadOnlyRow(Grid grid, int row, string label, string value)
    {
        AddLabel(grid, row, label);
        var text = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            Margin = new Thickness(0, 4, 0, 4),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(text, InsertDialogLayout.ChromeStyle);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
    }

    internal static void AddFieldRow(Grid grid, int row, string label, Control field)
    {
        AddLabel(grid, row, label);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        field.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private static void AddLabel(Grid grid, int row, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);
    }
}

internal sealed class CombineDocumentsDialog : FreeWDialogWindow
{
    private readonly string _originalPath;
    private readonly string _reviewerBPath;
    private readonly TextBox _authorABox = new()
    {
        MinWidth = 200,
        MaxWidth = 240,
    };

    private readonly TextBox _authorBBox = new()
    {
        MinWidth = 200,
        MaxWidth = 240,
    };

    public CombineDocumentsDialogResult? Result { get; private set; }

    private CombineDocumentsDialog(
        string originalPath,
        string reviewerBPath,
        CombineDocumentsPromptState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerBPath);
        ArgumentNullException.ThrowIfNull(state);

        _originalPath = originalPath;
        _reviewerBPath = reviewerBPath;
        var plan = ReviewCompareCombineWorkflow.BuildCombineDialogPlan(originalPath, reviewerBPath, state);
        _authorABox.Text = plan.DefaultAuthorA;
        _authorBBox.Text = plan.DefaultAuthorB;

        Title = plan.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AvaloniaCompactDialogChrome.ApplyTextBox(_authorABox, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_authorBBox, InsertDialogLayout.ChromeStyle);

        var grid = CompareDocumentsDialog.DialogGrid(rows: 7);
        CompareDocumentsDialog.AddReadOnlyRow(grid, 0, plan.OriginalLabel, plan.OriginalDisplayPath);
        CompareDocumentsDialog.AddReadOnlyRow(grid, 1, plan.ReviewerALabel, plan.ReviewerADisplayName);
        CompareDocumentsDialog.AddReadOnlyRow(grid, 2, plan.ReviewerBLabel, plan.ReviewerBDisplayPath);

        var separator = new Separator { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(separator, 3);
        Grid.SetColumnSpan(separator, 2);
        grid.Children.Add(separator);

        CompareDocumentsDialog.AddFieldRow(grid, 4, plan.AuthorALabel, _authorABox);
        CompareDocumentsDialog.AddFieldRow(grid, 5, plan.AuthorBLabel, _authorBBox);

        var buttons = InsertDialogLayout.OkCancelRow(Accept, Close);
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        _authorABox.KeyDown += (_, e) => InsertDialogLayout.HandleEnterEscape(e, buttons);
        _authorBBox.KeyDown += (_, e) => InsertDialogLayout.HandleEnterEscape(e, buttons);
    }

    public static async Task<CombineDocumentsDialogResult?> ShowAsync(
        Window owner,
        string originalPath,
        string reviewerBPath,
        CombineDocumentsPromptState state)
    {
        var dialog = new CombineDocumentsDialog(originalPath, reviewerBPath, state);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }

    private void Accept()
    {
        if (!ReviewCompareCombineWorkflow.TryBuildCombineDialogResult(
                _originalPath,
                _reviewerBPath,
                _authorABox.Text,
                _authorBBox.Text,
                out var result,
                out _))
        {
            return;
        }

        Result = result;
        Close();
    }
}
