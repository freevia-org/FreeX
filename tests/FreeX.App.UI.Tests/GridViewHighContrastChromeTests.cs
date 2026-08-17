using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

/// <summary>
/// shared-theme-highcontrast/wpf-grid-canvas-ignores-highcontrast (r139): GridView's own drawn
/// chrome -- row/column headers, gridlines, and the selection marquee -- was painted from
/// hardcoded RGB literals that never reacted to Windows High Contrast, unlike the rest of the
/// app's chrome (App.xaml.cs's RefreshSystemColorsBrushOverrides). The fix adds
/// GridView.ResolveHighContrastChromePalette/ApplyHighContrastChromePalette, which swap those
/// specific chrome brushes/pens for SystemColors-derived ones when HC is active, while leaving
/// TextBrush (the DOCUMENT's default cell font color) and every CellStyle-driven fill/font
/// resolution completely untouched -- cell content must render exactly as authored regardless of
/// the OS contrast theme.
///
/// NOTE ON STYLE: GridPen/HeaderBackgroundBrush/etc. are process-wide static fields that
/// EVERY other GridView render test in this assembly reads while asserting their own exact
/// hardcoded pixel expectations, and this assembly does not disable xUnit test-collection
/// parallelization. An earlier version of this file actually flipped ApplyHighContrastChromePalette
/// to true and rendered real pixels -- confirmed reliably: it non-deterministically broke
/// unrelated concurrently-running tests (e.g. R49ActiveHeaderHighlightTests observed the
/// HC-flipped ActiveHeaderHighlightBrush mid-render). So this file proves the wiring via the
/// same source-contract technique already used throughout GridViewRenderPerformanceTests.Rendering.cs
/// (a real, established pattern in this codebase for "does method X actually read field Y"
/// questions) plus one runtime test that only ever applies the palette's OWN already-active
/// (HC-off) default state, which is a no-op for every other concurrently-running test.
/// </summary>
public sealed class GridViewHighContrastChromeTests
{
    // ---- Pure resolver tests: no shared static state touched at all -------------------------

    [Fact]
    public void ResolveHighContrastChromePalette_HighContrastOn_PullsFromLiveSystemColors()
    {
        // Failure scenario (pre-fix): there was no such branch at all -- the grid's chrome was
        // always the same fixed light-mode literals no matter what. Passing highContrastEnabled:
        // true must now yield the CURRENT OS SystemColors instead of those literals.
        var palette = GridView.ResolveHighContrastChromePalette(highContrastEnabled: true);

        ((SolidColorBrush)palette.GridLine).Color.Should().Be(SystemColors.WindowTextColor);
        ((SolidColorBrush)palette.HeaderBackground).Color.Should().Be(SystemColors.ControlColor);
        ((SolidColorBrush)palette.HeaderHighlight).Color.Should().Be(SystemColors.HighlightColor);
        ((SolidColorBrush)palette.ActiveHeaderHighlight).Color.Should().Be(SystemColors.HighlightColor);
        ((SolidColorBrush)palette.HeaderText).Color.Should().Be(SystemColors.WindowTextColor);
        ((SolidColorBrush)palette.SelectionHandle).Color.Should().Be(SystemColors.HighlightColor);
        ((SolidColorBrush)palette.SelectionPen.Brush).Color.Should().Be(SystemColors.HighlightColor);

        // And must NOT be the fixed light-mode gray any more (the actual bug).
        ((SolidColorBrush)palette.GridLine).Color.Should().NotBe(Color.FromRgb(220, 220, 220));
    }

    [Fact]
    public void ResolveHighContrastChromePalette_HighContrastOff_KeepsOriginalLightModeLiterals_NoRegression()
    {
        // Sibling no-regression case: with HC off, the resolved palette must still be byte-for-byte
        // the SAME literals GridView always painted with, so normal (non-HC) usage is unchanged.
        var palette = GridView.ResolveHighContrastChromePalette(highContrastEnabled: false);

        ((SolidColorBrush)palette.GridLine).Color.Should().Be(Color.FromRgb(220, 220, 220));
        ((SolidColorBrush)palette.HeaderBackground).Color.Should().Be(Color.FromRgb(242, 242, 242));
        ((SolidColorBrush)palette.HeaderHighlight).Color.Should().Be(Color.FromRgb(218, 232, 218));
        ((SolidColorBrush)palette.ActiveHeaderHighlight).Color.Should().Be(Color.FromRgb(151, 181, 135));
        ((SolidColorBrush)palette.HeaderText).Color.Should().Be(Colors.Black);
        ((SolidColorBrush)palette.SelectionHandle).Color.Should().Be(Color.FromRgb(33, 115, 70));
        palette.SelectionPen.Thickness.Should().Be(2);
        ((SolidColorBrush)palette.SelectionPen.Brush).Color.Should().Be(Color.FromRgb(33, 115, 70));
    }

