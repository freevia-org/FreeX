using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

internal sealed partial class FindReplaceDialog
{
    internal FindReplaceDialogOpenMode OpenModeForTest => _session.State.OpenMode;

    internal FindReplaceDialogOpenMode? FocusedFieldForTest =>
        _findBox.IsKeyboardFocusWithin ? FindReplaceDialogOpenMode.Find :
        _replaceBox.IsKeyboardFocusWithin ? FindReplaceDialogOpenMode.Replace : null;

    internal void SetFindTextForTest(string text) => _findBox.Text = text;
    internal void SetReplaceTextForTest(string text) => _replaceBox.Text = text;
    internal void ReplaceForTest() => Execute(FindReplaceDialogActionKind.Replace);
    internal void ReplaceAllForTest() => Execute(FindReplaceDialogActionKind.ReplaceAll);
    internal string StatusForTest => _status.Text;
}

internal sealed partial class BookmarkManagerDialog
{
    internal static BookmarkManagerDialog CreateForVisualHarness(Window owner, DocumentView editor) =>
        new(owner, editor);
}

internal sealed partial class ManualHyphenationDialog
{
    internal static ManualHyphenationDialog CreateForVisualHarness(
        Window owner,
        ManualHyphenationCandidate candidate) => new(owner, candidate);
}

internal sealed partial class MarkIndexEntryDialog
{
    internal static MarkIndexEntryDialog CreateForTest(
        string seed = "",
        IReadOnlyList<string>? bookmarkNames = null) =>
        new(null, MarkIndexEntryDialogPlanner.BuildInitialState(seed), bookmarkNames ?? []);

    internal void SetForTest(
        string? mainEntry,
        string? subentry,
        bool useCrossReference,
        string? crossReference,
        bool boldPageNumber = false,
        bool italicPageNumber = false,
        string? identifier = null) =>
        SetReferenceForTest(
            mainEntry,
            subentry,
            useCrossReference ? IndexEntryReferenceKind.CrossReference : IndexEntryReferenceKind.CurrentPage,
            bookmarkName: null,
            crossReference,
            boldPageNumber,
            italicPageNumber,
            identifier);

    internal void SetReferenceForTest(
        string? mainEntry,
        string? subentry,
        IndexEntryReferenceKind referenceKind,
        string? bookmarkName,
        string? crossReference,
        bool boldPageNumber = false,
        bool italicPageNumber = false,
        string? identifier = null)
    {
        _mainEntry.Text = mainEntry;
        _subentry.Text = subentry;
        _identifier.Text = identifier;
        _currentPage.IsChecked = referenceKind == IndexEntryReferenceKind.CurrentPage;
        _pageRange.IsChecked = referenceKind == IndexEntryReferenceKind.PageRange;
        _crossReferenceOption.IsChecked = referenceKind == IndexEntryReferenceKind.CrossReference;
        _bookmarkName.SelectedItem = bookmarkName;
        _crossReference.Text = crossReference;
        _boldPageNumber.IsChecked = boldPageNumber;
        _italicPageNumber.IsChecked = italicPageNumber;
        UpdateReferenceState();
    }

    internal bool AcceptForTest() => Accept(markAll: false, closeOnSuccess: false);
    internal bool AcceptAllForTest() => Accept(markAll: true, closeOnSuccess: false);
    internal MarkIndexEntryDialogResult? ResultForTest => _result;
    internal bool CrossReferenceEnabledForTest => _crossReference.IsEnabled;
    internal bool BookmarkSelectorEnabledForTest => _bookmarkName.IsEnabled;
    internal IReadOnlyList<string> BookmarkNamesForTest => _bookmarkName.Items.Cast<string>().ToArray();
    internal bool PageNumberFormattingEnabledForTest => _boldPageNumber.IsEnabled && _italicPageNumber.IsEnabled;
    internal bool MarkAllEnabledForTest => _markAll.IsEnabled;
}

