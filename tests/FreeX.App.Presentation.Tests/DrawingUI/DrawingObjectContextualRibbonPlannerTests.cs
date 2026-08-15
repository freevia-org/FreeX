using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;
using Free.Shared.Ribbon;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class DrawingObjectContextualRibbonPlannerTests
{
    [Fact]
    public void Build_TextBoxSelectionShowsShapeTabAndDisablesShapeOnlyCommands()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 60
        };
        sheet.TextBoxes.Add(textBox);

        var plan = DrawingObjectContextualRibbonPlanner.Build(
            sheet,
            SelectionPaneObjectKind.TextBox,
            textBox.Id);

        plan.ShapeFormatVisible.Should().BeTrue();
        plan.PictureFormatVisible.Should().BeFalse();
        plan.ShapeGradientEnabled.Should().BeFalse();
        plan.ShapeEffectsEnabled.Should().BeFalse();
        plan.CropPictureEnabled.Should().BeFalse();
    }

    [Fact]
    public void Build_ShapeSelectionShowsShapeTabAndEnablesShapeOnlyCommands()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 60
        };
        sheet.DrawingShapes.Add(shape);

        var plan = DrawingObjectContextualRibbonPlanner.Build(
            sheet,
            SelectionPaneObjectKind.Shape,
            shape.Id);

        plan.ShapeFormatVisible.Should().BeTrue();
        plan.PictureFormatVisible.Should().BeFalse();
        plan.ShapeGradientEnabled.Should().BeTrue();
        plan.ShapeEffectsEnabled.Should().BeTrue();
        plan.CropPictureEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(PictureKind.Image, true)]
    [InlineData(PictureKind.CellRangeSnapshot, false)]
    public void Build_PictureSelectionShowsPictureTabAndEnablesCropOnlyForImages(
        PictureKind kind,
        bool expectedCropEnabled)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var picture = new PictureModel
        {
            Kind = kind,
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = 120,
            Height = 60
        };
        sheet.Pictures.Add(picture);

        var plan = DrawingObjectContextualRibbonPlanner.Build(
            sheet,
            SelectionPaneObjectKind.Picture,
            picture.Id);

        plan.ShapeFormatVisible.Should().BeFalse();
        plan.PictureFormatVisible.Should().BeTrue();
        plan.CropPictureEnabled.Should().Be(expectedCropEnabled);
        plan.ShapeGradientEnabled.Should().BeFalse();
        plan.ShapeEffectsEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(SelectionPaneObjectKind.Picture, DrawingObjectContextualRibbonPlanner.PictureContextKey)]
    [InlineData(SelectionPaneObjectKind.Shape, DrawingObjectContextualRibbonPlanner.ShapeContextKey)]
    [InlineData(SelectionPaneObjectKind.TextBox, DrawingObjectContextualRibbonPlanner.ShapeContextKey)]
    public void ResolveActivationKey_MapsDrawingSelectionToSharedContextKeys(
        SelectionPaneObjectKind kind,
        string expected) =>
        DrawingObjectContextualRibbonPlanner.ResolveActivationKey(kind).Should().Be(expected);

    [Fact]
    public void CreatePictureShapeCommandSpecs_OwnsContextualTabActionMetadata()
    {
        var specs = DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs();

        specs.Select(spec => spec.CommandId).Should().OnlyHaveUniqueItems();
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            "Format Picture",
            DrawingObjectContextualCommandAction.FormatPicture));
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            "Shape Gradient",
            DrawingObjectContextualCommandAction.ShapeGradient));
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            FreeXRibbonCommandIds.DrawingShapeEffectShadow,
            DrawingObjectContextualCommandAction.ShapeEffectPreset,
            DrawingShapeEffectPreset.Shadow));
        specs.Should().Contain(new DrawingObjectContextualCommandSpec(
            FreeXRibbonCommandIds.DrawingShapeEffectThreeDRotation,
            DrawingObjectContextualCommandAction.ShapeEffectPreset,
            DrawingShapeEffectPreset.ThreeDRotation));
    }

    [Theory]
    [InlineData(DrawingShapeEffectPreset.None)]
    [InlineData(DrawingShapeEffectPreset.Glow)]
    [InlineData(DrawingShapeEffectPreset.ThreeDRotation)]
    public void BuildShapeEffectCommandStates_ChecksExactlyTheCurrentPreset(
        DrawingShapeEffectPreset currentPreset)
    {
        var states = DrawingObjectContextualRibbonPlanner.BuildShapeEffectCommandStates(
            currentPreset,
            isEnabled: true);

        states.Should().HaveCount(8);
        states.Select(state => state.CommandId).Should().OnlyHaveUniqueItems();
        states.Should().ContainSingle(state => state.State.IsChecked)
            .Which.CommandId.Should().Be(
                DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs()
                    .Single(spec => spec.EffectPreset == currentPreset).CommandId);
        states.Should().OnlyContain(state => state.State.IsEnabled);
    }

    [Fact]
    public void BuildShapeEffectCommandStates_DisablesEveryPresetWithoutLosingSelection()
    {
        var states = DrawingObjectContextualRibbonPlanner.BuildShapeEffectCommandStates(
            DrawingShapeEffectPreset.Reflection,
            isEnabled: false);

        states.Should().OnlyContain(state => !state.State.IsEnabled);
        states.Should().ContainSingle(state => state.State.IsChecked);
    }

    [Fact]
    public void TryResolveShapeEffectPreset_UsesCanonicalCommandIds()
    {
        foreach (var spec in DrawingObjectContextualRibbonPlanner.CreatePictureShapeCommandSpecs()
                     .Where(spec => spec.Action == DrawingObjectContextualCommandAction.ShapeEffectPreset))
        {
            DrawingObjectContextualRibbonPlanner.TryResolveShapeEffectPreset(
                    spec.CommandId,
                    out var preset)
                .Should().BeTrue();
            preset.Should().Be(spec.EffectPreset);
        }

        DrawingObjectContextualRibbonPlanner.TryResolveShapeEffectPreset(
                DrawingObjectContextualRibbonPlanner.ShapeEffectsCommandName,
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void CanonicalShapeEffectMenus_DeclareEveryPresetCheckable()
    {
        var menus = FreeXRibbon.Build().Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Where(control => control.CommandId.Value == DrawingObjectContextualRibbonPlanner.ShapeEffectsCommandName)
            .Select(control => control.Menu)
            .ToArray();

        menus.Should().HaveCount(2);
        foreach (var menu in menus)
        {
            var commands = menu.Items.Where(item => item.CommandId is not null).ToArray();
            commands.Should().HaveCount(8);
            commands.Should().OnlyContain(item => item.IsChecked == false);
        }
    }

    [Fact]
    public void Hosts_ConsumeSharedShapeEffectStateAndWpfMenuMetadata()
    {
        var wpfDrawing = ReadSource("src", "FreeX.App.Host", "MainWindow.Drawing.cs");
        var wpfContext = ReadSource("src", "FreeX.App.Host", "MainWindow.DrawingContextualTabs.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var wpfRenderer = ReadSource("shared", "Free.Shared.Ribbon.Wpf", "RibbonWpfRenderer.cs");

        wpfDrawing.Should().Contain("DrawingObjectContextualRibbonPlanner.TryResolveShapeEffectPreset(");
        wpfDrawing.Should().NotContain("ShapeEffectsMenu_Opened");
        wpfContext.Should().Contain("DrawingObjectContextualRibbonPlanner.BuildShapeEffectCommandStates(");
        wpfContext.Should().Contain("_ribbonState.SetState(commandState.CommandId, commandState.State);");
        avalonia.Should().Contain("GetShapeEffectPresetRibbonState(DrawingShapeEffectPreset.");
        wpfRenderer.Should().Contain("RibbonMetadata.SetCommandName(menuItem, commandId.Value);");
        wpfRenderer.Should().Contain("contextMenu.Opened += (_, _) => RefreshMenuCommandStates(");
        wpfRenderer.Should().Contain("RibbonMenuCommandStatePlanner.Plan(");
    }

    private static string ReadSource(params string[] path)
    {
        var directory = RepositoryFileLocator.FindDirectory(path[..^1]);
        return File.ReadAllText(Path.Combine(directory, path[^1]));
    }
}
