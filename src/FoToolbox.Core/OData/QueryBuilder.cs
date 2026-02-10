using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FoToolbox.Core.OData;

public static class QueryBuilder
{
    private static string QuoteStringLiteral(string value)
    {
        // OData string literals are single-quoted and escape a single quote by doubling it.
        var escaped = (value ?? string.Empty).Replace("'", "''");
        return $"'{escaped}'";
    }

    public static QueryRequest Build(string baseUrl, QuerySpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Entity))
        {
            throw new ArgumentException("Entity is required", nameof(spec));
        }

        var parameters = new List<string>();

        if (spec.Select is { Count: > 0 })
        {
            parameters.Add($"$select={string.Join(",", spec.Select)}");
        }

        var filterParam = BuildFilter(spec);
        if (!string.IsNullOrWhiteSpace(filterParam))
        {
            parameters.Add($"$filter={Uri.EscapeDataString(filterParam)}");
        }

        if (!string.IsNullOrWhiteSpace(spec.OrderBy))
        {
            parameters.Add($"$orderby={Uri.EscapeDataString(spec.OrderBy)}");
        }

        if (spec.Top is not null) parameters.Add($"$top={spec.Top}");
        if (spec.Skip is not null) parameters.Add($"$skip={spec.Skip}");
        if (spec.Count) parameters.Add("$count=true");

        if (!string.IsNullOrWhiteSpace(spec.Expand))
        {
            parameters.Add($"$expand={Uri.EscapeDataString(spec.Expand)}");
        }

        if (spec.CrossCompany)
        {
            parameters.Add("cross-company=true");
        }

        var sb = new StringBuilder();
        sb.Append(baseUrl.TrimEnd('/'));
        sb.Append("/data/");
        sb.Append(spec.Entity);
        if (parameters.Any())
        {
            sb.Append('?');
            sb.Append(string.Join("&", parameters));
        }

        return new QueryRequest(sb.ToString());
    }

    private static string? BuildFilter(QuerySpec spec)
    {
        string? filter = null;

        if (!string.IsNullOrWhiteSpace(spec.Filter))
        {
            filter = spec.Filter;
        }
        else if (spec.Where is not null)
        {
            filter = RenderFilter(spec.Where);
        }

        if (!spec.CrossCompany && !string.IsNullOrWhiteSpace(spec.Company))
        {
            var companyClause = $"dataAreaId eq {QuoteStringLiteral(spec.Company)}";
            filter = string.IsNullOrWhiteSpace(filter) ? companyClause : $"({companyClause}) and ({filter})";
        }

        return filter;
    }

    private static string RenderFilter(FilterNode node)
    {
        return node switch
        {
            FilterCondition cond => RenderCondition(cond),
            FilterGroup group => $"({string.Join($" {group.LogicalOperator} ", group.Children.Select(RenderFilter))})",
            _ => throw new ArgumentOutOfRangeException(nameof(node), "Unknown filter node.")
        };
    }

    private static string RenderCondition(FilterCondition cond)
    {
        if (cond.Operator is "startswith" or "endswith" or "contains")
        {
            return $"{cond.Operator}({cond.Field},{cond.Value})";
        }

        return $"{cond.Field} {cond.Operator} {cond.Value}";
    }
}
