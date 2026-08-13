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
        probe.Should().Contain("canvas_margin=40");
        probe.Should().Contain("pointer_editor_left_x=$((pointer_shape_left_x - canvas_margin))");
        probe.Should().Contain("pointer_editor_top_y=$((pointer_shape_top_y - canvas_margin))");
        probe.Should().Contain("pointer_edge_y=$((pointer_editor_bottom_y + 64))");
        probe.Should().Contain("editor-overlay-offset=%s");
        probe.Should().Contain("pointer-editor-rect=%s,%s,%s,%s");
        probe.Should().Contain("pointer_drag \"$pointer_anchor_x\" \"$pointer_anchor_y\" \"$pointer_edge_x\" \"$pointer_edge_y\"");
        probe.Should().Contain("drag-contract=first visual line to captured pointer beyond editor bottom across paragraph boundary");
        probe.Should().Contain("pointer_paragraph_readback=false");
        probe.Should().Contain("xdotool click --clearmodifiers --repeat 3 --delay 120 1");
        probe.Should().Contain("pointer-paragraph-selection");
        probe.Should().Contain("Wide words make this first paragraph wrap at unequal visual line widths\\n");
        probe.Should().Contain("$pointer_paragraph_readback");

        var semanticReadbackStart = probe.IndexOf("\"semanticReadback\": (", StringComparison.Ordinal);
        semanticReadbackStart.Should().BeGreaterThanOrEqualTo(0);
        var pointerTranscriptStart = probe.IndexOf("\"pointer-selection-forward\"", semanticReadbackStart, StringComparison.Ordinal);
        var pointerGeometryProofStart = probe.IndexOf("\"geometryProof\": \"pointer-selection-calibration.txt\"", pointerTranscriptStart, StringComparison.Ordinal);
        pointerTranscriptStart.Should().BeGreaterThan(semanticReadbackStart);
        pointerGeometryProofStart.Should().BeGreaterThan(pointerTranscriptStart);
        var pointerManifest = probe[pointerTranscriptStart..pointerGeometryProofStart];
        pointerManifest.Should().Contain(
            "\"pointer-selection-forward\",\n" +
            "                \"pointer-selection-reverse\",\n" +
            "                \"pointer-paragraph-selection\",");

        var runner = File.ReadAllText(RepoFile(
                "tools/Run-FreePRichTextShortcutValidation.ps1"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        runner.Should().Contain("[System.Security.Cryptography.SHA256]::Create()");
        runner.Should().Contain("$sha256.ComputeHash(");
        runner.Should().NotContain("[System.Security.Cryptography.SHA256]::HashData(",
            "the runner is launched by Windows PowerShell 5.1 as well as modern pwsh");
        runner.Should().Contain(
            "\"pointer-selection-forward|pointer-selection-reverse|pointer-paragraph-selection\"");
        runner.Should().Contain(
            "\"pointer-selection-forward\",\n" +
            "        \"pointer-selection-reverse\",\n" +
            "        \"pointer-paragraph-selection\"");

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
        TestWorkspaceFileLocator.Find(relativePath);
}
