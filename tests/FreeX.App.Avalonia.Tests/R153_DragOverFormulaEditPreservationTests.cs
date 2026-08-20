using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R153-shared-drag-drop-F1: MainWindow_DragOver (fired continuously while an OS drag merely
/// hovers over the window, before any drop occurs) used to route through
/// TrySelectOpenableLocalWorkbookPath, which unconditionally called TryCommitPendingFormulaEdit
/// as its very first step. That force-committed (if the pending text happened to be a syntactically
/// valid formula) or force-cancelled/error'd (if it did not) an in-progress formula-bar edit purely
/// because a drag passed over the window -- the user never had to release the mouse. These tests
/// drive the real MainWindow_DragOver handler (the exact method named in the finding) with a
/// synthetic file-carrying DragEventArgs while a formula edit is pending, and assert the edit
/// survives untouched. The sibling test asserts the adjacent, still-correct case: an actual Drop
/// legitimately must still finish/commit the pending edit before proceeding.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R153_DragOverFormulaEditPreservationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DragOverHoveringAFile_DoesNotCommitAPendingValidFormulaEdit()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            var tempFile = CreateTempFile();
            try
            {
                var sheet = window.Session.ActiveSheet;
                var address = new CellAddress(sheet.Id, 3, 3);

                // A syntactically COMPLETE formula: under the old behaviour this is exactly the
                // case that gets silently committed to the cell mid-drag, tearing down the editor.
                window.BeginFormulaEditForTest(address, "=1+1");
                window.Session.FormulaEditAddress.Should().Be(address);

                var dragArgs = await CreateFileDragEventArgsAsync(window, tempFile);

                InvokeVoid(window, "MainWindow_DragOver", null, dragArgs);

                window.Session.FormulaEditAddress.Should().Be(address,
                    "a DragOver hover must not force-commit the pending formula edit");
                GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox").Text.Should().Be("=1+1");
                (sheet.GetCell(address)?.HasFormula ?? false).Should().BeFalse(
                    "the drag hover must not have written the formula into the cell");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
                DeleteTempFile(tempFile);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DragOverHoveringAFile_DoesNotCancelAPendingInvalidFormulaEditOrShowAnError()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            var tempFile = CreateTempFile();
            try
            {
                var sheet = window.Session.ActiveSheet;
                var address = new CellAddress(sheet.Id, 4, 4);

                // An INCOMPLETE formula (unclosed paren): under the old behaviour
                // TryCommitPendingFormulaEdit -> CommitFormulaBox fails validation and calls
                // ShowEditIssue, surfacing an edit-failed error purely from the drag hovering.
                window.BeginFormulaEditForTest(address, "=SUM(A1:A2");
                window.Session.FormulaEditAddress.Should().Be(address);
                var statusBefore = GetStatusText(window);

                var dragArgs = await CreateFileDragEventArgsAsync(window, tempFile);

                InvokeVoid(window, "MainWindow_DragOver", null, dragArgs);

                window.Session.FormulaEditAddress.Should().Be(address,
                    "a DragOver hover must not force-cancel the pending formula edit");
                GetField<global::Avalonia.Controls.TextBox>(window, "_formulaBox").Text.Should().Be("=SUM(A1:A2");
                GetStatusText(window).Should().Be(statusBefore,
                    "hovering a drag must not surface an edit-failed status purely from the pointer passing over the window");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
                DeleteTempFile(tempFile);
            }
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/no-regression case (R11e/R10): the actual Drop path must still behave exactly as
    /// before -- it legitimately commits (or blocks on) a pending formula edit before an open can
    /// proceed, because a real Drop is a genuine user-completed gesture, unlike a DragOver hover.
    /// </summary>
    [Fact]
    public async Task ActualDropStillCommitsAPendingValidFormulaEditBeforeOpening()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            var tempFile = CreateTempFile();
            try
            {
                var sheet = window.Session.ActiveSheet;
                var address = new CellAddress(sheet.Id, 5, 5);

                window.BeginFormulaEditForTest(address, "=2+2");
                window.Session.FormulaEditAddress.Should().Be(address);

                var dragArgs = await CreateFileDragEventArgsAsync(window, tempFile);

                // Matches MainWindow_Drop's exact call shape (4 out params: path, storageItem,
                // message) -- see the pinned call at MainWindow.cs's Drop handler.
                InvokeVoid(window, "TrySelectDroppedWorkbookPath", dragArgs, null, null, null);

                window.Session.FormulaEditAddress.Should().BeNull(
                    "an actual Drop must still finish the pending formula edit before an open can proceed");
                sheet.GetCell(address)!.HasFormula.Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
                DeleteTempFile(tempFile);
            }
            return true;
        }, CancellationToken.None);
    }

    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"r153-dragdrop-{Guid.NewGuid():N}.fxl");
        File.WriteAllText(path, "placeholder");
        return path;
    }

    private static void DeleteTempFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static async Task<DragEventArgs> CreateFileDragEventArgsAsync(MainWindow window, string localPath)
    {
        var storageFile = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(localPath))
            ?? throw new InvalidOperationException("Headless StorageProvider could not resolve the temp file.");
        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.CreateFile(storageFile));
        return new DragEventArgs(
            DragDrop.DragOverEvent,
            dataTransfer,
            window,
            new Point(10, 10),
            KeyModifiers.None);
    }

    private static string? GetStatusText(MainWindow window) =>
        GetField<global::Avalonia.Controls.TextBlock>(window, "_statusText").Text;

    private static T GetField<T>(MainWindow window, string name) where T : class =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) as T
        ?? throw new InvalidOperationException($"Missing field {name}.");

    private static void InvokeVoid(MainWindow window, string name, params object?[] args)
    {
        var method = typeof(MainWindow)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate =>
            {
                if (candidate.Name != name)
                    return false;

                var parameters = candidate.GetParameters();
                return parameters.Length == args.Length;
            });
        method.Invoke(window, args);
    }
}
