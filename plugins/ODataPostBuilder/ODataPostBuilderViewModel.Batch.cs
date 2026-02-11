using FoToolbox.Core.OData;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ODataPostBuilderPlugin;

public sealed partial class ODataPostBuilderViewModel
{
    private void BatchOperationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var op in _batchOperations)
            {
                op.PropertyChanged -= BatchOpChanged;
                op.PropertyChanged += BatchOpChanged;
            }
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<BatchOperationItem>())
                {
                    item.PropertyChanged -= BatchOpChanged;
                }
            }
            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<BatchOperationItem>())
                {
                    item.PropertyChanged -= BatchOpChanged;
                    item.PropertyChanged += BatchOpChanged;
                }
            }
        }

        RebuildBatchPreview();
    }

    private void AddCurrentToBatch()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            Status = "API URL is required before adding to batch.";
            return;
        }

        var m = NormalizeMethod(SelectedMethod);
        var body = m == "DELETE" ? null : (string.IsNullOrWhiteSpace(PayloadJson) ? null : PayloadJson);

        if (!TryResolveEffectiveUrlForWrite(m, out var effectiveUrl, out var err))
        {
            Status = err ?? "Invalid URL.";
            return;
        }

        var op = new BatchOperationItem
        {
            Method = m,
            Url = effectiveUrl,
            BodyJson = body
        };
        _batchOperations.Add(op);
        SelectedBatchOperation = op;
        Status = "Added to batch.";
    }

    private void RemoveSelectedBatchOp()
    {
        if (SelectedBatchOperation is null) return;
        _batchOperations.Remove(SelectedBatchOperation);
        SelectedBatchOperation = null;
    }

    private void ClearBatch()
    {
        _batchOperations.Clear();
        SelectedBatchOperation = null;
    }

    private void CopyBatch()
    {
        if (string.IsNullOrWhiteSpace(BatchBodyPreview))
        {
            Status = "No batch body to copy.";
            return;
        }

        Clipboard.SetText(BatchBodyPreview);
        Status = "Batch body copied to clipboard.";
    }

    private void BatchOpChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BatchOperationItem.Method) or nameof(BatchOperationItem.Url) or nameof(BatchOperationItem.BodyJson))
        {
            RebuildBatchPreview();
        }
    }

    private void RebuildBatchPreview()
    {
        var nonEmpty = _batchOperations.Where(op => !string.IsNullOrWhiteSpace(op.Url)).ToList();
        if (nonEmpty.Count == 0)
        {
            BatchUrl = $"{_ctx.CurrentEnv.BaseUrl.TrimEnd('/')}/data/$batch";
            BatchContentType = string.Empty;
            BatchBodyPreview = string.Empty;
            return;
        }

        try
        {
            var ops = nonEmpty.Select(op => new ODataBatchOperation(
                MethodFromString(op.Method),
                op.Url,
                string.IsNullOrWhiteSpace(op.BodyJson) ? null : op.BodyJson,
                Headers: BuildBatchHeadersFor(op.Method))).ToList();

            var built = ODataBatchBuilder.BuildWriteBatch(_ctx.CurrentEnv.BaseUrl, ops);
            BatchUrl = built.BatchUrl;
            BatchContentType = built.ContentType;
            BatchBodyPreview = built.Body;
        }
        catch (Exception ex)
        {
            BatchUrl = $"{_ctx.CurrentEnv.BaseUrl.TrimEnd('/')}/data/$batch";
            BatchContentType = string.Empty;
            BatchBodyPreview = string.Empty;
            Status = $"Batch preview error: {ex.Message}";
        }
    }

    private static HttpMethod MethodFromString(string method)
    {
        var m = NormalizeMethod(method);
        return m switch
        {
            "PATCH" => new HttpMethod("PATCH"),
            "DELETE" => HttpMethod.Delete,
            _ => HttpMethod.Post
        };
    }

    private IReadOnlyDictionary<string, string>? BuildBatchHeadersFor(string method)
    {
        var m = NormalizeMethod(method);
        if (m is not ("PATCH" or "DELETE")) return null;

        var ifMatch = string.IsNullOrWhiteSpace(IfMatchCustom)
            ? (UseIfMatchStar ? "*" : null)
            : IfMatchCustom!.Trim();

        if (string.IsNullOrWhiteSpace(ifMatch)) return null;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["If-Match"] = ifMatch };
    }

    private async Task SendBatchAsync(CancellationToken ct)
    {
        BatchSendStatus = string.Empty;
        BatchResponseDetails = "No batch response yet.";

        if (_ctxWrite?.ODataWrite is null)
        {
            Status = "Host did not provide OData.Write client for this plugin.";
            return;
        }

        var nonEmpty = _batchOperations.Where(op => !string.IsNullOrWhiteSpace(op.Url)).ToList();
        if (nonEmpty.Count == 0)
        {
            Status = "Add at least one operation to the batch.";
            return;
        }

        RebuildBatchPreview();
        if (string.IsNullOrWhiteSpace(BatchBodyPreview) || string.IsNullOrWhiteSpace(BatchContentType))
        {
            Status = "Batch preview is empty; fix batch operations first.";
            return;
        }

        if (!_confirmedThisSession)
        {
            var res = MessageBox.Show(
                $"This will send a batch with {nonEmpty.Count} operation(s) to environment '{_ctx.CurrentEnv.Name}'. Continue?",
                "Confirm batch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes)
            {
                Status = "Batch cancelled.";
                return;
            }
            _confirmedThisSession = true;
        }

        IsBusy = true;
        BatchSendStatus = "Sending...";
        Status = "Sending batch...";

        try
        {
            var req = new ODataWriteRequest(HttpMethod.Post, BatchUrl, JsonBody: null, Headers: null, Body: BatchBodyPreview, ContentType: BatchContentType);
            var resp = await _ctxWrite.ODataWrite.SendAsync(req, ct).ConfigureAwait(false);
            BatchSendStatus = $"HTTP {resp.StatusCode}";
            BatchResponseDetails = FormatResponse(resp);
            Status = "Batch complete.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Batch failed in {Env}", _ctx.CurrentEnv.Name);
            BatchSendStatus = "Failed.";
            BatchResponseDetails = ex.Message;
            Status = $"Batch failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
