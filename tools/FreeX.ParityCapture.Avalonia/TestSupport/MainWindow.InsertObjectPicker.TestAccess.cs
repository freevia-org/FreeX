using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal Func<AvaloniaFilePickerOpenRequest, Task<InsertObjectPickedFile?>>? InsertObjectFilePickerForTest { get; set; }

    partial void ResolveInsertObjectFilePicker(
        ref Func<AvaloniaFilePickerOpenRequest, Task<InsertObjectPickedFile?>>? picker) =>
        picker = InsertObjectFilePickerForTest;

    internal Task InsertPictureFromFileForTestAsync() => InsertPictureFromFileAsync();

    internal Task ShowInsertObjectForTestAsync() => ShowInsertObjectDialogAsync();
}
