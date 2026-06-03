using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class OpenWorkbookLoaderTests
{
    private sealed class FakeAdapter(Func<Stream, Workbook> load) : IFileAdapter
    {
        public string Extension => ".fxjson";
        public string FormatName => "Fake";
        public Workbook Load(Stream stream) => load(stream);
        public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
