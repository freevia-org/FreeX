using System;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Compare Documents" dialog (Review &gt; Compare &gt; Compare…). Lets the user pick the original
/// document to compare against the current (revised) document, and optionally override the reviewer name
/// that will be stamped onto every produced <c>w:ins</c>/<c>w:del</c> revision.
///
/// <para>
/// The dialog has two phases: first the shared WPF file dialog service collects the original
/// file path, then the main dialog shows a summary — "Original:" path, "Revised:" current document title,
/// "Label revisions with:" author text box — so the user can confirm before running the blackline engine.
/// Cancelling either phase returns null from <see cref="Prompt"/>.
/// </para>
///
/// <para>
/// A "More &gt;&gt;" expander reveals the Comparison Settings panel: check-boxes for which changes to track
/// (insertions, deletions, moves, formatting, comments, case changes, whitespace) and a "Show changes in:"
/// radio group (New Document / Original / Revised). These surface <see cref="CompareSettings"/> that are
/// passed straight through to <see cref="FreeW.Core.Model.DocumentCompare.Compare"/>. The date is stamped
/// by the calling command (UI side) so the pure model helper stays deterministic.
/// </para>
/// </summary>
internal sealed class CompareDocumentsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly string _originalPath;
    private readonly TextBox _authorBox;

    // Comparison Settings checkboxes.
    private readonly CheckBox _chkInsertions;
    private readonly CheckBox _chkDeletions;
    private readonly CheckBox _chkMoves;
    private readonly CheckBox _chkComments;
    private readonly CheckBox _chkFormatting;
    private readonly CheckBox _chkCaseChanges;
    private readonly CheckBox _chkWhitespace;

    // Show changes in radio buttons.
    private readonly RadioButton _radioNew;
    private readonly RadioButton _radioOriginal;
    private readonly RadioButton _radioRevised;
    private readonly Expander _moreExpander;
    private static readonly Free.Shared.Shell.DialogFocusPlan<string> FocusPlan = FreeWDialogFocusPlanner.CompareDocuments;

    private CompareDocumentsDialogResult? _result;

    private CompareDocumentsDialog(Window? owner, string originalPath, string defaultAuthor, string revisedTitle)
    {
        _originalPath = originalPath;
        var plan = ReviewCompareCombineWorkflow.BuildCompareDialogPlan(
            originalPath,
            new CompareDocumentsPromptState(defaultAuthor, revisedTitle));

        Owner = owner;
        Title = plan.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _authorBox = new TextBox
        {
            Text = plan.DefaultAuthor,
            MinWidth = 220,
            MaxWidth = 260
        };
        AutomationProperties.SetAutomationId(_authorBox, FocusPlan.InitialFocusTarget);

        // ---- Comparison Settings (all on by default, matching Word) ----
        _chkInsertions  = MakeCheckBox(plan, CompareChangeKind.Insertions);
        _chkDeletions   = MakeCheckBox(plan, CompareChangeKind.Deletions);
        _chkMoves       = MakeCheckBox(plan, CompareChangeKind.Moves);
        _chkComments    = MakeCheckBox(plan, CompareChangeKind.Comments);
        _chkFormatting  = MakeCheckBox(plan, CompareChangeKind.Formatting);
        _chkCaseChanges = MakeCheckBox(plan, CompareChangeKind.CaseChanges);
        _chkWhitespace  = MakeCheckBox(plan, CompareChangeKind.Whitespace);

        var settingsPanel = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };
        settingsPanel.Children.Add(new TextBlock { Text = plan.ChangeOptionsHeading, FontWeight = System.Windows.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        settingsPanel.Children.Add(_chkInsertions);
        settingsPanel.Children.Add(_chkDeletions);
        settingsPanel.Children.Add(_chkMoves);
        settingsPanel.Children.Add(_chkComments);
        settingsPanel.Children.Add(_chkFormatting);
        settingsPanel.Children.Add(_chkCaseChanges);
        settingsPanel.Children.Add(_chkWhitespace);

        settingsPanel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
        settingsPanel.Children.Add(new TextBlock { Text = plan.ShowChangesHeading, FontWeight = System.Windows.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _radioNew      = MakeRadio(plan, CompareShowChangesIn.NewDocument);
        _radioOriginal = MakeRadio(plan, CompareShowChangesIn.Original);
        _radioRevised  = MakeRadio(plan, CompareShowChangesIn.Revised);
        settingsPanel.Children.Add(_radioNew);
        settingsPanel.Children.Add(_radioOriginal);
        settingsPanel.Children.Add(_radioRevised);

        // ---- "More >>" expander ----
        _moreExpander = new Expander
        {
            Header = plan.MoreLabel,
            Content = settingsPanel,
            IsExpanded = false,
            Margin = new Thickness(0, 6, 0, 0)
        };

        // Grid: 2 read-only path rows, separator, author row, expander, then OK/Cancel.
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 6; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddReadOnlyRow(grid, 0, plan.OriginalLabel, plan.OriginalDisplayPath);
        AddReadOnlyRow(grid, 1, plan.RevisedLabel, plan.RevisedDisplayName);

        // A thin separator between the path summary and the editable author option.
        var sep = new Separator { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(sep, 2);
        Grid.SetColumnSpan(sep, 2);
        grid.Children.Add(sep);

        AddFieldRow(grid, 3, plan.AuthorLabel, _authorBox);

        Grid.SetRow(_moreExpander, 4);
        Grid.SetColumnSpan(_moreExpander, 2);
        grid.Children.Add(_moreExpander);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 5);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        Loaded += (_, _) => FocusAuthor();
    }

    private void FocusAuthor()
    {
        if (FocusPlan.SelectAllOnFocus)
            DialogFocus.FocusAndSelect(_authorBox);
        else
            DialogFocus.Focus(_authorBox);
    }

    private static CheckBox MakeCheckBox(CompareDocumentsDialogPlan plan, CompareChangeKind kind)
    {
        var option = plan.ChangeOptions.Single(item => item.Kind == kind);
        return new CheckBox
        {
            Content = option.Label,
            IsChecked = option.IsChecked,
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    private static RadioButton MakeRadio(CompareDocumentsDialogPlan plan, CompareShowChangesIn value)
    {
        var option = plan.ShowOptions.Single(item => item.Value == value);
        return new RadioButton
        {
            Content = option.Label,
            IsChecked = option.IsChecked,
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    // Add a label+read-only text row to the grid.
    private static void AddReadOnlyRow(Grid grid, int row, string label, string text)
    {
        AddLabel(grid, row, label);
        var value = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            Margin = new Thickness(0, 4, 0, 4),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent
        };
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }

    // Add a label+editable UIElement row to the grid.
    private static void AddFieldRow(Grid grid, int row, string label, UIElement field)
    {
        AddLabel(grid, row, label);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private static void AddLabel(Grid grid, int row, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);
    }

    private void Accept() => TryAccept(showWarnings: true);

    private void TryAccept(bool showWarnings)
    {
        var showIn = _radioOriginal.IsChecked == true ? CompareShowChangesIn.Original
            : _radioRevised.IsChecked == true ? CompareShowChangesIn.Revised
            : CompareShowChangesIn.NewDocument;

        var selection = new CompareDocumentsDialogSelection(
            _chkInsertions.IsChecked == true,
            _chkDeletions.IsChecked == true,
            _chkMoves.IsChecked == true,
            _chkComments.IsChecked == true,
            _chkFormatting.IsChecked == true,
            _chkCaseChanges.IsChecked == true,
            _chkWhitespace.IsChecked == true,
            showIn);
        if (!ReviewCompareCombineWorkflow.TryBuildCompareDialogResult(
                _originalPath,
                _authorBox.Text,
                selection,
                out _result,
                out var validationMessage))
        {
            if (showWarnings)
                DialogMessageHelper.ShowWarning(this, validationMessage!);
            return;
        }

        Close();
    }

    // -----------------------------------------------------------------------
    // Test seam
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test seam: create the dialog already seeded with <paramref name="originalPath"/>,
    /// <paramref name="defaultAuthor"/>, and <paramref name="revisedTitle"/> without showing a
    /// file picker, so STA tests can exercise the control wiring without a modal loop.
    /// </summary>
    internal static CompareDocumentsDialog CreateForTest(string originalPath, string defaultAuthor, string revisedTitle = "") =>
        new(owner: null, originalPath, defaultAuthor, revisedTitle);

    /// <summary>
    /// Test seam: validate the current author value and return the shared result, without
    /// closing the window (mirrors the pattern used in <see cref="PageSetupDialog.AcceptForTest"/>).
    /// </summary>
    internal CompareDocumentsDialogResult? AcceptForTest()
    {
        TryAccept(showWarnings: false);
        return _result;
    }

    internal Expander MoreExpanderForTest => _moreExpander;

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Run the two-phase Compare dialog (file picker then confirm/author dialog) owned by
    /// <paramref name="owner"/>. <paramref name="defaultAuthor"/> seeds the "Label revisions with:" box;
    /// <paramref name="revisedTitle"/> shows as the "Revised:" display name. Returns null if the user
    /// cancels either phase.
    /// </summary>
    public static CompareDocumentsDialogResult? Prompt(Window? owner, string defaultAuthor, string revisedTitle = "")
    {
        // Phase 1: file picker for the original document.
        var picker = WpfFileDialogService.ShowOpenDialog(
            owner,
            ReviewCompareCombineWorkflow.CombineDocumentFilter,
            defaultExtensionWithDot: ReviewCompareCombineWorkflow.CombineDocumentDefaultExtension,
            title: ReviewCompareCombineWorkflow.CompareOriginalPickerTitle);
        if (!picker.Chosen)
            return null;

        // Phase 2: review/confirm dialog — shows paths, lets user override author.
        var dlg = new CompareDocumentsDialog(owner, picker.FileName!, defaultAuthor, revisedTitle);
        dlg.ShowDialog();
        return dlg._result;
    }
}
