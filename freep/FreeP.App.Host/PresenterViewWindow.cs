using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Native WPF presenter dashboard synchronized with a running slideshow.</summary>
public sealed class PresenterViewWindow : Window
{
    private readonly Presentation _presentation;
    private readonly Func<SlideShowPresenterState> _stateProvider;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SlideCanvas _currentPreview;
    private readonly SlideCanvas _nextPreview;
    private readonly TextBlock _statusText;
    private readonly TextBlock _elapsedText;
    private readonly TextBlock _currentLabel;
    private readonly TextBlock _nextLabel;
    private readonly TextBox _notesText;

    public PresenterViewWindow(
        Presentation presentation,
        Func<SlideShowPresenterState> stateProvider)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));

        Title = "Presenter View";
        Width = 1200;
        Height = 760;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(30, 34, 42));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusText = MakeText(18, FontWeights.SemiBold);
        _elapsedText = MakeText(18, FontWeights.Normal);
        Grid.SetColumn(_elapsedText, 1);
        header.Children.Add(_statusText);
        header.Children.Add(_elapsedText);
        root.Children.Add(header);

        var previews = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _currentLabel = MakeText(14, FontWeights.SemiBold);
        _nextLabel = MakeText(14, FontWeights.SemiBold);
        _currentPreview = MakePreview();
        _nextPreview = MakePreview();
        previews.Children.Add(BuildPreviewPanel("Current", _currentLabel, _currentPreview));
        var nextPanel = BuildPreviewPanel("Next", _nextLabel, _nextPreview);
        Grid.SetColumn(nextPanel, 1);
        previews.Children.Add(nextPanel);
        Grid.SetRow(previews, 1);
        root.Children.Add(previews);

        var notesPanel = new Grid();
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var notesHeading = MakeText(14, FontWeights.SemiBold);
        notesHeading.Text = "Speaker notes";
        notesHeading.Margin = new Thickness(0, 0, 0, 6);
        _notesText = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(45, 50, 61)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 87, 102)),
            Padding = new Thickness(10),
        };
        Grid.SetRow(_notesText, 1);
        notesPanel.Children.Add(notesHeading);
        notesPanel.Children.Add(_notesText);
        Grid.SetRow(notesPanel, 2);
        root.Children.Add(notesPanel);

        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _refreshTimer.Tick += (_, _) => RefreshFromState();
        Loaded += (_, _) =>
        {
            RefreshFromState();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public void RefreshFromState()
    {
        var plan = SlideShowPresenterViewPlanner.Build(_stateProvider());
        _statusText.Text = plan.StatusText;
        _elapsedText.Text = $"Elapsed {plan.ElapsedText}";
        _currentLabel.Text = plan.CurrentSlideLabel;
        _nextLabel.Text = plan.NextSlideLabel;
        _notesText.Text = plan.NotesText;
        _currentPreview.Slide = plan.CurrentSlide;
        _nextPreview.Slide = plan.NextSlide;
        _currentPreview.Refresh();
        _nextPreview.Refresh();
    }

    private SlideCanvas MakePreview() => new()
    {
        Presentation = _presentation,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static Border BuildPreviewPanel(
        string heading,
        TextBlock label,
        SlideCanvas preview)
    {
        var panel = new Grid { Margin = new Thickness(heading == "Current" ? 0 : 8, 0, 0, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = MakeText(13, FontWeights.Normal);
        title.Text = heading;
        title.Foreground = new SolidColorBrush(Color.FromRgb(170, 178, 194));
        panel.Children.Add(title);
        Grid.SetRow(label, 1);
        label.Margin = new Thickness(0, 3, 0, 0);
        panel.Children.Add(label);
        Grid.SetRow(preview, 2);
        panel.Children.Add(preview);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(38, 43, 53)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 87, 102)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = panel,
        };
    }

    private static TextBlock MakeText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
