namespace FreeW.Core.Model.Tests;

/// <summary>
/// Model-level regression coverage for the section-index parameter added to
/// <see cref="SetPageSettingsCommand"/> (and shared by <see cref="PageSettingsSectionResolver"/>): the
/// command must target the requested section's <see cref="PageSettings"/> instance rather than always
/// <see cref="TextDocument.Page"/> (the final section), which was the pre-fix bug for every Layout /
/// Page Setup ribbon command on a multi-section document.
/// </summary>
public class PageSettingsSectionResolverTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static TextDocument TwoSectionDocument(out PageSettings section0Page)
    {
        var doc = new TextDocument();
        section0Page = new PageSettings { MarginLeftPt = 111 };
        doc.Blocks.Add(new Paragraph("section one")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("final section"));
        doc.Page.MarginLeftPt = 222;
        return doc;
    }

    [Fact]
    public void Resolve_NegativeIndex_ReturnsFinalSectionPage()
    {
        var doc = TwoSectionDocument(out _);

        PageSettingsSectionResolver.Resolve(doc, -1).Should().BeSameAs(doc.Page);
    }

    [Fact]
    public void Resolve_IndexZero_ReturnsFirstSectionPage_NotFinal()
    {
        var doc = TwoSectionDocument(out var section0Page);

        var resolved = PageSettingsSectionResolver.Resolve(doc, 0);

        resolved.Should().BeSameAs(section0Page);
        resolved.Should().NotBeSameAs(doc.Page);
    }

    [Fact]
    public void Resolve_IndexOutOfRange_ClampsIntoBounds()
    {
        var doc = TwoSectionDocument(out var section0Page);

        PageSettingsSectionResolver.Resolve(doc, 99).Should().BeSameAs(doc.Page);
        PageSettingsSectionResolver.Resolve(doc, -5).Should().BeSameAs(doc.Page);
    }

    [Fact]
    public void SetPageSettingsCommand_WithSectionIndexZero_AppliesToFirstSection_LeavesFinalSectionUntouched()
    {
        var doc = TwoSectionDocument(out var section0Page);
        var settings = section0Page.Clone();
        settings.MarginLeftPt = 555;
        var command = new SetPageSettingsCommand(settings, sectionIndex: 0);
        var context = new CommandContext(doc);

        command.Apply(context);

        doc.Sections[0].Page.MarginLeftPt.Should().Be(555);
        doc.Page.MarginLeftPt.Should().Be(222); // final section (the historical always-written target) is untouched

        command.Revert(context);

        doc.Sections[0].Page.MarginLeftPt.Should().Be(111);
        doc.Page.MarginLeftPt.Should().Be(222);
    }

    [Fact]
    public void SetPageSettingsCommand_DefaultSectionIndex_StillAppliesToFinalSection()
    {
        // Sibling no-regression: omitting sectionIndex (existing call sites, existing tests) must keep
        // targeting the final section exactly as before this change.
        var doc = TwoSectionDocument(out var section0Page);
        var settings = doc.Page.Clone();
        settings.MarginLeftPt = 999;
        var command = new SetPageSettingsCommand(settings);
        var context = new CommandContext(doc);

        command.Apply(context);

        doc.Page.MarginLeftPt.Should().Be(999);
        doc.Sections[0].Page.MarginLeftPt.Should().Be(111); // first section untouched
        section0Page.MarginLeftPt.Should().Be(111);

        command.Revert(context);

        doc.Page.MarginLeftPt.Should().Be(222);
    }
}
