using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal RendererValidationAccess CreateRendererValidationAccess() => new(this);

    internal sealed class RendererValidationAccess
    {
        private readonly MainWindow _owner;

        internal RendererValidationAccess(MainWindow owner) => _owner = owner;

        internal void StartWhenOpened(Func<Task> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ((Window)_owner).Opened += async (_, _) => await operation();
        }

        internal RendererShellObservation ObserveShell()
        {
            var externalImages = _owner._session.ActiveSheet.Pictures
                .Where(static picture =>
                    picture.Kind == PictureKind.Image &&
                    string.Equals(picture.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) &&
                    picture.ImageBytes is { Length: > 0 })
                .ToArray();

            return new RendererShellObservation(
                WindowShown: _owner.IsVisible,
                WindowTitle: _owner.Title ?? string.Empty,
                DisplayName: _owner._session.DisplayName,
                ActiveSheetName: _owner._session.ActiveSheet.Name,
                SheetTabCount: _owner._session.SheetTabs.Count,
                ViewportRowCount: _owner._session.Viewport.RowMetrics.Count,
                ViewportColumnCount: _owner._session.Viewport.ColMetrics.Count,
                ExternalImageClipboardPictureCount: externalImages.Length,
                ExternalImageClipboardPicturePngByteCount: externalImages.Sum(static picture => picture.ImageBytes!.Length),
                OpenedSourcePath: _owner._session.CurrentFilePath,
                IsOpening: _owner._isOpening);
        }

        internal T GetControl<T>(string fieldName)
            where T : Control =>
            GetFieldValue(fieldName) as T ??
            throw new InvalidOperationException($"Renderer field '{fieldName}' is not a {typeof(T).Name}.");

        internal NativeMenuItem GetNativeMenuItem(string fieldName) =>
            GetFieldValue(fieldName) as NativeMenuItem ??
            throw new InvalidOperationException($"Renderer field '{fieldName}' is not a native menu item.");

        internal NativeMenu? NativeMenu => _owner._nativeMenu;

        internal NativeMenu? NativeDockMenu =>
            global::Avalonia.Application.Current is { } app ? NativeDock.GetMenu(app) : null;

        internal bool HasSheetTab(Func<Button, bool> predicate) => _owner.HasSheetTabButton(predicate);

        internal Button? ActiveSheetTab => _owner.FindSheetTabButton(_owner._session.ActiveSheet.Id);

        internal IReadOnlyList<Control> ToolbarFocusTargets => _owner.GetToolbarFocusTargets();

        internal bool HasSheetTabContextMenuItem(string header) =>
            _owner.HasSheetTabContextMenuItem(header);

        internal bool HasSheetTabContextSubmenuItem(string header, string childHeader) =>
            _owner.HasSheetTabContextSubmenuItem(header, childHeader);

        internal async Task<bool> InspectFindDialogAsync(Action<FindDialogInspection> inspect) =>
            await _owner.ShowFindInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectReplaceDialogAsync(Action<ReplaceDialogInspection> inspect) =>
            await _owner.ShowReplaceInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectGoToDialogAsync(Action<GoToDialogInspection> inspect) =>
            await _owner.ShowGoToInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectGoToSpecialDialogAsync(Action<GoToSpecialDialogInspection> inspect) =>
            await _owner.ShowGoToSpecialInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectFormatCellsDialogAsync(Action<FormatCellsDialogInspection> inspect) =>
            await _owner.ShowFormatCellsInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectSortDialogAsync(Action<SortDialogInspection> inspect) =>
            await _owner.ShowSortInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectDataValidationDialogAsync(Action<DataValidationDialogInspection> inspect) =>
            await _owner.ShowDataValidationInputDialogAsync(inspect) is null;

        internal async Task<bool> InspectConditionalFormatRuleDialogAsync(
            Action<ConditionalFormatRuleDialogInspection> inspect) =>
            await _owner.ShowConditionalFormatRuleEditorAsync(existingRule: null, inspect) is null;

        internal Task InspectManageConditionalFormatsDialogAsync(
            Action<ManageConditionalFormatsDialogInspection> inspect) =>
            _owner.ShowManageConditionalFormatsDialogAsync(inspect);

        internal ComboBox CreateDataValidationDropdown(
            DataValidationDropdownPlan plan,
            double width,
            double height) =>
            _owner.CreateDataValidationDropdown(plan, width, height);

        internal Task<bool> TryPasteExternalClipboardImageAsync() =>
            _owner.TryPasteExternalClipboardImageAsync();

        internal RendererFormattingState BeginCommandObservation(Action<RendererCommandObservation> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            _owner.FocusShellRegion(ShellFocusTarget.Worksheet);
            _owner._rendererCommandObserver = observer;
            return new RendererFormattingState(
                _owner._session.IsSelectedRangeStartBold,
                _owner._session.IsSelectedRangeStartItalic,
                _owner._session.IsSelectedRangeStartUnderline);
        }

        internal bool HasNativeMenuItemGesture(
            string fieldName,
            Key expectedKey,
            KeyModifiers expectedModifiers) =>
            typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(_owner) is NativeMenuItem { Gesture: { } gesture } &&
            gesture.Key == expectedKey &&
            gesture.KeyModifiers == expectedModifiers;

        internal static bool HasMethods(params string[] methodNames) =>
            methodNames.All(methodName =>
                typeof(MainWindow).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic) is not null);

        private object? GetFieldValue(string fieldName) =>
            typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(_owner);
    }
}

internal sealed record RendererShellObservation(
    bool WindowShown,
    string WindowTitle,
    string DisplayName,
    string ActiveSheetName,
    int SheetTabCount,
    int ViewportRowCount,
    int ViewportColumnCount,
    int ExternalImageClipboardPictureCount,
    int ExternalImageClipboardPicturePngByteCount,
    string? OpenedSourcePath,
    bool IsOpening);

internal enum RendererObservedCommand
{
    SelectAll,
    Bold,
    Italic,
    Underline
}

internal readonly record struct RendererCommandObservation(
    RendererObservedCommand Command,
    bool Before,
    bool After);

internal readonly record struct RendererFormattingState(
    bool Bold,
    bool Italic,
    bool Underline);
