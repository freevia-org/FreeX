using Free.Shared.Shell;

namespace FreeX.App.Host.Logic.Tests;

public sealed class LegalNoticesDialogMetricsTests
{
    [Fact]
    public void Shared_metrics_keep_the_legal_notice_surface_coherent_across_hosts()
    {
        Assert.Equal(840, LegalNoticesDialogMetrics.Width);
        Assert.Equal(620, LegalNoticesDialogMetrics.Height);
        Assert.Equal(620, LegalNoticesDialogMetrics.MinWidth);
        Assert.Equal(420, LegalNoticesDialogMetrics.MinHeight);
        Assert.Equal(16, LegalNoticesDialogMetrics.ContentMargin);
        Assert.Equal(10, LegalNoticesDialogMetrics.IntroBottomMargin);
        Assert.Equal(12, LegalNoticesDialogMetrics.ActionRowTopMargin);
        Assert.Equal(12, LegalNoticesDialogMetrics.TextFontSize);
        Assert.Equal(16, LegalNoticesDialogMetrics.TextLineHeight);
        Assert.Equal(8, LegalNoticesDialogMetrics.TextPadding);
        Assert.Equal(280, LegalNoticesDialogMetrics.TextMinHeight);
        Assert.Equal(21, LegalNoticesDialogMetrics.TabControlHeight);
    }
}
