using System.Text.Json.Serialization;

namespace GameScanner;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PipelinePacket))]
[JsonSerializable(typeof(RulesConfig))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
