using System;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Combine Documents" dialog (Review &gt; Compare &gt; Combine…). Lets the user pick the original
/// (base) document and a second reviewer's revised copy, then confirm and optionally override the author
/// labels that will be stamped onto each reviewer's produced <c>w:ins</c>/<c>w:del</c> revisions.
///
/// <para>
/// The dialog has three phases: first the shared WPF file dialog service collects the original
/// base path, then a second picker collects reviewer B's revised copy path, then the main dialog shows a
/// summary — "Original:", "Reviewer A:" (current document title), "Reviewer B:" path, and editable author
/// boxes for each reviewer — so the user can confirm and name both authors before running the merge engine.
/// Cancelling any phase returns null from <see cref="Prompt"/>.
/// </para>
///
/// <para>
/// The result carries the resolved file paths and the (possibly user-overridden) author strings, ready for
/// <see cref="FreeW.Core.Model.DocumentCombine.Combine"/>. The date is stamped by the calling command (UI
/// side) so the pure model helper stays deterministic.
/// </para>
/// </summary>
internal sealed class CombineDocumentsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly string _originalPath;
    private readonly string _reviewerBPath;
    private readonly TextBox _authorABox;
    private readonly TextBox _authorBBox;
    private CombineDocumentsDialogResult? _result;

    private CombineDocumentsDialog(
        Window? owner,
        string originalPath,
        string reviewerBPath,
        string defaultAuthorA,
        string defaultAuthorB,
        string reviewerATitle)
    {
        _originalPath = originalPath;
        _reviewerBPath = reviewerBPath;
        var plan = ReviewCompareCombineWorkflow.BuildCombineDialogPlan(
            originalPath,
            reviewerBPath,
            new CombineDocumentsPromptState(defaultAuthorA, defaultAuthorB, reviewerATitle));

        Owner = owner;
        Title = plan.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _authorABox = new TextBox
        {
            Text = plan.DefaultAuthorA,
            MinWidth = 200,
            MaxWidth = 240
        };
        _authorBBox = new TextBox
        {
            Text = plan.DefaultAuthorB,
            MinWidth = 200,
            MaxWidth = 240
        };

        // Grid: original path row, reviewer-A (current doc) row, reviewer-B path row, separator, two
        // author rows, then OK/Cancel.
        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddReadOnlyRow(grid, 0, plan.OriginalLabel, plan.OriginalDisplayPath);
        AddReadOnlyRow(grid, 1, plan.ReviewerALabel, plan.ReviewerADisplayName);
        AddReadOnlyRow(grid, 2, plan.ReviewerBLabel, plan.ReviewerBDisplayPath);

        var sep = new Separator { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(sep, 3);
        Grid.SetColumnSpan(sep, 2);
        grid.Children.Add(sep);

        AddFieldRow(grid, 4, plan.AuthorALabel, _authorABox);
        AddFieldRow(grid, 5, plan.AuthorBLabel, _authorBBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_authorABox);
    }

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
        if (!ReviewCompareCombineWorkflow.TryBuildCombineDialogResult(
                _originalPath,
                _reviewerBPath,
                _authorABox.Text,
                _authorBBox.Text,
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
    /// Test seam: create the dialog already seeded with all path and author defaults without showing file
    /// pickers, so STA tests can exercise the control wiring without a modal loop.
    /// </summary>
    internal static CombineDocumentsDialog CreateForTest(
        string originalPath,
        string reviewerBPath,
        string defaultAuthorA,
        string defaultAuthorB,
        string reviewerATitle = "") =>
        new(owner: null, originalPath, reviewerBPath, defaultAuthorA, defaultAuthorB, reviewerATitle);

    /// <summary>
    /// Test seam: validate the current author values and return the shared result, without
    /// closing the window (mirrors <see cref="CompareDocumentsDialog.AcceptForTest"/>).
    /// </summary>
    internal CombineDocumentsDialogResult? AcceptForTest()
    {
        TryAccept(showWarnings: false);
        return _result;
    }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Run the three-phase Combine dialog (two file pickers then confirm/author dialog) owned by
    /// <paramref name="owner"/>. <paramref name="defaultAuthorA"/> seeds the Reviewer A author box;
    /// <paramref name="defaultAuthorB"/> seeds the Reviewer B author box;
    /// <paramref name="reviewerATitle"/> shows as the "Reviewer A:" display name. Returns null if the user
    /// cancels any phase.
    /// </summary>
    public static CombineDocumentsDialogResult? Prompt(
        Window? owner,
        string defaultAuthorA,
        string defaultAuthorB,
        string reviewerATitle = "")
    {
        // Phase 1: file picker for the original (base) document.
        var originalPicker = WpfFileDialogService.ShowOpenDialog(
            owner,
            ReviewCompareCombineWorkflow.CombineDocumentFilter,
            defaultExtensionWithDot: ReviewCompareCombineWorkflow.CombineDocumentDefaultExtension,
            title: ReviewCompareCombineWorkflow.CombineOriginalPickerTitle);
        if (!originalPicker.Chosen)
            return null;

        // Phase 2: file picker for reviewer B's revised copy.
        var reviewerBPicker = WpfFileDialogService.ShowOpenDialog(
            owner,
            ReviewCompareCombineWorkflow.CombineDocumentFilter,
            defaultExtensionWithDot: ReviewCompareCombineWorkflow.CombineDocumentDefaultExtension,
            title: ReviewCompareCombineWorkflow.CombineReviewerBPickerTitle);
        if (!reviewerBPicker.Chosen)
            return null;

        // Phase 3: confirm/author dialog.
        var dlg = new CombineDocumentsDialog(
            owner,
            originalPicker.FileName!,
            reviewerBPicker.FileName!,
            defaultAuthorA,
            defaultAuthorB,
            reviewerATitle);
        dlg.ShowDialog();
        return dlg._result;
    }
}
