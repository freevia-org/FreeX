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
    private readonly Button _previousButton;
    private readonly Button _nextButton;

    public EvaluateFormulaDialog(FormulaEvaluationSummary summary)
    {
        _session = FormulaEvaluationSession.Start(summary);

        Title = "Evaluate Formula";
        Width = 520;
        Height = 300;
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

        _previousButton = new Button { Content = "Previous", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _previousButton.Click += (_, _) =>
        {
            _session.MovePrevious();
            Refresh();
        };
        buttons.Children.Add(_previousButton);

        _nextButton = new Button { Content = "Evaluate", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _nextButton.Click += (_, _) =>
        {
            _session.MoveNext();
            Refresh();
        };
        buttons.Children.Add(_nextButton);

        var close = new Button { Content = "Close", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        var stack = new StackPanel();
        root.Children.Add(stack);

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

        _previousButton.IsEnabled = _session.CanMovePrevious;
        _nextButton.IsEnabled = _session.CanMoveNext;
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
