using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Xml.Linq;
using Free.Shared.Ribbon.Wpf;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Free.Shared.Ribbon.Wpf.Tests;

public sealed class SharedRibbonControlResourcesTests
{
    private const string SharedResourceUri =
        "/Free.Shared.Ribbon.Wpf;component/SharedRibbonControlResources.xaml";

    private static readonly string[] SharedStyleKeys =
    [
        "RibbonBtn",
        "RibbonToggleBtn",
        "GroupLbl",
        "RibbonGroupPanel",
        "RibbonGroupLabelBorder",
        "RibbonGroupDivider",
        "RibbonLargeButton",
        "RibbonIconButton",
        "RibbonIconToggleButton",
    ];

    [Fact]
    public void Shared_dictionary_is_the_single_xaml_owner_for_common_ribbon_styles()
    {
        var root = FindRepositoryRoot();
        var sharedPath = Path.Combine(root, "shared", "Free.Shared.Ribbon.Wpf", "SharedRibbonControlResources.xaml");
        var freeXPath = Path.Combine(root, "src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml");
        var freeWPath = Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonResources.xaml");

        var shared = XDocument.Load(sharedPath);
        var freeX = XDocument.Load(freeXPath);
        var freeW = XDocument.Load(freeWPath);
        var sharedKeys = ReadStyleKeys(shared);
        var freeXKeys = ReadStyleKeys(freeX);
        var freeWKeys = ReadStyleKeys(freeW);

        sharedKeys.Should().BeEquivalentTo(SharedStyleKeys);
        freeXKeys.Intersect(SharedStyleKeys, StringComparer.Ordinal).Should().BeEmpty();
        freeWKeys.Intersect(SharedStyleKeys, StringComparer.Ordinal).Should().BeEmpty();
        freeXKeys.Should().Contain("RibbonTallButton").And.Contain("RibbonCommandButton");
        freeWKeys.Should().BeEmpty();

        ReadMergedSources(freeX).Should().Contain(SharedResourceUri);
        ReadMergedSources(freeW).Should().Contain(SharedResourceUri);

        var sharedSource = File.ReadAllText(sharedPath);
        sharedSource.Should().NotContain("FreeX").And.NotContain("FreeW");
        foreach (var key in new[]
                 {
                     "ThemeNeutralTextBrush",
                     "ThemeNeutralMutedTextBrush",
                     "ThemeNeutralBorderBrush",
                     "ThemeNeutralBorderStrongBrush",
                     "ThemeAccentBrush",
                     "ThemeAccentDarkBrush",
                     "ThemeAccentPressedBrush",
                     "ThemeRibbonButtonHoverBrush",
                 })
        {
            sharedSource.Should().Contain(key);
        }
    }

    [Fact]
    [Trait("Category", "RibbonUiLane")]
    public void Shared_dictionary_preserves_layout_inheritance_metadata_and_visual_state_contracts()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(
                FindRepositoryRoot(),
                "shared",
                "Free.Shared.Ribbon.Wpf",
                "SharedRibbonControlResources.xaml");
            var source = File.ReadAllText(path).Replace(
                "clr-namespace:Free.Shared.Ribbon.Wpf\"",
                "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf\"",
                StringComparison.Ordinal);
            var resources = XamlReader.Parse(source).Should().BeOfType<ResourceDictionary>().Which;

            var buttonStyle = GetStyle(resources, "RibbonBtn", typeof(Button));
            GetSetterValue(buttonStyle, Control.PaddingProperty).Should().Be(new Thickness(4, 2, 4, 2));
            GetSetterValue(buttonStyle, FrameworkElement.MarginProperty).Should().Be(new Thickness(1));
            GetSetterValue(buttonStyle, Control.BorderThicknessProperty).Should().Be(new Thickness(1));
            GetSetterValue(buttonStyle, Control.FontSizeProperty).Should().Be(12d);
            GetSetterValue(buttonStyle, UIElement.FocusableProperty).Should().Be(true);
            GetSetterValue(buttonStyle, KeyboardNavigation.IsTabStopProperty).Should().Be(true);
            GetSetterValue(buttonStyle, FrameworkElement.CursorProperty).Should().Be(Cursors.Hand);

