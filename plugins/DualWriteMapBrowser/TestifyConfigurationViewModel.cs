using FoToolbox.SDK.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteMapBrowserPlugin;

internal sealed class TestifyConfigurationViewModel : INotifyPropertyChanged
{
    private readonly TestifyConfigurationStore _store;
    private readonly string _envId;
    private readonly string _mapId;
    private readonly Action? _onClose;
    private TestifyMapConfiguration? _configuration;

    private HashSet<string> _omitCreateFields = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _preferredCreateValues = new(StringComparer.OrdinalIgnoreCase);
    private int _cePollTimeoutMinutes = 5;
    private bool _allowPartialEnumCoverage = false;
    private bool _isSaving;
    private string _omitCreateFieldsText = string.Empty;
    private string _preferredCreateValuesText = string.Empty;
    private string _cePollTimeoutMinutesText = "5";
    private string _confirmationMessage = string.Empty;

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
            _confirmationMessage = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConfirmationMessage));
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

    public string OmitCreateFieldsText
    {
        get => _omitCreateFieldsText;
        set
        {
            if (string.Equals(_omitCreateFieldsText, value, StringComparison.Ordinal))
            {
                return;
            }

            _omitCreateFieldsText = value ?? string.Empty;
            _omitCreateFields = TestifySettingsTextSerializer.ParseLines(_omitCreateFieldsText);
            _confirmationMessage = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConfirmationMessage));
        }
    }

    public string PreferredCreateValuesText
    {
        get => _preferredCreateValuesText;
        set
        {
            if (string.Equals(_preferredCreateValuesText, value, StringComparison.Ordinal))
            {
                return;
            }

            _preferredCreateValuesText = value ?? string.Empty;
            try
            {
                _preferredCreateValues = TestifySettingsTextSerializer.ParseKeyValueLines(_preferredCreateValuesText);
            }
            catch (FormatException) { }
            _confirmationMessage = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConfirmationMessage));
        }
    }

    public string CePollTimeoutMinutesText
    {
        get => _cePollTimeoutMinutesText;
        set
        {
            if (string.Equals(_cePollTimeoutMinutesText, value, StringComparison.Ordinal))
            {
                return;
            }

            _cePollTimeoutMinutesText = value ?? string.Empty;
            if (int.TryParse(_cePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                _cePollTimeoutMinutes = parsed;
            }
            _confirmationMessage = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConfirmationMessage));
        }
    }

    public string ConfirmationMessage
    {
        get => _confirmationMessage;
        private set
        {
            if (string.Equals(_confirmationMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _confirmationMessage = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }

    public TestifyConfigurationViewModel(TestifyConfigurationStore store, string envId, string mapId, Action<Exception> onError, Action? onClose = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _envId = envId ?? throw new ArgumentNullException(nameof(envId));
        _mapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
        _onClose = onClose;

        SaveCommand = new AsyncRelayCommand(SaveAsync, IsTimeoutValid, onError);
        CloseCommand = new RelayCommand(_ => _onClose?.Invoke());

        _ = LoadAsync(CancellationToken.None);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            _configuration = await _store.GetOrCreateAsync(_envId, _mapId, cancellationToken);

            _omitCreateFields = new HashSet<string>(_configuration.OmitCreateFields, StringComparer.OrdinalIgnoreCase);
            _preferredCreateValues = new Dictionary<string, string>(_configuration.PreferredCreateValues, StringComparer.OrdinalIgnoreCase);
            _cePollTimeoutMinutes = _configuration.CePollTimeoutMinutes;
            _allowPartialEnumCoverage = _configuration.AllowPartialEnumCoverage;
        }
        catch
        {
            _omitCreateFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _preferredCreateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _cePollTimeoutMinutes = 5;
            _allowPartialEnumCoverage = false;
        }

        _omitCreateFieldsText = TestifySettingsTextSerializer.FormatLines(_omitCreateFields);
        _preferredCreateValuesText = TestifySettingsTextSerializer.FormatKeyValueLines(_preferredCreateValues);
        _cePollTimeoutMinutesText = _cePollTimeoutMinutes.ToString(CultureInfo.InvariantCulture);

        OnPropertyChanged(nameof(OmitCreateFields));
        OnPropertyChanged(nameof(PreferredCreateValues));
        OnPropertyChanged(nameof(CePollTimeoutMinutes));
        OnPropertyChanged(nameof(AllowPartialEnumCoverage));
        OnPropertyChanged(nameof(OmitCreateFieldsText));
        OnPropertyChanged(nameof(PreferredCreateValuesText));
        OnPropertyChanged(nameof(CePollTimeoutMinutesText));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_configuration is null)
        {
            return;
        }

        Dictionary<string, string> preferredCreateValues;
        try
        {
            preferredCreateValues = TestifySettingsTextSerializer.ParseKeyValueLines(_preferredCreateValuesText);
        }
        catch (FormatException ex)
        {
            ConfirmationMessage = $"Error: {ex.Message}";
            return;
        }

        IsSaving = true;
        try
        {
            _configuration.OmitCreateFields = new HashSet<string>(_omitCreateFields, StringComparer.OrdinalIgnoreCase);
            _configuration.PreferredCreateValues = preferredCreateValues;
            _configuration.CePollTimeoutMinutes = _cePollTimeoutMinutes;
            _configuration.AllowPartialEnumCoverage = _allowPartialEnumCoverage;

            await _store.SaveAsync(_configuration, cancellationToken);
            ConfirmationMessage = "Settings saved.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool IsTimeoutValid() =>
        int.TryParse(_cePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
        && v >= 5 && v <= 300;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
