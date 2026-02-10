using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace FoToolbox.Core.OData;

/// <summary>
/// Streaming parsers for OData $metadata.
/// Intended for fast entity listing and on-demand per-entity field loading.
/// </summary>
public static class ODataMetadataIndexParser
{
    public static ODataEntityIndex ParseIndex(string rawXml, string? etag = null)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return new ODataEntityIndex(Array.Empty<ODataEntityIndexItem>(), Array.Empty<ODataEnumType>(), etag);
        }

        var typeCounts = new Dictionary<string, (int props, int navs)>(StringComparer.OrdinalIgnoreCase);
        var entityTypes = new List<ODataEntityIndexItem>();
        var entitySets = new List<(string Name, string? TypeRef)>();
        var enums = new List<ODataEnumType>();
        string? currentSchemaNamespace = null;

        using var sr = new StringReader(rawXml);
        using var reader = XmlReader.Create(sr, CreateSettings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var local = reader.LocalName;
            if (local == "Schema")
            {
                currentSchemaNamespace = reader.GetAttribute("Namespace");
                continue;
            }

            if (local == "EnumType")
            {
                var enumName = reader.GetAttribute("Name");
                if (string.IsNullOrWhiteSpace(enumName))
                {
                    reader.Skip();
                    continue;
                }

                var fullName = Qualify(currentSchemaNamespace, enumName);
                var members = new List<string>();
                if (!reader.IsEmptyElement)
                {
                    var startDepth = reader.Depth;
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == startDepth && reader.LocalName == "EnumType")
                        {
                            break;
                        }
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Member")
                        {
                            var member = reader.GetAttribute("Name");
                            if (!string.IsNullOrWhiteSpace(member))
                            {
                                members.Add(member);
                            }
                        }
                    }
                }

                enums.Add(new ODataEnumType(fullName, members));
                continue;
            }

            if (local == "EntityType")
            {
                var typeName = reader.GetAttribute("Name");
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    reader.Skip();
                    continue;
                }

                var fullName = Qualify(currentSchemaNamespace, typeName);
                int propCount = 0;
                int navCount = 0;

                if (!reader.IsEmptyElement)
                {
                    var startDepth = reader.Depth;
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == startDepth && reader.LocalName == "EntityType")
                        {
                            break;
                        }

                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        // Count direct children only; nested nodes (e.g. Key) are ignored.
                        if (reader.Depth == startDepth + 1)
                        {
                            if (reader.LocalName == "Property") propCount++;
                            else if (reader.LocalName == "NavigationProperty") navCount++;
                        }
                    }
                }

                typeCounts[fullName] = (propCount, navCount);
                typeCounts[typeName] = (propCount, navCount);
                entityTypes.Add(new ODataEntityIndexItem(typeName, propCount, navCount));
                continue;
            }

            if (local == "EntitySet")
            {
                var setName = reader.GetAttribute("Name");
                if (string.IsNullOrWhiteSpace(setName))
                {
                    reader.Skip();
                    continue;
                }

                var typeRef = reader.GetAttribute("EntityType");
                entitySets.Add((setName, typeRef));
                continue;
            }
        }

        IReadOnlyList<ODataEntityIndexItem> entities;
        if (entitySets.Count > 0)
        {
            var items = new List<ODataEntityIndexItem>(entitySets.Count);
            foreach (var set in entitySets)
            {
                var (props, navs) = ResolveCounts(typeCounts, set.TypeRef);
                items.Add(new ODataEntityIndexItem(set.Name, props, navs));
            }
            entities = items;
        }
        else
        {
            // Fallback when EntitySet definitions are absent: list EntityType names.
            entities = entityTypes;
        }

        return new ODataEntityIndex(entities, enums, etag);
    }

    public static ODataEntity? TryParseEntityDetails(string rawXml, string entitySetName)
    {
        if (string.IsNullOrWhiteSpace(rawXml) || string.IsNullOrWhiteSpace(entitySetName))
        {
            return null;
        }

        var typeRef = FindEntityTypeRefForSet(rawXml, entitySetName);
        var targetTypeName = string.IsNullOrWhiteSpace(typeRef) ? entitySetName : typeRef!.Split('.').Last();

        using var sr = new StringReader(rawXml);
        using var reader = XmlReader.Create(sr, CreateSettings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "EntityType")
            {
                continue;
            }

            var name = reader.GetAttribute("Name");
            if (!string.Equals(name, targetTypeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var props = new List<ODataProperty>();
            var navs = new List<ODataNavigationProperty>();
            var keyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!reader.IsEmptyElement)
            {
                var startDepth = reader.Depth;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == startDepth && reader.LocalName == "EntityType")
                    {
                        break;
                    }

                    if (reader.NodeType != XmlNodeType.Element || reader.Depth != startDepth + 1)
                    {
                        continue;
                    }

                    if (reader.LocalName == "Key")
                    {
                        if (!reader.IsEmptyElement)
                        {
                            var keyDepth = reader.Depth;
                            while (reader.Read())
                            {
                                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == keyDepth && reader.LocalName == "Key")
                                {
                                    break;
                                }

                                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "PropertyRef")
                                {
                                    var keyName = reader.GetAttribute("Name");
                                    if (!string.IsNullOrWhiteSpace(keyName))
                                    {
                                        keyNames.Add(keyName);
                                    }
                                }
                            }
                        }
                    }
                    else if (reader.LocalName == "Property")
                    {
                        var propName = reader.GetAttribute("Name");
                        if (string.IsNullOrWhiteSpace(propName)) continue;
                        var type = reader.GetAttribute("Type") ?? "Edm.String";
                        var nullable = !string.Equals(reader.GetAttribute("Nullable"), "false", StringComparison.OrdinalIgnoreCase);
                        props.Add(new ODataProperty(propName, type, nullable, IsKey: keyNames.Contains(propName)));
                    }
                    else if (reader.LocalName == "NavigationProperty")
                    {
                        var navName = reader.GetAttribute("Name");
                        var navType = reader.GetAttribute("Type");
                        if (string.IsNullOrWhiteSpace(navName) || string.IsNullOrWhiteSpace(navType)) continue;
                        navs.Add(new ODataNavigationProperty(navName, navType));
                    }
                }
            }

            // In case <Key> appears after <Property> (unusual, but valid), fix up key flags.
            if (keyNames.Count > 0 && props.Count > 0)
            {
                for (var i = 0; i < props.Count; i++)
                {
                    var p = props[i];
                    if (!p.IsKey && keyNames.Contains(p.Name))
                    {
                        props[i] = p with { IsKey = true };
                    }
                }
            }

            return new ODataEntity(entitySetName, props, navs);
        }

        return null;
    }

    private static (int props, int navs) ResolveCounts(Dictionary<string, (int props, int navs)> typeCounts, string? typeRef)
    {
        if (string.IsNullOrWhiteSpace(typeRef))
        {
            return (0, 0);
        }

        if (typeCounts.TryGetValue(typeRef, out var counts))
        {
            return counts;
        }

        var shortName = typeRef.Split('.').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(shortName) && typeCounts.TryGetValue(shortName, out counts))
        {
            return counts;
        }

        return (0, 0);
    }

    private static string? FindEntityTypeRefForSet(string rawXml, string entitySetName)
    {
        using var sr = new StringReader(rawXml);
        using var reader = XmlReader.Create(sr, CreateSettings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "EntitySet")
            {
                continue;
            }

            var name = reader.GetAttribute("Name");
            if (!string.Equals(name, entitySetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return reader.GetAttribute("EntityType");
        }

        return null;
    }

    private static string Qualify(string? ns, string name)
    {
        return string.IsNullOrWhiteSpace(ns) ? name : $"{ns}.{name}";
    }

    private static XmlReaderSettings CreateSettings()
        => new()
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
            CloseInput = true
        };
}
