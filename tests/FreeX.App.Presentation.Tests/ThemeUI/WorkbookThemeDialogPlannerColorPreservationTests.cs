using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Presentation.ThemeUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ThemeUI;

/// <summary>
/// R48-io-theme-fonts-colors-3-1: Theme Colors dialog Save must only rewrite the clrScheme
/// slots the user actually changed, preserving the original native XML form (e.g. sysClr
/// "Automatic" bindings) of every untouched slot instead of baking all twelve slots into
/// literal srgbClr values on every save.
/// </summary>
public sealed class WorkbookThemeDialogPlannerColorPreservationTests
{
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    // Matches WorkbookTheme.Office's baked RGB values exactly (dk1/lt1 via sysClr "Automatic"
    // bindings, as a real Excel-authored theme1.xml commonly defines them; the rest as the
    // srgbClr values Office.OfficeColors already carries).
    private const string NativeColorSchemeXml =
        "<a:clrScheme xmlns:a=\"" + DrawingNs + "\" name=\"Office\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
        "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2>" +
        "<a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"156082\"/></a:accent1>" +
        "<a:accent2><a:srgbClr val=\"E97132\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"196B24\"/></a:accent3>" +
        "<a:accent4><a:srgbClr val=\"0F9ED5\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"A02B93\"/></a:accent5>" +
        "<a:accent6><a:srgbClr val=\"4EA72E\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink>" +
        "<a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>" +
        "</a:clrScheme>";

    private static WorkbookTheme CreateInitialTheme() =>
        WorkbookTheme.Office.WithNativeColorSchemeXml(NativeColorSchemeXml);

    private static Dictionary<WorkbookThemeColorSlot, string> LoadDialogTextFromTheme(WorkbookTheme theme) =>
        WorkbookThemeColorSlots.All.ToDictionary(
            slot => slot,
            slot => WorkbookThemeDialogColorCodec.FormatColor(theme.GetColor(slot)));

    [Fact]
    public void TryCreateTheme_EditingOnlyAccent1_PreservesDark1AndLight1AsSysClr()
    {
        var initialTheme = CreateInitialTheme();
        var colorText = LoadDialogTextFromTheme(initialTheme);
        colorText[WorkbookThemeColorSlot.Accent1] = "#010203";

        WorkbookThemeDialogPlanner.TryCreateTheme(
                initialTheme,
                "Demo",
                "Georgia",
                "Verdana",
                "Office",
                colorText,
                out var theme,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(0x01, 0x02, 0x03));

        var savedScheme = XElement.Parse(theme.NativeColorSchemeXml!);
        XNamespace ns = DrawingNs;

        // The untouched dk1/lt1 "Automatic" slots must keep their original sysClr element --
        // not be baked into a plain srgbClr just because Accent1 was edited on the same save.
        savedScheme.Element(ns + "dk1")!.Element(ns + "sysClr").Should().NotBeNull(
            "dk1 was never touched by the user and must keep its sysClr (Automatic) form");
        savedScheme.Element(ns + "dk1")!.Element(ns + "srgbClr").Should().BeNull();
        savedScheme.Element(ns + "lt1")!.Element(ns + "sysClr").Should().NotBeNull(
            "lt1 was never touched by the user and must keep its sysClr (Automatic) form");
        savedScheme.Element(ns + "lt1")!.Element(ns + "srgbClr").Should().BeNull();

        // The actually-edited Accent1 slot is the only one that becomes a fresh srgbClr.
        savedScheme.Element(ns + "accent1")!.Element(ns + "srgbClr")!.Attribute("val")!.Value
            .Should().Be("010203");
    }

    [Fact]
    public void TryCreateTheme_EditingEveryColor_StillProducesRequestedPalette()
    {
        // Sibling no-regression case: when every slot's text really is changed, all twelve
        // slots must still be applied (this is the existing, already-covered "customize every
        // slot" dialog flow -- it must keep working once untouched slots start being skipped).
        var initialTheme = CreateInitialTheme();
        var colorText = WorkbookThemeColorSlots.All.ToDictionary(slot => slot, _ => "#010203");
        colorText[WorkbookThemeColorSlot.Accent1] = "#112233";

        WorkbookThemeDialogPlanner.TryCreateTheme(
                initialTheme,
                "Demo",
                "Georgia",
                "Verdana",
                "Office",
                colorText,
                out var theme,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(0x11, 0x22, 0x33));
        theme.GetColor(WorkbookThemeColorSlot.Dark1).Should().Be(new CellColor(0x01, 0x02, 0x03));
        theme.GetColor(WorkbookThemeColorSlot.Light1).Should().Be(new CellColor(0x01, 0x02, 0x03));

        var savedScheme = XElement.Parse(theme.NativeColorSchemeXml!);
        XNamespace ns = DrawingNs;

        // dk1/lt1 were genuinely changed this time, so they legitimately become baked srgbClr.
        savedScheme.Element(ns + "dk1")!.Element(ns + "srgbClr")!.Attribute("val")!.Value
            .Should().Be("010203");
        savedScheme.Element(ns + "lt1")!.Element(ns + "srgbClr")!.Attribute("val")!.Value
            .Should().Be("010203");
    }
}
