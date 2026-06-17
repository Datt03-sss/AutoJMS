using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoJMS;

public class HashManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; }

    [JsonPropertyName("versions")]
    public Dictionary<string, HashVersionEntry> Versions { get; set; } = new();
}

public class HashVersionEntry
{
    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();
}
