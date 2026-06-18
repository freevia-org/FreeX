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
        string.Join(separator, fileNames.Select(ReadHostSource));

    public static string ReadHostSourceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.ReadAllText(
            new[] { "src", "FreeX.App.Host" }.Concat(relativeParts).ToArray());

    public static string ReadAppUiSources(params string[] fileNames) =>
        ReadAppUiSourcesWithSeparator(Environment.NewLine, fileNames);

    public static string ReadAppUiSourcesWithSeparator(string separator, params string[] fileNames) =>
        string.Join(separator, fileNames.Select(ReadAppUiSource));

    public static string FindHostSourceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.Find(
            new[] { "src", "FreeX.App.Host" }.Concat(relativeParts).ToArray());

    public static string FindHostSourceDirectory(params string[] relativeParts) =>
        Path.GetDirectoryName(FindHostSourceFile(relativeParts))
        ?? throw new DirectoryNotFoundException("Could not locate FreeX.App.Host source directory.");

    public static XDocument LoadHostXamlDocument(params string[] relativeParts) =>
        XDocument.Load(FindHostSourceFile(relativeParts));

    public static string ReadClassSource(string fileName, string startMarker, string endMarker)
    {
        var source = ReadHostSource(fileName);
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var end = string.IsNullOrEmpty(endMarker)
            ? source.Length
            : source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            end = source.Length;

        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    public static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var type = instance.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    public static void InvokePrivateHandler(object instance, string methodName) =>
        InvokePrivateHandler(instance, methodName, instance);

    public static void InvokePrivateHandler(object instance, string methodName, object sender)
    {
        var type = instance.GetType();
        MethodInfo? method = null;
        while (type is not null && method is null)
        {
            method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        method.Should().NotBeNull();
        object[] parameters = method!.GetParameters().Length == 0
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
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", fileName);

    private static string ReadAppUiSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.UI", fileName);

    public static string ReadAppServicesSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", fileName);

    public static string ReadAppServicesRibbonSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "Ribbon", fileName);

    public static string ReadRibbonDefinitionSource(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.Ribbon.Definitions", fileName);
}
