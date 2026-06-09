using System.Text.Json;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileAccessIdentityTests
{
    [Fact]
    public void FromLocalPath_NormalizesLocalPathAndTrimsBookmarkMetadata()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Book.fxl");

        var identity = new WorkbookFileAccessIdentity(
            $" {path} ",
            " macos-security-scoped-bookmark ",
            " token ");

        identity.LocalPath.Should().Be(Path.GetFullPath(path));
        identity.BookmarkKind.Should().Be("macos-security-scoped-bookmark");
        identity.BookmarkPayload.Should().Be("token");
        identity.HasBookmark.Should().BeTrue();
    }

    [Fact]
    public void TryFromLocalPath_RejectsNonFileUri()
    {
        WorkbookFileAccessIdentity.TryFromLocalPath(
                "https://example.test/Book.fxl",
                out var identity)
            .Should()
            .BeFalse();
        identity.Should().BeNull();
    }

    [Fact]
    public void Serialize_OmitsDerivedAndNullBookmarkMetadata()
    {
        using var temp = new TestTemporaryDirectory();
        var identity = WorkbookFileAccessIdentity.FromLocalPath(Path.Combine(temp.Path, "Book.fxl"));

        var json = JsonSerializer.Serialize(identity);

        json.Should().Contain("LocalPath");
        json.Should().NotContain("HasBookmark");
        json.Should().NotContain("BookmarkKind");
        json.Should().NotContain("BookmarkPayload");
    }
}
