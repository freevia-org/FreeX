using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Compare Documents" dialog (Review &gt; Compare &gt; Compare…). Lets the user pick the original
/// document to compare against the current (revised) document, and optionally override the reviewer name
/// that will be stamped onto every produced <c>w:ins</c>/<c>w:del</c> revision.
///
/// <para>
/// The dialog has two phases: first an <see cref="Microsoft.Win32.OpenFileDialog"/> collects the original
/// file path, then the main dialog shows a summary — "Original:" path, "Revised:" current document title,
/// "Label revisions with:" author text box — so the user can confirm before running the blackline engine.
/// Cancelling either phase returns null from <see cref="Prompt"/>.
/// </para>
///
/// <para>
/// The result carries the resolved file path and the (possibly user-overridden) author string ready for
/// <see cref="FreeW.Core.Model.DocumentCompare.Compare"/>. The date is stamped by the calling command
/// (UI side) so the pure model helper stays deterministic.
/// </para>
/// </summary>
internal sealed class CompareDocumentsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>What the dialog returns when the user clicks OK.</summary>
    internal sealed record Result(string OriginalFilePath, string Author);

    private const string DocxFilter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*";

    private readonly string _originalPath;
    private readonly TextBox _authorBox;
    private Result? _result;

    private CompareDocumentsDialog(Window? owner, string originalPath, string defaultAuthor, string revisedTitle)
    {
        _originalPath = originalPath;

        Owner = owner;
        Title = "Compare Documents";
        Width = 440;
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

        // Grid: 2 read-only path rows, a separator, the author row, then OK/Cancel.
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddReadOnlyRow(grid, 0, "Original:", TruncatePath(originalPath));
        AddReadOnlyRow(grid, 1, "Revised:", string.IsNullOrEmpty(revisedTitle) ? "(current document)" : revisedTitle);

        // A thin separator between the path summary and the editable author option.
        var sep = new Separator { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(sep, 2);
        Grid.SetColumnSpan(sep, 2);
        grid.Children.Add(sep);

        AddFieldRow(grid, 3, "Label revisions with:", _authorBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_authorBox);
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
        var author = _authorBox.Text.Trim();
        if (string.IsNullOrEmpty(author))
        {
            if (showWarnings)
                DialogMessageHelper.ShowWarning(this, "Enter a reviewer name to label the tracked changes.");
            return;
        }

        _result = new Result(_originalPath, author);
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
        var picker = new OpenFileDialog
        {
            Filter = DocxFilter,
            DefaultExt = ".docx",
            Title = "Compare: pick the ORIGINAL document"
        };
        if (picker.ShowDialog(owner) != true)
            return null;

        // Phase 2: review/confirm dialog — shows paths, lets user override author.
        var dlg = new CompareDocumentsDialog(owner, picker.FileName, defaultAuthor, revisedTitle);
        dlg.ShowDialog();
        return dlg._result;
    }
}
