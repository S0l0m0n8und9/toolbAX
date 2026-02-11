using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ODataPostBuilderPlugin;

public sealed partial class ODataPostBuilderViewModel
{
    private static bool HasCrossCompanyTrueQuery(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var qIdx = url.IndexOf('?', StringComparison.Ordinal);
        if (qIdx < 0 || qIdx >= url.Length - 1) return false;

        var rawQuery = url[(qIdx + 1)..];
        foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = Uri.UnescapeDataString(pair[..eq]);
            var val = Uri.UnescapeDataString(pair[(eq + 1)..]);
            if (string.Equals(key, "cross-company", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string EscapeODataStringLiteral(string value)
    {
        // OData string literals escape a single quote by doubling it.
        // We intentionally do not URL-encode here; callers should only use safe key values.
        return (value ?? string.Empty).Replace("'", "''");
    }

    private bool TryResolveEffectiveUrlForWrite(string method, out string effectiveUrl, out string? error)
    {
        effectiveUrl = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            error = "API URL is required.";
            return false;
        }

        var url = ApiUrl.Trim();
        var qIdx = url.IndexOf('?', StringComparison.Ordinal);
        var urlWithoutQuery = qIdx >= 0 ? url[..qIdx] : url;
        var rawQuery = qIdx >= 0 && qIdx < url.Length - 1 ? url[(qIdx + 1)..] : string.Empty;
        var querySuffix = string.IsNullOrWhiteSpace(rawQuery) ? string.Empty : "?" + rawQuery;

        // PATCH/DELETE must target a single entity. We allow query string only for cross-company=true.
        if (method is "PATCH" or "DELETE" && !string.IsNullOrWhiteSpace(rawQuery))
        {
            foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    error = $"{method} query string is invalid. Only cross-company=true is allowed.";
                    return false;
                }

                var key = Uri.UnescapeDataString(pair[..eq]);
                var val = Uri.UnescapeDataString(pair[(eq + 1)..]);
                if (!string.Equals(key, "cross-company", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"{method} URL must identify a single entity. Only cross-company=true is allowed in the query string.";
                    return false;
                }
            }

            // Checkbox controls whether the final write URL uses cross-company.
            querySuffix = CrossCompany ? "?cross-company=true" : string.Empty;
        }

        // If user already provided an instance URL, trust it.
        if (method is "PATCH" or "DELETE")
        {
            var hasKeyPredicate = urlWithoutQuery.Contains('(') && urlWithoutQuery.Contains(')');
            if (hasKeyPredicate)
            {
                effectiveUrl = urlWithoutQuery + querySuffix;
                return true;
            }

            // We can only scaffold the key predicate if metadata is loaded.
            if (_selectedEntityDetails is null)
            {
                error = $"{method} requires an entity key in the URL (e.g. /data/CustomersV3(AccountNumber='...')). Select an entity first, or enter the full instance URL.";
                return false;
            }

            var keyProps = _selectedEntityDetails.Properties.Where(p => p.IsKey).ToList();
            if (keyProps.Count == 0)
            {
                error = $"{method} requires an entity key in the URL, but this entity has no key metadata.";
                return false;
            }

            var missing = new List<string>();
            var parts = new List<string>();
            foreach (var kp in keyProps)
            {
                var field = _fields.FirstOrDefault(f => string.Equals(f.Name, kp.Name, StringComparison.OrdinalIgnoreCase));
                var raw = field?.GetEffectiveValueText()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    missing.Add(kp.Name);
                    continue;
                }

                var literal = kp.Type switch
                {
                    "Edm.Boolean" => raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
                        ? raw.ToLowerInvariant()
                        : raw,
                    "Edm.Int32" or "Edm.Int64" or "Edm.Decimal" or "Edm.Double" or "Edm.Single" => raw,
                    _ => $"'{EscapeODataStringLiteral(raw)}'"
                };

                // Use explicit key names so composite keys work.
                parts.Add($"{kp.Name}={literal}");
            }

            if (missing.Count > 0)
            {
                error = $"{method} requires key value(s): {string.Join(", ", missing)}.";
                return false;
            }

            // If URL is the collection URL (no key predicate), append "(...)". Trim trailing '/' first.
            urlWithoutQuery = urlWithoutQuery.TrimEnd('/');
            effectiveUrl = urlWithoutQuery + "(" + string.Join(",", parts) + ")" + querySuffix;
            return true;
        }

        effectiveUrl = url;
        return true;
    }

