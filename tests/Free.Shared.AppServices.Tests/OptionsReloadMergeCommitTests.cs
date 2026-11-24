namespace Free.Shared.AppServices.Tests;

public sealed class OptionsReloadMergeCommitTests
{
    private sealed class Opts : INormalizableApplicationOptions
    {
        public int Value { get; set; }

        public int Other { get; set; }

        public int NormalizeCount { get; private set; }

        public void Normalize() => NormalizeCount++;
    }

    [Fact]
    public void WithoutReload_PersistsLiveInstanceAfterApply()
    {
        var live = new Opts();
        Opts? persisted = null;

        var ok = OptionsReloadMergeCommit.Commit(
            live,
            () => throw new InvalidOperationException("no snapshot without reload"),
            () => live.Value = 5,
            () => throw new InvalidOperationException("no edited capture without reload"),
            o => { persisted = o; return true; },
            reloadFresh: null,
            (_, _, _) => throw new InvalidOperationException("no merge without reload"),
            (_, _) => throw new InvalidOperationException("no copy without reload"));

        ok.Should().BeTrue();
        persisted.Should().BeSameAs(live);
        live.Value.Should().Be(5);
    }

    [Fact]
    public void WithReload_MergesOntoFreshPersistsFreshAndCopiesBackOnlyOnSuccess()
    {
        var live = new Opts { Value = 1, Other = 1 };
        var fresh = new Opts { Value = 1, Other = 9 };
        Opts? persisted = null;

        var ok = OptionsReloadMergeCommit.Commit(
            live,
            () => new Opts { Value = live.Value, Other = live.Other },
            () => live.Value = 7,
            () => new Opts { Value = 7, Other = 1 },
            o => { persisted = o; return true; },
            () => fresh,
            (f, snap, edited) =>
            {
                if (edited.Value != snap.Value)
                    f.Value = edited.Value;
            },
            (l, f) => { l.Value = f.Value; l.Other = f.Other; });

        ok.Should().BeTrue();
        persisted.Should().BeSameAs(fresh);
        fresh.NormalizeCount.Should().Be(1);
        fresh.Value.Should().Be(7);
        fresh.Other.Should().Be(9, "a field this session never edited keeps the freshest on-disk value");
        live.Other.Should().Be(9);
    }

    [Fact]
    public void WithReload_FailedPersistDoesNotCopyFreshIntoLive()
    {
        var live = new Opts { Other = 1 };
        var fresh = new Opts { Other = 9 };

        var ok = OptionsReloadMergeCommit.Commit(
            live,
            () => new Opts(),
            () => { },
            () => new Opts(),
            _ => false,
            () => fresh,
            (_, _, _) => { },
            (_, _) => throw new InvalidOperationException("must not copy after a failed save"));

        ok.Should().BeFalse();
        live.Other.Should().Be(1);
    }

    [Theory]
    [InlineData("freew", "FreeW.App.Presentation", "Options", "FreeWOptionsRuntimeSession.cs")]
    [InlineData("freep", "FreeP.App.Presentation", "Options", "FreePOptionsPolicy.cs")]
    public void SisterRuntimeSessions_UseTheSharedCommitFlowInsteadOfAReimplementation(
        string app,
        string project,
        string folder,
        string file)
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(app, project, folder, file));

        source.Should().Contain("OptionsReloadMergeCommit.Commit(");
        source.Should().NotContain("reloadFresh()");
    }
}
