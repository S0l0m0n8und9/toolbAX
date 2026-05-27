using System;
using System.Collections.Generic;
using System.ComponentModel;
using FoToolbox.Host.ViewModels;
using FoToolbox.SDK.Plugins;
using Xunit;

namespace FoToolbox.Tests;

public class AppShellViewModelTests
{
    private sealed class StubBusy : IPluginBusyState
    {
        private bool _busy;
        public bool IsBusy
        {
            get => _busy;
            set
            {
                if (_busy == value) return;
                _busy = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void IsBusy_DefaultsToFalse()
    {
        var shell = new AppShellViewModel();
        Assert.False(shell.IsBusy);
    }

    [Fact]
    public void IsBusy_TrueWhenAnyRegisteredPluginBusy()
    {
        var shell = new AppShellViewModel();
        var a = new StubBusy();
        var b = new StubBusy();
        shell.RegisterPluginBusy(a);
        shell.RegisterPluginBusy(b);

        a.IsBusy = true;

        Assert.True(shell.IsBusy);
    }

    [Fact]
    public void IsBusy_FalseWhenAllPluginsIdle()
    {
        var shell = new AppShellViewModel();
        var a = new StubBusy { IsBusy = true };
        shell.RegisterPluginBusy(a);
        Assert.True(shell.IsBusy);

        a.IsBusy = false;

        Assert.False(shell.IsBusy);
    }

    [Fact]
    public void IsBusy_RaisesPropertyChangedOnTransition()
    {
        var shell = new AppShellViewModel();
        var stub = new StubBusy();
        shell.RegisterPluginBusy(stub);

        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        stub.IsBusy = true;
        stub.IsBusy = true; // no-op
        stub.IsBusy = false;

        Assert.Equal(new[] { nameof(AppShellViewModel.IsBusy), nameof(AppShellViewModel.IsBusy) }, raised);
    }

    [Fact]
    public void OnConnectionTested_SuccessSetsOkAndTimestamp()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile(envId: "PROD", name: "PROD-NZ");

        var when = DateTimeOffset.UtcNow;
        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "PROD",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = true,
            TestedAt = when,
        });

        Assert.Equal(ConnectionStatus.Ok, shell.ConnectionStatus);
        Assert.Equal(when, shell.LastPingAt);
    }

    [Fact]
    public void OnConnectionTested_FailureSetsError()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile(envId: "PROD", name: "PROD-NZ");

        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "PROD",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = false,
            TestedAt = DateTimeOffset.UtcNow,
            Detail = "401",
        });

        Assert.Equal(ConnectionStatus.Error, shell.ConnectionStatus);
    }

    [Fact]
    public void OnConnectionTested_IgnoresEventsForInactiveProfile()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile(envId: "PROD", name: "PROD-NZ");

        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "DEV",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = true,
            TestedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(ConnectionStatus.Unknown, shell.ConnectionStatus);
        Assert.Null(shell.LastPingAt);
    }

    [Fact]
    public void SetActiveProfile_NullClearsConnectionState()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile("PROD", "PROD-NZ");
        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "PROD",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = true,
            TestedAt = DateTimeOffset.UtcNow,
        });

        shell.SetActiveProfile(null, null);

        Assert.Null(shell.ActiveProfileName);
        Assert.Equal(ConnectionStatus.Unknown, shell.ConnectionStatus);
        Assert.Null(shell.LastPingAt);
    }
}
