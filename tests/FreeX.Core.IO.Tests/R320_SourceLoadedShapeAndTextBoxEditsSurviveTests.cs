using FreeX.Core.Commands;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r320: asks r316's question of the other two drawing kinds -- and corrects what r316 concluded.
///
/// <para>r316 censused <see cref="PictureModel"/> field by field and reported that a source-loaded
/// picture discards a rename. That is true of the model in isolation and NOT true of the product:
/// every command that edits a drawing's format, text or name clears <c>IsSourceLoaded</c> first
/// (<c>DrawingShapeFormatCommands</c>, <c>TextBoxCommands</c>, <c>SelectionPaneCommands</c>), so the
/// writer regenerates the object instead of replaying its original XML. <c>SelectionPaneCommands</c>
/// says so in a comment that describes the exact discard r316 rediscovered. The mechanism was
/// already understood and already handled; r316's census measured a state no user can reach.</para>
///
/// <para>So this does not census. A blind census over 42 members sets Kind, geometry and text to
/// mutually invalid values at once and measures the writer's reaction to nonsense -- the first
/// attempt here did exactly that and made objects vanish. These tests instead make the edits the
/// product makes, the way it makes them, and check they survive. The one edit that does NOT clear
/// the flag is visibility, which is why r318 had to patch the source-loaded rewriter as well as the
/// writer, and why that case is pinned here for all three kinds.</para>
/// </summary>
public sealed class R320_SourceLoadedShapeAndTextBoxEditsSurviveTests
{
    private static Workbook RoundTrip(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    private static Workbook WithOneOfEach()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel { Name = "TextBox 1", Text = "before" });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape 1" });
        return workbook;
    }

    [Fact]
    public void ObjectsSurviveAPlainRoundTripAsSourceLoaded()
    {
        // The premise every test below rests on. It replaced a hand-rolled "rename the way the
        // commands do" test that set the fields directly: it did NOT do what the command does -- it
        // omitted the superseding step the command needs -- so it asserted a behaviour the product
        // never performs. Renames are exercised through the real command instead.
        var loaded = RoundTrip(WithOneOfEach());

        loaded.Sheets[0].TextBoxes.Should().ContainSingle().Which.IsSourceLoaded.Should().BeTrue();
        loaded.Sheets[0].DrawingShapes.Should().ContainSingle().Which.IsSourceLoaded.Should().BeTrue();
    }

    [Fact]
    public void EditingATextBoxsTextSurvivesWhenDoneAsTheCommandsDoIt()
    {
        var loaded = RoundTrip(WithOneOfEach());
        var textBox = loaded.Sheets[0].TextBoxes.Should().ContainSingle().Subject;

        textBox.IsSourceLoaded = false;
        textBox.Text = "after";

        RoundTrip(loaded).Sheets[0].TextBoxes.Should().ContainSingle().Which.Text.Should().Be("after");
    }

    /// <summary>
    /// Visibility is the edit that does NOT clear the flag -- SelectionPaneCommands.SetVisible only
    /// sets IsVisible -- so it is the one that genuinely depends on the source-loaded rewriter. r318
    /// fixed this for pictures; this pins that the picture case stays fixed and records what the
    /// other two kinds do today.
    /// </summary>
    [Fact]
    public void HidingASourceLoadedPictureSurvivesWithoutClearingTheFlag()
    {
        var authored = new Workbook("Book1");
        var sheet = authored.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Picture 1",
            ContentType = "image/png",
            ImageBytes =
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48,
                0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00,
                0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78,
                0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
            ],
        });

        var loaded = RoundTrip(authored);
        var picture = loaded.Sheets[0].Pictures.Should().ContainSingle().Subject;
        picture.IsSourceLoaded.Should().BeTrue();

        picture.IsVisible = false;   // SelectionPaneCommands.SetVisible does exactly this and no more

        RoundTrip(loaded).Sheets[0].Pictures.Should().ContainSingle()
            .Which.IsVisible.Should().BeFalse(
                "hiding is the one drawing edit that leaves IsSourceLoaded set, so it depends "
                + "entirely on the source-loaded rewriter patching cNvPr@hidden (r318)");
    }
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    /// <summary>
    /// r320: renaming a source-loaded drawing object DUPLICATED it.
    ///
    /// <para>R124 found that a rename which keeps IsSourceLoaded set is discarded on save, and fixed
    /// it by clearing the flag so the writer regenerates the object under its new name. But the
    /// merger decides which ORIGINAL anchors to drop by matching each model's CURRENT name
    /// (GetRewrittenSourceObjectNames), so after a rename nothing matches the original -- it was
    /// copied through beside the regenerated object, and the sheet gained a second copy bearing the
    /// old name. Again on every subsequent rename.</para>
    /// </summary>
    [Theory]
    [InlineData(SelectionPaneObjectKind.TextBox)]
    [InlineData(SelectionPaneObjectKind.Shape)]
    [InlineData(SelectionPaneObjectKind.Picture)]
    public void RenamingASourceLoadedObjectDoesNotLeaveASecondCopyBehind(SelectionPaneObjectKind kind)
    {
        var loaded = RoundTrip(WithOneOfEachIncludingPicture());
        var sheet = loaded.Sheets[0];
        var (id, oldName) = kind switch
        {
            SelectionPaneObjectKind.TextBox => (sheet.TextBoxes.Single().Id, "TextBox 1"),
            SelectionPaneObjectKind.Shape => (sheet.DrawingShapes.Single().Id, "Shape 1"),
            _ => (sheet.Pictures.Single().Id, "Picture 1"),
        };

        var context = new TestCommandContext(loaded);
        var command = new RenameSelectionPaneObjectCommand(sheet.Id, kind, id, "Renamed");
        command.Apply(context).Success.Should().BeTrue();

        var resaved = RoundTrip(loaded);
        var after = resaved.Sheets[0];
        var names = kind switch
        {
            SelectionPaneObjectKind.TextBox => after.TextBoxes.Select(t => t.Name).ToList(),
            SelectionPaneObjectKind.Shape => after.DrawingShapes.Select(s => s.Name).ToList(),
            _ => after.Pictures.Select(p => p.Name).ToList(),
        };

        names.Should().ContainSingle($"renaming must not leave a second copy of the {kind}")
            .Which.Should().Be("Renamed");
        names.Should().NotContain(oldName);
    }

    /// <summary>
    /// Undoing the rename must restore the object, not delete its source XML: the fix supersedes the
    /// original anchor by name, so Revert has to withdraw that.
    /// </summary>
    [Fact]
    public void UndoingTheRenameRestoresTheObject()
    {
        var loaded = RoundTrip(WithOneOfEachIncludingPicture());
        var sheet = loaded.Sheets[0];
        var context = new TestCommandContext(loaded);
        var command = new RenameSelectionPaneObjectCommand(
            sheet.Id, SelectionPaneObjectKind.TextBox, sheet.TextBoxes.Single().Id, "Renamed");

        command.Apply(context).Success.Should().BeTrue();
        command.Revert(context);

        sheet.DeletedSourceDrawingObjectNames.Should().NotContain("TextBox 1",
            "undo restored the original name, so the original anchor must stop being superseded");

        var resaved = RoundTrip(loaded);
        resaved.Sheets[0].TextBoxes.Should().ContainSingle().Which.Name.Should().Be("TextBox 1");
    }

    private static Workbook WithOneOfEachIncludingPicture()
    {
        var workbook = WithOneOfEach();
        var sheet = workbook.Sheets[0];
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Picture 1",
            ContentType = "image/png",
            ImageBytes =
            [
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48,
                0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00,
                0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78,
                0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
            ],
        });
        return workbook;
    }

}