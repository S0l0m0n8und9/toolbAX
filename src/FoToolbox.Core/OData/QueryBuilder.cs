using FoToolbox.Core.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FoToolbox.Core.OData;

public static class QueryBuilder
{
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

        if (!string.IsNullOrWhiteSpace(spec.Filter))
        {
            parameters.Add($"$filter={Uri.EscapeDataString(spec.Filter)}");
        }
        else if (!spec.CrossCompany && !string.IsNullOrWhiteSpace(spec.Company))
        {
            parameters.Add($"$filter={Uri.EscapeDataString($"dataAreaId eq '{spec.Company}'")}");
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
}
