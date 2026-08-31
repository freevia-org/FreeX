using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R175-render-border-theme-color-reresolution (Avalonia sibling of the WPF
/// R175_BorderThemeColorReResolutionTests fix): <see cref="CellBorderPanel"/>'s <c>AddEdge</c>/
/// <c>AddDiagonal</c> read <c>border.Color</c> directly with no theme at all, so a border set via
/// the ribbon's Theme Colors picker (which populates <see cref="CellBorder.ThemeColor"/> alongside
/// a <see cref="CellBorder.Color"/> baked at load time) kept its stale baked color forever on the
/// Linux/macOS shell, even after a Theme Colors swap that correctly re-resolved the identical cell's
/// font/fill. The fix threads an optional <see cref="WorkbookTheme"/> into the panel's constructor
/// and resolves every edge/diagonal color through <see cref="CellBorder.ResolveColor"/>.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R175_CellBorderPanelThemeColorReResolutionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly CellColor StaleBakedRed = new(200, 0, 0);
    private static readonly CellColor NewThemeBlue = new(10, 20, 230);
    private static readonly CellColor PlainExplicitPurple = new(120, 10, 140);
    private static readonly CellColor SecondThemeGreen = new(20, 200, 40);
    private static readonly CellColor ThirdThemeOrange = new(240, 130, 15);

    private static WorkbookTheme ThemeWithAccent2As(CellColor color) =>
        WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, color);

    private static CellStyle StyleWithThemedBottomBorder(CellColor bakedColor, WorkbookThemeColorSlot slot)
    {
        var cellStyle = new CellStyle();
        cellStyle.BorderBottom = new CellBorder(BorderStyle.Thin, bakedColor, new WorkbookThemeColorReference(slot));
        return cellStyle;
    }

    [Fact]
    public async Task BorderThemeColor_ReResolvesAgainstSuppliedTheme_NotStaleBakedColor()
    {
        await Session.Dispatch(() =>
        {
            var style = StyleWithThemedBottomBorder(StaleBakedRed, WorkbookThemeColorSlot.Accent2);
            var theme = ThemeWithAccent2As(NewThemeBlue);

            var panel = new CellBorderPanel(style, theme: theme);
            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(1);
            var stroke = lines[0].Stroke.Should().BeOfType<SolidColorBrush>().Subject;

            stroke.Color.Should().Be(
                Color.FromRgb(NewThemeBlue.R, NewThemeBlue.G, NewThemeBlue.B),
                "the border's ThemeColor must be re-resolved against the SUPPLIED theme (Accent2 -> NewThemeBlue), not the stale baked Color");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BorderThemeColor_WithNoThemeSupplied_FallsBackToOfficeResolution_NotRawBakedColor()
    {
        // Sibling: a caller that omits the theme parameter entirely (the many pre-existing
        // constructions of this panel, including the ones in every other CellBorderPanel*Tests.cs
        // file) must still resolve consistently -- via the Office default, matching
        // WorkbookTheme.Office/ChartRenderer.Render's own no-theme fallback elsewhere in this
        // codebase -- rather than silently reverting to the raw unresolved Color.
        await Session.Dispatch(() =>
        {
            var style = StyleWithThemedBottomBorder(StaleBakedRed, WorkbookThemeColorSlot.Accent2);
            var expected = WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent2, 0.0);

            var panel = new CellBorderPanel(style);
            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var stroke = panel.Children.OfType<Line>().Single().Stroke.Should().BeOfType<SolidColorBrush>().Subject;
            stroke.Color.Should().Be(Color.FromRgb(expected.R, expected.G, expected.B));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlainExplicitBorderColor_WithNoThemeReference_StillRendersExactColor_NoRegression()
    {
        await Session.Dispatch(() =>
        {
            var style = new CellStyle { BorderBottom = new CellBorder(BorderStyle.Thin, PlainExplicitPurple) };
            // Deliberately supply a theme with wildly different accents -- must have ZERO effect on
            // a border with no ThemeColor reference.
            var theme = ThemeWithAccent2As(NewThemeBlue);

            var panel = new CellBorderPanel(style, theme: theme);
            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var stroke = panel.Children.OfType<Line>().Single().Stroke.Should().BeOfType<SolidColorBrush>().Subject;
            stroke.Color.Should().Be(Color.FromRgb(PlainExplicitPurple.R, PlainExplicitPurple.G, PlainExplicitPurple.B),
                "a border with no ThemeColor reference must keep rendering its plain explicit Color unchanged, regardless of the supplied theme");
        }, CancellationToken.None);
    }

    // ------------------------------------------------------------------
    // Two CONSECUTIVE theme swaps. Avalonia rebuilds the panel per render rather than caching a
    // pen the way the WPF grid does, but the same durability question applies: an implementation
    // that captured a theme once (statically, or into a shared brush cache keyed only by the
    // CellBorder) would follow the first swap and then go stale. Each theme must win in turn.
    // ------------------------------------------------------------------
    [Fact]
    public async Task BorderThemeColor_FollowsTwoConsecutiveThemeSwaps()
    {
        await Session.Dispatch(() =>
        {
            var style = StyleWithThemedBottomBorder(StaleBakedRed, WorkbookThemeColorSlot.Accent2);

            foreach (var expected in new[] { NewThemeBlue, SecondThemeGreen, ThirdThemeOrange })
            {
                var panel = new CellBorderPanel(style, theme: ThemeWithAccent2As(expected));
                panel.Measure(new Size(60, 20));
                panel.Arrange(new Rect(0, 0, 60, 20));

                var stroke = panel.Children.OfType<Line>().Single().Stroke.Should().BeOfType<SolidColorBrush>().Subject;
                stroke.Color.Should().Be(Color.FromRgb(expected.R, expected.G, expected.B),
                    "every theme in the sequence must win in turn, not just the first one");
            }
        }, CancellationToken.None);
    }

    // ------------------------------------------------------------------
    // Production wiring. The panel gaining a theme parameter is inert unless the shell actually
    // passes one: both real construction sites reach the panel through MainWindow.CreateCellBorder
    // (worksheet cells, via CreateDefaultCellContent/CreateOrientedCellContent ->
    // AddStyledCellBorderOverlay) so this drives that whole static chain and asserts the border
    // paints the SUPPLIED theme's color. Before the theme was threaded through those methods the
    // panel silently fell back to Office here and this test would see the Office accent instead.
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(0)]   // unrotated cell -> CreateDefaultCellContent
    [InlineData(45)]  // rotated cell   -> CreateOrientedCellContent
    public async Task CreateCellBorder_ThreadsTheSuppliedThemeIntoTheBorderOverlay(int textRotation)
    {
        await Session.Dispatch(() =>
        {
            var style = StyleWithThemedBottomBorder(StaleBakedRed, WorkbookThemeColorSlot.Accent2);

            foreach (var expected in new[] { NewThemeBlue, SecondThemeGreen })
            {
                var border = InvokeCreateCellBorder(style, textRotation, ThemeWithAccent2As(expected));
                border.Measure(new Size(80, 20));
                border.Arrange(new Rect(0, 0, 80, 20));

                var panel = Descendants(border).OfType<CellBorderPanel>().Single();
                var stroke = panel.Children.OfType<Line>().Single().Stroke.Should().BeOfType<SolidColorBrush>().Subject;
                stroke.Color.Should().Be(Color.FromRgb(expected.R, expected.G, expected.B),
                    "MainWindow.CreateCellBorder must thread its theme all the way down to CellBorderPanel");
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Invokes the private static <c>MainWindow.CreateCellBorder</c>, filling every parameter with
    /// its own compile-time default except the few this test cares about. Binding by parameter NAME
    /// (rather than a fixed positional list) keeps the test working as that long optional-argument
    /// list grows.
    /// </summary>
    private static Border InvokeCreateCellBorder(CellStyle style, int textRotation, WorkbookTheme theme)
    {
        var method = typeof(MainWindow).GetMethod(
            "CreateCellBorder", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MainWindow.CreateCellBorder not found");

        var overrides = new Dictionary<string, object?>
        {
            ["text"] = "x",
            ["background"] = null,
            ["foreground"] = Brushes.Black,
            ["textAlignment"] = TextAlignment.Left,
            ["verticalAlignment"] = global::Avalonia.Layout.VerticalAlignment.Center,
            ["textWrapping"] = TextWrapping.NoWrap,
            ["fontWeight"] = FontWeight.Normal,
            ["fontStyle"] = FontStyle.Normal,
            ["fontSize"] = 11.0,
            ["textDecorations"] = null,
            ["selected"] = false,
            ["textRotation"] = textRotation,
            ["style"] = style,
            ["theme"] = theme,
        };

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = overrides.TryGetValue(parameters[i].Name!, out var value)
                ? value
                : parameters[i].DefaultValue;
        }

        parameters.Should().Contain(p => p.Name == "theme",
            "CreateCellBorder must expose a theme parameter for the worksheet cell path to pass one");

        return (Border)method.Invoke(null, args)!;
    }

    private static IEnumerable<Visual> Descendants(Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
