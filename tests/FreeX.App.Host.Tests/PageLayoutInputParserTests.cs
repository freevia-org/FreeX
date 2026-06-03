using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PageLayoutInputParserTests
{
    private static void AssertRange(WorksheetRepeatRange? range, int? expectedStart, int? expectedEnd)
    {
        if (expectedStart is null)
        {
            range.Should().BeNull();
            return;
        }

        range.Should().NotBeNull();
        range!.Value.Start.Should().Be((uint)expectedStart.Value);
        range.Value.End.Should().Be((uint)expectedEnd!.Value);
    }
}
