using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class FieldDisplayParityTests
{
    [Fact]
    public void ToggleFieldCodeAtCaret_FlipsOnlyTheCurrentField()
    {
        var first = Run.ComplexFieldRun(" FIRST ", "First result");
        first.Formatting = RunFormatting.Default with { ColorHex = "#C00000" };
        var second = Run.ComplexFieldRun(" SECOND ", "Second result");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Before "), first, new Run(" / "), second }
        });
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(0, "Before ".Length + 2);

        view.ToggleFieldCodeAtCaret();

        first.ComplexField!.ShowCode.Should().BeTrue();
        second.ComplexField!.ShowCode.Should().BeFalse();

        view.ToggleFieldCodeAtCaret();

        first.ComplexField!.ShowCode.Should().BeFalse();
        first.Text.Should().Be("First result");
        first.Formatting.ColorHex.Should().Be("#C00000");
        second.ComplexField!.ShowCode.Should().BeFalse();
    }

    [Fact]
    public void UnlinkFieldAtCaret_PreservesResultAndFormatting_AndLeavesNeighborField()
    {
        var first = Run.ComplexFieldRun(
            " FIRST ",
            "First result",
            showCode: true,
            formatting: RunFormatting.Default with { ColorHex = "#C00000" });
        var second = Run.ComplexFieldRun(" SECOND ", "Second result");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Before "), first, new Run(" / "), second }
        });
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(0, "Before ".Length + 2);

        view.UnlinkFieldAtCaret();

        first.Text.Should().Be("First result");
        first.ComplexField.Should().BeNull();
        first.Formatting.ColorHex.Should().Be("#C00000");
        second.ComplexField!.Instruction.Should().Be(" SECOND ");
    }

    [Fact]
    public void SetFieldLockAtCaret_ChangesOnlyCurrentField_AndPreservesDirtyState()
    {
        var first = Run.ComplexFieldRun(
            " FIRST ",
            "First result",
            sequence: new ComplexFieldSequenceMetadata(IsLocked: false, IsDirty: true));
        var second = Run.ComplexFieldRun(" SECOND ", "Second result");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Before "), first, new Run(" / "), second }
        });
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(0, "Before ".Length + 2);

        view.SetFieldLockAtCaret(true);

        first.ComplexField!.Sequence
            .Should().Be(new ComplexFieldSequenceMetadata(IsLocked: true, IsDirty: true));
        second.ComplexField!.IsLocked.Should().BeFalse();

        view.SetFieldLockAtCaret(false);

        first.ComplexField!.Sequence
            .Should().Be(new ComplexFieldSequenceMetadata(IsLocked: false, IsDirty: true));
        second.ComplexField!.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void SelectedFieldCommands_ToggleLockAndUnlinkOnlyIntersectingFields()
    {
        var first = Run.ComplexFieldRun(" FIRST ", "First result");
        var second = Run.ComplexFieldRun(" SECOND ", "Second result");
        var third = Run.ComplexFieldRun(" THIRD ", "Third result");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Before "), first, new Run(" / "), second, new Run(" / "), third
            }
        });
        var view = new DocumentView();
        view.LoadDocument(document);

        void SelectFirstTwoFields()
        {
            var firstLength = ComplexFieldDisplayPlanner.Build(first.ComplexField!, first.Text, document).Text.Length;
            var secondLength = ComplexFieldDisplayPlanner.Build(second.ComplexField!, second.Text, document).Text.Length;
            var start = "Before ".Length;
            var end = start + firstLength + " / ".Length + secondLength;
            view.SetSelectionRangePublic(0, start, 0, end);
        }

        SelectFirstTwoFields();
        view.ToggleFieldCodeAtCaret();

        first.ComplexField!.ShowCode.Should().BeTrue();
        second.ComplexField!.ShowCode.Should().BeTrue();
        third.ComplexField!.ShowCode.Should().BeFalse();

        SelectFirstTwoFields();
        view.SetFieldLockAtCaret(true);

        first.ComplexField!.IsLocked.Should().BeTrue();
        second.ComplexField!.IsLocked.Should().BeTrue();
        third.ComplexField!.IsLocked.Should().BeFalse();

        SelectFirstTwoFields();
        view.UnlinkFieldAtCaret();

        first.ComplexField.Should().BeNull();
        first.Text.Should().Be("First result");
        second.ComplexField.Should().BeNull();
        second.Text.Should().Be("Second result");
        third.ComplexField.Should().NotBeNull();
        third.Text.Should().Be("Third result");
    }

    [Fact]
    public void UpdateFieldAtCaret_RefreshesOnlyFieldsInSameCellTextSelection()
    {
        var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "Stale title");
        var subject = Run.ComplexFieldRun(" DOCPROPERTY Subject ", "Stale subject");
        var author = Run.ComplexFieldRun(" DOCPROPERTY Author ", "Stale author");
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Current title";
        document.Properties.Subject = "Current subject";
        document.Properties.Author = "Current author";
        document.Blocks.Clear();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[0].Paragraphs[0].Runs.AddRange(
        [
            new Run("Before "), title, new Run(" / "), subject, new Run(" / "), author
        ]);
        document.Blocks.Add(table);
        var view = new DocumentView();
        view.LoadDocument(document);
        var start = "Before ".Length;
        var end = start + title.Text.Length + " / ".Length + subject.Text.Length;
        view.SetCellTextSelectionForTest(
            0,
            anchorRow: 0,
            anchorCol: 0,
            anchorParaIdx: 0,
            anchorOffset: start,
            caretRow: 0,
            caretCol: 0,
            caretParaIdx: 0,
            caretOffset: end);

        view.UpdateFieldAtCaret();

        title.Text.Should().Be("Current title");
        subject.Text.Should().Be("Current subject");
        author.Text.Should().Be("Stale author");
    }

    [Fact]
    public void SelectedFieldCommands_ApplyAcrossLogicalCells_AndExcludeBoundaryCell()
    {
        var first = Run.ComplexFieldRun(" FIRST ", "First result");
        var second = Run.ComplexFieldRun(" SECOND ", "Second result");
        var third = Run.ComplexFieldRun(" THIRD ", "Third result");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(first);
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(second);
        table.Rows[0].Cells[2].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[2].Paragraphs[0].Runs.Add(third);
        document.Blocks.Add(table);
        var view = new DocumentView();
        view.LoadDocument(document);

        void SelectFirstTwoCells()
        {
            view.SetCellTextSelectionForTest(
                0,
                anchorRow: 0,
                anchorCol: 0,
                anchorParaIdx: 0,
                anchorOffset: 0,
                caretRow: 0,
                caretCol: 2,
                caretParaIdx: 0,
                caretOffset: 0);
        }

        SelectFirstTwoCells();
        view.ToggleFieldCodeAtCaret();
        first.ComplexField!.ShowCode.Should().BeTrue();
        second.ComplexField!.ShowCode.Should().BeTrue();
        third.ComplexField!.ShowCode.Should().BeFalse();

        SelectFirstTwoCells();
        view.SetFieldLockAtCaret(true);
        first.ComplexField!.IsLocked.Should().BeTrue();
        second.ComplexField!.IsLocked.Should().BeTrue();
        third.ComplexField!.IsLocked.Should().BeFalse();

        SelectFirstTwoCells();
        view.UnlinkFieldAtCaret();
        first.ComplexField.Should().BeNull();
        second.ComplexField.Should().BeNull();
        third.ComplexField.Should().NotBeNull();
    }

    [Fact]
    public void UpdateFieldAtCaret_RefreshesFieldsInRectangularCellSelection()
    {
        var first = Run.ComplexFieldRun(" DOCPROPERTY Title ", "Stale title");
        var second = Run.ComplexFieldRun(" DOCPROPERTY Subject ", "Stale subject");
        var third = Run.ComplexFieldRun(" DOCPROPERTY Author ", "Stale author");
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Current title";
        document.Properties.Subject = "Current subject";
        document.Properties.Author = "Current author";
        document.Blocks.Clear();
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(first);
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(second);
        table.Rows[0].Cells[2].Paragraphs[0].Runs.Clear();
        table.Rows[0].Cells[2].Paragraphs[0].Runs.Add(third);
        document.Blocks.Add(table);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetCellBlockSelection(0, anchorRow: 0, anchorCol: 0, focusRow: 0, focusCol: 1);

        view.UpdateFieldAtCaret();

        first.Text.Should().Be("Current title");
        second.Text.Should().Be("Current subject");
        third.Text.Should().Be("Stale author");
    }

    [Fact]
    public void UpdateFieldAtCaret_RefreshesOnlyTheCurrentComplexField()
    {
        var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "Stale title");
        var subject = Run.ComplexFieldRun(" DOCPROPERTY Subject ", "Stale subject");
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Current title";
        document.Properties.Subject = "Current subject";
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Before "), title, new Run(" / "), subject }
        });
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(0, "Before ".Length + 2);

        view.UpdateFieldAtCaret();

        title.Text.Should().Be("Current title");
        subject.Text.Should().Be("Stale subject");
    }

    [Fact]
    public void UpdateFieldAtCaret_RefreshesSelectedComplexFields_AndLeavesLockedOrUnselectedFields()
    {
        var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "Stale title");
        var subject = Run.ComplexFieldRun(
            " DOCPROPERTY Subject ",
            "Stale subject",
            sequence: new ComplexFieldSequenceMetadata(IsLocked: true, IsDirty: true));
        var author = Run.ComplexFieldRun(" DOCPROPERTY Author ", "Stale author");
        var keywords = Run.ComplexFieldRun(" DOCPROPERTY Keywords ", "Stale keywords");
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Current title";
        document.Properties.Subject = "Current subject";
        document.Properties.Author = "Current author";
        document.Properties.Keywords = "Current keywords";
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Before "), title, new Run(" / "), subject, new Run(" / "), author,
                new Run(" / "), keywords
            }
        });
        var view = new DocumentView();
        view.LoadDocument(document);
        var selectionStart = "Before ".Length;
        var selectionEnd = selectionStart
            + title.Text.Length
            + " / ".Length
            + subject.Text.Length
            + " / ".Length
            + author.Text.Length;
        view.SetSelectionRangePublic(0, selectionStart, 0, selectionEnd);

        view.UpdateFieldAtCaret();

        title.Text.Should().Be("Current title");
        subject.Text.Should().Be("Stale subject");
        subject.ComplexField!.IsLocked.Should().BeTrue();
        author.Text.Should().Be("Current author");
        keywords.Text.Should().Be("Stale keywords");
    }

    [Fact]
    public void InsertComplexField_Formula_ComputesInitialResult()
    {
        var document = TextDocument.CreateEmpty();
        var view = new DocumentView();
        view.LoadDocument(document);

        view.InsertComplexField(" =2*(3+4) \\# \"0.00\" ");

        var run = document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(candidate => candidate.ComplexField?.Keyword == "=");
        run.Text.Should().Be("14.00");
    }

    [Fact]
    public void InsertComplexField_Template_ResolvesResultFromExtendedProperties()
    {
        var document = TextDocument.CreateEmpty();
        document.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Template>Proposal.dotx</Template>
                </Properties>
                """)));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.InsertComplexField("TEMPLATE");

        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "TEMPLATE")
            .Text.Should().Be("Proposal.dotx");
    }

    [Fact]
    public void InsertComplexField_RevisionNumber_ResolvesResultFromCoreProperties()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var document = TextDocument.CreateEmpty();
        document.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "revision", "12"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.InsertComplexField("REVNUM");

        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "REVNUM")
            .Text.Should().Be("12");
    }

    [Fact]
    public void InsertComplexField_EditTime_ResolvesMinutesFromExtendedProperties()
    {
        var document = TextDocument.CreateEmpty();
        document.Preserved.Parts.Add(new PreservedPart(
            Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesPartName,
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <TotalTime>135</TotalTime>
                </Properties>
                """)));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.InsertComplexField("EDITTIME");

        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "EDITTIME")
            .Text.Should().Be("135");
    }

    [Fact]
    public void InsertComplexField_PrintDate_ResolvesTimestampFromCoreProperties()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var document = TextDocument.CreateEmpty();
        document.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "lastPrinted", "2026-08-07T14:05:00Z"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.InsertComplexField("PRINTDATE \\@ \"yyyy-MM-dd HH:mm\"");

        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "PRINTDATE")
            .Text.Should().Be(
                new DateTimeOffset(2026, 8, 7, 14, 5, 0, TimeSpan.Zero)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public void UpdateFields_DoesNotRecomputeLockedImportedSimpleField()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        var field = new Run("Locked chapter")
        {
            ComplexField = new ComplexField(
                " STYLEREF 1 ",
                SimpleField: new SimpleFieldMetadata(IsLocked: true, IsDirty: true))
        };
        document.Blocks.Add(new Paragraph { Runs = { field } });

        var view = new DocumentView();
        view.LoadDocument(document);
        view.UpdateFields();

        field.Text.Should().Be("Locked chapter");
        field.ComplexField!.SimpleField.Should().Be(new SimpleFieldMetadata(true, true));
    }

    [Fact]
    public void UpdateFields_HonorsComplexSequenceLockAndStillUpdatesUnlockedControl()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        var locked = Run.ComplexFieldRun(
            " STYLEREF 1 ",
            "Locked chapter",
            sequence: new ComplexFieldSequenceMetadata(IsLocked: true, IsDirty: true));
        var unlocked = Run.ComplexFieldRun(" STYLEREF 1 ", "Stale chapter");
        document.Blocks.Add(new Paragraph { Runs = { locked, new Run(" | "), unlocked } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        locked.Text.Should().Be("Locked chapter");
        locked.ComplexField!.Sequence
            .Should().Be(new ComplexFieldSequenceMetadata(true, true));
        unlocked.Text.Should().Be("Chapter Two");
        unlocked.ComplexField!.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void UpdateFields_DistinguishesDateAndTimeForSimpleAndComplexFields()
    {
        var simpleDate = new Run("stale simple date") { FieldKind = RunFieldKind.Date };
        var simpleTime = new Run("stale simple time") { FieldKind = RunFieldKind.Time };
        var complexDate = Run.ComplexFieldRun(" DATE ", "stale complex date");
        var complexTime = Run.ComplexFieldRun(" TIME ", "stale complex time");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs = { simpleDate, simpleTime, complexDate, complexTime }
        });
        var before = DateTime.Now;

        var view = new DocumentView();
        view.LoadDocument(document);
        view.UpdateFields();

        var after = DateTime.Now;
        var expectedDates = new[] { before, after }
            .Select(value => ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(RunFieldKind.Date, value))
            .Distinct()
            .ToArray();
        var expectedTimes = new[] { before, after }
            .Select(value => ComplexFieldDisplayPlanner.FormatInvariantTemporalValue(RunFieldKind.Time, value))
            .Distinct()
            .ToArray();

        simpleDate.Text.Should().BeOneOf(expectedDates);
        complexDate.Text.Should().BeOneOf(expectedDates);
        simpleTime.Text.Should().BeOneOf(expectedTimes);
        complexTime.Text.Should().BeOneOf(expectedTimes);
        simpleTime.Text.Should().Be(complexTime.Text);
        simpleTime.Text.Should().MatchRegex(@"^\d{1,2}:\d{2} (AM|PM)$");
        simpleDate.Text.Should().MatchRegex(@"^\d{1,2}/\d{1,2}/\d{4}$");
    }

    [Fact]
    public void UpdateFields_AppliesDateAndTimePictureSwitches()
    {
        var before = DateTime.Now;
        var date = Run.ComplexFieldRun(" DATE \\@ \"yyyy-MM-dd\" ", "stale date");
        var time = Run.ComplexFieldRun(" TIME \\@ \"HH:mm\" ", "stale time");
        var created = Run.ComplexFieldRun(" CREATEDATE \\@ \"yyyy-MM-dd\" ", "stale created");
        var saved = Run.ComplexFieldRun(" SAVEDATE \\@ \"yyyy-MM-dd HH:mm\" ", "stale saved");
        var owner = Run.ComplexFieldRun(" LASTSAVEDBY ", "stale owner");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var metadataMoment = new DateTime(2026, 8, 6, 14, 5, 0);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(metadataMoment);
        document.Properties.Created = new DateTimeOffset(metadataMoment, localOffset);
        document.Properties.Modified = new DateTimeOffset(metadataMoment.AddDays(2), localOffset);
        document.Properties.LastModifiedBy = "Ada Lovelace";
        document.Blocks.Add(new Paragraph { Runs = { date, time, created, saved, owner } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        var after = DateTime.Now;
        date.Text.Should().BeOneOf(before.ToString("yyyy-MM-dd"), after.ToString("yyyy-MM-dd"));
        time.Text.Should().BeOneOf(before.ToString("HH:mm"), after.ToString("HH:mm"));
        created.Text.Should().Be("2026-08-06");
        saved.Text.Should().Be("2026-08-08 14:05");
        owner.Text.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void UpdateFields_RefreshesDocPropertyAndDocVariableFromDocumentPackageState()
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var relationships = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "stale title");
        var company = Run.ComplexFieldRun(" DOCPROPERTY Company ", "stale company");
        var manager = Run.ComplexFieldRun(" DOCPROPERTY Manager ", "stale manager");
        var templateProperty = Run.ComplexFieldRun(" DOCPROPERTY Template ", "stale property template");
        var template = Run.ComplexFieldRun(" TEMPLATE ", "stale template");
        var templatePath = Run.ComplexFieldRun(" TEMPLATE \\p ", @"C:\Templates\Proposal.dotx");
        var channel = Run.ComplexFieldRun(" DOCVARIABLE Channel ", "stale channel");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Properties.Title = "Current title";
        document.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "attachedTemplate",
                new System.Xml.Linq.XAttribute(relationships + "id", "rIdTemplate")),
            new System.Xml.Linq.XElement(
                word + "docVars",
                new System.Xml.Linq.XElement(
                    word + "docVar",
                    new System.Xml.Linq.XAttribute(word + "name", "Channel"),
                    new System.Xml.Linq.XAttribute(word + "val", "Preview"))));
        document.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Company>Contoso Research</Company>
                  <Manager>Ada Lovelace</Manager>
                  <Template>Proposal.dotx</Template>
                </Properties>
                """)));
        document.Preserved.Parts.Add(new PreservedPart(
            "/word/_rels/settings.xml.rels",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdTemplate" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file:///C:/Templates/Current.dotx" TargetMode="External"/>
                </Relationships>
                """)));
        document.Blocks.Add(new Paragraph
        {
            Runs = { title, company, manager, templateProperty, template, templatePath, channel }
        });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        title.Text.Should().Be("Current title");
        company.Text.Should().Be("Contoso Research");
        manager.Text.Should().Be("Ada Lovelace");
        templateProperty.Text.Should().Be("Proposal.dotx");
        template.Text.Should().Be("Proposal.dotx");
        templatePath.Text.Should().Be(@"C:\Templates\Current.dotx");
        channel.Text.Should().Be("Preview");
    }

    [Fact]
    public void UpdateFields_RefreshesDocumentStatisticsInStoryOrder()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello world."));
        var numChars = Run.ComplexFieldRun(" NUMCHARS ", "stale");
        var numWords = Run.ComplexFieldRun(" NUMWORDS ", "stale");
        document.Blocks.Add(new Paragraph { Runs = { numChars } });
        document.Blocks.Add(new Paragraph { Runs = { numWords } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        numChars.Text.Should().Be("21");
        numWords.Text.Should().Be("4");
    }

    [Fact]
    public void UpdateFields_RefreshesRevisionNumberFromCoreProperties()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var revision = Run.ComplexFieldRun(" REVNUM ", "stale");
        var revisionProperty = Run.ComplexFieldRun(" DOCPROPERTY \"Revision Number\" ", "stale property");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "revision", "12"));
        document.Blocks.Add(new Paragraph { Runs = { revision, revisionProperty } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        revision.Text.Should().Be("12");
        revisionProperty.Text.Should().Be("12");
    }

    [Fact]
    public void UpdateFields_RefreshesEditTimeFromExtendedProperties()
    {
        var editTime = Run.ComplexFieldRun(" EDITTIME ", "stale");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Preserved.Parts.Add(new PreservedPart(
            Free.Shared.Opc.OpcPackageProperties.ExtendedPropertiesPartName,
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <TotalTime>135</TotalTime>
                </Properties>
                """)));
        document.Blocks.Add(new Paragraph { Runs = { editTime } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        editTime.Text.Should().Be("135");
    }

    [Fact]
    public void UpdateFields_RefreshesPrintDateFromCoreProperties()
    {
        var core = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        var printDate = Run.ComplexFieldRun(" PRINTDATE \\@ \"yyyy-MM-dd\" ", "stale");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Preserved.OriginalCoreProperties = new System.Xml.Linq.XElement(
            core + "coreProperties",
            new System.Xml.Linq.XElement(core + "lastPrinted", "2026-08-07T14:05:00Z"));
        document.Blocks.Add(new Paragraph { Runs = { printDate } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        printDate.Text.Should().Be("2026-08-07");
    }

    [Fact]
    public void UpdateFields_SeqUsesAuthoredResultPicture()
    {
        var field = Run.ComplexFieldRun(" SEQ Figure \\r 27 \\* alphabetic ", "stale");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { field } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        field.Text.Should().Be("aa");
    }

    [Fact]
    public void UpdateFields_SeqCountsTableFieldsAndClearsHiddenResult()
    {
        var first = Run.ComplexFieldRun(" SEQ Figure ", "stale");
        var hidden = Run.ComplexFieldRun(" SEQ Figure \\h ", "stale");
        var last = Run.ComplexFieldRun(" SEQ Figure ", "stale");
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph { Runs = { hidden } });
        row.Cells.Add(cell);
        table.Rows.Add(row);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { first } });
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph { Runs = { last } });
        var view = new DocumentView();
        view.LoadDocument(document);

        view.UpdateFields();

        first.Text.Should().Be("1");
        hidden.Text.Should().BeEmpty();
        last.Text.Should().Be("3");
    }
}
