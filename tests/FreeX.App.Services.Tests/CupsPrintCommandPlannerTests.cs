using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class CupsPrintCommandPlannerTests
{
    private static PrintJobSubmission Submission(
        string printerId = "Office_HP",
        int copies = 1,
        bool collate = true,
        int firstPage = 1,
        int lastPage = 1,
        string title = "Budget") =>
        new(printerId, [1, 2, 3], copies, collate, firstPage, lastPage, title);

    [Fact]
    public void BuildSubmitArguments_IncludesPrinterCopiesRangeCollateTitleAndPath()
    {
        var args = CupsPrintCommandPlanner.BuildSubmitArguments(
            Submission(copies: 2, collate: true, firstPage: 2, lastPage: 4),
            "/tmp/job.pdf");

        args.Should().ContainInOrder("-d", "Office_HP");
        args.Should().ContainInOrder("-n", "2");
        args.Should().ContainInOrder("-P", "2-4");
        args.Should().ContainInOrder("-o", "collate=true");
        args.Should().ContainInOrder("-t", "Budget");
        args[^1].Should().Be("/tmp/job.pdf");
    }

    [Fact]
    public void BuildSubmitArguments_SingleCopy_OmitsCountFlag()
    {
        var args = CupsPrintCommandPlanner.BuildSubmitArguments(Submission(copies: 1), "/tmp/job.pdf");

        args.Should().NotContain("-n");
    }

    [Fact]
    public void BuildSubmitArguments_UncollatedEmitsCollateFalse()
    {
        var args = CupsPrintCommandPlanner.BuildSubmitArguments(Submission(collate: false), "/tmp/job.pdf");

        args.Should().ContainInOrder("-o", "collate=false");
    }

    [Fact]
    public void BuildSubmitArguments_NoPrinterId_OmitsDestinationFlag()
    {
        var args = CupsPrintCommandPlanner.BuildSubmitArguments(Submission(printerId: ""), "/tmp/job.pdf");

        args.Should().NotContain("-d");
        args[^1].Should().Be("/tmp/job.pdf");
    }

    [Fact]
    public void ParsePrinters_ParsesLinesAndMarksDefaultFirst()
    {
        const string listOutput = "Office_HP\nLab_Brother\nReception\n";

        var printers = CupsPrintCommandPlanner.ParsePrinters(listOutput, "Lab_Brother");

        printers.Should().HaveCount(3);
        printers[0].Id.Should().Be("Lab_Brother");
        printers[0].IsDefault.Should().BeTrue();
        printers.Where(p => p.IsDefault).Should().ContainSingle();
    }

    [Fact]
    public void ParsePrinters_DefaultMissingFromList_IsAppended()
    {
        var printers = CupsPrintCommandPlanner.ParsePrinters("Office_HP\n", "Hidden_Default");

        printers.Should().Contain(p => p.Id == "Hidden_Default" && p.IsDefault);
    }

    [Fact]
    public void ParsePrinters_EmptyOutput_ReturnsEmpty()
    {
        CupsPrintCommandPlanner.ParsePrinters("", null).Should().BeEmpty();
        CupsPrintCommandPlanner.ParsePrinters(null, null).Should().BeEmpty();
    }

    [Fact]
    public void ParseDefaultPrinter_ExtractsNameAfterMarker()
    {
        CupsPrintCommandPlanner
            .ParseDefaultPrinter("system default destination: Office_HP")
            .Should().Be("Office_HP");
    }

    [Fact]
    public void ParseDefaultPrinter_NoDefault_ReturnsNull()
    {
        CupsPrintCommandPlanner.ParseDefaultPrinter("no system default destination").Should().BeNull();
        CupsPrintCommandPlanner.ParseDefaultPrinter("").Should().BeNull();
        CupsPrintCommandPlanner.ParseDefaultPrinter(null).Should().BeNull();
    }

    [Fact]
    public async Task NullPlatformPrinter_CannotPrintAndEnumeratesNothing()
    {
        var printer = NullPlatformPrinter.Instance;

        printer.CanPrint.Should().BeFalse();
        (await printer.GetPrintersAsync()).Should().BeEmpty();
        (await printer.SubmitAsync(Submission())).Succeeded.Should().BeFalse();
    }
}
