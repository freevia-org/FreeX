namespace Freexcel.Core.IO;

public sealed record FileFormatDescriptor(
    string Extension,
    string FormatName,
    bool CanOpen = true,
    bool CanSave = true,
    bool OpensAsTemplate = false);
