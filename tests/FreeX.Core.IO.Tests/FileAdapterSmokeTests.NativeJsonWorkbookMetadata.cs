using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookFileSharing()
    {
        var workbook = new Workbook("FileSharingNativeJson")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                ReadOnlyRecommended = true,
                UserName = "FreeXTest",
                ReservationPassword = "ABCD"
            }
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.FileSharing.Should().BeEquivalentTo(workbook.FileSharing);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesAuthoredWorkbookFileSharingUserName()
    {
        var workbook = new Workbook("FileSharingUserNameXlsx")
        {
            FileSharing = new WorkbookFileSharingModel
            {
                UserName = "Analyst"
            }
        };
        workbook.AddSheet("Data");

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var fileSharing = workbookXml.Root!.Element(workbookNs + "fileSharing");
        fileSharing.Should().NotBeNull();
        fileSharing!.Attribute("userName")!.Value.Should().Be("Analyst");
        fileSharing.Attribute("readOnlyRecommended").Should().BeNull();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookFileRecoveryProperties()
    {
        var workbook = new Workbook("FileRecoveryNativeJson");
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = true,
            CrashSave = true,
            NativeAttributes = new Dictionary<string, string> { ["customRecoveryFlag"] = "keep" }
        });
        workbook.FileRecoveryProperties.Add(new WorkbookFileRecoveryPropertiesModel
        {
            DataExtractLoad = true,
            RepairLoad = false
        });
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.FileRecoveryProperties.Should().BeEquivalentTo(workbook.FileRecoveryProperties);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookFileVersion()
    {
        var workbook = new Workbook("FileVersionNativeJson")
        {
            FileVersion = new WorkbookFileVersionModel
            {
                AppName = "xl",
                LastEdited = "7",
                LowestEdited = "7",
                RupBuild = "28129",
                NativeAttributes = new Dictionary<string, string> { ["customVersionFlag"] = "keep" }
            }
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.FileVersion.Should().BeEquivalentTo(workbook.FileVersion);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookProperties()
    {
        var workbook = new Workbook("WorkbookPropertiesNativeJson")
        {
            Properties = MakeBag("workbookPr",
                new Dictionary<string, string> { ["defaultThemeVersion"] = "166925" },
                ["<fx:workbookPrNativeChild xmlns:fx=\"urn:freex:test\" id=\"first\" />"])
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.Properties.Should().BeEquivalentTo(workbook.Properties);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookFunctionGroups()
    {
        var workbook = new Workbook("FunctionGroupsNativeJson")
        {
            FunctionGroups = new WorkbookFunctionGroupsModel
            {
                BuiltInGroupCount = "16",
                NativeAttributes = new Dictionary<string, string> { ["customFunctionGroupFlag"] = "keep" },
                Groups =
                [
                    new WorkbookFunctionGroupModel
                    {
                        Name = "FreeXNativeFunctions",
                        NativeAttributes = new Dictionary<string, string> { ["customGroupFlag"] = "keep" }
                    }
                ]
            }
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.FunctionGroups.Should().BeEquivalentTo(workbook.FunctionGroups);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookSmartTags()
    {
        var workbook = new Workbook("SmartTagsNativeJson")
        {
            SmartTags = new WorkbookSmartTagMetadataModel
            {
                Embed = true,
                Show = "all",
                PropertiesNativeAttributes = new Dictionary<string, string> { ["customSmartTagFlag"] = "keep" },
                TypesNativeAttributes = new Dictionary<string, string> { ["customSmartTagTypesFlag"] = "keep" },
                Types =
                [
                    new WorkbookSmartTagTypeModel
                    {
                        NamespaceUri = "urn:schemas-microsoft-com:office:smarttags",
                        Name = "place",
                        NativeAttributes = new Dictionary<string, string> { ["customSmartTagTypeFlag"] = "keep" }
                    }
                ]
            }
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.SmartTags.Should().BeEquivalentTo(workbook.SmartTags);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_AdditionalWorkbookViews()
    {
        var workbook = new Workbook("AdditionalWorkbookViewsNativeJson");
        workbook.AdditionalViews = new WorkbookAdditionalViewsModel
        {
            NativeAttributes = new Dictionary<string, string> { ["nativeBookViewsAttr"] = "kept" },
            Views =
            [
                new WorkbookAdditionalViewModel
                {
                    NativeXml = "<workbookView xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" visibility=\"hidden\" tabRatio=\"700\" customWorkbookViewFlag=\"keep\" />",
                    NativeAttributes = new Dictionary<string, string>
                    {
                        ["visibility"] = "hidden",
                        ["tabRatio"] = "700",
                        ["customWorkbookViewFlag"] = "keep"
                    }
                }
            ]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        loaded.AdditionalViews.Should().BeEquivalentTo(workbook.AdditionalViews);
    }
}
