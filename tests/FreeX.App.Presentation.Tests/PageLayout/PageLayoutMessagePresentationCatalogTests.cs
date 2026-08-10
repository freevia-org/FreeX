using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutMessagePresentationCatalogTests
{
    [Fact]
    public void HeaderFooterPictureOpenFailure_PreservesExistingMessageSemantics()
    {
        var presentation = PageLayoutMessagePresentationCatalog
            .DescribeHeaderFooterPictureOpenFailure("bad image");

        presentation.Message.ResourceKey.Should().Be("MainWindowMessage_OpenFileFailed");
        presentation.Message.Arguments.Should().Equal("bad image");
        presentation.Title.ResourceKey.Should().Be("HeaderFooterPicture_InsertPictureTitle");
        presentation.Buttons.Should().Be(UserMessageButtons.Ok);
        presentation.Icon.Should().Be(UserMessageIcon.Warning);
    }

    [Fact]
    public void NativePrintFailure_PreservesExistingMessageSemantics()
    {
        var presentation = PageLayoutMessagePresentationCatalog
            .DescribeNativePrintFailure("printer offline");

        presentation.Message.ResourceKey.Should().Be("MainWindowMessage_PrintFailed");
        presentation.Message.Arguments.Should().Equal("printer offline");
        presentation.Title.ResourceKey.Should().Be("MainWindowMessage_PrintFailedTitle");
        presentation.Buttons.Should().Be(UserMessageButtons.Ok);
        presentation.Icon.Should().Be(UserMessageIcon.Error);
    }
}
