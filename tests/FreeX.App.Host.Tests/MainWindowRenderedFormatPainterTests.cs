using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Functional safety net for the persistent (double-click) Format Painter on the DECLARATIVE ribbon.
/// The single-click path routes through the command registry (FormatPainterBtn_Click), but the
/// double-click <c>PreviewMouseLeftButtonDown</c> handler — which arms persistent painter mode — is not
/// part of the command model. The XAML→declarative cutover dropped its wiring, silently breaking
/// double-click persistence; the host now re-attaches it to the rendered button by command name in
/// WireRenderedFormatPainterDoubleClick. This test asserts the rendered button actually carries the
/// handler at runtime, so a future re-drop fails here rather than going unnoticed.
/// </summary>
public sealed class MainWindowRenderedFormatPainterTests
{
    private static MainWindow CreateWindow(WorkbookRef workbookRef)
    {
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance);
    }

    private static WorkbookRef CreateWorkbookRef()
    {
        var initialWorkbook = new Workbook("Book1");
        initialWorkbook.AddSheet("Sheet1");
        return new WorkbookRef { Current = initialWorkbook };
    }

    [Fact]
    public void RenderedFormatPainterButton_HasPersistentDoubleClickHandlerWired()
    {
        StaTestRunner.Run(() =>
        {
            var workbookRef = CreateWorkbookRef();
            var window = CreateWindow(workbookRef);
            try
            {
                window.Show();
                PumpDispatcher();

                var button = window.FindRenderedRibbonCommandControlForTest("Format Painter") as ButtonBase;
                button.Should().NotBeNull("the declarative ribbon should render a 'Format Painter' button");

                HasPreviewMouseLeftButtonDownHandler(button!, "FormatPainterBtn_PreviewMouseLeftButtonDown")
                    .Should().BeTrue(
                        "double-click persistence relies on the PreviewMouseLeftButtonDown handler being " +
                        "attached to the rendered button (it is not wired through the command registry)");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    /// <summary>
    /// Returns true if <paramref name="element"/> has a <see cref="UIElement.PreviewMouseLeftButtonDownEvent"/>
    /// handler whose method name matches <paramref name="methodName"/>. Reads the internal
    /// <c>EventHandlersStore</c> by reflection because WPF exposes no public way to enumerate routed-event
    /// subscribers.
    /// </summary>
    private static bool HasPreviewMouseLeftButtonDownHandler(UIElement element, string methodName)
    {
        var storeProp = typeof(UIElement).GetProperty(
            "EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic);
        var store = storeProp?.GetValue(element);
        if (store is null)
            return false;

        var getHandlers = store.GetType().GetMethod(
            "GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public);
        if (getHandlers?.Invoke(store, new object[] { UIElement.PreviewMouseLeftButtonDownEvent })
            is not Array handlers)
            return false;

        foreach (var info in handlers)
        {
            var handlerProp = info.GetType().GetProperty(
                "Handler", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (handlerProp?.GetValue(info) is Delegate handler &&
                string.Equals(handler.Method.Name, methodName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
