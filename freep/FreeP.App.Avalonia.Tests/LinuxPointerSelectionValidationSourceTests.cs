namespace FreeP.App.Avalonia.Tests;

public sealed class LinuxPointerSelectionValidationSourceTests
{
    [Fact]
    public void PointerSelectionProbeUsesItsActualFixtureShapeAndSkipsMutationCommands()
    {
        var probe = File.ReadAllText(RepoFile(
                "tools/LinuxInteractiveDocker/run-freep-rich-text-shortcut-probe.sh"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        probe.Should().Contain("expected_run_count = 1 if pointer_geometry else 2");

        var pointerDragStart = probe.IndexOf("pointer_drag() {", StringComparison.Ordinal);
        var pointerDragEnd = probe.IndexOf("capture_window_state() {", pointerDragStart, StringComparison.Ordinal);
        var pointerDrag = probe[pointerDragStart..pointerDragEnd];
        pointerDrag.Should().Contain("xdotool mousemove \"$start_x\" \"$start_y\"");
        pointerDrag.Should().NotContain("xdotool mousemove --sync \"$start_x\" \"$start_y\"");

        var readOnlyStart = probe.IndexOf(
            "if [[ \"$app_surface\" == \"in-canvas-grouped-child-pointer-selection\" ]]; then\n" +
            "    final_hash=",
            StringComparison.Ordinal);
        var saveLaneStart = probe.IndexOf("save_predicate=assert_soft_break_inspection", StringComparison.Ordinal);
        readOnlyStart.Should().BeGreaterThanOrEqualTo(0);
        saveLaneStart.Should().BeGreaterThan(readOnlyStart);
        var readOnlyLane = probe[readOnlyStart..saveLaneStart];
        readOnlyLane.Should().Contain("ctrl-s-sent=false");
        readOnlyLane.Should().Contain("ctrl-z-sent=false");
        readOnlyLane.Should().Contain("ctrl-shift-z-sent=false");
        readOnlyLane.Should().Contain("fixture-mounted-after.sha256.txt");
        readOnlyLane.Should().Contain("exit 0");
        readOnlyLane.Should().NotContain("send_owner_key ctrl+z");
        readOnlyLane.Should().NotContain("save_checkpoint");
    }

    private static string RepoFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
}
