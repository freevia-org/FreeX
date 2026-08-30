using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
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
}
