using System.Text.Json;

namespace FreeP.App.Avalonia.Tests;

public sealed class PortablePrinterPhysicalValidationSourceTests
{
    [Fact]
    public void Portable_printer_schema_pins_the_nine_physical_gates_and_submission_contract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile(
            "tools/LinuxInteractiveDocker/freep-portable-printer-wave105-validation.schema.json")));
        var root = document.RootElement;

        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        root.GetProperty("properties").GetProperty("suite").GetProperty("const").GetString()
            .Should().Be("freep-portable-printer-wave105-physical");
        root.GetProperty("properties").GetProperty("fakePrinter").GetProperty("properties")
            .GetProperty("privatePath").GetProperty("const").GetString()
            .Should().Be("/tmp/freex-cups-dry-run");
        root.GetProperty("properties").GetProperty("summary").GetProperty("properties")
            .GetProperty("total").GetProperty("const").GetInt32().Should().Be(9);
        root.GetProperty("properties").GetProperty("screenshots").GetProperty("minItems")
            .GetInt32().Should().Be(5);
        root.GetProperty("properties").GetProperty("submission").GetProperty("properties")
            .GetProperty("queue").GetProperty("const").GetString().Should().Be("FreeP-Secondary");
    }

    [Fact]
    public void Portable_printer_probe_is_physical_and_records_real_fake_submission_evidence()
    {
        var probe = File.ReadAllText(RepoFile(
            "tools/LinuxInteractiveDocker/run-freep-portable-printer-probe.sh"));

        probe.Should().Contain("run_key Alt_L");
        probe.Should().Contain("run_key F");
        probe.Should().Contain("click_at 70 343");
        probe.Should().Contain("wait_window '^Print$'");
        probe.Should().Contain("xdotool getwindowpid");
        probe.Should().Contain("owner-pid=$owner_pid dialog-pid=$dialog_pid");
        probe.Should().Contain("scrot -o");
        probe.Should().Contain("FreeP-Secondary");
        probe.Should().Contain("last-invocation.json");
        probe.Should().Contain("last-submitted.pdf");
        probe.Should().Contain("orientation-requested=4");
        probe.Should().Contain("xdotool click --repeat 30 --delay 40 5");
        probe.Should().Contain("physical-x11-portable-printer");
        probe.Should().NotContain("ExecutePrintForTests");
        probe.Should().NotContain("SetFilePickerOverridesForTests");
        probe.Should().NotContain("callback");
    }

    [Fact]
    public void Portable_printer_fakes_expose_two_queues_default_and_argument_capture()
    {
        var lpstat = File.ReadAllText(RepoFile(
            "tools/LinuxInteractiveDocker/freep-portable-printer-fake-lpstat.sh"));
        var lp = File.ReadAllText(RepoFile(
            "tools/LinuxInteractiveDocker/freep-portable-printer-fake-lp.sh"));

        lpstat.Should().Contain("FreeP-Default");
        lpstat.Should().Contain("FreeP-Secondary");
        lpstat.Should().Contain("system default destination: FreeP-Default");
        lpstat.Should().Contain("lpstat-calls.txt");
        lp.Should().Contain("last-invocation.json");
        lp.Should().Contain("last-submitted.pdf");
        lp.Should().Contain("cp -- \"$pdf_path\"");
        lp.Should().Contain("request id is FreeP-Secondary");
    }

    [Fact]
    public void Portable_printer_runner_uses_private_fake_path_strict_gates_and_owned_cleanup()
    {
        var runner = File.ReadAllText(RepoFile("tools/Run-FreePPortablePrinterValidation.ps1"));

        runner.Should().Contain("Run-LinuxInteractiveDocker.ps1");
        runner.Should().Contain("-CupsDryRun");
        runner.Should().Contain("FREEP_PORTABLE_PRINTER_OUTPUT=/work/portable-printer");
        runner.Should().Contain("docker");
        runner.Should().Contain("/tmp/freex-cups-dry-run/lpstat");
        runner.Should().Contain("/tmp/freex-cups-dry-run/lp");
        runner.Should().Contain("freex-linux-interactive-freep-$Port");
        runner.Should().Contain("Assert-ManifestContract");
        runner.Should().Contain("-Action", "Stop");
        runner.Should().Contain("freep-portable-printer-wave105-validation.schema.json");
        runner.Should().NotContain("FreeX");
        runner.Should().NotContain("FreeW");
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", relativePath);
}
