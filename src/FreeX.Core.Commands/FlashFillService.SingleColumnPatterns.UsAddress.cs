namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private static Func<string, string?>? TryUsAddressComponentExtraction(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        UsAddressComponentKind? componentKind = null;

        foreach (var (source, expected) in examples)
        {
            if (!TryParseUsAddress(source, out var parts) ||
                !TryFindUsAddressComponent(parts, expected, out var currentKind))
            {
                return null;
            }

            if (componentKind is null)
                componentKind = currentKind;
            else if (componentKind.Value != currentKind)
                return null;
        }

        if (componentKind is null)
            return null;

        var kind = componentKind.Value;
        return source =>
            TryParseUsAddress(source, out var parts) &&
            TryGetUsAddressComponent(parts, kind, out var component)
                ? component
                : null;
    }

    private static bool TryParseUsAddress(string source, out UsAddressParts parts)
    {
        parts = default;
        var segments = source.Split(',', StringSplitOptions.TrimEntries);
        if (segments.Length != 3 ||
            segments[0].Length == 0 ||
            segments[1].Length == 0 ||
            segments[2].Length == 0)
        {
            return false;
        }

        var stateZipTokens = segments[2].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (stateZipTokens.Length != 2 ||
            !IsUsStateAbbreviation(stateZipTokens[0]) ||
            !IsUsZipCode(stateZipTokens[1]))
        {
            return false;
        }

        parts = new UsAddressParts(segments[0], segments[1], stateZipTokens[0], stateZipTokens[1]);
        return true;
    }

    private static bool TryFindUsAddressComponent(
        UsAddressParts parts,
        string expected,
        out UsAddressComponentKind componentKind)
    {
        componentKind = default;
        var matched = false;

        foreach (var candidateKind in UsAddressComponentKinds)
        {
            if (!TryGetUsAddressComponent(parts, candidateKind, out var component) ||
                component != expected)
            {
                continue;
            }

            if (matched)
                return false;

            componentKind = candidateKind;
            matched = true;
        }

        return matched;
    }

    private static bool TryGetUsAddressComponent(
        UsAddressParts parts,
        UsAddressComponentKind kind,
        out string component)
    {
        component = kind switch
        {
            UsAddressComponentKind.Street => parts.Street,
            UsAddressComponentKind.StreetNumber when TrySplitUsStreetNumber(parts.Street, out var number, out _) => number,
            UsAddressComponentKind.StreetName when TrySplitUsStreetNumber(parts.Street, out _, out var name) => name,
            UsAddressComponentKind.City => parts.City,
            UsAddressComponentKind.State => parts.State,
            UsAddressComponentKind.Zip5 => parts.Zip[..5],
            UsAddressComponentKind.Zip when parts.Zip.Contains('-', StringComparison.Ordinal) => parts.Zip,
            UsAddressComponentKind.Zip4 when parts.Zip.Contains('-', StringComparison.Ordinal) => parts.Zip[6..],
            UsAddressComponentKind.StateZip => parts.State + " " + parts.Zip,
            _ => string.Empty
        };

        return component.Length > 0;
    }

    private static bool TrySplitUsStreetNumber(string street, out string number, out string name)
    {
        number = string.Empty;
        name = string.Empty;

        var digitEnd = 0;
        while (digitEnd < street.Length && char.IsDigit(street[digitEnd]))
            digitEnd++;

        if (digitEnd == 0 ||
            digitEnd >= street.Length ||
            !char.IsWhiteSpace(street[digitEnd]))
        {
            return false;
        }

        var nameStart = digitEnd + 1;
        while (nameStart < street.Length && char.IsWhiteSpace(street[nameStart]))
            nameStart++;

        if (nameStart >= street.Length)
            return false;

        number = street[..digitEnd];
        name = street[nameStart..];
        return true;
    }

    private static bool IsUsStateAbbreviation(string value) =>
        value.Length == 2 && value.All(char.IsLetter);

    private static bool IsUsZipCode(string value) =>
        IsFiveDigitZipCode(value) ||
        value.Length == 10 &&
        value[5] == '-' &&
        IsFiveDigitZipCode(value[..5]) &&
        value[6..].All(char.IsDigit);

    private static bool IsFiveDigitZipCode(string value) =>
        value.Length == 5 && value.All(char.IsDigit);
}
