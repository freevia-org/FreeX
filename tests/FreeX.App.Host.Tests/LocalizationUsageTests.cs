using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class LocalizationUsageTests
{
    private static readonly Regex UiTextKeyRegex = new(
        @"UiText\.(?:Get|Format|GetNeutral)\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex XamlLocKeyRegex = new(
        @"\{local:Loc\s+Key=(?<quote>['""]?)(?<key>[A-Za-z0-9_]+)\k<quote>\}",
        RegexOptions.Compiled);

    private static readonly Regex MessageCallRegex = new(
        @"(?s)\b(?<call>ShowOwnedMessage|ShowOpenProgress|ShowSaveProgress)\((?<args>.*?)\);",
        RegexOptions.Compiled);

    private static readonly Regex InlineArgumentStringRegex = new(
        @"(^|,)\s*(?:\$@?|@?\$?)""",
        RegexOptions.Compiled);

    private static readonly Regex RawFailedWorkbookCommandRegex = new(
        @"\bnew\s+FailedWorkbookCommand\(\s*(?:\$@?|@?\$?)""",
        RegexOptions.Compiled);

    private static readonly Regex RawAutomationPropertyStringRegex = new(
        @"(?s)\bAutomationProperties\.Set(?<kind>Name|HelpText)\(\s*[^,]+,\s*(?:\$@?|@?\$?)""",
        RegexOptions.Compiled);

    private static readonly Regex RawMessageServiceStringRegex = new(
        @"(?s)_messageService\.(?<call>ShowInfo|ShowWarning|ShowError|AskYesNo)\(\s*(?:\$@?|@?\$?)""",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedRawXamlAttributeValues = new(StringComparer.Ordinal)
    {
        "A",
        "A1",
        "B",
        "FREE",
        "FreeX",
        "I",
        "S",
        "U",
        "ab",
        "X"
    };

    private static readonly HashSet<string> UserFacingXamlAttributeNames = new(StringComparer.Ordinal)
    {
        "Content",
        "Header",
        "Text",
        "Title",
        "ToolTip",
        "AutomationProperties.Name",
        "AutomationProperties.HelpText",
        "RibbonTooltip.Title",
        "RibbonTooltip.Description"
    };

    [Fact]
    public void AppSourceLocalizationKeys_AllExistInNeutralResources()
    {
        var sourceRoot = DialogSourceTestSupport.FindHostSourceDirectory("UiText.cs");
        var resourceKeys = UiText.GetNeutralResourceKeys();

        var usedKeys = Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FindLocalizationKeys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        usedKeys.Should().NotBeEmpty();
        usedKeys.Should().OnlyContain(key => resourceKeys.Contains(key));
    }

    [Fact]
    public void AppXamlUserFacingText_UsesLocalizationResources()
    {
        var sourceRoot = DialogSourceTestSupport.FindHostSourceDirectory("UiText.cs");
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(FindRawXamlUserFacingText)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void MainWindowMessageAndProgressCalls_DoNotUseInlineUserFacingText()
    {
        var sourceRoot = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var checkedFiles = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path =>
                Path.GetFileName(path).StartsWith("MainWindow", StringComparison.Ordinal) ||
                Path.GetFileName(path) is "OpenWorkbookLoader.cs" or "SaveWorkbookWriter.cs")
            .Order(StringComparer.Ordinal)
            .ToArray();

        var offenders = checkedFiles
            .SelectMany(FindInlineMessageArguments)
            .Concat(checkedFiles.SelectMany(FindRawFailedWorkbookCommandMessages))
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty("message/progress user-facing text should flow through UiText resources");
    }

    [Fact]
    public void AppCodeAutomationMetadata_UsesLocalizationResources()
    {
        var sourceRoot = DialogSourceTestSupport.FindHostSourceDirectory("UiText.cs");
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(FindRawAutomationPropertyText)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty("automation names and help text are user-facing and should flow through UiText resources");
    }

    [Fact]
    public void HomeRibbonMessageServicePrompts_UseLocalizationResources()
    {
        var sourceRoot = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var checkedFiles = new[]
        {
            Path.Combine(sourceRoot, "MainWindow.CellsCommands.cs"),
            Path.Combine(sourceRoot, "MainWindow.HomeEditing.cs")
        };

        var offenders = checkedFiles
            .SelectMany(FindRawMessageServiceText)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty("Home ribbon message-service prompts are user-facing and should flow through UiText resources");
    }

    [Fact]
    public void AllMainWindowMessageServiceCalls_UseLocalizationResources()
    {
        var sourceRoot = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var checkedFiles = Directory
            .EnumerateFiles(sourceRoot, "MainWindow.*.cs", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var offenders = checkedFiles
            .SelectMany(FindRawMessageServiceText)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty("all MainWindow _messageService calls are user-facing and should flow through UiText resources");
    }

    private static IEnumerable<string> FindLocalizationKeys(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in UiTextKeyRegex.Matches(source))
            yield return match.Groups["key"].Value;

        foreach (Match match in XamlLocKeyRegex.Matches(source))
            yield return match.Groups["key"].Value;
    }

    private static IEnumerable<string> FindRawXamlUserFacingText(string path)
    {
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!UserFacingXamlAttributeNames.Contains(attribute.Name.LocalName) ||
                    !IsRawUserFacingXamlValue(attribute.Value))
                {
                    continue;
                }

                yield return $"{Path.GetFileName(path)}:{GetLine(attribute)} {attribute.Name.LocalName}=\"{attribute.Value}\"";
            }

            if (element.Name.LocalName == "TextBlock" &&
                element.Attribute("Text") is null &&
                IsRawUserFacingXamlValue(element.Value.Trim()))
            {
                yield return $"{Path.GetFileName(path)}:{GetLine(element)} TextBlock=\"{element.Value.Trim()}\"";
            }
        }
    }

    private static bool IsRawUserFacingXamlValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            AllowedRawXamlAttributeValues.Contains(value) ||
            value.Contains("{local:Loc", StringComparison.Ordinal) ||
            value.Contains("{Binding", StringComparison.Ordinal) ||
            value.Contains("{StaticResource", StringComparison.Ordinal) ||
            value.Contains("{DynamicResource", StringComparison.Ordinal) ||
            value.Contains("{TemplateBinding", StringComparison.Ordinal) ||
            value.Contains("{x:Static", StringComparison.Ordinal))
        {
            return false;
        }

        return value.Any(char.IsLetter);
    }

    private static IEnumerable<string> FindInlineMessageArguments(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in MessageCallRegex.Matches(source))
        {
            if (!InlineArgumentStringRegex.IsMatch(match.Groups["args"].Value))
                continue;

            yield return $"{Path.GetFileName(path)}:{LineNumber(source, match.Index)} {match.Groups["call"].Value}";
        }
    }

    private static IEnumerable<string> FindRawFailedWorkbookCommandMessages(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in RawFailedWorkbookCommandRegex.Matches(source))
            yield return $"{Path.GetFileName(path)}:{LineNumber(source, match.Index)} FailedWorkbookCommand";
    }

    private static IEnumerable<string> FindRawAutomationPropertyText(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in RawAutomationPropertyStringRegex.Matches(source))
            yield return $"{Path.GetFileName(path)}:{LineNumber(source, match.Index)} AutomationProperties.Set{match.Groups["kind"].Value}";
    }

    private static IEnumerable<string> FindRawMessageServiceText(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in RawMessageServiceStringRegex.Matches(source))
            yield return $"{Path.GetFileName(path)}:{LineNumber(source, match.Index)} _messageService.{match.Groups["call"].Value}";
    }

    private static int GetLine(XObject node) =>
        node is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
            ? lineInfo.LineNumber
            : 0;

    private static int LineNumber(string source, int index) =>
        source[..index].Count(ch => ch == '\n') + 1;
}
