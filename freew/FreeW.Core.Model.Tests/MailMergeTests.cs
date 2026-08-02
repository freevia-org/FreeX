namespace FreeW.Core.Model.Tests;

public class MailMergeTests
{
    [Fact]
    public void FieldNames_FromText_AreDistinctInFirstAppearanceOrder()
    {
        const string text = "Dear «First» «Last», your code is «First».";

        var names = MailMerge.FieldNames(text);

        names.Should().Equal("First", "Last");
    }

    [Fact]
    public void FieldNames_AreDistinctCaseInsensitively_FirstSpellingWins()
    {
        const string text = "«City» then «city» then «CITY»";

        MailMerge.FieldNames(text).Should().Equal("City");
    }

    [Fact]
    public void FieldNames_TrimsWhitespaceAndIgnoresEmptyPlaceholders()
    {
        const string text = "«  Name  » and «» and «   »";

        MailMerge.FieldNames(text).Should().Equal("Name");
    }

    [Fact]
    public void FieldNames_FromDocument_ScansParagraphsAndTableCells()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Hello «Name», "));
        para.Runs.Add(new Run("from «Company»"));
        doc.Blocks.Add(para);

        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("«Item»"));
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(new Run("«Name»")); // duplicate, dropped
        doc.Blocks.Add(table);

        MailMerge.FieldNames(doc).Should().Equal("Name", "Company", "Item");
    }

    [Fact]
    public void Substitute_ReplacesPresentField()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        MailMerge.Substitute("Hello «Name»!", row).Should().Be("Hello Ada!");
    }

    [Fact]
    public void Substitute_MissingField_BecomesEmptyString()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        MailMerge.Substitute("Hi «Name» «Title»!", row).Should().Be("Hi Ada !");
    }

    [Fact]
    public void Substitute_IsCaseInsensitive_WhenDictionarySupportsIt()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada" };

        MailMerge.Substitute("Hello «Name»!", row).Should().Be("Hello Ada!");
    }

    [Fact]
    public void Substitute_UnterminatedDelimiter_NoClosing_IsLeftLiteral()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        // No closing » anywhere → the opening « and the rest are emitted verbatim.
        MailMerge.Substitute("Hello «Name and the rest", row)
            .Should().Be("Hello «Name and the rest");
    }

    [Fact]
    public void Substitute_NoPlaceholders_ReturnsInputUnchanged()
    {
        MailMerge.Substitute("plain text", new Dictionary<string, string>())
            .Should().Be("plain text");
    }

    [Fact]
    public void MergeRecord_SubstitutesEveryRun_AndDoesNotMutateTemplate()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Dear ", new RunFormatting { Bold = true }));
        para.Runs.Add(new Run("«First» «Last»"));
        template.Blocks.Add(para);

        var row = new Dictionary<string, string> { ["First"] = "Grace", ["Last"] = "Hopper" };
        var merged = MailMerge.MergeRecord(template, row);

        merged.PlainText.Should().Be("Dear Grace Hopper");
        // The bold formatting on the leading run is preserved.
        var firstRun = ((Paragraph)merged.Blocks[0]).Runs[0];
        firstRun.Formatting.Bold.Should().BeTrue();
        // Template is untouched.
        template.PlainText.Should().Be("Dear «First» «Last»");
    }

    [Fact]
    public void MergeRecord_SubstitutesInsideTableCells()
    {
        var template = new TextDocument();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("«Item»"));
        template.Blocks.Add(table);

        var merged = MailMerge.MergeRecord(template,
            new Dictionary<string, string> { ["Item"] = "Widget" });

        var cell = ((Table)merged.Blocks[0]).Rows[0].Cells[0];
        cell.PlainText.Should().Be("Widget");
    }

    [Fact]
    public void MergeRecord_SubstitutesAllSectionHeaderFooterStoriesAndPreservesPageSettings()
    {
        var template = new TextDocument();
        template.Page.DifferentFirstPage = true;
        template.Page.DifferentOddEvenPages = true;
        template.Page.HeaderDistancePt = 24;
        template.Page.FooterDistancePt = 18;
        template.FirstHeader = new HeaderFooter("Final first «Name»");
        template.EvenFooter = new HeaderFooter("Final even «Name»");

        var firstSection = new Section(
            new PageSettings
            {
                DifferentFirstPage = true,
                DifferentOddEvenPages = true,
                HeaderDistancePt = 30
            },
            SectionBreakKind.OddPage)
        {
            HeadersFooters = new SectionHeadersFooters
            {
                Header = new HeaderFooter("Section default «Name»"),
                EvenHeader = new HeaderFooter("Section even «Name»"),
                FirstFooter = new HeaderFooter("Section first «Name»")
            }
        };
        template.Blocks.Add(new Paragraph("Body «Name»") { SectionBreak = firstSection });
        template.Blocks.Add(new Paragraph("Final body «Name»"));

        var merged = MailMerge.MergeRecord(template,
            new Dictionary<string, string> { ["Name"] = "Ada" });

        merged.Page.DifferentFirstPage.Should().BeTrue();
        merged.Page.DifferentOddEvenPages.Should().BeTrue();
        merged.Page.HeaderDistancePt.Should().Be(24);
        merged.Page.FooterDistancePt.Should().Be(18);
        merged.FirstHeader!.PlainText.Should().Be("Final first Ada");
        merged.EvenFooter!.PlainText.Should().Be("Final even Ada");
        var mergedSection = ((Paragraph)merged.Blocks[0]).SectionBreak!;
        mergedSection.Should().NotBeSameAs(firstSection);
        mergedSection.Page.Should().NotBeSameAs(firstSection.Page);
        mergedSection.BreakKind.Should().Be(SectionBreakKind.OddPage);
        mergedSection.HeadersFooters.Header!.PlainText.Should().Be("Section default Ada");
        mergedSection.HeadersFooters.EvenHeader!.PlainText.Should().Be("Section even Ada");
        mergedSection.HeadersFooters.FirstFooter!.PlainText.Should().Be("Section first Ada");
        template.FirstHeader!.PlainText.Should().Contain("«Name»");
        firstSection.HeadersFooters.Header!.PlainText.Should().Contain("«Name»");
    }

    [Fact]
    public void MergeRecordWithRules_EvaluatesRulesInFirstEvenAndNonFinalSectionStories()
    {
        var template = new TextDocument();
        var ifInstruction = MergeRuleEvaluator.BuildIfInstruction(
            "City", MergeConditionOperator.Equal, "London", "Local", "Remote");
        template.FirstHeader = new HeaderFooter(
            $"{MailMerge.FieldOpen}{ifInstruction}{MailMerge.FieldClose}");
        template.EvenFooter = new HeaderFooter($"Even {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        template.Blocks.Add(new Paragraph("Section end")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
            {
                HeadersFooters = new SectionHeadersFooters
                {
                    FirstFooter = new HeaderFooter(
                        $"Section {MailMerge.FieldOpen}Name{MailMerge.FieldClose}")
                }
            }
        });

        var merged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada", ["City"] = "London" },
            new MergeState(),
            recordIndex: 1);

        merged.FirstHeader!.PlainText.Should().Be("Local");
        merged.EvenFooter!.PlainText.Should().Be("Even Ada");
        ((Paragraph)merged.Blocks[0]).SectionBreak!.HeadersFooters.FirstFooter!.PlainText
            .Should().Be("Section Ada");
    }

    [Fact]
    public void MergeRecord_PreservesBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var template = new TextDocument();
        var paragraph = new Paragraph
        {
            BlockContentControl = control,
        };
        paragraph.Runs.Add(new Run($"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string> { ["Name"] = "Ada" });

        merged.PlainText.Should().Be("Ada");
        merged.Blocks[0].BlockContentControl.Should().Be(control);
    }

    [Fact]
    public void MergeAll_ProducesOneDocumentPerRow_InOrder()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Hello «Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Name"],
            [["Ada"], ["Grace"], ["Linus"]]);

        var merged = MailMerge.MergeAll(template, data);

        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Be("Hello Ada");
        merged[1].PlainText.Should().Be("Hello Grace");
        merged[2].PlainText.Should().Be("Hello Linus");
    }

    [Fact]
    public void MergeAll_EmptyData_YieldsEmptyList()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("«Name»"));

        MailMerge.MergeAll(template, MergeData.FromCsv(string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public void CombineMergedRecords_Letters_StartsEachAdditionalRecordOnANewPage()
    {
        var docs = new[]
        {
            new TextDocument { Blocks = { new Paragraph("Ada") } },
            new TextDocument { Blocks = { new Paragraph("Grace") } }
        };

        var combined = MailMerge.CombineMergedRecords(docs, MailMergeOutputMode.Letters);

        combined.Blocks.Should().HaveCount(2);
        ((Paragraph)combined.Blocks[0]).Formatting.PageBreakBefore.Should().BeFalse();
        ((Paragraph)combined.Blocks[1]).Formatting.PageBreakBefore.Should().BeTrue();
        combined.PlainText.Should().Be("Ada\nGrace");
    }

    [Fact]
    public void CombineMergedRecords_Directory_AppendsRecordsContinuously()
    {
        var docs = new[]
        {
            new TextDocument { Blocks = { new Paragraph("Ada") } },
            new TextDocument { Blocks = { new Paragraph("Grace") } }
        };

        var combined = MailMerge.CombineMergedRecords(docs, MailMergeOutputMode.Directory);

        combined.Blocks.Should().HaveCount(2);
        ((Paragraph)combined.Blocks[1]).Formatting.PageBreakBefore.Should().BeFalse();
        combined.PlainText.Should().Be("Ada\nGrace");
    }

    [Fact]
    public void SuggestEmailAddressField_PrefersCommonEmailHeaders()
    {
        MailMerge.SuggestEmailAddressField(["Name", "E-mail Address", "City"])
            .Should().Be("E-mail Address");
    }

    [Fact]
    public void CreateEmailDeliveryPlan_AllRecords_ValidatesDeliverableRows()
    {
        var data = new MergeData(
            ["Name", "Email"],
            [["Ada", "ada@example.test"], ["Grace", ""], ["Linus", "linus@example.test"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Newsletter",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.IsReady.Should().BeTrue();
        plan.RecordIndexes.Should().Equal(0, 1, 2);
        plan.DeliverableRecordIndexes.Should().Equal(0, 2);
        plan.Warnings.Should().ContainSingle().Which.Should().Contain("Record 2");
    }

    [Fact]
    public void CreateEmailDeliveryPlan_CurrentRecord_ClampsToRecipientRange()
    {
        var data = new MergeData(["Email"], [["a@example.test"], ["b@example.test"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Subject",
            MailMergeEmailOutputFormat.Attachment,
            MailMergeEmailBodyFormat.PlainText,
            MailMergeEmailRecordScope.CurrentRecord,
            CurrentRecordIndex: 99);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.RecordIndexes.Should().Equal(1);
        plan.DeliverableRecordIndexes.Should().Equal(1);
        plan.Intent.OutputFormat.Should().Be(MailMergeEmailOutputFormat.Attachment);
        plan.Intent.BodyFormat.Should().Be(MailMergeEmailBodyFormat.PlainText);
    }

    [Fact]
    public void CreateEmailDeliveryPlan_SelectedRecords_DeduplicatesAndWarnsForInvalidIndexes()
    {
        var data = new MergeData(["Email"], [["a@example.test"], ["b@example.test"], ["c@example.test"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Subject",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.SelectedRecords,
            SelectedRecordIndexes: [2, 0, 2, 5]);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.RecordIndexes.Should().Equal(2, 0);
        plan.DeliverableRecordIndexes.Should().Equal(2, 0);
        plan.Warnings.Should().Contain(message => message.Contains("outside the recipient list"));
    }

    [Fact]
    public void CreateEmailDeliveryPlan_MissingEmailField_IsBlockingValidation()
    {
        var data = new MergeData(["Name"], [["Ada"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.IsReady.Should().BeFalse();
        plan.Errors.Should().Contain(message => message.Contains("not in the recipient data source"));
        plan.Warnings.Should().Contain("Subject line is blank.");
    }

    [Fact]
    public void FromCsv_ParsesHeaderAndRows()
    {
        const string csv = "First,Last\nAda,Lovelace\nGrace,Hopper";

        var data = MergeData.FromCsv(csv);

        data.Header.Should().Equal("First", "Last");
        data.Count.Should().Be(2);
        data.Rows[0]["First"].Should().Be("Ada");
        data.Rows[0]["Last"].Should().Be("Lovelace");
        data.Rows[1]["First"].Should().Be("Grace");
    }

    [Fact]
    public void FromCsv_HonoursQuotedFields_WithEmbeddedCommasAndQuotes()
    {
        const string csv = "Name,Note\n\"Doe, Jane\",\"She said \"\"hi\"\"\"";

        var data = MergeData.FromCsv(csv);

        data.Rows.Should().HaveCount(1);
        data.Rows[0]["Name"].Should().Be("Doe, Jane");
        data.Rows[0]["Note"].Should().Be("She said \"hi\"");
    }

    [Fact]
    public void FromCsv_LookupIsCaseInsensitive()
    {
        var data = MergeData.FromCsv("Name\nAda");

        data.Rows[0]["name"].Should().Be("Ada");
        data.Rows[0]["NAME"].Should().Be("Ada");
    }

    [Fact]
    public void FromCsv_ShortRow_PadsMissingCellsWithEmpty()
    {
        var data = MergeData.FromCsv("A,B,C\n1,2");

        data.Rows[0]["A"].Should().Be("1");
        data.Rows[0]["B"].Should().Be("2");
        data.Rows[0]["C"].Should().Be(string.Empty);
    }

    [Fact]
    public void FromCsv_HandlesCrlfLineEndings()
    {
        var data = MergeData.FromCsv("First,Last\r\nAda,Lovelace\r\n");

        data.Count.Should().Be(1);
        data.Rows[0]["First"].Should().Be("Ada");
    }

    [Fact]
    public void EndToEnd_MergeAll_OverCsv_FillsTemplate()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("Dear «First» «Last»,"));

        var data = MergeData.FromCsv("First,Last\nAda,Lovelace\nGrace,Hopper");
        var docs = MailMerge.MergeAll(template, data);

        docs.Should().HaveCount(2);
        docs[0].PlainText.Should().Be("Dear Ada Lovelace,");
        docs[1].PlainText.Should().Be("Dear Grace Hopper,");
    }

    [Fact]
    public void MergeRecord_PreservesEndnoteAndCellMergeMarks()
    {
        // Regression: the clone path used to drop EndnoteId/HyperlinkTooltip from runs and
        // GridSpan/VerticalMerge from cells, orphaning endnotes and collapsing merged cells.
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.EndnoteReference(1));
        template.Blocks.Add(para);
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("m") { GridSpan = 2, VerticalMerge = VerticalMergeState.Restart };
        template.Blocks.Add(table);

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string>());

        merged.Paragraphs.First().Runs.Single().EndnoteId.Should().Be(1);
        var mergedTable = merged.Blocks.OfType<Table>().Single();
        mergedTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
        mergedTable.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
    }

    // ── FieldMapping / AutoMatchFields ───────────────────────────────────────────────────────────────

    [Fact]
    public void AutoMatchFields_MatchesFirstNameAndLastName_CaseInsensitive()
    {
        var mapping = MailMerge.AutoMatchFields(["first name", "LAST NAME", "Company"]);

        mapping[FieldRole.FirstName].Should().Be("first name");
        mapping[FieldRole.LastName].Should().Be("LAST NAME");
        mapping[FieldRole.Company].Should().Be("Company");
    }

    [Fact]
    public void AutoMatchFields_MatchesConcatenatedVariants()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName", "PostalCode"]);

        mapping[FieldRole.FirstName].Should().Be("FirstName");
        mapping[FieldRole.LastName].Should().Be("LastName");
        mapping[FieldRole.PostalCode].Should().Be("PostalCode");
    }

    [Fact]
    public void AutoMatchFields_UnmatchedRole_IsNull()
    {
        var mapping = MailMerge.AutoMatchFields(["Name"]);

        // "Name" alone matches neither FirstName nor LastName synonyms exactly.
        mapping[FieldRole.MiddleName].Should().BeNull();
        mapping[FieldRole.Suffix].Should().BeNull();
    }

    [Fact]
    public void AutoMatchFields_EmptyHeader_AllRolesNull()
    {
        var mapping = MailMerge.AutoMatchFields([]);

        foreach (FieldRole role in Enum.GetValues(typeof(FieldRole)))
            mapping[role].Should().BeNull($"role {role} should be unmatched for an empty header");
    }

    [Fact]
    public void AutoMatchFields_ZipSynonym_MatchesPostalCode()
    {
        var mapping = MailMerge.AutoMatchFields(["Zip"]);

        mapping[FieldRole.PostalCode].Should().Be("Zip");
    }

    [Fact]
    public void AutoMatchFields_AddressSynonym_MatchesAddress1()
    {
        var mapping = MailMerge.AutoMatchFields(["Address", "City", "State"]);

        mapping[FieldRole.Address1].Should().Be("Address");
        mapping[FieldRole.City].Should().Be("City");
        mapping[FieldRole.State].Should().Be("State");
    }

    // ── ComposeAddressBlock ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeAddressBlock_FullRecord_FormatsCorrectly()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName", "Company", "Address1", "City", "State", "PostalCode"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = "Ada", ["LastName"] = "Lovelace", ["Company"] = "Babbage Inc.",
            ["Address1"] = "1 Engine Way", ["City"] = "London", ["State"] = "England", ["PostalCode"] = "EC1A 1BB"
        };

        var block = MailMerge.ComposeAddressBlock(row, mapping);

        block.Should().Be("Ada Lovelace\nBabbage Inc.\n1 Engine Way\nLondon, England EC1A 1BB");
    }

    [Fact]
    public void ComposeAddressBlock_MissingCity_OmitsCityStateSeparator()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName", "State", "PostalCode"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = "Grace", ["LastName"] = "Hopper",
            ["State"] = "CT", ["PostalCode"] = "06830"
        };

        var block = MailMerge.ComposeAddressBlock(row, mapping);

        // No city → state is used alone on the city-state line (no leading comma).
        block.Should().Contain("CT 06830");
        block.Should().NotContain(", CT");
    }

    [Fact]
    public void ComposeAddressBlock_AllFieldsUnmapped_ReturnsEmpty()
    {
        var block = MailMerge.ComposeAddressBlock(
            new Dictionary<string, string>(),
            new FieldMapping());

        block.Should().BeEmpty();
    }

    [Fact]
    public void ComposeAddressBlock_WithCountry_AppendsCountryOnLastLine()
    {
        var mapping = new FieldMapping();
        mapping[FieldRole.FirstName] = "F";
        mapping[FieldRole.LastName]  = "L";
        mapping[FieldRole.Country]   = "Country";
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["F"] = "Marie", ["L"] = "Curie", ["Country"] = "France" };

        var block = MailMerge.ComposeAddressBlock(row, mapping);

        block.Should().EndWith("\nFrance");
    }

    // ── ComposeGreetingLine ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeGreetingLine_TitleAndLastName_UsesTitleLastNameForm()
    {
        var mapping = MailMerge.AutoMatchFields(["Title", "FirstName", "LastName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["Title"] = "Dr.", ["FirstName"] = "Ada", ["LastName"] = "Lovelace" };

        MailMerge.ComposeGreetingLine(row, mapping).Should().Be("Dear Dr. Lovelace,");
    }

    [Fact]
    public void ComposeGreetingLine_NoTitle_UsesFirstLastForm()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["FirstName"] = "Grace", ["LastName"] = "Hopper" };

        MailMerge.ComposeGreetingLine(row, mapping).Should().Be("Dear Grace Hopper,");
    }

    [Fact]
    public void ComposeGreetingLine_NoNameFields_FallsBackToSirOrMadam()
    {
        MailMerge.ComposeGreetingLine(
            new Dictionary<string, string>(),
            new FieldMapping())
            .Should().Be("Dear Sir or Madam,");
    }

    [Fact]
    public void ComposeGreetingLine_CustomGreetingPrefix()
    {
        var mapping = MailMerge.AutoMatchFields(["LastName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["LastName"] = "Turing" };

        MailMerge.ComposeGreetingLine(row, mapping, greetingFormat: "Hello").Should().Be("Hello Turing,");
    }

    [Fact]
    public void ComposeGreetingLine_OnlyFirstName_UsesFirstName()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["FirstName"] = "Linus" };

        MailMerge.ComposeGreetingLine(row, mapping).Should().Be("Dear Linus,");
    }

    // ── SubstituteSpecial (Next Record / Merge Record #) ────────────────────────────────────────────

    [Fact]
    public void SubstituteSpecial_MergeRecordNumber_InjectsOneBasedIndex()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial(
            $"Record {MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
            row, recordIndex: 3, out var advance);

        result.Should().Be("Record 3");
        advance.Should().BeFalse();
    }

    [Fact]
    public void SubstituteSpecial_NextRecord_SetsAdvanceFlagAndProducesNoOutput()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial(
            $"A{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}B",
            row, recordIndex: 1, out var advance);

        // «Next Record» emits nothing (only the surrounding literal text remains).
        result.Should().Be("AB");
        advance.Should().BeTrue();
    }

    [Fact]
    public void SubstituteSpecial_NextRecord_CaseInsensitive()
    {
        var row = new Dictionary<string, string>();
        MailMerge.SubstituteSpecial(
            $"{MailMerge.FieldOpen}NEXT RECORD{MailMerge.FieldClose}",
            row, recordIndex: 1, out var advance);

        advance.Should().BeTrue();
    }

    [Fact]
    public void SubstituteSpecial_MergeRecordNumber_CaseInsensitive()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial(
            $"{MailMerge.FieldOpen}merge record #{MailMerge.FieldClose}",
            row, recordIndex: 7, out _);

        result.Should().Be("7");
    }

    [Fact]
    public void SubstituteSpecial_RegularField_StillSubstituted()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Ada" };
        var result = MailMerge.SubstituteSpecial(
            $"Hi {MailMerge.FieldOpen}Name{MailMerge.FieldClose}",
            row, recordIndex: 1, out _);

        result.Should().Be("Hi Ada");
    }

    [Fact]
    public void SubstituteSpecial_NoPlaceholders_ReturnsSameString()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial("plain text", row, recordIndex: 1, out var advance);

        result.Should().Be("plain text");
        advance.Should().BeFalse();
    }

    // ── FieldMapping accessors ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void FieldMapping_SetAndGetRoundTrips()
    {
        var m = new FieldMapping();
        m[FieldRole.City] = "MyCityColumn";

        m[FieldRole.City].Should().Be("MyCityColumn");
        m[FieldRole.State].Should().BeNull("unmapped role returns null");
    }

    [Fact]
    public void FieldMapping_SetToNull_UnmapsRole()
    {
        var m = new FieldMapping();
        m[FieldRole.Country] = "Country";
        m[FieldRole.Country] = null;

        m[FieldRole.Country].Should().BeNull();
    }

    // ── MergeRuleEvaluator — If…Then…Else ───────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_IfEqual_TrueCondition_EmitsTrueText()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Status"] = "VIP" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Status", MergeConditionOperator.Equal, "VIP", "Gold", "Standard");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result.Should().NotBeNull();
        result!.Value.Text.Should().Be("Gold");
        result.Value.SkipRecord.Should().BeFalse();
        result.Value.AdvanceRecord.Should().BeFalse();
    }

    [Fact]
    public void MergeRuleEvaluator_IfEqual_FalseCondition_EmitsFalseText()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Status"] = "Regular" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Status", MergeConditionOperator.Equal, "VIP", "Gold", "Standard");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Standard");
    }

    [Fact]
    public void MergeRuleEvaluator_IfNotEqual_EmitsCorrectBranch()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Country"] = "UK" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Country", MergeConditionOperator.NotEqual, "US", "International", "Domestic");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("International");
    }

    [Fact]
    public void MergeRuleEvaluator_IfLessThan_NumericComparison()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Score"] = "45" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Score", MergeConditionOperator.LessThan, "50", "Low", "High");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Low");
    }

    [Fact]
    public void MergeRuleEvaluator_IfGreaterThanOrEqual_NumericComparison()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Score"] = "100" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Score", MergeConditionOperator.GreaterThanOrEqual, "100", "Perfect", "Not perfect");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Perfect");
    }

    [Fact]
    public void MergeRuleEvaluator_IfIsBlank_TrueWhenFieldEmpty()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["MiddleName"] = "" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("MiddleName", MergeConditionOperator.IsBlank, "", "No middle name", "Has middle name");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("No middle name");
    }

    [Fact]
    public void MergeRuleEvaluator_IfIsNotBlank_TrueWhenFieldPopulated()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Title"] = "Dr." };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Title", MergeConditionOperator.IsNotBlank, "", "Dr. ", "");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Dr. ");
    }

    [Fact]
    public void MergeRuleEvaluator_IfContains_CaseInsensitiveSubstring()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Notes"] = "Premium subscriber since 2020" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Notes", MergeConditionOperator.Contains, "premium", "VIP", "Regular");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("VIP");
    }

    [Fact]
    public void MergeRuleEvaluator_IfMissingField_TreatedAsBlank()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("NoSuchField", MergeConditionOperator.IsBlank, "", "Blank", "NotBlank");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Blank");
    }

    // ── MergeRuleEvaluator — Skip Record If ─────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_SkipRecordIf_ConditionTrue_MarksSkipAndSetsFlag()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Opted Out"] = "Yes" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSkipRecordIfInstruction("Opted Out", MergeConditionOperator.Equal, "Yes");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 2);

        result!.Value.SkipRecord.Should().BeTrue();
        result.Value.Text.Should().BeEmpty();
        state.SkippedIndices.Should().Contain(2);
    }

    [Fact]
    public void MergeRuleEvaluator_SkipRecordIf_ConditionFalse_DoesNotSkip()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Opted Out"] = "No" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSkipRecordIfInstruction("Opted Out", MergeConditionOperator.Equal, "Yes");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 2);

        result!.Value.SkipRecord.Should().BeFalse();
        state.SkippedIndices.Should().BeEmpty();
    }

    // ── MergeRuleEvaluator — Next Record If ─────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_NextRecordIf_ConditionTrue_SetsAdvanceFlag()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Type"] = "Header" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.AdvanceRecord.Should().BeTrue();
        result.Value.SkipRecord.Should().BeFalse();
    }

    [Fact]
    public void MergeRuleEvaluator_NextRecordIf_ConditionFalse_NoAdvance()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Type"] = "Data" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.AdvanceRecord.Should().BeFalse();
    }

    // ── MergeRuleEvaluator — Merge Sequence # ───────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_MergeSequenceNumber_EmitsCurrentSequenceNumber()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState { SequenceNumber = 3 };
        var instruction = "Merge Sequence #";

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("3");
    }

    [Fact]
    public void MergeRuleEvaluator_MergeSequenceNumber_CaseInsensitive()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState { SequenceNumber = 7 };

        var result = MergeRuleEvaluator.Evaluate("merge sequence #", row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("7");
    }

    // ── MergeRuleEvaluator — Set / Ref Bookmark ─────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_SetBookmark_StoresValueInState()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Region"] = "EMEA" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSetInstruction("MyBookmark", "fixed value");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().BeEmpty();
        state.Bookmarks["MyBookmark"].Should().Be("fixed value");
    }

    [Fact]
    public void MergeRuleEvaluator_RefBookmark_EmitsStoredValue()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        state.Bookmarks["Greeting"] = "Hello, friend";
        var instruction = MergeRuleEvaluator.BuildRefInstruction("Greeting");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Hello, friend");
    }

    [Fact]
    public void MergeRuleEvaluator_RefBookmark_MissingBookmark_EmitsEmpty()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildRefInstruction("NoSuchBookmark");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().BeEmpty();
    }

    // ── MergeRuleEvaluator — Fill-in / Ask ──────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_FillIn_EmitsPrePopulatedAnswer()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        state.FillInAnswers["Enter your name:"] = "John Smith";
        var instruction = MergeRuleEvaluator.BuildFillInInstruction("Enter your name:");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("John Smith");
    }

    [Fact]
    public void MergeRuleEvaluator_FillIn_MissingAnswer_EmitsEmpty()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildFillInInstruction("What is your department?");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().BeEmpty();
    }

    [Fact]
    public void MergeRuleEvaluator_Ask_StoresAnswerAsBookmarkAndEmitsIt()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        state.AskAnswers["Manager"] = "Alice";
        var instruction = MergeRuleEvaluator.BuildAskInstruction("Manager", "Who is the manager?");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Alice");
        state.Bookmarks["Manager"].Should().Be("Alice");
    }

    // ── MergeRuleEvaluator — unrecognised instruction ────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_UnrecognisedInstruction_ReturnsNull()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Ada" };
        var state = new MergeState();

        var result = MergeRuleEvaluator.Evaluate("Name", row, state, recordIndex: 0);

        result.Should().BeNull("plain merge-field names are not rule instructions");
    }

    // ── MergeRuleEvaluator.EvaluateCondition — standalone operator tests ─────────────────────────

    [Theory]
    [InlineData("apple", MergeConditionOperator.Equal, "apple", true)]
    [InlineData("apple", MergeConditionOperator.Equal, "Apple", true)]   // case-insensitive
    [InlineData("apple", MergeConditionOperator.NotEqual, "banana", true)]
    [InlineData("10",    MergeConditionOperator.LessThan, "20", true)]
    [InlineData("20",    MergeConditionOperator.LessThan, "10", false)]
    [InlineData("10",    MergeConditionOperator.LessThanOrEqual, "10", true)]
    [InlineData("15",    MergeConditionOperator.GreaterThan, "10", true)]
    [InlineData("10",    MergeConditionOperator.GreaterThanOrEqual, "10", true)]
    [InlineData("",      MergeConditionOperator.IsBlank, "", true)]
    [InlineData("  ",    MergeConditionOperator.IsBlank, "", true)]
    [InlineData("x",     MergeConditionOperator.IsBlank, "", false)]
    [InlineData("x",     MergeConditionOperator.IsNotBlank, "", true)]
    [InlineData("Hello World", MergeConditionOperator.Contains, "world", true)]
    [InlineData("Hello World", MergeConditionOperator.Contains, "xyz", false)]
    public void EvaluateCondition_OperatorCases(string fieldValue, MergeConditionOperator op, string value, bool expected)
    {
        MergeRuleEvaluator.EvaluateCondition(fieldValue, op, value).Should().Be(expected);
    }

    // ── MailMerge.MergeAllWithRules — integration tests ──────────────────────────────────────────

    [Fact]
    public void MergeAllWithRules_SkipRecordIf_ExcludesMatchingRecords()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        // First run: Skip Record If Type = Header
        para.Runs.Add(new Run($"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildSkipRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header")}{MailMerge.FieldClose}"));
        para.Runs.Add(new Run("«Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Type", "Name"],
            [["Header", "Section A"], ["Data", "Alice"], ["Data", "Bob"], ["Header", "Section B"], ["Data", "Carol"]]);

        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        // Header records (indices 0 and 3) should be skipped.
        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Contain("Alice");
        merged[1].PlainText.Should().Contain("Bob");
        merged[2].PlainText.Should().Contain("Carol");
        state.SkippedIndices.Should().BeEquivalentTo([0, 3]);
    }

    [Fact]
    public void MergeAllWithRules_PreservesBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var template = new TextDocument();
        var paragraph = new Paragraph
        {
            BlockContentControl = control,
        };
        paragraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildIfInstruction("Tier", MergeConditionOperator.Equal, "VIP", "Priority", "Standard")}{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);
        var data = new MergeData(["Tier"], [["VIP"]]);

        var merged = MailMerge.MergeAllWithRules(template, data, new MergeState());

        merged.Should().ContainSingle();
        merged[0].PlainText.Should().Be("Priority");
        merged[0].Blocks[0].BlockContentControl.Should().Be(control);
    }

    [Fact]
    public void MergeAllWithRules_MergeSequenceNumber_CountsNonSkippedRecords()
    {
        // Template: «Skip Record If Type = Header»«Merge Sequence #» «Name»
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildSkipRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header")}{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose} «Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Type", "Name"],
            [["Header", "Ignored"], ["Data", "Alice"], ["Data", "Bob"]]);

        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        // Record 0 (Header) is skipped; Alice is sequence 1, Bob is sequence 2.
        merged.Should().HaveCount(2);
        merged[0].PlainText.Should().Contain("1 Alice");
        merged[1].PlainText.Should().Contain("2 Bob");
    }

    [Fact]
    public void MergeAllWithRules_SetAndRefBookmark_ResolveAcrossRuns()
    {
        // Template: «Set Region "EMEA"»Dear «Name», your region is «Ref Region».
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildSetInstruction("Region", "EMEA")}{MailMerge.FieldClose}" +
            $"Dear «Name», your region is {MailMerge.FieldOpen}{MergeRuleEvaluator.BuildRefInstruction("Region")}{MailMerge.FieldClose}."));
        template.Blocks.Add(para);

        var data = new MergeData(["Name"], [["Ada"], ["Grace"]]);
        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Should().HaveCount(2);
        merged[0].PlainText.Should().Be("Dear Ada, your region is EMEA.");
        merged[1].PlainText.Should().Be("Dear Grace, your region is EMEA.");
    }

    [Fact]
    public void MergeAllWithRules_IfThenElse_EmitsCorrectBranchPerRecord()
    {
        // Template: «If Status = VIP Then "Gold treatment" Else "Standard treatment"»
        var template = new TextDocument();
        var para = new Paragraph();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Status", MergeConditionOperator.Equal, "VIP", "Gold treatment", "Standard treatment");
        para.Runs.Add(new Run($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}"));
        template.Blocks.Add(para);

        var data = new MergeData(["Status"], [["VIP"], ["Regular"], ["VIP"]]);
        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Be("Gold treatment");
        merged[1].PlainText.Should().Be("Standard treatment");
        merged[2].PlainText.Should().Be("Gold treatment");
    }

    [Fact]
    public void MergeAllWithRules_FillIn_UsesPrePopulatedAnswer()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(
            $"Hello «Name», {MailMerge.FieldOpen}{MergeRuleEvaluator.BuildFillInInstruction("Department:")}{MailMerge.FieldClose}"));
        template.Blocks.Add(para);

        var data = new MergeData(["Name"], [["Ada"], ["Grace"]]);
        var state = new MergeState();
        state.FillInAnswers["Department:"] = "Engineering";

        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Should().HaveCount(2);
        merged[0].PlainText.Should().Be("Hello Ada, Engineering");
        merged[1].PlainText.Should().Be("Hello Grace, Engineering");
    }

    [Fact]
    public void MergeRecordWithRules_NextRecordIf_PreservesAdvanceRequestForCaller()
    {
        var template = new TextDocument();
        var paragraph = new Paragraph();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction(
            "Type", MergeConditionOperator.Equal, "Header");
        paragraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);

        var state = new MergeState();
        var merged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string> { ["Type"] = "Header", ["Name"] = "Section A" },
            state,
            recordIndex: 1);

        merged.PlainText.Should().Be("Section A");
        state.AdvanceRecordRequested.Should().BeTrue();
        state.SkipRecordRequested.Should().BeFalse();
    }

    [Fact]
    public void MergeRecordWithRules_ResetsPriorRecordOutcomeBeforeCloning()
    {
        var advanceTemplate = new TextDocument();
        var advanceParagraph = new Paragraph();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction(
            "Type", MergeConditionOperator.Equal, "Header");
        advanceParagraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}"));
        advanceTemplate.Blocks.Add(advanceParagraph);

        var plainTemplate = new TextDocument();
        var plainParagraph = new Paragraph();
        plainParagraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        plainTemplate.Blocks.Add(plainParagraph);

        var state = new MergeState();
        var header = new Dictionary<string, string> { ["Type"] = "Header", ["Name"] = "Section A" };
        MailMerge.MergeRecordWithRules(advanceTemplate, header, state, recordIndex: 1);
        state.AdvanceRecordRequested.Should().BeTrue();

        var data = new Dictionary<string, string> { ["Type"] = "Data", ["Name"] = "Ada" };
        var merged = MailMerge.MergeRecordWithRules(plainTemplate, data, state, recordIndex: 2);

        merged.PlainText.Should().Be("Ada");
        state.AdvanceRecordRequested.Should().BeFalse();
        state.SkipRecordRequested.Should().BeFalse();
    }

    // ── SubstituteSpecialWithRules — unit tests ──────────────────────────────────────────────────

    [Fact]
    public void SubstituteSpecialWithRules_MergeSequenceNumber_EmitsSequenceNumber()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState { SequenceNumber = 5 };

        var result = MailMerge.SubstituteSpecialWithRules(
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}",
            row, state, recordIndex: 7, out var advance, out var skip);

        result.Should().Be("5");
        advance.Should().BeFalse();
        skip.Should().BeFalse();
    }

    [Fact]
    public void SubstituteSpecialWithRules_SkipRule_SetsSkipFlag()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X"] = "Y" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSkipRecordIfInstruction("X", MergeConditionOperator.Equal, "Y");

        MailMerge.SubstituteSpecialWithRules(
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}",
            row, state, recordIndex: 1, out _, out var skip);

        skip.Should().BeTrue();
    }

    [Fact]
    public void SubstituteSpecialWithRules_MergeRecordNumber_StillWorks()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();

        var result = MailMerge.SubstituteSpecialWithRules(
            $"Record {MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
            row, state, recordIndex: 4, out _, out _);

        result.Should().Be("Record 4");
    }
}
