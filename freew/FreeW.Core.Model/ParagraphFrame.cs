namespace FreeW.Core.Model;

/// <summary>
/// The text-frame properties (<c>w:framePr</c>) a paragraph carries: size, anchoring, wrapping and
/// position. Consecutive paragraphs that carry equal frame properties belong to ONE frame in Word, so the
/// renderer joins them into a single box. The attributes are kept verbatim (name, value) and ordered by name,
/// which makes two frames compare equal exactly when Word would merge them and lets the writer emit
/// the element unchanged. Drop caps are modelled separately (<see cref="DropCapLayoutIntent"/>) and never
/// carry a <see cref="ParagraphFrame"/>.
/// </summary>
public sealed record ParagraphFrame
{
    /// <summary>A canonical <c>name=value;name=value</c> form of the attributes (names ordered, no whitespace).</summary>
    public string Signature { get; init; } = string.Empty;

    /// <summary>Builds a frame from raw <c>w:framePr</c> attributes (local name, value). Null when there are none.</summary>
    public static ParagraphFrame? FromAttributes(IEnumerable<KeyValuePair<string, string>> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var ordered = attributes
            .Where(pair => !string.IsNullOrEmpty(pair.Key) && pair.Value is not null)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value.Replace("\\", "\\\\").Replace(";", "\\;"))
            .ToArray();
        return ordered.Length == 0 ? null : new ParagraphFrame { Signature = string.Join(';', ordered) };
    }

    /// <summary>The frame attributes as (local name, value) pairs in name order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes
    {
        get
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var part in SplitEscaped(Signature))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0)
                    continue;
                result.Add(new KeyValuePair<string, string>(
                    part[..separator],
                    part[(separator + 1)..].Replace("\\;", ";").Replace("\\\\", "\\")));
            }

            return result;
        }
    }

    /// <summary>The value of one frame attribute, or null when it is absent.</summary>
    public string? Get(string name)
    {
        foreach (var pair in Attributes)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
                return pair.Value;
        }

        return null;
    }

    private static IEnumerable<string> SplitEscaped(string signature)
    {
        if (string.IsNullOrEmpty(signature))
            yield break;

        var start = 0;
        for (var i = 0; i < signature.Length; i++)
        {
            if (signature[i] == '\\')
            {
                i++;
                continue;
            }

            if (signature[i] == ';')
            {
                yield return signature[start..i];
                start = i + 1;
            }
        }

        yield return signature[start..];
    }
}
