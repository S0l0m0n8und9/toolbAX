using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoToolbox.Core.OData;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
// FoToolbox.Core.OData also defines an IODataClient; in this view-model IODataClient always means the
// Avalonia app's client abstraction.
using IODataClient = ToolBax.Core.Services.IODataClient;

namespace ToolBax.App.ViewModels;

/// <summary>
/// POST Builder (control-map §7): compose a POST/PATCH/DELETE against an OData path and view the
/// response. The send goes through <see cref="IODataClient"/>; cross-company appends the query option.
/// An optional <b>field-grid</b> mode (<see cref="UseFieldGrid"/>) builds the JSON body from a chosen
/// entity's metadata — pick an entity, include/value its fields, and a validated, type-coerced payload
/// is generated via the shared <see cref="ODataPayloadBuilder"/> (the same engine the WPF plugin uses).
/// </summary>
public partial class PostBuilderViewModel : ObservableObject
{
    private readonly IODataClient _client;
    private readonly IClipboardService _clipboard;
    private readonly IMetadataService _metadata;

    // True only while RefreshEntityFilter is rebuilding FilteredEntities, so the transient selection
    // null a bound ComboBox emits during Clear() doesn't run OnSelectedEntityChanged's side-effects.
    private bool _refreshingEntities;

    public IReadOnlyList<string> Methods { get; } = new[] { "POST", "PATCH", "DELETE" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    private string _method = "POST";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    private string _path = "/data/CustomersV3";

    /// <summary>Apply the write across all legal entities (<c>cross-company=true</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    private bool _crossCompany;

    [ObservableProperty]
    private string _requestBody = "{\n  \"dataAreaId\": \"USMF\",\n  \"CustomerAccount\": \"US-9001\"\n}";

    [ObservableProperty]
    private string _responseBody = string.Empty;

    [ObservableProperty]
    private string _statusText = "No response yet.";

    /// <summary>True only when the last send returned a 2xx — gates the success badge.</summary>
    [ObservableProperty]
    private bool _sendSucceeded;

    /// <summary>The last send's "{code} {reason}".</summary>
    [ObservableProperty]
    private string _statusBadge = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    // --- Field-grid mode ---

    /// <summary>When true, the body is built from the field grid (and the raw editor is read-only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodyReadOnly))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _useFieldGrid;

    /// <summary>Case-insensitive substring filter over the entity-list names.</summary>
    [ObservableProperty]
    private string _entitySearch = string.Empty;

    [ObservableProperty]
    private EntitySet? _selectedEntity;

    /// <summary>Validation issues from the last payload build (blank when the payload is valid).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPayloadIssues))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _payloadIssues = string.Empty;

    public bool HasPayloadIssues => !string.IsNullOrEmpty(PayloadIssues);

    /// <summary>The raw-JSON editor is read-only while the grid is the source of truth.</summary>
    public bool IsBodyReadOnly => UseFieldGrid;

    public ObservableCollection<EntitySet> Entities { get; }

    /// <summary>The entity list as shown, after applying <see cref="EntitySearch"/>.</summary>
    public ObservableCollection<EntitySet> FilteredEntities { get; } = new();

    /// <summary>The selected entity's fields (grid rows that build the payload).</summary>
    public ObservableCollection<PostFieldRow> Fields { get; } = new();

    public PostBuilderViewModel(IODataClient client, IClipboardService? clipboard = null, IMetadataService? metadata = null)
    {
        _client = client;
        _clipboard = clipboard ?? new FakeClipboardService();
        _metadata = metadata ?? new FakeMetadataService();
        Entities = new ObservableCollection<EntitySet>(_metadata.GetEntities());
        RefreshEntityFilter();
        // No entity is auto-selected: grid mode is opt-in, so construction leaves the raw body/path intact.
    }

    /// <summary>The full "{METHOD} {path}" line that will be sent (with cross-company applied).</summary>
    public string RequestUrl => $"{Method} {EffectivePath()}";

    // Appends cross-company to the user's path (respecting an existing query string).
    private string EffectivePath()
    {
        var path = Path.Trim();
        if (CrossCompany && path.Length > 0)
        {
            path += path.Contains('?', StringComparison.Ordinal) ? "&cross-company=true" : "?cross-company=true";
        }

        return path;
    }

    partial void OnEntitySearchChanged(string value) => RefreshEntityFilter();

    // Method affects mandatory enforcement (POST enforces, PATCH/DELETE don't), so rebuild the payload.
    partial void OnMethodChanged(string value)
    {
        if (UseFieldGrid)
        {
            RebuildPayload();
        }
    }

    partial void OnUseFieldGridChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        if (SelectedEntity is null)
        {
            // Defaulting the selection triggers OnSelectedEntityChanged, which (grid mode is now on)
            // already loads the fields + builds the payload — so don't do it a second time here.
            SelectedEntity = FilteredEntities.FirstOrDefault() ?? Entities.FirstOrDefault();
        }
        else
        {
            LoadFields();
            RebuildPayload();
        }
    }

