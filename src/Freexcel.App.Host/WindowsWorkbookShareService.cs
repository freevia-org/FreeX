using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using WinRT;

namespace Freexcel.App.Host;

public interface IWorkbookShareService
{
    Task ShareFileAsync(Window owner, string filePath, string workbookName);
}

public sealed class WindowsWorkbookShareService : IWorkbookShareService
{
    private static readonly Guid DataTransferManagerIid = new(
        0xa5caee9b,
        0x8708,
        0x49d1,
        0x8d,
        0x36,
        0x67,
        0xd2,
        0x5a,
        0x8d,
        0xa0,
        0x0c);

    public Task ShareFileAsync(Window owner, string filePath, string workbookName)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A file path is required for Windows Share.", nameof(filePath));

        var windowHandle = new WindowInteropHelper(owner).Handle;
        if (windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("The workbook window is not ready for sharing.");

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = DataTransferManagerIid;
        var managerPointer = interop.GetForWindow(windowHandle, ref iid);
        var manager = MarshalInterface<DataTransferManager>.FromAbi(managerPointer);

        TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
        handler = async (_, args) =>
        {
            if (handler is not null)
                manager.DataRequested -= handler;

            var request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                request.Data.Properties.Title = string.IsNullOrWhiteSpace(workbookName)
                    ? Path.GetFileName(filePath)
                    : workbookName;
                request.Data.Properties.Description = "Freexcel workbook";
                request.Data.SetStorageItems([storageFile]);
                request.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            catch
            {
                request.FailWithDisplayText("Freexcel could not prepare this workbook for Windows Share.");
            }
            finally
            {
                deferral.Complete();
            }
        };
        manager.DataRequested += handler;

        interop.ShowShareUIForWindow(windowHandle);
        return Task.CompletedTask;
    }

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow([In] IntPtr appWindow);
    }
}
