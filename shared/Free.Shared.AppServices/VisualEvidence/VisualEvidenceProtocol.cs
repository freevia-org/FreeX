using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

public sealed record VisualEvidenceArgumentSpec(
    string Key,
    string Name,
    string MissingValueMessage,
    string BlankValueMessage,
    string DuplicateMessage,
    bool AllowEqualsSyntax = false);

public sealed record VisualEvidenceArgumentParseResult(
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlySet<string> PresentKeys,
    string[] RemainingArguments,
    string? Error)
{
    public bool IsPresent(string key) => PresentKeys.Contains(key);

    public string? Value(string key) =>
        Values.TryGetValue(key, out var value) ? value : null;
}

public readonly record struct VisualEvidenceArgumentValue(bool IsPresent, string? Value);

public static class VisualEvidenceArgumentParser
{
    public static VisualEvidenceArgumentParseResult Parse(
        IReadOnlyList<string> args,
        IReadOnlyList<VisualEvidenceArgumentSpec> specs,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(specs);

        var specsByKey = specs.ToDictionary(spec => spec.Key, StringComparer.Ordinal);
        if (specsByKey.Count != specs.Count)
            throw new ArgumentException("Visual-evidence argument keys must be unique.", nameof(specs));

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var remaining = new List<string>();

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            var match = FindMatch(argument, specs, comparison);
            if (match is null)
            {
                remaining.Add(argument);
                continue;
            }

            var (spec, inlineValue) = match.Value;
            if (!present.Add(spec.Key))
                return Result(values, present, [], spec.DuplicateMessage);

            string? value;
            if (inlineValue is not null)
            {
                value = inlineValue;
            }
            else
            {
                if (index + 1 >= args.Count)
                    return Result(values, present, [], spec.MissingValueMessage);
                value = args[++index];
            }

            if (string.IsNullOrWhiteSpace(value))
                return Result(values, present, [], spec.BlankValueMessage);

            values[spec.Key] = value;
        }

        return Result(values, present, remaining, null);
    }

    public static VisualEvidenceArgumentValue ReadFirst(
        IReadOnlyList<string> args,
        string name,
        StringComparison comparison = StringComparison.Ordinal,
        bool allowEqualsSyntax = false)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], name, comparison))
                continue;

            var value = index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1])
                ? args[index + 1]
                : null;
            return new VisualEvidenceArgumentValue(true, value);
        }

        if (allowEqualsSyntax)
        {
            var prefix = name + "=";
            foreach (var argument in args)
            {
                if (!argument.StartsWith(prefix, comparison))
                    continue;

                var value = argument[prefix.Length..];
                return new VisualEvidenceArgumentValue(
                    true,
                    string.IsNullOrWhiteSpace(value) ? null : value);
            }
        }

        return new VisualEvidenceArgumentValue(false, null);
    }

    private static (VisualEvidenceArgumentSpec Spec, string? InlineValue)? FindMatch(
        string argument,
        IReadOnlyList<VisualEvidenceArgumentSpec> specs,
        StringComparison comparison)
    {
        foreach (var spec in specs)
        {
            if (string.Equals(argument, spec.Name, comparison))
                return (spec, null);

            if (!spec.AllowEqualsSyntax)
                continue;

            var prefix = spec.Name + "=";
            if (argument.StartsWith(prefix, comparison))
                return (spec, argument[prefix.Length..]);
        }

        return null;
    }

    private static VisualEvidenceArgumentParseResult Result(
        IReadOnlyDictionary<string, string?> values,
        IReadOnlySet<string> present,
        IEnumerable<string> remaining,
        string? error) =>
        new(values, present, remaining.ToArray(), error);
}

public static class VisualEvidencePathPolicy
{
    public static string ResolveDeclaredPath(string baseDirectory, string declaredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredPath);

        return Path.IsPathRooted(declaredPath)
            ? Path.GetFullPath(declaredPath)
            : Path.GetFullPath(Path.Combine(baseDirectory, declaredPath));
    }

    public static string ResolveContainedPath(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Visual-evidence artifact paths must be relative to the run root.");

        var fullPath = ResolveDeclaredPath(root, relativePath);
        if (!IsContained(root, fullPath))
            throw new InvalidDataException($"Visual-evidence artifact path escapes the run root: {relativePath}");
        return fullPath;
    }

    public static bool IsContained(
        string root,
        string path,
        StringComparison? comparison = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullRoot = TrimSeparators(Path.GetFullPath(root));
        var fullPath = TrimSeparators(Path.GetFullPath(path));
        var pathComparison = comparison ?? (OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
        if (string.Equals(fullRoot, fullPath, pathComparison))
            return true;

        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, pathComparison);
    }

    public static string NormalizeRelativePath(string root, string path) =>
        Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path))
            .Replace('\\', '/');

    private static string TrimSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

