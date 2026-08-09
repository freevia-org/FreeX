using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves placeholder inheritance for a <see cref="SlideShape"/>.
/// A shape that is a placeholder and has missing geometry or text properties inherits them from
/// the matching placeholder on the slide's layout and then on the layout's master.
/// "Matching" = same Type and Idx as the placeholder tag on the shape.
/// </summary>
public static class PlaceholderResolver
{
    // â”€â”€â”€ Public entry point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Resolves inherited run-level text defaults for a shape, using the slide's exact
    /// layout/master linkage (never a list-order scan across every layout in the file, which
    /// can pick an unrelated layout's compatible placeholder when idx/type match by coincidence).
    /// Returns the placeholder from layout/master that carries text defaults (font, size, color).
    /// </summary>
    public static SlideShape? FindInheritedTextSource(SlideShape shape, Slide slide, PresentationModel presentation)
    {
        if (shape.Placeholder is null) return null;
        return FindLayoutPlaceholder(shape.Placeholder, slide, presentation)
            ?? FindMasterPlaceholder(shape.Placeholder, slide, presentation);
    }

    // â”€â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// All-layouts fallback used only when a master lookup cannot be anchored to a specific
    /// slide's layout (e.g. an orphaned slide whose LayoutId no longer resolves). Deliberately
    /// NOT exposed for the primary (slide-known) resolution path -- see FindMasterPlaceholder(ph, slide, presentation).
    /// </summary>
    private static SlideShape? FindMasterPlaceholder(Placeholder ph, PresentationModel presentation)
    {
        foreach (var master in presentation.Masters)
            foreach (var mph in master.Placeholders)
                if (MatchesPlaceholder(mph, ph))
                    return mph;
        return null;
    }

    /// <summary>
    /// Finds the layout placeholder for a shape on a specific slide.
    /// </summary>
    internal static SlideShape? FindLayoutPlaceholder(Placeholder ph, Slide slide, PresentationModel presentation)
    {
        var layout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
        if (layout is null) return null;
        return layout.Placeholders.Find(lph => MatchesPlaceholder(lph, ph));
    }

    /// <summary>
    /// Finds the master placeholder for a shape on a specific slide.
    /// </summary>
    internal static SlideShape? FindMasterPlaceholder(Placeholder ph, Slide slide, PresentationModel presentation)
    {
        var layout = presentation.Layouts.Find(l => l.Id == slide.LayoutId);
        if (layout is null)
            return FindMasterPlaceholder(ph, presentation);

        var master = presentation.Masters.Find(m => m.Id == layout.MasterId);
        if (master is null) return null;
        return master.Placeholders.Find(mph => MatchesPlaceholder(mph, ph));
    }

    /// <summary>
    /// Resolves anchor for a specific slide's context (preferred; uses exact layout/master linkage).
    /// </summary>
    public static ResolvedAnchor ResolveAnchor(SlideShape shape, Slide slide, PresentationModel presentation)
    {
        if (shape.ExtentCxEmu > 0 || shape.ExtentCyEmu > 0)
            return new ResolvedAnchor(shape.OffsetXEmu, shape.OffsetYEmu,
                                      shape.ExtentCxEmu, shape.ExtentCyEmu,
                                      shape.RotationDeg, shape.FlipH, shape.FlipV);

        if (shape.Placeholder is null)
            return new ResolvedAnchor(shape.OffsetXEmu, shape.OffsetYEmu,
                                      shape.ExtentCxEmu, shape.ExtentCyEmu,
                                      shape.RotationDeg, shape.FlipH, shape.FlipV);

        var layoutPh = FindLayoutPlaceholder(shape.Placeholder, slide, presentation);
        if (layoutPh is not null && (layoutPh.ExtentCxEmu > 0 || layoutPh.ExtentCyEmu > 0))
            return new ResolvedAnchor(layoutPh.OffsetXEmu, layoutPh.OffsetYEmu,
                                      layoutPh.ExtentCxEmu, layoutPh.ExtentCyEmu,
                                      layoutPh.RotationDeg, layoutPh.FlipH, layoutPh.FlipV);

        var masterPh = FindMasterPlaceholder(shape.Placeholder, slide, presentation);
        if (masterPh is not null && (masterPh.ExtentCxEmu > 0 || masterPh.ExtentCyEmu > 0))
            return new ResolvedAnchor(masterPh.OffsetXEmu, masterPh.OffsetYEmu,
                                      masterPh.ExtentCxEmu, masterPh.ExtentCyEmu,
                                      masterPh.RotationDeg, masterPh.FlipH, masterPh.FlipV);

        return new ResolvedAnchor(shape.OffsetXEmu, shape.OffsetYEmu,
                                  shape.ExtentCxEmu, shape.ExtentCyEmu,
                                  shape.RotationDeg, shape.FlipH, shape.FlipV);
    }

    private static bool MatchesPlaceholder(SlideShape candidate, Placeholder target)
    {
        if (candidate.Placeholder is null) return false;
        return candidate.Placeholder.Idx == target.Idx &&
               AreCompatibleTypes(candidate.Placeholder.Type, target.Type);
    }

    /// <summary>
    /// Returns true when two placeholder types are "compatible" for inheritance matching,
    /// following PowerPoint's matching semantics.
    /// <para>
    /// Title group: <see cref="PlaceholderType.Title"/> and <see cref="PlaceholderType.CenteredTitle"/>
    /// are interchangeable (a slide ctrTitle matches a layout title and vice-versa).
    /// </para>
    /// <para>
    /// Body/Content group: <see cref="PlaceholderType.Body"/>, <see cref="PlaceholderType.Object"/>,
    /// <see cref="PlaceholderType.Chart"/>, <see cref="PlaceholderType.Table"/>,
    /// <see cref="PlaceholderType.ClipArt"/>, <see cref="PlaceholderType.Diagram"/>,
    /// <see cref="PlaceholderType.Media"/>, and <see cref="PlaceholderType.Picture"/> are
    /// interchangeable.  A slide placeholder with no explicit type (defaults to Body) will
    /// therefore match a layout "obj" placeholder at the same idx.
    /// </para>
    /// <para>
    /// All other types (SubTitle, Footer, DateTime, SlideNumber, Header) require an exact
    /// type match.
    /// </para>
    /// </summary>
    private static bool AreCompatibleTypes(PlaceholderType a, PlaceholderType b)
    {
        if (a == b) return true;

        // Title group: Title ↔ CenteredTitle
        if (IsTitleGroup(a) && IsTitleGroup(b)) return true;

        // Body/Content group: Body, Object, and the specialized content subtypes
        if (IsContentGroup(a) && IsContentGroup(b)) return true;

        return false;
    }

    private static bool IsTitleGroup(PlaceholderType t) =>
        t == PlaceholderType.Title || t == PlaceholderType.CenteredTitle;

    private static bool IsContentGroup(PlaceholderType t) =>
        t == PlaceholderType.Body    ||
        t == PlaceholderType.Object  ||
        t == PlaceholderType.Chart   ||
        t == PlaceholderType.Table   ||
        t == PlaceholderType.ClipArt ||
        t == PlaceholderType.Diagram ||
        t == PlaceholderType.Media   ||
        t == PlaceholderType.Picture;
}

/// <summary>Fully-resolved anchor for a shape: EMU absolute coordinates.</summary>
public readonly record struct ResolvedAnchor(
    long OffsetXEmu,
    long OffsetYEmu,
    long ExtentCxEmu,
    long ExtentCyEmu,
    double RotationDeg,
    bool FlipH,
    bool FlipV);

