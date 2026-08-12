using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
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
    private readonly EntityCatalogLoader _loader;
    private readonly IDialogService _dialogs;

    // True only while RefreshEntityFilter is rebuilding FilteredEntities, so the transient selection
    // null a bound ComboBox emits during Clear() doesn't run OnSelectedEntityChanged's side-effects.
    private bool _refreshingEntities;

    public IReadOnlyList<string> Methods { get; } = new[] { "POST", "PATCH", "DELETE" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    [NotifyPropertyChangedFor(nameof(ShowIfMatch))]
    private string _method = "POST";

    /// <summary>Send an <c>If-Match</c> header on PATCH/DELETE (optimistic concurrency).</summary>
    [ObservableProperty]
    private bool _useIfMatch;

    /// <summary>The <c>If-Match</c> value — "*" (any version) or a specific ETag.</summary>
    [ObservableProperty]
    private string _ifMatch = "*";

    /// <summary>If-Match only applies to PATCH/DELETE; the view shows the controls only then.</summary>
    public bool ShowIfMatch => IsKeyedMethod(Method);

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

    /// <summary>The last response's headers, formatted "Name: value" per line (blank when none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResponseHeaders))]
    private string _responseHeaders = string.Empty;

    public bool HasResponseHeaders => !string.IsNullOrEmpty(ResponseHeaders);

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

    // Surfaces an entity-catalogue/field load failure regardless of mode. Grid mode already names the
    // load failure as the cause of its own block (see BlockPayload); raw mode had no signal at all for
    // the same Initialize/EnsureFields failure, since the picker (and its issue panel) is hidden there.
    // Mirrors Query Builder's LoadError banner; deliberately kept out of the PayloadIssues plumbing so it
    // doesn't count towards CanSend or the issue-count summary.
    [ObservableProperty]
    private string? _loadError;

    // --- Field-grid mode ---

    /// <summary>When true, the body is built from the field grid (and the raw editor is read-only).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodyReadOnly))]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
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

    /// <summary>Number of validation issues — drives the concise summary header above the (capped,
    /// scrollable) detail, so a many-mandatory entity can't turn the panel into a wall.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PayloadIssueSummary))]
    private int _payloadIssueCount;

    public bool HasPayloadIssues => !string.IsNullOrEmpty(PayloadIssues);

    /// <summary>One-line summary of how many issues block sending (the detail scrolls below).</summary>
    public string PayloadIssueSummary => PayloadIssueCount == 1
        ? "1 issue to resolve before sending:"
        : $"{PayloadIssueCount} issues to resolve before sending:";

    /// <summary>The raw-JSON editor is read-only while the grid is the source of truth.</summary>
    public bool IsBodyReadOnly => UseFieldGrid;

    public ObservableCollection<EntitySet> Entities { get; }

    /// <summary>The entity list as shown, after applying <see cref="EntitySearch"/>.</summary>
    public ObservableCollection<EntitySet> FilteredEntities { get; } = new();

    /// <summary>The selected entity's fields (grid rows that build the payload).</summary>
    public ObservableCollection<PostFieldRow> Fields { get; } = new();

    public PostBuilderViewModel(IODataClient client, IClipboardService? clipboard = null,
        IMetadataService? metadata = null, IDialogService? dialogs = null)
    {
        _client = client;
        _clipboard = clipboard ?? new FakeClipboardService();
        _metadata = metadata ?? new FakeMetadataService();
        _loader = new EntityCatalogLoader(_metadata);
        _dialogs = dialogs ?? new AutoConfirmDialogs();
        // The fake seeds its catalogue synchronously; the real service starts empty and fills in via
        // Initialize (triggered by the view on load) — so this snapshot is a starting point, not the load.
        Entities = new ObservableCollection<EntitySet>(_metadata.GetEntities());
        RefreshEntityFilter();
        // No entity is auto-selected: grid mode is opt-in, so construction leaves the raw body/path intact.
    }

    // Fetches the entity catalogue from the active environment's live $metadata (and the grid's fields when
    // it's in use). The view calls this on load, so opening the POST Builder first in a session gets a
    // populated entity picker instead of one that stays empty all session. Re-running it on every Loaded is
    // cheap — the list is only rebuilt when it actually changed — and it refreshes the catalogue after an
    // environment switch, since the metadata cache is environment-scoped. A no-op over the fake's seeded data.
    [RelayCommand]
    private async Task Initialize(CancellationToken ct)
    {
        var loaded = await _loader.LoadEntitiesAsync(Entities.Select(e => e.Name).ToList(), ct);
        LoadError = _loader.LastError;
        if (loaded is not null)
        {
            var previous = SelectedEntity?.Name;
            Entities.Clear();
            foreach (var e in loaded)
            {
                Entities.Add(e);
            }

            RefreshEntityFilter();
            // Keep the selection across the rebuild by name (the instances are new). Grid mode always needs
            // an entity — mirroring OnUseFieldGridChanged's default — so it falls back to the first when the
            // previous one isn't in this environment; raw mode keeps "nothing selected" so the user's own
            // path and body stand.
            SelectedEntity = Entities.FirstOrDefault(e => e.Name == previous)
                ?? (UseFieldGrid ? FilteredEntities.FirstOrDefault() ?? Entities.FirstOrDefault() : null);
        }

        await EnsureFieldsAsync(ct);
        if (UseFieldGrid && SelectedEntity is null)
        {
            // Grid mode with nothing to select (the catalogue is empty, or the load failed): state that
            // rather than leaving the raw body sendable against a path the grid isn't driving.
            RebuildPayload();
        }
    }

    // Fetches the selected entity's fields if they aren't cached yet, then rebuilds the grid + payload.
    [RelayCommand]
    private Task EnsureFields(CancellationToken ct) => EnsureFieldsAsync(ct);

    // Deliberately rebuilds via LoadFields/RebuildPayload rather than ReloadGrid: a fetch that yields no
    // fields must settle on the "hasn't loaded" block, not re-enter the fetch and loop.
    private async Task EnsureFieldsAsync(CancellationToken ct)
    {
        var entity = SelectedEntity;
        if (!UseFieldGrid || entity is null || Fields.Count > 0)
        {
            return;
        }

        var fetched = await _loader.EnsureFieldsAsync(entity.Name, ct);
        if (SelectedEntity != entity)
        {
            // The user moved on; that selection's own load (ReloadGrid/EnsureFieldsAsync) owns the grid
            // AND the LoadError banner now — this fetch's outcome, success or failure, belongs to
            // `entity`, not to whatever is selected now. Returning here BEFORE touching LoadError is what
            // stops a slow fetch for an entity the user has already left from clobbering the current
            // selection's (possibly healthy) state once it finally resolves (PR #196 review).
            return;
        }

        // _loader.LastError is shared, "most recent fetch" state: EntityCatalogLoader.EnsureFieldsAsync
        // returns early WITHOUT touching it when the entity's fields are already cached, so a cache hit
        // here could otherwise leave LoadError holding an unrelated, earlier entity's failure. Re-derive
        // per-entity truth from whether THIS entity's fields actually ended up available, rather than
        // trusting the shared field blindly (PR #196 review).
        LoadError = _metadata.GetFields(entity.Name) is null ? _loader.LastError : null;

        if (fetched || LoadError is not null)
        {
            LoadFields();     // clears the block's cause when the fields arrived
            RebuildPayload(); // …or re-states the block with the failure attached
        }
    }

    /// <summary>Number of grid fields currently included in the payload.</summary>
    public int IncludedFieldCount => Fields.Count(f => f.Include);

    /// <summary>"{Entity} · N field(s) included" context line shown while the grid is in use.</summary>
    public string GridSummary => SelectedEntity is null
        ? string.Empty
        : $"{SelectedEntity.Name} · {IncludedFieldCount} field{(IncludedFieldCount == 1 ? string.Empty : "s")} included";

    /// <summary>The full "{METHOD} {path}" line that will be sent (with cross-company applied).</summary>
    public string RequestUrl => $"{Method} {EffectivePath()}";

    // The path the request actually targets, with cross-company applied. In grid mode a PATCH/DELETE
    // targets a specific record via a key predicate built from the grid's key values (when complete).
    private string EffectivePath()
    {
        var path = BasePath();
        if (CrossCompany && path.Length > 0)
        {
            path += path.Contains('?', StringComparison.Ordinal) ? "&cross-company=true" : "?cross-company=true";
        }

        return path;
    }

    // Grid-mode PATCH/DELETE → "/data/{Entity}(key predicate)" when the key values are all present;
    // otherwise the user's Path verbatim (POST writes to the collection; raw mode owns its own path).
    private string BasePath()
    {
        if (UseFieldGrid && SelectedEntity is not null && IsKeyedMethod(Method))
        {
            var predicate = BuildKeyPredicate();
            if (predicate is not null)
            {
                return $"/data/{SelectedEntity.Name}{predicate}";
            }
        }

        return Path.Trim();
    }

    private static bool IsKeyedMethod(string method) =>
        string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);

    // Builds "(K1=…,K2=…)" from the key fields' grid values, or null if any key value is missing
    // (so the preview/request falls back to the collection path rather than targeting a wrong record).
    private string? BuildKeyPredicate()
    {
        if (SelectedEntity is null)
        {
            return null;
        }

        var fields = _metadata.GetFields(SelectedEntity.Name);
        var keyFields = fields?.Where(f => f.IsKey).ToList();
        if (keyFields is null || keyFields.Count == 0)
        {
            return null;
        }

        var parts = new List<string>(keyFields.Count);
        foreach (var kf in keyFields)
        {
            var value = Fields.FirstOrDefault(r => string.Equals(r.Name, kf.Name, StringComparison.Ordinal))?.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null; // incomplete key — don't target a partial record
            }

            parts.Add($"{kf.Name}={FormatKeyValue(kf.Type, value)}");
        }

        return $"({string.Join(",", parts)})";
    }

    // OData key literals: strings/enums are single-quoted (with '' escaping); numerics/bools/guids are raw.
    //
    // The quoted literal is then percent-encoded as a whole, because this predicate is interpolated straight
    // into the request URL and CoreODataClient.BuildUri hands that string to new Uri(...). Left raw, a key
    // value carrying URL syntax stopped being data: an ItemNumber of "1000/A" became an extra path segment
    // (404), "50%" became a broken escape sequence, and worst of all "A#1" started the URI *fragment* — which
    // silently truncated the request at the '#' AND took "?cross-company=true" with it, so a cross-company
    // write turned into a single-company one against a key the user never typed, with no error anywhere.
    // OData accepts percent-encoded key literals, so encoding the delimiting quotes to %27 costs nothing.
    // Non-string key types stay raw: digits, GUIDs and booleans contain no URL-significant characters, and
    // encoding them would only make the predicate harder to read.
    private static string FormatKeyValue(string friendlyType, string value) =>
        friendlyType is "String" or "Enum"
            ? Uri.EscapeDataString($"'{value.Replace("'", "''")}'")
            : value;

    partial void OnEntitySearchChanged(string value) => RefreshEntityFilter();

    // Method affects mandatory enforcement (POST enforces, PATCH/DELETE don't), so rebuild the payload.
    partial void OnMethodChanged(string value)
    {
        // PATCH/DELETE default to sending If-Match (optimistic concurrency); a POST create has no use for
        // it. The user can still toggle it off afterward.
        UseIfMatch = IsKeyedMethod(value);
        if (UseFieldGrid)
        {
            RebuildPayload();
        }
    }

    partial void OnUseFieldGridChanged(bool value)
    {
        if (!value)
        {
            // Back to raw mode: the editor owns the body again, so drop the grid's validation state instead
            // of leaving a stale issue panel over a body the user now controls.
            PayloadIssues = string.Empty;
            PayloadIssueCount = 0;
            return;
        }

        if (SelectedEntity is null)
        {
            // Defaulting the selection triggers OnSelectedEntityChanged, which (grid mode is now on)
            // already loads the fields + builds the payload — so don't do it a second time here.
            SelectedEntity = FilteredEntities.FirstOrDefault() ?? Entities.FirstOrDefault();
            if (SelectedEntity is null)
            {
                RebuildPayload(); // nothing to select yet — block rather than leaving the raw body sendable
            }
        }
        else
        {
            ReloadGrid();
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
            ReloadGrid();
        }
    }

    // Shows what's cached for the selected entity, then fetches its fields when the grid came up empty (they
    // weren't cached) so the "hasn't loaded" block clears once they arrive.
    private void ReloadGrid()
    {
        // A fresh selection starts with a clean slate for the load-error banner — whatever this reload
        // finds (a cache hit needing no fetch, or the fetch EnsureFieldsAsync is about to run) owns
        // LoadError from here. Without this, switching away from an entity whose fields failed to load
        // left that entity's error banner showing over a DIFFERENT, healthy (already-cached) entity that
        // never triggers a fetch at all (PR #196 review).
        LoadError = null;
        LoadFields();
        RebuildPayload();
        if (Fields.Count == 0)
        {
            // Read off the grid rather than asking the metadata service a second time, so selecting an
            // already-cached entity doesn't pay for an extra lookup.
            EnsureFieldsCommand.Execute(null);
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
            var (editor, members) = ResolveEditor(f);
            var row = new PostFieldRow(f.Name, f.TypeDisplay, mandatory, f.IsKey, include: mandatory, editor, members);
            // Seed sensible defaults so a write starts closer to ready (mirrors the prototype): the
            // company code, and a concrete false for Booleans. Set before subscribing so it doesn't
            // trigger a per-field payload rebuild (RebuildPayload runs once after the grid is built).
            if (string.Equals(f.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase))
            {
                row.Value = "USMF"; // company codes are conventionally uppercase (matches the raw-body template)
            }
            else if (editor == PostFieldEditor.Bool)
            {
                row.Value = "false";
            }

            row.PropertyChanged += OnFieldChanged;
            Fields.Add(row);
        }

        OnPropertyChanged(nameof(IncludedFieldCount));
        OnPropertyChanged(nameof(GridSummary));
    }

    // Chooses the Value-cell editor for a field: a dropdown for enums (when the members are known),
    // a checkbox for Booleans, otherwise a text box.
    private (PostFieldEditor Editor, IReadOnlyList<string>? Members) ResolveEditor(EntityField f)
    {
        if (string.Equals(f.Type, "Enum", StringComparison.Ordinal) && f.EnumType is not null)
        {
            var members = _metadata.GetEnumMembers(f.EnumType);
            if (members is { Count: > 0 })
            {
                return (PostFieldEditor.Enum, members);
            }
        }

        if (string.Equals(f.Type, "Boolean", StringComparison.Ordinal))
        {
            return (PostFieldEditor.Bool, null);
        }

        return (PostFieldEditor.Text, null);
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PostFieldRow.Include) or nameof(PostFieldRow.Value))
        {
            RebuildPayload();
            OnPropertyChanged(nameof(RequestUrl)); // a key value may have changed the target predicate
            if (e.PropertyName == nameof(PostFieldRow.Include))
            {
                OnPropertyChanged(nameof(IncludedFieldCount));
                OnPropertyChanged(nameof(GridSummary));
            }
        }
    }

    // Turns the grid into a validated, type-coerced JSON body via the shared Core payload builder.
    // POST enforces mandatory fields; PATCH/DELETE relax that (partial updates / key-only).
    private void RebuildPayload()
    {
        if (!UseFieldGrid)
        {
            return; // raw mode: the user owns the body, and CanSend doesn't gate on payload issues
        }

        // The grid can only build a body from loaded field metadata. Until that's there — no entity picked,
        // or the entity's fields aren't cached — block rather than return: leaving the PREVIOUS entity's body
        // behind an empty issue list kept Send enabled, so a CustomersV3 payload could be POSTed to
        // /data/VendorsV2. Worse for a keyed write: with no fields there's no key predicate, BasePath falls
        // back to the collection URL and the keyed-write guard below never runs, so the confirm dialog would
        // promise a targeted DELETE for a request carrying no record identity at all. See issue #155.
        var selected = SelectedEntity;
        var fields = selected is null ? null : _metadata.GetFields(selected.Name);
        if (selected is null || fields is null || Fields.Count == 0)
        {
            BlockPayload(selected is null
                ? "No entity is selected — pick one to build the payload."
                : $"Field metadata for {selected.Name} hasn't loaded — the payload can't be built yet.");
            return;
        }

        var entity = PostPayloadMapper.ToEntity(selected.Name, fields);
        // For PATCH/DELETE the key fields identify the record in the URL predicate, so they're excluded
        // from the body (sending key fields in a PATCH body is redundant and some services reject it).
        var keyedMethod = IsKeyedMethod(Method);
        var values = Fields
            .Select(r => new ODataFieldValue(r.Name, r.Include && !(keyedMethod && r.IsKey), r.Value))
            .ToList();
        var enforceMandatory = string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase);
        // On a PATCH the Include checkbox carries the omit/clear distinction: unchecked = leave the field
        // alone, checked-but-blank = clear it (the builder emits an explicit null, which the payload preview
        // then shows — the visible truth about what will be written). A POST has no such distinction, and a
        // DELETE sends no body at all (see Send), so both keep the omit-blanks reading.
        var isPatch = string.Equals(Method, "PATCH", StringComparison.OrdinalIgnoreCase);

        var result = ODataPayloadBuilder.BuildPayloadJson(entity, values,
            enforceMandatory: enforceMandatory, blankIncludedMeansNull: isPatch);
        var issues = new List<string>(result.Issues);

        // A PATCH body of "{}" is a request that changes nothing — and F&O answers it 204, so the success
        // badge went green over a write that never happened. Block it instead. DELETE is exempt: the send
        // path drops its body entirely, so an empty object there is by design, not a silent no-op.
        if (isPatch && result.Ok && BodyHasNoProperties(result.Json!))
        {
            issues.Add("No fields included — this PATCH would send an empty body and change nothing.");
        }

        // A keyed write must target a specific record. Since the keys are excluded from the body, an
        // incomplete key would otherwise produce a request with the record identity in neither the URL
        // nor the body — so flag it (which disables Send via CanSend) until every key value is present.
        // (Uses the already-fetched fields + the grid rows; no extra metadata lookup.)
        if (keyedMethod)
        {
            var keyNames = fields.Where(f => f.IsKey).Select(f => f.Name).ToList();
            if (keyNames.Count == 0)
            {
                // No key fields at all: BuildKeyPredicate can never produce one, so BasePath falls back to
                // the bare collection URL — a DELETE/PATCH would 405 there, but with nothing flagged the
                // confirm dialog still claimed "the targeted record will be removed". Block instead of
                // sending a keyed write with no addressing at all.
                issues.Add($"{selected.Name} declares no key fields — a {Method} can't target a record. " +
                    "Use raw mode if you know the service's addressing.");
            }
            else
            {
                var keyIncomplete = keyNames.Any(kn =>
                    string.IsNullOrEmpty(Fields.FirstOrDefault(r => string.Equals(r.Name, kn, StringComparison.Ordinal))?.Value?.Trim()));
                if (keyIncomplete)
                {
                    issues.Add($"Enter all key values ({string.Join(", ", keyNames)}) to target the record for {Method}.");
                }
            }
        }

        PayloadIssueCount = issues.Count;
        if (issues.Count == 0)
        {
            RequestBody = result.Json!;
            PayloadIssues = string.Empty;
        }
        else
        {
            // Keep a valid body visible when the only problem is an incomplete key (the body itself is
            // fine); otherwise clear it so a stale/default payload can't be sent while the grid is invalid.
            RequestBody = result.Ok ? result.Json! : string.Empty;
            PayloadIssues = string.Join(Environment.NewLine, issues);
        }
    }

    // True when the built body is an object carrying no properties — "{}" however it happens to be formatted.
    // Parsed rather than string-compared so a change to the builder's serializer options can't quietly turn
    // the empty-PATCH guard off, which would fail open (an empty PATCH sendable again). Only ever called on
    // the builder's own successful output, so the JSON is known-valid.
    private static bool BodyHasNoProperties(string json) =>
        JsonNode.Parse(json) is JsonObject obj && obj.Count == 0;

    // Grid mode with no usable field metadata: clear the body and raise a single blocking issue (which
    // disables Send via CanSend), naming the load failure as the cause when there was one. Raises the target
    // URL alongside the observable writes — without fields there's no key predicate, so the target changed too.
    private void BlockPayload(string issue)
    {
        var cause = _loader.LastError;
        PayloadIssueCount = 1;
        PayloadIssues = cause is null ? issue : $"{issue} ({cause})";
        RequestBody = string.Empty;
        OnPropertyChanged(nameof(RequestUrl));
    }

    // In grid mode an invalid payload must not be sent (the body is blank and the issues are shown);
    // in raw mode the user owns the body, so there's nothing to gate on.
    private bool CanSend() => !(UseFieldGrid && HasPayloadIssues);

    // The If-Match header for a PATCH/DELETE when enabled (optimistic concurrency); null otherwise.
    private IReadOnlyDictionary<string, string>? BuildHeaders() =>
        UseIfMatch && IsKeyedMethod(Method) && !string.IsNullOrWhiteSpace(IfMatch)
            ? new Dictionary<string, string> { ["If-Match"] = IfMatch.Trim() }
            : null;

    // IncludeCancelCommand: surfaces SendCancelCommand and lets the generated AsyncRelayCommand carry
    // the token's lifecycle, so an in-flight send can be cancelled on navigate-away/shutdown.
    // Confirm-on-mutation: every send is a live write, so gate it behind a confirm dialog (PATCH/DELETE
    // are styled destructive, with a caveat). Mirrors the Operations screen's confirm-on-mutation rule.
    private Task<bool> ConfirmSendAsync()
    {
        var danger = IsKeyedMethod(Method);
        var caveat = string.Equals(Method, "DELETE", StringComparison.OrdinalIgnoreCase)
            ? "Delete is permanent — the targeted record will be removed."
            : string.Equals(Method, "PATCH", StringComparison.OrdinalIgnoreCase)
                ? "Patch overwrites the targeted record's fields."
                : null;

        return _dialogs.ConfirmAsync(new ConfirmRequest(
            Title: $"Send {Method}?",
            Message: $"Sends a live {Method} request to the selected environment.",
            Targets: new[] { EffectivePath() },
            ConfirmLabel: $"Send {Method}",
            IsDanger: danger,
            Caveat: caveat));
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanSend))]
    private async Task Send(CancellationToken ct)
    {
        if (!await ConfirmSendAsync())
        {
            StatusText = "Send cancelled.";
            return;
        }

        IsBusy = true;
        StatusText = "Sending…";
        // Clear the PREVIOUS send's outcome up front — a cancellation never gets a real response to
        // overwrite these with, so without this reset its "Send cancelled." status was left sitting over
        // an unrelated earlier send's badge/body/headers, misreadable as this send's own result
        // (PR #196 review).
        SendSucceeded = false;
        StatusBadge = string.Empty;
        ResponseBody = string.Empty;
        ResponseHeaders = string.Empty;
        try
        {
            var body = string.Equals(Method, "DELETE", StringComparison.OrdinalIgnoreCase) ? null : RequestBody;
            var response = await _client.SendAsync(Method, EffectivePath(), body, BuildHeaders(), ct);
            StatusText = response.StatusLine;
            StatusBadge = $"{response.StatusCode} {response.ReasonPhrase}";
            SendSucceeded = response.IsSuccess;
            ResponseBody = response.Body;
            ResponseHeaders = FormatHeaders(response.Headers);
        }
        // A cancelled send is not a failed one. CoreODataClient rethrows genuine cancellation instead of
        // folding it into a response (so the view models' own cancellation handling can actually run) —
        // without this clause that arrived as a bare Exception here and got misreported as "Request
        // failed." Gate on OUR token: an HTTP/socket timeout also surfaces as an OperationCanceledException
        // but with the caller's token still live — only a cancelled token means the user pressed Cancel; a
        // timeout falls through to the general handler and is reported as the failure it is (#168).
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SendSucceeded = false;
            StatusText = "Send cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Request failed.";
            SendSucceeded = false;
            StatusBadge = string.Empty;
            ResponseBody = ex.Message;
            ResponseHeaders = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Renders response headers as "Name: value" lines, sorted for stable display.
    private static string FormatHeaders(IReadOnlyDictionary<string, string>? headers) =>
        headers is null || headers.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}: {h.Value}"));

    [RelayCommand]
    private Task CopyUrl() => CopyToClipboardAsync(EffectivePath(), "Request URL copied to the clipboard.");

    [RelayCommand]
    private Task CopyPayload() => CopyToClipboardAsync(RequestBody, "Payload copied to the clipboard.");

    // A contended clipboard throws (COMException on Windows) and an AsyncRelayCommand rethrows that on the
    // dispatcher, so a failed copy has to end as a status line, not a dead app (#163).
    private async Task CopyToClipboardAsync(string text, string success)
    {
        try
        {
            await _clipboard.SetTextAsync(text);
            StatusText = success;
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't copy to the clipboard: {ex.Message}";
        }
    }
}
