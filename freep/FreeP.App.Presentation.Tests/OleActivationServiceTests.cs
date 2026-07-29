using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class OleActivationServiceTests
{
    [Fact]
    public void TryActivate_EmptyPayload_ReturnsFalse()
    {
        OleActivationService.TryActivate(new OleObjectInfo()).Should().BeFalse();
    }

    [Theory]
    [InlineData(".XLSX", "xlsx")]
    [InlineData("docx", "docx")]
    [InlineData("../../payload", "bin")]
    [InlineData("", "bin")]
    public void ResolveExtension_NormalizesEmbeddedExtension(string extension, string expected)
    {
        OleActivationService.ResolveExtension(new OleObjectInfo
        {
            EmbeddedExtension = extension,
        }).Should().Be(expected);
    }

    [Fact]
    public void ResolveExtension_UsesContentTypeWhenExtensionIsUnknown()
    {
        OleActivationService.ResolveExtension(new OleObjectInfo
        {
            EmbeddedExtension = "bin",
            EmbeddedContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        }).Should().Be("xlsx");
    }

    [Fact]
    public void TryCommitEditedPayload_ReplacesChangedBytes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"freep-ole-test-{Guid.NewGuid():N}.bin");
        try
        {
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, [4, 5, 6, 7]);
            var ole = new OleObjectInfo { EmbeddedBytes = original.ToArray() };

            OleActivationService.TryCommitEditedPayload(ole, path, original)
                .Should().BeTrue();
            ole.EmbeddedBytes.Should().Equal(4, 5, 6, 7);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void TryCommitEditedPayload_LeavesModelUntouchedForUnchangedOrEmptyPayload()
    {
        string unchangedPath = Path.Combine(Path.GetTempPath(), $"freep-ole-test-{Guid.NewGuid():N}.bin");
        string emptyPath = Path.Combine(Path.GetTempPath(), $"freep-ole-test-{Guid.NewGuid():N}.bin");
        try
        {
            byte[] original = [1, 2, 3];
            var unchanged = new OleObjectInfo { EmbeddedBytes = original.ToArray() };
            File.WriteAllBytes(unchangedPath, original);
            OleActivationService.TryCommitEditedPayload(unchanged, unchangedPath, original)
                .Should().BeFalse();
            unchanged.EmbeddedBytes.Should().Equal(original);

            var empty = new OleObjectInfo { EmbeddedBytes = original.ToArray() };
            File.WriteAllBytes(emptyPath, []);
            OleActivationService.TryCommitEditedPayload(empty, emptyPath, original)
                .Should().BeFalse();
            empty.EmbeddedBytes.Should().Equal(original);
        }
        finally
        {
            try { File.Delete(unchangedPath); } catch { }
            try { File.Delete(emptyPath); } catch { }
        }
    }
}
