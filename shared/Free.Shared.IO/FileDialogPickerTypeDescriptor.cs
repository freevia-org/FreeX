namespace Free.Shared.IO;

public sealed record FileDialogPickerTypeDescriptor(
    string DisplayName,
    IReadOnlyList<string> Patterns);
