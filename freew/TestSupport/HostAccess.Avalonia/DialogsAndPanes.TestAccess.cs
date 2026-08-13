using Avalonia.Controls;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Panes;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed partial class NavigationPane
{
    internal int HeadingItemCount => _headingList.Items.Count;
    internal int CountHeadingsMatching(string term, TextDocument document) =>
        NavigationPaneSession.ProjectHeadings(document, term).Count;
}

public sealed partial class RevealFormattingPane
{
    internal static int DescribeSectionCount(
        RunFormatting run,
        ParagraphFormatting paragraph,
        PageSettings page) =>
        RevealFormatting.Describe(run, paragraph, page).Count;

    internal static IReadOnlyList<RevealFormattingItem> DescribeSection(
        RunFormatting run,
        ParagraphFormatting paragraph,
        PageSettings page,
        string sectionHeading) =>
        RevealFormatting.Describe(run, paragraph, page)
            .FirstOrDefault(section => section.Heading == sectionHeading)?.Items
            ?? [];
}

internal sealed partial class CharacterFormattingPickerDialog
{
    internal IReadOnlyList<Button> PaletteButtonsForTest =>
        _palette.Children.OfType<Button>().ToArray();
    internal TextBlock? PromptForTest => _prompt;
    internal Button ClearButtonForTest => _clear;
    internal static CharacterFormattingPickerDialog ForTestBorder() => new(PickerKind.Border);
    internal static CharacterFormattingPickerDialog ForTestShading() => new(PickerKind.Shading);
}

internal sealed partial class InsertIndexDialog
{
    internal static InsertIndexDialog CreateUpdateForTests(string? identifier = null) =>
        new(isUpdate: true, identifier);
    internal string ActionLabelForTests => _actionLabel;

    internal InsertIndexDialogResult BuildResultForTests(string? identifier)
    {
        _identifier.Text = identifier;
        return BuildResult();
    }
}

internal sealed partial class PasswordPromptDialog
{
    internal static PasswordPromptDialog CreateForTest(string title, string prompt) => new(title, prompt);
    internal TextBox PasswordBoxForTest => _passwordBox;

    internal string? AcceptForTest(string? password)
    {
        _passwordBox.Text = password;
        Accept(close: false);
        return Result;
    }
}

public sealed partial class ReviewBalloonsPane
{
    internal IReadOnlyList<ReviewBalloonLayout> LayoutsForTest => _layouts;
    internal int VisualChildCountForTest => _balloonCanvas.Children.Count;
}

internal sealed partial class CompareDocumentsDialog
{
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
}

internal sealed partial class RestrictEditingDialog
{
    internal Task StopProtectionForTestAsync() => StopProtectionAsync();
}

internal sealed partial class StyleDialog
{
    internal static StyleDialog CreateForVisualHarness(StyleDialogSession session) => new(session);
    internal static double ControlHeightForTests => DialogChromeStyle.ControlHeight;
    internal static double ButtonHeightForTests => DialogChromeStyle.ButtonHeight;
    internal static double CheckBoxHeightForTests => StyleDialogMetrics.CheckBoxHeight;
}

internal sealed partial class SymbolPickerDialog
{
    internal IReadOnlyList<Button> GlyphButtonsForTest => _glyphButtons;

    internal string? SelectGlyphForTest(string glyph)
    {
        SelectGlyph(glyph, close: false);
        return Result;
    }
}

internal sealed partial class TableOfAuthoritiesDialog
{
    internal ToaOptions? BuildResultForTest()
    {
        SynchronizeSession();
        return _session.PlanAcceptance().Options;
    }
}

public sealed partial class CustomizeThemeColorsDialog
{
    internal static double WpfWidthForTests => DialogWidth;
    internal static double WpfLabelColumnWidthForTests => LabelColumnWidth;
    internal static double WpfColorRowHeightForTests => ColorRowHeight;
    internal static double WpfButtonWidthForTests => ActionButtonWidth;
    internal bool AcceptForTests() => Accept(closeOnSuccess: false);
}

public sealed partial class CustomizeThemeFontsDialog
{
    internal bool AcceptForTests() => Accept(closeOnSuccess: false);
}

public sealed partial class PageColorDialog
{
    internal bool AcceptForTests() => TryAccept();

    internal void SelectCustomColorForTests(string value)
    {
        _palette.SelectedIndex = -1;
        _custom.Text = value;
    }
}

public sealed partial class ThemeEffectsDialog
{
    internal bool AcceptForTests()
    {
        Result = DocumentEffectSet.Catalog[Math.Clamp(_effects.SelectedIndex, 0, DocumentEffectSet.Catalog.Count - 1)];
        return true;
    }
}

public sealed partial class StyleSetDialog
{
    internal bool AcceptForTests()
    {
        Result = DocumentStyleSet.Catalog[Math.Clamp(_styleSets.SelectedIndex, 0, DocumentStyleSet.Catalog.Count - 1)];
        return true;
    }
}

