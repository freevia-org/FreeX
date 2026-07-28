namespace FreeP.Core.Model;

/// <summary>
/// The OPC presentation family encoded by <c>[Content_Types].xml</c>.
/// FreeP does not execute VBA or apply template/show-only UI semantics, but it
/// preserves the package identity so PowerPoint can continue to open the file.
/// </summary>
public enum PresentationPackageKind
{
    Presentation,
    MacroEnabledPresentation,
    Template,
    MacroEnabledTemplate,
    SlideShow,
    MacroEnabledSlideShow,
}
