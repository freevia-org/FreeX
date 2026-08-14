using Free.Shared.AppServices;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAssetImportOutcomePlannerTests
{
    private static readonly SisterAppFileTextSpec FileText = PresentationFileTextResources.Presentation;

    [Fact]
    public void Plan_PreservesSuccessfulStatusPolicies()
    {
        var result = Result(PresentationAssetImportStatus.Succeeded, sourceName: "photo.png");

        PresentationAssetImportOutcomePlanner.Plan(result, FileText)
            .Should().Be(PresentationAssetImportOutcomePresentation.Empty);

        var inserted = PresentationAssetImportOutcomePlanner.Plan(
            result,
            FileText,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true));
        inserted.StatusText.Should().Be("Inserted photo.png");
        inserted.Message.Should().BeNull();

        var explicitStatus = PresentationAssetImportOutcomePlanner.Plan(
            result,
            FileText,
            new PresentationAssetImportOutcomePolicy(
                ShowInsertedStatus: true,
                SuccessStatusText: "Picture bullet applied."));
        explicitStatus.StatusText.Should().Be("Picture bullet applied.");
        explicitStatus.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(PresentationAssetImportStatus.Unavailable, "Insert picture unavailable.")]
    [InlineData(PresentationAssetImportStatus.Failed, "Insert picture failed: read failed")]
    public void Plan_MapsNonSuccessOutcomesToExistingStatusText(
        PresentationAssetImportStatus status,
        string expected)
    {
        var presentation = PresentationAssetImportOutcomePlanner.Plan(
            Result(status, message: "read failed"),
            FileText);

        presentation.StatusText.Should().Be(expected);
        presentation.Message.Should().BeNull();
    }

    [Fact]
    public void Plan_SmartArtPanePolicyPreservesExistingPaneFailureText()
    {
        var result = new PresentationAssetImportResult(
            PresentationAssetImportRequest.Create(PresentationAssetImportKind.SmartArtPicture),
            PresentationAssetImportStatus.Failed,
            Message: "read failed");

        var presentation = PresentationAssetImportOutcomePlanner.Plan(
            result,
            FileText,
            PresentationAssetImportOutcomePolicy.SmartArtPane);

        presentation.StatusText.Should().Be("Could not replace SmartArt picture: read failed");
        presentation.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(PresentationAssetImportStatus.Cancelled)]
    [InlineData(PresentationAssetImportStatus.NotApplied)]
    public void Plan_KeepsCancelledAndNotAppliedOutcomesSilent(
        PresentationAssetImportStatus status)
    {
        var presentation = PresentationAssetImportOutcomePlanner.Plan(
            Result(status, message: "No active paragraph"),
            FileText);

        presentation.Should().Be(PresentationAssetImportOutcomePresentation.Empty);
    }

    [Fact]
    public void Plan_MapsZoomFailureToExactTypedModalRequest()
    {
        var request = PresentationAssetImportRequest.Create(
            PresentationAssetImportKind.ZoomCoverImage);
        var result = new PresentationAssetImportResult(
            request,
            PresentationAssetImportStatus.Failed,
            Message: "Could not read cover.png");

        var presentation = PresentationAssetImportOutcomePlanner.Plan(
            result,
            FileText,
            PresentationAssetImportOutcomePolicy.ModalError);

        presentation.StatusText.Should().BeNull();
        presentation.Message.Should().NotBeNull();
        presentation.Message!.Message.Should().Be("Could not read cover.png");
        presentation.Message.Title.Should().Be(ZoomCoverImagePlanner.DialogTitle);
        presentation.Message.Buttons.Should().Be(UserMessageButtons.Ok);
        presentation.Message.Kind.Should().Be(UserMessageIcon.Error);
        presentation.Message.Owner.IsDefault.Should().BeTrue();
    }

    [Theory]
    [InlineData(PresentationAssetImportStatus.Cancelled)]
    [InlineData(PresentationAssetImportStatus.Unavailable)]
    [InlineData(PresentationAssetImportStatus.NotApplied)]
    public void Plan_ModalPolicyDoesNotAddFeedbackForPreviouslySilentOutcomes(
        PresentationAssetImportStatus status)
    {
        var presentation = PresentationAssetImportOutcomePlanner.Plan(
            Result(status, message: "not available"),
            FileText,
            PresentationAssetImportOutcomePolicy.ModalError);

        presentation.Should().Be(PresentationAssetImportOutcomePresentation.Empty);
    }

    [Fact]
    public void RenderersOnlyRealizePortableAssetImportPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sharedSession = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationAssetImportHostSession.cs");
        var wpfAdapter = Read(root, "freep", "FreeP.App.Host", "MainWindow.AssetImports.cs");
        var wpfWindow = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var avaloniaAdapter = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.AssetImports.cs");
        var avaloniaWindow = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");

        sharedSession.Should().Contain("PresentationAssetImportOutcomePlanner.Plan(")
            .And.Contain("messageService.ShowMessageAsync(message, cancellationToken)")
            .And.Contain("new PresentationAssetImportWorkflow(")
            .And.Contain("new PresentationAssetImportExecutionPort(_editor, callbacks)");
        wpfAdapter.Should().Contain("PresentationAssetImportHostSession")
            .And.Contain("Action<string>? statusTarget = null")
            .And.Contain("_messageService ?? new WpfUserMessageService(this)")
            .And.Contain("AssetImportSession.MaterializeOutcomeAsync(")
            .And.NotContain("new PresentationAssetImportWorkflow(")
            .And.NotContain("PresentationAssetImportOutcomePlanner.Plan(")
            .And.NotContain("MessageBox.Show(");
        wpfWindow.Should().Contain("PresentationAssetImportOutcomePolicy.ModalError")
            .And.Contain("PresentationAssetImportOutcomePolicy.SmartArtPane")
            .And.Contain("statusText => _smartArtTextPaneMessage.Text = statusText")
            .And.NotContain("Could not replace SmartArt picture:")
            .And.NotContain("MessageBox.Show(this, result.Message");
        avaloniaAdapter.Should().Contain("PresentationAssetImportHostSession")
            .And.Contain("Action<string>? statusTarget = null")
            .And.Contain("_messageService ?? new AvaloniaUserMessageService(this)")
            .And.Contain("AssetImportSession.MaterializeOutcomeAsync(")
            .And.NotContain("new PresentationAssetImportWorkflow(")
            .And.NotContain("PresentationAssetImportOutcomePlanner.Plan(")
            .And.NotContain("switch (result.Status)")
            .And.NotContain("SisterAppFileTextPlanner.FormatCommand");
        avaloniaWindow.Should().Contain("PresentationAssetImportOutcomePolicy.ModalError")
            .And.Contain("PresentationAssetImportOutcomePolicy.SmartArtPane")
            .And.Contain("statusText => _smartArtTextPaneMessage.Text = statusText")
            .And.Contain("IUserMessageService? messageService = null")
            .And.NotContain("Could not replace SmartArt picture:");
    }

    private static PresentationAssetImportResult Result(
        PresentationAssetImportStatus status,
        string? sourceName = null,
        string? message = null) =>
        new(
            PresentationAssetImportRequest.Create(PresentationAssetImportKind.Picture),
            status,
            sourceName,
            message);

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
