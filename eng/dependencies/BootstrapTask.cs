using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;

// Compiled by the installed .NET SDK's RoslynCodeTaskFactory before NuGet restore.
// This task has no PackageReferences or external executable/tool dependencies.
public sealed class FreeXPrepareAvaloniaGridFeed : Microsoft.Build.Utilities.Task
{
    [Required] public string RepositoryRoot { get; set; }
    [Required] public string Version { get; set; }
    [Required] public string ArchiveUrl { get; set; }
    [Required] public string ArchiveSha256 { get; set; }
    [Required] public long ArchiveBytes { get; set; }
    [Required] public ITaskItem[] Packages { get; set; }
    [Required] public string PackagesCachePath { get; set; }
    public bool AllowLoopbackHttpForTests { get; set; }
    public int LockTimeoutSeconds { get; set; } = 180;
    public int DownloadTimeoutSeconds { get; set; } = 120;
    [Output] public string FeedPath { get; set; }
    private string fingerprint;

    public override bool Execute()
    {
        string stage = null;
        FileStream heldLock = null;
        try
        {
            if (String.IsNullOrEmpty(Version) || Version.Any(c => !(Char.IsLetterOrDigit(c) || c == '.' || c == '-')))
                throw new InvalidDataException("Invalid immutable package version.");
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in Packages)
            {
                var name = package.ItemSpec;
                if (name != Path.GetFileName(name) || name.Contains("\\") || name.Contains(":") || !name.EndsWith("." + Version + ".nupkg", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid pinned package filename.");
                var hash = package.GetMetadata("Sha256").ToLowerInvariant();
                if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)) || expected.ContainsKey(name))
                    throw new InvalidDataException("Invalid or duplicated package hash.");
                expected.Add(name, hash);
            }
            if (expected.Count != 21 || ArchiveSha256.Length != 64 || ArchiveSha256.Any(c => !Uri.IsHexDigit(c)) || ArchiveBytes < 1)
                throw new InvalidDataException("The complete immutable 21-package manifest is required.");
            fingerprint = Version + "\n" + ArchiveSha256.ToLowerInvariant() + "\n" + String.Join("\n", expected.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
            var root = Path.GetFullPath(RepositoryRoot);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Repository root does not exist.");
            var localPackages = Path.GetFullPath(PackagesCachePath);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!localPackages.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                throw new InvalidDataException("NuGet package cache must stay inside this project; global cache overrides are not allowed.");
            var current = root;
            foreach (var part in localPackages.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length).Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part); NoLink(current);
            }
            var dependencyRoot = Path.Combine(root, ".dependencies");
            OwnedDirectory(dependencyRoot);
            var cache = Path.Combine(dependencyRoot, "avalonia-grid");
            OwnedDirectory(cache);
            FeedPath = Path.Combine(cache, Version + "-" + ArchiveSha256.Substring(0, 16).ToLowerInvariant());
            var lockPath = Path.Combine(cache, ".bootstrap.lock");
            NoLink(lockPath);
            heldLock = AcquireLock(lockPath);
            {
                var identity = Path.Combine(cache, Version + ".identity");
                NoLink(identity);
                if (File.Exists(identity) && File.ReadAllText(identity) != ArchiveSha256.ToLowerInvariant())
                    throw new InvalidDataException("Immutable version identity changed; use a new package version.");
                // A lock holder cannot coexist with another active bootstrap writer. Only marked
                // stages belonging to this exact immutable input set can be reclaimed after a crash.
                foreach (var oldStage in Directory.GetDirectories(cache, ".stage-*"))
                {
                    NoLink(oldStage);
                    var owner = Path.Combine(oldStage, ".owner");
                    NoLink(owner);
                    if (File.Exists(owner) && File.ReadAllText(owner) == fingerprint)
                        SafeRemove(oldStage);
                }
                if (ValidFeed(FeedPath, expected))
                {
                    if (!File.Exists(identity)) File.WriteAllText(identity, ArchiveSha256.ToLowerInvariant());
                    VerifyExistingNuGetPayloads(expected);
                    Log.LogMessage(MessageImportance.Low, "Avalonia Grid bootstrap: verified warm cache.");
                    return true;
                }
                if (Directory.Exists(FeedPath))
                {
                    var marker = Path.Combine(FeedPath, ".ready");
                    NoLink(marker);
                    if (!File.Exists(marker) || File.ReadAllText(marker) != fingerprint)
                        throw new InvalidDataException("Unrecognized cache directory; refusing to delete it.");
                    SafeRemove(FeedPath); // Rehydrate only the same pinned bytes, never new bytes for this version.
                }
                stage = Path.Combine(cache, ".stage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                File.WriteAllText(Path.Combine(stage, ".owner"), fingerprint);
                var archive = Path.Combine(stage, "download.zip");
                Download(archive);
                if (Hash(archive) != ArchiveSha256.ToLowerInvariant())
                    throw new InvalidDataException("Avalonia Grid archive SHA256 mismatch.");
                var prepared = Path.Combine(stage, "feed");
                Directory.CreateDirectory(prepared);
                using (var input = File.OpenRead(archive))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in zip.Entries)
                    {
                        var name = entry.FullName;
                        if (!names.Add(name) || String.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.Ordinal) || name.Contains("\\") || name.Contains(":") || name.Split('/').Any(x => x == "." || x == "..") || (((int)typeof(ZipArchiveEntry).GetProperty("ExternalAttributes").GetValue(entry, null) >> 16) & 0xF000) == 0xA000)
                            throw new InvalidDataException("Unsafe or duplicated archive entry.");
                        var destination = Path.GetFullPath(Path.Combine(stage, name));
                        if (!destination.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                            throw new InvalidDataException("Archive entry escapes its staging directory.");
                        if (!name.StartsWith("packages/", StringComparison.Ordinal)) continue;
                        var packageName = name.Substring("packages/".Length);
                        if (!expected.ContainsKey(packageName) || !extracted.Add(packageName))
                            throw new InvalidDataException("Unexpected package in immutable archive.");
                        if (entry.Length > 128L * 1024 * 1024) throw new InvalidDataException("Package entry exceeds size bound.");
                        var output = Path.Combine(prepared, packageName);
                        using (var reader = entry.Open())
                        using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[65536]; long total = 0; int count;
                            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                total += count;
                                if (total > entry.Length) throw new InvalidDataException("Package entry exceeds its declared size.");
                                writer.Write(buffer, 0, count);
                            }
                            if (total != entry.Length) throw new InvalidDataException("Package entry was truncated.");
                        }
                        if (Hash(output) != expected[packageName]) throw new InvalidDataException("Pinned package SHA256 mismatch: " + packageName);
                    }
                    if (extracted.Count != expected.Count) throw new InvalidDataException("Archive is missing pinned packages.");
                }
                File.WriteAllText(Path.Combine(prepared, ".ready"), fingerprint);
                if (!ValidFeed(prepared, expected)) throw new InvalidDataException("Staged package validation failed.");
                // Publish on the same filesystem only after every package is complete and verified.
                Directory.Move(prepared, FeedPath);
                File.WriteAllText(identity, ArchiveSha256.ToLowerInvariant());
                VerifyExistingNuGetPayloads(expected);
                Log.LogMessage(MessageImportance.High, "Avalonia Grid bootstrap: verified and atomically prepared 21 packages.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.LogError("Avalonia Grid bootstrap failed: " + ex.Message);
            return false;
        }
        finally
        {
            try { if (stage != null && Directory.Exists(stage)) SafeRemove(stage); }
            finally { if (heldLock != null) heldLock.Dispose(); }
        }
    }

    private void VerifyExistingNuGetPayloads(Dictionary<string, string> expected)
    {
        foreach (var item in expected)
        {
            var suffix = "." + Version + ".nupkg";
            var id = item.Key.Substring(0, item.Key.Length - suffix.Length).ToLowerInvariant();
            var directory = Path.Combine(PackagesCachePath, id, Version.ToLowerInvariant());
            var payload = Path.Combine(directory, id + suffix.ToLowerInvariant());
            NoLink(Path.Combine(PackagesCachePath, id)); NoLink(directory); NoLink(payload);
            if (File.Exists(payload) && Hash(payload) != item.Value)
                throw new InvalidDataException("Existing NuGet cache has different bytes for immutable package " + item.Key + "; refusing same-version replacement.");
        }
    }

    private bool ValidFeed(string directory, Dictionary<string, string> expected)
    {
        NoLink(directory);
        if (!Directory.Exists(directory)) return false;
        var marker = Path.Combine(directory, ".ready");
        NoLink(marker);
        if (!File.Exists(marker) || File.ReadAllText(marker) != fingerprint) return false;
        if (Directory.GetDirectories(directory).Length != 0 || Directory.GetFiles(directory).Length != expected.Count + 1) return false;
        foreach (var item in expected)
        {
            var file = Path.Combine(directory, item.Key);
            NoLink(file);
            if (!File.Exists(file) || Hash(file) != item.Value) return false;
        }
        return true;
    }

    private void Download(string output)
    {
        var uri = new Uri(ArchiveUrl, UriKind.Absolute);
        using (var cancel = new CancellationTokenSource())
        using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) })
        {
            if (DownloadTimeoutSeconds < 1 || DownloadTimeoutSeconds > 600) throw new InvalidDataException("Invalid download timeout.");
            cancel.CancelAfter(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FreeX-DependencyBootstrap/1.0");
            for (int redirects = 0; redirects <= 8; redirects++)
            {
                if (uri.UserInfo.Length != 0 || !(uri.Scheme == "https" || (AllowLoopbackHttpForTests && uri.Scheme == "http" && (uri.Host == "127.0.0.1" || uri.Host == "[::1]"))))
                    throw new InvalidDataException("Bootstrap URL must use HTTPS without credentials.");
                using (var response = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel.Token).GetAwaiter().GetResult())
                {
                    var status = (int)response.StatusCode;
                    if (status >= 300 && status <= 399 && response.Headers.Location != null)
                    {
                        uri = new Uri(uri, response.Headers.Location);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != ArchiveBytes)
                        throw new InvalidDataException("Archive content length mismatch.");
                    using (var reader = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var writer = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536]; long total = 0; int count;
                        while ((count = reader.ReadAsync(buffer, 0, buffer.Length, cancel.Token).GetAwaiter().GetResult()) > 0)
                        {
                            total += count;
                            if (total > ArchiveBytes) throw new InvalidDataException("Archive exceeds pinned size.");
                            writer.Write(buffer, 0, count);
                        }
                        if (total != ArchiveBytes) throw new InvalidDataException("Archive transfer was interrupted.");
                    }
                    return;
                }
            }
            throw new InvalidDataException("Too many bootstrap URL redirects.");
        }
    }

    private FileStream AcquireLock(string file)
    {
        var deadline = DateTime.UtcNow.AddSeconds(LockTimeoutSeconds);
        while (true)
        {
            try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for another bootstrap process.");
                Thread.Sleep(100);
            }
        }
    }
    private static string Hash(string file)
    {
        using (var stream = File.OpenRead(file))
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static void OwnedDirectory(string path) { NoLink(path); Directory.CreateDirectory(path); NoLink(path); }
    private static void NoLink(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Symlink/reparse points are forbidden inside the bootstrap cache."); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
    private static void SafeRemove(string directory)
    {
        NoLink(directory);
        // Reject links before recursive deletion; never traverse user/global cache locations.
        foreach (var file in Directory.GetFileSystemEntries(directory))
        {
            NoLink(file);
            if (Directory.Exists(file)) SafeRemove(file); else File.Delete(file);
        }
        Directory.Delete(directory);
    }
}
