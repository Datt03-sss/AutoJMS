using System.Text.Json;

namespace AutoJMS.DataHub.Api.Domain;

public static class ProjectionPayloadCompactor
{
    private const int MaximumStringLength = 512;
    private static readonly IReadOnlyDictionary<string, string> AllowedProperties = BuildAllowedProperties();

    public static JsonElement? Compact(JsonElement? source)
    {
        if (source is null || source.Value.ValueKind != JsonValueKind.Object) return null;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in source.Value.EnumerateObject())
            {
                if (!AllowedProperties.TryGetValue(property.Name, out var canonicalName)
                    || !written.Add(canonicalName)
                    || !IsScalar(property.Value.ValueKind))
                    continue;

                writer.WritePropertyName(canonicalName);
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString() ?? "";
                    writer.WriteStringValue(value.Length <= MaximumStringLength ? value : value[..MaximumStringLength]);
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static bool IsScalar(JsonValueKind kind)
        => kind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static IReadOnlyDictionary<string, string> BuildAllowedProperties()
    {
        var names = new[]
        {
            "scanNetworkName", "scanNetworkCode", "scanByCode", "scanByName",
            "staffCode", "staffName", "staffContact", "packageNumber", "taskCode",
            "nextStopName", "nextNetworkCode", "weight",
            "remark1", "remark2", "remark3", "remark4", "remark5",
            "remark6", "remark7", "remark8", "remark9"
        };
        return names.ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
    }
}
