using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace QueryBuilderPlugin;

public sealed class QueryBuilderViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly ObservableCollection<string> _entities = new();
    private readonly ObservableCollection<string> _fields = new();
    private string? _selectedEntity;
    private readonly ObservableCollection<string> _selectedFields = new();
    private string? _orderBy;
    private bool _crossCompany = true;
    private string? _company;
    private bool _count;
    private string _status = "Ready";

    public QueryBuilderViewModel(IPluginContext ctx)
    {
        _ctx = ctx;
        Entities = new ReadOnlyObservableCollection<string>(_entities);
        Fields = new ReadOnlyObservableCollection<string>(_fields);
        SelectedFields = new ReadOnlyObservableCollection<string>(_selectedFields);
        LoadEntitiesCommand = new AsyncRelayCommand(LoadEntitiesAsync);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync);
    }

    public ReadOnlyObservableCollection<string> Entities { get; }
    public ReadOnlyObservableCollection<string> Fields { get; }
    public ReadOnlyObservableCollection<string> SelectedFields { get; }

    public string? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (_selectedEntity != value)
            {
                _selectedEntity = value;
                OnPropertyChanged();
                _fields.Clear();
                _selectedFields.Clear();
            }
        }
    }

    public string? OrderBy
    {
        get => _orderBy;
        set { _orderBy = value; OnPropertyChanged(); }
    }

    public bool CrossCompany
    {
        get => _crossCompany;
        set { _crossCompany = value; OnPropertyChanged(); }
    }

    public string? Company
    {
        get => _company;
        set { _company = value; OnPropertyChanged(); }
    }

    public bool Count
    {
        get => _count;
        set { _count = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public ICommand LoadEntitiesCommand { get; }
    public ICommand PreviewCommand { get; }

    private async Task LoadEntitiesAsync(CancellationToken cancellationToken)
    {
        Status = "Loading entities...";
        // Placeholder: static list until metadata provider is wired into context.
        _entities.Clear();
        _entities.Add("Customers");
        _entities.Add("Vendors");
        _entities.Add("SalesOrders");
        Status = "Ready";
    }

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedEntity))
        {
            Status = "Select an entity to preview.";
            return;
        }

        Status = "Running query...";
        var spec = new QuerySpec(
            Entity: SelectedEntity!,
            CrossCompany: CrossCompany,
            Company: Company,
            Select: SelectedFields.ToList(),
            OrderBy: OrderBy,
            Count: Count);

        var request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
        // Placeholder: no grid binding yet; just update status.
        Status = request.Url;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly CancellationTokenSource _cts = new();

    public AsyncRelayCommand(Func<CancellationToken, Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(_cts.Token);
        }
        catch
        {
            // swallow for now; could log via context
        }
    }
}
