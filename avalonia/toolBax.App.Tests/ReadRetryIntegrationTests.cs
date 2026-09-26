using FoToolbox.Core.Net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

#pragma warning disable xUnit1051 // Tests intentionally use dedicated cancellation tokens to drive retry timing.
public sealed class ReadRetryIntegrationTests
{
    private static EnvProfile Env(string id = "env", string url = "https://fo.example") => new(
        id, id, url, "tenant", "USMF", "Tier 2", EnvStatus.Connected,
        DataverseUrl: "https://dv.example");

    private static ReadRetryPolicy ImmediatePolicy() => new(initialBackoff: TimeSpan.Zero);

    [Fact]
    public async Task CoreOData_GET_retries_503_with_one_auth_and_snapshotted_headers()
    {
        var suppliedHeaders = new Dictionary<string, string> { ["If-None-Match"] = "\"original\"" };
        var handler = new SequenceHandler(
            _ =>
            {
                suppliedHeaders["If-None-Match"] = "\"changed\"";
                return Response(HttpStatusCode.ServiceUnavailable, "temporary");
            },
            _ => Response(HttpStatusCode.OK, "{\"value\":[]}"));
        var auth = new Auth();
        using var http = new HttpClient(handler);
        using var client = new CoreODataClient(auth, () => Env(), http, ImmediatePolicy());

        var response = await client.SendAsync("GET", "/data/Rows", null, suppliedHeaders);

        Assert.True(response.IsSuccess);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, auth.FoCalls);
        Assert.All(handler.Requests, request => Assert.Equal("\"original\"", request.IfNoneMatch));
    }

    [Fact]
    public async Task CoreOData_GET_exhausts_three_attempts()
    {
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        using var client = new CoreODataClient(new Auth(), () => Env(), new HttpClient(handler), ImmediatePolicy());

        var response = await client.SendAsync("GET", "/data/Rows", null);

        Assert.Equal(503, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task CoreOData_environment_change_after_first_attempt_prevents_second_send()
    {
        var active = Env();
        var handler = new SequenceHandler(_ =>
        {
            active = Env("other", "https://other.example");
            return Response(HttpStatusCode.ServiceUnavailable, "temporary");
        });
        using var client = new CoreODataClient(new Auth(), () => active, new HttpClient(handler), ImmediatePolicy());

        var response = await client.SendAsync("GET", "/data/Rows", null);

        Assert.Equal("Environment changed", response.ReasonPhrase);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("POST", 429)]
    [InlineData("PATCH", 503)]
    [InlineData("PUT", 429)]
    [InlineData("DELETE", 503)]
    public async Task CoreOData_mutations_keep_one_logical_dispatch(string method, int status)
    {
        var handler = new SequenceHandler(_ => Response((HttpStatusCode)status, "mutation response"));
        using var client = new CoreODataClient(new Auth(), () => Env(), new HttpClient(handler), ImmediatePolicy());

        _ = await client.SendAsync(method, "/data/Rows(1)", "{\"Name\":\"kept\"}");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(method, request.Method.Method);
        Assert.Equal(method == "DELETE" ? string.Empty : "{\"Name\":\"kept\"}", request.Body);
    }

    [Fact]
    public async Task CoreOData_OPTIONS_buffers_content_with_the_HttpClient_deadline_token()
    {
        var content = new SerializationProbeContent();
        var handler = new SequenceHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = new CoreODataClient(new Auth(), () => Env(),
            new HttpClient(handler), ImmediatePolicy());

        _ = await client.SendAsync("OPTIONS", "/data/Rows", null);

        Assert.True(content.SerializationStarted);
        Assert.True(content.SerializationToken.CanBeCanceled);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CoreOData_HEAD_keeps_single_dispatch_and_framework_no_body_semantics()
    {
        var content = new SerializationProbeContent();
        var handler = new SequenceHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = new CoreODataClient(new Auth(), () => Env(),
            new HttpClient(handler), ImmediatePolicy());

        _ = await client.SendAsync("HEAD", "/data/Rows", null);

        Assert.True(content.SerializationStarted);
        Assert.False(content.SerializationToken.CanBeCanceled);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ResponseHeadersRead_negative_control_serializes_later_with_caller_token()
    {
        var content = new SerializationProbeContent();
        var handler = new SequenceHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var http = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Head, "https://fo.example/data/Rows");

        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            CancellationToken.None);

        Assert.False(content.SerializationStarted);
        _ = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.True(content.SerializationStarted);
        Assert.False(content.SerializationToken.CanBeCanceled);
    }

    [Fact]
    public async Task CoreDataverse_retries_429_and_preserves_headers()
    {
        var handler = new SequenceHandler(
            _ => Response((HttpStatusCode)429, "throttled"),
            _ => Response(HttpStatusCode.OK, "{\"value\":[]}"));
        var auth = new Auth();
        using var client = new CoreDataverseClient(auth, () => Env(), new HttpClient(handler), ImmediatePolicy());

        var response = await client.GetAsync("accounts");

        Assert.True(response.IsSuccess);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, auth.DataverseCalls);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Authorization?.Scheme);
            Assert.Contains("FormattedValue", request.Prefer);
        });
    }

    [Fact]
    public async Task CoreDataverse_exhausts_three_attempts()
    {
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        using var client = new CoreDataverseClient(new Auth(), () => Env(), new HttpClient(handler), ImmediatePolicy());

        var response = await client.GetAsync("accounts");

        Assert.Equal(503, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task CoreDataverse_environment_change_after_first_attempt_prevents_second_send()
    {
        var active = Env();
        var handler = new SequenceHandler(_ =>
        {
            active = Env("other", "https://other.example");
            return Response(HttpStatusCode.ServiceUnavailable, "temporary");
        });
        using var client = new CoreDataverseClient(new Auth(), () => active,
            new HttpClient(handler), ImmediatePolicy());

        var response = await client.GetAsync("accounts");

        Assert.Equal("Environment changed", response.ReasonPhrase);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CoreDataverse_returns_final_429_when_valid_RetryAfter_exceeds_budget()
    {
        var handler = new SequenceHandler(_ => Response(
            (HttpStatusCode)429,
            "throttled",
            retryAfter: TimeSpan.FromSeconds(31)));
        using var client = new CoreDataverseClient(new Auth(), () => Env(),
            new HttpClient(handler), ImmediatePolicy());

        var response = await client.GetAsync("accounts");

        Assert.Equal(429, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CoreDataverse_body_buffer_timeout_returns_promptly_and_disposes_late_response()
    {
        var content = new GatedContent();
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var policy = new ReadRetryPolicy(overallBudget: TimeSpan.FromMilliseconds(500));
        using var client = new CoreDataverseClient(new Auth(), () => Env(), new HttpClient(handler), policy);

        var elapsed = Stopwatch.StartNew();
        var pending = client.GetAsync("accounts");
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        elapsed.Stop();

        Assert.Equal(0, response.StatusCode);
        Assert.Contains("budget", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(2));
        content.Release.TrySetResult();
        await WaitForAsync(() => content.Disposed);
    }

    [Fact]
    public async Task Connection_probe_retries_503_with_one_force_refresh_auth()
    {
        var handler = new SequenceHandler(
            _ => Response(HttpStatusCode.ServiceUnavailable, "temporary"),
            _ => Response(HttpStatusCode.OK, "metadata"));
        var auth = new Auth();
        var tester = new CoreConnectionTester(auth, new HttpClient(handler), ImmediatePolicy());

        var result = await tester.TestFoAsync(Env());

        Assert.True(result.Success);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, auth.FoForceRefreshCalls);
    }

    [Fact]
    public async Task Connection_probe_exhausts_three_attempts()
    {
        var handler = new SequenceHandler(_ => Response(HttpStatusCode.ServiceUnavailable, "temporary"));
        var tester = new CoreConnectionTester(new Auth(), new HttpClient(handler), ImmediatePolicy());

        var result = await tester.TestDataverseAsync(Env());

        Assert.False(result.Success);
        Assert.Contains("503", result.Message);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Connection_probe_cancellation_during_retry_wait_prevents_second_send()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new SequenceHandler(_ =>
        {
            first.TrySetResult();
            return Response(HttpStatusCode.ServiceUnavailable, "temporary");
        });
        using var cancellation = new CancellationTokenSource();
        var policy = new ReadRetryPolicy(initialBackoff: TimeSpan.FromSeconds(10));
        var tester = new CoreConnectionTester(new Auth(), new HttpClient(handler), policy);

        var pending = tester.TestFoAsync(Env(), cancellation.Token);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, handler.Calls);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string body,
        TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (retryAfter is not null)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        return response;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class Auth : IAuthService
    {
        public int FoCalls { get; private set; }
        public int DataverseCalls { get; private set; }
        public int FoForceRefreshCalls { get; private set; }

        public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default)
        {
            FoCalls++;
            return Task.FromResult("fo-token");
        }

        public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default)
        {
            DataverseCalls++;
            return Task.FromResult("dv-token");
        }

        public Task<string> AcquireDualWriteTokenAsync(EnvProfile env, CancellationToken ct = default) =>
            Task.FromResult("dw-token");

        public Task<string> AcquireFoTokenAsync(
            EnvProfile env, bool forceRefresh, CancellationToken ct = default)
        {
            FoForceRefreshCalls++;
            return Task.FromResult("fo-token");
        }

        public Task<string> AcquireDataverseTokenAsync(
            EnvProfile env, bool forceRefresh, CancellationToken ct = default)
        {
            DataverseCalls++;
            return Task.FromResult("dv-token");
        }
    }

    private sealed class SequenceHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(new CapturedRequest(
                request.Method,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization,
                string.Join(",", request.Headers.TryGetValues("Prefer", out var prefer)
                    ? prefer
                    : Array.Empty<string>()),
                request.Headers.IfNoneMatch.SingleOrDefault()?.Tag));
            return responses[Math.Min(_index++, responses.Length - 1)](request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Body,
        AuthenticationHeaderValue? Authorization,
        string Prefer,
        string? IfNoneMatch);

    private sealed class GatedContent : HttpContent
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Started.TrySetResult();
            await Release.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class SerializationProbeContent : HttpContent
    {
        public bool SerializationStarted { get; private set; }
        public CancellationToken SerializationToken { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            SerializationStarted = true;
            return Task.CompletedTask;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            SerializationStarted = true;
            SerializationToken = cancellationToken;
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }
    }
}
