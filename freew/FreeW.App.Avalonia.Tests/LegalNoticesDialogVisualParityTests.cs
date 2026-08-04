using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class LegalNoticesDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact(Timeout = 15_000)]
    public async Task Legal_notices_matches_WPF_metrics_for_all_tabs_and_focus_targets()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new LegalNoticesDialog(
            [
                ("Project License", "license text"),
                ("Legal Notices", "legal text"),
                ("Privacy Notice", "privacy text"),
                ("Third-Party Notices", "third-party notices"),
                ("Third-Party License Texts", "third-party license texts"),
            ]);

            dialog.Width.Should().Be(LegalNoticesDialogMetrics.Width);
            dialog.Height.Should().Be(LegalNoticesDialogMetrics.Height);
            dialog.MinWidth.Should().Be(LegalNoticesDialogMetrics.MinWidth);
            dialog.MinHeight.Should().Be(LegalNoticesDialogMetrics.MinHeight);
            dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
            dialog.FontSize.Should().Be(12);

            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            tabs.Items.Count.Should().Be(5);
            tabs.SelectedIndex.Should().Be(0);
            tabs.Padding.Should().Be(new Thickness(0));
            tabs.HorizontalContentAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
            tabs.VerticalContentAlignment.Should().Be(global::Avalonia.Layout.VerticalAlignment.Stretch);

            var tabItems = tabs.Items.OfType<TabItem>().ToArray();
            tabItems.Select(tab => tab.Header?.ToString()).Should().Equal(
                "Project License",
                "Legal Notices",
                "Privacy Notice",
                "Third-Party Notices",
                "Third-Party License Texts");

            dialog.Show();
            dialog.UpdateLayout();
            dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(tabItems[0].Content);
            var headerPresenters = tabItems
                .SelectMany(tab => tab.GetVisualDescendants().OfType<ContentPresenter>())
                .Where(presenter => presenter.Name == "PART_ContentPresenter")
                .ToArray();
            headerPresenters.Should().HaveCount(5);
            headerPresenters.Should().OnlyContain(presenter =>
                presenter.Foreground.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color == Colors.Black);
            headerPresenters
                .SelectMany(presenter => presenter.GetVisualDescendants().OfType<AccessText>())
                .Should().OnlyContain(accessText =>
                    accessText.Foreground.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color == Colors.Black);
            var focusedDocumentBorder = ((TextBox)tabItems[0].Content!)
                .GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Name == "PART_BorderElement");
            ((ISolidColorBrush)focusedDocumentBorder.BorderBrush!)
                .Color.Should().Be(Color.FromRgb(86, 157, 229));
            ((TextBox)tabItems[0].Content!).Padding.Top
                .Should().BeGreaterThan(LegalNoticesDialogMetrics.TextPadding);

            foreach (var tab in tabItems)
            {
                var text = tab.Content.Should().BeOfType<TextBox>().Subject;
                text.IsReadOnly.Should().BeTrue();
                text.Focusable.Should().BeTrue();
                text.AcceptsReturn.Should().BeTrue();
                text.AcceptsTab.Should().BeTrue();
                text.Margin.Should().Be(new Thickness(2, 0, 2, 0));
                text.Padding.Left.Should().Be(LegalNoticesDialogMetrics.TextPadding + 4);
                text.Padding.Top.Should().BeGreaterThanOrEqualTo(LegalNoticesDialogMetrics.TextPadding);
                text.Padding.Right.Should().Be(LegalNoticesDialogMetrics.TextPadding);
                text.Padding.Bottom.Should().Be(LegalNoticesDialogMetrics.TextPadding);
                text.FontSize.Should().Be(12.1);
                if (ReferenceEquals(text, tabItems[0].Content))
                    text.LineHeight.Should().Be(14.6);
                else
                    double.IsNaN(text.LineHeight).Should().BeTrue();
                text.FontFamily.Should().Be(new FontFamily("Consolas"));
                text.VerticalContentAlignment.Should().Be(global::Avalonia.Layout.VerticalAlignment.Top);
                text.HorizontalContentAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Left);
                ((ISolidColorBrush)text.Foreground!).Color.Should().Be(Colors.Black);
                text.GetValue(ScrollViewer.AllowAutoHideProperty).Should().BeFalse();
                AutomationProperties.GetAutomationId(text).Should().StartWith("LegalNotices");
            }

            for (var index = 0; index < tabItems.Length; index++)
            {
                tabs.SelectedIndex = index;
                tabs.SelectedItem.Should().BeSameAs(tabItems[index]);
            }
            var close = dialog.GetLogicalDescendants().OfType<Button>().Single();
            close.IsDefault.Should().BeTrue();
            close.IsCancel.Should().BeTrue();
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legal_notices_uses_a_local_right_margin_override_without_changing_shared_document_chrome()
    {
        await Session.Dispatch(() =>
        {
            var sharedDefault = new TextBox();
            AvaloniaCompactDialogChrome.ApplyAvaloniaReadOnlyDocumentTemplatePadding(
                sharedDefault,
                LegalNoticesDialogMetrics.TextPadding);
            sharedDefault.Margin.Should().Be(new Thickness(2, 0, 1, 0));

            var dialog = new LegalNoticesDialog(
                [("Legal Notices", "legal text")]);
            var text = dialog.GetLogicalDescendants().OfType<TextBox>().Single();

            AvaloniaCompactDialogChrome.ApplyAvaloniaReadOnlyDocumentTemplatePadding(
                text,
                LegalNoticesDialogMetrics.TextPadding,
                rightMargin: 2);
            text.Margin.Should().Be(new Thickness(2, 0, 2, 0));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legal_notices_preserves_shared_keyboard_scroll_and_automation_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new LegalNoticesDialog(
            [
                ("Project License", string.Join("\n", Enumerable.Repeat("a long legal notice line that should wrap and scroll", 80))),
                ("Legal Notices", "legal text"),
                ("Privacy Notice", "privacy text"),
            ]);
            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            var close = dialog.GetLogicalDescendants().OfType<Button>().Single();
            close.Width.Should().Be(84);
            var textBoxes = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                var first = textBoxes[0];
                var scroll = first.GetVisualDescendants().OfType<ScrollViewer>().Single();
                scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);
                first.VerticalContentAlignment.Should().Be(global::Avalonia.Layout.VerticalAlignment.Top);
                first.Padding.Top.Should().Be(LegalNoticesDialogMetrics.TextPadding);
                first.LineHeight.Should().Be(15.0);
                scroll.GetVisualDescendants().OfType<ScrollBar>()
                    .Single(bar => bar.Orientation == Orientation.Vertical)
                    .Bounds.Width.Should().Be(18);
                scroll.GetValue(ScrollViewer.AllowAutoHideProperty).Should().BeFalse();
                first.Focus().Should().BeTrue();
                first.BorderBrush.Should().BeAssignableTo<ISolidColorBrush>();
                ((ISolidColorBrush)first.BorderBrush!).Color.Should().Be(Color.FromRgb(86, 157, 229));
                first.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Tab,
                    KeyModifiers = KeyModifiers.None,
                });
                tabs.SelectedIndex.Should().Be(0);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);

                first.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Tab,
                    KeyModifiers = KeyModifiers.Control,
                });
                tabs.SelectedIndex.Should().Be(0, "WPF leaves Ctrl+Tab to the native read-only text control");
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);

                first.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Tab,
                    KeyModifiers = KeyModifiers.Shift | KeyModifiers.Control,
                });
                tabs.SelectedIndex.Should().Be(0);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);

                textBoxes.Should().OnlyContain(text =>
                    text.IsReadOnly &&
                    text.Focusable &&
                    text.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty) == ScrollBarVisibility.Auto &&
                    text.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty) == ScrollBarVisibility.Disabled);
                AutomationProperties.GetAutomationId(tabs).Should().Be("LegalNoticesSectionTabs");
                AutomationProperties.GetAutomationId(close).Should().Be("LegalNoticesCloseButton");
                close.IsDefault.Should().BeTrue();
                close.IsCancel.Should().BeTrue();
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Legal_notices_uses_distinct_short_and_overflow_authority_line_boxes()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new LegalNoticesDialog(
            [
                ("Short", "short notice"),
                ("Long", string.Join("\n", Enumerable.Repeat("long notice content that remains scrollable", 90))),
            ]);
            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            var textBoxes = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                textBoxes[0].LineHeight.Should().Be(14.6);
                textBoxes[0].Padding.Top.Should().BeGreaterThanOrEqualTo(LegalNoticesDialogMetrics.TextPadding);

                tabs.SelectedIndex = 1;
                dialog.UpdateLayout();
                textBoxes[1].LineHeight.Should().Be(15.0);
                textBoxes[1].Padding.Top.Should().Be(LegalNoticesDialogMetrics.TextPadding);
                textBoxes[1].GetVisualDescendants().OfType<ScrollViewer>().Single()
                    .Extent.Height.Should().BeGreaterThan(textBoxes[1].Bounds.Height);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(436, 228, 104)]
    [InlineData(436, 436, 0)]
    [InlineData(436, 900, 0)]
    [InlineData(double.NaN, 228, 0)]
    public void Legal_notices_short_document_inset_is_bounded_and_measure_independent(
        double viewportHeight,
        double documentHeight,
        double expectedInset)
    {
        AvaloniaCompactDialogChrome.CalculateReadOnlyDocumentInset(viewportHeight, documentHeight)
            .Should().Be(expectedInset);
    }

    [Theory]
    [InlineData(436, 14, 16, 16, false)]
    [InlineData(436, 26, 16, 16, false)]
    [InlineData(436, 27, 16, 16, true)]
    [InlineData(436, 80, 16, 16, true)]
    public void Legal_notices_overflow_line_height_is_planned_from_fixed_metrics(
        double viewportHeight,
        int lineCount,
        double overflowLineHeight,
        double verticalPadding,
        bool expected)
    {
        AvaloniaCompactDialogChrome.RequiresReadOnlyDocumentOverflowLineHeight(
                viewportHeight,
                lineCount,
                overflowLineHeight,
                verticalPadding)
            .Should().Be(expected);
    }

    [Fact]
    public async Task Legal_notices_keeps_selected_tab_and_scroll_offset_through_tab_lifecycle()
    {
        await Session.Dispatch(() =>
        {
            var notices = new[]
            {
                ("Legal Notices", string.Join("\n", Enumerable.Repeat("legal notice content that wraps and remains scrollable", 90))),
                ("Privacy Notice", string.Join("\n", Enumerable.Repeat("privacy notice content that wraps and remains scrollable", 90))),
                ("Third-Party Notices", string.Join("\n", Enumerable.Repeat("third-party notice content that wraps and remains scrollable", 90))),
                ("Third-Party License Texts", string.Join("\n", Enumerable.Repeat("third-party license content that wraps and remains scrollable", 90))),
            };
            var dialog = new LegalNoticesDialog(notices);
            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            var textBoxes = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            try
            {
                dialog.Show();
                dialog.UpdateLayout();

                var firstScroll = textBoxes[0].GetVisualDescendants().OfType<ScrollViewer>().Single();
                firstScroll.Extent.Height.Should().BeGreaterThan(firstScroll.Viewport.Height);
                firstScroll.Offset = new Vector(0, 36);
                var retainedOffset = firstScroll.Offset.Y;

                for (var index = 0; index < notices.Length; index++)
                {
                    tabs.SelectedIndex = index;
                    dialog.UpdateLayout();
                    tabs.SelectedItem.Should().BeSameAs(tabs.Items[index]);
                    ((TabItem)tabs.Items[index]!).Content.Should().BeSameAs(textBoxes[index]);
                }

                tabs.SelectedIndex = 0;
                dialog.UpdateLayout();
                firstScroll.Offset.Y.Should().Be(retainedOffset);
                textBoxes.Should().OnlyContain(text => text.IsReadOnly && text.AcceptsReturn && text.AcceptsTab);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }
}
