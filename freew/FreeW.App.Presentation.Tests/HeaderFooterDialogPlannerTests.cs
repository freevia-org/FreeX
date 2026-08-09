using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterDialogPlannerTests
{
    [Theory]
    [InlineData(HeaderFooterSlotKind.Header, "header", "Default Header")]
    [InlineData(HeaderFooterSlotKind.Footer, "footer", "Default Footer")]
    [InlineData(HeaderFooterSlotKind.EvenHeader, "even-header", "Even-Page Header")]
    [InlineData(HeaderFooterSlotKind.EvenFooter, "even-footer", "Even-Page Footer")]
    [InlineData(HeaderFooterSlotKind.FirstHeader, "first-header", "First-Page Header")]
    [InlineData(HeaderFooterSlotKind.FirstFooter, "first-footer", "First-Page Footer")]
    public void SlotNameAndLabel_CoverAllHeaderFooterSlots(
        HeaderFooterSlotKind slot,
        string expectedName,
        string expectedLabel)
    {
        HeaderFooterDialogPlanner.SlotNameFor(slot).Should().Be(expectedName);
        HeaderFooterDialogPlanner.ParseSlot(expectedName).Should().Be(slot);
        HeaderFooterDialogPlanner.LabelFor(slot).Should().Be(expectedLabel);
    }

    [Theory]
    [InlineData(HeaderFooterSlotKind.Header, false, 0)]
    [InlineData(HeaderFooterSlotKind.Footer, true, 1)]
    [InlineData(HeaderFooterSlotKind.FirstHeader, false, 2)]
    [InlineData(HeaderFooterSlotKind.FirstFooter, true, 3)]
    [InlineData(HeaderFooterSlotKind.EvenHeader, false, 4)]
    [InlineData(HeaderFooterSlotKind.EvenFooter, true, 5)]
    public void SlotIdentity_CoversRendererAndUndoCommandPolicies(
        HeaderFooterSlotKind slot,
        bool isFooter,
        int commandSlotIndex)
    {
        HeaderFooterDialogPlanner.IsFooterSlot(slot).Should().Be(isFooter);
        HeaderFooterDialogPlanner.CommandSlotIndexFor(slot).Should().Be(commandSlotIndex);
    }

    [Fact]
    public void PlanSlotActivation_GuardsEvenAndFirstPageSlots()
    {
        var even = HeaderFooterDialogPlanner.PlanSlotActivation(
            HeaderFooterSlotKind.EvenFooter,
            differentOddEvenPages: false,
            differentFirstPage: true);

        even.Kind.Should().Be(HeaderFooterSlotActivationKind.RequiresDifferentOddEvenPages);
        even.Message.Should().Contain("Different Odd & Even Pages");

        var first = HeaderFooterDialogPlanner.PlanSlotActivation(
            HeaderFooterSlotKind.FirstHeader,
            differentOddEvenPages: true,
            differentFirstPage: false);

        first.Kind.Should().Be(HeaderFooterSlotActivationKind.RequiresDifferentFirstPage);
        first.Message.Should().Contain("Different First Page");

        var active = HeaderFooterDialogPlanner.PlanSlotActivation(
            HeaderFooterSlotKind.EvenHeader,
            differentOddEvenPages: true,
            differentFirstPage: false);

        active.Kind.Should().Be(HeaderFooterSlotActivationKind.Active);
        active.Message.Should().BeNull();
    }

    [Fact]
    public void PlanSlotActivation_PageSettingsOverloadUsesTypedSlot()
    {
        var page = new PageSettings
        {
            DifferentOddEvenPages = true,
            DifferentFirstPage = false
        };

        HeaderFooterDialogPlanner.PlanSlotActivation(HeaderFooterSlotKind.EvenHeader, page)
            .Kind.Should().Be(HeaderFooterSlotActivationKind.Active);
        HeaderFooterDialogPlanner.PlanSlotActivation(HeaderFooterSlotKind.FirstHeader, page)
            .Kind.Should().Be(HeaderFooterSlotActivationKind.RequiresDifferentFirstPage);
    }

    [Fact]
    public void BuildPlainTextHeaderFooter_ClearsBlankSlotUnlessPageNumberExists()
    {
        HeaderFooterDialogPlanner.BuildPlainTextHeaderFooter(string.Empty, existing: null)
            .Should().BeNull();

        var existing = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.PageNumberField());
        existing.Paragraphs.Add(paragraph);

        var result = HeaderFooterDialogPlanner.BuildPlainTextHeaderFooter("Title", existing);

        result.Should().NotBeNull();
        var runs = result!.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().ContainInOrder("Title", HeaderFooterDialogPlanner.RunSeparator);
        runs.Should().Contain(run => run.FieldKind == RunFieldKind.PageNumber);
    }

    [Fact]
    public void AddPageNumberToSlot_InsertsCenteredPageFieldOnce()
    {
        var headerFooter = HeaderFooterDialogPlanner.AddPageNumberToSlot(current: null);
        var paragraph = headerFooter.Paragraphs.Single();

        paragraph.Formatting.Alignment.Should().Be(TextAlignment.Center);
        paragraph.Runs[0].Text.Should().Be(HeaderFooterDialogPlanner.PageNumberPrefix);
        paragraph.Runs.Should().Contain(run => run.FieldKind == RunFieldKind.PageNumber);

        var again = HeaderFooterDialogPlanner.AddPageNumberToSlot(headerFooter);

        again.Should().BeSameAs(headerFooter);
        again.Paragraphs.SelectMany(p => p.Runs)
            .Count(run => run.FieldKind == RunFieldKind.PageNumber)
            .Should().Be(1);
    }

    [Fact]
    public void BuildSlotDialogResult_AppendsDateFieldAndPageNumberWithSeparators()
    {
        var result = HeaderFooterDialogPlanner.BuildSlotDialogResult(
            "Prepared by",
            appendPageNumber: true,
            appendDateTimeText: "June 29, 2026",
            appendFieldInstruction: " AUTHOR ");

        result.Should().NotBeNull();
        var runs = result!.Paragraphs.Single().Runs;

        runs[0].Text.Should().Be("Prepared by");
        runs[1].Text.Should().Be(HeaderFooterDialogPlanner.RunSeparator);
        runs[2].Text.Should().Be("June 29, 2026");
        runs[3].Text.Should().Be(HeaderFooterDialogPlanner.RunSeparator);
        runs[4].ComplexField!.Instruction.Should().Be(" AUTHOR ");
        runs[5].Text.Should().Be(HeaderFooterDialogPlanner.RunSeparator);
        runs[6].FieldKind.Should().Be(RunFieldKind.PageNumber);
    }

    [Fact]
    public void BuildSlotDialogState_ProjectsTextAndInsertFlags()
    {
        var headerFooter = new HeaderFooter();
        var paragraph = new Paragraph("Intro");
        paragraph.Runs.Add(Run.ComplexFieldRun(" AUTHOR "));
        paragraph.Runs.Add(Run.PageNumberField());
        headerFooter.Paragraphs.Add(paragraph);

        var state = HeaderFooterDialogPlanner.BuildSlotDialogState(headerFooter);

        state.Text.Should().Contain("Intro");
        state.HasComplexField.Should().BeTrue();
        state.HasPageNumber.Should().BeTrue();
        state.CanInsertPageNumber.Should().BeFalse();
    }

    [Theory]
    [InlineData("36", true, 36)]
    [InlineData("10.5", true, 10.5)]
    [InlineData("-1", false, 0)]
    [InlineData("bad", false, 0)]
    public void DistanceParsing_UsesInvariantPositivePointValues(
        string text,
        bool expected,
        double expectedPoints)
    {
        HeaderFooterDialogPlanner.TryParseDistance(text, out var points).Should().Be(expected);
        if (expected)
            points.Should().Be(expectedPoints);
    }

    [Fact]
    public void FormatDistance_UsesCompactInvariantText()
    {
        HeaderFooterDialogPlanner.FormatDistance(36).Should().Be("36");
        HeaderFooterDialogPlanner.FormatDistance(36.25).Should().Be("36.25");
    }
}
