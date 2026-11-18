using System.Text.Json.Serialization;

namespace GameScanner;

/// <summary>JSON cho dữ liệu riêng của GUI (library.json, settings.json).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryData))]
[JsonSerializable(typeof(ScannerSettings))]
internal partial class AppDataJsonContext : JsonSerializerContext
{
}
