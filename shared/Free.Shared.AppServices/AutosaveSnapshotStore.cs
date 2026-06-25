using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

/// <summary>
/// Sidecar metadata saved alongside every autosave snapshot.
/// </summary>
public sealed class AutosaveSidecar
{
    [JsonPropertyName("originalFilePath")]
    public string? OriginalFilePath { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("timestampUtc")]
    public string? TimestampUtc { get; set; }

    [JsonPropertyName("snapshotId")]
    public string? SnapshotId { get; set; }
}

/// <summary>
/// Represents a recovery candidate found on disk.
/// </summary>
public sealed class AutosaveRecoveryCandidate
{
    public AutosaveRecoveryCandidate(string snapshotPath, string sidecarPath, AutosaveSidecar sidecar)
    {
        SnapshotPath = snapshotPath;
        SidecarPath = sidecarPath;
        Sidecar = sidecar;
    }

    public string SnapshotPath { get; }
    public string SidecarPath { get; }
    public AutosaveSidecar Sidecar { get; }
}

/// <summary>
/// Manages the autosave recovery directory: path resolution, sidecar serialization,
/// and recovery-candidate enumeration. All pure-logic, no WPF dependency.
/// </summary>
public sealed class AutosaveSnapshotStore
{
    public const string RecoveryDirectoryName = "Recovery";
    private const string SnapshotExtension = ".fxl";
    private const string SidecarExtension = ".sidecar.json";

    /// <summary>
    /// Unique identifier for this process launch. Embedded in every snapshot ID so that a
    /// recycled OS process-ID (PID) can never clobber a snapshot from a prior crashed session
    /// that the user has not yet recovered.
    /// </summary>
    public static readonly Guid LaunchId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _recoveryDirectory;

    public AutosaveSnapshotStore(string recoveryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        _recoveryDirectory = recoveryDirectory;
    }

    /// <summary>
    /// Creates a store backed by <c>%LOCALAPPDATA%\FreeX\Recovery</c>.
    /// </summary>
    public static AutosaveSnapshotStore CreateDefault(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        var localData = pathProvider.GetApplicationDataDirectory();
        return new AutosaveSnapshotStore(
            Path.Combine(localData, AppStoragePathPlanner.ProductDirectoryName, RecoveryDirectoryName));
    }

    /// <summary>
    /// Computes the snapshot path for a given stable session ID.
    /// </summary>
    public string GetSnapshotPath(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return Path.Combine(_recoveryDirectory, snapshotId + SnapshotExtension);
    }

    /// <summary>
    /// Computes the sidecar path for a given stable session ID.
    /// </summary>
    public string GetSidecarPath(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return Path.Combine(_recoveryDirectory, snapshotId + SidecarExtension);
    }

    /// <summary>
    /// Serializes the sidecar to a JSON string (no file I/O — testable).
    /// </summary>
    public static string SerializeSidecar(AutosaveSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        return JsonSerializer.Serialize(sidecar, JsonOptions);
    }

    /// <summary>
    /// Attempts to deserialize a sidecar from a JSON string (no file I/O — testable).
    /// Returns null if the JSON is missing or corrupt.
    /// </summary>
    public static AutosaveSidecar? TryDeserializeSidecar(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AutosaveSidecar>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether a snapshot should be written based on dirty state and generation tracking.
    /// </summary>
    public static bool ShouldSnapshot(bool workbookDirty, int currentGeneration, int lastSnapshotGeneration)
    {
        return workbookDirty && currentGeneration != lastSnapshotGeneration;
    }

    /// <summary>
    /// Enumerates all valid recovery candidates in the recovery directory.
    /// Stale or corrupt sidecar files are silently skipped.
    /// </summary>
    public IReadOnlyList<AutosaveRecoveryCandidate> EnumerateCandidates()
    {
        if (!Directory.Exists(_recoveryDirectory))
            return [];

        var candidates = new List<AutosaveRecoveryCandidate>();

        try
        {
            foreach (var snapshotPath in Directory.EnumerateFiles(_recoveryDirectory, "*" + SnapshotExtension))
            {
                try
                {
                    if (!File.Exists(snapshotPath))
                        continue;

                    // Derive the sidecar from the snapshot name
                    var baseName = Path.GetFileNameWithoutExtension(snapshotPath);
                    var sidecarPath = Path.Combine(_recoveryDirectory, baseName + SidecarExtension);

                    if (!File.Exists(sidecarPath))
                        continue;

                    var json = File.ReadAllText(sidecarPath);
                    var sidecar = TryDeserializeSidecar(json);
                    if (sidecar is null)
                        continue;

                    candidates.Add(new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar));
                }
                catch
                {
                    // Skip corrupt or inaccessible entries.
                }
            }
        }
        catch
        {
            // Directory enumeration failed — return what we have.
        }

        return candidates;
    }

    /// <summary>
    /// Deletes a recovery snapshot and its sidecar. Ignores errors (best-effort cleanup).
    /// </summary>
    public static void DeleteCandidate(AutosaveRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        try { File.Delete(candidate.SnapshotPath); } catch { /* best-effort */ }
        try { File.Delete(candidate.SidecarPath); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Moves a recovery snapshot + sidecar aside into a <c>Quarantine</c> subfolder of the recovery
    /// directory, so a structurally-corrupt snapshot (e.g. a truncated ZIP from a crashed write) is
    /// NOT re-offered on every launch — which otherwise produces an endless "Could not recover the
    /// document" loop. The bytes are preserved (moved, not deleted) for diagnostics. Best-effort: if
    /// the move fails the snapshot is deleted instead so the loop still ends. Ignores errors.
    /// </summary>
    public static void QuarantineCandidate(AutosaveRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var dir = Path.GetDirectoryName(candidate.SnapshotPath);
        if (string.IsNullOrEmpty(dir))
        {
            DeleteCandidate(candidate);
            return;
        }

        string? quarantine = null;
        try
        {
            quarantine = Path.Combine(dir, "Quarantine");
            Directory.CreateDirectory(quarantine);
        }
        catch { /* fall back to delete below */ }

        MoveAsideOrDelete(candidate.SnapshotPath, quarantine);
        MoveAsideOrDelete(candidate.SidecarPath, quarantine);
    }

    private static void MoveAsideOrDelete(string path, string? quarantineDir)
    {
        try
        {
            if (!File.Exists(path))
                return;
            if (quarantineDir is not null)
            {
                var dest = Path.Combine(quarantineDir, Path.GetFileName(path));
                if (File.Exists(dest))
                    dest = Path.Combine(quarantineDir,
                        Path.GetFileNameWithoutExtension(path) + "." + LaunchId.ToString("N")[..8] + Path.GetExtension(path));
                File.Move(path, dest);
                return;
            }
        }
        catch { /* fall through to delete */ }

        try { File.Delete(path); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Deletes the snapshot and sidecar for a specific session ID. Ignores errors (best-effort cleanup).
    /// </summary>
    public void DeleteSnapshot(string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            return;

        try { File.Delete(GetSnapshotPath(snapshotId)); } catch { /* best-effort */ }
        try { File.Delete(GetSidecarPath(snapshotId)); } catch { /* best-effort */ }
    }
}
