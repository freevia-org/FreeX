using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class FindReplaceDialogSchemaTests
{
    [Fact]
    public void ChoiceSchema_PreservesControlIndexContract()
    {
        FindReplaceDialogSchema.WithinChoices.Select(choice => choice.Value)
            .Should().Equal(FindWithin.Sheet, FindWithin.Workbook);
        FindReplaceDialogSchema.SearchChoices.Select(choice => choice.Value)
            .Should().Equal(FindSearchOrder.ByRows, FindSearchOrder.ByColumns);
        FindReplaceDialogSchema.LookInChoices.Select(choice => choice.Value)
            .Should().Equal(FindLookIn.Formulas, FindLookIn.Values, FindLookIn.Notes, FindLookIn.Comments);
    }

    [Fact]
    public void EverySemanticTextId_HasAPortableDescriptor()
    {
        foreach (var text in Enum.GetValues<FindReplaceDialogText>())
            FindReplaceDialogSchema.Describe(text).Should().NotBeNull();
    }

    [Fact]
    public void Resolve_CanPreserveOrStripRendererAccessKeys()
    {
        static string Get(string _) => "_Find";
        static string Format(string _, object?[] __) => "_Find";

        FindReplaceDialogSchema.Resolve(FindReplaceDialogText.Find, Get, Format)
            .Should().Be("_Find");
        FindReplaceDialogSchema.Resolve(
                FindReplaceDialogText.Find,
                Get,
                Format,
                stripAccessKeys: true)
            .Should().Be("Find");
    }

    [Fact]
    public void ResolvePolicyText_UsesFreeXResourceDescriptors()
    {
        var values = new Dictionary<string, string>
        {
            ["FindReplace_FindWhatRequired"] = "localized required",
            ["FindReplace_NoMatchesFound"] = "localized missing",
            ["FindReplace_NoReplaceableMatchFound"] = "localized no replacement",
            ["FindReplace_MatchStatus"] = "localized match {0}/{1}",
            ["FindReplace_ReplacedCellsStatus"] = "localized replaced {0}",
        };

        var text = FindReplaceDialogSchema.ResolvePolicyText(key => values[key]);

        text.SearchTermRequired.Should().Be("localized required");
        text.NoMatches.Should().Be("localized missing");
        text.NoReplacements.Should().Be("localized no replacement");
        text.NotFoundFormat.Should().Be("localized missing");
        text.MatchFormat.Should().Be("localized match {0}/{1}");
        text.ReplacedOccurrencesFormat.Should().Be("localized replaced {0}");
        text.ReplacementsMadeFormat.Should().Be("localized replaced {0}");
    }

    [Fact]
    public void Renderers_ConsumeSharedSchemaInsteadOfOwningResourceKeysAndChoiceLists()
    {
        var wpfRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var avaloniaRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Avalonia");
        var wpfXaml = File.ReadAllText(Path.Combine(wpfRoot, "FindReplaceDialog.xaml"));
        var wpf = File.ReadAllText(Path.Combine(wpfRoot, "FindReplaceDialog.xaml.cs"));
        var avalonia = File.ReadAllText(Path.Combine(avaloniaRoot, "MainWindow.cs"));

        wpfXaml.Should().NotContain("FindReplace_");
        wpf.Should().Contain("ApplySharedDialogSchema();");
        wpf.Should().Contain("FindReplaceDialogSchema.WithinChoices");
        avalonia.Should().Contain("FindReplaceDialogSchema.WithinChoices");
        avalonia.Should().Contain("FindReplaceDialogSchema.SearchChoices");
        avalonia.Should().Contain("FindReplaceDialogSchema.LookInChoices");
        avalonia.Should().NotContain("[\"Sheet\", \"Workbook\"]");
        avalonia.Should().NotContain("[\"By Rows\", \"By Columns\"]");
        avalonia.Should().NotContain("[\"Formulas\", \"Values\", \"Notes\", \"Comments\"]");
        avalonia.Should().NotContain("UiText.Get(\"MainLoc_Find");
        avalonia.Should().NotContain("UiText.Format(\"MainLoc_Found");
    }
}
