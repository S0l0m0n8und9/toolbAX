using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Services;
using ToolBax.Core.Models;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Dual-Write Operations screen (control-map §3) — read path. Connects to the live dual-write gateway
/// for the active environment (via <see cref="IDualWriteConnector"/>, which wraps the real
/// <c>FoToolbox.Core</c> gateway) and lists its maps with their current lifecycle state. Lifecycle
/// actions (start/stop/pause/resume/initial-sync) are layered on in a follow-up; this slice establishes
/// the connection + map list (the prototype's seeded gateway is gone).
/// </summary>
public partial class DualWriteOpsViewModel : ObservableObject
{
    private readonly IDualWriteConnector _connector;
    private readonly Func<EnvProfile?> _activeEnv;
    private DualWriteSession? _session;

    public ObservableCollection<MapRowViewModel> Maps { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool _isBusy;

    /// <summary>Resolved connection name (cname) once connected; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    private string? _connectionName;

    [ObservableProperty]
    private string _status = "Not connected.";

    /// <summary>A connect/load failure message for the error banner (null when fine).</summary>
    [ObservableProperty]
    private string? _loadError;

    public bool IsConnected => ConnectionName is not null;

    public bool HasMaps => Maps.Count > 0;

    public DualWriteOpsViewModel(IDualWriteConnector connector, Func<EnvProfile?> activeEnv)
    {
        _connector = connector;
        _activeEnv = activeEnv;
        Maps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMaps));
    }

    private bool CanLoad() => !IsBusy;

    /// <summary>Connects to the gateway for the active environment and loads its maps.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanLoad))]
    private async Task Load(CancellationToken ct)
    {
        var env = _activeEnv();
        if (env is null)
        {
            LoadError = "Select an environment first.";
            Status = "No active environment.";
            return;
        }

        IsBusy = true;
        LoadError = null;
        Status = "Connecting…";
        try
        {
            var session = await _connector.ConnectAsync(env, ct);
            DisposeSession();
            _session = session;
            ConnectionName = session.Cname;

            var maps = await session.Gateway.GetMapsAsync(session.Cid, ct);
            Maps.Clear();
            foreach (var map in maps)
            {
                Maps.Add(MapRowViewModel.From(map));
            }

            Status = Maps.Count == 0 ? "No maps on this connection." : $"{Maps.Count} map(s).";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            // Failed connect/load: surface the message and keep the screen in a disconnected state
            // rather than a stale half-loaded one.
            LoadError = ex.Message;
            Status = "Connection failed.";
            ConnectionName = null;
            Maps.Clear();
            DisposeSession();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DisposeSession()
    {
        if (_session?.Gateway is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _session = null;
    }
}
