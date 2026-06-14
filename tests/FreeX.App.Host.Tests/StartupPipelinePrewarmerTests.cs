using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StartupPipelinePrewarmerTests
{
    [Fact]
    public void Prewarm_RoundTripsRepresentativeWorkbookWithoutThrowing()
    {
        // The prewarm runs the real save -> load -> patch-save pipeline on a representative workbook
        // to pay the cold-process JIT/static-init cost before the first user open.  If the
        // representative workbook or the pipeline call chain ever breaks, the background prewarm would
        // silently swallow it and the optimization would become a no-op; this guards against that.
        var act = () => StartupPipelinePrewarmer.RunPrewarmForTests();

        act.Should().NotThrow();
    }
}
