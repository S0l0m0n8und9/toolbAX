using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IODataClient"/> for design-mode + tests: echoes a plausible response per verb
/// without issuing HTTP (GET→200 with a sample value set, POST→201 Created, PATCH/DELETE→204 No
/// Content, empty POST/PATCH body→400).
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

    public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
    {
        var verb = (method ?? string.Empty).Trim().ToUpperInvariant();

        if (verb is "POST" or "PATCH" && string.IsNullOrWhiteSpace(body))
        {
            return Task.FromResult(new ODataResponse(400, "Bad Request",
                "{\"error\":{\"message\":\"A request body is required.\"}}", 12));
        }

        return Task.FromResult(verb switch
        {
            "GET" => new ODataResponse(200, "OK", SampleValue, 312),
            "POST" => new ODataResponse(201, "Created", body ?? "{}", 142),
            "PATCH" => new ODataResponse(204, "No Content", string.Empty, 96),
            "DELETE" => new ODataResponse(204, "No Content", string.Empty, 88),
            _ => new ODataResponse(405, "Method Not Allowed", string.Empty, 8),
        });
    }
}
