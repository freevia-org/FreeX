using System.Reflection;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// XlsxFileAdapter recovers from three known ClosedXML load failures by recognizing each one and
/// retrying with the offending metadata stripped. If a detector stops matching, the recovery stops
/// firing and files that Excel opens fail to load -- silently, because the workbook simply throws
/// as it did before the recovery existed.
///
/// Two of the three used to be recognized by text that is not part of any contract: one by a
/// localizable BCL message, one by a stack frame the JIT may inline away (which is how the
/// shared-formula detector was observed to miss under a loaded test gate). These tests pin the
/// framework behaviour the replacements rely on, and the replacements themselves.
/// </summary>
public sealed class XlsxLoadRecoveryDetectorContractTests
{
    [Fact]
    public void LinqEmptySequenceFailures_AllOriginateFromTheLinqThrowHelper()
    {
        // The relationship-lookup detector keys on this type instead of on the message, so that it
        // keeps working on a runtime with localized framework resources.
        DeclaringTypeOfThrowFrom(() => Array.Empty<int>().First(x => x == 1))
            .Should().Be("System.Linq.ThrowHelper");
        DeclaringTypeOfThrowFrom(() => Array.Empty<int>().First())
            .Should().Be("System.Linq.ThrowHelper");
        DeclaringTypeOfThrowFrom(() => Array.Empty<int>().Single())
            .Should().Be("System.Linq.ThrowHelper");
    }

    [Fact]
    public void EmptySequenceFailures_UseMoreThanOneMessage_SoMessageMatchingIsNotSufficient()
    {
        // Documents why the old check was incomplete even in English: it matched only the
        // First(predicate) wording, so Single()/First() on an empty sequence never triggered the
        // pivot-stripping recovery at all.
        var predicateMessage = MessageOfThrowFrom(() => Array.Empty<int>().First(x => x == 1));
        var emptyMessage = MessageOfThrowFrom(() => Array.Empty<int>().Single());

        predicateMessage.Should().NotBe(emptyMessage);
        predicateMessage.Should().Contain("no matching element");
        emptyMessage.Should().Contain("no elements");
    }

    [Fact]
    public void RecoveryDetectors_DoNotDependOnLocalizableMessagesOrInlinableFrames()
    {
        var adapterSource = File.ReadAllText(RepositoryFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));

        adapterSource.Should().Contain(
            "private const string LinqThrowHelperTypeName = \"System.Linq.ThrowHelper\";",
            "the relationship-lookup detector must recognize LINQ's empty-sequence throws by origin");
        adapterSource.Should().Contain(
            "IsClosedXmlAssembly(argument.TargetSite?.DeclaringType?.Assembly)",
            "the shared-formula detector must recognize the throwing assembly, not one frame name");
        adapterSource.Should().Contain(
            "current.TargetSite?.Name.Contains(\"ConditionalFormatting\", StringComparison.Ordinal)",
            "the conditional-formatting detector must check the throwing method, not only the trace");
    }

    private static string? DeclaringTypeOfThrowFrom(Action action) =>
        CaptureThrow(action).TargetSite?.DeclaringType?.FullName;

    private static string MessageOfThrowFrom(Action action) => CaptureThrow(action).Message;

    private static Exception CaptureThrow(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The action was expected to throw.");
    }

    private static string RepositoryFile(params string[] parts) =>
        Path.Combine(
            [
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
                .. parts,
            ]);
}
