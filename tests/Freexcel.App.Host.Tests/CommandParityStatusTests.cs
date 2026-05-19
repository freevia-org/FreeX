using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class CommandParityStatusTests
{
    [Fact]
    public void NamedCloseoutRows_AreTrackedInCommandSurfaceParityDocument()
    {
        var doc = File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_SURFACE_PARITY.md"));

        string[] rows =
        [
            "Advanced Chart Families",
            "Export to PDF/XPS",
            "Cut (Ctrl+X)",
            "Copy (Ctrl+C)",
            "Paste (Ctrl+V)",
            "Paste Special",
            "Format Painter",
            "Distributed/Justify alignment",
            "Shrink to Fit",
            "Format Cells Alignment dialog",
            "Custom Number Format",
            "Full Excel locale/accounting fidelity",
            "AutoFit Row/Column",
            "Format Cells dialog (Ctrl+1)",
            "Flash Fill"
        ];

        foreach (var row in rows)
            doc.Should().Contain(row);
    }
}
