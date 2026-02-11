using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ODataPostBuilderPlugin;

public sealed partial class ODataPostBuilderViewModel
{
    private async Task LoadSavedRequestsAsync(CancellationToken ct)
    {
        try
        {
            _savedRequests.Clear();
            var items = await _savedStore.LoadForEnvAsync(_ctx.CurrentEnv.Id).ConfigureAwait(false);
            foreach (var it in items)
            {
                _savedRequests.Add(it);
            }
            SavedStatus = _savedRequests.Count == 0 ? "No saved requests." : $"Loaded {_savedRequests.Count} saved request(s).";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load saved API requests.");
            SavedStatus = $"Load failed: {ex.Message}";
        }
    }

    private void LoadSelectedRequest()
    {
        if (SelectedSavedRequest is null)
        {
            Status = "Select a saved request.";
            return;
        }

        SelectedMethod = NormalizeMethod(SelectedSavedRequest.Method);
        ApiUrl = SelectedSavedRequest.Url;
        CrossCompany = HasCrossCompanyTrueQuery(SelectedSavedRequest.Url);
        Status = $"Loaded saved request '{SelectedSavedRequest.Name}'.";
    }

    private async Task SaveCurrentRequestAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            Status = "API URL is required to save.";
            return;
        }

        var suggested = SelectedEntityItem?.Name;
        suggested = string.IsNullOrWhiteSpace(suggested) ? $"{SelectedMethod} {ApiUrl}" : $"{SelectedMethod} {suggested}";

        var name = PromptWindow.Show("Name for saved request:", string.IsNullOrWhiteSpace(SelectedSavedRequest?.Name) ? suggested : SelectedSavedRequest!.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "Save cancelled.";
            return;
        }

        var trimmed = name.Trim();
        var existing = _savedRequests.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && (SelectedSavedRequest is null || !string.Equals(existing.Id, SelectedSavedRequest.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (!ConfirmOverwrite(trimmed))
            {
                Status = "Save cancelled.";
                return;
            }
            SelectedSavedRequest = existing;
        }

        var item = SelectedSavedRequest ?? new SavedApiRequestItem { EnvId = _ctx.CurrentEnv.Id };
        item.EnvId = _ctx.CurrentEnv.Id;
        item.Name = trimmed;
        item.Method = NormalizeMethod(SelectedMethod);
        if (!TryResolveEffectiveUrlForWrite(item.Method, out var effectiveUrl, out var effectiveUrlErr))
        {
            Status = effectiveUrlErr ?? "Invalid URL.";
            return;
        }
        item.Url = effectiveUrl;
        item.BodyJson = string.Equals(SelectedMethod, "DELETE", StringComparison.OrdinalIgnoreCase) ? null : PayloadJson;

        await _savedStore.SaveAsync(item).ConfigureAwait(false);

        if (!_savedRequests.Any(s => string.Equals(s.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _savedRequests.Add(item);
        }

        SavedStatus = $"Saved '{item.Name}'.";
        Status = SavedStatus;
    }

    private async Task RenameSelectedRequestAsync(CancellationToken ct)
    {
        if (SelectedSavedRequest is null)
        {
            Status = "Select a saved request.";
            return;
        }

        var renamed = PromptWindow.Show("Rename saved request:", SelectedSavedRequest.Name);
        if (string.IsNullOrWhiteSpace(renamed))
        {
            Status = "Rename cancelled.";
            return;
        }

        var trimmed = renamed.Trim();
        var existing = _savedRequests.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !string.Equals(existing.Id, SelectedSavedRequest.Id, StringComparison.OrdinalIgnoreCase))
        {
            Status = $"A saved request named '{trimmed}' already exists.";
            return;
        }

        SelectedSavedRequest.Name = trimmed;
        await _savedStore.SaveAsync(SelectedSavedRequest).ConfigureAwait(false);

        SavedStatus = $"Renamed to '{trimmed}'.";
        Status = SavedStatus;
    }

    private async Task DeleteSelectedRequestAsync(CancellationToken ct)
    {
        if (SelectedSavedRequest is null)
        {
            Status = "Select a saved request.";
            return;
        }

        var res = MessageBox.Show($"Delete saved request '{SelectedSavedRequest.Name}'?", "Delete saved request", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes)
        {
            Status = "Delete cancelled.";
            return;
        }

        await _savedStore.DeleteAsync(SelectedSavedRequest).ConfigureAwait(false);
        _savedRequests.Remove(SelectedSavedRequest);
        SelectedSavedRequest = null;
        SavedStatus = "Deleted.";
        Status = SavedStatus;
    }

    private void ExportSelectedRequest()
    {
        if (SelectedSavedRequest is null)
        {
            Status = "Select a saved request.";
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "Export OpenCollection collection (1 request)",
            Filter = "JSON (*.json)|*.json",
            FileName = $"{SanitizeFileName(SelectedSavedRequest.Name)}.json"
        };

        if (sfd.ShowDialog() != true) return;

        // OpenCollection schema represents requests as "items" inside a collection doc.
        var json = _savedStore.ExportAllAsOpenCollection(
            SelectedSavedRequest.Name,
            new[] { SelectedSavedRequest });

        File.WriteAllText(sfd.FileName, json);
        Status = $"Exported to {sfd.FileName}";
    }

    private void ExportAllRequests()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export OpenCollection collection",
            Filter = "JSON (*.json)|*.json",
            FileName = "FoToolbox-ApiRequests.json"
        };

        if (sfd.ShowDialog() != true) return;

        var json = _savedStore.ExportAllAsOpenCollection($"FoToolbox API ({_ctx.CurrentEnv.Name})", _savedRequests.ToList());
        File.WriteAllText(sfd.FileName, json);
        Status = $"Exported to {sfd.FileName}";
    }

    private async Task ImportRequestsAsync(CancellationToken ct)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Import OpenCollection JSON",
            Filter = "JSON (*.json)|*.json"
        };

        if (ofd.ShowDialog() != true) return;

        var json = await File.ReadAllTextAsync(ofd.FileName, ct).ConfigureAwait(false);
        var imported = _savedStore.ImportOpenCollection(json, _ctx.CurrentEnv.Id);
        if (imported.Count == 0)
        {
            Status = "No requests found to import.";
            return;
        }

        foreach (var item in imported)
        {
            var baseName = item.Name;
            var name = baseName;
            var i = 2;
            while (_savedRequests.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} ({i++})";
            }
            item.Name = name;
            item.EnvId = _ctx.CurrentEnv.Id;
            await _savedStore.SaveAsync(item).ConfigureAwait(false);
            _savedRequests.Add(item);
        }

        SavedStatus = $"Imported {imported.Count} request(s).";
        Status = SavedStatus;
    }

    private static bool ConfirmOverwrite(string name)
    {
        var result = MessageBox.Show($"A saved request named '{name}' already exists. Overwrite it?", "Overwrite saved request", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private static string SanitizeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(bad.Contains(ch) ? '_' : ch);
        }
        return sb.ToString();
    }
}
