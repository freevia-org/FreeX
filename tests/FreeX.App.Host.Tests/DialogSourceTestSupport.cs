using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

internal static class DialogSourceTestSupport
{
    public static string ReadHostSources(params string[] fileNames) =>
        ReadHostSourcesWithSeparator(Environment.NewLine, fileNames);

    public static string ReadHostSourcesWithSeparator(string separator, params string[] fileNames) =>
        SourceTextTestSupport.ReadSources(ReadHostSource, separator, fileNames);

    public static string ReadHostSourceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.ReadAllText(
            HostSourceRoot(relativeParts[0]).Concat(relativeParts).ToArray());

    public static string ReadAppUiSources(params string[] fileNames) =>
        ReadAppUiSourcesWithSeparator(Environment.NewLine, fileNames);

    public static string ReadAppUiSourcesWithSeparator(string separator, params string[] fileNames) =>
        string.Join(separator, fileNames.Select(ReadAppUiSource));

    public static string FindHostSourceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.Find(
            HostSourceRoot(relativeParts[0]).Concat(relativeParts).ToArray());

    public static string FindHostSourceDirectory(params string[] relativeParts) =>
        Path.GetDirectoryName(FindHostSourceFile(relativeParts))
        ?? throw new DirectoryNotFoundException("Could not locate FreeX.App.Host source directory.");

    public static XDocument LoadHostXamlDocument(params string[] relativeParts) =>
        XDocument.Load(FindHostSourceFile(relativeParts));

    public static string ReadClassSource(string fileName, string startMarker, string endMarker) =>
        SourceTextTestSupport.ExtractBetweenMarkers(ReadHostSource(fileName), startMarker, endMarker);

    public static T GetPrivateField<T>(object instance, string name)
        where T : class =>
        SourceTextTestSupport.GetPrivateField<T>(instance, name);

    public static void InvokePrivateHandler(object instance, string methodName) =>
        InvokePrivateHandler(instance, methodName, instance);

    public static void InvokePrivateHandler(object instance, string methodName, object sender)
    {
        var method = SourceTextTestSupport.GetPrivateMethod(instance, methodName);
        object[] parameters = method.GetParameters().Length == 0
            ? []
            : [sender, new RoutedEventArgs()];
        method.Invoke(instance, parameters);
    }

    public static void InvokePrivateHandlerAllowingNonModalDialogResult(object instance, string methodName)
    {
        try
        {
            InvokePrivateHandler(instance, methodName);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation &&
                                                   invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
        }
    }

    public static MouseButtonEventArgs CreateMouseDoubleClickEvent() =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Control.MouseDoubleClickEvent
        };

    public static RoutedEventArgs CreateButtonClickEvent() =>
        new(Button.ClickEvent);

    public static void ClickButton(Button button) =>
        button.RaiseEvent(CreateButtonClickEvent());

    public static void ClickButtonAllowingNonModalDialogResult(Button button)
    {
        try
        {
            ClickButton(button);
        }
        catch (InvalidOperationException invalidOperation)
            when (invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
        }
    }

    private static string ReadHostSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText([.. HostSourceRoot(fileName), fileName]);

    private static string[] HostSourceRoot(string fileName) =>
        fileName is "ParityCapture.cs" or "MainWindow.NameBoxParityCapture.cs" ||
        fileName.StartsWith("MainWindow.ScreenshotTour", StringComparison.Ordinal)
            ? ["tools", "FreeX.ParityCapture.Wpf", "Capture"]
            : ["src", "FreeX.App.Host"];

    public static string ReadLocalizationSources(params string[] fileNames) =>
        SourceTextTestSupport.ReadSources(ReadLocalizationSource, fileNames);

    private static string ReadLocalizationSource(string fileName) =>
        // The neutral Strings.resx (and satellite cultures) moved out of FreeX.App.Host into the
        // shared FreeX.App.Localization project; resolve localization assets from there.
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Localization", fileName);

    public static string ReadPresentationSources(params string[] relativeParts) =>
        WorkspaceFileLocator.ReadAllText(
            new[] { "src", "FreeX.App.Presentation" }.Concat(relativeParts).ToArray());

    private static string ReadAppUiSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.UI", fileName);

    public static string ReadShellSources(params string[] fileNames) =>
        string.Join(Environment.NewLine, fileNames.Select(ReadShellSource));

    private static string ReadShellSource(string fileName) =>
        // The WPF-facing shell helpers (dialog button rows, message helper, dialog sizing/focus) live in
        // Free.Shared.Shell.Wpf after the shared-shell split; the platform-neutral core is Free.Shared.Shell.
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.Shell.Wpf", fileName);

    public static string ReadAppServicesSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", fileName);

    public static string ReadSharedAppServicesSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.AppServices", fileName);

    public static string ReadSharedRibbonWpfSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("shared", "Free.Shared.Ribbon.Wpf", fileName);

    public static string ReadAppServicesRibbonSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "Ribbon", fileName);

    public static string ReadRibbonDefinitionSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Ribbon.Definitions", fileName);

    public static string ReadRibbonDefinitionFile(params string[] relativeParts) =>
        WorkspaceFileLocator.ReadAllText(
            new[] { "src", "FreeX.Ribbon.Definitions" }.Concat(relativeParts).ToArray());

    public static string FindRibbonDefinitionFile(params string[] relativeParts) =>
        WorkspaceFileLocator.Find(
            new[] { "src", "FreeX.Ribbon.Definitions" }.Concat(relativeParts).ToArray());

    public static string FindRibbonDefinitionDirectory(params string[] relativeParts) =>
        Path.GetDirectoryName(FindRibbonDefinitionFile(relativeParts))
        ?? throw new DirectoryNotFoundException("Could not locate FreeX.Ribbon.Definitions source directory.");
}
