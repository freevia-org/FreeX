using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class HeaderFooterDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly CheckBox _dateTimeCheck;
    private readonly CheckBox _footerCheck;
    private readonly CheckBox _slideNumberCheck;
    private readonly TextBox _footerBox;

    public HeaderFooterApplyPlan? LastApplyPlan { get; private set; }

    public HeaderFooterDialog(EditingSession editor, HeaderFooterCommandFocus focus)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var state = HeaderFooterCommandPlanner.BuildState(editor);
        var defaults = HeaderFooterCommandPlanner.BuildDefaultOptions(state, focus);

        Title = "Header and Footer";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel
        {
            Margin = new Thickness(14),
        };

        _dateTimeCheck = new CheckBox
        {
            Content = "Date and time",
            IsChecked = defaults.ShowDateTime,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _footerCheck = new CheckBox
        {
            Content = "Footer",
            IsChecked = defaults.ShowFooter,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _footerBox = new TextBox
        {
            Text = defaults.FooterText,
            Margin = new Thickness(20, 0, 0, 8),
            MinWidth = 260,
        };
        _slideNumberCheck = new CheckBox
        {
            Content = "Slide number",
            IsChecked = defaults.ShowSlideNumber,
            Margin = new Thickness(0, 0, 0, 12),
        };

        _footerCheck.Checked += (_, _) => UpdateFooterEnabled();
        _footerCheck.Unchecked += (_, _) => UpdateFooterEnabled();

        panel.Children.Add(_dateTimeCheck);
        panel.Children.Add(_footerCheck);
        panel.Children.Add(_footerBox);
        panel.Children.Add(_slideNumberCheck);
        panel.Children.Add(BuildButtonRow());

        Content = panel;
        UpdateFooterEnabled();

        if (focus == HeaderFooterCommandFocus.Footer)
        {
            _footerBox.Focus();
            _footerBox.SelectAll();
        }
    }

    private UIElement BuildButtonRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        row.Children.Add(MakeButton("Apply", isDefault: true, () => Apply(HeaderFooterApplyScope.CurrentSlide)));
        row.Children.Add(MakeButton("Apply to All", isDefault: false, () => Apply(HeaderFooterApplyScope.AllSlides)));
        row.Children.Add(MakeButton("Cancel", isDefault: false, () => DialogResult = false, isCancel: true));
        return row;
    }

    private static Button MakeButton(
        string label,
        bool isDefault,
        Action action,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 76,
            Margin = new Thickness(6, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateFooterEnabled()
    {
        _footerBox.IsEnabled = _footerCheck.IsChecked == true;
    }

    private void Apply(HeaderFooterApplyScope scope)
    {
        var options = new HeaderFooterApplyOptions(
            _dateTimeCheck.IsChecked == true,
            _footerCheck.IsChecked == true,
            _slideNumberCheck.IsChecked == true,
            _footerBox.Text ?? string.Empty,
            scope);

        if (HeaderFooterCommandPlanner.TryApply(_editor, options, out var plan))
        {
            LastApplyPlan = plan;
            DialogResult = true;
        }
    }
}
