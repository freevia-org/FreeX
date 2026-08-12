using System.Reflection;
using FreeW.App.Avalonia.Pdf;

namespace FreeW.App.Avalonia.Tests;

public sealed class PdfExportOwnershipTests
{
    [Fact]
    public void Pdf_export_exposes_only_stream_owned_save_endpoints()
    {
        var saveMethods = typeof(FreeWAvaloniaPdfExport)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == nameof(FreeWAvaloniaPdfExport.Save))
            .ToArray();

        saveMethods.Should().HaveCount(2);
        saveMethods.Should().OnlyContain(method =>
            method.GetParameters().Length >= 2 &&
            method.GetParameters()[1].ParameterType == typeof(Stream));
    }
}
