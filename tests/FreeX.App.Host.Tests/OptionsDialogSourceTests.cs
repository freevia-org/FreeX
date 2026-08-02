using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [Fact]
    public void ParityCapture_OptionsTabsUsePlannerCaptureFrameForDefaultAndCategories()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("Func<string, (double Width, double Height)>? captureSizeResolver = null");
        source.Should().Contain("var captureSize = captureSizeResolver?.Invoke(surfaceId);");
        source.Should().Contain("var captureSize = captureSizeResolver?.Invoke(tabSurfaceId);");
        source.Should().Contain("ApplyDialogClientCaptureSize(liveDialog, captureSize);");
        source.Should().Contain("dialog.ActualWidth - content.ActualWidth");
        source.Should().Contain("dialog.ActualHeight - content.ActualHeight");
        source.Should().Contain("captureSizeResolver: surfaceId => surfaceId.Equals(\"dialog.Options.Formulas\", StringComparison.Ordinal)");
        source.Should().Contain("(OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.FormulasCaptureHeight)");
        source.Should().Contain("(OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.CaptureHeight)");
    }

    [Fact]
    public void ParityCapture_TargetsOptionsSaveWithCurrentWpfClientFrame()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("string.Equals(targetSurfaceId, \"dialog.Options.Save\", StringComparison.Ordinal)");
        source.Should().Contain("captureOnlySurfaceId: targetSurfaceId");
        source.Should().Contain("captureSizeResolver: surfaceId =>");
        source.Should().Contain("OptionsDialogPlanner.CaptureWidth, OptionsDialogPlanner.CaptureHeight");
    }

    [Fact]
    public void ParityCapture_TargetsOptionsLanguageForFocusedEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("string.Equals(targetSurfaceId, \"dialog.Options.Language\", StringComparison.Ordinal)");
        source.Should().Contain("captureOnlySurfaceId: targetSurfaceId");
    }

    [Fact]
    public void ParityCapture_TargetsOptionsEaseOfAccessForFocusedEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("string.Equals(targetSurfaceId, \"dialog.Options.EaseOfAccess\", StringComparison.Ordinal)");
        source.Should().Contain("captureOnlySurfaceId: targetSurfaceId");
    }

    [Fact]
    public void ParityCapture_TargetsOptionsTrustCenterForFocusedEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("string.Equals(targetSurfaceId, \"dialog.Options.TrustCenter\", StringComparison.Ordinal)");
        source.Should().Contain("captureOnlySurfaceId: targetSurfaceId");
    }

    [Fact]
    public void ParityCapture_TargetsOptionsCustomizeRibbonForFocusedEvidence()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("string.Equals(targetSurfaceId, \"dialog.Options.CustomizeRibbon\", StringComparison.Ordinal)");
        source.Should().Contain("captureOnlySurfaceId: targetSurfaceId");
    }

    [Fact]
    public void ParityCapture_OptionsAdvancedRendersActionFooterInsideCanonicalClientFrame()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            dialog.Show();
            try
            {
                GetControl<ListBox>(dialog, "TabList").SelectedIndex = 6;
                dialog.UpdateLayout();

                var bitmap = ParityCapture.RenderDialogClientFrameForTest(
                    dialog,
                    OptionsDialogPlanner.CaptureWidth,
                    OptionsDialogPlanner.CaptureHeight);
                var content = dialog.Content.Should().BeAssignableTo<FrameworkElement>().Subject;
                var footer = GetControl<Border>(dialog, "OptionsFooterBorder");
                var okButton = GetControl<Button>(dialog, "OkBtn");
                var cancelButton = GetControl<Button>(dialog, "CancelBtn");
                var footerBounds = footer.TransformToAncestor(content).TransformBounds(new Rect(footer.RenderSize));
                var okBounds = okButton.TransformToAncestor(content).TransformBounds(new Rect(okButton.RenderSize));
                var cancelBounds = cancelButton.TransformToAncestor(content).TransformBounds(new Rect(cancelButton.RenderSize));

                content.ActualWidth.Should().BeApproximately(OptionsDialogPlanner.CaptureWidth, 0.25);
                content.ActualHeight.Should().BeApproximately(OptionsDialogPlanner.CaptureHeight, 0.25);
                footer.ActualHeight.Should().BeApproximately(OptionsDialogPlanner.FooterHeight, 0.01);
                footerBounds.Bottom.Should().BeApproximately(OptionsDialogPlanner.CaptureHeight, 0.25);
                okBounds.Bottom.Should().BeLessThan(footerBounds.Bottom);
                cancelBounds.Bottom.Should().BeLessThan(footerBounds.Bottom);
                bitmap.PixelWidth.Should().Be((int)OptionsDialogPlanner.CaptureWidth);
                bitmap.PixelHeight.Should().Be((int)Math.Ceiling(OptionsDialogPlanner.CaptureHeight));
                CountNonWhitePixels(bitmap, okBounds).Should().BeGreaterThan(
                    500,
                    "the captured Advanced surface must paint the OK action button inside its footer");
                CountNonWhitePixels(bitmap, cancelBounds).Should().BeGreaterThan(
                    500,
                    "the captured Advanced surface must paint the Cancel action button inside its footer");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static int CountNonWhitePixels(BitmapSource bitmap, Rect bounds)
    {
        var source = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.PixelWidth);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.PixelHeight);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.PixelHeight);
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = (y * source.PixelWidth + x) * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var alpha = pixels[offset + 3];
                if (alpha > 10 && (red < 245 || green < 245 || blue < 245))
                    count++;
            }
        }

        return count;
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

        "OptEaseAccessibilityDisplay"

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
