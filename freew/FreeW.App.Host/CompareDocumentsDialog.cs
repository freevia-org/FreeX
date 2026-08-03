using System;
using System.IO;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
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
    /// <summary>What the dialog returns when the user clicks OK.</summary>
    internal sealed record Result(string OriginalFilePath, string Author, CompareSettings Settings);

    private const string DocxFilter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*";

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
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.CompareDocuments;

    private Result? _result;

    private CompareDocumentsDialog(Window? owner, string originalPath, string defaultAuthor, string revisedTitle)
    {
        _originalPath = originalPath;

        Owner = owner;
        Title = "Compare Documents";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _authorBox = new TextBox
        {
            Text = defaultAuthor,
            MinWidth = 220,
            MaxWidth = 260
        };
        AutomationProperties.SetAutomationId(_authorBox, FocusPlan.InitialFocusTargetAutomationId);

        // ---- Comparison Settings (all on by default, matching Word) ----
        _chkInsertions  = MakeCheckBox("Insertions and deletions", true);
        _chkDeletions   = MakeCheckBox("Deletions", true);
        _chkMoves       = MakeCheckBox("Moves", true);
        _chkComments    = MakeCheckBox("Comments", true);
        _chkFormatting  = MakeCheckBox("Formatting", true);
        _chkCaseChanges = MakeCheckBox("Case changes", true);
        _chkWhitespace  = MakeCheckBox("White space", true);

        var settingsPanel = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };
        settingsPanel.Children.Add(new TextBlock { Text = "Mark up which changes:", FontWeight = System.Windows.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        settingsPanel.Children.Add(_chkInsertions);
        settingsPanel.Children.Add(_chkDeletions);
        settingsPanel.Children.Add(_chkMoves);
        settingsPanel.Children.Add(_chkComments);
        settingsPanel.Children.Add(_chkFormatting);
        settingsPanel.Children.Add(_chkCaseChanges);
        settingsPanel.Children.Add(_chkWhitespace);

        settingsPanel.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
        settingsPanel.Children.Add(new TextBlock { Text = "Show changes in:", FontWeight = System.Windows.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _radioNew      = new RadioButton { Content = "New document", IsChecked = true, Margin = new Thickness(0, 0, 0, 2) };
        _radioOriginal = new RadioButton { Content = "Original document", Margin = new Thickness(0, 0, 0, 2) };
        _radioRevised  = new RadioButton { Content = "Revised document", Margin = new Thickness(0, 0, 0, 2) };
        settingsPanel.Children.Add(_radioNew);
        settingsPanel.Children.Add(_radioOriginal);
        settingsPanel.Children.Add(_radioRevised);

        // ---- "More >>" expander ----
        _moreExpander = new Expander
        {
            Header = "More",
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

        AddReadOnlyRow(grid, 0, "Original:", TruncatePath(originalPath));
        AddReadOnlyRow(grid, 1, "Revised:", string.IsNullOrEmpty(revisedTitle) ? "(current document)" : revisedTitle);

        // A thin separator between the path summary and the editable author option.
        var sep = new Separator { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(sep, 2);
        Grid.SetColumnSpan(sep, 2);
        grid.Children.Add(sep);

        AddFieldRow(grid, 3, "Label revisions with:", _authorBox);

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

    private static CheckBox MakeCheckBox(string label, bool isChecked) =>
        new() { Content = label, IsChecked = isChecked, Margin = new Thickness(0, 0, 0, 2) };

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
        var author = _authorBox.Text.Trim();
        if (string.IsNullOrEmpty(author))
        {
            if (showWarnings)
                DialogMessageHelper.ShowWarning(this, "Enter a reviewer name to label the tracked changes.");
            return;
        }

        var showIn = _radioOriginal.IsChecked == true ? CompareShowChangesIn.Original
            : _radioRevised.IsChecked == true ? CompareShowChangesIn.Revised
            : CompareShowChangesIn.NewDocument;

        var settings = new CompareSettings
        {
            Insertions  = _chkInsertions.IsChecked == true,
            Deletions   = _chkDeletions.IsChecked == true,
            Moves       = _chkMoves.IsChecked == true,
            Comments    = _chkComments.IsChecked == true,
            Formatting  = _chkFormatting.IsChecked == true,
            CaseChanges = _chkCaseChanges.IsChecked == true,
            Whitespace  = _chkWhitespace.IsChecked == true,
            ShowChangesIn = showIn
        };

        _result = new Result(_originalPath, author, settings);
        Close();
    }

    // Show at most the last two path components so the dialog is not too wide, e.g. "…\Docs\Contract_v1.docx".
    private static string TruncatePath(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir))
            return file;
        var parent = Path.GetFileName(dir);
        return string.IsNullOrEmpty(parent) ? file : $"…\\{parent}\\{file}";
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
    /// Test seam: validate the current author value and return the <see cref="Result"/>, without
    /// closing the window (mirrors the pattern used in <see cref="PageSetupDialog.AcceptForTest"/>).
    /// </summary>
    internal Result? AcceptForTest()
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
    public static Result? Prompt(Window? owner, string defaultAuthor, string revisedTitle = "")
    {
        // Phase 1: file picker for the original document.
        var picker = WpfFileDialogService.ShowOpenDialog(
            owner,
            DocxFilter,
            defaultExtensionWithDot: ".docx",
            title: "Compare: pick the ORIGINAL document");
        if (!picker.Chosen)
            return null;

        // Phase 2: review/confirm dialog — shows paths, lets user override author.
        var dlg = new CompareDocumentsDialog(owner, picker.FileName!, defaultAuthor, revisedTitle);
        dlg.ShowDialog();
        return dlg._result;
    }
}
