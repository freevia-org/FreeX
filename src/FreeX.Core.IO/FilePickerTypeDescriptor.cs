namespace FreeX.Core.IO;

public sealed record FilePickerTypeDescriptor(
    string DisplayName,
    IReadOnlyList<string> Patterns);
