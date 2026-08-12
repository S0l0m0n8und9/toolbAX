using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IODataClient"/> for design-mode + tests: echoes a plausible response per verb
/// without issuing HTTP (GET→200 with a sample value set, POST→201 Created, PATCH/DELETE→204 No
/// Content, empty POST/PATCH body→400).
/// <para>
/// A GET honours the request options a real service would not ignore: the caller's
/// <see cref="CancellationToken"/> is observed before anything else, and <c>$top</c> caps the rows
/// served. Server-driven paging is opt-in via <see cref="FakeODataClient(int)"/> — without it the fake
/// answers every GET in one page, exactly as it always has, so design mode is unchanged.
/// </para>
/// </summary>
public sealed class FakeODataClient : IODataClient
{
    // A small CustomersV3-shaped sample set so Query Builder can project any selected subset.
    private const string SampleValue = """
        {"value":[
          {"dataAreaId":"USMF","CustomerAccount":"US-001","OrganizationName":"Contoso Retail","CustomerGroupId":"10","CurrencyCode":"USD","PaymentTermsName":"Net30","CreditLimit":50000,"IsOneTime":"No","PrimaryContactEmail":"ar@contoso.com"},
          {"dataAreaId":"USMF","CustomerAccount":"US-002","OrganizationName":"Fabrikam Wholesale","CustomerGroupId":"20","CurrencyCode":"USD","PaymentTermsName":"Net15","CreditLimit":120000,"IsOneTime":"No","PrimaryContactEmail":"billing@fabrikam.com"},
          {"dataAreaId":"USMF","CustomerAccount":"US-003","OrganizationName":"Northwind Traders","CustomerGroupId":"10","CurrencyCode":"EUR","PaymentTermsName":"Net30","CreditLimit":0,"IsOneTime":"Yes","PrimaryContactEmail":"hello@northwind.eu"}
        ]}
        """;

    /// <summary>The seeded rows as raw JSON, so a synthesised page reuses the exact seeded shape.</summary>
    private static readonly Lazy<IReadOnlyList<string>> SampleRows = new(ParseSampleRows);

    /// <summary>Origin the fake stamps on the next-page links it issues (never dialled).</summary>
    private const string NextLinkBase = "https://fake.local/data/CustomersV3";

    /// <summary>Rows per GET response; 0 = unpaged (one response, the historical default).</summary>
    private readonly int _pageSize;

    /// <summary>Unpaged seeded fake: every GET answers in a single page. Design-mode default.</summary>
    public FakeODataClient()
    {
    }

    /// <summary>
    /// Opt-in server-driven paging: a GET answers with at most <paramref name="pageSize"/> of the seeded
    /// rows and, while rows remain, an absolute <c>@odata.nextLink</c> the caller is expected to follow
    /// verbatim (as <c>CoreODataClient</c> does). Lets paging be exercised through the standard fake
    /// instead of a test-local stub.
    /// </summary>
    public FakeODataClient(int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "A page size must be positive.");
        }

        _pageSize = pageSize;
    }

    public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
    {
        // A real client observes the token before it does any work; a fake that ignores it makes every
        // cancellation path untestable through the standard seam.
        ct.ThrowIfCancellationRequested();

        var verb = (method ?? string.Empty).Trim().ToUpperInvariant();

        if (verb is "POST" or "PATCH" && string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult(new ODataResponse(400, "Bad Request",
                "{\"error\":{\"message\":\"A request body is required.\"}}", 12));
        }

        return Task.FromResult(verb switch
        {
            "GET" => Get(path),
            "POST" => new ODataResponse(201, "Created", body ?? "{}", 142),
            "PATCH" => new ODataResponse(204, "No Content", string.Empty, 96),
            "DELETE" => new ODataResponse(204, "No Content", string.Empty, 88),
            _ => new ODataResponse(405, "Method Not Allowed", string.Empty, 8),
        });
    }

    private ODataResponse Get(string path)
    {
        var rows = SampleRows.Value;
        var top = ReadIntOption(path, "$top");
        var available = top is { } requested ? Math.Min(requested, rows.Count) : rows.Count;

        // Unconstrained + unpaged: hand back the seed verbatim, so the long-standing design-mode body
        // (and anything asserting on it) is byte-for-byte unchanged.
        if (_pageSize == 0 && top is null)
        {
            return new ODataResponse(200, "OK", SampleValue, 312);
        }

        if (_pageSize == 0)
        {
            return new ODataResponse(200, "OK", Page(rows.Take(available), nextLink: null), 312);
        }

        // $skiptoken is the cursor this fake itself issued on the previous page's nextLink.
        var skipped = Math.Clamp(ReadIntOption(path, "$skiptoken") ?? 0, 0, available);
        var page = rows.Skip(skipped).Take(Math.Min(_pageSize, available - skipped)).ToList();
        var served = skipped + page.Count;
        var next = served < available ? NextPageLink(served, top) : null;
        return new ODataResponse(200, "OK", Page(page, next), 312);
    }

    private static string NextPageLink(int served, int? top)
    {
        // $top is carried forward: without it the next page would re-derive the total from the full seed
        // and serve rows the caller's $top excluded.
        var query = $"?$skiptoken={served.ToString(CultureInfo.InvariantCulture)}";
        return top is { } carried
            ? $"{NextLinkBase}{query}&$top={carried.ToString(CultureInfo.InvariantCulture)}"
            : $"{NextLinkBase}{query}";
    }

    private static string Page(IEnumerable<string> rows, string? nextLink)
    {
        var sb = new StringBuilder("{");
        if (nextLink is not null)
        {
            sb.Append("\"@odata.nextLink\":").Append(JsonSerializer.Serialize(nextLink)).Append(',');
        }

        return sb.Append("\"value\":[").Append(string.Join(",", rows)).Append("]}").ToString();
    }

    // Reads a positive integer query option (e.g. $top) out of a path or an absolute nextLink URL.
    private static int? ReadIntOption(string? pathOrUrl, string name)
    {
        if (string.IsNullOrEmpty(pathOrUrl))
        {
            return null;
        }

        var query = pathOrUrl.IndexOf('?');
        if (query < 0)
        {
            return null;
        }

        foreach (var pair in pathOrUrl[(query + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (!string.Equals(Uri.UnescapeDataString(pair[..eq]), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A non-numeric or negative value is treated as absent, the way a server would reject it
            // rather than silently truncate the result set.
            if (int.TryParse(Uri.UnescapeDataString(pair[(eq + 1)..]), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ParseSampleRows()
    {
        using var doc = JsonDocument.Parse(SampleValue);
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(row => row.GetRawText())
            .ToList();
    }
}
