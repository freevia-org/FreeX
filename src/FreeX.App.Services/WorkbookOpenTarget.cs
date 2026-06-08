using FreeX.Core.IO;

namespace FreeX.App.Services;

public sealed record WorkbookOpenTarget(
    string Path,
    IFileAdapter Adapter,
    string Extension,
    FileFormatDescriptor Format,
    WorkbookFileAccessIdentity? FileAccessIdentity = null);
