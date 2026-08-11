using FluentAssertions;
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
