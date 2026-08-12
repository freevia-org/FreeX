using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

/// <summary>
/// Common JSON serialization and file persistence for validation and evidence artifacts.
/// </summary>
public static class JsonArtifactIO
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static JsonSerializerOptions CreateSerializerOptions(
        bool propertyNameCaseInsensitive = false,
        bool camelCase = true,
        bool writeIndented = true,
        bool stringEnums = false,
        bool ignoreNullValues = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive,
            PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null,
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = ignoreNullValues
                ? JsonIgnoreCondition.WhenWritingNull
                : JsonIgnoreCondition.Never,
        };
        if (stringEnums)
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static T Read<T>(
        string path,
        JsonSerializerOptions? options = null,
        string? missingMessage = null,
        string? invalidMessage = null,
        Func<Exception>? invalidExceptionFactory = null)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (missingMessage is not null && !File.Exists(path))
            throw new FileNotFoundException(missingMessage, path);

        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        return value ?? throw (invalidExceptionFactory?.Invoke() ?? new InvalidDataException(
            invalidMessage ?? $"JSON artifact could not be read: {Path.GetFileName(path)}"));
    }

    public static T? ReadIfExists<T>(string path, JsonSerializerOptions? options = null)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), options)
            : null;
    }

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, options);
    }

    public static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        EnsureParentDirectory(path);
        File.WriteAllText(path, Serialize(value, options), Utf8WithoutBom);
    }

    public static Task WriteAtomicAsync<T>(
        string path,
        T value,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AtomicFileWriter.WriteAllBytesAsync(
            path,
            JsonSerializer.SerializeToUtf8Bytes(value, options),
            cancellationToken);
    }

    public static void AppendLine<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        EnsureParentDirectory(path);
        File.AppendAllText(path, Serialize(value, options) + Environment.NewLine, Utf8WithoutBom);
    }

    private static void EnsureParentDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
