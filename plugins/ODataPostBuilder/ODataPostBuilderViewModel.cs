using FoToolbox.Core.OData;
using FoToolbox.Core.Profiles;
using FoToolbox.SDK.Collections;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Data;

namespace ODataPostBuilderPlugin;

public sealed partial class ODataPostBuilderViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly IPluginContextWrite? _ctxWrite;
    private readonly SavedApiRequestStore _savedStore;

    private readonly BulkObservableCollection<EntityItem> _entities = new();
    private readonly ObservableCollection<PostFieldItem> _fields = new();
    private readonly ObservableCollection<BatchOperationItem> _batchOperations = new();
    private readonly ObservableCollection<SavedApiRequestItem> _savedRequests = new();

    private Dictionary<string, IReadOnlyList<string>> _enumMembersByType = new(StringComparer.OrdinalIgnoreCase);
    private FoToolbox.Core.OData.ODataEntity? _selectedEntityDetails;

    private string? _entitySearch;
    private EntityItem? _selectedEntityItem;
    private string _entityLoadStatus = "Metadata not loaded.";
    private string _selectedEntitySummary = "No entity selected.";

    private string _selectedMethod = "POST";
    private string _apiUrl = string.Empty;
    private bool _crossCompany;
    private bool _useIfMatchStar;
    private string? _ifMatchCustom;

    private string _payloadJson = string.Empty;
    private string _payloadStatus = "No payload yet.";

    private string _sendStatus = string.Empty;
    private string _responseDetails = "No response yet.";

    private SavedApiRequestItem? _selectedSavedRequest;
    private string _savedStatus = "No saved requests loaded.";

    private BatchOperationItem? _selectedBatchOperation;
    private string _batchUrl = string.Empty;
    private string _batchContentType = string.Empty;
    private string _batchBodyPreview = string.Empty;
    private string _batchSendStatus = string.Empty;
    private string _batchResponseDetails = "No batch response yet.";

    private string _status = "Ready";
    private bool _isBusy;
    private bool _loadingEntities;
    private bool _confirmedThisSession;
    private CancellationTokenSource? _entitySearchCts;
    private CancellationTokenSource? _selectedEntityDetailsCts;
    private string? _pendingNavigationEntity;

    public ODataPostBuilderViewModel(IPluginContext ctx)
    {
        _ctx = ctx;
        _ctxWrite = ctx as IPluginContextWrite;
        _savedStore = new SavedApiRequestStore(ProfilePaths.ResolveProfileDbPath(), ctx.Logger);

        Entities = new ReadOnlyObservableCollection<EntityItem>(_entities);
        Fields = new ReadOnlyObservableCollection<PostFieldItem>(_fields);
        BatchOperations = _batchOperations;
        SavedRequests = new ReadOnlyObservableCollection<SavedApiRequestItem>(_savedRequests);

        EntitiesView = CollectionViewSource.GetDefaultView(_entities);
        EntitiesView.Filter = EntityFilter;

        Methods = new ReadOnlyObservableCollection<string>(new ObservableCollection<string>(new[] { "POST", "PATCH", "DELETE" }));

        _batchOperations.CollectionChanged += BatchOperationsChanged;

        Action<Exception> onCommandError = ex =>
        {
            _ctx.Logger.LogError(ex, "ODataPostBuilder command failed.");
            Status = $"Command failed: {ex.Message}";
        };

        LoadEntitiesCommand = new AsyncRelayCommand(LoadEntitiesAsync, onCommandError);
        SendCommand = new AsyncRelayCommand(SendAsync, onCommandError);
        CopyPayloadCommand = new RelayCommand(_ => CopyPayload());
        CopyUrlCommand = new RelayCommand(_ => CopyUrl());

        LoadSavedRequestsCommand = new AsyncRelayCommand(LoadSavedRequestsAsync, onCommandError);
        SaveCurrentRequestCommand = new AsyncRelayCommand(SaveCurrentRequestAsync, onCommandError);
        LoadSelectedRequestCommand = new RelayCommand(_ => LoadSelectedRequest());
        RenameSelectedRequestCommand = new AsyncRelayCommand(RenameSelectedRequestAsync, onCommandError);
        DeleteSelectedRequestCommand = new AsyncRelayCommand(DeleteSelectedRequestAsync, onCommandError);
        ExportSelectedRequestCommand = new RelayCommand(_ => ExportSelectedRequest());
        ExportAllRequestsCommand = new RelayCommand(_ => ExportAllRequests());
        ImportRequestsCommand = new AsyncRelayCommand(ImportRequestsAsync, onCommandError);

        AddCurrentToBatchCommand = new RelayCommand(_ => AddCurrentToBatch());
        RemoveSelectedBatchOpCommand = new RelayCommand(_ => RemoveSelectedBatchOp());
        ClearBatchCommand = new RelayCommand(_ => ClearBatch());
        CopyBatchCommand = new RelayCommand(_ => CopyBatch());
        SendBatchCommand = new AsyncRelayCommand(SendBatchAsync, onCommandError);

        UpdateIfMatchDefaults();

        _ = LoadSavedRequestsAsync(CancellationToken.None);
    }

    public ReadOnlyObservableCollection<EntityItem> Entities { get; }
    public ICollectionView EntitiesView { get; }
    public ReadOnlyObservableCollection<PostFieldItem> Fields { get; }
    public ReadOnlyObservableCollection<string> Methods { get; }
    public ObservableCollection<BatchOperationItem> BatchOperations { get; }
    public ReadOnlyObservableCollection<SavedApiRequestItem> SavedRequests { get; }

    public AsyncRelayCommand LoadEntitiesCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand CopyPayloadCommand { get; }
    public RelayCommand CopyUrlCommand { get; }

    public AsyncRelayCommand LoadSavedRequestsCommand { get; }
    public AsyncRelayCommand SaveCurrentRequestCommand { get; }
    public RelayCommand LoadSelectedRequestCommand { get; }
    public AsyncRelayCommand RenameSelectedRequestCommand { get; }
    public AsyncRelayCommand DeleteSelectedRequestCommand { get; }
    public RelayCommand ExportSelectedRequestCommand { get; }
    public RelayCommand ExportAllRequestsCommand { get; }
    public AsyncRelayCommand ImportRequestsCommand { get; }

    public RelayCommand AddCurrentToBatchCommand { get; }
    public RelayCommand RemoveSelectedBatchOpCommand { get; }
    public RelayCommand ClearBatchCommand { get; }
    public RelayCommand CopyBatchCommand { get; }
    public AsyncRelayCommand SendBatchCommand { get; }

    public string EntityLoadStatus { get => _entityLoadStatus; private set { _entityLoadStatus = value; OnPropertyChanged(); } }
    public string? EntitySearch { get => _entitySearch; set { if (_entitySearch != value) { _entitySearch = value; OnPropertyChanged(); ScheduleEntitySearchRefresh(); } } }
    public EntityItem? SelectedEntityItem { get => _selectedEntityItem; set { if (_selectedEntityItem != value) { _selectedEntityItem = value; OnPropertyChanged(); StartLoadSelectedEntityDetails(); } } }
    public string SelectedEntitySummary { get => _selectedEntitySummary; private set { _selectedEntitySummary = value; OnPropertyChanged(); } }

    public string SelectedMethod { get => _selectedMethod; set { var norm = NormalizeMethod(value); if (_selectedMethod != norm) { _selectedMethod = norm; OnPropertyChanged(); UpdateIfMatchDefaults(); RebuildPayloadPreview(); OnPropertyChanged(nameof(SendButtonText)); OnPropertyChanged(nameof(IsCrossCompanyApplicable)); } } }
    public string ApiUrl { get => _apiUrl; set { if (_apiUrl != value) { _apiUrl = value; OnPropertyChanged(); } } }
    public bool CrossCompany { get => _crossCompany; set { if (_crossCompany != value) { _crossCompany = value; OnPropertyChanged(); } } }
    public bool IsCrossCompanyApplicable => SelectedMethod is "PATCH" or "DELETE";
    public bool UseIfMatchStar { get => _useIfMatchStar; set { if (_useIfMatchStar != value) { _useIfMatchStar = value; OnPropertyChanged(); } } }
    public string? IfMatchCustom { get => _ifMatchCustom; set { if (_ifMatchCustom != value) { _ifMatchCustom = value; OnPropertyChanged(); } } }

    public string PayloadJson { get => _payloadJson; private set { _payloadJson = value; OnPropertyChanged(); } }
    public string PayloadStatus { get => _payloadStatus; private set { _payloadStatus = value; OnPropertyChanged(); } }
    public string SendButtonText => SelectedMethod switch { "PATCH" => "Send PATCH", "DELETE" => "Send DELETE", _ => "Send POST" };
    public string SendStatus { get => _sendStatus; private set { _sendStatus = value; OnPropertyChanged(); } }
    public string ResponseDetails { get => _responseDetails; private set { _responseDetails = value; OnPropertyChanged(); } }

    public SavedApiRequestItem? SelectedSavedRequest { get => _selectedSavedRequest; set { _selectedSavedRequest = value; OnPropertyChanged(); } }
    public string SavedStatus { get => _savedStatus; private set { _savedStatus = value; OnPropertyChanged(); } }

    public BatchOperationItem? SelectedBatchOperation { get => _selectedBatchOperation; set { _selectedBatchOperation = value; OnPropertyChanged(); } }
    public string BatchUrl { get => _batchUrl; private set { _batchUrl = value; OnPropertyChanged(); } }
    public string BatchContentType { get => _batchContentType; private set { _batchContentType = value; OnPropertyChanged(); } }
    public string BatchBodyPreview { get => _batchBodyPreview; private set { _batchBodyPreview = value; OnPropertyChanged(); } }
    public string BatchSendStatus { get => _batchSendStatus; private set { _batchSendStatus = value; OnPropertyChanged(); } }
    public string BatchResponseDetails { get => _batchResponseDetails; private set { _batchResponseDetails = value; OnPropertyChanged(); } }

    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Methods are implemented in other partial files.

    private static string NormalizeMethod(string method)
    {
        var m = (method ?? string.Empty).Trim().ToUpperInvariant();
        return m is "POST" or "PATCH" or "DELETE" ? m : "POST";
    }
}
