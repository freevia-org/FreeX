using System.Windows;
using Free.Shared.Testing;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReusableWpfWindowSessionTests
{
    [Fact]
    public void Run_ReusesOneWindowAndResetsAroundEveryBorrower()
    {
        var created = 0;
        var resets = 0;
        Window? firstWindow = null;
        using var session = new ReusableWpfWindowSession<Window>(
            () =>
            {
                created++;
                return new Window();
            },
            _ => resets++);

        session.Run(window => firstWindow = window);
        session.Run(window => window.Should().BeSameAs(firstWindow));

        created.Should().Be(1);
        resets.Should().Be(4);
    }
}
