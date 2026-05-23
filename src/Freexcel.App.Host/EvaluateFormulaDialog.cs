using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed class EvaluateFormulaDialog : Window
{
    private readonly FormulaEvaluationSession _session;
    private readonly TextBlock _formulaText;
    private readonly TextBlock _stepText;
    private readonly TextBlock _valueText;
    private readonly TextBlock _positionText;
    private readonly Button _stepOutButton;
    private readonly Button _nextButton;

    public EvaluateFormulaDialog(FormulaEvaluationSummary summary)
    {
        _session = FormulaEvaluationSession.Start(summary);

        Title = "Evaluate Formula";
        Width = 600;
        Height = 360;
        MinWidth = 420;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _nextButton = new Button { Content = "_Evaluate", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _nextButton.Click += (_, _) =>
        {
            _session.MoveNext();
            Refresh();
        };
        buttons.Children.Add(_nextButton);

        var stepIn = new Button { Content = "Step _In", Width = 68, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        stepIn.Click += (_, _) =>
        {
            _session.MoveNext();
            Refresh();
        };
        buttons.Children.Add(stepIn);

        _stepOutButton = new Button { Content = "Step _Out", Width = 76, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _stepOutButton.Click += (_, _) =>
        {
            _session.StepOut();
            Refresh();
        };
        buttons.Children.Add(_stepOutButton);

        var restart = new Button { Content = "_Restart", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        restart.Click += (_, _) =>
        {
            while (_session.CanMovePrevious)
                _session.MovePrevious();
            Refresh();
        };
        buttons.Children.Add(restart);

        var close = new Button { Content = "_Close", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var help = new Button { Content = "_Help on this formula", Width = 142, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        help.Click += (_, _) => ShowFormulaHelp();
        buttons.Children.Add(help);

        var stack = new StackPanel();
        root.Children.Add(stack);

        stack.Children.Add(new TextBlock
        {
            Text = "Evaluation:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"{summary.SheetName}!{summary.Address.ToA1()}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _formulaText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        stack.Children.Add(_formulaText);
        stack.Children.Add(new TextBlock
        {
            Text = $"Result: {summary.ValueText}",
            Margin = new Thickness(0, 0, 0, 12)
        });

        _positionText = new TextBlock { Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(_positionText);

        _stepText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(_stepText);

        _valueText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };
        stack.Children.Add(_valueText);

        Refresh();
    }

    private void Refresh()
    {
        RefreshFormulaHighlight();

        if (_session.CurrentStep is { } step)
        {
            _positionText.Text = $"Step {_session.CurrentStepNumber} of {_session.StepCount}";
            _stepText.Text = step.Expression;
            _valueText.Text = $"Value: {step.ValueText}";
        }
        else
        {
            _positionText.Text = "No intermediate evaluation steps.";
            _stepText.Text = _session.Summary.FormulaText;
            _valueText.Text = $"Value: {_session.Summary.ValueText}";
        }

        _stepOutButton.IsEnabled = _session.CurrentStep is not null;
        _nextButton.IsEnabled = _session.CanMoveNext;
    }

    private void ShowFormulaHelp()
    {
        MessageBox.Show(
            this,
            "Evaluate Formula shows the selected formula one calculation step at a time. Use Evaluate or Step In to advance, Step Out to return to the previous step, and Restart to begin again.",
            "Evaluate Formula Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshFormulaHighlight()
    {
        var highlight = _session.CurrentHighlight;
        _formulaText.Inlines.Clear();
        _formulaText.Inlines.Add(new Run("Formula: "));
        if (!string.IsNullOrEmpty(highlight.Prefix))
            _formulaText.Inlines.Add(new Run(highlight.Prefix));

        _formulaText.Inlines.Add(new Run(highlight.Highlight)
        {
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(255, 242, 157))
        });

        if (!string.IsNullOrEmpty(highlight.Suffix))
            _formulaText.Inlines.Add(new Run(highlight.Suffix));
    }
}
