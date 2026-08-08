using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R86-services-file-format-detect-5-1: <c>MainWindow.OpenWorkbookFromTargetAsync</c>'s catch
/// clause only handled an allow-list of exception types (<c>IOException</c>,
/// <c>InvalidDataException</c>, <c>NotSupportedException</c>, <c>UnauthorizedAccessException</c>,
/// <c>WorkbookTooLargeException</c>). <c>WorkbookPasswordProtectedException</c> (thrown by
/// <c>XlsxFileAdapter.ThrowIfPasswordEncrypted</c> for a real "Encrypt with Password" .xlsx) is a
/// plain <see cref="System.Exception"/> subtype, not any of those, so it escaped that catch clause
/// entirely and propagated out of the <c>async void</c> Open button/menu-item click handlers --
/// fatal under .NET/Avalonia, crashing the whole app instead of showing "Open failed: ...". The
/// fix broadens the catch to a plain <c>catch (Exception ex)</c>, matching the WPF host's
/// equivalent open path (<c>MainWindow.Backstage.cs</c>).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R86_OpenWorkbookPasswordProtectedGuardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // Real "Encrypt with Password" .xlsx files are OLE/CFB compound files whose payload is an
    // EncryptedPackage stream. A bare signature padded with zeros is not a parseable CFB structure,
    // so XlsxFileAdapter.ThrowIfPasswordEncrypted falls through to its conservative
    // WorkbookPasswordProtectedException report (mirrors XlsxFileAdapterIoRobustnessTests).
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    [Fact]
    public async Task OpenWorkbookFromTargetAsync_PasswordProtectedXlsx_DoesNotCrashAndSurfacesMessage()
    {
        await Session.Dispatch(async () =>
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"freex-r86-password-protected-test-{Guid.NewGuid():N}.xlsx");
            File.WriteAllBytes(path, [.. CompoundFileSignature, .. new byte[512]]);

            var window = new MainWindow([]);
            try
            {
                window.Session.TryResolveOpenTarget(path, out var target, out var resolveMessage)
                    .Should().BeTrue(resolveMessage);

                var openFromTargetMethod = typeof(MainWindow).GetMethod(
                    "OpenWorkbookFromTargetAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                var act = () => (Task)openFromTargetMethod.Invoke(window, [target])!;

                // The core assertion: this must complete (not throw/crash the process) even though
                // the adapter throws WorkbookPasswordProtectedException deep inside LoadAsync.
                await act.Should().NotThrowAsync();

                window.StatusTextForTest.Text.Should().Contain("Open failed",
                    "a password-protected workbook must surface a clear open-failure message on " +
                    "the status text instead of silently doing nothing or crashing");
                window.StatusTextForTest.Text.Should().ContainEquivalentOf("password",
                    "the surfaced message should carry the real reason (password-protected), not a " +
                    "generic/opaque failure");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
                File.Delete(path);
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// No-regression sibling: a workbook that opens successfully must still complete normally
    /// through the same code path (the broadened catch clause must not swallow success or leave
    /// the window in some half-opened state).
    /// </summary>
    [Fact]
    public async Task OpenWorkbookFromTargetAsync_ValidWorkbook_OpensSuccessfullyWithoutOpenIssue()
    {
        await Session.Dispatch(async () =>
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"freex-r86-valid-open-test-{Guid.NewGuid():N}.fxl");
            var sourceWindow = new MainWindow([]);
            try
            {
                using (var stream = File.Create(path))
                {
                    new NativeJsonAdapter().Save(sourceWindow.Session.Workbook, stream);
                }

                var window = new MainWindow([]);
                try
                {
                    window.Session.TryResolveOpenTarget(path, out var target, out var resolveMessage)
                        .Should().BeTrue(resolveMessage);

                    var openFromTargetMethod = typeof(MainWindow).GetMethod(
                        "OpenWorkbookFromTargetAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;

                    var act = () => (Task)openFromTargetMethod.Invoke(window, [target])!;

                    await act.Should().NotThrowAsync();

                    window.StatusTextForTest.Text.Should().NotContain("Open failed",
                        "a workbook that opens successfully must not report an open failure");
                    window.Session.CurrentFilePath.Should().NotBeNull();
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    window.Close();
                }
            }
            finally
            {
                sourceWindow.AllowCloseWithoutDirtyPromptForParityCapture();

                sourceWindow.Close();
                File.Delete(path);
            }

            return true;
        }, CancellationToken.None);
    }
}
