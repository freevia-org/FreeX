namespace FreeP.Core.Model;

/// <summary>
/// Resolves PowerPoint's built-in table style GUIDs to concrete <see cref="TableStyleData"/> when a
/// <see cref="TableShape"/> references one but has no parsed <see cref="TableShape.StyleData"/>.
///
/// This happens for freshly inserted/pasted tables (<c>EditingSession.InsertTable</c>,
/// <c>ClipboardTablePlanner.TryBuildStandaloneTable</c>), which set <see cref="TableShape.TableStyleId"/>
/// to this GUID but have no <c>ppt/tableStyles.xml</c> to parse. It also covers round-tripped files:
/// <c>PptxPackageWriter.BuildTableStylesXml</c> only emits the <c>tblStyleLst</c> <c>def</c> pointer, not
/// a matching <c>&lt;a:tblStyle&gt;</c> body, so re-reading a FreeP-saved file leaves
/// <see cref="TableShape.StyleData"/> null too. Third-party files that reference this GUID without
/// embedding its definition hit the same gap.
///
/// Colors mirror the actual <c>&lt;a:tblStyle&gt;</c> XML PowerPoint embeds for this GUID (see the
/// <c>ppt/tableStyles.xml</c> part of the <c>05-table.pptx</c> render-compare corpus fixture), with the
/// same Dark2-at-half-tint compatibility adjustment
/// <c>PptxPackageReader.ApplyPowerPointBuiltInTableBandFillCompatibility</c> applies when parsing that
/// XML, so a freshly inserted table renders identically to one round-tripped through a real
/// PowerPoint-authored file.
/// </summary>
public static class BuiltInTableStyleCatalog
{
    /// <summary>"Medium Style 2 - Accent 1" — the style FreeP assigns to newly inserted/pasted tables.</summary>
    public const string MediumStyle2Accent1Id = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";

    private static readonly Lazy<TableStyleData> MediumStyle2Accent1 = new(BuildMediumStyle2Accent1);

    /// <summary>
    /// Returns the built-in style data for <paramref name="styleId"/>, or null if the GUID is not one
    /// of FreeP's known built-in styles (in which case callers should keep treating the table as
    /// unstyled rather than guessing).
    /// </summary>
    public static TableStyleData? TryResolve(string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
            return null;

        return styleId.Trim().Equals(MediumStyle2Accent1Id, StringComparison.OrdinalIgnoreCase)
            ? MediumStyle2Accent1.Value
            : null;
    }

    private static TableStyleData BuildMediumStyle2Accent1()
    {
        var scheme = PresentationColorScheme.CreateDefault();

        var thinBorder = new ShapeOutline.Visible(SchemeColor(scheme, ThemeColorSlot.Lt1, "lt1"), widthPt: 1.0);
        var thickBorder = new ShapeOutline.Visible(SchemeColor(scheme, ThemeColorSlot.Lt1, "lt1"), widthPt: 3.0);
        var accent1Fill = new ShapeFill.Solid(SchemeColor(scheme, ThemeColorSlot.Accent1, "accent1"));
        var lt1Text = SchemeColor(scheme, ThemeColorSlot.Lt1, "lt1");

        return new TableStyleData
        {
            StyleId = MediumStyle2Accent1Id,
            WholeTbl = new TableStyleEntry
            {
                Fill = new ShapeFill.Solid(SchemeColor(scheme, ThemeColorSlot.Dk2, "dk2", tint: 0.1)),
                BorderOutline = thinBorder,
                TextColor = SchemeColor(scheme, ThemeColorSlot.Dk1, "dk1")
            },
            Band1H = new TableStyleEntry
            {
                Fill = new ShapeFill.Solid(SchemeColor(scheme, ThemeColorSlot.Dk2, "dk2", tint: 0.2))
            },
            Band1V = new TableStyleEntry
            {
                Fill = new ShapeFill.Solid(SchemeColor(scheme, ThemeColorSlot.Dk2, "dk2", tint: 0.2))
            },
            FirstRow = new TableStyleEntry { Fill = accent1Fill, BorderOutline = thickBorder, TextColor = lt1Text },
            LastRow  = new TableStyleEntry { Fill = accent1Fill, BorderOutline = thickBorder, TextColor = lt1Text },
            FirstCol = new TableStyleEntry { Fill = accent1Fill, TextColor = lt1Text },
            LastCol  = new TableStyleEntry { Fill = accent1Fill, TextColor = lt1Text }
        };
    }

    private static ThemeAwareColor SchemeColor(
        PresentationColorScheme scheme, ThemeColorSlot slot, string roleName, double tint = 1.0)
    {
        var schemeColor = new SchemeColorRef { RoleName = roleName, Slot = slot, Tint = tint };
        var resolved = ThemeColorTransform.Apply(
            scheme[slot], schemeColor.LumMod, schemeColor.LumOff, schemeColor.Tint, schemeColor.Shade);
        return new ThemeAwareColor(resolved, schemeColor);
    }
}
