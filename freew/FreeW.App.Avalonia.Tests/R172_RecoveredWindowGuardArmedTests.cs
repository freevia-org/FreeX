using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using FreeW.App.Avalonia;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r172 remediation. The round-172 recovery fix routed FreeW's recover-into-the-CURRENT-window
/// path through the guarded OpenSnapshotAsync, but the scope audit found the sibling gesture --
/// recover into a NEW window, which every accepted candidate beyond the first uses, i.e. any
/// multi-document crash -- still read the snapshot with DocxReader and called MarkDirtyWithPath
/// directly. The freshly constructed window's guard baseline was therefore null while its
/// CurrentPath was the real original file, so the first save in a recovered window skipped the
/// changed-on-disk check and could overwrite a copy edited while the app was gone.
///
/// The factory now performs the guarded open. What the guard then DOES (prompt, and refuse when
/// declined) is already pinned behaviourally by R172_RecoveryExternalWriteGuardTests; that path
/// injects its own confirm callback. This window is constructed by production code and routes its
/// confirm through the real message service, which a headless test cannot answer -- so what is
/// asserted here is the post-condition that separates the two implementations: the recovered
/// window's baseline is armed to the original file's write time, not left null.
/// </summary>
public sealed class R172_RecoveredWindowGuardArmedTests
{
    [Fact]
    public async Task Recovered_new_window_arms_the_external_modification_guard_from_the_original_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var originalPath = Path.Combine(dir, "Report.docx");
            DocxWriter.Write(TextDocument.CreateEmpty(), originalPath);
            var originalWriteTimeUtc = DateTime.UtcNow.AddMinutes(-5);
            File.SetLastWriteTimeUtc(originalPath, originalWriteTimeUtc);

            var snapshotPath = Path.Combine(dir, "snapshot.docx");
            DocxWriter.Write(TextDocument.CreateEmpty(), snapshotPath);

            DateTime? baseline = null;
            string? currentPath = null;

            var ran = await HeadlessUiThread.RunAsync(async () =>
            {
                var window = await MainWindow.CreateRecoveredSnapshotWindowAsync(snapshotPath, originalPath);
                baseline = window.DocumentFileWorkflowForTests.CurrentFileSourceLastWriteTimeUtcForTests;
                currentPath = window.DocumentFileWorkflowForTests.CurrentPathForTests;
            });

            ran.Should().BeTrue("the headless drawing backend is required to construct a MainWindow");

            currentPath.Should().Be(
                originalPath,
                "recovery adopts the original file as the save target, which is exactly why the guard matters");
            baseline.Should().NotBeNull(
                "a null baseline is what made the first save after recovery skip the changed-on-disk check");
            baseline!.Value.Should().BeCloseTo(
                File.GetLastWriteTimeUtc(originalPath),
                TimeSpan.FromSeconds(1),
                "the baseline must come from the ORIGINAL file, not the snapshot -- comparing against the " +
                "snapshot would make every ordinary recover-then-save look like an external modification");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
