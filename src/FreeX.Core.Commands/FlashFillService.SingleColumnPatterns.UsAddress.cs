namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private static readonly Dictionary<string, string> UsStateNameAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "AL",
        ["Alaska"] = "AK",
        ["Arizona"] = "AZ",
        ["Arkansas"] = "AR",
        ["California"] = "CA",
        ["Colorado"] = "CO",
        ["Connecticut"] = "CT",
        ["Delaware"] = "DE",
        ["Florida"] = "FL",
        ["Georgia"] = "GA",
        ["Hawaii"] = "HI",
        ["Idaho"] = "ID",
        ["Illinois"] = "IL",
        ["Indiana"] = "IN",
        ["Iowa"] = "IA",
        ["Kansas"] = "KS",
        ["Kentucky"] = "KY",
        ["Louisiana"] = "LA",
        ["Maine"] = "ME",
        ["Maryland"] = "MD",
        ["Massachusetts"] = "MA",
        ["Michigan"] = "MI",
        ["Minnesota"] = "MN",
        ["Mississippi"] = "MS",
        ["Missouri"] = "MO",
        ["Montana"] = "MT",
        ["Nebraska"] = "NE",
        ["Nevada"] = "NV",
        ["New Hampshire"] = "NH",
        ["New Jersey"] = "NJ",
        ["New Mexico"] = "NM",
        ["New York"] = "NY",
        ["North Carolina"] = "NC",
        ["North Dakota"] = "ND",
        ["Ohio"] = "OH",
        ["Oklahoma"] = "OK",
        ["Oregon"] = "OR",
        ["Pennsylvania"] = "PA",
        ["Rhode Island"] = "RI",
        ["South Carolina"] = "SC",
        ["South Dakota"] = "SD",
        ["Tennessee"] = "TN",
        ["Texas"] = "TX",
        ["Utah"] = "UT",
        ["Vermont"] = "VT",
        ["Virginia"] = "VA",
        ["Washington"] = "WA",
        ["West Virginia"] = "WV",
        ["Wisconsin"] = "WI",
        ["Wyoming"] = "WY"
    };

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

        if (!TryParseUsStateZipSegment(segments[2], out var state, out var zip))
        {
            return false;
        }

        parts = new UsAddressParts(segments[0], segments[1], state, zip);
        return true;
    }

    private static bool TryParseUsStateZipSegment(string segment, out string state, out string zip)
    {
        state = string.Empty;
        zip = string.Empty;

        var tokens = segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var zipCandidate = tokens[^1];
        if (!IsUsZipCode(zipCandidate))
            return false;

        var stateCandidate = string.Join(' ', tokens[..^1]);
        if (!TryNormalizeUsState(stateCandidate, out state))
            return false;

        zip = zipCandidate;
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
            UsAddressComponentKind.StreetWithoutUnit when TrySplitUsStreetUnit(parts.Street, out var streetWithoutUnit, out _, out _) => streetWithoutUnit,
            UsAddressComponentKind.UnitSuffix when TrySplitUsStreetUnit(parts.Street, out _, out var unitSuffix, out _) => unitSuffix,
            UsAddressComponentKind.UnitIdentifier when TrySplitUsStreetUnit(parts.Street, out _, out _, out var unitIdentifier) => unitIdentifier,
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

    private static bool TrySplitUsStreetUnit(
        string street,
        out string streetWithoutUnit,
        out string unitSuffix,
        out string unitIdentifier)
    {
        streetWithoutUnit = string.Empty;
        unitSuffix = string.Empty;
        unitIdentifier = string.Empty;

        var end = street.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(street[end]))
            end--;

        if (end < 0)
            return false;

        var identifierStart = end;
        while (identifierStart >= 0 && !char.IsWhiteSpace(street[identifierStart]))
            identifierStart--;

        var identifierTokenStart = identifierStart + 1;
        var identifierToken = street[identifierTokenStart..(end + 1)];
        if (identifierToken.Length > 1 &&
            identifierToken[0] == '#')
        {
            var prefixEnd = identifierTokenStart - 1;
            while (prefixEnd >= 0 && char.IsWhiteSpace(street[prefixEnd]))
                prefixEnd--;

            if (prefixEnd < 0)
                return false;

            var hashDesignatorEnd = prefixEnd;
            var hashDesignatorStart = hashDesignatorEnd;
            while (hashDesignatorStart >= 0 && !char.IsWhiteSpace(street[hashDesignatorStart]))
                hashDesignatorStart--;

            var hashDesignatorTokenStart = hashDesignatorStart + 1;
            var hashDesignator = street[hashDesignatorTokenStart..(hashDesignatorEnd + 1)];
            if (IsUsStreetUnitDesignator(hashDesignator))
            {
                var hashStreetEnd = hashDesignatorTokenStart - 1;
                while (hashStreetEnd >= 0 && char.IsWhiteSpace(street[hashStreetEnd]))
                    hashStreetEnd--;

                if (hashStreetEnd < 0)
                    return false;

                streetWithoutUnit = street[..(hashStreetEnd + 1)].Trim();
                unitSuffix = street[hashDesignatorTokenStart..(end + 1)].Trim();
                unitIdentifier = identifierToken[1..];
                return streetWithoutUnit.Length > 0 && unitSuffix.Length > 0 && unitIdentifier.Length > 0;
            }

            var unitStart = identifierTokenStart;
            streetWithoutUnit = street[..(prefixEnd + 1)].Trim();
            unitSuffix = street[unitStart..(end + 1)].Trim();
            unitIdentifier = identifierToken[1..];
            return streetWithoutUnit.Length > 0 && unitIdentifier.Length > 0;
        }

        var designatorEnd = identifierStart;
        while (designatorEnd >= 0 && char.IsWhiteSpace(street[designatorEnd]))
            designatorEnd--;

        if (designatorEnd < 0)
            return false;

        var designatorStart = designatorEnd;
        while (designatorStart >= 0 && !char.IsWhiteSpace(street[designatorStart]))
            designatorStart--;

        var designatorTokenStart = designatorStart + 1;
        var designator = street[designatorTokenStart..(designatorEnd + 1)];
        if (!IsUsStreetUnitDesignator(designator))
            return false;

        var streetEnd = designatorTokenStart - 1;
        while (streetEnd >= 0 && char.IsWhiteSpace(street[streetEnd]))
            streetEnd--;

        if (streetEnd < 0)
            return false;

        streetWithoutUnit = street[..(streetEnd + 1)].Trim();
        unitSuffix = street[designatorTokenStart..(end + 1)].Trim();
        unitIdentifier = identifierToken.Trim();
        return streetWithoutUnit.Length > 0 && unitSuffix.Length > 0 && unitIdentifier.Length > 0;
    }

    private static bool IsUsStreetUnitDesignator(string value)
    {
        var normalized = value.TrimEnd('.');
        return normalized.Equals("Apt", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Apartment", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Unit", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Suite", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Ste", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("No", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsStateAbbreviation(string value) =>
        value.Length == 2 && value.All(char.IsLetter);

    private static bool TryNormalizeUsState(string value, out string state)
    {
        state = string.Empty;

        if (IsUsStateAbbreviation(value))
        {
            state = value;
            return true;
        }

        if (!UsStateNameAbbreviations.TryGetValue(value, out var abbreviation))
            return false;

        state = abbreviation;
        return true;
    }

    private static bool IsUsZipCode(string value) =>
        IsFiveDigitZipCode(value) ||
        value.Length == 10 &&
        value[5] == '-' &&
        IsFiveDigitZipCode(value[..5]) &&
        value[6..].All(char.IsDigit);

    private static bool IsFiveDigitZipCode(string value) =>
        value.Length == 5 && value.All(char.IsDigit);
}
