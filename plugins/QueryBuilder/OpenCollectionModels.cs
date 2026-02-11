using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QueryBuilderPlugin;

// Minimal subset of OpenCollection v1.0.0 schema (https://schema.opencollection.com).
// QueryBuilder exports/imports HTTP requests (GET URLs) using this shape.

public sealed class OpenCollectionCollectionDoc
{
    [JsonPropertyName("opencollection")]
    public string OpenCollection { get; set; } = "1.0.0";

    [JsonPropertyName("info")]
    public OpenCollectionCollectionInfo Info { get; set; } = new();

    [JsonPropertyName("items")]
    public List<OpenCollectionHttpRequestItem> Items { get; set; } = new();

    [JsonPropertyName("bundled")]
    public bool? Bundled { get; set; } = true;
}

public sealed class OpenCollectionCollectionInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "FoToolbox Queries";
}

// HttpRequest (Item) in the schema.
public sealed class OpenCollectionHttpRequestItem
{
    [JsonPropertyName("info")]
    public OpenCollectionItemInfo Info { get; set; } = new();

    [JsonPropertyName("http")]
    public OpenCollectionHttp Http { get; set; } = new();
}

public sealed class OpenCollectionItemInfo
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
    public string Method { get; set; } = "GET";

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("headers")]
    public List<OpenCollectionHeader>? Headers { get; set; }
}

public sealed class OpenCollectionHeader
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