            AssertDynamicResource(
                GetTriggerSetter(buttonStyle, UIElement.IsMouseOverProperty, true, Control.BackgroundProperty),
                "ThemeRibbonButtonHoverBrush");
            AssertDynamicResource(
                GetTriggerSetter(buttonStyle, UIElement.IsMouseOverProperty, true, Control.BorderBrushProperty),
                "ThemeNeutralBorderStrongBrush");
            AssertDynamicResource(
                GetTriggerSetter(buttonStyle, ButtonBase.IsPressedProperty, true, Control.BackgroundProperty),
                "ThemeAccentPressedBrush");
            AssertDynamicResource(
                GetTriggerSetter(buttonStyle, ButtonBase.IsPressedProperty, true, Control.BorderBrushProperty),
                "ThemeAccentBrush");
            AssertDynamicResource(
                GetTriggerSetter(buttonStyle, UIElement.IsEnabledProperty, false, Control.ForegroundProperty),
                "ThemeNeutralMutedTextBrush");
            GetTriggerSetter(buttonStyle, UIElement.IsEnabledProperty, false, FrameworkElement.CursorProperty)
                .Value.Should().Be(Cursors.Arrow);

            var buttonTemplate = GetSetterValue(buttonStyle, Control.TemplateProperty)
                .Should().BeOfType<ControlTemplate>().Which;
            buttonTemplate.TargetType.Should().Be(typeof(Button));
            AssertDynamicResource(
                GetTriggerSetter(buttonTemplate, UIElement.IsKeyboardFocusedProperty, true, Control.BorderBrushProperty),
                "ThemeAccentBrush");
            GetTriggerSetter(buttonTemplate, UIElement.IsEnabledProperty, false, UIElement.OpacityProperty)
                .Value.Should().Be(0.58d);

            var toggleStyle = GetStyle(resources, "RibbonToggleBtn", typeof(ToggleButton));
            GetSetterValue(toggleStyle, Control.PaddingProperty).Should().Be(new Thickness(3, 1, 3, 1));
            GetSetterValue(toggleStyle, UIElement.FocusableProperty).Should().Be(true);
            GetSetterValue(toggleStyle, KeyboardNavigation.IsTabStopProperty).Should().Be(true);
            AssertDynamicResource(
                GetTriggerSetter(toggleStyle, ToggleButton.IsCheckedProperty, true, Control.BackgroundProperty),
                "ThemeAccentPressedBrush");
            AssertCheckedHoverContract(toggleStyle);

            var largeStyle = GetStyle(resources, "RibbonLargeButton", typeof(Button));
            largeStyle.BasedOn.Should().BeSameAs(buttonStyle);
            GetSetterValue(largeStyle, FrameworkElement.WidthProperty).Should().Be(70d);
            GetSetterValue(largeStyle, FrameworkElement.HeightProperty).Should().Be(76d);
            GetSetterValue(largeStyle, Control.PaddingProperty).Should().Be(new Thickness(3, 2, 3, 2));

            var iconStyle = GetStyle(resources, "RibbonIconButton", typeof(Button));
            iconStyle.BasedOn.Should().BeSameAs(buttonStyle);
            GetSetterValue(iconStyle, FrameworkElement.WidthProperty).Should().Be(24d);
            GetSetterValue(iconStyle, FrameworkElement.HeightProperty).Should().Be(22d);
            GetSetterValue(iconStyle, Control.PaddingProperty).Should().Be(new Thickness(2));

