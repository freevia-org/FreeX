using System.Windows;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedWorkbookPath(e) is null
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void MainWindow_Drop(object sender, DragEventArgs e)
    {
        var path = GetDroppedWorkbookPath(e);
        e.Handled = true;
        if (path is null)
            return;

        await OpenFileAsync(path);
    }

    private string? GetDroppedWorkbookPath(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return null;

        return e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? WorkbookDropPlanner.SelectOpenableFile(paths, _fileAdapters)
            : null;
    }
}
