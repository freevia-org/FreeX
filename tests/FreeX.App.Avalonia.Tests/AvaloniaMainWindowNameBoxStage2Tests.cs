using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for review5 finding K23 (group C-avalonia-mainwindow, stage 2 — interactive
/// Name Box):
///
///   K23 — The Avalonia (Linux/macOS) shell's Name Box (<c>_cellAddressText</c>, automation id
///         "CellAddressText") was a plain, non-interactive <c>TextBlock</c> with no
///         PointerPressed/Tapped/GotFocus/KeyDown handler anywhere: a user could not click it, type
///         a range/name/table reference, get autocomplete, or define a name by typing — the entire
///         Name Box interaction surface the WPF host provides via its editable
///         <c>CellAddressBox</c> ComboBox (MainWindow.Editing.cs) was simply absent on this
///         platform. Fixed by making <c>_cellAddressText</c> a real, focusable <c>TextBox</c> wired
///         with GotFocus (select-all), KeyDown (Enter-to-navigate / define-name-by-typing /
///         Escape-to-restore), and a chevron <c>DropDownButton</c> flyout listing defined names
///         (workbook-global + names scoped to the active sheet) for basic autocomplete-by-click —
///         mirroring the WPF host's <c>CellAddressBox_KeyDown</c>/<c>_SelectionChanged</c>/
///         <c>_DropDownOpened</c>/<c>TryDefineNameFromNameBox</c>.
///
/// These drive the real production code via the internal test seams
/// (CellAddressBoxTextForTest / RaiseCellAddressBoxKeyDownForTest /
/// CellAddressAutocompleteNamesForTest) added alongside the fix, so the resulting
/// <see cref="MainWindow.Session"/> state and box text reflect actual runtime behavior rather than
/// a source-string proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaMainWindowNameBoxStage2Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ParityPhysicalFixture_PopulatesTheProductionDropdown()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow(
                [InteractionValidationOptions.NameBoxDropdownParityPhysicalFixtureArgument]);

            window.CellAddressAutocompleteNamesForTest().Should().Equal(
                "Sales",
                "Tour Name Box Chart",
                "Tour Name Box Picture",
                "Tour Name Box Shape",
                "Tour Name Box Text Box");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProductionDropdown_RendersNavigationLabelsInItsPopupRows()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow(
                [InteractionValidationOptions.NameBoxDropdownParityPhysicalFixtureArgument]);

            window.CellAddressAutocompleteRenderedNamesForTest().Should().Equal(
                "Sales",
                "Tour Name Box Chart",
                "Tour Name Box Picture",
                "Tour Name Box Shape",
                "Tour Name Box Text Box");
            window.CellAddressAutocompleteOpenForTest.Should().BeTrue();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Enter-to-navigate: plain cell reference ───────────────────────────────────────────────

    [Fact]
    public async Task Enter_WithCellReference_NavigatesActiveCellToThatAddress()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            window.CellAddressBoxTextForTest = "C5";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 5, 3));
            window.CellAddressBoxTextForTest.Should().Be("C5");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Enter-to-navigate: a range reference selects the whole range ─────────────────────────

    [Fact]
    public async Task Enter_WithRangeReference_SelectsTheWholeRange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            window.CellAddressBoxTextForTest = "B2:D4";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 4, 4)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Enter-to-navigate: an existing defined name navigates to its range ───────────────────

    [Fact]
    public async Task Enter_WithDefinedNameText_NavigatesToTheNamedRange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var namedRange = new GridRange(new CellAddress(sheet.Id, 7, 2), new CellAddress(sheet.Id, 7, 2));
            window.Session.Workbook.DefineNamedRange("Total", namedRange);

            window.CellAddressBoxTextForTest = "Total";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(namedRange.Start);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Enter-to-navigate: sheet-scoped name precedence, matching formula evaluation ─────────

    [Fact]
    public async Task Enter_WithSheetScopedName_PrefersTheScopedRangeOverASameNamedGlobalName()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var globalSheet = window.Session.Workbook.AddSheet("Global");
            var scopedSheet = window.Session.Workbook.AddSheet("Scoped");
            var globalNamedRange = new GridRange(
                new CellAddress(globalSheet.Id, 1, 1), new CellAddress(globalSheet.Id, 1, 1));
            var scopedNamedRange = new GridRange(
                new CellAddress(scopedSheet.Id, 9, 9), new CellAddress(scopedSheet.Id, 9, 9));
            window.Session.Workbook.DefineNamedRange("Shadowed", globalNamedRange);
            window.Session.Workbook.DefineNamedRange("Shadowed", scopedNamedRange, metadata: null, scopeSheetId: scopedSheet.Id);

            window.Session.SelectSheet(scopedSheet.Id);
            window.CellAddressBoxTextForTest = "Shadowed";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(scopedNamedRange.Start);
            window.Session.ActiveSheet.Id.Should().Be(scopedSheet.Id);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Enter-to-navigate: cross-sheet reference switches the active sheet ───────────────────

    [Fact]
    public async Task Enter_WithCrossSheetReference_SwitchesTheActiveSheet()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheetOne = window.Session.Workbook.AddSheet("One");
            var sheetTwo = window.Session.Workbook.AddSheet("Two");
            window.Session.SelectSheet(sheetOne.Id);

            window.CellAddressBoxTextForTest = "Two!B3";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveSheet.Id.Should().Be(sheetTwo.Id);
            window.Session.ActiveCell.Should().Be(new CellAddress(sheetTwo.Id, 3, 2));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Define-name-by-typing: a valid new name typed into the box defines it on the current selection ──

    [Fact]
    public async Task Enter_WithNewValidNameText_DefinesANameOverTheCurrentSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var selection = new GridRange(new CellAddress(sheet.Id, 3, 3), new CellAddress(sheet.Id, 4, 4));
            window.Session.SelectRange(selection);

            window.CellAddressBoxTextForTest = "MyRegion";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.Workbook.TryGetNamedRange("MyRegion", out var defined).Should().BeTrue();
            defined.Should().Be(selection);
            window.CellAddressBoxTextForTest.Should().Be("MyRegion");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Invalid input: neither a parseable reference nor a definable name is rejected, not silently accepted ──

    [Fact]
    public async Task Enter_WithUnparseableText_IsRejectedAndDoesNotMoveTheActiveCellOrDefineAName()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 2, 2));

            // Spaces are neither a valid A1/R1C1 reference token nor a valid defined-name character
            // (Workbook.ValidateNamedRangeName rejects anything but letters/digits/underscore/period),
            // so this text must be rejected outright rather than silently doing nothing useful,
            // crashing, or being accepted as a name.
            window.CellAddressBoxTextForTest = "not a valid ref";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 2));
            window.Session.Workbook.NamedRanges.Should().NotContainKey("not a valid ref");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Escape restores the box text to the active cell's address without navigating ─────────

    [Fact]
    public async Task Escape_RestoresBoxTextToActiveCellAddress()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 6, 1));

            window.CellAddressBoxTextForTest = "garbage text the user typed";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });

            window.CellAddressBoxTextForTest.Should().Be("A6");
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 6, 1));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Basic autocomplete: the name list merges workbook-global and current-sheet-scoped names ──

    [Fact]
    public async Task AutocompleteNames_MergesGlobalAndActiveSheetScopedNamesIntoFullNavigationProjection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var activeSheet = window.Session.Workbook.AddSheet("Active");
            var otherSheet = window.Session.Workbook.AddSheet("Other");
            window.Session.SelectSheet(activeSheet.Id);

            window.Session.Workbook.DefineNamedRange(
                "Zebra", new GridRange(new CellAddress(activeSheet.Id, 1, 1), new CellAddress(activeSheet.Id, 1, 1)));
            window.Session.Workbook.DefineNamedRange(
                "Apple",
                new GridRange(new CellAddress(activeSheet.Id, 2, 2), new CellAddress(activeSheet.Id, 2, 2)),
                metadata: null,
                scopeSheetId: activeSheet.Id);
            // A name scoped to a different sheet must not leak into this sheet's autocomplete list.
            window.Session.Workbook.DefineNamedRange(
                "OtherSheetOnly",
                new GridRange(new CellAddress(otherSheet.Id, 1, 1), new CellAddress(otherSheet.Id, 1, 1)),
                metadata: null,
                scopeSheetId: otherSheet.Id);

            var names = window.CellAddressAutocompleteNamesForTest();

            names.Should().Contain("Apple");
            names.Should().Contain("Zebra");
            names.Should().NotContain("OtherSheetOnly");
            names.Should().OnlyHaveUniqueItems();
            names.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Typing a name (case-insensitively) matches an autocomplete-listed defined name ────────

    [Fact]
    public async Task Enter_WithDifferentCasingOfADefinedName_StillNavigatesToItsRange()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var namedRange = new GridRange(new CellAddress(sheet.Id, 8, 8), new CellAddress(sheet.Id, 8, 8));
            window.Session.Workbook.DefineNamedRange("Picked", namedRange);

            window.CellAddressAutocompleteNamesForTest().Should().Contain("Picked");

            // Excel's Name Box (and its autocomplete) resolves defined names case-insensitively.
            window.CellAddressBoxTextForTest = "picked";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(namedRange.Start);

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DropdownKeyboardSelection_CommitsAHandledEnterOnTheThirdTableEntry()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("KeyboardFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.Workbook.DefineNamedRange(
                "FirstName",
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
            sheet.StructuredTables.Add(new StructuredTableModel
            {
                Id = 32,
                Name = "OrdersTable",
                DisplayName = "OrdersTable",
                Range = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2)),
                HeaderRowCount = 1,
            });
            var shape = new DrawingShapeModel
            {
                Name = "OrdersShape",
                Anchor = new CellAddress(sheet.Id, 4, 4),
            };
            sheet.DrawingShapes.Add(shape);

            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.A });
            window.CellAddressBoxHasPendingEditForTest.Should().BeTrue();

            var selected = window.SelectCellAddressAutocompleteKeyboardForTest(
                Key.Home,
                Key.Down,
                Key.Down,
                Key.Enter);

            selected.Should().NotBeNull();
            selected!.Name.Should().Be("OrdersTable");
            selected.Kind.Should().Be(NameBoxNavigationItemKind.Table);
            window.CellAddressBoxHasPendingEditForTest.Should().BeFalse(
                "committing a dropdown item ends the Name Box edit just like WPF's ComboBox selection");
            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 2, 2)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(Key.Down, KeyModifiers.Alt)]
    [InlineData(Key.F4, KeyModifiers.None)]
    public async Task DropdownShortcut_FromNameBox_OpensTheProductionAutocompletePopup(
        Key key,
        KeyModifiers modifiers)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow(
                [InteractionValidationOptions.NameBoxDropdownParityPhysicalFixtureArgument]);

            window.CellAddressBoxTextForTest = "Sales";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs
            {
                Key = key,
                KeyModifiers = modifiers,
            });

            window.CellAddressAutocompleteOpenForTest.Should().BeTrue();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DropdownSelection_NavigatesToTableAndSelectsNamedObjectAcrossSheets()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var first = window.Session.Workbook.AddSheet("First");
            var second = window.Session.Workbook.AddSheet("Second");
            window.Session.SelectSheet(first.Id);
            var table = new StructuredTableModel
            {
                Id = 31,
                Name = "OrdersTable",
                DisplayName = "OrdersTable",
                Range = new GridRange(
                    new CellAddress(second.Id, 1, 1),
                    new CellAddress(second.Id, 4, 2)),
            };
            second.StructuredTables.Add(table);
            var shape = new DrawingShapeModel
            {
                Name = "OrdersShape",
                Anchor = new CellAddress(second.Id, 8, 3),
            };
            second.DrawingShapes.Add(shape);
            var picture = new PictureModel
            {
                Name = "OrdersPicture",
                Anchor = new CellAddress(second.Id, 9, 3),
                Kind = PictureKind.Image,
            };
            second.Pictures.Add(picture);
            var textBox = new TextBoxModel
            {
                Name = "OrdersTextBox",
                Anchor = new CellAddress(second.Id, 10, 3),
            };
            second.TextBoxes.Add(textBox);
            var chart = new ChartModel
            {
                Name = "OrdersChart",
                DataRange = new GridRange(
                    new CellAddress(second.Id, 11, 3),
                    new CellAddress(second.Id, 12, 4)),
            };
            second.Charts.Add(chart);

            var items = NameBoxDropdownPlanner.Build(window.Session.Workbook, first.Id);
            var tableItem = items.Single(item => item.Name == "OrdersTable");
            tableItem.Kind.Should().Be(NameBoxNavigationItemKind.Table);
            window.SelectCellAddressBoxItemForTest(tableItem).Should().BeTrue();
            window.Session.ActiveSheet.Id.Should().Be(second.Id);
            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(second.Id, 2, 1),
                new CellAddress(second.Id, 4, 2)));

            var objectItem = NameBoxDropdownPlanner
                .Build(window.Session.Workbook, second.Id)
                .Single(item => item.Name == "OrdersShape");
            objectItem.Kind.Should().Be(NameBoxNavigationItemKind.Object);
            window.SelectCellAddressBoxItemForTest(objectItem).Should().BeTrue();
            window.SelectedDrawingObjectKindForTest.Should().Be(SelectionPaneObjectKind.Shape);
            window.SelectedDrawingObjectIdForTest.Should().Be(shape.Id);
            window.Session.ActiveSheet.Id.Should().Be(second.Id);

            foreach (var expected in new[]
            {
                ("OrdersChart", SelectionPaneObjectKind.Chart, chart.Id),
                ("OrdersPicture", SelectionPaneObjectKind.Picture, picture.Id),
                ("OrdersShape", SelectionPaneObjectKind.Shape, shape.Id),
                ("OrdersTextBox", SelectionPaneObjectKind.TextBox, textBox.Id),
            })
            {
                var item = NameBoxDropdownPlanner
                    .Build(window.Session.Workbook, second.Id)
                    .Single(entry => entry.Name == expected.Item1);

                window.Session.SelectCell(new CellAddress(second.Id, 20, 1));
                window.SelectCellAddressBoxItemForTest(item).Should().BeTrue();
                window.SelectedDrawingObjectKindForTest.Should().Be(expected.Item2);
                window.SelectedDrawingObjectIdForTest.Should().Be(expected.Item3);
                window.Session.ActiveSheet.Id.Should().Be(second.Id);
                window.CellAddressBoxTextForTest.Should().Be(expected.Item1,
                    "the WPF Name Box keeps the selected drawing object's name instead of its anchor cell");
            }

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
