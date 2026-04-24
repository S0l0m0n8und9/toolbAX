using FoToolbox.SDK.Commands;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteMapBrowserPlugin;

public sealed partial class DualWriteMapBrowserViewModel
{
    private bool _isTestifySettingsVisible;
    private bool _isLoadingTestifySettings;
    private bool _isSavingTestifySettings;
    private string _testifyOmitCreateFieldsText = string.Empty;
    private string _testifyPreferredCreateValuesText = string.Empty;
    private string _testifyCePollTimeoutMinutesText = "5";
    private bool _testifyAllowPartialEnumCoverage;

    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;

    public bool IsTestifySettingsVisible
    {
        get => _isTestifySettingsVisible;
        set
        {
            if (_isTestifySettingsVisible == value)
            {
                return;
            }

            _isTestifySettingsVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoadingTestifySettings
    {
        get => _isLoadingTestifySettings;
        private set
        {
            if (_isLoadingTestifySettings == value)
            {
                return;
            }

            _isLoadingTestifySettings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsSavingTestifySettings
    {
        get => _isSavingTestifySettings;
        private set
        {
            if (_isSavingTestifySettings == value)
            {
                return;
            }

            _isSavingTestifySettings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public string TestifyOmitCreateFieldsText
    {
        get => _testifyOmitCreateFieldsText;
        set
        {
            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
            {
                return;
            }

            _testifyOmitCreateFieldsText = value;
            OnPropertyChanged();
        }
    }

    public string TestifyPreferredCreateValuesText
    {
        get => _testifyPreferredCreateValuesText;
        set
        {
            if (string.Equals(_testifyPreferredCreateValuesText, value, StringComparison.Ordinal))
            {
                return;
            }

            _testifyPreferredCreateValuesText = value;
            OnPropertyChanged();
        }
    }

    public string TestifyCePollTimeoutMinutesText
    {
        get => _testifyCePollTimeoutMinutesText;
        set
        {
            if (string.Equals(_testifyCePollTimeoutMinutesText, value, StringComparison.Ordinal))
            {
                return;
            }

            _testifyCePollTimeoutMinutesText = value;
            OnPropertyChanged();
        }
    }

    public bool TestifyAllowPartialEnumCoverage
    {
        get => _testifyAllowPartialEnumCoverage;
        set
        {
            if (_testifyAllowPartialEnumCoverage == value)
            {
                return;
            }

            _testifyAllowPartialEnumCoverage = value;
            OnPropertyChanged();
        }
    }

    private void InitializeTestifySettingsCommands(Action<Exception> onError)
    {
        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
    }

    private void OnSelectedRecordChanged()
    {
        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
    }

    private void OpenTestifySettings()
    {
        if (SelectedRecord is null)
        {
            StatusMessage = "Select a dual-write map before opening Testify settings.";
            return;
        }

        IsTestifySettingsVisible = true;
        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
    }

    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
    {
        var record = SelectedRecord;
        if (record is null)
        {
            TestifyOmitCreateFieldsText = string.Empty;
            TestifyPreferredCreateValuesText = string.Empty;
            TestifyCePollTimeoutMinutesText = "5";
            TestifyAllowPartialEnumCoverage = false;
            return;
        }

        IsLoadingTestifySettings = true;
        try
        {
            var config = await _testifyConfigStore.GetOrCreateAsync(_ctx.CurrentEnv.Id, record.Id, cancellationToken);
            if (!string.Equals(SelectedRecord?.Id, record.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            TestifyOmitCreateFieldsText = TestifySettingsTextSerializer.FormatLines(config.OmitCreateFields);
            TestifyPreferredCreateValuesText = TestifySettingsTextSerializer.FormatKeyValueLines(config.PreferredCreateValues);
            TestifyCePollTimeoutMinutesText = config.CePollTimeoutMinutes.ToString(CultureInfo.InvariantCulture);
            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
        }
        finally
        {
            IsLoadingTestifySettings = false;
        }
    }

    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
    {
        var record = SelectedRecord;
        if (record is null)
        {
            StatusMessage = "Select a dual-write map before saving Testify settings.";
            return;
        }

        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
            timeoutMinutes <= 0)
        {
            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
            return;
        }

        var omitCreateFields = TestifySettingsTextSerializer.ParseLines(TestifyOmitCreateFieldsText);
        Dictionary<string, string> preferredCreateValues;
        try
        {
            preferredCreateValues = TestifySettingsTextSerializer.ParseKeyValueLines(TestifyPreferredCreateValuesText);
        }
        catch (FormatException ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        IsSavingTestifySettings = true;
        try
        {
            var config = await _testifyConfigStore.GetOrCreateAsync(_ctx.CurrentEnv.Id, record.Id, cancellationToken);
            config.OmitCreateFields = omitCreateFields;
            config.PreferredCreateValues = preferredCreateValues;
            config.CePollTimeoutMinutes = timeoutMinutes;
            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
            await _testifyConfigStore.SaveAsync(config, cancellationToken);

            if (_testifyPlans.TryGetValue(record.Id, out var plan))
            {
                plan.Configuration.OmitCreateFields = new HashSet<string>(omitCreateFields, StringComparer.OrdinalIgnoreCase);
                plan.Configuration.PreferredCreateValues = new Dictionary<string, string>(preferredCreateValues, StringComparer.OrdinalIgnoreCase);
                plan.Configuration.CePollTimeoutMinutes = timeoutMinutes;
                plan.Configuration.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
            }

            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
        }
        finally
        {
            IsSavingTestifySettings = false;
        }
    }
}
