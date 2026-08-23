namespace Free.Shared.AppServices.Tests;

public sealed class AtomicLineSetStoreTests
{
    private const string StorePath = @"C:\fake\product\customdictionary.lex";

    [Fact]
    public void Load_ReturnsPersistedLinesWithoutApplyingProductSemantics()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.Files[StorePath] = ["Beta", "alpha", "ALPHA", "", " "];

        var lines = new AtomicLineSetStore(StorePath, fileSystem).Load();

        lines.Should().Equal("Beta", "alpha", "ALPHA", "", " ");
    }

    [Fact]
    public void Load_WhenPathIsNull_DoesNotTouchTheFileSystem()
    {
        var fileSystem = new FakeFileSystem { ThrowOnAccess = true };

        var lines = new AtomicLineSetStore(null, fileSystem).Load();

        lines.Should().BeEmpty();
        fileSystem.AccessCount.Should().Be(0);
    }

    [Theory]
    [InlineData(FailureOperation.Exists)]
    [InlineData(FailureOperation.Read)]
    public void Load_WhenFileSystemFails_ReturnsAnEmptySet(FailureOperation operation)
    {
        var fileSystem = new FakeFileSystem { Failure = operation };
        fileSystem.Files[StorePath] = ["existing"];

        var action = () => new AtomicLineSetStore(StorePath, fileSystem).Load();

        action.Should().NotThrow();
        action().Should().BeEmpty();
    }

    [Fact]
    public void TrySave_CreatesTheParentAndSerializesInCallerOrderWithATrailingNewline()
    {
        var fileSystem = new FakeFileSystem();
        var store = new AtomicLineSetStore(StorePath, fileSystem);

        store.TrySave(["Beta", "alpha", "ALPHA", " "]).Should().BeTrue();

        fileSystem.CreatedDirectories.Should().Equal(@"C:\fake\product");
        fileSystem.WrittenPath.Should().Be(StorePath);
        fileSystem.WrittenContent.Should().Be(
            $"Beta{Environment.NewLine}alpha{Environment.NewLine}ALPHA{Environment.NewLine} {Environment.NewLine}");
    }

    [Fact]
    public void TrySave_EmptySet_WritesAnEmptyFile()
    {
        var fileSystem = new FakeFileSystem();

        new AtomicLineSetStore(StorePath, fileSystem).TrySave([]).Should().BeTrue();

        fileSystem.WrittenContent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FailureOperation.CreateDirectory)]
    [InlineData(FailureOperation.Write)]
    public void TrySave_WhenFileSystemFails_ReturnsFalseWithoutThrowing(FailureOperation operation)
    {
        var fileSystem = new FakeFileSystem { Failure = operation };
        var store = new AtomicLineSetStore(StorePath, fileSystem);

        var action = () => store.TrySave(["word"]);

        action.Should().NotThrow();
        action().Should().BeFalse();
    }

    [Fact]
    public void TrySave_WhenEnumerationFails_ReturnsFalseWithoutWriting()
    {
        var fileSystem = new FakeFileSystem();
        var store = new AtomicLineSetStore(StorePath, fileSystem);

        store.TrySave(FailingLines()).Should().BeFalse();

        fileSystem.WrittenContent.Should().BeNull();
    }

    [Fact]
    public void PersistedFileExists_IsBestEffortAndNullPathSafe()
    {
        var nullPathFileSystem = new FakeFileSystem { ThrowOnAccess = true };
        new AtomicLineSetStore(null, nullPathFileSystem).PersistedFileExists().Should().BeFalse();
        nullPathFileSystem.AccessCount.Should().Be(0);

        var failingFileSystem = new FakeFileSystem { Failure = FailureOperation.Exists };
        var action = () => new AtomicLineSetStore(StorePath, failingFileSystem).PersistedFileExists();
        action.Should().NotThrow();
        action().Should().BeFalse();
    }

    [Fact]
    public void PhysicalFileSystem_RoundTripsUtf8WithoutBomAndLeavesNoTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"FreeX.AtomicLineSetStore.{Guid.NewGuid():N}");
        var path = Path.Combine(root, "nested", "customdictionary.lex");
        try
        {
            var first = new AtomicLineSetStore(path);
            first.TrySave(["caf\u00E9", "na\u00EFve"]).Should().BeTrue();

            new AtomicLineSetStore(path).Load().Should().Equal("caf\u00E9", "na\u00EFve");
            var bytes = File.ReadAllBytes(path);
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                .Should().BeFalse();
            Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<string> FailingLines()
    {
        yield return "first";
        throw new IOException("simulated enumeration failure");
    }

    public enum FailureOperation
    {
        None,
        Exists,
        Read,
        CreateDirectory,
        Write,
    }

    private sealed class FakeFileSystem : IAtomicLineSetFileSystem
    {
        public Dictionary<string, string[]> Files { get; } = [];
        public List<string> CreatedDirectories { get; } = [];
        public FailureOperation Failure { get; init; }
        public bool ThrowOnAccess { get; init; }
        public int AccessCount { get; private set; }
        public string? WrittenPath { get; private set; }
        public string? WrittenContent { get; private set; }

        public bool FileExists(string path)
        {
            RecordAccess(FailureOperation.Exists);
            return Files.ContainsKey(path);
        }

        public string[] ReadAllLines(string path)
        {
            RecordAccess(FailureOperation.Read);
            return Files.TryGetValue(path, out var lines) ? lines : [];
        }

        public void CreateDirectory(string path)
        {
            RecordAccess(FailureOperation.CreateDirectory);
            CreatedDirectories.Add(path);
        }

        public void WriteAllTextAtomically(string path, string content)
        {
            RecordAccess(FailureOperation.Write);
            WrittenPath = path;
            WrittenContent = content;
        }

        private void RecordAccess(FailureOperation operation)
        {
            AccessCount++;
            if (ThrowOnAccess || Failure == operation)
                throw new IOException($"simulated {operation} failure");
        }
    }
}
