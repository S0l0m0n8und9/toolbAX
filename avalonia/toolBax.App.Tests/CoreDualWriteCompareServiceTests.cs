using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

public class CoreDualWriteCompareServiceTests
{
    [AvaloniaFact]
    public async Task Sequential_sign_ins_resume_on_the_UI_thread_after_an_asynchronous_first_connect()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var connector = new UiThreadRecordingConnector();
        var service = new CoreDualWriteCompareService(connector);

        var compare = service.CompareAsync(EnvNamed("src"), EnvNamed("tgt"), CancellationToken.None);

        Assert.Equal(new[] { "start:src" }, connector.Log);
        Assert.False(compare.IsCompleted);

        await Task.Run(connector.CompleteFirstConnect);
        var rows = await compare;

        Assert.Equal(new[] { "start:src", "finish:src", "start:tgt", "finish:tgt" }, connector.Log);
        Assert.Equal(new[] { true, true }, connector.ConnectStartedOnUiThread);
        Assert.Equal(1, connector.MaxConcurrent);
        Assert.Equal(FakeDualWriteConnector.SeedMaps().Count, rows.Count);
    }

    private static EnvProfile EnvNamed(string id) =>
        new(id, id, $"https://{id}.operations.dynamics.com", "contoso.onmicrosoft.com", "USMF", "Tier 2",
            EnvStatus.Connected);

    private sealed class UiThreadRecordingConnector : IDualWriteConnector
    {
        private readonly TaskCompletionSource _firstConnect =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _log = new();
        private readonly List<bool> _connectStartedOnUiThread = new();
        private int _inFlight;

        public IReadOnlyList<string> Log
        {
            get { lock (_log) { return _log.ToArray(); } }
        }

        public IReadOnlyList<bool> ConnectStartedOnUiThread
        {
            get { lock (_log) { return _connectStartedOnUiThread.ToArray(); } }
        }

        public int MaxConcurrent { get; private set; }

        public void CompleteFirstConnect() => _firstConnect.TrySetResult();

        public async Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
        {
            lock (_log)
            {
                _log.Add($"start:{env.Id}");
                _connectStartedOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
                MaxConcurrent = Math.Max(MaxConcurrent, ++_inFlight);
            }

            if (env.Id == "src")
            {
                await _firstConnect.Task.ConfigureAwait(false);
            }

            lock (_log)
            {
                _inFlight--;
                _log.Add($"finish:{env.Id}");
            }

            return new DualWriteSession(
                new FakeCoreDualWriteGateway(FakeDualWriteConnector.SeedMaps()),
                "fake-cid", "Contoso", env.Id, "https://fake-gateway.dual-write.example");
        }
    }
}
