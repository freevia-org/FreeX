using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RibbonDialogAutomationOwnershipSourceTests
{
    [Fact]
    public void Ribbon_hosts_consume_shared_typed_numeric_parsers()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var profile = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonEditorExecutionProfile.cs");
        var floatingObjectFactory = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonFloatingObjectCommandFactory.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("FreeWRibbonNumericValueParser.TryParseTypedFontSize(");
            source.Should().NotContain("FreeWRibbonNumericValueParser.TryParseObjectPosition(");
            source.Should().NotContain("FreeWRibbonNumericValueParser.TryParseObjectSize(");
            source.Should().NotContain("FreeWRibbonNumericValueParser.TryParseChartSize(");
            source.Should().NotContain("private static bool TryParsePosition(");
            source.Should().NotContain("private static bool TryParseSize(");
            source.Should().NotContain("private static bool TryParseChartSize(");
        }

        floatingObjectFactory.Should().Contain("FreeWRibbonNumericValueParser.TryParseObjectPosition(");
        floatingObjectFactory.Should().Contain("FreeWRibbonNumericValueParser.TryParseObjectSize(");
        profile.Should().Contain("FreeWRibbonNumericValueParser.TryParseChartSize(");

        // r604: these two lines used to assert that the WPF host parses with CurrentCulture and the
        // Avalonia host with InvariantCulture. That pinned a defect rather than a decision -- it
        // arrived in a refactor that shared the parsers, recording what the code did.
        //
        // What it froze: BOTH hosts DISPLAY the value through FormatInvariant, so the font-size box
        // always shows "10.5". WPF then re-read that text under the user's culture with
        // AllowThousands, and on a de-DE machine "10.5" parses as 105 -- opening the box and
        // pressing Enter without editing anything multiplied the font size by ten. Avalonia's
        // invariant parse had the opposite failure: a user typing "10,5" was silently ignored.
        //
        // The rule now is that neither host chooses. They call the shared typed-entry parser, which
        // reads the current culture first and falls back to the invariant spelling, with
        // NumberStyles.Float so a group separator cannot turn 10.5 into 105.
        //
        // The rule itself is enforced by R604_BothHostsReadTypedNumbersTheSameWayTests, which looks
        // at TryParse CALLS specifically. A blanket "these files must not mention CultureInfo" was
        // the first attempt and it was wrong: both hosts legitimately pass CurrentCulture to
        // string.Format for user-facing text, which is exactly where the current culture belongs.
        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().NotContain(
                "TryParseFontSize(",
                "the culture-taking overload is not for typed input -- use TryParseTypedFontSize");
        }
    }

    [Fact]
    public void Dialog_and_backstage_hosts_do_not_redeclare_planned_automation_ids()
    {
        var dialogSources = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "CellShadingDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "CellShadingDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "PasswordPromptDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "PasswordPromptDialog.cs"),
            ReadSource("freew", "FreeW.App.Host", "ParagraphBreaksDialog.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "ParagraphDialog.cs"),
        };
        var backstageSources = new[]
        {
            ReadSource("freew", "FreeW.App.Host", "Backstage", "BackstageView.cs"),
            ReadSource("freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs"),
        };

        foreach (var source in dialogSources)
        {
            source.Should().NotContain("\"CellShadingNoColorButton\"");
            source.Should().NotContain("$\"CellShadingSwatch{index}\"");
            source.Should().NotContain("\"PasswordPromptDialog\"");
            source.Should().NotContain("\"PasswordPromptPasswordBox\"");
            source.Should().NotContain("\"PasswordPromptOkButton\"");
            source.Should().NotContain("\"PasswordPromptCancelButton\"");
            source.Should().NotContain("\"paragraph-left-indent\"");
        }

        foreach (var source in backstageSources)
        {
            source.Should().Contain("BackstagePaneSurfacePlanner.WindowAutomationId");
            source.Should().Contain("surface.Search.AutomationId");
            source.Should().Contain("inline.FileNameAutomationId");
            source.Should().Contain("inline.FileTypeAutomationId");
            source.Should().NotContain("\"FreeWBackstageWindow\"");
            source.Should().NotContain("\"OpenSearchBox\"");
            source.Should().NotContain("\"SaveAsSuggestedFileName\"");
            source.Should().NotContain("\"SaveAsSelectedExtension\"");
        }
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }
                .Concat(parts)
                .ToArray()));
}
