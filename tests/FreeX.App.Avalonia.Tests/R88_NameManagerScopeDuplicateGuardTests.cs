using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-88 regression test for finding R88-app-name-manager-ui-5-1 (HIGH): editing a defined name's
/// Scope dropdown to a scope that already holds an UNRELATED same-text name silently overwrote that
/// pre-existing entry. <c>MainWindow.ShowDefineNameDialogAsync</c> excluded the seed's own name from
/// the duplicate check purely by TEXT match (<c>DefinedNameValidator.Validate(name, existing,
/// seed?.Name)</c>), never checking that the excluded entry actually lived in the seed's ORIGINAL
/// scope -- so a workbook-scoped name with the exact same text as a sheet-scoped seed being re-scoped
/// to Workbook was wrongly treated as "the entry being edited" and let through. Fixed by
/// <c>OriginalNameForDuplicateCheck</c>, which only excludes the seed's name when the candidate scope
/// matches the seed's ORIGINAL scope label (mirrors the WPF host's NamedRangeDialog.DefineOrUpdateName,
/// whose <c>isSameEntry</c> gate requires both the original name AND the original scope to match).
/// </summary>
public sealed class R88_NameManagerScopeDuplicateGuardTests
{
    // A fixed sheet identity shared by every "Sheet1"-labelled scope in these tests, standing in for the
    // real SheetId that DefinedNamesShellGlue.BuildRows/BuildScopeChoices would carry for the actual
    // Sheet1 in a workbook -- same sheet, same identity, matching production (unique sheet names always
    // pair with a single stable SheetId).
    private static readonly SheetId Sheet1Id = SheetId.New();

    private static DefinedNameRow SheetScopedSeed(string name, string sheetScopeLabel) =>
        DefinedNameListProjector.CreateRow(name, sheetScopeLabel, "Sheet1!$A$1", scopeSheetId: Sheet1Id);

    [Fact]
    public void OriginalNameExcluded_OnlyWhenCandidateScopeMatchesSeedsOriginalScope()
    {
        var seed = SheetScopedSeed("Foo", "Sheet1");

        // The failure scenario: seed is Sheet1-scoped "Foo"; the dialog's Scope dropdown is changed to
        // Workbook, where an unrelated workbook-scoped "Foo" already exists. Because the candidate
        // scope ("Workbook") differs from the seed's original scope ("Sheet1"), the seed's name must
        // NOT be excluded from the duplicate check -- that unrelated pre-existing "Foo" must still be
        // caught as a collision.
        var excludedForDifferentScope = MainWindow.OriginalNameForDuplicateCheckForTest(seed, DefinedNameScope.Workbook);

        excludedForDifferentScope.Should().BeNull(
            "an unrelated same-text name already occupying the NEW target scope must be caught as a " +
            "duplicate, not waved through as though it were the entry being edited");

        // End-to-end: validating "Foo" against a workbook scope that already contains "Foo" (the
        // unrelated pre-existing entry) must now fail with Duplicate instead of silently succeeding.
        var result = DefinedNameValidator.Validate("Foo", existingNamesInScope: ["Foo"], excludedForDifferentScope);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(DefinedNameError.Duplicate);
    }

    [Fact]
    public void OriginalNameExcluded_WhenScopeUnchanged_NoRegression()
    {
        // No-regression sibling: a normal in-place edit (RefersTo/Comment change, scope left alone)
        // must still exclude the seed's own name from the duplicate check, or every plain edit of an
        // existing name would start failing with a false "already exists" error.
        var seed = SheetScopedSeed("Foo", "Sheet1");
        // The SAME sheet identity as the seed's (Sheet1Id) -- an unchanged scope, the way the Define
        // Name editor's own scope choices are actually built (identity-keyed, from the real
        // workbook.Sheets): editing back with Scope left on Sheet1 re-selects this exact identity.
        var sameLabelScope = DefinedNameScope.ForSheet(Sheet1Id, "Sheet1");

        var excludedForSameScope = MainWindow.OriginalNameForDuplicateCheckForTest(seed, sameLabelScope);

        excludedForSameScope.Should().Be("Foo");

        // End-to-end: re-validating "Foo" against its own current scope (which of course still
        // contains "Foo") must pass, since the seed's own entry is properly excluded here.
        var result = DefinedNameValidator.Validate("Foo", existingNamesInScope: ["Foo"], excludedForSameScope);
        result.IsValid.Should().BeTrue();
    }
}
