using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Commands;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle EvaluateFormulaDialogChromeStyle =>
        EvaluateFormulaDialogChromeStyleForTest;

    internal static AvaloniaCompactDialogChromeStyle EvaluateFormulaDialogChromeStyleForTest =>
        new(FormulaBarFontFamily)
        {
            ControlHeight = EvaluateFormulaDialogPlanner.ButtonHeight,
            ButtonHeight = EvaluateFormulaDialogPlanner.ButtonHeight,
            ActionSpacing = EvaluateFormulaDialogPlanner.ActionSpacing,
        };

    private async Task ShowEvaluateFormulaDialogAsync()
    {
        var summary = EvaluateFormulaDialogPlanner.CreateSummary(_session.Workbook, _session.ActiveCell);
        if (summary is null)
        {
            ShowEditIssue(UiText.Get(EvaluateFormulaDialogPlanner.SelectFormulaMessageKey));
            return;
        }

        await ShowEvaluateFormulaDialogAsync(summary);
    }

    private async Task ShowEvaluateFormulaDialogAsync(FormulaEvaluationSummary summary)
    {
        var evaluationSession = EvaluateFormulaDialogPlanner.CreateSession(summary);

        var formulaText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = EvaluateFormulaDialogPlanner.LabelFontSize,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var positionText = new TextBlock
        {
            FontSize = EvaluateFormulaDialogPlanner.LabelFontSize,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var stepText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = EvaluateFormulaDialogPlanner.StepFontSize,
            FontWeight = FontWeight.SemiBold,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var valueText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = EvaluateFormulaDialogPlanner.ValueFontSize,
            FontFamily = FormulaBarFontFamily,
        };

        var dialog = new Window
        {
            Title = UiText.Get(EvaluateFormulaDialogPlanner.TitleKey),
            Width = EvaluateFormulaDialogPlanner.Width,
            Height = EvaluateFormulaDialogPlanner.Height,
            MinWidth = EvaluateFormulaDialogPlanner.MinWidth,
            MinHeight = EvaluateFormulaDialogPlanner.MinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, EvaluateFormulaDialogChromeStyle);
        AutomationProperties.SetAutomationId(dialog, "EvaluateFormulaDialog");

        var evaluateButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.EvaluateButtonKey, EvaluateFormulaDialogPlanner.EvaluateButtonWidth, isDefault: true);
        evaluateButton.IsDefault = true;
        var stepInButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.StepInButtonKey, EvaluateFormulaDialogPlanner.StepInButtonWidth);
        var stepOutButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.StepOutButtonKey, EvaluateFormulaDialogPlanner.StepOutButtonWidth);
        var restartButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.RestartButtonKey, EvaluateFormulaDialogPlanner.RestartButtonWidth);
        var closeButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.CloseButtonKey, EvaluateFormulaDialogPlanner.CloseButtonWidth);
        closeButton.IsCancel = true;
        var helpButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.HelpButtonKey, EvaluateFormulaDialogPlanner.HelpButtonWidth);

        evaluateButton.Click += (_, _) =>
        {
            evaluationSession.MoveNext();
            Refresh();
        };
        stepInButton.Click += (_, _) =>
        {
            evaluationSession.StepIn();
            Refresh();
        };
        stepOutButton.Click += (_, _) =>
        {
            evaluationSession.StepOut();
            Refresh();
        };
        restartButton.Click += (_, _) =>
        {
            while (evaluationSession.CanMovePrevious)
                evaluationSession.MovePrevious();
            Refresh();
        };
        closeButton.Click += (_, _) => dialog.Close();
        helpButton.Click += (_, _) => ShowEditIssue(UiText.Get(EvaluateFormulaDialogPlanner.HelpBodyKey));

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [evaluateButton, stepInButton, stepOutButton, restartButton, closeButton, helpButton],
            new Thickness(0, EvaluateFormulaDialogPlanner.ActionRowTopMargin, 0, 0),
            EvaluateFormulaDialogChromeStyle);

        var stack = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get(EvaluateFormulaDialogPlanner.EvaluationLabelKey),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = EvaluateFormulaDialogPlanner.LabelFontSize,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                new TextBlock
                {
                    Text = $"{summary.SheetName}!{summary.Address.ToA1()}",
                    FontWeight = FontWeight.SemiBold,
                    FontSize = EvaluateFormulaDialogPlanner.LabelFontSize,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                formulaText,
                new TextBlock
                {
                    Text = UiText.Format(EvaluateFormulaDialogPlanner.ResultTextKey, summary.ValueText),
                    FontSize = EvaluateFormulaDialogPlanner.LabelFontSize,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 12),
                },
                positionText,
                stepText,
                valueText,
            },
        };

        var root = new DockPanel { Margin = new Thickness(EvaluateFormulaDialogPlanner.RootMargin) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(stack);
        dialog.Content = root;

        void Refresh()
        {
            var highlight = evaluationSession.CurrentHighlight;
            formulaText.Inlines!.Clear();
            formulaText.Inlines.Add(new Run(UiText.Get(EvaluateFormulaDialogPlanner.FormulaPrefixKey)));
            if (!string.IsNullOrEmpty(highlight.Prefix))
                formulaText.Inlines.Add(new Run(highlight.Prefix));
            formulaText.Inlines.Add(new Run(highlight.Highlight)
            {
                FontWeight = FontWeight.Bold,
                Background = new SolidColorBrush(Color.FromRgb(255, 242, 157)),
            });
            if (!string.IsNullOrEmpty(highlight.Suffix))
                formulaText.Inlines.Add(new Run(highlight.Suffix));

            if (evaluationSession.CurrentStep is { } step)
            {
                positionText.Text = UiText.Format(
                    EvaluateFormulaDialogPlanner.StepPositionTextKey,
                    evaluationSession.CurrentStepNumber,
                    evaluationSession.StepCount);
                stepText.Text = step.Expression;
                valueText.Text = UiText.Format(EvaluateFormulaDialogPlanner.ValueTextKey, step.ValueText);
            }
            else
            {
                positionText.Text = UiText.Get(EvaluateFormulaDialogPlanner.NoIntermediateStepsTextKey);
                stepText.Text = evaluationSession.Summary.FormulaText;
                valueText.Text = UiText.Format(EvaluateFormulaDialogPlanner.ValueTextKey, evaluationSession.Summary.ValueText);
            }

            stepOutButton.IsEnabled = evaluationSession.CanStepOut;
            evaluateButton.IsEnabled = evaluationSession.CanMoveNext;
            stepInButton.IsEnabled = evaluationSession.CanStepIn;
        }

        Refresh();
        dialog.Opened += (_, _) => (evaluateButton.IsEnabled ? evaluateButton : closeButton).Focus();
        await dialog.ShowDialog(this);
    }

    private static Button CreateEvaluateFormulaButton(string contentKey, double width, bool isDefault = false)
    {
        var button = new Button
        {
            Content = UiText.Get(contentKey),
            Width = width,
        };
        AvaloniaCompactDialogChrome.ApplyButton(button, EvaluateFormulaDialogChromeStyle, width, isDefault);
        return button;
    }
}
