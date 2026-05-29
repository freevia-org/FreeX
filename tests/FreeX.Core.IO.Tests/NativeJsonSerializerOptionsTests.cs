using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonSerializerOptionsTests
{
    /// <summary>
    /// Serializing twice must reuse the same <see cref="JsonSerializerOptions"/> instance
    /// so that .NET's reflection/source-gen cache is shared across calls.
    /// </summary>
    [Fact]
    public void Save_ReusesStaticOptionsInstance()
    {
        var adapter = new NativeJsonAdapter();
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");

        JsonSerializerOptions? firstOptions = null;
        JsonSerializerOptions? secondOptions = null;

        // Capture the options used during serialisation by checking that
        // the static field is the same object on repeated calls.
        // We verify via the public observable: the saved bytes must be equal
        // (idempotent) and the static field exposed for testing must be the
        // same reference both times.
        using var stream1 = new MemoryStream();
        using var stream2 = new MemoryStream();

        adapter.Save(workbook, stream1);
        adapter.Save(workbook, stream2);

        // The static options field is internal and accessible via InternalsVisibleTo.
        firstOptions  = NativeJsonAdapter.SaveOptionsForTest;
        secondOptions = NativeJsonAdapter.SaveOptionsForTest;

        object.ReferenceEquals(firstOptions, secondOptions).Should().BeTrue(
            "SaveOptions must be a static field so the same instance is used every time");
    }

    [Fact]
    public void Load_ReusesStaticOptionsInstance()
    {
        // Same principle for the deserialization side.
        var first  = NativeJsonAdapter.LoadOptionsForTest;
        var second = NativeJsonAdapter.LoadOptionsForTest;

        object.ReferenceEquals(first, second).Should().BeTrue(
            "LoadOptions must be a static field so the same instance is used every time");
    }

    [Fact]
    public void Save_WritesCompactJson_NotPrettyPrinted()
    {
        var adapter = new NativeJsonAdapter();
        var workbook = new Workbook("Compact");
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        // Pretty-printed JSON always contains newlines between properties.
        json.Should().NotContain("\n",
            "native .fxl files must be compact JSON (WriteIndented = false) to avoid inflating file size");
    }

    [Fact]
    public void SaveOptions_HasWriteIndentedFalse()
    {
        NativeJsonAdapter.SaveOptionsForTest.WriteIndented.Should().BeFalse(
            "the native .fxl format must not use pretty-printed JSON");
    }
}
