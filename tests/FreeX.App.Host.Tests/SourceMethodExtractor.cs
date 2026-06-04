using FluentAssertions;

namespace FreeX.App.Host.Tests;

internal static class SourceMethodExtractor
{
    public static string ExtractMethodSource(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        signatureIndex.Should().BeGreaterThanOrEqualTo(0, $"source should contain {signature}");

        var bodyStart = source.IndexOf('{', signatureIndex);
        bodyStart.Should().BeGreaterThanOrEqualTo(signatureIndex, $"source should contain a body for {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
                return source.Substring(signatureIndex, index - signatureIndex + 1);
        }

        throw new InvalidOperationException($"Could not find the end of {signature}.");
    }
}