    private bool EntityFilter(object obj)
    {
        if (obj is not EntityItem e) return false;
        if (string.IsNullOrWhiteSpace(EntitySearch)) return true;
        var q = EntitySearch.Trim();
        return e.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadEntitiesAsync(CancellationToken ct)
    {
        IsBusy = true;
        Status = "Loading entities...";
        EntityLoadStatus = "Loading...";

        try
        {
            _entities.Clear();
            _fields.Clear();
            _selectedEntityDetails = null;
            SelectedEntitySummary = "No entity selected.";
            ApiUrl = string.Empty;
            PayloadJson = string.Empty;
            PayloadStatus = "No payload yet.";
            ResponseDetails = "No response yet.";

            var index = await _ctx.Catalog.GetODataEntityIndexAsync(_ctx.CurrentEnv, CatalogRefreshMode.UseCacheIfFresh, ct).ConfigureAwait(false);
            _enumMembersByType = index.Enums
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.First().Members, StringComparer.OrdinalIgnoreCase);

            foreach (var e in index.Entities.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                _entities.Add(new EntityItem(e.Name, e.PropertyCount, e.NavigationCount));
            }

            EntitiesView.Refresh();
            EntityLoadStatus = $"Loaded {_entities.Count} entities.";
            Status = _ctxWrite is null
                ? "Loaded entities. Warning: host did not provide OData.Write capability."
                : "Loaded entities. Select one to scaffold a payload.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load entity index for {Env}", _ctx.CurrentEnv.Name);
            EntityLoadStatus = $"Load failed: {ex.Message}";
            Status = EntityLoadStatus;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ScheduleEntitySearchRefresh()
    {
        _entitySearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _entitySearchCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, cts.Token).ConfigureAwait(false);
                if (cts.Token.IsCancellationRequested) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is not null)
                {
                    dispatcher.Invoke(() => EntitiesView.Refresh());
                }
                else
                {
                    EntitiesView.Refresh();
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void StartLoadSelectedEntityDetails()
    {
        _selectedEntityDetailsCts?.Cancel();

        _fields.Clear();
        _selectedEntityDetails = null;
        PayloadJson = string.Empty;
        PayloadStatus = "No payload yet.";
        ResponseDetails = "No response yet.";

        if (_selectedEntityItem is null)
        {
            SelectedEntitySummary = "No entity selected.";
            ApiUrl = string.Empty;
            return;
        }

        var entityName = _selectedEntityItem.Name;
        SelectedEntitySummary = $"Loading fields for {entityName}...";
        ApiUrl = _ctx.Catalog.BuildODataEntityUrl(_ctx.CurrentEnv, entityName);

        var cts = new CancellationTokenSource();
        _selectedEntityDetailsCts = cts;
        _ = LoadSelectedEntityDetailsAsync(entityName, cts.Token);
    }

    private async Task LoadSelectedEntityDetailsAsync(string entityName, CancellationToken ct)
    {
        try
        {
            var entity = await _ctx.Catalog.GetODataEntityDetailsAsync(_ctx.CurrentEnv, entityName, CatalogRefreshMode.UseCacheIfFresh, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            if (entity is null)
            {
                SelectedEntitySummary = $"Entity not found in metadata: {entityName}";
                return;
            }

            if (_selectedEntityItem is null || !string.Equals(_selectedEntityItem.Name, entityName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedEntityDetails = entity;

            var defaultCompany = _ctx.CurrentEnv.DefaultCompany;
            var hasDataAreaId = entity.Properties.Any(p => string.Equals(p.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase));
            var hasDataAreaIdKey = entity.Properties.Any(p =>
                string.Equals(p.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase) && p.IsKey);

            foreach (var prop in entity.Properties.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var editorKind = ResolveEditorKind(prop.Type);
                var enumMembers = editorKind == PostFieldEditorKind.Enum && _enumMembersByType.TryGetValue(prop.Type, out var members)
                    ? new ObservableCollection<string>(members)
                    : null;

                var item = new PostFieldItem(prop.Name, prop.Type, prop.Nullable, prop.Mandatory, editorKind, enumMembers);
                item.Include = prop.Mandatory;
                if (hasDataAreaId && string.Equals(prop.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(defaultCompany))
                {
                    item.Include = true;
                    item.TextValue = defaultCompany;
                }

                item.PropertyChanged += FieldChanged;
                _fields.Add(item);
            }

            // If company is part of entity key, cross-company routing is commonly required to resolve updates.
            CrossCompany = hasDataAreaIdKey;

            SelectedEntitySummary = $"{entityName}: {entity.Properties.Count} properties";
            RebuildPayloadPreview();
            Status = "Edit values, then copy JSON, save a request, or send.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load entity details for {Entity}", entityName);
            SelectedEntitySummary = $"Failed to load fields: {ex.Message}";
            Status = SelectedEntitySummary;
        }
    }

    private void FieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PostFieldItem.Include) or nameof(PostFieldItem.TextValue) or nameof(PostFieldItem.BoolValue) or nameof(PostFieldItem.EnumValue))
        {
            RebuildPayloadPreview();
        }
    }

    private void RebuildPayloadPreview()
    {
        if (_selectedEntityDetails is null)
        {
            PayloadJson = string.Empty;
            PayloadStatus = "No payload yet.";
            return;
        }

        if (string.Equals(SelectedMethod, "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            PayloadJson = string.Empty;
            PayloadStatus = "No payload for DELETE.";
            return;
        }

        // PATCH should not attempt to write key fields; they should be used to address the entity via URL.
        var keyNames = string.Equals(SelectedMethod, "PATCH", StringComparison.OrdinalIgnoreCase)
            ? _selectedEntityDetails.Properties.Where(p => p.IsKey).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var values = _fields
            .Where(f => keyNames is null || !keyNames.Contains(f.Name))
            .Select(f => new ODataFieldValue(f.Name, f.Include, f.GetEffectiveValueText()))
            .ToList();

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_ctx.CurrentEnv.DefaultCompany))
        {
            defaults["dataAreaId"] = _ctx.CurrentEnv.DefaultCompany!;
        }

        var enforceMandatory = string.Equals(SelectedMethod, "POST", StringComparison.OrdinalIgnoreCase);
        var result = ODataPayloadBuilder.BuildPayloadJson(_selectedEntityDetails, values, _enumMembersByType, defaults, enforceMandatory: enforceMandatory);

        if (result.Ok)
        {
            PayloadJson = result.Json ?? string.Empty;
            PayloadStatus = enforceMandatory ? "Payload valid." : "Payload valid (mandatory not enforced for PATCH).";
        }
        else
        {
            PayloadJson = string.Empty;
            PayloadStatus = $"{result.Issues.Count} issue(s).";
        }
    }