            var iconToggleStyle = GetStyle(resources, "RibbonIconToggleButton", typeof(ToggleButton));
            iconToggleStyle.BasedOn.Should().BeSameAs(toggleStyle);
            GetSetterValue(iconToggleStyle, FrameworkElement.WidthProperty).Should().Be(24d);
            GetSetterValue(iconToggleStyle, FrameworkElement.HeightProperty).Should().Be(22d);
            GetSetterValue(iconToggleStyle, Control.PaddingProperty).Should().Be(new Thickness(2));

            var group = new Grid { Style = GetStyle(resources, "RibbonGroupPanel", typeof(Grid)) };
            group.Margin.Should().Be(new Thickness(0));
            group.MinHeight.Should().Be(96d);
            RibbonMetadata.GetRole(group).Should().Be(RibbonMetadataRole.RibbonGroup);

            var labelBorder = new Border
            {
                Style = GetStyle(resources, "RibbonGroupLabelBorder", typeof(Border))
            };
            labelBorder.BorderThickness.Should().Be(new Thickness(0, 1, 0, 0));
            labelBorder.MinHeight.Should().Be(18d);

            var divider = new Rectangle
            {
                Style = GetStyle(resources, "RibbonGroupDivider", typeof(Rectangle))
            };
            divider.Width.Should().Be(1d);
            divider.Margin.Should().Be(new Thickness(2, 5, 3, 18));
        });
    }

    private static void AssertCheckedHoverContract(Style style)
    {
        var trigger = style.Triggers.OfType<MultiTrigger>().Single(candidate =>
            candidate.Conditions.Count == 2 &&
            candidate.Conditions.Cast<Condition>().Any(condition =>
                condition.Property == ToggleButton.IsCheckedProperty && Equals(condition.Value, true)) &&
            candidate.Conditions.Cast<Condition>().Any(condition =>
                condition.Property == UIElement.IsMouseOverProperty && Equals(condition.Value, true)));

        AssertDynamicResource(GetSetter(trigger.Setters, Control.BackgroundProperty), "ThemeAccentPressedBrush");
        AssertDynamicResource(GetSetter(trigger.Setters, Control.BorderBrushProperty), "ThemeAccentDarkBrush");
    }

    private static Style GetStyle(ResourceDictionary resources, string key, Type targetType)
    {
        var style = resources[key].Should().BeOfType<Style>().Which;
        style.TargetType.Should().Be(targetType);
        return style;
    }

    private static object? GetSetterValue(Style style, DependencyProperty property) =>
        GetSetter(style.Setters, property).Value;

    private static Setter GetTriggerSetter(
        Style style,
        DependencyProperty triggerProperty,
        object triggerValue,
        DependencyProperty setterProperty)
    {
        var trigger = style.Triggers.OfType<Trigger>().Single(candidate =>
            candidate.Property == triggerProperty && Equals(candidate.Value, triggerValue));
        return GetSetter(trigger.Setters, setterProperty);
    }

    private static Setter GetTriggerSetter(
        ControlTemplate template,
        DependencyProperty triggerProperty,
        object triggerValue,
        DependencyProperty setterProperty)
    {
        var trigger = template.Triggers.OfType<Trigger>().Single(candidate =>
            candidate.Property == triggerProperty && Equals(candidate.Value, triggerValue));
        return GetSetter(trigger.Setters, setterProperty);
    }

    private static Setter GetSetter(SetterBaseCollection setters, DependencyProperty property) =>
        setters.OfType<Setter>().Single(setter => setter.Property == property);

    private static void AssertDynamicResource(Setter setter, string expectedKey) =>
        setter.Value.Should().BeOfType<DynamicResourceExtension>()
            .Which.ResourceKey.Should().Be(expectedKey);

    private static string[] ReadStyleKeys(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Key"))
            .Select(element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value)
            .ToArray();

    private static string[] ReadMergedSources(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary")
            .Select(element => element.Attribute("Source")?.Value)
            .Where(source => source is not null)
            .Cast<string>()
            .ToArray();

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
