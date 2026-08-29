using Free.Shared.IO;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWApplicationStartupTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.ApplicationStartupTests-");
    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void Profile_OwnsFreeWIdentityAndCaseInsensitiveThemeSelection()
    {
        FreeWApplicationStartup.ProductIdentity.Should().BeEquivalentTo(new
        {
            ProductDirectoryName = "FreeW",
            DiagnosticsEnvironmentVariable = "FREEW_DIAGNOSTICS",
            ProductName = "FreeW"
        });

        var theme = FreeWApplicationStartup.Theme;
        theme.EnvironmentVariableName.Should().Be("FREEW_THEME");
        theme.Resolve(_ => null).Should().BeSameAs(theme.DefaultTheme);
        theme.Resolve(_ => "MIDNIGHT").Should().BeSameAs(theme.AlternateTheme);
        theme.Resolve(_ => " midnight ").Should().BeSameAs(theme.DefaultTheme);
    }

    // shared-startup-args F1: PlanStartupDocuments used to be capped at exactly one candidate
    // (MaximumOpenableFiles: 1), so laterPath here was silently dropped -- this proves the plan now
    // keeps EVERY valid candidate (not just the first), each correctly flagged for "this window" vs
    // "a new window", and that each remains independently openable through the same API a second
    // window would use.
    [Fact]
    public void PlanStartupDocuments_SkipsMissingAndUnsupportedArgumentsButKeepsEveryValidCandidate()
    {
        var adapter = new FakeDocumentAdapter();
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var unsupportedPath = WriteText("Ignored.pdf", "ignored");
        var supportedPath = WriteText("Opened.docx", "startup body");
        var laterPath = WriteText("Later.docx", "later body");

        var plan = FreeWApplicationStartup.PlanStartupDocuments(
            [Path.Combine(TempDirectory, "Missing.docx"), unsupportedPath, supportedPath, laterPath],
            workflow);

        plan.Entries.Select(entry => entry.Path).Should().Equal(supportedPath, laterPath);
        plan.Entries[0].OpenInNewWindow.Should().BeFalse();
        plan.Entries[1].OpenInNewWindow.Should().BeTrue();

        var primaryResult = FreeWApplicationStartup.TryOpenStartupDocument(plan.Entries[0], workflow);
        primaryResult.Should().NotBeNull();
        primaryResult!.Document.PlainText.Should().Be("startup body");
        primaryResult.SavedPath.Should().Be(supportedPath);

        var additionalResult = FreeWApplicationStartup.TryOpenStartupDocument(plan.Entries[1], workflow);
        additionalResult.Should().NotBeNull();
        additionalResult!.Document.PlainText.Should().Be("later body");
        additionalResult.SavedPath.Should().Be(laterPath);
        adapter.LoadCount.Should().Be(2);
    }

    // Sibling: a broken FIRST candidate must not cascade-fail the rest -- each plan entry opens
    // independently, matching what each shell now does by giving every additional entry its own
    // window (a corrupt/locked file must not take the remaining startup files down with it).
    [Fact]
    public void TryOpenStartupDocument_FirstCandidateFailureDoesNotPreventOpeningALaterCandidate()
    {
        var adapter = new FakeDocumentAdapter { ThrowOnLoad = true };
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var firstPath = WriteText("Broken.docx", "broken");
        var laterPath = WriteText("Later.docx", "later body");

        var plan = FreeWApplicationStartup.PlanStartupDocuments([firstPath, laterPath], workflow);
        plan.Entries.Select(entry => entry.Path).Should().Equal(firstPath, laterPath);

        var primaryResult = FreeWApplicationStartup.TryOpenStartupDocument(plan.Entries[0], workflow);
        primaryResult.Should().BeNull();

        var additionalResult = FreeWApplicationStartup.TryOpenStartupDocument(plan.Entries[1], workflow);
        additionalResult.Should().NotBeNull();
        additionalResult!.Document.PlainText.Should().Be("later body");
    }

    // Sibling no-regression: the very same path given twice must collapse to a single plan entry --
    // proves the uncapped plan still de-duplicates (StartupFileOpenPlanner's seenPaths guard), so
    // removing the old MaximumOpenableFiles: 1 cap does not resurrect the "same file twice opens two
    // unsynchronized windows" defect the shared planner already exists to prevent.
    [Fact]
    public void PlanStartupDocuments_CollapsesTheSamePathGivenTwiceIntoOneEntry()
    {
        var adapter = new FakeDocumentAdapter();
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Repeated.docx", "startup body");

        var plan = FreeWApplicationStartup.PlanStartupDocuments([path, path], workflow);

        plan.Entries.Should().ContainSingle();
        plan.Entries[0].Path.Should().Be(path);
        plan.Entries[0].OpenInNewWindow.Should().BeFalse();
    }

    // Sibling no-regression: a startup argument that names a file which does not exist on disk must
    // still degrade gracefully -- the plan reports it via FirstMissingPath/ShouldReportMissingPath
    // instead of producing an openable entry for it.
    [Fact]
    public void PlanStartupDocuments_ReportsAPathThatDoesNotExistAsMissingRatherThanAnEntry()
    {
        var adapter = new FakeDocumentAdapter();
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var missingPath = Path.Combine(TempDirectory, "does-not-exist.docx");

        var plan = FreeWApplicationStartup.PlanStartupDocuments([missingPath], workflow);

        plan.Entries.Should().BeEmpty();
        plan.ShouldReportMissingPath.Should().BeTrue();
        plan.FirstMissingPath.Should().Be(missingPath);
    }

    [Fact]
    public void PlanStartupDocuments_NormalizesLocalFileUrisThroughSharedPlanning()
    {
        var adapter = new FakeDocumentAdapter();
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var path = WriteText("Opened from URI.docx", "startup body");

        var plan = FreeWApplicationStartup.PlanStartupDocuments([new Uri(path).AbsoluteUri], workflow);

        plan.Entries.Should().ContainSingle();
        var result = FreeWApplicationStartup.TryOpenStartupDocument(plan.Entries[0], workflow);

        result.Should().NotBeNull();
        result!.SavedPath.Should().Be(path);
        result.Document.PlainText.Should().Be("startup body");
    }

    [Fact]
    public void Hosts_ConsumeNeutralProfileWhilePlatformStartupRemainsLocal()
    {
        var avaloniaProgram = ReadSource("freew", "FreeW.App.Avalonia", "Program.cs");
        var avaloniaApp = ReadSource("freew", "FreeW.App.Avalonia", "App.cs");
        var avaloniaWindow = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var wpfProgram = ReadSource("freew", "FreeW.App.Host", "Program.cs");
        var neutralStartup = ReadSource("freew", "FreeW.App.Presentation", "Shell", "FreeWApplicationStartup.cs");

        avaloniaProgram.Should().Contain("SisterAvaloniaStandardDesktopFactory.Run(args, App.DesktopProfile)");
        avaloniaApp.Should().Contain("FreeWApplicationStartup.ProductIdentity");
        avaloniaApp.Should().Contain("FreeWApplicationStartup.Theme");
        avaloniaApp.Should().Contain("SisterAvaloniaStandardDesktopFactory.Initialize(this, DesktopProfile)");
        avaloniaWindow.Should().Contain("FreeWApplicationStartup.PlanStartupDocuments(");
        avaloniaWindow.Should().Contain("FreeWApplicationStartup.TryOpenStartupDocument(");
        avaloniaWindow.Should().NotContain("LoadStartupDocument(");

        wpfProgram.Should().Contain("FreeWApplicationStartup.ProductIdentity");
        wpfProgram.Should().Contain("Plan: FreeWApplicationStartup.Theme");
        wpfProgram.Should().Contain("WpfApplicationStartupRunner.Run(");

        avaloniaProgram.Should().NotContain("new AppProductIdentity(\"FreeW\"");
        avaloniaProgram.Should().NotContain("LocalAppDiagnostics.CreateDefault");
        avaloniaProgram.Should().NotContain("diagnostics.RegisterCrashHandlers");
        avaloniaProgram.Should().NotContain("diagnostics.RecordCrash");
        avaloniaApp.Should().NotContain("Environment.GetEnvironmentVariable(\"FREEW_THEME\")");
        wpfProgram.Should().NotContain("EnvironmentVariableName: \"FREEW_THEME\"");
        neutralStartup.Should().NotContain("using Avalonia");
        neutralStartup.Should().NotContain("using System.Windows");
        neutralStartup.Should().NotContain("Dispatcher");
        neutralStartup.Should().NotContain("MainWindow");
        neutralStartup.Should().Contain("StartupFileOpenPlanner.Plan(");
        // shared-startup-args F1: the plan used to be capped at exactly one candidate, silently
        // dropping every startup-argument file beyond the first -- assert the cap is gone rather than
        // pinning it, so this contract cannot re-lock the very defect it exists to catch.
        neutralStartup.Should().NotContain("MaximumOpenableFiles: 1");
    }

    [Fact]
    public void AvaloniaProgram_LeavesValidationCommandsOutsideShippingAndPreservesPlatformActivation()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "Program.cs");
        var validation = ReadSource("freew", "TestSupport", "Validation.Avalonia", "Program.cs");

        source.Should().NotContain("PackagingSmoke.TryRun");
        source.Should().NotContain("ReadAloudPauseSmoke.TryRun");
        source.Should().NotContain("SisterAppLaunchSmokeOptions.TryParse");
        source.Should().Contain("SisterAvaloniaStandardDesktopFactory.Run(args, App.DesktopProfile)");
        validation.Should().Contain("PackagingSmoke.TryRun");
        validation.Should().Contain("ReadAloudPauseSmoke.TryRun");
        validation.Should().Contain("SisterAppLaunchSmokeOptions.TryParse");
    }

    private string WriteText(string fileName, string text)
    {
        var path = Path.Combine(TempDirectory, fileName);
        File.WriteAllText(path, text);
        return path;
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static TextDocument Document(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private sealed class FakeDocumentAdapter : IDocumentFileAdapter
    {
        public string Extension => ".docx";

        public string FormatName => "Word Document";

        public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
            [new FileFormatDescriptor(".docx", "Word Document")];

        public int LoadCount { get; private set; }

        // Throws only on the FIRST Load call (a broken/corrupt "first" startup file), so a test can
        // prove a later, distinct candidate still opens fine through the same adapter instance -- a
        // real corrupt document only fails once; it does not retroactively corrupt every other file.
        public bool ThrowOnLoad { get; init; }

        public TextDocument Load(Stream stream)
        {
            LoadCount++;
            if (ThrowOnLoad && LoadCount == 1)
                throw new InvalidDataException("broken startup document");

            using var reader = new StreamReader(stream, leaveOpen: true);
            return Document(reader.ReadToEnd());
        }

        public void Save(TextDocument document, Stream stream) => throw new NotSupportedException();
    }
}
