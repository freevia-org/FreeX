namespace FreeX.Core.IO;

/// <summary>
/// Result of saving an XLSX file, containing any non-fatal warnings collected during the save
/// (e.g. individual named ranges or data-validation rules that could not be serialized).
/// The file is always written; warnings indicate partial data loss.
/// </summary>
/// <param name="Warnings">
/// Diagnostic messages for feature-save failures that were recovered from.
/// Empty when the file saved without any issues. Non-empty indicates data that could
/// not be persisted (e.g. named ranges, data validation, merged regions).
/// </param>
public sealed record XlsxSaveResult(IReadOnlyList<string> Warnings)
{
    /// <summary>Returns <c>true</c> if any warnings were collected during saving.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>A save result with no warnings.</summary>
    public static XlsxSaveResult Clean { get; } = new([]);
}
