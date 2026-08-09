using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarOptionUpdateWorkflowTests
{
    [Fact]
    public void ApplyAndSave_MutatesRecognizedOptionAndPersistsOnce()
    {
        var options = new AppOptions { StatusBarShowMaximum = false };
        var saveCalls = 0;

        var result = StatusBarOptionUpdateWorkflow.ApplyAndSave(
            options,
            StatusBarOptionTags.Maximum,
            isVisible: true,
            saved =>
            {
                saveCalls++;
                saved.Should().BeSameAs(options);
                return true;
            });

        result.Succeeded.Should().BeTrue();
        result.Visibility.Maximum.Should().BeTrue();
        options.StatusBarShowMaximum.Should().BeTrue();
        saveCalls.Should().Be(1);
    }

    [Fact]
    public void ApplyAndSave_DoesNotPersistUnknownOption()
    {
        var saveCalls = 0;

        var result = StatusBarOptionUpdateWorkflow.ApplyAndSave(
            new AppOptions(),
            "Unknown",
            isVisible: true,
            _ =>
            {
                saveCalls++;
                return true;
            });

        result.IsRecognized.Should().BeFalse();
        result.IsPersisted.Should().BeFalse();
        saveCalls.Should().Be(0);
    }

    [Fact]
    public void ApplyToFreshOptionsAndSave_LoadsBeforeMutation()
    {
        var options = new AppOptions { StatusBarShowMinimum = false };

        var result = StatusBarOptionUpdateWorkflow.ApplyToFreshOptionsAndSave(
            StatusBarOptionTags.Minimum,
            isVisible: true,
            load: () => options,
            save: saved => saved.StatusBarShowMinimum);

        result.Succeeded.Should().BeTrue();
        result.Visibility.Minimum.Should().BeTrue();
    }
}
