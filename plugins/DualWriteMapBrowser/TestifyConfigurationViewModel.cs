using FoToolbox.SDK.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteMapBrowserPlugin;

internal sealed class TestifyConfigurationViewModel : INotifyPropertyChanged
{
    private readonly TestifyConfigurationStore _store;
    private readonly string _envId;
    private readonly string _mapId;
    private TestifyMapConfiguration? _configuration;

    private HashSet<string> _omitCreateFields = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _preferredCreateValues = new(StringComparer.OrdinalIgnoreCase);
    private int _cePollTimeoutMinutes = 5;
    private bool _allowPartialEnumCoverage = false;
    private bool _isSaving;

    public event PropertyChangedEventHandler? PropertyChanged;

    public HashSet<string> OmitCreateFields
    {
        get => _omitCreateFields;
        set
        {
            if (ReferenceEquals(_omitCreateFields, value))
            {
                return;
            }

            _omitCreateFields = value ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged();
        }
    }

    public Dictionary<string, string> PreferredCreateValues
    {
        get => _preferredCreateValues;
        set
        {
            if (ReferenceEquals(_preferredCreateValues, value))
            {
                return;
            }

            _preferredCreateValues = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged();
        }
    }

    public int CePollTimeoutMinutes
    {
        get => _cePollTimeoutMinutes;
        set
        {
            if (_cePollTimeoutMinutes == value)
            {
                return;
            }

            _cePollTimeoutMinutes = value;
            OnPropertyChanged();
        }
    }

    public bool AllowPartialEnumCoverage
    {
        get => _allowPartialEnumCoverage;
        set
        {
            if (_allowPartialEnumCoverage == value)
            {
                return;
            }

            _allowPartialEnumCoverage = value;
            OnPropertyChanged();
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (_isSaving == value)
            {
                return;
            }

            _isSaving = value;
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand SaveCommand { get; }

    public TestifyConfigurationViewModel(TestifyConfigurationStore store, string envId, string mapId, Action<Exception> onError)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _envId = envId ?? throw new ArgumentNullException(nameof(envId));
        _mapId = mapId ?? throw new ArgumentNullException(nameof(mapId));

        SaveCommand = new AsyncRelayCommand(SaveAsync, onError);

        _ = LoadAsync(CancellationToken.None);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            _configuration = await _store.GetOrCreateAsync(_envId, _mapId, cancellationToken);

            OmitCreateFields = new HashSet<string>(_configuration.OmitCreateFields, StringComparer.OrdinalIgnoreCase);
            PreferredCreateValues = new Dictionary<string, string>(_configuration.PreferredCreateValues, StringComparer.OrdinalIgnoreCase);
            CePollTimeoutMinutes = _configuration.CePollTimeoutMinutes;
            AllowPartialEnumCoverage = _configuration.AllowPartialEnumCoverage;
        }
        catch
        {
            OmitCreateFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PreferredCreateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CePollTimeoutMinutes = 5;
            AllowPartialEnumCoverage = false;
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_configuration is null)
        {
            return;
        }

        IsSaving = true;
        try
        {
            _configuration.OmitCreateFields = new HashSet<string>(_omitCreateFields, StringComparer.OrdinalIgnoreCase);
            _configuration.PreferredCreateValues = new Dictionary<string, string>(_preferredCreateValues, StringComparer.OrdinalIgnoreCase);
            _configuration.CePollTimeoutMinutes = _cePollTimeoutMinutes;
            _configuration.AllowPartialEnumCoverage = _allowPartialEnumCoverage;

            await _store.SaveAsync(_configuration, cancellationToken);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
