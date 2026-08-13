using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWBackstageSessionTests
{
    [Fact]
    public void InfoPaneReadsOneLiveDocumentSnapshotAndCurrentFileState()
    {
        var displayName = "Initial.docx";
        var currentPath = "C:/Docs/Initial.docx";
        var isDirty = false;
        var documentReads = 0;
        var document = new TextDocument();
        document.Properties.Title = "Initial title";
        var callbacks = CreateCallbacks(
            document: () =>
            {
                documentReads++;
                return document;
            },
            isDirty: () => isDirty,
            getDisplayName: () => displayName,
            getCurrentPath: () => currentPath);
        var session = new FreeWBackstageSession(callbacks);

        displayName = "Current.docx";
        currentPath = "C:/Docs/Current.docx";
        isDirty = true;
        document.Properties.Title = "Current title";
        var pane = session.BuildInfoPane();

        documentReads.Should().Be(1);
        pane.DisplayName.Should().Be("Current.docx");
        pane.Location.Should().Be("C:/Docs/Current.docx");
        pane.IsDirty.Should().BeTrue();
        pane.Properties.Should().Contain(row => row.Value == "Current title");
    }

    [Fact]
    public void BlankLiveDisplayNameUsesUntitledFallback()
    {
        var session = new FreeWBackstageSession(CreateCallbacks(getDisplayName: () => "  "));

        session.DisplayName.Should().Be(BackstageViewTextResources.UntitledValue);
        session.BuildSaveAsPane().InlinePlan.SuggestedFileName.Should().StartWith("Document");
    }

    [Fact]
    public void InfoPanePreservesStableActionAutomationIds()
    {
        var session = new FreeWBackstageSession(CreateCallbacks());

        var action = session.BuildInfoPane().ActionGroups!
            .SelectMany(group => group.Actions)
            .Single(row => row.AutomationId == "InfoAction_MarkAsFinal");

        action.Label.Should().NotBeNullOrWhiteSpace();
        action.ResolveAutomationId("BackstageAction_").Should().Be("InfoAction_MarkAsFinal");
    }

    [Fact]
    public void PrintPaneInfersNativeCapabilityAndDismissesBeforeDispatch()
    {
        var calls = new List<string>();
        var callbacks = CreateCallbacks(print: () => calls.Add("print"));
        var session = new FreeWBackstageSession(callbacks, Binder(calls));

        var pane = session.BuildPrintPane();
        var print = pane.Groups
            .SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "PrintAction_Print");
        print.Invoke.Should().NotBeNull();

        print.Invoke!();

        calls.Should().Equal("dismiss", "print");
        pane.DeferredNote.Should().BeNull();
    }

    [Fact]
    public void ExplicitDeferredPrintCapabilitySuppressesHostPrintCallback()
    {
        var calls = new List<string>();
        var callbacks = CreateCallbacks(
            print: () => calls.Add("print"),
            directPrintCapability: BackstageDirectPrintCapability.Deferred("Unavailable", "Use preview."));
        var session = new FreeWBackstageSession(callbacks, Binder(calls));

        var pane = session.BuildPrintPane();
        var print = pane.Groups
            .SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "PrintAction_Print");

        print.IsEnabled.Should().BeFalse();
        print.Invoke.Should().BeNull();
        pane.DeferredNote.Should().Be("Use preview.");
        calls.Should().BeEmpty();
    }

    [Fact]
    public void SaveInlinePrefersSuggestedNameAndBindsBeforeDispatch()
    {
        var calls = new List<string>();
        (string? FileName, string? Extension)? saved = null;
        var callbacks = CreateCallbacks(saveAsSuggested: (fileName, extension) =>
        {
            calls.Add("save-suggested");
            saved = (fileName, extension);
        });
        var session = new FreeWBackstageSession(callbacks, Binder(calls));

        session.SaveInline(
            "Typed name.docx",
            new BackstageSaveAsFileTypeChoice("Rich Text", ".rtf", 7),
            ".docx");

        calls.Should().Equal("dismiss", "save-suggested");
        saved.Should().Be(("Typed name.docx", ".rtf"));
    }

    [Fact]
    public void SaveInlineFallsBackToFormatCallbackWithCatalogFilterIndex()
    {
        var calls = new List<string>();
        (string Extension, int FilterIndex)? saved = null;
        var callbacks = CreateCallbacks(saveAsFormat: (extension, filterIndex) =>
        {
            calls.Add("save-format");
            saved = (extension, filterIndex);
        });
        var session = new FreeWBackstageSession(callbacks, Binder(calls));

        session.SaveInline(
            "Ignored by format-only hosts",
            new BackstageSaveAsFileTypeChoice("Web Page", ".htm", 4),
            ".docx");

        calls.Should().Equal("dismiss", "save-format");
        saved.Should().Be((".htm", 4));
    }

    [Fact]
    public void PaneActionsUseTheSharedDismissBinder()
    {
        var calls = new List<string>();
        var callbacks = CreateCallbacks(
            newDocument: () => calls.Add("new"),
            openRecent: path => calls.Add("open:" + path));
        callbacks = callbacks with
        {
            GetRecentEntries = () => [new RecentFileEntry { Path = "C:/Docs/Recent.docx" }],
        };
        var session = new FreeWBackstageSession(callbacks, Binder(calls));
        var home = session.BuildHomePane(() => calls.Add("open-more"));

        home.Groups.SelectMany(group => group.Actions)
            .Single(action => action.Label == "Blank document")
            .Invoke();
        home.Groups.SelectMany(group => group.Actions)
            .Single(action => action.Description.Contains("Recent.docx", StringComparison.Ordinal))
            .Invoke();

        calls.Should().Equal("dismiss", "new", "dismiss", "open:C:/Docs/Recent.docx");
    }

    [Fact]
    public void DismissBeforeBinderOwnsAllCallbackShapes()
    {
        var calls = new List<string>();
        var binder = BackstageActionBinder.DismissBefore(() => calls.Add("dismiss"));

        binder.Bind(() => calls.Add("plain"))();
        binder.Bind<string>(value => calls.Add("string:" + value))("one");
        binder.Bind<string, int>((value, index) => calls.Add($"format:{value}:{index}"))("two", 2);
        binder.Bind<string?, string?>((fileName, extension) => calls.Add($"suggested:{fileName}:{extension}"))(
            "three",
            ".docx");

        calls.Should().Equal(
            "dismiss", "plain",
            "dismiss", "string:one",
            "dismiss", "format:two:2",
            "dismiss", "suggested:three:.docx");
    }

    [Theory]
    [InlineData(null, ".pdf", "Document.pdf")]
    [InlineData("", ".rtf", "Document.rtf")]
    [InlineData("Report.docx", "txt", "Report.txt")]
    public void ChangeInlineFileTypePreservesDeletedAvaloniaFallbackBehavior(
        string? fileName,
        string extension,
        string expected)
    {
        var session = new FreeWBackstageSession(CreateCallbacks());

        session.ChangeInlineFileType(fileName, extension).Should().Be(expected);
    }

    private static BackstageCallbacks CreateCallbacks(
        Func<TextDocument>? document = null,
        Func<bool>? isDirty = null,
        Func<string>? getDisplayName = null,
        Func<string?>? getCurrentPath = null,
        Action? newDocument = null,
        Action<string>? openRecent = null,
        Action<string, int>? saveAsFormat = null,
        Action? print = null,
        Action<string?, string?>? saveAsSuggested = null,
        BackstageDirectPrintCapability? directPrintCapability = null)
    {
        var model = new TextDocument();
        return new BackstageCallbacks(
            DisplayName: "Document.docx",
            CurrentPath: null,
            GetRecentEntries: () => [],
            GetFileFormats: () => [],
            GetPageSettings: () => model.Page,
            GetCurrentOptions: () => new FreeWOptions(),
            GetDataFolder: () => "C:/Data",
            GetDocument: document ?? (() => model),
            GetIsDirty: isDirty ?? (() => false),
            NewDocument: newDocument ?? NoAction,
            OpenRecent: openRecent ?? (_ => { }),
            OpenFolder: _ => { },
            Browse: NoAction,
            RecoverUnsaved: NoAction,
            ImportPdfText: NoAction,
            Save: NoAction,
            SaveAs: NoAction,
            SaveAsFormat: saveAsFormat ?? ((_, _) => { }),
            SaveCopy: NoAction,
            OpenContainingFolder: _ => { },
            ExportPdf: NoAction,
            ExportXps: null,
            EditProperties: NoAction,
            MarkAsFinal: NoAction,
            RestrictEditing: NoAction,
            InspectDocument: NoAction,
            CheckAccessibility: NoAction,
            OpenOptions: NoAction,
            CloseDocument: NoAction,
            DirectPrintCapability: directPrintCapability,
            Print: print,
            PrintPreview: NoAction,
            SaveAsSuggested: saveAsSuggested,
            GetDisplayName: getDisplayName,
            GetCurrentPath: getCurrentPath);
    }

    private static BackstageActionBinder Binder(List<string> calls) =>
        BackstageActionBinder.DismissBefore(() => calls.Add("dismiss"));

    private static void NoAction()
    {
    }
}
