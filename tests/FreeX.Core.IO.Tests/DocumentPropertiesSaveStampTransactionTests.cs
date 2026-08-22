using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

public sealed class DocumentPropertiesSaveStampTransactionTests
{
    private static readonly DateTimeOffset OriginalModified =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SavedAt =
        new(2026, 8, 23, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Begin_StampsDeterministicTimeAndTrimmedDocumentAuthor()
    {
        var properties = Properties(author: "  Document Author  ");

        using var transaction = DocumentPropertiesSaveStampTransaction.Begin(
            properties,
            fallbackLastModifiedBy: "Product User",
            SavedAt,
            operatingSystemAuthor: "OS User");

        properties.Modified.Should().Be(SavedAt);
        properties.LastModifiedBy.Should().Be("Document Author");
    }

    [Fact]
    public void Commit_PreservesTheSuccessfulSaveStamp()
    {
        var properties = Properties(author: null);

        using (var transaction = DocumentPropertiesSaveStampTransaction.Begin(
                   properties,
                   fallbackLastModifiedBy: "Product User",
                   SavedAt,
                   operatingSystemAuthor: "  OS User  "))
        {
            transaction.Commit();
        }

        properties.Modified.Should().Be(SavedAt);
        properties.LastModifiedBy.Should().Be("OS User");
    }

    [Fact]
    public void DisposeWithoutCommit_RestoresBothPreviousValues()
    {
        var properties = Properties(author: "Document Author");

        using (DocumentPropertiesSaveStampTransaction.Begin(
                   properties,
                   fallbackLastModifiedBy: "Product User",
                   SavedAt,
                   operatingSystemAuthor: "OS User"))
        {
            properties.Modified.Should().Be(SavedAt);
        }

        properties.Modified.Should().Be(OriginalModified);
        properties.LastModifiedBy.Should().Be("Previous Author");
    }

    [Fact]
    public void WriterFailurePattern_RollsBackBeforeExceptionEscapes()
    {
        var properties = Properties(author: "Document Author");

        var act = () =>
        {
            using var transaction = DocumentPropertiesSaveStampTransaction.Begin(
                properties,
                fallbackLastModifiedBy: "Product User",
                SavedAt,
                operatingSystemAuthor: "OS User");
            throw new IOException("writer failed");
        };

        act.Should().Throw<IOException>();
        properties.Modified.Should().Be(OriginalModified);
        properties.LastModifiedBy.Should().Be("Previous Author");
    }

    [Theory]
    [InlineData("Document", "Operating System", "Fallback", "Document")]
    [InlineData(" ", " Operating System ", "Fallback", "Operating System")]
    [InlineData(null, " ", " Fallback ", "Fallback")]
    public void ResolveLastModifiedBy_UsesDocumentThenOperatingSystemThenProductFallback(
        string? documentAuthor,
        string? operatingSystemAuthor,
        string fallback,
        string expected)
    {
        DocumentPropertiesSaveStampTransaction.ResolveLastModifiedBy(
                documentAuthor,
                operatingSystemAuthor,
                fallback)
            .Should().Be(expected);
    }

    [Fact]
    public void CommitAfterDispose_IsRejectedAndCannotResurrectTheStamp()
    {
        var properties = Properties(author: "Document Author");
        var transaction = DocumentPropertiesSaveStampTransaction.Begin(
            properties,
            fallbackLastModifiedBy: "Product User",
            SavedAt,
            operatingSystemAuthor: "OS User");

        transaction.Dispose();
        var act = transaction.Commit;

        act.Should().Throw<ObjectDisposedException>();
        properties.Modified.Should().Be(OriginalModified);
        properties.LastModifiedBy.Should().Be("Previous Author");
    }

    private static DocumentProperties Properties(string? author) =>
        new()
        {
            Author = author,
            Modified = OriginalModified,
            LastModifiedBy = "Previous Author",
        };
}
