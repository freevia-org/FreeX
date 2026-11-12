using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Localization;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWTemplateGalleryTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(FreeWTemplateGalleryTests));
    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ── Gallery planner ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_OffersEveryBuiltInTemplateWithLocalizedCaptionAndThumbnail()
    {
        var created = new List<string>();
        var blank = 0;

        var gallery = FreeWTemplateGalleryPlanner.Build(
            () => blank++,
            created.Add,
            newFromTemplateFile: null,
            personalTemplates: null,
            personalTemplatesFolder: null,
            key => "localized:" + key);

        gallery.Heading.Should().Be("localized:Common_New");
        var featured = gallery.Sections.Should().ContainSingle().Subject;
        featured.Heading.Should().Be("localized:FreeW_Backstage_New_FeaturedHeading");
        featured.Tiles.Select(tile => tile.Id).Should().Equal(
            "blank", "business-letter", "resume", "report", "meeting-notes", "memo");
        featured.Tiles.Select(tile => tile.Caption).Should().Equal(
            "localized:FreeW_Template_Blank_Name",
            "localized:FreeW_Template_BusinessLetter_Name",
            "localized:FreeW_Template_Resume_Name",
            "localized:FreeW_Template_Report_Name",
            "localized:FreeW_Template_MeetingNotes_Name",
            "localized:FreeW_Template_Memo_Name");
        featured.Tiles.Select(tile => tile.AutomationId).Should().OnlyHaveUniqueItems()
            .And.Contain("BackstageTemplate_businessletter");
        featured.Tiles.Where(tile => tile.Id != "blank")
            .Should().OnlyContain(tile => tile.Thumbnail.Count > 3);
        featured.Tiles.Single(tile => tile.Id == "blank").Thumbnail.Should().BeEmpty();

        foreach (var tile in featured.Tiles)
            tile.Invoke();

        blank.Should().Be(1, "Blank document is plain File > New");
        created.Should().Equal("business-letter", "resume", "report", "meeting-notes", "memo");
    }

    [Fact]
    public void Build_UsesEnglishFallbacksWithoutAResourceLookup()
    {
        var gallery = FreeWTemplateGalleryPlanner.Build(() => { }, _ => { }, _ => { }, [], @"C:\Templates");

        gallery.Heading.Should().Be("New");
        gallery.Sections.Select(section => section.Heading).Should().Equal("Featured", "Personal");
        gallery.Sections[0].Tiles.Select(tile => tile.Caption).Should().Equal(
            "Blank document", "Business letter", "Resume", "Report", "Meeting notes", "Memo");
        gallery.Sections[1].EmptyText.Should().StartWith("No personal templates yet.");
        gallery.Sections[1].FooterText.Should().Be(@"Templates folder: C:\Templates");
    }

    [Fact]
    public void Build_WithoutTemplateSupport_IsHonestAndOffersOnlyBlank()
    {
        var gallery = FreeWTemplateGalleryPlanner.Build(() => { }, null, null, null, null);

        gallery.Sections.Should().ContainSingle()
            .Which.Tiles.Should().ContainSingle().Which.Id.Should().Be("blank");
    }

    [Fact]
    public void Build_ListsPersonalTemplatesAndOpensTheChosenFile()
    {
        // Paths are built with the platform separator: the description shows Path.GetFileName, which
        // does not split on '\' on Linux/macOS.
        var opened = new List<string>();
        var folder = Path.Combine(Path.GetTempPath(), "Templates");
        var macroReport = Path.Combine(folder, "Macro Report.dotm");
        var entries = new[]
        {
            new PersonalTemplateEntry(Path.Combine(folder, "Invoice.dotx"), "Invoice"),
            new PersonalTemplateEntry(macroReport, "Macro Report"),
        };

        var gallery = FreeWTemplateGalleryPlanner.Build(() => { }, _ => { }, opened.Add, entries, folder);

        var personal = gallery.Sections[1];
        personal.EmptyText.Should().BeNull();
        personal.Tiles.Select(tile => tile.Caption).Should().Equal("Invoice", "Macro Report");
        personal.Tiles[1].Description.Should().Be("Create a new document based on Macro Report.dotm.");
        personal.Tiles[1].AutomationId.Should().Be("BackstagePersonalTemplate_MacroReport");
        personal.Tiles.Should().OnlyContain(tile => tile.Thumbnail.Count > 0);

        personal.Tiles[1].Invoke();
        opened.Should().Equal(macroReport);
    }

    [Fact]
    public void RequiredResourceKeys_AllExistInTheNeutralCatalog()
    {
        var available = Loc.GetNeutralResourceKeys();

        FreeWTemplateGalleryPlanner.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        FreeWTemplateGalleryPlanner.RequiredResourceKeys.Should().OnlyContain(key => available.Contains(key));
        Loc.GetNeutral(FreeWBackstagePaneResourceKeys.TemplateFooterText)
            .Should().NotContain("not available");
    }

    // ── Thumbnails ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BuiltInDocumentTemplates.BusinessLetterId)]
    [InlineData(BuiltInDocumentTemplates.ResumeId)]
    [InlineData(BuiltInDocumentTemplates.ReportId)]
    [InlineData(BuiltInDocumentTemplates.MeetingNotesId)]
    [InlineData(BuiltInDocumentTemplates.MemoId)]
    public void Thumbnail_StaysOnThePageAndReflectsTheTemplate(string id)
    {
        var marks = FreeWTemplateThumbnailPlanner.Build(BuiltInDocumentTemplates.Create(id)!);

        marks.Should().NotBeEmpty();
        marks.Should().OnlyContain(mark =>
            mark.X >= 0 && mark.Y >= 0 && mark.Width > 0 && mark.Height > 0
            && mark.X + mark.Width <= 1.0001 && mark.Y + mark.Height <= 1.0001);
        marks.Select(mark => mark.Y).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Thumbnail_ShowsOnlyPageOneAndDrawsTablesAndHeadings()
    {
        var report = FreeWTemplateThumbnailPlanner.Build(
            BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.ReportId)!);
        var notes = FreeWTemplateThumbnailPlanner.Build(
            BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.MeetingNotesId)!);

        report.Should().Contain(mark => mark.Kind == TemplateThumbnailMarkKind.Title);
        report.Should().NotContain(mark => mark.Kind == TemplateThumbnailMarkKind.Table,
            "the report's table is past the cover page break");
        notes.Should().Contain(mark => mark.Kind == TemplateThumbnailMarkKind.Table && mark.Rows == 5 && mark.Columns == 2);
        notes.Should().Contain(mark => mark.Kind == TemplateThumbnailMarkKind.Heading);
        FreeWTemplateThumbnailPlanner.Build(TextDocument.CreateEmpty()).Should().BeEmpty();
    }

    // ── Personal templates folder ────────────────────────────────────────────────────────────────

    [Fact]
    public void PersonalFolder_DefaultsToWordsCustomOfficeTemplatesUnderDocuments()
    {
        PersonalTemplatesFolder.Resolve(null, @"C:\Users\me\Documents")
            .Should().Be(Path.Combine(@"C:\Users\me\Documents", "Custom Office Templates"));
        PersonalTemplatesFolder.Resolve("   ", @"C:\Users\me\Documents")
            .Should().EndWith("Custom Office Templates");
        PersonalTemplatesFolder.Resolve(null).Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Custom Office Templates"));
    }

    [Fact]
    public void PersonalFolder_HonoursTheConfiguredFolder()
    {
        PersonalTemplatesFolder.Resolve(@"  D:\Shared\Templates ", @"C:\Docs").Should().Be(@"D:\Shared\Templates");
    }

    [Theory]
    [InlineData(".dotx", true)]
    [InlineData("DOTM", true)]
    [InlineData(".docx", false)]
    [InlineData(".dot", false)]
    [InlineData(null, false)]
    public void IsTemplateExtension_CoversWordTemplatesOnly(string? extension, bool expected) =>
        PersonalTemplatesFolder.IsTemplateExtension(extension).Should().Be(expected);

    [Fact]
    public void ResolveSaveDirectory_StartsTemplateSavesInThePersonalFolderAndCreatesIt()
    {
        var folder = Path.Combine(TempDirectory, "Custom Templates");

        PersonalTemplatesFolder.ResolveSaveDirectory(".docx", folder).Should().BeNull();
        PersonalTemplatesFolder.ResolveSaveDirectory(null, folder).Should().BeNull();
        Directory.Exists(folder).Should().BeFalse("a non-template save must not create the folder");

        PersonalTemplatesFolder.ResolveSaveDirectory(".dotx", folder).Should().Be(folder);
        Directory.Exists(folder).Should().BeTrue();
        PersonalTemplatesFolder.ResolveSaveDirectory(".dotm", null, TempDirectory)
            .Should().Be(Path.Combine(TempDirectory, "Custom Office Templates"));
    }

    [Fact]
    public void Catalog_ListsTemplateFilesByNameAndSkipsEverythingElse()
    {
        File.WriteAllText(Path.Combine(TempDirectory, "Zeta.dotx"), "x");
        File.WriteAllText(Path.Combine(TempDirectory, "alpha.DOTM"), "x");
        File.WriteAllText(Path.Combine(TempDirectory, "Notes.docx"), "x");
        File.WriteAllText(Path.Combine(TempDirectory, "~$Zeta.dotx"), "x");
        Directory.CreateDirectory(Path.Combine(TempDirectory, "Nested.dotx"));

        var entries = PersonalTemplateCatalog.List(TempDirectory);

        entries.Select(entry => entry.DisplayName).Should().Equal("alpha", "Zeta");
        entries[1].Path.Should().Be(Path.Combine(TempDirectory, "Zeta.dotx"));
    }

    [Fact]
    public void Catalog_MissingOrInvalidFolderIsEmpty()
    {
        PersonalTemplateCatalog.List(Path.Combine(TempDirectory, "missing")).Should().BeEmpty();
        PersonalTemplateCatalog.List(null).Should().BeEmpty();
        PersonalTemplateCatalog.List("bad\0path").Should().BeEmpty();
    }

    // ── Options ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Options_PersonalTemplatesFolderRoundTripsAndSurvivesAnOptionsDialogCommit()
    {
        var live = new FreeWOptions { PersonalTemplatesFolder = @"  D:\Templates  " };
        var runtime = new FreeWOptionsRuntimeSession(live);
        live.PersonalTemplatesFolder.Should().Be(@"D:\Templates");
        live.Clone().PersonalTemplatesFolder.Should().Be(@"D:\Templates");

        // The Options dialog never edits the folder, so its result carries the default.
        FreeWOptions? persisted = null;
        runtime.ApplyAndPersist(
            new FreeWOptions { RecentFilesCap = 7 },
            options => { persisted = options; return true; },
            reloadFresh: () => new FreeWOptions { PersonalTemplatesFolder = @"D:\Templates" });

        live.RecentFilesCap.Should().Be(7);
        live.PersonalTemplatesFolder.Should().Be(@"D:\Templates");
        persisted!.PersonalTemplatesFolder.Should().Be(@"D:\Templates");

        var json = System.Text.Json.JsonSerializer.Serialize(live);
        System.Text.Json.JsonSerializer.Deserialize<FreeWOptions>(json)!
            .PersonalTemplatesFolder.Should().Be(@"D:\Templates");
        new FreeWOptions().PersonalTemplatesFolder.Should().BeEmpty();
    }

    // ── Backstage session ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Session_GalleryDismissesBackstageThenCreatesFromTheChosenTemplate()
    {
        var events = new List<string>();
        File.WriteAllText(Path.Combine(TempDirectory, "Mine.dotx"), "x");
        var session = new FreeWBackstageSession(
            Callbacks(events) with
            {
                NewFromBuiltInTemplate = id => events.Add("builtin:" + id),
                NewFromTemplateFile = path => events.Add("file:" + Path.GetFileName(path)),
                GetPersonalTemplatesFolder = () => TempDirectory,
            },
            BackstageActionBinder.DismissBefore(() => events.Add("dismiss")));

        var gallery = session.BuildTemplateGallery();
        gallery.Sections[0].Tiles.Single(tile => tile.Id == "memo").Invoke();
        gallery.Sections[1].Tiles.Single().Invoke();
        gallery.Sections[0].Tiles[0].Invoke();

        gallery.Sections[1].FooterText.Should().Contain(TempDirectory);
        events.Should().Equal("dismiss", "builtin:memo", "dismiss", "file:Mine.dotx", "dismiss", "new");
    }

    [Fact]
    public void Session_WithoutTemplateCallbacks_OffersBlankOnlyAndLeavesHomeUnchanged()
    {
        var session = new FreeWBackstageSession(Callbacks([]));

        session.BuildTemplateGallery().Sections.Should().ContainSingle()
            .Which.Tiles.Should().ContainSingle();
        session.BuildHomePane(() => { }).Groups[0].Actions.Select(action => action.Label)
            .Should().Equal("Blank document");
    }

    [Fact]
    public void Session_HomePaneNewGroupListsBuiltInTemplatesAfterBlank()
    {
        var events = new List<string>();
        var session = new FreeWBackstageSession(
            Callbacks(events) with { NewFromBuiltInTemplate = id => events.Add("builtin:" + id) });

        var newGroup = session.BuildHomePane(() => { }).Groups[0];

        newGroup.Heading.Should().Be("New");
        newGroup.Actions.Select(action => action.Label).Should().Equal(
            "Blank document", "Business letter", "Resume", "Report", "Meeting notes", "Memo");
        newGroup.Actions.Select(action => action.ResolveAutomationId("BackstageAction_"))
            .Should().OnlyHaveUniqueItems();
        newGroup.Actions[3].Invoke();
        events.Should().Equal("builtin:report");
    }

    private static BackstageCallbacks Callbacks(List<string> events) => new(
        DisplayName: "Document1",
        CurrentPath: null,
        GetRecentEntries: () => [],
        GetFileFormats: () => [],
        GetPageSettings: () => new PageSettings(),
        GetCurrentOptions: () => new FreeWOptions(),
        GetDataFolder: () => string.Empty,
        GetDocument: TextDocument.CreateEmpty,
        GetIsDirty: () => false,
        NewDocument: () => events.Add("new"),
        OpenRecent: _ => { },
        OpenFolder: _ => { },
        Browse: () => { },
        RecoverUnsaved: () => { },
        ImportPdfText: () => { },
        Save: () => { },
        SaveAs: () => { },
        SaveAsFormat: (_, _) => { },
        SaveCopy: () => { },
        OpenContainingFolder: _ => { },
        ExportPdf: () => { },
        ExportXps: null,
        EditProperties: () => { },
        MarkAsFinal: () => { },
        RestrictEditing: () => { },
        InspectDocument: () => { },
        CheckAccessibility: () => { },
        OpenOptions: () => { },
        CloseDocument: () => { });
}
