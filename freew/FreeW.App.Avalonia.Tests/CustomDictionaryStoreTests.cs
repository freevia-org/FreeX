using FluentAssertions;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Proofing;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// GB2: <see cref="CustomDictionaryStore"/> persists added words to a .lex file shared by both shells
/// instead of the old plain in-memory dictionary that lost every
/// added word on restart. These tests use an in-memory file-system fake so persistence is
/// verifiable without touching the real user data folder.
/// </summary>
public sealed class CustomDictionaryStoreTests
{
    private const string Path = @"C:\fake\FreeW\customdictionary.lex";

    [Fact]
    public void Add_persists_the_word_to_the_backing_file()
    {
        var fs = new FakeFileSystem();
        var store = new CustomDictionaryStore(Path, fs);

        store.Add("teh").Should().BeTrue();

        fs.Files.Should().ContainKey(Path);
        fs.Files[Path].Should().ContainSingle().Which.Should().Be("teh");
    }

    [Fact]
    public void Loading_a_fresh_store_over_the_same_path_reads_back_the_persisted_word()
    {
        var fs = new FakeFileSystem();
        var first = new CustomDictionaryStore(Path, fs);
        first.Add("gonna");

        // Simulate a restart: a brand-new store instance over the same path/file-system state.
        var second = new CustomDictionaryStore(Path, fs);

        second.Words.Should().Contain("gonna");
        second.Contains("gonna").Should().BeTrue();
    }

    [Fact]
    public void Load_at_construction_populates_the_in_memory_set_before_any_Add_call()
    {
        var fs = new FakeFileSystem();
        fs.Seed(Path, "alpha", "beta");

        var store = new CustomDictionaryStore(Path, fs);

        store.Words.Should().BeEquivalentTo(new[] { "alpha", "beta" });
        store.Contains("alpha").Should().BeTrue();
        store.Contains("beta").Should().BeTrue();
    }

    [Fact]
    public void Duplicate_add_is_a_no_op_and_does_not_rewrite_the_file()
    {
        var fs = new FakeFileSystem();
        var store = new CustomDictionaryStore(Path, fs);
        store.Add("teh");
        var writeCountAfterFirstAdd = fs.WriteCount;

        store.Add("TEH").Should().BeFalse("already present case-insensitively");

        fs.WriteCount.Should().Be(writeCountAfterFirstAdd);
    }

    [Fact]
    public void Null_store_path_behaves_as_an_in_memory_session_only_dictionary()
    {
        var fs = new FakeFileSystem();
        var store = new CustomDictionaryStore(storePath: null, fs);

        store.Add("teh").Should().BeTrue("in-memory add still succeeds");
        store.Contains("teh").Should().BeTrue();
        fs.Files.Should().BeEmpty("no path means no backing file to write");
    }

    [Fact]
    public void Remove_persists_the_removal()
    {
        var fs = new FakeFileSystem();
        var store = new CustomDictionaryStore(Path, fs);
        store.Add("teh");

        store.Remove("teh").Should().BeTrue();

        fs.Files[Path].Should().BeEmpty();
        var reloaded = new CustomDictionaryStore(Path, fs);
        reloaded.Contains("teh").Should().BeFalse();
    }

    [Fact]
    public void Failed_atomic_write_leaves_the_previous_dictionary_file_intact()
    {
        var fs = new FakeFileSystem();
        fs.Seed(Path, "existing");
        var store = new CustomDictionaryStore(Path, fs);
        fs.FailNextAtomicWrite = true;

        store.Add("new-word").Should().BeTrue();

        fs.Files[Path].Should().Equal("existing");
        new CustomDictionaryStore(Path, fs).Words.Should().Equal("existing");
    }

    // ── Real-disk round trip (temp dir — never the real user data folder) ──────────────────────────

    /// <summary>
    /// End-to-end with the real <see cref="RealCustomDictionaryFileSystem"/>: a word added by one store instance is
    /// readable by a fresh instance over the same path (simulating an app restart), and the file on disk
    /// is UTF-8 without a BOM, word-per-line — the same .lex format the WPF host's spell checker consumes,
    /// so the two shells can share a dictionary file.
    /// </summary>
    [Fact]
    public void Real_disk_round_trip_survives_a_simulated_restart_in_lex_format()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.CustomDictionaryStoreTests-");
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "customdictionary.lex");
        {
            var first = new CustomDictionaryStore(path, RealCustomDictionaryFileSystem.Instance);
            first.Add("gonna").Should().BeTrue();

            File.Exists(path).Should().BeTrue();
            var bytes = File.ReadAllBytes(path);
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                .Should().BeFalse("the .lex file must have no BOM, matching the WPF host's format");

            // Simulate restart: a fresh store instance over the same on-disk file.
            var second = new CustomDictionaryStore(path, RealCustomDictionaryFileSystem.Instance);
            second.Words.Should().Contain("gonna");
        }
    }

    /// <summary>In-memory fake for <see cref="ICustomDictionaryFileSystem"/> — a dictionary of path → lines, so tests can
    /// assert exactly what would have been written to disk and simulate a fresh process reading it back.</summary>
    private sealed class FakeFileSystem : ICustomDictionaryFileSystem
    {
        public Dictionary<string, List<string>> Files { get; } = new();
        public int WriteCount { get; private set; }
        public bool FailNextAtomicWrite { get; set; }

        public void Seed(string path, params string[] lines) => Files[path] = new List<string>(lines);

        public bool Exists(string path) => Files.ContainsKey(path);

        public string[] ReadAllLines(string path) => Files.TryGetValue(path, out var lines) ? lines.ToArray() : [];

        public void WriteAllLinesAtomically(string path, IEnumerable<string> lines)
        {
            var replacement = new List<string>(lines);
            if (FailNextAtomicWrite)
            {
                FailNextAtomicWrite = false;
                throw new IOException("simulated atomic write failure");
            }

            Files[path] = replacement;
            WriteCount++;
        }

        public void CreateDirectory(string path)
        {
            // No directory structure to simulate — the fake keys files by full path directly.
        }
    }
}