    // ---- Wiring tests: prove the render methods a real user's screen paints from actually -----
    // ---- read the mutable fields ApplyHighContrastChromePalette writes (Rule: test the path a --
    // ---- real user reaches, not just the helper) -- via source contracts rather than live -------
    // ---- mutation of the shared statics every other render test in this assembly also reads. --

    [Fact]
    public void ApplyHighContrastChromePalette_WritesEveryChromeFieldFromTheResolvedPalette_ButNeverTextBrush()
    {
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var apply = gridViewSource[
            gridViewSource.IndexOf("internal static void ApplyHighContrastChromePalette", StringComparison.Ordinal)..
            gridViewSource.IndexOf("internal static void RefreshHighContrastChromePalette", StringComparison.Ordinal)];

        apply.Should().Contain("GridLineBrush = palette.GridLine;");
        apply.Should().Contain("HeaderBackgroundBrush = palette.HeaderBackground;");
        apply.Should().Contain("HeaderHighlightBrush = palette.HeaderHighlight;");
        apply.Should().Contain("ActiveHeaderHighlightBrush = palette.ActiveHeaderHighlight;");
        apply.Should().Contain("HeaderTextBrush = palette.HeaderText;");
        apply.Should().Contain("SelectionBrush = palette.Selection;");
        apply.Should().Contain("SelectionPen = palette.SelectionPen;");
        apply.Should().Contain("SelectionHandleBrush = palette.SelectionHandle;");
        apply.Should().Contain("GridPen = MakePen(GridLineBrush, 1);");

        // The document-boundary contract: chrome may react to HC, cell content must not.
        // ApplyHighContrastChromePalette must never assign the plain (document) TextBrush field --
        // strip out the legitimate "HeaderTextBrush =" assignment first so this check isn't
        // defeated by "HeaderTextBrush" itself ending in the substring "TextBrush".
        apply.Replace("HeaderTextBrush =", string.Empty).Should().NotContain("TextBrush =");

        // And TextBrush's OWN declaration must stay a plain (never-reassigned) constant.
        gridViewSource.Should().Contain("private static readonly Brush TextBrush = Brushes.Black;");
    }

    [Fact]
    public void RenderMethodsReadTheMutableChromeFields_NotFreshHardcodedBrushes()
    {
        var headers = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.Headers.cs");
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");

        // Column/row header rect + border: reads the field-level GridPen, not a fresh Pen.
        var drawColumnHeader = headers[
            headers.IndexOf("private void DrawColumnHeader(", StringComparison.Ordinal)..
            headers.IndexOf("private void DrawRowHeader(", StringComparison.Ordinal)];
        drawColumnHeader.Should().Contain("dc.DrawRectangle(background, GridPen, rect);");
        drawColumnHeader.Should().Contain("GetDefaultHeaderFormattedText(textValue, 11, pixelsPerDip);");

        // Header base-layer/outline-level-button/gridline drawing likewise read the field, and
        // header labels are colored via HeaderTextBrush (through GetDefaultHeaderFormattedText),
        // never TextBrush.
        headers.Should().Contain("dc.DrawRectangle(HeaderBackgroundBrush, GridPen,");
        headers.Should().Contain("var brush = activeCell is { } active && active.Col == col.Col ? ActiveHeaderHighlightBrush : HeaderHighlightBrush;");
        headers.Should().NotContain("GetDefaultFormattedText(textValue,");

        // The plain worksheet gridlines (RenderCellBackgroundBase) also read the field-level
        // GridPen -- confirming a live palette swap reaches every gridline draw, not just headers.
        var renderCellBackgroundBase = rendering[
            rendering.IndexOf("private void RenderCellBackgroundBase(", StringComparison.Ordinal)..
            rendering.IndexOf("private static bool IntersectsVisibleGrid(", StringComparison.Ordinal)];
        renderCellBackgroundBase.Should().Contain("dc.DrawLine(GridPen, new Point(left, y), new Point(visibleRight, y));");
    }

