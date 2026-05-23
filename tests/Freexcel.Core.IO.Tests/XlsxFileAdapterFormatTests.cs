using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxFileAdapterFormatTests
{
    [Fact]
    public void Formats_IncludeModernExcelOpenVariants()
    {
        var adapter = new XlsxFileAdapter();

        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlsx" &&
            format.CanOpen &&
            format.CanSave &&
            !format.OpensAsTemplate);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlsm" &&
            format.CanOpen &&
            !format.CanSave &&
            !format.OpensAsTemplate);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xltx" &&
            format.CanOpen &&
            !format.CanSave &&
            format.OpensAsTemplate);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xltm" &&
            format.CanOpen &&
            !format.CanSave &&
            format.OpensAsTemplate);
    }
}
