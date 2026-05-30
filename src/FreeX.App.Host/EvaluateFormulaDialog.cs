using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed class EvaluateFormulaDialog : Window
{
    private readonly FormulaEvaluationSession _session;
    private readonly TextBlock _formulaText;
    private readonly TextBlock _stepText;
    private readonly TextBlock _valueText;
    private readonly TextBlock _positionText;
    private readonly Button _stepOutButton;
    private readonly Button _nextButton;
    private readonly Button _stepInButton;
    private readonly Button _closeButton;

    public EvaluateFormulaDialog(FormulaEvaluationSummary summary)
    {
        _session = FormulaEvaluationSession.Start(summary);

        Title = UiText.Get("EvaluateFormula_Title");
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

        _nextButton = new Button { Content = UiText.Get("EvaluateFormula_EvaluateButton"), Width = 80, Height = 26, IsDefault = true, Margin = new Thickness(4, 0, 0, 0) };
        _nextButton.Click += (_, _) =>
        {
            _session.MoveNext();
            Refresh();
        };
        buttons.Children.Add(_nextButton);

        _stepInButton = new Button { Content = UiText.Get("EvaluateFormula_StepInButton"), Width = 68, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _stepInButton.Click += (_, _) =>
        {
            _session.StepIn();
            Refresh();
        };
        buttons.Children.Add(_stepInButton);

        _stepOutButton = new Button { Content = UiText.Get("EvaluateFormula_StepOutButton"), Width = 76, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        _stepOutButton.Click += (_, _) =>
        {
            _session.StepOut();
            Refresh();
        };
        buttons.Children.Add(_stepOutButton);

        var restart = new Button { Content = UiText.Get("EvaluateFormula_RestartButton"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        restart.Click += (_, _) =>
        {
            while (_session.CanMovePrevious)
                _session.MovePrevious();
            Refresh();
        };
        buttons.Children.Add(restart);

        _closeButton = new Button { Content = UiText.Get("EvaluateFormula_CloseButton"), Width = 80, Height = 26, IsCancel = true, Margin = new Thickness(4, 0, 0, 0) };
        _closeButton.Click += (_, _) => Close();
        buttons.Children.Add(_closeButton);

        var help = new Button { Content = UiText.Get("EvaluateFormula_HelpButton"), Width = 142, Height = 26, Margin = new Thickness(4, 0, 0, 0) };
        help.Click += (_, _) => ShowFormulaHelp();
        buttons.Children.Add(help);

        var stack = new StackPanel();
        root.Children.Add(stack);

        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get("EvaluateFormula_EvaluationLabel"),
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
            Text = UiText.Format("EvaluateFormula_ResultText", summary.ValueText),
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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void Refresh()
    {
        RefreshFormulaHighlight();

        if (_session.CurrentStep is { } step)
        {
            _positionText.Text = UiText.Format("EvaluateFormula_StepPositionText", _session.CurrentStepNumber, _session.StepCount);
            _stepText.Text = step.Expression;
            _valueText.Text = UiText.Format("EvaluateFormula_ValueText", step.ValueText);
        }
        else
        {
            _positionText.Text = UiText.Get("EvaluateFormula_NoIntermediateStepsText");
            _stepText.Text = _session.Summary.FormulaText;
            _valueText.Text = UiText.Format("EvaluateFormula_ValueText", _session.Summary.ValueText);
        }

        _stepOutButton.IsEnabled = _session.CurrentStep is not null;
        _nextButton.IsEnabled = _session.CanMoveNext;
        _stepInButton.IsEnabled = _session.CanStepIn;
    }

    private void ShowFormulaHelp()
    {
        DialogMessageHelper.ShowInfo(this,
            UiText.Get("EvaluateFormula_HelpBody"),
            UiText.Get("EvaluateFormula_HelpTitle"));
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusFirstEnabledCommand();
    }

    private void FocusFirstEnabledCommand()
    {
        var target = _nextButton.IsEnabled ? _nextButton : _closeButton;
        target.Focus();
        Keyboard.Focus(target);
    }

    private void RefreshFormulaHighlight()
    {
        var highlight = _session.CurrentHighlight;
        _formulaText.Inlines.Clear();
        _formulaText.Inlines.Add(new Run(UiText.Get("EvaluateFormula_FormulaPrefix")));
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