    [Fact]
    public void DrawCellSurfaceFillResolution_HasNoHighContrastReference_DocumentBoundaryIsStructural()
    {
        // Sibling/boundary proof: cell fills (CellStyle.FillColor, resolved via
        // CellFillMaterializationPlanner) are workbook DOCUMENT data. The method that resolves and
        // draws them must never mention the HC chrome palette at all -- not "HighContrast", not
        // "HeaderTextBrush", nothing -- so this is a structural guarantee, not just a runtime one.
        var rendering = AppUiSourceTestSupport.ReadAppUiSources("GridView.Rendering.cs");
        var drawCellSurface = rendering[
            rendering.IndexOf("private void DrawCellSurface(", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderCellBackgroundBase(", StringComparison.Ordinal)];

        drawCellSurface.Should().Contain("CellFillMaterializationPlanner.Plan(");
        drawCellSurface.Should().NotContain("HighContrast");
        drawCellSurface.Should().NotContain("HeaderTextBrush");
    }

    [Fact]
    public void Constructor_SubscribesToRealOsHighContrastChangeEvent()
    {
        // Proves the fix is actually reachable from a live OS toggle while the app runs -- not just
        // a method nobody calls. SystemParameters.StaticPropertyChanged is the exact same event
        // App.xaml.cs's RefreshSystemColorsBrushOverrides already relies on for the rest of the
        // app's chrome (menus/dialogs).
        var gridViewSource = AppUiSourceTestSupport.ReadAppUiSources("GridView.cs");
        var ctor = gridViewSource[
            gridViewSource.IndexOf("public GridView()", StringComparison.Ordinal)..
            gridViewSource.IndexOf("private void OnSystemParametersChangedForHighContrastChrome", StringComparison.Ordinal)];

        ctor.Should().Contain("Loaded += (_, _) => SystemParameters.StaticPropertyChanged += OnSystemParametersChangedForHighContrastChrome;");
        ctor.Should().Contain("Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChangedForHighContrastChrome;");

        var handler = gridViewSource[
            gridViewSource.IndexOf("private void OnSystemParametersChangedForHighContrastChrome", StringComparison.Ordinal)..
            gridViewSource.IndexOf("protected override AutomationPeer OnCreateAutomationPeer", StringComparison.Ordinal)];
        handler.Should().Contain("RefreshHighContrastChromePalette();");
        // These caches bake HeaderBackgroundBrush/GridPen/HeaderTextBrush into cached
        // Drawing/FormattedText objects (see DrawHeaderText/GetDefaultHeaderFormattedText), so a
        // live HC toggle must invalidate them or the new palette would be silently absorbed by
        // stale cached drawings instead of showing up on the next paint.
        handler.Should().Contain("_headerBaseLayerCache = null;");
        handler.Should().Contain("ClearSelectedHeaderLayerCache();");
        handler.Should().Contain("_headerTextDrawingCache.Clear();");
        handler.Should().Contain("_defaultHeaderTextLayoutCache.Clear();");
        handler.Should().Contain("InvalidateVisual();");
    }

    // ---- Runtime behavioral test: safe because it only ever re-applies the CURRENTLY-ACTIVE ----
    // ---- (HC-off) default palette on this test machine -- a no-op for every value every other --
    // ---- concurrently-running render test reads, while still proving the cache-invalidation ----
    // ---- side effect (not just source text) actually runs end-to-end when the OS event fires. --

    [Fact]
    public void OnSystemParametersChangedForHighContrastChrome_ClearsStaleHeaderBaseLayerCache()
    {
        // Safe against the cross-test races described in this file's class doc: this only ever
        // re-resolves the palette against THIS test machine's actual (HC-off) SystemParameters
        // state, which is a no-op re-assignment of the SAME default values every other
        // concurrently-running render test already expects -- it never flips anything to the HC
        // palette. What it DOES prove for real (not just via source text) is that firing the
        // handler actually nulls out a populated instance-level cache, end to end.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();

            // Seed the header base-layer cache with dummy non-null state, simulating a grid that
            // already rendered headers once before the OS HC setting changed.
            SetPrivateField(grid, "_headerBaseLayerCache", new DrawingGroup());

            InvokePrivate(grid, "OnSystemParametersChangedForHighContrastChrome", null, EventArgs.Empty);

            GetPrivateField(grid, "_headerBaseLayerCache").Should().BeNull(
                "the SystemParameters change handler must null out the stale header base-layer cache " +
                "so a live HC toggle is not silently absorbed by a cached pre-toggle drawing");
        });
    }

    private static object? GetPrivateField(GridView grid, string fieldName)
    {
        var field = typeof(GridView).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(grid);
    }

    private static void SetPrivateField(GridView grid, string fieldName, object? value)
    {
        var field = typeof(GridView).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(grid, value);
    }

    private static void InvokePrivate(GridView grid, string methodName, params object?[] arguments)
    {
        var method = typeof(GridView).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(grid, arguments);
    }
}
