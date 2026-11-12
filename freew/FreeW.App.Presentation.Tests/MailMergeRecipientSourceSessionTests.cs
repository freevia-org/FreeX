using FreeW.App.Localization;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO.RecipientSources;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeRecipientSourceSessionTests
{
    private static readonly IReadOnlyList<IReadOnlyList<string>> PeopleRows =
    [
        ["Name", "City"],
        ["Ada", "London"],
        ["Grace", "New York"],
        ["Alan", "Wilmslow"],
        ["Edsger", "Rotterdam"],
        ["Barbara", "Los Angeles"],
        ["Donald", "Milwaukee"],
    ];

    private static RecipientSourceImportSession WorkbookSession(List<RecipientSourceOptions> reads) =>
        new(
            "people.xlsx",
            new RecipientSourceInfo(
                RecipientSourceKind.Workbook,
                [
                    new RecipientSourceTable("sheet:People", "People$", RecipientSourceTableKind.Worksheet),
                    new RecipientSourceTable("name:VIPs", "VIPs", RecipientSourceTableKind.NamedRange),
                    new RecipientSourceTable("table:Staff", "Staff", RecipientSourceTableKind.Table),
                ],
                ',',
                string.Empty),
            options =>
            {
                reads.Add(options);
                return options.TableKey switch
                {
                    "name:VIPs" => PeopleRows.Take(2).ToArray(),
                    "table:Staff" => throw new RecipientSourceException(RecipientSourceError.NoTable),
                    _ => PeopleRows,
                };
            });

    [Fact]
    public void ImportSession_ForWorkbook_OffersTables_AndPreviewsTheFirstRecords()
    {
        var reads = new List<RecipientSourceOptions>();
        var session = WorkbookSession(reads);

        session.ShowsTables.Should().BeTrue();
        session.ShowsDelimiters.Should().BeFalse();
        session.SourceNote.Should().BeNull();
        session.Tables.Select(table => table.Label).Should().Equal(
            "People$ (worksheet)", "VIPs (named range)", "Staff (table)");
        session.FileLabel.Should().Contain("people.xlsx");
        session.FirstRowIsHeader.Should().BeTrue();

        var preview = session.Preview;

        preview.IsValid.Should().BeTrue();
        preview.Header.Should().Equal("Name", "City");
        preview.Rows.Should().HaveCount(RecipientSourceImportSession.PreviewRowCount);
        preview.TotalRecords.Should().Be(6);
        preview.Summary.Should().Be(MailMergeRecipientSourceText.PreviewCount(5, 6));
        _ = session.Preview;
        reads.Should().ContainSingle("the source is read once until an option changes");
        reads[0].Should().Be(new RecipientSourceOptions("sheet:People", true, null));
        session.Accept()!.Count.Should().Be(6);
    }

    [Fact]
    public void ImportSession_OptionChanges_RereadAndRebuildThePreview()
    {
        var reads = new List<RecipientSourceOptions>();
        var session = WorkbookSession(reads);
        _ = session.Preview;

        session.SelectTable(1);
        session.Preview.TotalRecords.Should().Be(1);
        session.Options.TableKey.Should().Be("name:VIPs");

        session.SetFirstRowIsHeader(false);
        var headerless = session.Preview;
        headerless.Header.Should().Equal("F1", "F2");
        headerless.TotalRecords.Should().Be(2, "the first row is now a record");
        headerless.Rows[0].Should().Equal("Name", "City");

        session.SelectTable(1);
        session.SetFirstRowIsHeader(false);
        session.SelectTable(99);
        _ = session.Preview;
        reads.Should().HaveCount(3, "re-selecting the same option does not re-read the file");
    }

    [Fact]
    public void ImportSession_UnusableSelection_ShowsTheReason_AndCannotBeAccepted()
    {
        var session = WorkbookSession([]);

        session.SelectTable(2);

        session.Preview.IsValid.Should().BeFalse();
        session.Preview.Summary.Should().Be(MailMergeRecipientSourceText.ErrorMessage(RecipientSourceError.NoTable));
        session.CanAccept.Should().BeFalse();
        session.Accept().Should().BeNull();

        session.SelectTable(0);
        session.CanAccept.Should().BeTrue();
    }

    [Fact]
    public void ImportSession_ForDelimitedText_PreselectsDetectedDelimiter_AndPassesTheChoice()
    {
        var reads = new List<RecipientSourceOptions>();
        var session = new RecipientSourceImportSession(
            "people.txt",
            new RecipientSourceInfo(RecipientSourceKind.DelimitedText, [], ';', "Unicode (UTF-8)"),
            options =>
            {
                reads.Add(options);
                return MergeRecipientTable.ParseDelimited("Name;City\nAda;London\n", options.Delimiter ?? ',');
            });

        session.ShowsTables.Should().BeFalse();
        session.ShowsDelimiters.Should().BeTrue();
        session.Delimiters.Select(choice => choice.Value).Should().Equal(MergeRecipientTable.SupportedDelimiters);
        session.Delimiters[session.SelectedDelimiterIndex].Value.Should().Be(';');
        session.SourceNote.Should().Be(MailMergeRecipientSourceText.Encoding("Unicode (UTF-8)"));
        session.Preview.Header.Should().Equal("Name", "City");

        session.SelectDelimiter(0);

        session.Options.Delimiter.Should().Be(',');
        session.Preview.Header.Should().Equal("Name;City");
        reads.Select(options => options.Delimiter).Should().Equal(';', ',');
    }

    [Fact]
    public void ImportSession_ForDocument_ExplainsThatTheFirstTableIsUsed_AndReportsAnEmptyList()
    {
        var session = new RecipientSourceImportSession(
            "people.docx",
            new RecipientSourceInfo(RecipientSourceKind.Document, [], ',', string.Empty),
            _ => []);

        session.ShowsTables.Should().BeFalse();
        session.ShowsDelimiters.Should().BeFalse();
        session.SourceNote.Should().Be(MailMergeRecipientSourceText.DocumentHint);
        session.Options.Should().Be(new RecipientSourceOptions(null, true, null));
        session.Preview.Summary.Should().Be(MailMergeRecipientSourceText.ErrorMessage(RecipientSourceError.Empty));
    }

    [Fact]
    public void Planner_BuildsFileDialogFilters_AndDescribesFailures()
    {
        var open = MailMergeRecipientSourcePlanner.OpenFilters;

        open[0].Extensions.Should().Contain([".xlsx", ".xlsm", ".csv", ".tsv", ".txt", ".docx", ".rtf"]);
        open.Skip(1).SelectMany(filter => filter.Extensions).Should().BeEquivalentTo(open[0].Extensions);
        open[1].Pattern.Should().Be("*.xlsx;*.xlsm");
        var filter = MailMergeRecipientSourcePlanner.ToDialogFilter(open);
        filter.Split('|').Should().HaveCount(open.Count * 2);
        filter.Should().StartWith(MailMergeRecipientSourceText.FilterAll + " (*.xlsx;");
        MailMergeRecipientSourcePlanner.SaveFilters.SelectMany(save => save.Extensions)
            .Should().BeSubsetOf(RecipientListFile.SaveExtensions);

        MailMergeRecipientSourcePlanner.DescribeFailure(new RecipientSourceException(RecipientSourceError.TooLarge))
            .Should().Be(MailMergeRecipientSourceText.ErrorMessage(RecipientSourceError.TooLarge));
        MailMergeRecipientSourcePlanner.DescribeFailure(new IOException("locked"))
            .Should().Be(MailMergeRecipientSourceText.ErrorMessage(RecipientSourceError.Unreadable));
        foreach (var error in Enum.GetValues<RecipientSourceError>())
            MailMergeRecipientSourceText.ErrorMessage(error).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EditorSession_StartsWithTheStandardAddressFields_AndOneEmptyEntry()
    {
        var session = new RecipientListEditorSession();

        session.Columns.Should().Equal(RecipientListEditorSession.DefaultColumns);
        session.Columns.Should().Contain(["First Name", "Last Name", "E-mail Address"]);
        session.RowCount.Should().Be(1);
        session.Build().Count.Should().Be(0, "an entry left blank is not a recipient");
        session.Build().Header.Should().Equal(RecipientListEditorSession.DefaultColumns);
    }

    [Fact]
    public void EditorSession_AddsAndRemovesEntries_AndBuildsMergeData()
    {
        var session = new RecipientListEditorSession();
        var first = session.Columns.ToList().IndexOf("First Name");
        var email = session.Columns.ToList().IndexOf("E-mail Address");

        session.SetCell(0, first, "Ada");
        session.SetCell(0, email, "ada@example.com");
        var second = session.AddRow();
        session.SetCell(second, first, "Grace");
        var third = session.AddRow();
        session.SetCell(third, first, "   ");
        session.SetCell(99, 0, "ignored");
        session.SetCell(0, 99, "ignored");

        var data = session.Build();

        data.Count.Should().Be(2);
        data.Rows[0]["First Name"].Should().Be("Ada");
        data.Rows[0]["E-mail Address"].Should().Be("ada@example.com");
        data.Rows[1]["First Name"].Should().Be("Grace");

        session.RemoveRow(0);
        session.Build().Rows.Should().ContainSingle().Which["First Name"].Should().Be("Grace");
        session.RemoveRow(0);
        session.RemoveRow(0);
        session.RowCount.Should().Be(1, "the grid always keeps an entry to type into");
        session.Cell(0, first).Should().BeEmpty();
    }

    [Fact]
    public void EditorSession_CustomizesColumns_WithValidation()
    {
        var session = new RecipientListEditorSession();
        session.SetCell(0, 0, "Dr");
        var count = session.Columns.Count;

        session.AddColumn("  Nickname ").Success.Should().BeTrue();
        session.Columns[^1].Should().Be("Nickname");
        session.Row(0).Should().HaveCount(count + 1);
        session.AddColumn(" ").Error.Should().Be(RecipientListEditorError.EmptyColumnName);
        var duplicate = session.AddColumn("nickname");
        duplicate.Error.Should().Be(RecipientListEditorError.DuplicateColumnName);
        duplicate.Message.Should().Contain("nickname");

        session.RenameColumn(0, "Salutation").Success.Should().BeTrue();
        session.RenameColumn(0, "salutation").Success.Should().BeTrue("a column may keep its own name");
        session.RenameColumn(1, "Salutation").Error.Should().Be(RecipientListEditorError.DuplicateColumnName);
        session.Cell(0, 0).Should().Be("Dr", "renaming keeps the values");

        session.RemoveColumn(0).Success.Should().BeTrue();
        session.Columns.Should().NotContain("salutation");
        session.Row(0).Should().HaveCount(count);
        while (session.Columns.Count > 1)
            session.RemoveColumn(0).Success.Should().BeTrue();
        session.RemoveColumn(0).Error.Should().Be(RecipientListEditorError.LastColumn);
        session.Columns.Should().ContainSingle();
    }

    [Fact]
    public void EditorSession_FromExistingList_KeepsItsColumnsAndEntries()
    {
        var existing = new MergeData(["Name", "City"], [["Ada", "London"], ["Grace", "New York"]]);

        var session = new RecipientListEditorSession(existing);

        session.Columns.Should().Equal("Name", "City");
        session.RowCount.Should().Be(2);
        session.Cell(1, 1).Should().Be("New York");
    }

    private static MergeData People() => new(
        ["Name", "City", "Email"],
        [
            ["Grace", "New York", "grace@example.com"],
            ["ada", "London", ""],
            ["Alan", "Wilmslow", "alan@example.com"],
            ["Édith", "Paris", "edith@example.org"],
        ]);

    [Fact]
    public void SelectionSession_ListsActiveRecipientsFirstAndChecked_ThenTheExcludedOnes()
    {
        var session = new RecipientListSelectionSession(People(), [2, 0, 2, 99]);

        var rows = session.VisibleRows;

        rows.Select(row => row.SourceIndex).Should().Equal(2, 0, 1, 3);
        rows.Select(row => row.IsIncluded).Should().Equal(true, true, false, false);
        rows[0].Values.Should().Equal("Alan", "Wilmslow", "alan@example.com");
        session.IncludedCount.Should().Be(2);
        session.Summary.Should().Be(MailMergeRecipientSourceText.Summary(2, 4));
        session.BuildSelection().Should().Equal(2, 0);
    }

    [Fact]
    public void SelectionSession_IncludeExclude_AndSelectAllClearAll()
    {
        var session = new RecipientListSelectionSession(People(), [0, 1, 2, 3]);

        session.SetIncluded(1, false);
        session.SetIncluded(99, false);
        session.BuildSelection().Should().Equal(0, 2, 3);

        session.SetAllVisibleIncluded(false);
        session.BuildSelection().Should().BeEmpty();
        session.SetAllVisibleIncluded(true);
        session.BuildSelection().Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void SelectionSession_SortsIgnoringCase_BothDirections_AndIsStable()
    {
        var session = new RecipientListSelectionSession(People(), [0, 1, 2, 3]);

        session.Sort(0, ascending: true);
        session.VisibleRows.Select(row => row.Values[0]).Should().Equal("ada", "Alan", "Édith", "Grace");

        session.Sort(0, ascending: false);
        session.BuildSelection().Should().Equal(0, 3, 2, 1);

        session.Sort(99, ascending: true);
        session.BuildSelection().Should().Equal([0, 3, 2, 1], "an unknown column leaves the order alone");
        session.SortColumns[0].Value.Should().Be(-1);
        session.SortColumns.Skip(1).Select(choice => choice.Label).Should().Equal("Name", "City", "Email");
        session.SortDirections.Select(choice => choice.Value).Should().Equal(true, false);
    }

    [Fact]
    public void SelectionSession_Filter_NarrowsTheListAndTheMerge()
    {
        var session = new RecipientListSelectionSession(People(), [0, 1, 2, 3]);
        session.HasFilter.Should().BeFalse();

        session.SetFilter(2, RecipientFilterComparison.Contains, "EXAMPLE.COM");
        session.VisibleRows.Select(row => row.SourceIndex).Should().Equal(0, 2);
        session.BuildSelection().Should().Equal([0, 2], "a filtered-out recipient is not merged");
        session.Summary.Should().Be(MailMergeRecipientSourceText.Summary(2, 4));

        session.SetFilter(2, RecipientFilterComparison.NotContains, "example.com");
        session.VisibleRows.Select(row => row.SourceIndex).Should().Equal(1, 3);

        session.SetFilter(1, RecipientFilterComparison.Equals, " london ");
        session.VisibleRows.Should().ContainSingle().Which.SourceIndex.Should().Be(1);

        session.SetFilter(1, RecipientFilterComparison.NotEquals, "London");
        session.VisibleRows.Select(row => row.SourceIndex).Should().Equal(0, 2, 3);

        session.SetFilter(2, RecipientFilterComparison.IsBlank, "ignored");
        session.FilterNeedsValue.Should().BeFalse();
        session.VisibleRows.Should().ContainSingle().Which.SourceIndex.Should().Be(1);

        session.SetFilter(2, RecipientFilterComparison.IsNotBlank, null);
        session.VisibleRows.Should().HaveCount(3);

        session.SetFilter(-1, RecipientFilterComparison.Contains, "paris");
        session.FilterColumnIndex.Should().Be(0, "the first choice is 'any field'");
        session.VisibleRows.Should().ContainSingle().Which.SourceIndex.Should().Be(3);

        session.SetFilter(1, RecipientFilterComparison.Contains, "   ");
        session.HasFilter.Should().BeFalse("an empty value filters nothing");
        session.VisibleRows.Should().HaveCount(4);

        session.SetFilter(1, RecipientFilterComparison.Equals, "London");
        session.ClearFilter();
        session.VisibleRows.Should().HaveCount(4);
        session.FilterComparisons.Select(choice => choice.Value)
            .Should().Equal(Enum.GetValues<RecipientFilterComparison>());
    }

    [Fact]
    public void SelectionSession_SelectAll_OnlyTouchesWhatTheFilterShows()
    {
        var session = new RecipientListSelectionSession(People(), []);
        session.SetFilter(2, RecipientFilterComparison.Contains, "example.com");

        session.SetAllVisibleIncluded(true);
        session.ClearFilter();

        session.BuildSelection().Should().Equal(0, 2);
    }

    [Fact]
    public void Workflow_ApplyRecipientSelection_KeepsTheFullList_AndOriginalRecordNumbers()
    {
        var session = new MailMergeSession();
        var workflow = new MailMergeSessionWorkflow(session);
        workflow.LoadRecipients(People());
        session.SourceData.Should().BeSameAs(session.Data);
        workflow.ActiveSourceIndexes().Should().Equal(0, 1, 2, 3);

        var transition = workflow.ApplyRecipientSelection([3, 1, 3, 42]);

        transition.Message.Should().Be("Recipient list now contains 2 record(s).");
        session.Data!.Rows.Select(row => row["Name"]).Should().Equal("Édith", "ada");
        session.SourceData!.Count.Should().Be(4, "excluded recipients stay available");
        session.HasFilteredRecipients.Should().BeTrue();
        workflow.ActiveSourceIndexes().Should().Equal(3, 1);

        // Re-opening Edit Recipient List shows the current state and can bring everyone back.
        var selection = new RecipientListSelectionSession(session.SourceData, workflow.ActiveSourceIndexes());
        selection.VisibleRows.Select(row => (row.SourceIndex, row.IsIncluded))
            .Should().Equal((3, true), (1, true), (0, false), (2, false));
        selection.SetAllVisibleIncluded(true);
        workflow.ApplyRecipientSelection(selection.BuildSelection());
        session.Data.Count.Should().Be(4);
        session.HasFilteredRecipients.Should().BeFalse();
    }

    [Fact]
    public void Workflow_ApplyRecipientSelection_ComposesWithTheOlderFilterAndSortCommand()
    {
        var session = new MailMergeSession();
        var workflow = new MailMergeSessionWorkflow(session);
        workflow.LoadRecipients(People());

        workflow.ApplyRecipientFilter(
            MailMergeRecipientFilterSortPlanner.Apply(session.Data!, [2, 0], "Name", ascending: true));

        workflow.ActiveSourceIndexes().Should().Equal([2, 0], "Alan sorts before Grace");
        session.SourceData!.Count.Should().Be(4);
    }

    [Fact]
    public void Workflow_WithNoRecipientList_DoesNothing_AndClearForgetsTheSource()
    {
        var session = new MailMergeSession();
        var workflow = new MailMergeSessionWorkflow(session);

        workflow.ApplyRecipientSelection([0]).DocumentToLoad.Should().BeNull();
        session.Data.Should().BeNull();
        workflow.ActiveSourceIndexes().Should().BeEmpty();

        workflow.LoadRecipients(People());
        workflow.Clear();
        session.SourceData.Should().BeNull();
    }

    [Fact]
    public void RequiredResourceKeys_ResolveToRealText()
    {
        MailMergeRecipientSourceText.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        foreach (var key in MailMergeRecipientSourceText.RequiredResourceKeys)
        {
            var text = Loc.Get(key);
            text.Should().NotBeNullOrWhiteSpace();
            text.Should().NotContain("[[", $"resource {key} must exist in the neutral catalog");
        }
    }
}
