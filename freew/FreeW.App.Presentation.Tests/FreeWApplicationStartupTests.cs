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

    [Fact]
    public void TryOpenStartupDocument_SkipsMissingAndUnsupportedArgumentsThenOpensFirstCandidate()
    {
        var adapter = new FakeDocumentAdapter();
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var unsupportedPath = WriteText("Ignored.pdf", "ignored");
        var supportedPath = WriteText("Opened.docx", "startup body");
        var laterPath = WriteText("Later.docx", "later body");

        var result = FreeWApplicationStartup.TryOpenStartupDocument(
            [Path.Combine(TempDirectory, "Missing.docx"), unsupportedPath, supportedPath, laterPath],
            workflow);

        result.Should().NotBeNull();
        result!.Document.PlainText.Should().Be("startup body");
        result.SavedPath.Should().Be(supportedPath);
        adapter.LoadCount.Should().Be(1);
    }

    [Fact]
    public void TryOpenStartupDocument_FirstCandidateFailureFallsBackWithoutTryingLaterArguments()
    {
        var adapter = new FakeDocumentAdapter { ThrowOnLoad = true };
        var workflow = new DocumentPersistenceWorkflow([adapter]);
        var firstPath = WriteText("Broken.docx", "broken");
        var laterPath = WriteText("Later.docx", "later body");

        var result = FreeWApplicationStartup.TryOpenStartupDocument([firstPath, laterPath], workflow);

        result.Should().BeNull();
        adapter.LoadCount.Should().Be(1);
    }

    [Fact]
    public void Hosts_ConsumeNeutralProfileWhilePlatformStartupRemainsLocal()
    {
        var avaloniaProgram = ReadSource("freew", "FreeW.App.Avalonia", "Program.cs");
        var avaloniaApp = ReadSource("freew", "FreeW.App.Avalonia", "App.cs");
        var avaloniaWindow = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var wpfProgram = ReadSource("freew", "FreeW.App.Host", "Program.cs");
        var neutralStartup = ReadSource("freew", "FreeW.App.Presentation", "Shell", "FreeWApplicationStartup.cs");

        avaloniaProgram.Should().Contain("SisterAvaloniaProgramRunner.Run(");
        avaloniaProgram.Should().Contain("FreeWApplicationStartup.ProductIdentity");
        avaloniaApp.Should().Contain("FreeWApplicationStartup.Theme");
        avaloniaApp.Should().Contain("SisterAvaloniaAppBootstrap.Initialize(");
        avaloniaProgram.Should().Contain("BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments)");
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
    }

    [Fact]
    public void AvaloniaProgram_PreservesSmokeOrderingFailureMessageAndPlatformActivation()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "Program.cs");

        SourceIndex(source, "PackagingSmoke.TryRun")
            .Should().BeLessThan(SourceIndex(source, "ReadAloudPauseSmoke.TryRun"));
        SourceIndex(source, "ReadAloudPauseSmoke.TryRun")
            .Should().BeLessThan(SourceIndex(source, "LaunchSmokeOptions.TryParse"));
        SourceIndex(source, "LaunchSmokeOptions.TryParse")
            .Should().BeLessThan(SourceIndex(source, "App.StartupArguments = startupArguments"));
        SourceIndex(source, "App.StartupArguments = startupArguments")
            .Should().BeLessThan(SourceIndex(source, "SisterAvaloniaLaunchPreparation.Continue(startupArguments)"));
        source.Should().Contain("SisterAvaloniaProgramRunner.Run(");
        source.Should().Contain("FreeWApplicationStartup.ProductIdentity");
        source.Should().Contain("Console.Error.WriteLine(error);");
        source.Should().Contain("SisterAvaloniaLaunchPreparation.Exit(1)");
    }

    private string WriteText(string fileName, string text)
    {
        var path = Path.Combine(TempDirectory, fileName);
        File.WriteAllText(path, text);
        return path;
    }

    private static int SourceIndex(string source, string value)
    {
        var index = source.IndexOf(value, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"the source should contain {value}");
        return index;
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

        public bool ThrowOnLoad { get; init; }

        public TextDocument Load(Stream stream)
        {
            LoadCount++;
            if (ThrowOnLoad)
                throw new InvalidDataException("broken startup document");

            using var reader = new StreamReader(stream, leaveOpen: true);
            return Document(reader.ReadToEnd());
        }

        public void Save(TextDocument document, Stream stream) => throw new NotSupportedException();
    }
}
