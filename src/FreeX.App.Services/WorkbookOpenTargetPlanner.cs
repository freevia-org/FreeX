using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeX.Core.IO;

namespace FreeX.App.Services;

/// <summary>
/// Resolves a user- or shell-supplied workbook path into the neutral open target that hosts can load.
/// Hosts still own file pickers, dirty prompts, progress UI, and applying the loaded workbook.
/// </summary>
public static class WorkbookOpenTargetPlanner
{
    public const string LocalPathRequiredMessage = "Open requires a local file path.";
    public const string UnsupportedFileTypeMessage = "Unsupported file type.";

    // Extensions whose real format is a ZIP-based OOXML package. A file bearing one of these
    // extensions is expected to start with the ZIP "PK" local-file-header signature; anything else
    // (e.g. a CSV/plain-text file that was merely renamed to ".xlsx") is a content/extension mismatch.
    private static readonly string[] ZipPackageExtensions = [".xlsx", ".xlsm", ".xltx", ".xltm"];

    public static bool TryCreateOpenTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        out WorkbookOpenTarget? target,
        out string message) =>
        TryCreateOpenTarget(adapters, path, fileAccessIdentity: null, out target, out message);

    public static bool TryCreateOpenTarget(
        IEnumerable<IFileAdapter> adapters,
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity,
        out WorkbookOpenTarget? target,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        target = null;
        if (!LocalFilePath.TryNormalize(path, out var openPath))
        {
            message = LocalPathRequiredMessage;
            return false;
        }

        if (!FilePathPolicy.TryGetExtension(openPath, out var extension))
        {
            message = UnsupportedFileTypeMessage;
            return false;
        }

        var adapter = FileFormatResolver.FindOpenAdapter(adapters, extension, out var format);
        if (adapter is null || format is null)
        {
            message = $"Unsupported file type: {extension}.";
            return false;
        }

        // Real Excel sniffs a workbook's actual bytes rather than trusting its extension outright, but
        // it never refuses to PLAN the open over a mere content/extension mismatch -- it plans the
        // open and only warns/handles the mismatch once something actually tries to read the file. So
        // the sniff below must never fail planning by itself: a short/empty/unrecognized-but-not-ZIP
        // file with a valid, resolvable extension still plans Success=True through the extension's own
        // adapter, exactly as if the sniff hadn't run. The sniff only changes behavior when the
        // content POSITIVELY identifies a *different*, specific known format -- e.g. a CSV file renamed
        // to ".xlsx" is unmistakably delimited plain text, not just "not a ZIP" -- in which case we
        // reclassify to whichever CSV-open adapter the host has registered (matching Excel's own
        // behavior of reading the mismatched file as plain text) so the open still succeeds without
        // handing CSV bytes to the ZIP-based adapter's Load and hitting a raw "End of Central Directory
        // not found"-style exception. Only when that positive re-identification has nowhere to go (no
        // CSV adapter registered) do we surface a clear mismatch message instead of letting Load throw.
        if (IsZipPackageExtension(extension) && !LooksLikeZipPackage(openPath) && LooksLikeDelimitedText(openPath))
        {
            var fallbackAdapter = FileFormatResolver.FindOpenAdapter(adapters, ".csv", out var fallbackFormat);
            if (fallbackAdapter is not null && fallbackFormat is not null)
            {
                adapter = fallbackAdapter;
                format = fallbackFormat;
            }
            else
            {
                target = null;
                message = $"The file doesn't look like a valid {extension} workbook. " +
                    "It may have been renamed from a different file type.";
                return false;
            }
        }

        target = new WorkbookOpenTarget(
            openPath,
            adapter,
            extension,
            format,
            ResolveFileAccessIdentity(openPath, fileAccessIdentity));
        message = "";
        return true;
    }

    private static bool IsZipPackageExtension(string extension) =>
        ZipPackageExtensions.Contains(FileFormatResolver.NormalizeExtension(extension), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True if the file at <paramref name="path"/> begins with the ZIP local-file-header signature
    /// ("PK") that every OOXML package (.xlsx/.xlsm/.xltx/.xltm) must start with. Returns true (i.e.
    /// "assume it's fine, don't block the open") when the file can't be sniffed at all -- e.g. it's
    /// momentarily locked by another process -- so a sniff failure never masks the real error the
    /// adapter's own Load would otherwise surface.
    /// </summary>
    private static bool LooksLikeZipPackage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[2];
            var bytesRead = stream.Read(header);
            return bytesRead == header.Length && header[0] == 0x50 && header[1] == 0x4B; // "PK"
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// True only when the file at <paramref name="path"/> POSITIVELY looks like delimited plain text
    /// (e.g. a CSV file renamed to a ZIP-package extension): a leading sample that contains no binary
    /// control bytes and at least one comma or tab delimiter. This is deliberately narrower than "not a
    /// ZIP" -- an empty file, a short stub, or unrecognized bytes with no delimiter are NOT positively
    /// anything in particular, so they return false here and the caller trusts the extension instead of
    /// reclassifying or failing. Returns false (i.e. "don't reclassify, don't fail") whenever the file
    /// can't be sampled at all, matching <see cref="LooksLikeZipPackage"/>'s fail-open stance.
    /// </summary>
    private static bool LooksLikeDelimitedText(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> buffer = stackalloc byte[512];
            var bytesRead = stream.Read(buffer);
            if (bytesRead == 0)
                return false;

            var sample = buffer[..bytesRead];
            var hasDelimiter = false;
            foreach (var b in sample)
            {
                // Any binary-looking control byte (other than plain-text whitespace) means this is
                // just unrecognized bytes, not identifiable delimited text -- bail out without
                // reclassifying so the extension's own adapter stays in charge.
                if (b == 0 || (b < 0x20 && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n'))
                    return false;

                if (b == (byte)',' || b == (byte)'\t')
                    hasDelimiter = true;
            }

            return hasDelimiter;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static WorkbookFileAccessIdentity ResolveFileAccessIdentity(
        string path,
        WorkbookFileAccessIdentity? fileAccessIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (fileAccessIdentity is not null &&
            fileAccessIdentity.TryWithLocalPath(path, out var resolvedIdentity) &&
            resolvedIdentity is not null)
        {
            return resolvedIdentity;
        }

        return WorkbookFileAccessIdentity.FromLocalPath(path);
    }

}