public sealed partial class WatermarkDialog
{
    internal void SelectPictureWatermarkForTests(
        byte[] imageBytes,
        string fileName,
        string scaleText,
        bool isHorizontal,
        bool isWashout)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _pictureMode.IsChecked = true;
        LoadPictureImage(fileName, imageBytes);
        _scaleBox.Text = scaleText;
        _pictureHorizontal.IsChecked = isHorizontal;
        _pictureDiagonal.IsChecked = !isHorizontal;
        _washout.IsChecked = isWashout;
        SyncModePanels();
    }

    internal bool AcceptForTests() => Accept(closeOnSuccess: false);
}

public sealed partial class CustomParagraphSpacingDialog
{
    internal bool AcceptForTests() => TryAccept(closeOnSuccess: false);
}

internal sealed partial class CaptionDialog
{
    internal CaptionDialogResult BuildResultForTest(int selectedIndex, string? text) =>
        BuildResult(selectedIndex, text);

    internal CaptionLabel SelectedLabelForTest =>
        _plan.Choices[Math.Clamp(_label.SelectedIndex, 0, _plan.Choices.Count - 1)].Value;
}

internal sealed partial class DateTimeDialog
{
    internal DateTimeDialogResult BuildResultForTest(int selectedIndex, bool updateAutomatically)
    {
        _session.UpdateSelection(Math.Clamp(selectedIndex, 0, _session.Formats.Count - 1));
        _session.UpdateAutomatically(updateAutomatically);
        return _session.PlanAcceptance()!;
    }
}

internal sealed partial class BookmarkManagerDialog
{
    internal int ItemCountForTest => _list.ItemCount;
    internal void SelectForTest(int index) => _list.SelectedIndex = index;
    internal void DeleteForTest() => Delete();
    internal void GoToForTest() => GoTo();
    internal string StatusTextForTest => _status.Text ?? string.Empty;
}

internal sealed partial class FootnoteEndnoteOptionsDialog
{
    internal FootnoteEndnoteOptionsDialogResult? BuildResultForTest()
    {
        SynchronizeSession();
        return _session.PlanAcceptance().Result;
    }

    internal void ValidateForTest() => Accept();
}

internal sealed partial class MultilevelListDialog
{
    internal void ValidateForTest()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        if (acceptance.IsAccepted)
            return;
        FocusValidationTarget(acceptance.Validation);
    }
}

public sealed partial class FindReplaceDialog
{
    internal FindReplaceDialogOpenMode OpenModeForTest => _session.State.OpenMode;

    internal FindReplaceDialogOpenMode? FocusedFieldForTest =>
        _findBox.IsFocused ? FindReplaceDialogOpenMode.Find :
        _replaceBox.IsFocused ? FindReplaceDialogOpenMode.Replace : null;
}

internal sealed partial class MarkIndexEntryDialog
{
    internal void SetForTests(
        string? mainEntry,
        string? subentry,
        bool useCrossReference,
        string? crossReference,
        bool boldPageNumber = false,
        bool italicPageNumber = false,
        string? identifier = null)
    {
        _mainEntry.Text = mainEntry;
        _subentry.Text = subentry;
        _identifier.Text = identifier;
        _currentPage.IsChecked = !useCrossReference;
        _pageRange.IsChecked = false;
        _crossReferenceOption.IsChecked = useCrossReference;
        _crossReference.Text = crossReference;
        _boldPageNumber.IsChecked = boldPageNumber;
        _italicPageNumber.IsChecked = italicPageNumber;
        UpdateCrossReferenceState();
    }

    internal void SetForTests(
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
        _bookmark.SelectedItem = bookmarkName;
        _crossReference.Text = crossReference;
        _boldPageNumber.IsChecked = boldPageNumber;
        _italicPageNumber.IsChecked = italicPageNumber;
        UpdateCrossReferenceState();
    }

    internal bool AcceptForTests() => Accept(markAll: false, closeOnSuccess: false);
    internal bool AcceptAllForTests() => Accept(markAll: true, closeOnSuccess: false);
    internal bool CrossReferenceEnabledForTests => _crossReference.IsEnabled;
    internal bool BookmarkSelectorEnabledForTests => _bookmark.IsEnabled;
    internal bool PageNumberFormattingEnabledForTests => _boldPageNumber.IsEnabled && _italicPageNumber.IsEnabled;
    internal bool MarkAllEnabledForTests => _markAll.IsEnabled;
}

internal sealed partial class MarkCitationDialog
{
    internal void SetForTests(CitationCategory category, string? longCitation, string? shortCitation)
    {
        _categoryBox.SelectedIndex = _session.CategoryIndex(category);
        _longCitationBox.Text = longCitation;
        _shortCitationBox.Text = shortCitation;
    }

