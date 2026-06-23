using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Commands;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
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
            Margin = new Thickness(0, 0, 0, 4),
        };
        var positionText = new TextBlock { Margin = new Thickness(0, 0, 0, 6) };
        var stepText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var valueText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
        };

        var dialog = new Window
        {
            Title = UiText.Get(EvaluateFormulaDialogPlanner.TitleKey),
            Width = 600,
            Height = 360,
            MinWidth = 420,
            MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "EvaluateFormulaDialog");

        var evaluateButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.EvaluateButtonKey, 80);
        evaluateButton.IsDefault = true;
        var stepInButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.StepInButtonKey, 68);
        var stepOutButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.StepOutButtonKey, 76);
        var restartButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.RestartButtonKey, 80);
        var closeButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.CloseButtonKey, 80);
        closeButton.IsCancel = true;
        var helpButton = CreateEvaluateFormulaButton(EvaluateFormulaDialogPlanner.HelpButtonKey, 142);

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

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                evaluateButton,
                stepInButton,
                stepOutButton,
                restartButton,
                closeButton,
                helpButton,
            },
        };

        var stack = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get(EvaluateFormulaDialogPlanner.EvaluationLabelKey),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                new TextBlock
                {
                    Text = $"{summary.SheetName}!{summary.Address.ToA1()}",
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                formulaText,
                new TextBlock
                {
                    Text = UiText.Format(EvaluateFormulaDialogPlanner.ResultTextKey, summary.ValueText),
                    Margin = new Thickness(0, 0, 0, 12),
                },
                positionText,
                stepText,
                valueText,
            },
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(stack);
        dialog.Content = root;

        void Refresh()
        {
            var highlight = evaluationSession.CurrentHighlight;
            formulaText.Text =
                UiText.Get(EvaluateFormulaDialogPlanner.FormulaPrefixKey) +
                highlight.Prefix +
                highlight.Highlight +
                highlight.Suffix;

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

    private static Button CreateEvaluateFormulaButton(string contentKey, double width) =>
        new()
        {
            Content = UiText.Get(contentKey),
            Width = width,
            Height = 26,
            Margin = new Thickness(4, 0, 0, 0),
        };
}
