using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FoToolbox.Tests;

internal sealed class FakeODataServer : IAsyncDisposable
{
    private readonly TestServer _server;
    public HttpClient Client { get; }
    public Uri BaseUri => Client.BaseAddress ?? new Uri("http://localhost");

    private FakeODataServer(TestServer server, HttpClient client)
    {
        _server = server;
        Client = client;
    }

    public static FakeODataServer Create(string metadataXml)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services => services.AddRouting())
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/data/$metadata", async context =>
                    {
                        context.Response.ContentType = "application/xml";
                        await context.Response.WriteAsync(metadataXml);
                    });

                    endpoints.MapGet("/data/{entity}", async context =>
                    {
                        var entity = context.Request.RouteValues["entity"]?.ToString() ?? "Entity";
                        var page = 1;
                        if (int.TryParse(context.Request.Query["page"], out var parsed))
                        {
                            page = parsed;
                        }

                        var rows = new List<Dictionary<string, object?>>
                        {
                            new()
                            {
                                ["Id"] = page,
                                ["Name"] = $"{entity}-Row-{page}"
                            }
                        };

                        var nextLink = page < 2
                            ? $"/data/{entity}?page=2"
                            : null;

                        var payload = new Dictionary<string, object?>
                        {
                            ["value"] = rows,
                            ["@odata.nextLink"] = nextLink
                        };

                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                    });

                    endpoints.MapPost("/data/{entity}", async context =>
                    {
                        // Minimal write endpoint used by HttpODataWriteClient tests.
                        var contentType = context.Request.ContentType ?? string.Empty;
                        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.StatusCode = 415;
                            await context.Response.WriteAsync("Expected application/json");
                            return;
                        }

                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();

                        context.Response.StatusCode = 201;
                        context.Response.ContentType = "application/json";
                        context.Response.Headers["X-Echo-Method"] = "POST";
                        await context.Response.WriteAsync(body);
                    });
                });
            });

        var server = new TestServer(builder);
        var client = server.CreateClient();
        client.BaseAddress = new Uri("http://localhost/");
        return new FakeODataServer(server, client);
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        _server.Dispose();
        return ValueTask.CompletedTask;
    }
}
