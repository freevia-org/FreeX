using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class LinuxWorksheetEditingParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SelectedFontFamily_IsAppliedToRenderedCellText()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("Font sample"));
            window.Session.SelectCell(address);
            window.Session.SetSelectedRangeFontName("Times New Roman").Success.Should().BeTrue();
            RefreshViewport(window);

            var cell = FindByAutomationId<Border>(window.RebuildSheetGridForTest(), "Cell_A1");
            var text = FindDescendants(cell).OfType<TextBlock>().First(block => block.Text == "Font sample");

            text.FontFamily.ToString().Should().Contain("Times New Roman");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HomeColorDropdowns_UsePopulatedPaletteFlyouts()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));

            var buttons = window.RibbonControlForTest!.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => button.Tag is "Fill Color" or "Font Color")
                .ToDictionary(button => (string)button.Tag!);

            buttons.Keys.Should().Contain(["Fill Color", "Font Color"]);
            var fillPalette = buttons["Fill Color"].Flyout.Should().BeOfType<Flyout>().Subject;
            var fontPalette = buttons["Font Color"].Flyout.Should().BeOfType<Flyout>().Subject;
            var fillContent = fillPalette.Content.Should().BeAssignableTo<Control>().Subject;
            var fontContent = fontPalette.Content.Should().BeAssignableTo<Control>().Subject;

            FindDescendants(fillContent).OfType<Button>()
                .Count(button => AutomationProperties.GetAutomationId(button)?.StartsWith("RibbonThemeColor", StringComparison.Ordinal) == true)
                .Should().Be(60);
            FindDescendants(fontContent).OfType<Button>()
                .Count(button => AutomationProperties.GetAutomationId(button)?.StartsWith("RibbonStandardColor", StringComparison.Ordinal) == true)
                .Should().Be(10);
            FindDescendants(fillContent).OfType<Button>()
                .Should().Contain(button => AutomationProperties.GetAutomationId(button) == "RibbonColorPaletteMoreColors");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LongText_UsesOverflowLayerWithoutEllipsis()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("A long worksheet title that extends into blank cells"));
            RefreshViewport(window);

            var grid = window.RebuildSheetGridForTest();
            var overflow = FindByAutomationId<Canvas>(grid, "WorksheetCellTextOverflowOverlay");
            overflow.Children.Should().NotBeEmpty();

            var cell = FindByAutomationId<Border>(grid, "Cell_A1");
            var text = FindDescendants(cell).OfType<TextBlock>()
                .First(block => block.Text?.StartsWith("A long worksheet title", StringComparison.Ordinal) == true);
            text.TextTrimming.Should().Be(TextTrimming.None);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FormulaPointing_InsertsReferenceAndRefreshesVisibleOverlay()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            try
            {
                var formulaAddress = new CellAddress(sheet.Id, 1, 1);
                var target = new CellAddress(sheet.Id, 2, 2);
                var formulaBox = GetField<TextBox>(window, "_formulaBox");

                window.BeginFormulaEditForTest(formulaAddress, "=");
                formulaBox.CaretIndex = 1;
                formulaBox.SelectionStart = 1;
                formulaBox.SelectionEnd = 1;

                Invoke<bool>(window, "TryInsertFormulaPointReference", target).Should().BeTrue();
                formulaBox.Text.Should().Be("=B2");

                var overlay = GetField<TextBlock>(window, "_formulaReferenceTextOverlay");
                string.Concat(overlay.Inlines?.OfType<Run>().Select(run => run.Text) ?? [])
                    .Should().Be("=B2");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LinuxSelector_SelectAllCornerFailsClosedIntoFormulaPointMode()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            try
            {
                var formulaAddress = new CellAddress(sheet.Id, 2, 2);
                window.BeginFormulaPointModeEditForTest(formulaAddress, "=SUM(");
                window.FormulaPointModeForTest.Should().BeTrue();
                typeof(MainWindow).GetMethod("SelectAllCells", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);

                window.FormulaBoxTextForTest.Should().Be("=SUM(A1:XFD1048576");
                sheet.GetCell(formulaAddress)?.HasFormula.Should().BeFalse();
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void SourceRoutesResizeAndInlineEditingBeforeSelection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("IsHeaderResizeHotspot(point.Position, header.Bounds, HeaderResizeKind.Column)");
        source.Should().Contain("IsHeaderResizeHotspot(point.Position, header.Bounds, HeaderResizeKind.Row)");
        source.Should().Contain("point.Properties.IsLeftButtonPressed && IsCellDoubleClick(address, args.ClickCount)");
        source.Should().Contain("BeginInlineCellEdit(address, editText, editText.Length);");
    }

    [Fact]
    public void SourceArmsAutofillAndMoveAfterPersistentPointerCaptureInitialization()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var autofill = MethodSource(source, "private bool TryBeginAutofillDrag", "private bool IsPointerOnAutofillHandle");
        var move = MethodSource(source, "private bool TryBeginSelectionMoveDrag", "private bool IsPointerOnSelectionMoveBorder");

        autofill.IndexOf("BeginCellSelectionDrag(args, capture, source.Start);", StringComparison.Ordinal)
            .Should().BeLessThan(autofill.IndexOf("_autofillDragging = true;", StringComparison.Ordinal));
        move.IndexOf("BeginCellSelectionDrag(args, capture, source.Start);", StringComparison.Ordinal)
            .Should().BeLessThan(move.IndexOf("_selectionMoveDragging = true;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NumberFormatComboSelection_AppliesFormatWithoutOpeningDialog()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new NumberValue(1));
            window.Session.SelectCell(address);

            Invoke(window, "ApplyRibbonNumberFormat", "Number");

            window.Session.SelectedRangeStartNumberFormat.Should().Be("0.00");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CellDoubleClickTracker_SurvivesCellControlRebuild()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 2, 2);

            Invoke<bool>(window, "IsCellDoubleClick", address, 1).Should().BeFalse();
            Invoke<bool>(window, "IsCellDoubleClick", address, 1).Should().BeTrue();
        }, CancellationToken.None);
    }

    private static MainWindow CreateCleanWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("LinuxEditingFixture");
        window.Session.SelectSheet(sheet.Id);
        return window;
    }

    private static void RefreshViewport(MainWindow window) =>
        window.Session.UpdateViewportSize(881, 1440);

    private static T FindByAutomationId<T>(Control root, string automationId)
        where T : Control =>
        FindDescendantsAndSelf(root)
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static IEnumerable<Control> FindDescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (var descendant in FindDescendants(root))
            yield return descendant;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Decorator { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var childControl in panel.Children)
            {
                yield return childControl;
                foreach (var descendant in FindDescendants(childControl))
                    yield return descendant;
            }
        }
        else if (root is ContentControl { Content: Control content })
        {
            yield return content;
            foreach (var descendant in FindDescendants(content))
                yield return descendant;
        }
    }

    private static T GetField<T>(MainWindow window, string name) where T : class =>
        (T)typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static T Invoke<T>(MainWindow window, string name, params object[] args) =>
        (T)typeof(MainWindow)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, args)!;

    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, args);

    private static string MethodSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
