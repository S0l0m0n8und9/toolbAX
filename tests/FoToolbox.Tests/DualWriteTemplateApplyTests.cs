using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class TemplateSelectorTests
{
    private static DualWriteTemplate T(string version, string author) =>
        new($"t-{version}-{author}", version, author);

    [Trait("Category", "DualWrite")]
    [Fact]
    public void SelectLatest_NoAuthorFilter_PicksHighestVersion()
    {
        var templates = new[] { T("1.0.0", "MS"), T("1.0.2", "Contoso"), T("1.0.1", "MS") };
        var latest = TemplateSelector.SelectLatest(templates);
        Assert.Equal("1.0.2", latest!.Version);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void SelectLatest_AuthorFilter_RestrictsToThatAuthor()
    {
        var templates = new[] { T("1.0.0", "MS"), T("2.0.0", "Contoso"), T("1.5.0", "MS") };
        var latest = TemplateSelector.SelectLatest(templates, new[] { "MS" });
        Assert.Equal("1.5.0", latest!.Version);
        Assert.Equal("MS", latest.Author);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void SelectLatest_AnyAuthorToken_DisablesFiltering()
    {
        var templates = new[] { T("1.0.0", "MS"), T("2.0.0", "Contoso") };
        var latest = TemplateSelector.SelectLatest(templates, new[] { "ANY" });
        Assert.Equal("2.0.0", latest!.Version);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void SelectLatest_NoMatchingAuthor_ReturnsNull()
    {
        var templates = new[] { T("1.0.0", "MS") };
        Assert.Null(TemplateSelector.SelectLatest(templates, new[] { "Nobody" }));
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void SelectLatest_EmptyList_ReturnsNull()
    {
        Assert.Null(TemplateSelector.SelectLatest(Array.Empty<DualWriteTemplate>()));
    }

    [Trait("Category", "DualWrite")]
    [Theory]
    [InlineData("MS, Contoso", 2)]
    [InlineData("  MS ;Contoso ", 2)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseAuthorFilter_SplitsTokens(string? raw, int expected)
    {
        Assert.Equal(expected, TemplateSelector.ParseAuthorFilter(raw).Count);
    }
}

public class DualWriteSwitchActiveTests
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

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SwitchActive_BuildsExpectedRequest_AndSendsRawTemplateId()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("\"req-7\"")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.example/") };
        var client = new DualWriteGatewayClient(http);

        var response = await client.SwitchActiveTemplateAsync("C123", "proj-1", "t-101", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://gw.example/api/DualWriteManagement/1.0/SolutionAware/C123/SwitchActive/t-101?pid=proj-1",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("t-101", handler.LastBody);
        Assert.Equal("req-7", response.RequestId);
    }
}
