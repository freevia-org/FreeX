using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

        if (preferredKind is null or AltTextObjectKind.Picture)
        {
            var picture = sheet.Pictures.LastOrDefault(item => item.Anchor.Row == address.Row && item.Anchor.Col == address.Col);
            if (picture is not null)
                return new AltTextObjectTarget(AltTextObjectKind.Picture, picture.Id, picture.Anchor, picture.AltText);
        }

        if (preferredKind is null or AltTextObjectKind.Shape)
        {
            var shape = sheet.DrawingShapes.LastOrDefault(item => item.Anchor.Row == address.Row && item.Anchor.Col == address.Col);
            if (shape is not null)
                return new AltTextObjectTarget(AltTextObjectKind.Shape, shape.Id, shape.Anchor, shape.AltText);
        }

        if (preferredKind is null or AltTextObjectKind.TextBox)
        {
            var textBox = sheet.TextBoxes.LastOrDefault(item => item.Anchor.Row == address.Row && item.Anchor.Col == address.Col);
            if (textBox is not null)
                return new AltTextObjectTarget(AltTextObjectKind.TextBox, textBox.Id, textBox.Anchor, textBox.AltText);
        }

        return null;
    }
}
