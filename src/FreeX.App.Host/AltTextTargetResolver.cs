using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum AltTextObjectKind
{
    Picture,
    Shape,
    TextBox
}

public sealed record AltTextObjectTarget(
    AltTextObjectKind Kind,
    Guid Id,
    CellAddress Anchor,
    string? AltText);

public static class AltTextTargetResolver
{
    public static AltTextObjectTarget? Resolve(
        Sheet sheet,
        CellAddress? selectedAddress,
        AltTextObjectKind? preferredKind = null)
    {
        if (selectedAddress is not { } address)
            return null;

        if (ShouldSearch(preferredKind, AltTextObjectKind.Picture))
        {
            var picture = sheet.Pictures.LastOrDefault(item => IsAnchoredAt(item.Anchor, address));
            if (picture is not null)
                return new AltTextObjectTarget(AltTextObjectKind.Picture, picture.Id, picture.Anchor, picture.AltText);
        }

        if (ShouldSearch(preferredKind, AltTextObjectKind.Shape))
        {
            var shape = sheet.DrawingShapes.LastOrDefault(item => IsAnchoredAt(item.Anchor, address));
            if (shape is not null)
                return new AltTextObjectTarget(AltTextObjectKind.Shape, shape.Id, shape.Anchor, shape.AltText);
        }

        if (ShouldSearch(preferredKind, AltTextObjectKind.TextBox))
        {
            var textBox = sheet.TextBoxes.LastOrDefault(item => IsAnchoredAt(item.Anchor, address));
            if (textBox is not null)
                return new AltTextObjectTarget(AltTextObjectKind.TextBox, textBox.Id, textBox.Anchor, textBox.AltText);
        }

        return null;
    }

    private static bool ShouldSearch(AltTextObjectKind? preferredKind, AltTextObjectKind kind) =>
        preferredKind is null || preferredKind == kind;

    private static bool IsAnchoredAt(CellAddress anchor, CellAddress selectedAddress) =>
        anchor.Row == selectedAddress.Row && anchor.Col == selectedAddress.Col;
}
