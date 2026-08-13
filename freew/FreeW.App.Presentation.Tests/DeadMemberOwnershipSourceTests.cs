namespace FreeW.App.Presentation.Tests;

public sealed class DeadMemberOwnershipSourceTests
{
    [Fact]
    public void Declaration_only_core_presentation_and_renderer_members_stay_retired()
    {
        AssertMissing("freew", "FreeW.Core.IO", "DocxWriter.cs",
            "WordArtWarpFromToken(", "BuildPartLocalPreservedDrawingRels(");
        AssertMissing("freew", "FreeW.Core.Model", "MailMerge.cs", "SplitFirstToken(");
        AssertMissing("freew", "FreeW.App.Presentation", "DocumentView", "PageBorderArtVisualPlanner.cs", "AddWhitePolygon(");
        AssertMissing("freew", "FreeW.Core.Model", "CrossReferences.cs", "ParagraphTextAt(");
        AssertMissing("freew", "FreeW.App.Presentation", "Dialogs", "PageSetupDialogPlanner.cs", "FormatCompactPoints(");
        AssertMissing("freew", "FreeW.Core.Model", "DocumentTableStyle.cs", "ResolveBodyFill(");
        AssertMissing("freew", "FreeW.App.Host", "Editing", "CrossPageSelection.cs", "GetSelectedXaml(");
        AssertMissing("freew", "FreeW.Core.Model", "BuiltInStyles.cs", "IsCharacterStyle(");
        AssertMissing("freew", "FreeW.Core.Model", "AutoCorrectEngine.cs", "public static bool TryReplace(");
        AssertMissing("freew", "FreeW.App.Presentation", "DocumentView", "ContentControlInteractionPlanner.cs", "BuildChromePlan(");
        AssertMissing("freew", "FreeW.App.Presentation", "Ribbon", "PageLayoutCommandPlanner.cs", "CountHyphenationCandidates(");
        AssertMissing("freew", "FreeW.Core.Model", "Groups.cs", "TryGetGroup(");
        AssertMissing("freew", "FreeW.App.Host", "Editing", "WpfRgbColorAdapter.cs", "public static Color ParseDrawingMl(");
    }

    [Fact]
    public void Superseded_mail_merge_catalog_getters_stay_retired()
    {
        AssertMissing("freew", "FreeW.App.Presentation", "Ribbon", "MailMergeEmailDeliveryPlanner.cs",
            "GetOutputFormats(", "GetBodyFormats(", "GetRecordScopes(");
        AssertMissing("freew", "FreeW.App.Presentation", "Ribbon", "MailMergeMatchFieldsDialogPlanner.cs",
            "public static IReadOnlyList<FieldRole> GetRoles(");
        AssertMissing("freew", "FreeW.App.Presentation", "Ribbon", "MailMergeFinishPlanner.cs",
            "GetDestinationChoices(");
    }

    private static void AssertMissing(params string[] pathAndMembers)
    {
        var firstMemberIndex = Array.FindIndex(pathAndMembers, part => part.EndsWith('('));
        var source = TestWorkspaceFileLocator.ReadAllText(pathAndMembers[..firstMemberIndex]);
        foreach (var member in pathAndMembers[firstMemberIndex..])
            source.Should().NotContain(member);
    }
}
