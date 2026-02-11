using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ODataPostBuilderPlugin;

// Minimal subset of OpenCollection v1.0.0 schema (https://schema.opencollection.com).
// We only support HTTP request items for export/import today.

// Root "OpenCollection" document that can contain one or more items.
public sealed class OpenCollectionCollectionDoc
{
    [JsonPropertyName("opencollection")]
    public string OpenCollection { get; set; } = "1.0.0";

    [JsonPropertyName("info")]
    public OpenCollectionCollectionInfo Info { get; set; } = new();

    [JsonPropertyName("items")]
    public List<OpenCollectionRequestDoc> Items { get; set; } = new();

    // Optional per schema; we set it on export so consumers know this is a single-file bundle.
    [JsonPropertyName("bundled")]
    public bool? Bundled { get; set; } = true;
}

// Root info block (subset).
public sealed class OpenCollectionCollectionInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "FoToolbox API";

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("authors")]
    public List<OpenCollectionAuthor>? Authors { get; set; }
}

public sealed class OpenCollectionAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

// HTTP request item ("HttpRequest" in the schema). This is an "Item" inside the root document.
public sealed class OpenCollectionRequestDoc
{
    [JsonPropertyName("info")]
    public OpenCollectionInfo Info { get; set; } = new();

    [JsonPropertyName("http")]
    public OpenCollectionHttp Http { get; set; } = new();
}

public sealed class OpenCollectionInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "http";

    [JsonPropertyName("seq")]
    public int Seq { get; set; } = 1;
}

public sealed class OpenCollectionHttp
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "POST";

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("headers")]
    public List<OpenCollectionHeader>? Headers { get; set; }

    [JsonPropertyName("body")]
    public OpenCollectionBody? Body { get; set; }
}

public sealed class OpenCollectionHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public sealed class OpenCollectionBody
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "json";

    // For json bodies, we store the JSON string.
    [JsonPropertyName("data")]
    public string? Data { get; set; }
}
