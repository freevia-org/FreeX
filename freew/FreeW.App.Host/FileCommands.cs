using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's File lifecycle: New / Open / Save / Save As over the docx reader+writer, with recent-files
/// tracking via the shared <see cref="RecentFilesStore"/> (which persists under FreeW's own data
/// folder because Program.Main set AppProduct = "FreeW") and a simple dirty flag.
/// </summary>
internal sealed class FileCommands(Window window, DocumentView editor, Action onChanged)
{
    private const string Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*";
    private string? _currentPath;

    public bool IsDirty { get; private set; }

    public string DisplayName => _currentPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(_currentPath);

    public void MarkDirty()
    {
        if (IsDirty)
            return;
        IsDirty = true;
        onChanged();
    }

    public void New()
    {
        editor.LoadModel(TextDocument.CreateEmpty());
        _currentPath = null;
        IsDirty = false;
        onChanged();
    }

    public void Open()
    {
        var dialog = new OpenFileDialog { Filter = Filter, DefaultExt = ".docx" };
        if (dialog.ShowDialog(window) != true)
            return;
        try
        {
            editor.LoadModel(DocxReader.Read(dialog.FileName));
            SetSaved(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(window, $"Could not open the document:\n{ex.Message}", "FreeW",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public bool Save() => _currentPath is null ? SaveAs() : SaveTo(_currentPath);

    public bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = Filter,
            DefaultExt = ".docx",
            FileName = _currentPath is null ? "Document.docx" : Path.GetFileName(_currentPath)
        };
        return dialog.ShowDialog(window) == true && SaveTo(dialog.FileName);
    }

    private bool SaveTo(string path)
    {
        try
        {
            editor.CommitToModel();
            DocxWriter.Write(editor.Model, path);
            SetSaved(path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(window, $"Could not save the document:\n{ex.Message}", "FreeW",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SetSaved(string path)
    {
        _currentPath = path;
        IsDirty = false;
        try
        {
            RecentFilesStore.Load().AddOrUpdate(path);
        }
        catch
        {
            // Recent-files tracking is best-effort; never block a save/open on it.
        }
        onChanged();
    }
}
