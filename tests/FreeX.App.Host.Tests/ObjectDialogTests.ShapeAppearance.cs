using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    [Fact]
    public void ShapeAppearanceDialogPlanners_DelegatePortableRulesToAppServicesPlanners()
    {
        var gradientSource = DialogSourceTestSupport.ReadHostSources("ShapeGradientDialog.cs");
        var effectsSource = DialogSourceTestSupport.ReadHostSources("ShapeEffectsDialog.cs");
        var gradientPlannerSource = DialogSourceTestSupport.ReadAppServicesSource("ShapeGradientPlanner.cs");
        var effectsPlannerSource = DialogSourceTestSupport.ReadAppServicesSource("ShapeEffectsPlanner.cs");

        gradientSource.Should().Contain("using FreeX.App.Services;");
        gradientSource.Should().Contain("ShapeGradientPlanner.CreateDirectionOptions()");
        gradientSource.Should().Contain("ShapeGradientPlanner.DefaultStartColor");
        gradientSource.Should().Contain("ShapeGradientPlanner.DefaultEndColor");
        gradientSource.Should().Contain("ShapeGradientPlanner.NormalizeDirection(direction)");
        gradientSource.Should().Contain("ShapeGradientPlanner.CreateResult(startColor, endColor, direction)");
        gradientSource.Should().Contain("ShapeGradientPlanner.PreviewVector(direction, width, height)");
        gradientSource.Should().NotContain("using FreeX.App.Presentation.DrawingUI;");
        gradientSource.Should().NotContain("new(31, 119, 180)");
        gradientSource.Should().NotContain("new(180, 210, 240)");
        gradientSource.Should().NotContain("if (width > height)");
        gradientSource.Should().NotContain("Enum.IsDefined(direction)");

        effectsSource.Should().Contain("using FreeX.App.Services;");
        effectsSource.Should().Contain("ShapeEffectsPlanner.CreateResolvedPlan(currentPreset, UiText.Get)");
        effectsSource.Should().Contain("ShapeEffectsPlanner.NormalizePreset(preset)");
        effectsSource.Should().Contain("ShapeEffectsPlanner.DefaultPreset");
        effectsSource.Should().Contain("nameof(ShapeEffectsPlanner.ResolvedShapeEffectOption.Label)");
        effectsSource.Should().Contain("_plan.ResolveSelection(");
        effectsSource.Should().NotContain("using FreeX.App.Presentation.DrawingUI;");
        effectsSource.Should().NotContain("Enum.IsDefined(preset)");
        effectsSource.Should().NotContain("DrawingShapeEffectPreset.InnerShadow,");
        effectsSource.Should().NotContain("DrawingShapeEffectPreset.None");
        effectsSource.Should().NotContain("internal sealed record ShapeEffectsDialogOption(");
        effectsSource.Should().NotContain("internal sealed record ShapeEffectsDialogPlan(");
        effectsSource.Should().NotContain("ToDialogOption");
        effectsSource.Should().NotContain("UiText.Get(option.");

        gradientPlannerSource.Should().Contain("namespace FreeX.App.Services;");
        gradientPlannerSource.Should().Contain("public static class ShapeGradientPlanner");
        gradientPlannerSource.Should().NotContain("System.Windows");
        gradientPlannerSource.Should().NotContain("UiText");
        effectsPlannerSource.Should().Contain("namespace FreeX.App.Services;");
        effectsPlannerSource.Should().Contain("public static class ShapeEffectsPlanner");
        effectsPlannerSource.Should().Contain("public sealed record ResolvedShapeEffectOption(");
        effectsPlannerSource.Should().Contain("public static ResolvedShapeEffectsPlan CreateResolvedPlan(");
        effectsPlannerSource.Should().NotContain("System.Windows");
        effectsPlannerSource.Should().NotContain("UiText");
    }

    [Fact]
    public void ShapeGradientDialog_LabelsStopRgbEditorsWithAccessKeyTargets()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ShapeGradientDialog.cs");

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
        var source = DialogSourceTestSupport.ReadHostSources("ShapeGradientDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_startColorBox);");
    }

    [Fact]
    public void ShapeGradientDialogInvalidColor_ShowsOwnedWarningAndRefocusesFirstInvalidColor()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ShapeGradientDialog.cs");

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
                var expectedStartColor = ShapeGradientPlanner.DefaultStartColor;
                AutomationProperties.GetName(startColorButton).Should().Be("Choose start gradient color");
                AutomationProperties.GetAutomationId(startColorButton).Should().Be("ShapeGradientStartColorButton");
                AutomationProperties.GetHelpText(startColorButton).Should().Be("Open the color picker for the first gradient stop.");
                startColorButton.Background.Should()
                    .BeOfType<SolidColorBrush>()
                    .Which.Color.Should()
                    .Be(Color.FromRgb(expectedStartColor.R, expectedStartColor.G, expectedStartColor.B));

                var endColorButton = GetField<Button>(dialog, "_endColorButton");
                var expectedEndColor = ShapeGradientPlanner.DefaultEndColor;
                AutomationProperties.GetName(endColorButton).Should().Be("Choose end gradient color");
                AutomationProperties.GetAutomationId(endColorButton).Should().Be("ShapeGradientEndColorButton");
                AutomationProperties.GetHelpText(endColorButton).Should().Be("Open the color picker for the second gradient stop.");
                endColorButton.Background.Should()
                    .BeOfType<SolidColorBrush>()
                    .Which.Color.Should()
                    .Be(Color.FromRgb(expectedEndColor.R, expectedEndColor.G, expectedEndColor.B));

                var directionBox = GetField<ComboBox>(dialog, "_directionBox");
                AutomationProperties.GetName(directionBox).Should().Be("Direction");
                AutomationProperties.GetAutomationId(directionBox).Should().Be("ShapeGradientDirectionBox");
                directionBox.SelectedItem.Should()
                    .BeOfType<ShapeGradientDirectionOption>()
                    .Which.Direction.Should()
                    .Be(DrawingShapeGradientDirection.Vertical);

                var preview = GetField<Border>(dialog, "_gradientPreview");
                AutomationProperties.GetAutomationId(preview).Should().Be("ShapeGradientPreviewSwatch");
                preview.Background.Should()
                    .BeOfType<LinearGradientBrush>()
                    .Which.GradientStops.Select(stop => stop.Color)
                    .Should()
                    .Equal(
                        Color.FromRgb(expectedStartColor.R, expectedStartColor.G, expectedStartColor.B),
                        Color.FromRgb(expectedEndColor.R, expectedEndColor.G, expectedEndColor.B));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ShapeGradientDialog_InitializesFromSelectedShapeGradientColors()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ShapeGradientDialog(
                new CellColor(112, 48, 160),
                new CellColor(0, 176, 240),
                DrawingShapeGradientDirection.DiagonalUp);
            try
            {
                GetField<TextBox>(dialog, "_startColorBox").Text.Should().Be("112,48,160");
                GetField<TextBox>(dialog, "_endColorBox").Text.Should().Be("0,176,240");
                GetField<Button>(dialog, "_startColorButton").Background.Should()
                    .BeOfType<SolidColorBrush>()
                    .Which.Color.Should()
                    .Be(Color.FromRgb(112, 48, 160));
                GetField<Button>(dialog, "_endColorButton").Background.Should()
                    .BeOfType<SolidColorBrush>()
                    .Which.Color.Should()
                    .Be(Color.FromRgb(0, 176, 240));

                GetField<ComboBox>(dialog, "_directionBox").SelectedItem.Should()
                    .BeOfType<ShapeGradientDirectionOption>()
                    .Which.Direction.Should()
                    .Be(DrawingShapeGradientDirection.DiagonalUp);
                dialog.Result.Should().Be(new ShapeGradientDialogResult(
                    new CellColor(112, 48, 160),
                    new CellColor(0, 176, 240),
                    DrawingShapeGradientDirection.DiagonalUp));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ShapeGradientDialogPlanner_CreatePreviewGradientPoints_KeepsWideDiagonalPreviewVisiblyDiagonal()
    {
        var (diagonalStart, diagonalEnd) = ShapeGradientDialogPlanner.CreatePreviewGradientPoints(
            DrawingShapeGradientDirection.DiagonalDown,
            width: 400,
            height: 80);
        var (reverseStart, reverseEnd) = ShapeGradientDialogPlanner.CreatePreviewGradientPoints(
            DrawingShapeGradientDirection.DiagonalUp,
            width: 400,
            height: 80);

        diagonalStart.Should().Be(new Point(0.4, 0));
        diagonalEnd.Should().Be(new Point(0.6, 1));
        reverseStart.Should().Be(new Point(0.4, 1));
        reverseEnd.Should().Be(new Point(0.6, 0));
        ((diagonalEnd.X - diagonalStart.X) * 400).Should().BeApproximately(
            (diagonalEnd.Y - diagonalStart.Y) * 80,
            0.0001);

        var (tallStart, tallEnd) = ShapeGradientDialogPlanner.CreatePreviewGradientPoints(
            DrawingShapeGradientDirection.DiagonalDown,
            width: 80,
            height: 400);
        tallStart.Should().Be(new Point(0, 0.4));
        tallEnd.Should().Be(new Point(1, 0.6));
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
                AutomationProperties.GetHelpText(effectBox).Should().Be("Choose no effect, shadow, inner shadow, reflection, glow, soft edges, bevel, or 3-D rotation for the selected shape.");
                effectBox.Items.Cast<ShapeEffectsPlanner.ResolvedShapeEffectOption>()
                    .Select(option => option.Label)
                    .Should()
                    .Equal(
                        "No Effect",
                        "Shadow",
                        "Inner Shadow",
                        "Reflection",
                        "Glow",
                        "Soft Edges",
                        "Bevel",
                        "3-D Rotation");
                effectBox.SelectedItem.Should()
                    .BeOfType<ShapeEffectsPlanner.ResolvedShapeEffectOption>()
                    .Which.Preset.Should()
                    .Be(DrawingShapeEffectPreset.SoftEdges);

                var descriptionText = GetField<TextBlock>(dialog, "_descriptionText");
                AutomationProperties.GetName(descriptionText).Should().Be("Shape effect description");
                AutomationProperties.GetAutomationId(descriptionText).Should().Be("ShapeEffectsDescriptionText");
                descriptionText.Text.Should().Be("Apply a softened edge effect to the selected shape.");
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
        var source = DialogSourceTestSupport.ReadHostSources("ShapeEffectsDialog.cs");

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
        var source = DialogSourceTestSupport.ReadHostSources("ShapeGradientDialog.cs");

        source.Should().Contain("_startColorButton");
        source.Should().Contain("_endColorButton");
        source.Should().Contain("ConfigureSwatchButton(_startColorButton");
        source.Should().Contain("ConfigureSwatchButton(_endColorButton");
        source.Should().Contain("UpdateColorSwatches()");
        source.Should().Contain("ApplySwatch(_startColorButton, _startColor)");
        source.Should().Contain("new ColorPickerDialog(_startColor)");
        source.Should().Contain("new ColorPickerDialog(_endColor)");
        source.Should().Contain("_startColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("_endColorBox.TextChanged += (_, _) => SyncGradientTextFromInputs()");
        source.Should().Contain("UpdateColorVisuals()");
        source.Should().Contain("ShapeGradientPreviewSwatch");
        source.Should().Contain("_gradientPreview.SizeChanged += (_, _) => UpdateGradientPreview()");
        source.Should().Contain("CreateGradientBrush(");
        source.Should().Contain("Width = ShapeGradientPlanner.DialogWidth");
        source.Should().Contain("Height = ShapeGradientPlanner.DialogHeight");
        source.Should().Contain("DialogButtonRowFactory.Create(Accept, 76, rowMargin");
        source.Should().NotContain("Height = 280");
        source.Should().NotContain("Content = UiText.Get(\"ShapeGradient_StartColorButton\")");
        source.Should().NotContain("Content = UiText.Get(\"ShapeGradient_EndColorButton\")");
    }
}
