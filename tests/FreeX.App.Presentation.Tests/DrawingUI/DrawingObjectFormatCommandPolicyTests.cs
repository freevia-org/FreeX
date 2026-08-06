using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectFormatCommandPolicyTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void ResolveSelectedFormatTarget_ReturnsTextBoxValuesAndTarget()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 2),
            Width = 144,
            Height = 72,
            RotationDegrees = 15,
            AltText = "Callout"
        };
        sheet.TextBoxes.Add(textBox);

        var result = DrawingObjectFormatCommandPolicy.ResolveSelectedFormatTarget(
            sheet,
            SelectionPaneObjectKind.TextBox,
            textBox.Id);

        result.HasTarget.Should().BeTrue();
        result.Target!.Kind.Should().Be(DrawingObjectTargetKind.TextBox);
        result.Target.Id.Should().Be(textBox.Id);
        result.Target.Target.Anchor.Should().Be(textBox.Anchor);
        result.Target.Values.Width.Should().Be(144);
        result.Target.Values.Height.Should().Be(72);
        result.Target.Values.RotationDegrees.Should().Be(15);
        result.Target.Values.LockAspectRatioSupported.Should().BeFalse();
        result.Target.Values.AltText.Should().Be("Callout");
    }

    [Fact]
    public void BuildFormatCommands_AppliesTextBoxResizeRotateAndAltText()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            RotationDegrees = 10,
            AltText = "old"
        };
        sheet.TextBoxes.Add(textBox);

        var target = new DrawingObjectFormatTarget(DrawingObjectTarget.FromTextBox(textBox), FormatPicturePlanner.Capture(textBox));
        var commands = DrawingObjectFormatCommandPolicy.BuildFormatCommands(
            sheet.Id,
            target,
            new FormatPicturePlanner.FormatObjectResult(222, 111, 35, true, " updated "));

        commands.Should().HaveCount(3);
        commands.Should().NotContain(command => command is SetPictureLockAspectRatioCommand);
        foreach (var command in commands)
            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        textBox.Width.Should().Be(222);
        textBox.Height.Should().Be(111);
        textBox.RotationDegrees.Should().Be(35);
        textBox.AltText.Should().Be("updated");
    }

    [Fact]
    public void BuildFormatCommands_IncludesPictureLockAspectStep()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            LockAspectRatio = false
        };
        sheet.Pictures.Add(picture);

        var target = new DrawingObjectFormatTarget(DrawingObjectTarget.FromPicture(picture), FormatPicturePlanner.Capture(picture));
        var commands = DrawingObjectFormatCommandPolicy.BuildFormatCommands(
            sheet.Id,
            target,
            new FormatPicturePlanner.FormatObjectResult(220, 110, 25, true, "Picture alt"));

        commands.Should().HaveCount(4);
        commands.Should().Contain(command => command is SetPictureLockAspectRatioCommand);
        foreach (var command in commands)
            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        picture.Width.Should().Be(220);
        picture.Height.Should().Be(110);
        picture.RotationDegrees.Should().Be(25);
        picture.LockAspectRatio.Should().BeTrue();
        picture.AltText.Should().Be("Picture alt");
    }

    [Fact]
    public void BuildPictureFormatCommands_AddsCropForInsertedImages()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            LockAspectRatio = false
        };
        sheet.Pictures.Add(picture);

        var commands = DrawingObjectFormatCommandPolicy.BuildPictureFormatCommands(
            sheet.Id,
            picture,
            new FormatPicturePlanner.PictureFormatResult(
                new FormatPicturePlanner.FormatObjectResult(220, 110, 25, true, "Picture alt"),
                new PictureCropDialogPlanner.CropResult(0.10, 0.05, 0.20, 0)));

        commands.Should().HaveCount(5);
        commands.Should().Contain(command => command is SetPictureLockAspectRatioCommand);
        commands.Should().Contain(command => command is SetPictureCropCommand);
        foreach (var command in commands)
            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        picture.Width.Should().Be(220);
        picture.Height.Should().Be(110);
        picture.RotationDegrees.Should().Be(25);
        picture.LockAspectRatio.Should().BeTrue();
        picture.AltText.Should().Be("Picture alt");
        picture.CropLeft.Should().Be(0.10);
        picture.CropTop.Should().Be(0.05);
        picture.CropRight.Should().Be(0.20);
        picture.CropBottom.Should().Be(0);
    }

    [Fact]
    public void BuildPictureFormatCommand_ComposesAllPictureChangesAsOneCommand()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new PictureModel
        {
            Kind = PictureKind.Image,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
        };
        sheet.Pictures.Add(picture);
        var result = new FormatPicturePlanner.PictureFormatResult(
            new FormatPicturePlanner.FormatObjectResult(240, 160, 30, true, "Updated"),
            new PictureCropDialogPlanner.CropResult(0.10, 0.05, 0.20, 0));

        var command = DrawingObjectFormatCommandPolicy.BuildPictureFormatCommand(
            sheet.Id,
            picture,
            result,
            "Format Picture",
            "Picture missing");

        command.Should().BeOfType<CompositeWorkbookCommand>();
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        picture.Width.Should().Be(240);
        picture.Height.Should().Be(160);
        picture.CropLeft.Should().Be(0.10);
    }

    [Fact]
    public void BuildPictureFormatCommand_ReturnsFailureWhenGroupedTargetIsMissing()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var result = new FormatPicturePlanner.PictureFormatResult(
            new FormatPicturePlanner.FormatObjectResult(240, 160, 30, true, "Updated"),
            new PictureCropDialogPlanner.CropResult(0, 0, 0, 0));

        var command = DrawingObjectFormatCommandPolicy.BuildPictureFormatCommand(
            sheet.Id,
            picture: null,
            result,
            "Format Picture",
            "Picture missing");

        command.Label.Should().Be("Unavailable");
        command.Apply(new TestCommandContext(workbook))
            .Should().Be(new CommandOutcome(false, "Picture missing"));
    }

    [Fact]
    public void StandaloneBuilders_NormalizeDialogResultsForResizeRotationAndAltText()
    {
        var workbook = new Workbook("drawing");
        var sheet = workbook.AddSheet("Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 80,
            RotationDegrees = 10,
            AltText = "old"
        };
        sheet.TextBoxes.Add(textBox);
        var target = new DrawingObjectFormatTarget(DrawingObjectTarget.FromTextBox(textBox), FormatPicturePlanner.Capture(textBox));

        var commands = new[]
        {
            DrawingObjectFormatCommandPolicy.BuildResizeCommand(
                sheet.Id,
                target,
                new ObjectSizeDialogSize(300, 150)),
            DrawingObjectFormatCommandPolicy.BuildRotationCommand(
                sheet.Id,
                target,
                new FormatPicturePlanner.RotationResult(45)),
            DrawingObjectFormatCommandPolicy.BuildAltTextCommand(
                sheet.Id,
                target,
                "  Updated alt  "),
        };

        foreach (var command in commands)
            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        textBox.Width.Should().Be(300);
        textBox.Height.Should().Be(150);
        textBox.RotationDegrees.Should().Be(45);
        textBox.AltText.Should().Be("Updated alt");
    }

    [Fact]
    public void ResolveFillAndOutlineColor_UsesTextBoxDefaultsAndThemeOverrides()
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 20, 30))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(40, 50, 60));
        var textBox = new TextBoxModel
        {
            HasFill = true,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)
        };
        var target = DrawingObjectTarget.FromTextBox(textBox);

        DrawingObjectFormatCommandPolicy.ResolveFillColor(target, theme).Should().Be(new CellColor(10, 20, 30));
        DrawingObjectFormatCommandPolicy.ResolveOutlineColor(target, theme).Should().Be(new CellColor(40, 50, 60));

        textBox.HasFill = false;
        DrawingObjectFormatCommandPolicy.ResolveFillColor(DrawingObjectTarget.FromTextBox(textBox), theme).Should().BeNull();
    }
}
