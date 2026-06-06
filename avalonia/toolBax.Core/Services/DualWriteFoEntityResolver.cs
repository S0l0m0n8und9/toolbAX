using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ToolBax.Core.Services;

/// <summary>
/// Best-effort fuzzy match from a dual-write map leg's source schema (a data-entity name like
/// <c>VendVendorV2Entity</c>) to an F&amp;O OData entity set (<c>VendVendorsV2</c>), used to default the
/// Row counts "F&amp;O entity". A faithful port of the WPF plugin's scorer: builds normalised aliases
/// (drops the <c>Entity</c> suffix / version tokens), scores entity names by exact/prefix/contains +
/// token overlap, and refuses ambiguous matches. Pure — no metadata fetch (the caller supplies the
/// entity names). Returns the best entity name, or empty when nothing is confident.
/// </summary>
public static class DualWriteFoEntityResolver
{
    private static readonly HashSet<string> StopTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "cds", "dynamics", "d365", "entity", "entities", "the", "of", "and", "for", "data",
    };

    public static string Resolve(string? sourceSchema, string? sourceSchemaDistinctName, IReadOnlyList<string> entityNames)
    {
        if (entityNames is null || entityNames.Count == 0)
        {
            return string.Empty;
        }

        foreach (var schema in new[] { sourceSchema, sourceSchemaDistinctName })
        {
            var resolved = ResolveSingle(schema, entityNames);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return string.Empty;
    }

    private static string ResolveSingle(string? sourceSchema, IReadOnlyList<string> entityNames)
    {
        if (string.IsNullOrWhiteSpace(sourceSchema))
        {
            return string.Empty;
        }

        var aliases = BuildNormalizedAliases(sourceSchema);

        foreach (var alias in aliases)
        {
            foreach (var entityName in entityNames)
            {
                if (string.Equals(NormalizeEntityKey(entityName), alias, StringComparison.OrdinalIgnoreCase))
                {
                    return entityName;
                }
            }
        }

        var sourceTokens = TokenizeName(sourceSchema).Where(t => !StopTokens.Contains(t)).ToList();
        if (sourceTokens.Count == 0)
        {
            sourceTokens = TokenizeName(sourceSchema).ToList();
        }

        var ranked = new List<(string Name, int Score)>(entityNames.Count);
        foreach (var entityName in entityNames)
        {
            var score = ScoreEntityName(entityName, aliases, sourceTokens);
            if (score > int.MinValue)
            {
                ranked.Add((entityName, score));
            }
        }

        if (ranked.Count == 0)
        {
            return string.Empty;
        }

        var best = ranked.OrderByDescending(r => r.Score).First();
        if (best.Score < 110)
        {
            return string.Empty;
        }

        var second = ranked
            .Where(r => !string.Equals(r.Name, best.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Score)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(second.Name) && second.Score >= best.Score - 8)
        {
            return string.Empty;
        }

        return best.Name;
    }

    private static List<string> BuildNormalizedAliases(string sourceSchema)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = sourceSchema.Trim();
        var withoutParen = Regex.Replace(raw, @"\([^)]*\)", " ");
        var tokens = TokenizeName(withoutParen).ToList();
        var filtered = tokens.Where(t => !StopTokens.Contains(t)).ToList();

        AddAlias(aliases, raw);
        AddAlias(aliases, withoutParen);
        AddAlias(aliases, string.Concat(filtered));
        AddAlias(aliases, string.Concat(filtered.Where(t => !Regex.IsMatch(t, @"^v\d+$", RegexOptions.IgnoreCase))));
        AddAlias(aliases, string.Concat(filtered.Select(t =>
            t.StartsWith("v", StringComparison.OrdinalIgnoreCase) && t.Length > 1 ? t[1..] : t)));

        return aliases.ToList();
    }

    private static void AddAlias(HashSet<string> aliases, string candidate)
    {
        var normalized = NormalizeEntityKey(candidate);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.Add(normalized);
        }
    }

    private static int ScoreEntityName(string entityName, IReadOnlyList<string> aliases, IReadOnlyList<string> sourceTokens)
    {
        var entityNorm = NormalizeEntityKey(entityName);
        if (string.IsNullOrWhiteSpace(entityNorm))
        {
            return int.MinValue;
        }

        var bestScore = int.MinValue;
        foreach (var alias in aliases)
        {
            var score = 0;
            if (string.Equals(entityNorm, alias, StringComparison.OrdinalIgnoreCase))
            {
                score += 220;
            }
            else if (entityNorm.StartsWith(alias, StringComparison.OrdinalIgnoreCase) ||
                     alias.StartsWith(entityNorm, StringComparison.OrdinalIgnoreCase))
            {
                score += 130;
            }
            else if (entityNorm.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
                     alias.Contains(entityNorm, StringComparison.OrdinalIgnoreCase))
            {
                score += 90;
            }

            score -= Math.Abs(entityNorm.Length - alias.Length);
            bestScore = Math.Max(bestScore, score);
        }

        var entityTokens = TokenizeName(entityName).Where(t => !StopTokens.Contains(t)).ToList();
        if (entityTokens.Count > 0 && sourceTokens.Count > 0)
        {
            var overlap = entityTokens.Intersect(sourceTokens, StringComparer.OrdinalIgnoreCase).Count();
            bestScore += overlap * 28;

            if (string.Equals(entityTokens[0], sourceTokens[0], StringComparison.OrdinalIgnoreCase))
            {
                bestScore += 20;
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> TokenizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var withBoundaries = Regex.Replace(value, @"([a-z])([A-Z])", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"([A-Za-z])(\d)", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"(\d)([A-Za-z])", "$1 $2");

        foreach (Match match in Regex.Matches(withBoundaries, @"[A-Za-z0-9]+"))
        {
            var token = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                yield return token.ToLowerInvariant();
            }
        }
    }

    private static string NormalizeEntityKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }
}
