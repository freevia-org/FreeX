using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using FreeX.App.Host;
using FreeX.App.Services;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_UsesSharedFrameSizingContract()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions());
            try
            {
                dialog.Width.Should().Be(OptionsDialogPlanner.WindowWidth);
                dialog.Height.Should().Be(OptionsDialogPlanner.WindowHeight);
                OptionsDialogPlanner.CaptureWidth.Should().Be(744);
                OptionsDialogPlanner.CaptureHeight.Should().Be(520.5);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static T GetControl<T>(OptionsDialog dialog, string name)

        where T : class

    {

        var field = typeof(OptionsDialog).GetField(

            name,

            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        field.Should().NotBeNull();

        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;

    }



    private static readonly string[] DeferredOptionCheckboxNames =

    [

        "OptFormulasAutocomplete",

        "OptProofingCheckSpellingAsYouType",

        "OptProofingIgnoreUppercase",

        "OptProofingFlagRepeatedWords",

        "OptEaseFeedbackSound",

        "OptEaseQuickAnalysis",

        "OptEaseAccessibilityDisplay",

        "OptAdvancedFillHandle"

    ];



    private static void AssertNamedCheckBoxDisabled(XDocument document, string name)

    {

        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";



        var checkBox = document

            .Descendants(presentation + "CheckBox")

            .Single(element => element.Attribute(xaml + "Name")?.Value == name);



        checkBox.Attribute("IsEnabled")?.Value.Should().Be("False");

    }



    private static IReadOnlyList<string> GetListDisplayNames(ListBox listBox) =>

        listBox.Items

            .Cast<object>()

            .Select(GetListDisplayName)

            .ToArray();



    private static string GetListDisplayName(object item) =>

        item.GetType().GetProperty("DisplayName")?.GetValue(item) as string ?? string.Empty;



    private static MouseButtonEventArgs CreateMouseDoubleClickEvent() =>
        DialogSourceTestSupport.CreateMouseDoubleClickEvent();



    private static KeyEventArgs CreateKeyDownEvent(OptionsDialog dialog, Key key)

    {

        var source = PresentationSource.FromVisual(dialog);

        source.Should().NotBeNull();

        return new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, key)

        {

            RoutedEvent = Keyboard.KeyDownEvent

        };

    }



    private static bool InvokeQuickAccessSelectedKeyHandler(

        OptionsDialog dialog,

        Key key,

        ModifierKeys modifiers)

    {

        var method = typeof(OptionsDialog).GetMethod(

            "TryHandleQuickAccessSelectedCommandsListKey",

            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        return method!.Invoke(dialog, [key, modifiers]).Should().BeOfType<bool>().Subject;

    }



    private static void ClickOkAllowingNonModalDialogResult(OptionsDialog dialog)

    {

        var okButton = GetControl<Button>(dialog, "OkBtn");

        DialogSourceTestSupport.ClickButtonAllowingNonModalDialogResult(okButton);

    }
}
