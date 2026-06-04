using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    [Fact]
    public void ShapeGradientDialog_LabelsStopRgbEditorsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("UiText.Get(\"ShapeGradient_GradientStopsGroup\")");
        source.Should().Contain("AddStopRow(grid, 0, UiText.Get(\"ShapeGradient_Stop1ColorLabel\"), _startColorBox");
        source.Should().Contain("AddStopRow(grid, 1, UiText.Get(\"ShapeGradient_Stop2ColorLabel\"), _endColorBox");
        source.Should().Contain("Target = box");
        source.Should().NotContain("RGB _override:");
        source.Should().NotContain("_gradientBox");
    }

    [Fact]
    public void ShapeGradientDialogOpenedFromKeyboard_FocusesStartColorBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_startColorBox);");
    }

    [Fact]
    public void ShapeGradientDialogInvalidColor_ShowsOwnedWarningAndRefocusesFirstInvalidColor()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        source.Should().Contain("UiText.Get(\"ShapeGradient_InvalidRgbColorMessage\")");
        source.Should().Contain("FocusInvalidColorInput(_startColorBox);");
        source.Should().Contain("FocusInvalidColorInput(_endColorBox);");
        source.Should().Contain("private static void FocusInvalidColorInput(TextBox colorBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(colorBox);");
    }

    [Fact]
    public void ShapeGradientDialog_ColorControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ShapeGradientDialog(DrawingShapeGradientDirection.Vertical);
            try
            {
                var startColorBox = GetField<TextBox>(dialog, "_startColorBox");
                AutomationProperties.GetName(startColorBox).Should().Be("Start gradient color");
                AutomationProperties.GetAutomationId(startColorBox).Should().Be("ShapeGradientStartColorBox");
                AutomationProperties.GetHelpText(startColorBox).Should().Be("Enter the first gradient stop color as R,G,B.");

                var endColorBox = GetField<TextBox>(dialog, "_endColorBox");
                AutomationProperties.GetName(endColorBox).Should().Be("End gradient color");
                AutomationProperties.GetAutomationId(endColorBox).Should().Be("ShapeGradientEndColorBox");
                AutomationProperties.GetHelpText(endColorBox).Should().Be("Enter the second gradient stop color as R,G,B.");

                var startColorButton = GetField<Button>(dialog, "_startColorButton");
                AutomationProperties.GetName(startColorButton).Should().Be("Choose start gradient color");
                AutomationProperties.GetAutomationId(startColorButton).Should().Be("ShapeGradientStartColorButton");
                AutomationProperties.GetHelpText(startColorButton).Should().Be("Open the color picker for the first gradient stop.");

                var endColorButton = GetField<Button>(dialog, "_endColorButton");
                AutomationProperties.GetName(endColorButton).Should().Be("Choose end gradient color");
                AutomationProperties.GetAutomationId(endColorButton).Should().Be("ShapeGradientEndColorButton");
                AutomationProperties.GetHelpText(endColorButton).Should().Be("Open the color picker for the second gradient stop.");

                var directionBox = GetField<ComboBox>(dialog, "_directionBox");
                AutomationProperties.GetName(directionBox).Should().Be("Direction");
                AutomationProperties.GetAutomationId(directionBox).Should().Be("ShapeGradientDirectionBox");
                directionBox.SelectedItem.Should()
                    .BeOfType<ShapeGradientDirectionOption>()
                    .Which.Direction.Should()
                    .Be(DrawingShapeGradientDirection.Vertical);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ShapeGradientDialogPlanner_CreateDirectionOptions_OffersGradientDirectionPresets()
    {
        var options = ShapeGradientDialogPlanner.CreateDirectionOptions();

        options.Select(option => option.Direction).Should().Equal(
            DrawingShapeGradientDirection.DiagonalDown,
            DrawingShapeGradientDirection.Horizontal,
            DrawingShapeGradientDirection.Vertical,
            DrawingShapeGradientDirection.DiagonalUp);
        ShapeGradientDialogPlanner.NormalizeDirection((DrawingShapeGradientDirection)99)
            .Should()
            .Be(DrawingShapeGradientDirection.DiagonalDown);
    }

    [Fact]
    public void ShapeEffectsDialogPlanner_CreatePlan_OffersExcelLikeEffectPresets()
    {
        var plan = ShapeEffectsDialogPlanner.CreatePlan(DrawingShapeEffectPreset.Glow);

        plan.SelectedPreset.Should().Be(DrawingShapeEffectPreset.Glow);
        plan.Options.Select(option => option.Preset).Should().Equal(
            DrawingShapeEffectPreset.None,
            DrawingShapeEffectPreset.Shadow,
            DrawingShapeEffectPreset.InnerShadow,
            DrawingShapeEffectPreset.Reflection,
            DrawingShapeEffectPreset.Glow,
            DrawingShapeEffectPreset.SoftEdges,
            DrawingShapeEffectPreset.Bevel);
        plan.Options.Select(option => option.Label).Should().Equal(
            "No Effect",
            "Shadow",
            "Inner Shadow",
            "Reflection",
            "Glow",
            "Soft Edges",
            "Bevel");
    }

    [Fact]
    public void ShapeEffectsDialogPlanner_CreatePlan_NormalizesUnknownPresetToNone()
    {
        var plan = ShapeEffectsDialogPlanner.CreatePlan((DrawingShapeEffectPreset)99);

        plan.SelectedPreset.Should().Be(DrawingShapeEffectPreset.None);
    }

    [Fact]
    public void ShapeEffectsDialog_TryCreateResult_AcceptsKnownPreset()
    {
        ShapeEffectsDialog.TryCreateResult(DrawingShapeEffectPreset.SoftEdges, out var result)
            .Should()
            .BeTrue();

        result.Should().Be(new ShapeEffectsDialogResult(DrawingShapeEffectPreset.SoftEdges));
    }

    [Fact]
    public void ShapeEffectsDialog_TryCreateResult_RejectsUnknownPreset()
    {
        ShapeEffectsDialog.TryCreateResult((DrawingShapeEffectPreset)99, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShapeEffectsDialog_ControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ShapeEffectsDialog(DrawingShapeEffectPreset.SoftEdges);
            try
            {
                var effectBox = GetField<ComboBox>(dialog, "_effectBox");
                AutomationProperties.GetName(effectBox).Should().Be("Shape effect");
                AutomationProperties.GetAutomationId(effectBox).Should().Be("ShapeEffectsPresetBox");
                AutomationProperties.GetHelpText(effectBox).Should().Be("Choose no effect, shadow, inner shadow, reflection, glow, soft edges, or bevel for the selected shape.");
                effectBox.SelectedItem.Should()
                    .BeOfType<ShapeEffectsDialogOption>()
                    .Which.Preset.Should()
                    .Be(DrawingShapeEffectPreset.SoftEdges);

                var descriptionText = GetField<TextBlock>(dialog, "_descriptionText");
                AutomationProperties.GetName(descriptionText).Should().Be("Shape effect description");
                AutomationProperties.GetAutomationId(descriptionText).Should().Be("ShapeEffectsDescriptionText");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ShapeEffectsDialog_LabelsPresetComboWithAccessKeyTarget()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeEffectsDialog.cs"));

        source.Should().Contain("Content = UiText.Get(\"ShapeEffects_EffectLabel\")");
        source.Should().Contain("Target = _effectBox");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("Keyboard.Focus(_effectBox);");
    }

    [Fact]
    public void ShapeGradientDialog_TryCreateResult_ParsesTwoRgbColors()
    {
        ShapeGradientDialog.TryCreateResult("31,119,180; 180,210,240", out var result, out _).Should().BeTrue();

        result.Should().Be(new ShapeGradientDialogResult(
            new CellColor(31, 119, 180),
            new CellColor(180, 210, 240)));
    }

    [Fact]
    public void ShapeGradientDialog_ExposesColorPickerButtonsForStartAndEndColors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ShapeGradientDialog.cs"));

        source.Should().Contain("_startColorButton");
        source.Should().Contain("_endColorButton");
        source.Should().Contain("Content = UiText.Get(\"ShapeGradient_StartColorButton\")");
        source.Should().Contain("Content = UiText.Get(\"ShapeGradient_EndColorButton\")");
        source.Should().Contain("new ColorPickerDialog(_startColor)");
        source.Should().Contain("new ColorPickerDialog(_endColor)");
        source.Should().Contain("_startColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("_endColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("UpdateColorText()");
    }
}
