using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteConnectionSetTests
{
    private const string SampleJson = """
    {
      "name": "contoso-connset",
      "environments": {
        "ce-prod": {
          "name": "ce-prod",
          "targetType": "CRM",
          "environmentDisplayName": "Contoso CE",
          "powerAppsEnvironment": "pae-ce",
          "isDevInstance": false,
          "directUrl": "https://contoso.crm.dynamics.com",
          "schemas": [
            { "name": "customers", "keys": [ { "name": "USERKEYS", "displayName": "Acct Key", "fields": ["accountnumber"] } ] }
          ]
        },
        "fo-prod": {
          "name": "fo-prod",
          "targetType": "AX",
          "environmentDisplayName": "Contoso FO",
          "powerAppsEnvironment": "pae-ce",
          "isDevInstance": false,
          "directUrl": "https://contoso.operations.dynamics.com",
          "schemas": []
        }
      },
      "dualWriteDetail": {
        "legalEntityMappings": {
          "mappings": [
            { "left": { "name": "USMF", "id": "le-usmf" }, "right": { "name": "USMF OU", "id": "ou" } },
            { "left": { "name": "DEMF", "id": "le-demf" }, "right": { "name": "DEMF OU", "id": "ou2" } }
          ]
        }
      }
    }
    """;

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Parse_ResolvesEnvironments_LegalEntities_AndSchemas()
    {
        var set = DualWriteConnectionSetParser.Parse(SampleJson);

        Assert.Equal("contoso-connset", set.Name);
        Assert.Equal(2, set.Environments.Count);
        Assert.Equal(new[] { "USMF", "DEMF" }, set.LegalEntities.ToArray());

        Assert.NotNull(set.CeEnvironment);
        Assert.Equal("ce-prod", set.CeEnvironment!.Name);
        Assert.True(set.CeEnvironment.IsCe);
        Assert.Equal("fo-prod", set.FoEnvironment!.Name);
        Assert.True(set.FoEnvironment.IsFo);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void GetIntegrationKey_FindsUserKeysForEntity()
    {
        var set = DualWriteConnectionSetParser.Parse(SampleJson);
        var key = set.GetIntegrationKey("customers");
        Assert.NotNull(key);
        Assert.Equal("USERKEYS", key!.Name);
        Assert.Equal(new[] { "accountnumber" }, key.Fields.ToArray());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void ResetLinkPayload_HasCeThenFoEnvironments_AndLegalEntities()
    {
        var set = DualWriteConnectionSetParser.Parse(SampleJson);

        var json = ResetLinkPayloadBuilder.Build(set, set.LegalEntities);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("pae-ce", root.GetProperty("powerAppsEnvironmentName").GetString());

        var envs = root.GetProperty("environments");
        Assert.Equal(2, envs.GetArrayLength());
        Assert.Equal("CRM", envs[0].GetProperty("targetType").GetString());
        Assert.Equal("AX", envs[1].GetProperty("targetType").GetString());
        // MS quirk: id is the powerApps environment id.
        Assert.Equal("pae-ce", envs[0].GetProperty("id").GetString());
        Assert.Equal("https://contoso.crm.dynamics.com", envs[0].GetProperty("directUrl").GetString());

        Assert.Equal(new[] { "USMF", "DEMF" },
            root.GetProperty("legalEntities").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    // #166: the alias list is a priority order, not a set. Matching in document order made the winner an
    // accident of the gateway's serialization — the same defect as the cid/id mix-up in the response parser.
    [Trait("Category", "DualWrite")]
    [Fact]
    public void Parse_PrefersEnvironmentDisplayName_OverAnEarlierConnectionDisplayName()
    {
        const string json = """
        {
          "name": "cs",
          "environments": {
            "ce-prod": {
              "name": "ce-prod",
              "targetType": "CRM",
              "connectionDisplayName": "the connection",
              "environmentDisplayName": "the environment",
              "schemas": []
            }
          }
        }
        """;

        var set = DualWriteConnectionSetParser.Parse(json);

        Assert.Equal("the environment", Assert.Single(set.Environments).DisplayName);
    }
}

public class DualWriteResetClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public CapturingHandler(HttpResponseMessage response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _response;
        }
    }

    private static (DualWriteGatewayClient Client, CapturingHandler Handler) Make(string responseJson)
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.example/") };
        return (new DualWriteGatewayClient(http), handler);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetConnectionSet_BuildsHostRootRequest()
    {
        var (client, handler) = Make("{\"name\":\"cs\",\"environments\":{}}");

        await client.GetConnectionSetAsync("contoso-link", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("https://gw.example/api/ConnectionSet/contoso-link", handler.LastRequest!.RequestUri!.ToString());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ResetLinks_PostsToResetPath_WithForceResetQuery_AndPayload()
    {
        var (client, handler) = Make("{}");
        var set = new DualWriteConnectionSet(
            "cs",
            new[]
            {
                new DualWriteConnectionSetEnvironment("ce", "CE", "pae", false, "CRM", "https://ce", Array.Empty<DualWriteSchema>())
            },
            new[] { "USMF" });

        await client.ResetLinksAsync("C123", set, new[] { "USMF" }, forceReset: true, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://gw.example/api/ConnectionSet/C123/Reset?targetType=AX&forceReset=true",
            handler.LastRequest!.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("USMF", doc.RootElement.GetProperty("legalEntities")[0].GetString());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task ApplyIntegrationKeys_PostsKeyedByEntity_ToDatasetPath()
    {
        var (client, handler) = Make("{}");

        await client.ApplyIntegrationKeysAsync("ce-prod", "Customers V3", new[] { "CustomerAccount", "dataAreaId" }, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://gw.example/api/dataset/ce-prod/IntegrationKeys", handler.LastRequest!.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("ce-prod", doc.RootElement.GetProperty("datasetName").GetString());
        var fields = doc.RootElement.GetProperty("integrationKeys").GetProperty("Customers V3");
        Assert.Equal(new[] { "CustomerAccount", "dataAreaId" }, fields.EnumerateArray().Select(e => e.GetString()).ToArray());
    }
}
