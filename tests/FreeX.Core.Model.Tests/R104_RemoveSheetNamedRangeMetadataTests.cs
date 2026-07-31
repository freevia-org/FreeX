using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the R104 finding: <see cref="RemoveSheetCommand"/>.Apply (via
/// <c>Workbook.RemoveSheet</c> -&gt; <c>Workbook.RemoveNamedRangesForSheet</c>) correctly keeps a
/// defined name alive with RefersTo rewritten to "#REF!" when the sheet it targets is deleted -
/// matching real Excel, which never drops the Name Manager entry outright - but used to
/// permanently discard the name's <see cref="NamedRangeMetadata"/> (Hidden flag and Comment) in
/// the process. The conversion called <c>Workbook.RemoveNamedRange</c> / <c>RemoveScopedNamedRange</c>,
/// both of which unconditionally wipe the metadata dictionary entry for the name before it is
/// re-homed into NamedFormulas/ScopedNamedFormulas, so the metadata could never be recovered once
/// the sheet delete had gone through - even though the name itself lived on.
/// </summary>
public sealed class R104_RemoveSheetNamedRangeMetadataTests
{
    [Fact]
    public void RemoveSheetCommand_PreservesHiddenAndCommentOnGlobalNameConvertedToRefError()
    {
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        var range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 10, 1));
        workbook.DefineNamedRange(
            "HiddenTotals",
            range,
            new NamedRangeMetadata("Workbook", "Pivot cache helper", Hidden: true));

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(data.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.NamedFormulas["HiddenTotals"].Should().Be("#REF!");
        workbook.TryGetNamedRangeMetadata("HiddenTotals", out var metadata).Should().BeTrue(
            because: "the name's metadata must survive its conversion from a NamedRanges entry " +
                     "into a NamedFormulas #REF! entry, not be discarded along with the range");
        metadata.Hidden.Should().BeTrue();
        metadata.Comment.Should().Be("Pivot cache helper");
    }

    [Fact]
    public void RemoveSheetCommand_PreservesHiddenAndCommentOnCrossSheetScopedNameConvertedToRefError()
    {
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 5, 1));
        workbook.DefineNamedRange(
            "ScopedHiddenName",
            range,
            new NamedRangeMetadata("Report", "Scoped comment", Hidden: true),
            report.Id);

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(data.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.ScopedNamedFormulas[("ScopedHiddenName", report.Id)].Should().Be("#REF!");
        workbook.TryGetScopedNamedRangeMetadata("ScopedHiddenName", report.Id, out var metadata).Should().BeTrue(
            because: "a cross-sheet-scoped name's metadata must survive its #REF! conversion " +
                     "exactly like the workbook-global case");
        metadata.Hidden.Should().BeTrue();
        metadata.Comment.Should().Be("Scoped comment");
    }

    [Fact]
    public void RemoveSheetCommand_ScopeSheetItselfDeleted_DropsScopedNameAndMetadataEntirely()
    {
        // Sibling/no-regression case: a name whose SCOPE sheet (not merely its target range) is
        // the deleted sheet has no sheet left to be scoped to and must be dropped entirely -
        // metadata included - unlike the cross-sheet-target case above which survives as #REF!.
        var workbook = new Workbook("Test");
        var scoped = workbook.AddSheet("Scoped");
        workbook.AddSheet("Other");
        var range = new GridRange(new CellAddress(scoped.Id, 1, 1), new CellAddress(scoped.Id, 3, 1));
        workbook.DefineNamedRange(
            "LocalName",
            range,
            new NamedRangeMetadata("Scoped", "Local comment", Hidden: true),
            scoped.Id);

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(scoped.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.ScopedNamedRanges.Should().NotContainKey(("LocalName", scoped.Id));
        workbook.ScopedNamedFormulas.Should().NotContainKey(("LocalName", scoped.Id));
        workbook.TryGetScopedNamedRangeMetadata("LocalName", scoped.Id, out _).Should().BeFalse(
            because: "a name scoped to the deleted sheet itself has nowhere to live and must be " +
                     "removed entirely, metadata included - it is not converted to #REF!");
    }

    [Fact]
    public void RemoveSheetCommand_PlainNameWithNoMetadata_ConvertsCleanlyWithNoSpuriousMetadata()
    {
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        var range = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 3, 1));
        workbook.DefineNamedRange("PlainName", range);

        var ctx = new TestCommandContext(workbook);
        new RemoveSheetCommand(data.Id).Apply(ctx).Success.Should().BeTrue();

        workbook.NamedFormulas["PlainName"].Should().Be("#REF!");
        workbook.TryGetNamedRangeMetadata("PlainName", out var metadata).Should().BeTrue();
        metadata.Hidden.Should().BeFalse();
    }
}
