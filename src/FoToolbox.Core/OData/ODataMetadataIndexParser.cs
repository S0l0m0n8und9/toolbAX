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

        // An element with a blank Name is passed over with reader.Skip(), which ALREADY advances the reader
        // onto the node that follows the skipped element (for a self-closing element Skip() is equivalent to
        // Read(); for an expanded one it steps over the children and the end tag). Reading again at the top
        // of the loop would step over that node as well, silently dropping the sibling after any unnamed
        // element (#184). So the loop advances manually: when Skip() has already positioned the reader on an
        // unhandled node, the next iteration processes it instead of reading past it. Well-formed documents
        // never set the flag and behave exactly as `while (reader.Read())` did.
        var alreadyPositioned = false;

        while (alreadyPositioned || reader.Read())
        {
            alreadyPositioned = false;

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
                    alreadyPositioned = !reader.EOF;
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
                    alreadyPositioned = !reader.EOF;
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
                    alreadyPositioned = !reader.EOF;
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

        var termAliases = ReadTermAliases(rawXml);
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
                        var maxLength = TrimOrNull(reader.GetAttribute("MaxLength"));
                        var precision = TrimOrNull(reader.GetAttribute("Precision"));
                        var scale = TrimOrNull(reader.GetAttribute("Scale"));
                        string? minValue = null;
                        string? maxValue = null;

                        if (!reader.IsEmptyElement)
                        {
                            var propertyDepth = reader.Depth;
                            while (reader.Read())
                            {
                                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == propertyDepth && reader.LocalName == "Property")
                                {
                                    break;
                                }

                                if (reader.NodeType != XmlNodeType.Element || reader.Depth != propertyDepth + 1 || reader.LocalName != "Annotation")
                                {
                                    continue;
                                }

                                var term = NormalizeTerm(reader.GetAttribute("Term"), termAliases);
                                if (string.Equals(term, ValidationMinimumTerm, StringComparison.OrdinalIgnoreCase))
                                {
                                    minValue ??= ReadAnnotationLiteral(reader, "Minimum");
                                }
                                else if (string.Equals(term, ValidationMaximumTerm, StringComparison.OrdinalIgnoreCase))
                                {
                                    maxValue ??= ReadAnnotationLiteral(reader, "Maximum");
                                }
                                else if (string.Equals(term, ValidationMaxLengthTerm, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(term, CoreMaxLengthTerm, StringComparison.OrdinalIgnoreCase))
                                {
                                    maxLength ??= ReadAnnotationLiteral(reader, "MaxLength");
                                }
                            }
                        }

                        (minValue, maxValue) = ApplyDefaultNumericRange(type, minValue, maxValue);

                        props.Add(new ODataProperty(
                            propName,
                            type,
                            nullable,
                            IsKey: keyNames.Contains(propName),
                            MaxLength: maxLength,
                            Precision: precision,
                            Scale: scale,
                            MinValue: minValue,
                            MaxValue: maxValue));
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

    private static Dictionary<string, string> ReadTermAliases(string rawXml)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var sr = new StringReader(rawXml);
        using var reader = XmlReader.Create(sr, CreateSettings());

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Schema")
            {
                continue;
            }

            var alias = TrimOrNull(reader.GetAttribute("Alias"));
            var ns = TrimOrNull(reader.GetAttribute("Namespace"));
            if (!string.IsNullOrWhiteSpace(alias) &&
                !string.IsNullOrWhiteSpace(ns) &&
                !aliases.ContainsKey(alias))
            {
                aliases[alias] = ns;
            }
        }

        return aliases;
    }

    private static string? NormalizeTerm(string? term, IReadOnlyDictionary<string, string> aliases)
    {
        var trimmed = TrimOrNull(term);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var dot = trimmed.IndexOf('.');
        if (dot <= 0)
        {
            return trimmed;
        }

        var alias = trimmed[..dot];
        if (!aliases.TryGetValue(alias, out var ns))
        {
            return trimmed;
        }

        return $"{ns}{trimmed[dot..]}";
    }

    private static (string? MinValue, string? MaxValue) ApplyDefaultNumericRange(string type, string? minValue, string? maxValue)
    {
        var normalized = TrimOrNull(type);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (minValue, maxValue);
        }

        string? defaultMin = null;
        string? defaultMax = null;
        switch (normalized)
        {
            case "Edm.SByte":
                defaultMin = "-128";
                defaultMax = "127";
                break;
            case "Edm.Byte":
                defaultMin = "0";
                defaultMax = "255";
                break;
            case "Edm.Int16":
                defaultMin = "-32768";
                defaultMax = "32767";
                break;
            case "Edm.Int32":
                defaultMin = "-2147483648";
                defaultMax = "2147483647";
                break;
            case "Edm.Int64":
                defaultMin = "-9223372036854775808";
                defaultMax = "9223372036854775807";
                break;
        }

        if (defaultMin is null || defaultMax is null)
        {
            return (minValue, maxValue);
        }

        return (minValue ?? defaultMin, maxValue ?? defaultMax);
    }

    private static string? ReadAnnotationLiteral(XmlReader annotationReader, string recordPropertyName)
    {
        var direct = TryGetLiteralAttributeValue(annotationReader);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (annotationReader.IsEmptyElement)
        {
            return null;
        }

        using var subtree = annotationReader.ReadSubtree();
        subtree.Read();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var fromAttribute = TryGetLiteralAttributeValue(subtree);
            if (!string.IsNullOrWhiteSpace(fromAttribute))
            {
                return fromAttribute;
            }

            if (IsLiteralElementName(subtree.LocalName))
            {
                var value = TrimOrNull(subtree.ReadElementContentAsString());
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
                continue;
            }

            if (!string.Equals(subtree.LocalName, "PropertyValue", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyName = subtree.GetAttribute("Property");
            if (!string.Equals(propertyName, recordPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var propertyValueLiteral = TryGetLiteralAttributeValue(subtree);
            if (!string.IsNullOrWhiteSpace(propertyValueLiteral))
            {
                return propertyValueLiteral;
            }

            if (subtree.IsEmptyElement)
            {
                continue;
            }

            using var propertyValueSubtree = subtree.ReadSubtree();
            propertyValueSubtree.Read();
            while (propertyValueSubtree.Read())
            {
                if (propertyValueSubtree.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                var nestedLiteral = TryGetLiteralAttributeValue(propertyValueSubtree);
                if (!string.IsNullOrWhiteSpace(nestedLiteral))
                {
                    return nestedLiteral;
                }

                if (!IsLiteralElementName(propertyValueSubtree.LocalName))
                {
                    continue;
                }

                var nestedValue = TrimOrNull(propertyValueSubtree.ReadElementContentAsString());
                if (!string.IsNullOrWhiteSpace(nestedValue))
                {
                    return nestedValue;
                }
            }
        }

        return null;
    }

    private static string? TryGetLiteralAttributeValue(XmlReader reader)
    {
        foreach (var attrName in LiteralAttributeNames)
        {
            var value = TrimOrNull(reader.GetAttribute(attrName));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static bool IsLiteralElementName(string name)
    {
        return LiteralElementNames.Contains(name);
    }

    private static string? TrimOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static readonly HashSet<string> LiteralAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Int",
        "Decimal",
        "Float",
        "String",
        "Date",
        "DateTimeOffset",
        "Duration",
        "TimeOfDay",
        "Bool",
        "Guid",
        "Binary"
    };

    private static readonly HashSet<string> LiteralElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Int",
        "Decimal",
        "Float",
        "String",
        "Date",
        "DateTimeOffset",
        "Duration",
        "TimeOfDay",
        "Bool",
        "Guid",
        "Binary"
    };

    private const string ValidationMinimumTerm = "Org.OData.Validation.V1.Minimum";
    private const string ValidationMaximumTerm = "Org.OData.Validation.V1.Maximum";
    private const string ValidationMaxLengthTerm = "Org.OData.Validation.V1.MaxLength";
    private const string CoreMaxLengthTerm = "Org.OData.Core.V1.MaxLength";

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
