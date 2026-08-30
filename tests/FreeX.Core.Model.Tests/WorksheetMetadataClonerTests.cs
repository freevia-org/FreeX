using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class WorksheetMetadataClonerTests
{
    [Fact]
    public void ClonePageBreaks_DeepClonesRootAndNestedAttributes()
    {
        var source = new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = Attributes("root", StringComparer.OrdinalIgnoreCase),
            BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
            {
                [7] = Attributes("break", StringComparer.OrdinalIgnoreCase)
            }
        };

        var clone = WorksheetMetadataCloner.ClonePageBreaks(source)!;

        AssertRootAttributesAreIsolated(source.NativeAttributes, clone.NativeAttributes);
        clone.BreakNativeAttributes.Should().NotBeSameAs(source.BreakNativeAttributes);
        AssertNestedAttributesAreIsolated(
            source.BreakNativeAttributes[7],
            clone.BreakNativeAttributes[7]);
    }

    [Fact]
    public void CloneCellWatches_DeepClonesAttributesAndNormalizesComparers()
    {
        var source = new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes = Attributes("root", StringComparer.OrdinalIgnoreCase),
            WatchNativeAttributes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                ["Sheet1!A1"] = Attributes("watch", StringComparer.OrdinalIgnoreCase)
            }
        };

        var clone = WorksheetMetadataCloner.CloneCellWatches(source)!;

        AssertRootAttributesAreIsolated(source.NativeAttributes, clone.NativeAttributes);
        clone.WatchNativeAttributes.Should().NotBeSameAs(source.WatchNativeAttributes);
        clone.WatchNativeAttributes.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
        clone.WatchNativeAttributes.ContainsKey("sheet1!a1").Should().BeTrue();
        AssertNestedAttributesAreIsolated(
            source.WatchNativeAttributes["Sheet1!A1"],
            clone.WatchNativeAttributes["Sheet1!A1"]);
    }

    [Fact]
    public void CloneIgnoredErrors_DeepClonesAttributesAndNormalizesComparers()
    {
        var source = new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes = Attributes("root", StringComparer.OrdinalIgnoreCase),
            ErrorNativeAttributes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                ["A1:A9"] = Attributes("error", StringComparer.OrdinalIgnoreCase)
            }
        };

        var clone = WorksheetMetadataCloner.CloneIgnoredErrors(source)!;

        AssertRootAttributesAreIsolated(source.NativeAttributes, clone.NativeAttributes);
        clone.ErrorNativeAttributes.Should().NotBeSameAs(source.ErrorNativeAttributes);
        clone.ErrorNativeAttributes.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
        clone.ErrorNativeAttributes.ContainsKey("a1:a9").Should().BeTrue();
        AssertNestedAttributesAreIsolated(
            source.ErrorNativeAttributes["A1:A9"],
            clone.ErrorNativeAttributes["A1:A9"]);
    }

    [Fact]
    public void CloneMethods_PreserveNullAndNonNullEmptyShapes()
    {
        WorksheetMetadataCloner.ClonePageBreaks(null).Should().BeNull();
        WorksheetMetadataCloner.CloneCellWatches(null).Should().BeNull();
        WorksheetMetadataCloner.CloneIgnoredErrors(null).Should().BeNull();

        var pageBreaks = WorksheetMetadataCloner.ClonePageBreaks(new WorksheetPageBreaksMetadataModel());
        var cellWatches = WorksheetMetadataCloner.CloneCellWatches(new WorksheetCellWatchesMetadataModel());
        var ignoredErrors = WorksheetMetadataCloner.CloneIgnoredErrors(new WorksheetIgnoredErrorsMetadataModel());

        pageBreaks.Should().NotBeNull();
        pageBreaks!.BreakNativeAttributes.Should().BeEmpty();
        cellWatches.Should().NotBeNull();
        cellWatches!.WatchNativeAttributes.Should().BeEmpty();
        ignoredErrors.Should().NotBeNull();
        ignoredErrors!.ErrorNativeAttributes.Should().BeEmpty();
    }

    [Fact]
    public void ClonePolicy_HasOneModelOwnedImplementationWithoutLinqProjectionCopies()
    {
        var clonerSource = ModelSourceTestSupport.ReadModelSource("WorksheetMetadataCloner.cs");
        var sheetCloneSource = ModelSourceTestSupport.ReadModelSource("Sheet.Clone.cs");
        var addressStateSource = ModelSourceTestSupport.ReadCommandsSource("RowColumnShiftHelpers.AddressState.cs");

        clonerSource.Should().Contain("internal static class WorksheetMetadataCloner");
        clonerSource.Should().Contain("source.Count");
        clonerSource.Should().NotContain(".ToDictionary(");
        sheetCloneSource.Should().NotContain("ClonePageBreaksMetadata(");
        sheetCloneSource.Should().NotContain("CloneCellWatchesMetadata(");
        sheetCloneSource.Should().NotContain("CloneIgnoredErrorsMetadata(");
        addressStateSource.Should().NotContain("ClonePageBreaksMetadata(");
        addressStateSource.Should().NotContain("CloneCellWatchesMetadata(");
        addressStateSource.Should().NotContain("CloneIgnoredErrorsMetadata(");
    }

    private static Dictionary<string, string> Attributes(
        string key,
        StringComparer comparer) =>
        new(comparer) { [key] = key };

    private static void AssertRootAttributesAreIsolated(
        Dictionary<string, string> source,
        Dictionary<string, string> clone)
    {
        clone.Should().NotBeSameAs(source);
        clone.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("root", "root"));
        clone.Comparer.Should().BeSameAs(StringComparer.Ordinal);
        clone.ContainsKey("ROOT").Should().BeFalse();

        source["root"] = "changed";
        clone["root"].Should().Be("root");
    }

    private static void AssertNestedAttributesAreIsolated(
        Dictionary<string, string> source,
        Dictionary<string, string> clone)
    {
        clone.Should().NotBeSameAs(source);
        clone.Comparer.Should().BeSameAs(StringComparer.Ordinal);

        var key = clone.Keys.Single();
        clone.ContainsKey(key.ToUpperInvariant()).Should().BeFalse();
        source[key] = "changed";
        clone[key].Should().Be(key);
    }
}
