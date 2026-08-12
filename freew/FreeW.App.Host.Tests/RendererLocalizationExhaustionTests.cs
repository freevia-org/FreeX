using System.IO;
using System.Text.RegularExpressions;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host.Tests;

public sealed class RendererLocalizationExhaustionTests
{
    private static readonly Regex RawSemanticAssignment = new(
        "(?:\\b(?:Text|Content|Header|Title|ToolTip|Watermark|PlaceholderText|Description|Message|Prompt|Label)" +
        "\\s*=\\s*|AutomationProperties\\.Set(?:Name|HelpText)\\([^,\\r\\n]+,\\s*)" +
        "(?:\\$@|@\\$|\\$|@)?\\\"(?<text>(?:\\\\.|[^\\\"\\\\])*)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RawStatusAssignment = new(
        "(?:_status|button)\\.(?:Text|ToolTip)\\s*=\\s*(?<expression>.*?);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex UserMessageCall = new(
        "(?:DialogMessageHelper\\.Show(?:Info|Warning|Error|Message)|" +
        "AvaloniaUserMessageDialog\\.ShowWarningAsync|TextPrompt\\.Ask|\\bShowInfo)" +
        "\\s*\\((?<arguments>.{0,1600}?)\\);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex StringLiteral = new(
        "(?<![A-Za-z0-9_])(?:\\$@|@\\$|\\$|@)?\\\"(?<text>(?:\\\\.|[^\\\"\\\\])*)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> AllowedRendererTokens =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["‹"] = "icon-only previous navigation glyph",
            ["›"] = "icon-only next navigation glyph",
            ["↵"] = "icon-only insert action glyph",
            ["⎘"] = "icon-only copy action glyph",
            ["_"] = "page-preview baseline marker",
            ["▾"] = "icon-only gallery expander glyph",
            ["A"] = "SmartArt preview sample glyph",
            ["Aa"] = "theme preview sample glyph",
            ["Fx"] = "theme preview sample glyph",
            ["1"] = "chart preview sample value",
            ["0.5"] = "culture-parsed default border width seed",
            ["{item.Author} – {kindLabel}"] = "localized parts composed with punctuation only",
            ["\u25c0 {MailMergeDialogMetadata.PreviousLabel}"] = "localized label composed with a navigation glyph",
            ["{MailMergeDialogMetadata.NextLabel} \u25b6"] = "localized label composed with a navigation glyph",
        };

    [Fact]
    public void ShippingRenderers_DoNotAssignRawUserFacingText()
    {
        var failures = new List<string>();
        var seenAllowlist = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, source) in RendererSources())
        {
            foreach (Match match in RawSemanticAssignment.Matches(source))
                InspectLiteral(name, source, match, match.Groups["text"].Value, failures, seenAllowlist);

            foreach (Match assignment in RawStatusAssignment.Matches(source))
            foreach (Match literal in StringLiteral.Matches(assignment.Groups["expression"].Value))
                InspectLiteral(name, source, assignment, literal.Groups["text"].Value, failures, seenAllowlist);

            foreach (Match call in UserMessageCall.Matches(source))
            foreach (Match literal in StringLiteral.Matches(call.Groups["arguments"].Value))
                InspectLiteral(name, source, call, literal.Groups["text"].Value, failures, seenAllowlist);
        }

        failures.Should().BeEmpty(
            "shipping WPF/Avalonia labels, prompts, automation metadata, status text, and user messages " +
            "must resolve through the neutral/shared localization catalogs");
        seenAllowlist.Should().BeEquivalentTo(
            AllowedRendererTokens.Keys,
            "the token allowlist must stay exact so stale exclusions cannot hide new localization debt");
    }

    [Fact]
    public void SharedRendererTextCatalogs_DeclareOnlyResolvableResourceKeys()
    {
        var available = UiText.GetNeutralResourceKeys();
        MailMergeDialogMetadata.RequiredResourceKeys.Should().OnlyContain(key => available.Contains(key));
        MailMergeDialogMetadata.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        InsertDialogTextResources.RequiredResourceKeys.Should().OnlyContain(key => available.Contains(key));
        InsertDialogTextResources.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
    }

    private static void InspectLiteral(
        string name,
        string source,
        Match owner,
        string encoded,
        ICollection<string> failures,
        ISet<string> seenAllowlist)
    {
        var literal = Regex.Unescape(encoded);
        if (AllowedRendererTokens.ContainsKey(literal))
        {
            seenAllowlist.Add(literal);
            return;
        }

        if (string.IsNullOrWhiteSpace(literal) || IsResourceKey(literal) || !LooksUserFacing(literal))
            return;

        var line = source.AsSpan(0, owner.Index).Count('\n') + 1;
        failures.Add($"{name}:{line}: {literal}");
    }

    private static bool IsResourceKey(string value) =>
        value.Contains('_', StringComparison.Ordinal) &&
        value.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool LooksUserFacing(string value) =>
        Regex.IsMatch(value, "[A-Za-z]{2}", RegexOptions.CultureInvariant);

    private static IReadOnlyList<(string Name, string Source)> RendererSources()
    {
        var repository = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var freeW = Path.Combine(repository, "freew");
        var host = Path.Combine(freeW, "FreeW.App.Host");

        return new[] { host, Path.Combine(freeW, "FreeW.App.Avalonia") }
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrSupportPath(path))
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(freeW, path), File.ReadAllText(path)))
            .ToArray();
    }

    private static bool IsGeneratedOrSupportPath(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "TestSupport");
}
