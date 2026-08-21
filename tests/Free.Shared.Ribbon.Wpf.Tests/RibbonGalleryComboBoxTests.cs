using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
public sealed class RibbonGalleryComboBoxTests
{
    [Fact]
    public void GalleryCombo_UsesItsOwnPopupAndKeepsTheCompactComboSelection()
    {
        RunSta(() =>
        {
            var choices = new[]
            {
                new RibbonComboBoxChoice("General", "General", "No specific format", RibbonComboBoxGalleryPreviewKind.General),
                new RibbonComboBoxChoice("number-format.more", "More Number Formats...", null, RibbonComboBoxGalleryPreviewKind.More),
            };
            var gallery = new RibbonGalleryComboBox();
            foreach (var choice in choices)
                gallery.Items.Add(choice);
            gallery.SetGalleryChoices(choices);
            gallery.SelectedIndex = 0;

            gallery.OpenGallery();

            gallery.IsGalleryOpen.Should().BeTrue();
            gallery.IsDropDownOpen.Should().BeFalse();
            gallery.GalleryPopupChild.Measure(new Size(264, double.PositiveInfinity));
            gallery.GalleryPopupChild.DesiredSize.Width.Should().BeGreaterThan(0);

            gallery.CloseGallery();
            gallery.IsGalleryOpen.Should().BeFalse();
        });
    }

    [Fact]
    public void CellStyleGallery_GroupsPreviewTilesAndExecutesTheSelectedStyle()
    {
        RunSta(() =>
        {
            RibbonCommandId? executed = null;
            var gallery = new RibbonCellStyleGalleryButton();
            gallery.SetMenu(
                new RibbonMenu(
                [
                    new RibbonMenuItem("Good", new RibbonCommandId("cell-style.good")),
                    new RibbonMenuItem("Heading 1", new RibbonCommandId("cell-style.heading1")),
                    new RibbonMenuItem("20% - Accent 1", new RibbonCommandId("cell-style.accent1")),
                ]),
                (commandId, _) => executed = commandId);

            gallery.OpenGallery();

            gallery.IsGalleryOpen.Should().BeTrue();
            gallery.ItemHeaders.Should().Equal("Good", "Heading 1", "20% - Accent 1");
            gallery.GalleryPopupChild.Measure(new Size(510, double.PositiveInfinity));
            gallery.GalleryPopupChild.Arrange(new Rect(0, 0, 510, gallery.GalleryPopupChild.DesiredSize.Height));
            var good = FindDescendant<Button>(gallery.GalleryPopupChild, button => button.Tag is RibbonCommandId id && id.Value == "cell-style.good");
            good.Should().NotBeNull();

            good!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            executed.Should().Be(new RibbonCommandId("cell-style.good"));
            gallery.IsGalleryOpen.Should().BeFalse();
        });
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T candidate && predicate(candidate))
            return candidate;

        var count = root is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;
        for (var index = 0; index < count; index++)
        {
            var match = FindDescendant(VisualTreeHelper.GetChild(root, index), predicate);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
