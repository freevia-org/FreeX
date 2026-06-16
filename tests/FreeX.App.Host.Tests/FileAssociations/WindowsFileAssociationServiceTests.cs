using FluentAssertions;
using FreeX.App.Host.FileAssociations;
using FreeX.App.Services.FileAssociations;
using Microsoft.Win32;
using Xunit;

namespace FreeX.App.Host.Tests.FileAssociations;

[Collection("registry")] // serialize: these tests mutate a shared test hive
public class WindowsFileAssociationServiceTests : IDisposable
{
    private const string TestRoot = @"Software\FreeXTest\Classes";

    private static WindowsFileAssociationService NewService() =>
        new(classesRootPath: TestRoot, logger: null);

    [Fact]
    public void RegisterAll_OwnsFxl_AsDefaultHandler()
    {
        NewService().RegisterAll(@"C:\Apps\FreeX\FreeX.App.Host.exe");

        using var ext = Registry.CurrentUser.OpenSubKey($@"{TestRoot}\.fxl");
        ext!.GetValue(null).Should().Be("FreeX.Workbook.fxl");

        using var cmd = Registry.CurrentUser.OpenSubKey($@"{TestRoot}\FreeX.Workbook.fxl\shell\open\command");
        ((string)cmd!.GetValue(null)!).Should().Contain("FreeX.App.Host.exe").And.Contain("\"%1\"");
    }

    [Fact]
    public void RegisterAll_NeutralType_AddsOpenWith_DoesNotStealDefault()
    {
        // Simulate an existing default handler for .csv.
        using (var pre = Registry.CurrentUser.CreateSubKey($@"{TestRoot}\.csv"))
            pre.SetValue(null, "Excel.CSV");

        NewService().RegisterAll(@"C:\Apps\FreeX\FreeX.App.Host.exe");

        using var ext = Registry.CurrentUser.OpenSubKey($@"{TestRoot}\.csv");
        ext!.GetValue(null).Should().Be("Excel.CSV", "the existing default must be preserved");

        using var openWith = Registry.CurrentUser.OpenSubKey($@"{TestRoot}\.csv\OpenWithProgids");
        openWith!.GetValueNames().Should().Contain("FreeX.Workbook.csv");
    }

    [Fact]
    public void UnregisterAll_RemovesEveryFreeXProgId()
    {
        var svc = NewService();
        svc.RegisterAll(@"C:\Apps\FreeX\FreeX.App.Host.exe");
        svc.UnregisterAll();

        foreach (var def in FileAssociationDefinition.All)
        {
            using var progId = Registry.CurrentUser.OpenSubKey($@"{TestRoot}\{def.ProgId}");
            progId.Should().BeNull($"{def.ProgId} should be removed on uninstall");
        }
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\FreeXTest", throwOnMissingSubKey: false);
}
