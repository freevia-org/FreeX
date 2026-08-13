using System.Collections.Generic;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageSetupCommandFactoryTests
{
    [Fact]
    public void BuildHeaderFooterCommand_AppliesSharedHeaderFooterRequest()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var request = new HeaderFooterEditorState
        {
            Header = new WorksheetHeaderFooter("Left", "Center", "Right"),
            Footer = new WorksheetHeaderFooter("Footer left", "Footer center", "Footer right"),
            FirstPageHeader = new WorksheetHeaderFooter("First left", "First center", "First right"),
            EvenPageFooter = new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"),
            DifferentFirstPage = true,
            DifferentOddEvenPages = true,
            ScaleWithDocument = false,
            AlignWithMargins = false
        };

        var command = PageSetupCommandFactory.BuildHeaderFooterCommand(sheet.Id, request);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PageHeader.Should().Be(request.Header);
        sheet.PageFooter.Should().Be(request.Footer);
        sheet.FirstPageHeader.Should().Be(request.FirstPageHeader);
        sheet.EvenPageFooter.Should().Be(request.EvenPageFooter);
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        sheet.HeaderFooterAlignWithMargins.Should().BeFalse();
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