internal sealed partial class CompareDocumentsDialog
{
    internal static CompareDocumentsDialog CreateForTest(
        string originalPath,
        string defaultAuthor,
        string revisedTitle = "") =>
        new(owner: null, originalPath, defaultAuthor, revisedTitle);

    internal CompareDocumentsDialogResult? AcceptForTest()
    {
        TryAccept(showWarnings: false);
        return _result;
    }

    internal Expander MoreExpanderForTest => _moreExpander;
}

internal sealed partial class CombineDocumentsDialog
{
    internal static CombineDocumentsDialog CreateForTest(
        string originalPath,
        string reviewerBPath,
        string defaultAuthorA,
        string defaultAuthorB,
        string reviewerATitle = "") =>
        new(owner: null, originalPath, reviewerBPath, defaultAuthorA, defaultAuthorB, reviewerATitle);

    internal CombineDocumentsDialogResult? AcceptForTest()
    {
        TryAccept(showWarnings: false);
        return _result;
    }
}

internal sealed partial class FootnoteEndnoteOptionsDialog
{
    internal void ValidateForTest()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        FocusFailure(acceptance.Validation?.Field);
    }
}

internal sealed partial class InsertIndexDialog
{
    internal static InsertIndexDialog CreateForTest(string? identifier = null) =>
        new(null, InsertIndexDialogPlanner.BuildInitialState(identifier), isUpdate: false);

    internal static InsertIndexDialog CreateForUpdateTest(string? identifier = null) =>
        new(null, InsertIndexDialogPlanner.BuildInitialState(identifier), isUpdate: true);

    internal void SetIdentifierForTest(string? identifier) => _identifier.Text = identifier;
    internal void AcceptForTest() => Accept(closeOnSuccess: false);
    internal InsertIndexDialogResult? ResultForTest => _result;
    internal string ActionLabelForTest => _actionLabel;
}

internal sealed partial class MarkCitationDialog
{
    internal static MarkCitationDialog CreateForTest(
        string longCitation = "",
        CitationCategory category = CitationCategory.Cases,
        string shortCitation = "") =>
        new(null, new MarkCitationDialogState(category, longCitation, shortCitation));

    internal void SetForTest(CitationCategory category, string? longCitation, string? shortCitation)
    {
        _categoryCombo.SelectedIndex = _session.CategoryIndex(category);
        _longForm.Text = longCitation;
        _shortForm.Text = shortCitation;
    }

    internal bool AcceptForTest() => Accept(closeOnSuccess: false);
    internal MarkCitationDialogResult? ResultForTest => _result;
}

internal sealed partial class PageSetupDialog
{
    internal static PageSetupDialog CreateForTest(
        PageSettings page,
        PageSetupDialogTabKind initialTab = PageSetupDialogTabKind.Margins) =>
        new(owner: null, page, SectionBreakKind.NextPage, initialTab);

    internal PageSetupDialogResult? AcceptForTest()
    {
        Accept();
        return _result;
    }
}

internal sealed partial class TablePropertiesDialog
{
    internal static TablePropertiesDialog CreateForTest(
        ModelTableContext context,
        TablePropertiesDialogTabKind initialTab = TablePropertiesDialogTabKind.Table) =>
        new(owner: null, context, initialTab);

    internal TablePropertiesValues? AcceptForTest()
    {
        Accept();
        return _result;
    }
}

internal sealed partial class TableOfAuthoritiesDialog
{
    internal static TableOfAuthoritiesDialog CreateForTest(
        bool passim = false,
        bool keepFormatting = false,
        CitationCategory? categoryFilter = null,
        ToaTabLeader leader = ToaTabLeader.Dots) =>
        new(
            owner: null,
            options: new ToaOptions
            {
                UsePassim = passim,
                KeepOriginalFormatting = keepFormatting,
                CategoryFilter = categoryFilter,
                TabLeader = leader,
            });

    internal ToaOptions? AcceptForTest()
    {
        Accept();
        return _result;
    }
}