public static class VisualEvidenceTextPolicy
{
    private const string PortableInvalidFileNameCharacters = "<>:\"/\\|?*";

    public static string ToSafeArtifactName(string value, char replacement = '-')
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character =>
            character < ' ' || invalid.Contains(character) ||
            PortableInvalidFileNameCharacters.IndexOf(character) >= 0
                ? replacement
                : character).ToArray());
    }

    public static string ToAsciiSafeArtifactName(string value, char replacement = '-')
    {
        ArgumentNullException.ThrowIfNull(value);
        return new string(value.Select(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-'
                ? character
                : replacement).ToArray());
    }

    public static string ToAlphaNumericSafeArtifactName(string value, char replacement = '_')
    {
        ArgumentNullException.ThrowIfNull(value);
        return new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : replacement).ToArray());
    }

    public static string ToLowerSafeArtifactName(string value, char replacement = '-')
    {
        ArgumentNullException.ThrowIfNull(value);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = value.Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character)
                ? replacement
                : char.ToLowerInvariant(character));
        return new string(characters.ToArray()).Trim(replacement);
    }

    public static string NormalizeLabel(string? label, string? fallback = null) =>
        (string.IsNullOrWhiteSpace(label) ? fallback ?? string.Empty : label)
            .Trim()
            .TrimEnd(':')
            .Replace("_", string.Empty, StringComparison.Ordinal);

    public static string SemanticActionId(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var value = label.Trim().ToLowerInvariant();
        if (value.StartsWith("+", StringComparison.Ordinal))
            value = "add " + value[1..];
        else if (value.StartsWith("-", StringComparison.Ordinal))
            value = "remove " + value[1..];

        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        return string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}

public static class VisualEvidenceManifestIO
{
    public static JsonSerializerOptions CreateJsonOptions(
        bool propertyNameCaseInsensitive = false,
        bool camelCase = true,
        bool writeIndented = true,
        bool stringEnums = true)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive,
            PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null,
            WriteIndented = writeIndented,
        };
        if (stringEnums)
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static T Read<T>(
        string path,
        JsonSerializerOptions options,
        string? missingMessage = null,
        string? invalidMessage = null,
        Func<Exception>? invalidExceptionFactory = null)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        if (missingMessage is not null && !File.Exists(path))
            throw new FileNotFoundException(missingMessage, path);

        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        return value ?? throw (invalidExceptionFactory?.Invoke() ?? new InvalidDataException(
            invalidMessage ?? $"Visual-evidence manifest could not be read: {Path.GetFileName(path)}"));
    }

    public static T? ReadIfExists<T>(string path, JsonSerializerOptions options)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), options)
            : null;
    }

    public static string Serialize<T>(T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        return JsonSerializer.Serialize(value, options);
    }

    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(value, options), new UTF8Encoding(false));
    }
}

public sealed record VisualEvidenceProgressRecord(string Message)
{
    public override string ToString() => Message;
}

public static class VisualEvidenceProgressLog
{
    public static void Reset(string? path)
    {
        if (path is not null)
            File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
    }

    public static void Append(string? path, VisualEvidenceProgressRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (path is not null)
            File.AppendAllText(path, record + Environment.NewLine, new UTF8Encoding(false));
    }
}

public static class VisualEvidenceHash
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return ToLowerHex(SHA256.HashData(stream));
    }

    public static string Sha256Text(string value) =>
        ToLowerHex(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Sha256Bytes(ReadOnlySpan<byte> value) =>
        ToLowerHex(SHA256.HashData(value));

    private static string ToLowerHex(ReadOnlySpan<byte> hash) =>
        Convert.ToHexString(hash).ToLowerInvariant();
}

public static class VisualEvidenceNormalization
{
    public static IReadOnlyDictionary<string, TValue> OrderMetadata<TValue>(
        IEnumerable<KeyValuePair<string, TValue>> values,
        StringComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(comparer);
        return values
            .OrderBy(pair => pair.Key, comparer)
            .ToDictionary(pair => pair.Key, pair => pair.Value, comparer);
    }
}
