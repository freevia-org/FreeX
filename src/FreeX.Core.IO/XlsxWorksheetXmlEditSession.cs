using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal sealed class XlsxWorksheetXmlEditSession : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly XlsxWorkbookWorksheetPathMap _worksheetPathMap;
    private readonly Dictionary<string, XDocument> _documents = new(StringComparer.Ordinal);
    private readonly List<string> _dirtyPaths = [];
    private readonly HashSet<string> _dirtyPathSet = new(StringComparer.Ordinal);
    private bool _disposed;

    public XlsxWorksheetXmlEditSession(Stream xlsxStream, XlsxWorkbookWorksheetPathMap worksheetPathMap)
    {
        _archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        _worksheetPathMap = worksheetPathMap;
    }

    // R89-io-autofilter-color-dxf-1-1: exposes the underlying archive so a writer that needs to touch
    // xl/styles.xml (e.g. XlsxAutoFilterColorFilterDxfWriter, allocating a dxf for a colour filter)
    // can do so within the same package-edit pass as the worksheet-level writers this session serves,
    // instead of opening a second independent ZipArchive over the same stream.
    public ZipArchive Archive => _archive;

    public bool TryGetWorksheet(Sheet sheet, out XlsxWorksheetXmlEdit edit)
    {
        edit = default;
        if (!_worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
            return false;

        if (!_documents.TryGetValue(worksheetPath, out var worksheetXml))
        {
            var worksheetEntry = _archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                return false;

            worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            _documents[worksheetPath] = worksheetXml;
        }

        var root = worksheetXml.Root;
        if (root is null)
            return false;

        edit = new XlsxWorksheetXmlEdit(worksheetPath, root);
        return true;
    }

    public void MarkDirty(XlsxWorksheetXmlEdit edit)
    {
        if (_dirtyPathSet.Add(edit.Path))
            _dirtyPaths.Add(edit.Path);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            foreach (var path in _dirtyPaths)
                XlsxPackageXmlEditor.ReplaceXml(_archive, path, _documents[path]);
        }
        finally
        {
            _archive.Dispose();
            _disposed = true;
        }
    }
}

internal readonly record struct XlsxWorksheetXmlEdit(string Path, XElement Root);