    internal bool AcceptForTests() => Accept(closeOnSuccess: false);
}

internal sealed partial class TableFormulaDialog
{
    internal TextBox FormulaBoxForTest => _formula;
    internal ComboBox FormatBoxForTest => _format;
    internal ComboBox FunctionBoxForTest => _function;
    internal TextBlock ValidationForTest => _validation;

    internal TableFormulaField? AcceptForTest(string? formula, string? format)
    {
        _formula.Text = formula;
        _format.Text = format;
        TryAccept(close: false);
        return Result;
    }

    internal void PasteFunctionForTest(string functionName)
    {
        _function.SelectedItem = functionName;
        PasteSelectedFunction();
    }
}

internal sealed partial class TablePropertiesDialog
{
    internal TabControl TabsForTest => _tabs;
    internal TextBlock ValidationForTest => _validation;
    internal Control InitialFocusTargetForTest => ResolveFocusTarget(_session.InitialFocusPlan);
    internal TablePropertiesValues? AcceptForTest() => TryAccept(close: false);
}

public sealed partial class TabsDialog
{
    internal TextBox PositionBoxForTest => _position;
    internal TextBox DefaultTabStopBoxForTest => _defaultTab;
    internal ListBox StopsForTest => _stops;
    internal ComboBox AlignmentBoxForTest => _alignment;
    internal ComboBox LeaderBoxForTest => _leader;
}

public sealed partial class BordersAndShadingDialog
{
    internal TabControl TabsForTest => _tabs;
    internal TextBox ParagraphWidthForTest => _paragraphWidth;
    internal ComboBox PageSettingForTest => _pageSetting;
    internal ComboBox ShadingColorForTest => _shadingColor;
    internal TextBlock StatusForTest => _status;
}

internal sealed partial class OptionsDialog
{
    internal TextBox RecentFilesCapForTest => _recentFilesCap;

    internal IReadOnlyList<(TextBox Replace, TextBox With)> ReplacementEditorsForTest =>
        _replacementEditors.Select(row => (row.Replace, row.With)).ToArray();

    internal void AcceptForTest() => Accept();
}

internal sealed partial class NotesPane
{
    internal static NotesPane CreateForVisualHarness(DocumentView editor)
    {
        var pane = new NotesPane(editor);
        pane.Toggle();
        return pane;
    }

    internal int ItemCountForTest => _list.ItemCount;
    internal DocumentView SubEditorForTest => _subEditor;
    internal void SelectForTest(bool footnote, int id) => ShowAndSelect(footnote, id);
    internal void ApplyForTest() => ApplySelected();
    internal void DeleteForTest() => DeleteSelected();
}

public sealed partial class ReviewingPane
{
    internal int RevisionItemCount => _session.State.Entries.Count;
    internal int SelectedRevisionIndexForTest => _session.State.SelectedIndex;
    internal RevisionEntry? SelectedRevisionForTest => SelectedRevision;
    internal ReviewRevisionSortOrder SortOrderForTest => _session.State.SortOrder;

    internal void SetSortOrderForTest(ReviewRevisionSortOrder order)
    {
        _sortCombo.SelectedIndex = order switch
        {
            ReviewRevisionSortOrder.Sequence => 0,
            ReviewRevisionSortOrder.Author => 1,
            ReviewRevisionSortOrder.Kind => 2,
            ReviewRevisionSortOrder.Date => 3,
            _ => 0,
        };
    }

    internal static IReadOnlyList<RevisionEntry> EnumerateRevisions(TextDocument document) =>
        ReviewingPaneSession.Enumerate(document);
}

internal sealed partial class ThesaurusPane
{
    internal string HeadingForTest => _heading.Text ?? string.Empty;
    internal int SenseCountForTest => _senses.Children.OfType<StackPanel>().Count();
    internal IReadOnlyList<(bool InsertEnabled, bool CopyEnabled)> ActionStatesForTest =>
        _actionButtons.Select(buttons => (buttons.Insert.IsEnabled, buttons.Copy.IsEnabled)).ToArray();

    internal bool ReplaceForTest(string synonym)
    {
        var action = FindAction(synonym);
        var availability = action is null
            ? null
            : _session.PlanAction(
                action,
                _editor.CanReplaceCurrentProofingWord(action.DisplayText),
                CanCopy);
        return availability?.ReplaceIntent is { } intent && Replace(intent);
    }

    internal Task<bool> CopyForTestAsync(string synonym)
    {
        var action = FindAction(synonym);
        var availability = action is null
            ? null
            : _session.PlanAction(
                action,
                _editor.CanReplaceCurrentProofingWord(action.DisplayText),
                CanCopy);
        return availability?.CopyIntent is { } intent
            ? CopyAsync(intent)
            : Task.FromResult(false);
    }
}
