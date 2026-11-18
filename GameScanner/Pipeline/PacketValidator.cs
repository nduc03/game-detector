using System.Text.Json;

namespace GameScanner;

/// <summary>Kết quả một bước pipeline không đúng cấu trúc <see cref="PipelinePacket"/>.</summary>
internal sealed class PacketFormatException(string message) : Exception(message);

/// <summary>
/// Kiểm tra lại cấu trúc JSON mà một bước pipeline trả về trước khi chuyển cho bước sau.
/// Cấu trúc hợp lệ:
///   { "included": [ mục... ], "excluded": [ mục... ] | null | (không có) }
///   mục = { "name"?, "publisher"?, "exePath"?, "installDir"?, "source"?: chuỗi | null,
///           "candidateExes"?: [chuỗi...] | null, "launcherGame"?, "redistributable"?: true/false | null }
/// Mục trong "included" phải có exePath hoặc installDir. Trường lạ được chấp nhận và giữ nguyên.
/// </summary>
internal static class PacketValidator
{
    private static readonly string[] StringFields = ["name", "publisher", "exePath", "installDir", "source"];
    private static readonly string[] BoolFields = ["launcherGame", "redistributable"];

    public static PipelinePacket Parse(string? json)
    {
        string text = (json ?? "").TrimStart('﻿').Trim();
        if (text.Length == 0)
            throw new PacketFormatException("Kết quả rỗng, không có JSON nào.");

        try
        {
            using (JsonDocument doc = JsonDocument.Parse(text))
                ValidateRoot(doc.RootElement);

            PipelinePacket packet = JsonSerializer.Deserialize(text, AppJsonContext.Default.PipelinePacket)
                ?? throw new PacketFormatException("Kết quả là JSON null.");
            Normalize(packet);
            return packet;
        }
        catch (JsonException ex)
        {
            throw new PacketFormatException($"JSON không hợp lệ: {ex.Message}");
        }
    }

    /// <summary>Đưa packet về dạng không còn null ở những chỗ code phía sau coi là danh sách.</summary>
    public static void Normalize(PipelinePacket packet)
    {
        packet.Included ??= [];
        packet.Included.RemoveAll(e => e is null);
        packet.Excluded?.RemoveAll(e => e is null);

        foreach (AppEntry entry in packet.Included.Concat(packet.Excluded ?? []))
            entry.CandidateExes ??= [];
    }

    private static void ValidateRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new PacketFormatException($"Gốc JSON phải là object {{ \"included\": [...] }}, nhưng nhận được {Describe(root)}.");

        if (!TryGetProperty(root, "included", out JsonElement included))
            throw new PacketFormatException("Thiếu trường bắt buộc \"included\".");

        ValidateEntries(included, "included", requireLocation: true);

        if (TryGetProperty(root, "excluded", out JsonElement excluded) && excluded.ValueKind != JsonValueKind.Null)
            ValidateEntries(excluded, "excluded", requireLocation: false);
    }

    private static void ValidateEntries(JsonElement array, string path, bool requireLocation)
    {
        if (array.ValueKind != JsonValueKind.Array)
            throw new PacketFormatException($"\"{path}\" phải là mảng, nhưng nhận được {Describe(array)}.");

        int index = 0;
        foreach (JsonElement entry in array.EnumerateArray())
            ValidateEntry(entry, $"{path}[{index++}]", requireLocation);
    }

    private static void ValidateEntry(JsonElement entry, string path, bool requireLocation)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            throw new PacketFormatException($"{path} phải là object, nhưng nhận được {Describe(entry)}.");

        bool hasLocation = false;
        foreach (string field in StringFields)
        {
            if (!TryGetProperty(entry, field, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                continue;

            if (value.ValueKind != JsonValueKind.String)
                throw new PacketFormatException($"{path}.{field} phải là chuỗi hoặc null, nhưng nhận được {Describe(value)}.");

            if (field is "exePath" or "installDir" && !string.IsNullOrWhiteSpace(value.GetString()))
                hasLocation = true;
        }

        foreach (string field in BoolFields)
        {
            if (TryGetProperty(entry, field, out JsonElement value)
                && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False))
            {
                throw new PacketFormatException($"{path}.{field} phải là true/false hoặc null, nhưng nhận được {Describe(value)}.");
            }
        }

        if (TryGetProperty(entry, "candidateExes", out JsonElement candidates) && candidates.ValueKind != JsonValueKind.Null)
        {
            if (candidates.ValueKind != JsonValueKind.Array)
                throw new PacketFormatException($"{path}.candidateExes phải là mảng chuỗi, nhưng nhận được {Describe(candidates)}.");

            int index = 0;
            foreach (JsonElement candidate in candidates.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.String)
                    throw new PacketFormatException($"{path}.candidateExes[{index}] phải là chuỗi, nhưng nhận được {Describe(candidate)}.");
                index++;
            }
        }

        if (requireLocation && !hasLocation)
            throw new PacketFormatException($"{path} thiếu cả exePath lẫn installDir.");
    }

    /// <summary>Tìm trường không phân biệt hoa thường, khớp với cách deserialize của <see cref="AppJsonContext"/>.</summary>
    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "mảng",
        JsonValueKind.String => "chuỗi",
        JsonValueKind.Number => "số",
        JsonValueKind.True or JsonValueKind.False => "true/false",
        JsonValueKind.Null => "null",
        _ => element.ValueKind.ToString()
    };
}
