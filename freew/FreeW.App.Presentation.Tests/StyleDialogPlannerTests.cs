using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class StyleDialogPlannerTests
{
    [Fact]
    public void LayoutMetrics_KeepCompactStyleEditorsOnTheSharedContract()
    {
        StyleDialogPlanner.Text.NewTitle.Should().Be("New Style");
        StyleDialogPlanner.Text.ModifyTitlePrefix.Should().Be("Modify Style —");
        StyleDialogPlanner.Text.ManageTitle.Should().Be("Manage Styles");
        StyleDialogMetrics.DialogMargin.Should().Be(16);
        StyleDialogMetrics.FieldBottomMargin.Should().Be(10);
        StyleDialogMetrics.NameTextBoxHeight.Should().Be(20);
        StyleDialogMetrics.ActionRowTopMargin.Should().Be(12);
    }

    [Fact]
    public void Surface_OrdersDefinitionAndManageControlsForEitherRenderer()
    {
        var surface = StyleDialogPlanner.Surface;

        surface.ActionButtonWidth.Should().Be(72);
        surface.Fields.Select(field => field.Kind).Should().Equal(Enum.GetValues<StyleDialogFieldKind>());
        surface.Effects.Select(effect => effect.Kind).Should().Equal(Enum.GetValues<StyleDialogEffectKind>());
        surface.Fields.Select(field => field.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Effects.Select(effect => effect.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Field(StyleDialogFieldKind.Name).MinWidth.Should().Be(280);

        surface.Manage.Title.Should().Be(StyleDialogPlanner.Text.ManageTitle);
        surface.Manage.Field(ManageStyleFieldKind.Styles).MinHeight.Should().Be(220);
        surface.Manage.Actions.Select(action => action.Kind).Should().Equal(Enum.GetValues<ManageStyleCommandKind>());
        surface.Manage.Actions.Select(action => action.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Manage.Action(ManageStyleCommandKind.Apply).IsDefault.Should().BeTrue();
        surface.Manage.Action(ManageStyleCommandKind.Close).IsCancel.Should().BeTrue();
        surface.Manage.Action(ManageStyleCommandKind.Delete).ActionKind.Should().Be(ManageStyleActionKind.Delete);
        surface.Manage.Action(ManageStyleCommandKind.Close).ActionKind.Should().BeNull();
    }

    [Fact]
    public void CaptureControlState_MapsTypedFormattingEffects()
    {
        var state = StyleDialogPlanner.CaptureControlState(
            "Callout",
            basedOnIndex: 1,
            nextStyleIndex: 2,
            fontSizeIndex: 3,
            colorIndex: 4,
            alignmentIndex: 1,
            kind => kind is StyleDialogEffectKind.Bold or StyleDialogEffectKind.Underline);

        state.Name.Should().Be("Callout");
        state.Bold.Should().BeTrue();
        state.Italic.Should().BeFalse();
        state.Underline.Should().BeTrue();
        state.AlignmentIndex.Should().Be(1);
    }

    [Fact]
    public void TryBuildDefinition_TrimsName_AndMapsFormattingChoices()
    {
        var input = new StyleDialogInput(
            "  Callout  ",
            "Normal",
            "Heading1",
            Bold: true,
            Italic: true,
            Underline: false,
            FontSizeIndex: 7,
            ColorIndex: 4,
            AlignmentIndex: 1);

        StyleDialogPlanner.TryBuildDefinition(
                input,
                RunFormatting.Default,
                ParagraphFormatting.Default,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result!.Name.Should().Be("Callout");
        result.BasedOnId.Should().Be("Normal");
        result.NextStyleId.Should().Be("Heading1");
        result.Run.Bold.Should().BeTrue();
        result.Run.Italic.Should().BeTrue();
        result.Run.FontSizePt.Should().Be(16);
        result.Run.ColorHex.Should().Be("#2F5496");
        result.Paragraph.Alignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void TryBuildDefinition_RejectsEmptyName()
    {
        StyleDialogPlanner.TryBuildDefinition(
                new StyleDialogInput("   ", null, null, false, false, false, 0, 0, 0),
                RunFormatting.Default,
                ParagraphFormatting.Default,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(StyleDialogValidationError.EmptyName);
    }

    [Fact]
    public void BuildRows_ByType_OrdersBuiltInsBeforeCustomStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc,
            "Callout",
            null,
            RunFormatting.Default,
            ParagraphFormatting.Default);

        var rows = StyleDialogPlanner.BuildRows(doc, StyleDialogSortOrder.ByType);
        var firstCustomIndex = rows.ToList().FindIndex(row => !row.IsBuiltIn);

        rows.TakeWhile(row => row.IsBuiltIn).Should().NotBeEmpty();
        rows.First(row => row.Id == custom.Id).IsBuiltIn.Should().BeFalse();
        rows.Skip(firstCustomIndex).Should().OnlyContain(row => !row.IsBuiltIn);
    }

    [Fact]
    public void BuildRows_ByUse_OrdersMostUsedStylesFirst()
    {
        var doc = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            doc,
            "Frequently Used",
            null,
            RunFormatting.Default,
            ParagraphFormatting.Default);
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("one") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("two") { StyleId = custom.Id });
        doc.Blocks.Add(new Paragraph("three") { StyleId = custom.Id });

        var rows = StyleDialogPlanner.BuildRows(doc, StyleDialogSortOrder.ByUse);

        rows.First().Id.Should().Be(custom.Id);
    }

    [Fact]
    public void NewSession_ProjectsSortedOptionsAndDefaultStyle()
    {
        var names = new Dictionary<string, string>
        {
            ["Heading1"] = "Heading 1",
            ["Normal"] = "Normal",
        };

        var session = StyleDialogPlanner.CreateNewSession(names, "Heading1");

        session.InitialState.Title.Should().Be("New Style");
        session.InitialState.Name.Should().BeEmpty();
        session.InitialState.NameIsReadOnly.Should().BeFalse();
        session.InitialState.InitialFocus.Should().Be(StyleDialogFocusTarget.Name);
        session.ValidationTitle.Should().Be("New Style");
        session.InitialState.BasedOnOptions.Select(option => option.Key)
            .Should().Equal("(none)", "Heading 1", "Normal");
        session.InitialState.BasedOnIndex.Should().Be(1);
        session.InitialState.NextStyleIndex.Should().Be(0);
    }

    [Fact]
    public void ModifySession_ProjectsExistingStyleAndMapsAcceptance()
    {
        var names = new Dictionary<string, string>
        {
            ["Heading1"] = "Heading 1",
            ["Normal"] = "Normal",
        };
        var existing = new DocumentStyle
        {
            Id = "Callout",
            Name = "Callout",
            BasedOnStyleId = "Normal",
            NextStyleId = "Heading1",
            Run = RunFormatting.Default with { Bold = true, FontSizePt = 14, ColorHex = "#FF0000" },
            Paragraph = ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
        };
        var session = StyleDialogPlanner.CreateModifySession(names, existing);

        session.InitialState.Name.Should().Be("Callout");
        session.InitialState.Title.Should().Be("Modify Style — Callout");
        session.InitialState.NameIsReadOnly.Should().BeTrue();
        session.InitialState.InitialFocus.Should().Be(StyleDialogFocusTarget.BasedOn);
        session.ValidationTitle.Should().Be("Modify Style — Callout");
        session.InitialState.BasedOnOptions[session.InitialState.BasedOnIndex].Value.Should().Be("Normal");
        session.InitialState.NextStyleOptions[session.InitialState.NextStyleIndex].Value.Should().Be("Heading1");

        var acceptance = session.PlanAcceptance(new StyleDialogControlState(
            session.InitialState.Name,
            session.InitialState.BasedOnIndex,
            session.InitialState.NextStyleIndex,
            session.InitialState.Bold,
            session.InitialState.Italic,
            session.InitialState.Underline,
            session.InitialState.FontSizeIndex,
            session.InitialState.ColorIndex,
            session.InitialState.AlignmentIndex));

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Result!.BasedOnId.Should().Be("Normal");
        acceptance.Result.NextStyleId.Should().Be("Heading1");
        acceptance.Result.Run.Bold.Should().BeTrue();
        acceptance.Result.Paragraph.Alignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void Session_OwnsNameValidationMessage()
    {
        var session = StyleDialogPlanner.CreateNewSession(
            new Dictionary<string, string>(),
            defaultBasedOnId: null);
        var state = session.InitialState;

        var acceptance = session.PlanAcceptance(new StyleDialogControlState(
            "   ",
            state.BasedOnIndex,
            state.NextStyleIndex,
            state.Bold,
            state.Italic,
            state.Underline,
            state.FontSizeIndex,
            state.ColorIndex,
            state.AlignmentIndex));

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.ErrorMessage.Should().Be("Please enter a style name.");
        acceptance.FocusField.Should().Be(StyleDialogField.Name);
    }

    [Fact]
    public void ManageSession_PreservesSelectionAcrossSortsAndProjectsButtons()
    {
        var document = TextDocument.CreateEmpty();
        var custom = StyleManager.CreateStyle(
            document,
            "Callout",
            null,
            RunFormatting.Default,
            ParagraphFormatting.Default);
        var session = StyleDialogPlanner.CreateManageStylesSession(document, custom.Id);

        session.State.SelectedRow!.Id.Should().Be(custom.Id);
        session.State.SortIndex.Should().Be(0);
        session.State.Buttons.Should().Be(new ManageStyleButtonState(true, true, true));
        session.State.Buttons.IsEnabled(ManageStyleCommandKind.Close).Should().BeTrue();

        var sorted = session.PlanSort(1);

        sorted.SortOrder.Should().Be(StyleDialogSortOrder.ByType);
        sorted.SortIndex.Should().Be(1);
        sorted.SelectedRow!.Id.Should().Be(custom.Id);
        session.PlanAction(ManageStyleActionKind.Delete, sorted.SelectedIndex)
            .Should().Be(new ManageStyleAction.Delete(custom.Id));
    }

    [Fact]
    public void ManageSession_RejectsDeleteForBuiltInStyle()
    {
        var document = TextDocument.CreateEmpty();
        var session = StyleDialogPlanner.CreateManageStylesSession(document, "Normal");

        session.State.SelectedRow!.IsBuiltIn.Should().BeTrue();
        session.State.Buttons.DeleteEnabled.Should().BeFalse();
        session.PlanAction(ManageStyleActionKind.Delete, session.State.SelectedIndex).Should().BeNull();
        session.PlanAction(ManageStyleActionKind.Apply, session.State.SelectedIndex)
            .Should().Be(new ManageStyleAction.Apply("Normal"));
    }

    [Theory]
    [InlineData(-1, StyleDialogSortOrder.Alphabetical)]
    [InlineData(0, StyleDialogSortOrder.Alphabetical)]
    [InlineData(1, StyleDialogSortOrder.ByType)]
    [InlineData(2, StyleDialogSortOrder.ByUse)]
    [InlineData(9, StyleDialogSortOrder.Alphabetical)]
    public void SortOrderForIndex_NormalizesRendererSelection(
        int selectedIndex,
        StyleDialogSortOrder expected)
    {
        StyleDialogPlanner.SortOrderForIndex(selectedIndex).Should().Be(expected);
    }
}
