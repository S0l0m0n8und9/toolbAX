using FoToolbox.SDK.Commands;
using System;
using System.Collections.Generic;

namespace DualWriteMapBrowserPlugin;

public sealed partial class DualWriteMapBrowserViewModel
{
    private bool _isTestifySettingsVisible;
    private TestifyConfigurationViewModel? _testifySettingsViewModel;

    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;

    internal TestifyConfigurationViewModel? TestifySettingsViewModel
    {
        get => _testifySettingsViewModel;
        private set
        {
            if (ReferenceEquals(_testifySettingsViewModel, value))
            {
                return;
            }

            _testifySettingsViewModel = value;
            OnPropertyChanged();
        }
    }

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

    private void InitializeTestifySettingsCommands(Action<Exception> onError)
    {
        _ = onError;
        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
    }

    private void OpenTestifySettings()
    {
        if (SelectedRecord is null)
        {
            StatusMessage = "Select a dual-write map before opening Testify settings.";
            return;
        }

        var record = SelectedRecord;
        TestifySettingsViewModel = new TestifyConfigurationViewModel(
            _testifyConfigStore,
            _ctx.CurrentEnv.Id,
            record.Id,
            ex => StatusMessage = $"Testify settings error: {ex.Message}",
            onClose: CloseTestifySettings,
            onSaved: saved => ApplyTestifyConfigurationToPlan(record.Id, record.DisplayName, saved));

        IsTestifySettingsVisible = true;
    }

    private void CloseTestifySettings()
    {
        IsTestifySettingsVisible = false;
        TestifySettingsViewModel = null;
    }

    private void ApplyTestifyConfigurationToPlan(string mapId, string mapDisplayName, TestifyMapConfiguration saved)
    {
        if (_testifyPlans.TryGetValue(mapId, out var plan))
        {
            plan.Configuration.OmitCreateFields = new HashSet<string>(saved.OmitCreateFields, StringComparer.OrdinalIgnoreCase);
            plan.Configuration.PreferredCreateValues = new Dictionary<string, string>(saved.PreferredCreateValues, StringComparer.OrdinalIgnoreCase);
            plan.Configuration.CePollTimeoutSeconds = saved.CePollTimeoutSeconds;
            plan.Configuration.AllowPartialEnumCoverage = saved.AllowPartialEnumCoverage;
        }

        StatusMessage = $"Saved Testify settings for '{mapDisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
    }
}