    partial void OnSelectedEntityChanged(EntitySet? value)
    {
        if (_refreshingEntities)
        {
            return; // a transient null/restore from rebuilding the filtered list — not a real selection change
        }

        if (value is not null)
        {
            Path = $"/data/{value.Name}";
        }

        if (UseFieldGrid)
        {
            LoadFields();
            RebuildPayload();
        }
    }

    // Rebuilds FilteredEntities from Entities applying the (trimmed, case-insensitive) EntitySearch.
    // The currently-selected entity is always kept in the list (even when it doesn't match the term),
    // and the selection is snapshot/restored, so typing in the search box can't make a bound ComboBox
    // null the selection and wipe the field grid. See OnSelectedEntityChanged's guard.
    private void RefreshEntityFilter()
    {
        var term = EntitySearch?.Trim();
        var saved = SelectedEntity;
        _refreshingEntities = true;
        try
        {
            FilteredEntities.Clear();
            foreach (var e in Entities)
            {
                if (string.IsNullOrEmpty(term)
                    || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || ReferenceEquals(e, saved))
                {
                    FilteredEntities.Add(e);
                }
            }

            if (saved is not null && !ReferenceEquals(SelectedEntity, saved))
            {
                SelectedEntity = saved; // restore if the bound control nulled it during Clear()
            }
        }
        finally
        {
            _refreshingEntities = false;
        }
    }

    // Rebuilds the grid rows for the selected entity. Key/non-nullable fields are pre-included
    // (mirroring the WPF plugin), so a POST starts from the minimal required set.
    private void LoadFields()
    {
        foreach (var old in Fields)
        {
            old.PropertyChanged -= OnFieldChanged;
        }

        Fields.Clear();

        var fields = SelectedEntity is null ? null : _metadata.GetFields(SelectedEntity.Name);
        if (fields is null)
        {
            return;
        }

        foreach (var f in fields)
        {
            var mandatory = f.IsKey || !f.Nullable;
            var row = new PostFieldRow(f.Name, f.TypeDisplay, mandatory, f.IsKey, include: mandatory);
            row.PropertyChanged += OnFieldChanged;
            Fields.Add(row);
        }
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PostFieldRow.Include) or nameof(PostFieldRow.Value))
        {
            RebuildPayload();
        }
    }

    // Turns the grid into a validated, type-coerced JSON body via the shared Core payload builder.
    // POST enforces mandatory fields; PATCH/DELETE relax that (partial updates / key-only).
    private void RebuildPayload()
    {
        if (SelectedEntity is null)
        {
            return;
        }

        var fields = _metadata.GetFields(SelectedEntity.Name);
        if (fields is null)
        {
            return;
        }

        var entity = PostPayloadMapper.ToEntity(SelectedEntity.Name, fields);
        var values = Fields.Select(r => new ODataFieldValue(r.Name, r.Include, r.Value)).ToList();
        var enforceMandatory = string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase);

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, values, enforceMandatory: enforceMandatory);
        if (result.Ok)
        {
            RequestBody = result.Json!;
            PayloadIssues = string.Empty;
        }
        else
        {
            // Clear the body so a stale (previously-built or default) payload can't be sent while the
            // grid is invalid; Send is also disabled via CanSend while there are payload issues.
            RequestBody = string.Empty;
            PayloadIssues = string.Join(Environment.NewLine, result.Issues);
        }
    }

    // In grid mode an invalid payload must not be sent (the body is blank and the issues are shown);
    // in raw mode the user owns the body, so there's nothing to gate on.
    private bool CanSend() => !(UseFieldGrid && HasPayloadIssues);

    // IncludeCancelCommand: surfaces SendCancelCommand and lets the generated AsyncRelayCommand carry
    // the token's lifecycle, so an in-flight send can be cancelled on navigate-away/shutdown.
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanSend))]
    private async Task Send(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Sending…";
        try
        {
            var body = string.Equals(Method, "DELETE", StringComparison.OrdinalIgnoreCase) ? null : RequestBody;
            var response = await _client.SendAsync(Method, EffectivePath(), body, ct);
            StatusText = response.StatusLine;
            StatusBadge = $"{response.StatusCode} {response.ReasonPhrase}";
            SendSucceeded = response.IsSuccess;
            ResponseBody = response.Body;
        }
        catch (Exception ex)
        {
            StatusText = "Request failed.";
            SendSucceeded = false;
            StatusBadge = string.Empty;
            ResponseBody = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CopyUrl() => _clipboard.SetTextAsync(EffectivePath());

    [RelayCommand]
    private Task CopyPayload() => _clipboard.SetTextAsync(RequestBody);
}