    private void UpdateIfMatchDefaults()
    {
        UseIfMatchStar = string.Equals(SelectedMethod, "PATCH", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(SelectedMethod, "DELETE", StringComparison.OrdinalIgnoreCase);
    }

    private static PostFieldEditorKind ResolveEditorKind(string type)
    {
        if (string.Equals(type, "Edm.Boolean", StringComparison.OrdinalIgnoreCase)) return PostFieldEditorKind.Boolean;
        if (!string.IsNullOrWhiteSpace(type) && !type.StartsWith("Edm.", StringComparison.OrdinalIgnoreCase)) return PostFieldEditorKind.Enum;
        return PostFieldEditorKind.Text;
    }

    private void CopyPayload()
    {
        if (string.IsNullOrWhiteSpace(PayloadJson))
        {
            Status = "No payload to copy.";
            return;
        }

        Clipboard.SetText(PayloadJson);
        Status = "Payload copied to clipboard.";
    }

    private void CopyUrl()
    {
        var method = NormalizeMethod(SelectedMethod);
        if (TryResolveEffectiveUrlForWrite(method, out var effectiveUrl, out var err))
        {
            Clipboard.SetText(effectiveUrl);
            Status = method is "PATCH" or "DELETE"
                ? "Resolved URL copied to clipboard."
                : "URL copied to clipboard.";
            return;
        }

        Status = err ?? "No URL to copy.";
    }

    private async Task SendAsync(CancellationToken ct)
    {
        SendStatus = string.Empty;
        ResponseDetails = "No response yet.";

        if (_ctxWrite?.ODataWrite is null)
        {
            Status = "Host did not provide OData.Write client for this plugin.";
            return;
        }

        var method = NormalizeMethod(SelectedMethod);
        var httpMethod = method switch
        {
            "PATCH" => new HttpMethod("PATCH"),
            "DELETE" => HttpMethod.Delete,
            _ => HttpMethod.Post
        };

        if (!TryResolveEffectiveUrlForWrite(method, out var effectiveUrl, out var effectiveUrlErr))
        {
            Status = effectiveUrlErr ?? "Invalid URL.";
            return;
        }

        string? jsonBody = null;
        if (httpMethod != HttpMethod.Delete)
        {
            if (string.IsNullOrWhiteSpace(PayloadJson))
            {
                Status = method == "PATCH"
                    ? "PATCH requires at least one value to be set (payload cannot be empty)."
                    : "Payload is required for POST.";
                return;
            }

            if (method == "PATCH" && PayloadJson.Trim() == "{}")
            {
                Status = "PATCH payload is empty ({}). Set at least one field value.";
                return;
            }

            jsonBody = PayloadJson;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (httpMethod == HttpMethod.Delete || method == "PATCH")
        {
            var ifMatch = string.IsNullOrWhiteSpace(IfMatchCustom)
                ? (UseIfMatchStar ? "*" : null)
                : IfMatchCustom!.Trim();

            if (!string.IsNullOrWhiteSpace(ifMatch))
            {
                headers["If-Match"] = ifMatch;
            }
        }

        if (!_confirmedThisSession)
        {
            var res = MessageBox.Show(
                $"This will send {method} to environment '{_ctx.CurrentEnv.Name}'. Continue?",
                $"Confirm {method}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes)
            {
                Status = "Request cancelled.";
                return;
            }
            _confirmedThisSession = true;
        }

        IsBusy = true;
        SendStatus = "Sending...";
        Status = $"Sending {method}...";

        try
        {
            var req = new ODataWriteRequest(httpMethod, effectiveUrl, jsonBody, headers.Count == 0 ? null : headers);
            var resp = await _ctxWrite.ODataWrite.SendAsync(req, ct).ConfigureAwait(false);

            SendStatus = $"HTTP {resp.StatusCode}";
            ResponseDetails = FormatResponse(resp);
            Status = $"{method} complete.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "{Method} failed for {Url} in {Env}", method, effectiveUrl, _ctx.CurrentEnv.Name);
            SendStatus = "Failed.";
            ResponseDetails = ex.Message;
            Status = $"{method} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatResponse(ODataWriteResponse resp)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Status: {resp.StatusCode}");

        if (resp.Headers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Headers:");
            foreach (var kvp in resp.Headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Body:");
        sb.AppendLine(string.IsNullOrWhiteSpace(resp.Body) ? "<empty>" : resp.Body);
        return sb.ToString().TrimEnd();
    }
}
