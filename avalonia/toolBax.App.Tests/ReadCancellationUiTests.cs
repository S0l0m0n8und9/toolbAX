using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ToolBax.App.Views;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class ReadCancellationUiTests
{
    private sealed class Metadata : IMetadataService
    {
        public IReadOnlyList<EntitySet> Entities { get; set; } = new[] { new EntitySet("Old", "", 1, "Id", false, "") };
        public IReadOnlyList<EntityField>? Fields { get; set; } = new[] { new EntityField("Id", "String", false) };
        public Func<CancellationToken, Task> Load { get; set; } = _ => Task.CompletedTask;
        public Func<string, CancellationToken, Task<bool>> LoadFields { get; set; } = (_, _) => Task.FromResult(true);
        public int EntityCalls { get; private set; }
        public int FieldCalls { get; private set; }
        public int EntityReads { get; private set; }
        public int FieldReads { get; private set; }
        public void Invalidate() { }
        public IReadOnlyList<EntitySet> GetEntities() { EntityReads++; return Entities; }
        public IReadOnlyList<EntityField>? GetFields(string entityName) { FieldReads++; return Fields; }
        public Task LoadEntitiesAsync(CancellationToken ct = default) { EntityCalls++; return Load(ct); }
        public Task<bool> LoadFieldsAsync(string entityName, CancellationToken ct = default) { FieldCalls++; return LoadFields(entityName, ct); }
    }

    [Fact]
    public async Task Loader_precancelled_calls_do_not_read_or_fetch_metadata()
    {
        var metadata = new Metadata();
        using var loader = new EntityCatalogLoader(metadata);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Null(await loader.LoadEntitiesAsync(Array.Empty<string>(), cts.Token));
        Assert.False(await loader.EnsureFieldsAsync("A", cts.Token));
        Assert.Equal(0, metadata.EntityCalls + metadata.FieldCalls + metadata.EntityReads + metadata.FieldReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Loader_cancelled_ignoring_result_does_not_publish_success_error_or_getters(bool fields, bool fault)
    {
        var metadata = new Metadata { Fields = null, Load = _ => throw new InvalidOperationException("prior error") };
        using var loader = new EntityCatalogLoader(metadata);
        await loader.LoadEntitiesAsync(Array.Empty<string>(), TestContext.Current.CancellationToken);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        metadata.Load = _ => gate.Task;
        metadata.LoadFields = (_, _) => gate.Task;
        using var cts = new CancellationTokenSource();
        Task pending = fields ? loader.EnsureFieldsAsync("A", cts.Token) : loader.LoadEntitiesAsync(Array.Empty<string>(), cts.Token);
        var reads = metadata.EntityReads + metadata.FieldReads;
        cts.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late error"));
        else gate.SetResult(true);
        await pending;
        Assert.Equal("prior error", loader.LastError);
        Assert.Equal(reads, metadata.EntityReads + metadata.FieldReads);
        if (fields) Assert.False(await (Task<bool>)pending);
        else Assert.Null(await (Task<IReadOnlyList<EntitySet>?>)pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loader_A_B_A_older_completion_cannot_replace_newest_error(bool oldFault)
    {
        var gates = new List<TaskCompletionSource<bool>>();
        var metadata = new Metadata { Fields = null, LoadFields = (_, _) => {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gates.Add(gate);
            return gate.Task;
        } };
        using var loader = new EntityCatalogLoader(metadata);
        var a1 = loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken);
        var b = loader.EnsureFieldsAsync("B", TestContext.Current.CancellationToken);
        var a2 = loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken);
        gates[2].SetException(new InvalidOperationException("newest error"));
        Assert.False(await a2);
        if (oldFault) gates[0].SetException(new InvalidOperationException("old error"));
        else gates[0].SetResult(true);
        gates[1].SetResult(true);
        Assert.False(await a1);
        Assert.False(await b);
        Assert.Equal("newest error", loader.LastError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loader_live_token_timeout_is_reported_as_failure(bool fields)
    {
        var metadata = new Metadata
        {
            Fields = null,
            Load = _ => throw new OperationCanceledException("timeout"),
            LoadFields = (_, _) => throw new OperationCanceledException("timeout")
        };
        using var loader = new EntityCatalogLoader(metadata);
        if (fields) Assert.False(await loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken));
        else Assert.Null(await loader.LoadEntitiesAsync(Array.Empty<string>(), TestContext.Current.CancellationToken));
        Assert.Contains("timeout", loader.LastError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_refresh_cancel_does_not_paint_ignoring_results_or_errors(bool fault)
    {
        var metadata = new Metadata();
        using var vm = new MetadataViewModel(metadata);
        vm.LoadError = "prior error";
        var before = vm.Entities.ToArray();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metadata.Load = async _ => { await gate.Task; metadata.Entities = Array.Empty<EntitySet>(); };
        var refresh = vm.RefreshCommand.ExecuteAsync(null);
        vm.RefreshCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late error"));
        else gate.SetResult();
        await refresh;
        Assert.Equal(before, vm.Entities);
        Assert.Equal("prior error", vm.LoadError);
        Assert.False(vm.IsBusy);
    }

    private sealed class VirtualReader : IVirtualTableReader
    {
        public Func<CancellationToken, Task<VirtualTableLoadResult>> Read { get; set; } = ct => new FakeVirtualTableReader().GetVirtualTablesAsync(ct);
        public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default) => Read(ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Virtual_refresh_cancel_preserves_prior_rows_and_error(bool fault)
    {
        var reader = new VirtualReader();
        using var vm = new VirtualTablesViewModel(reader);
        await vm.InitializeCommand.ExecuteAsync(null);
        var before = vm.Tables.ToArray();
        vm.LoadError = "prior error";
        var gate = new TaskCompletionSource<VirtualTableLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Read = _ => gate.Task;
        var refresh = vm.RefreshCommand.ExecuteAsync(null);
        vm.RefreshCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late error"));
        else gate.SetResult(VirtualTableLoadResult.Ok(Array.Empty<VirtualTableInfo>()));
        await refresh;
        Assert.Equal(before, vm.Tables);
        Assert.Equal("prior error", vm.LoadError);
        Assert.False(vm.IsLoading);
    }

    private sealed class Client : IODataClient
    {
        public Func<CancellationToken, Task<ODataResponse>> Read { get; set; } = _ => Task.FromResult(new ODataResponse(200, "OK", "{\"value\":[{\"Id\":1}]}", 1));
        public int Calls { get; private set; }
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default) { Calls++; return Read(ct); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_cancelled_ignoring_run_preserves_prior_rows_without_late_failure(bool fault)
    {
        var client = new Client();
        using var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        await vm.RunCommand.ExecuteAsync(null);
        var rows = vm.ResultRows.ToArray();
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Read = _ => gate.Task;
        var run = vm.RunCommand.ExecuteAsync(null);
        vm.RunCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late error"));
        else gate.SetResult(new ODataResponse(200, "OK", "{\"value\":[]}", 1));
        await run;
        Assert.Equal(rows, vm.ResultRows);
        Assert.DoesNotContain("late error", vm.StatusText);
        Assert.Contains("cancelled", vm.StatusText);
        Assert.False(vm.IsBusy);
    }
    private sealed class SaveFile : IFileSaveService
    {
        public int Calls { get; private set; }
        public Action? OnSave { get; set; }
        public Task<string?> SaveTextAsync(string name, string content, SaveFileType type, CancellationToken ct = default)
        {
            Calls++;
            OnSave?.Invoke();
            return Task.FromResult<string?>("saved.csv");
        }
        public Task<string?> SaveStreamAsync(string name, Stream content, SaveFileType type,
            CancellationToken ct = default)
        {
            Calls++;
            OnSave?.Invoke();
            return Task.FromResult<string?>("saved.csv");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_cancelled_page_does_not_append_fetch_next_or_save(bool export)
    {
        var client = new Client
        {
            Read = _ => Task.FromResult(new ODataResponse(200, "OK", "{\"value\":[{\"Id\":1}],\"@odata.nextLink\":\"https://example.test/data/next\"}", 1))
        };
        var save = new SaveFile();
        using var vm = new QueryBuilderViewModel(new FakeMetadataService(), client, fileSave: save);
        await vm.RunCommand.ExecuteAsync(null);
        var rows = vm.ResultRows.ToArray();
        var gate = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Read = _ => gate.Task;
        var command = export ? vm.ExportAllCsvCommand : vm.LoadMoreCommand;
        var pending = command.ExecuteAsync(null);
        command.Cancel();
        gate.SetResult(new ODataResponse(200, "OK", "{\"value\":[{\"Id\":2}],\"@odata.nextLink\":\"https://example.test/data/third\"}", 1));
        await pending;
        Assert.Equal(rows, vm.ResultRows);
        Assert.Equal(2, client.Calls);
        Assert.Equal(0, save.Calls);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Query_cancel_during_committed_file_save_retains_saved_result()
    {
        var save = new SaveFile();
        using var vm = new QueryBuilderViewModel(new FakeMetadataService(), new Client(), fileSave: save);
        save.OnSave = vm.ExportAllCsvCommand.Cancel;
        await vm.ExportAllCsvCommand.ExecuteAsync(null);
        Assert.Equal(1, save.Calls);
        Assert.Contains("Saved", vm.StatusText);
        Assert.DoesNotContain("cancelled", vm.StatusText);
    }

    [Fact]
    public async Task Query_old_run_cannot_clear_new_run_busy_or_paint()
    {
        var client = new Client();
        var first = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ODataResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Read = _ => client.Calls == 1 ? first.Task : second.Task;
        using var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
        var old = vm.RunCommand.ExecuteAsync(null);
        var current = vm.RunCommand.ExecuteAsync(null);
        first.SetResult(new ODataResponse(200, "OK", "{\"value\":[{\"Id\":1}]}", 1));
        await old;
        Assert.True(vm.IsBusy);
        Assert.Empty(vm.ResultRows);
        second.SetResult(new ODataResponse(200, "OK", "{\"value\":[{\"Id\":2}]}", 1));
        await current;
        Assert.False(vm.IsBusy);
        Assert.Single(vm.ResultRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_old_catalogue_cannot_clear_new_owner_busy(bool fault)
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new Metadata();
        metadata.Load = _ => metadata.EntityCalls == 1 ? first.Task : second.Task;
        using var vm = new MetadataViewModel(metadata);
        var old = vm.InitializeCommand.ExecuteAsync(null);
        var current = vm.RefreshCommand.ExecuteAsync(null);
        if (fault) first.SetException(new InvalidOperationException("old error"));
        else first.SetResult();
        await old;
        Assert.True(vm.IsBusy);
        Assert.Null(vm.LoadError);
        second.SetResult();
        await current;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Virtual_old_read_cannot_clear_new_owner_loading()
    {
        var first = new TaskCompletionSource<VirtualTableLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<VirtualTableLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var reader = new VirtualReader { Read = _ => ++calls == 1 ? first.Task : second.Task };
        using var vm = new VirtualTablesViewModel(reader);
        var old = vm.InitializeCommand.ExecuteAsync(null);
        var current = vm.RefreshCommand.ExecuteAsync(null);
        first.SetResult(VirtualTableLoadResult.Ok(Array.Empty<VirtualTableInfo>()));
        await old;
        Assert.True(vm.IsLoading);
        second.SetResult(await new FakeVirtualTableReader().GetVirtualTablesAsync(TestContext.Current.CancellationToken));
        await current;
        Assert.False(vm.IsLoading);
        Assert.NotEmpty(vm.Tables);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_cancelled_initial_catalogue_keeps_payload_and_stops_fields(bool fault)
    {
        var metadata = new Metadata();
        using var vm = new PostBuilderViewModel(new Client(), metadata: metadata);
        vm.RequestBody = "keep editable body";
        vm.LoadError = "prior error";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metadata.Load = _ => gate.Task;
        var pending = vm.InitializeCommand.ExecuteAsync(null);
        var reads = metadata.FieldReads;
        vm.InitializeCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late error"));
        else gate.SetResult();
        await pending;
        Assert.Equal("keep editable body", vm.RequestBody);
        Assert.Equal("prior error", vm.LoadError);
        Assert.Equal(reads, metadata.FieldReads);
        Assert.Equal(0, metadata.FieldCalls);
    }

    private sealed class Maps : IDualWriteMapReader
    {
        public Func<CancellationToken, Task<DwSolutionLoadResult>> Solutions { get; set; } = ct => new FakeDualWriteMapReader().GetSolutionsAsync(ct);
        public Func<CancellationToken, Task<DwMapLoadResult>> Read { get; set; } = ct => new FakeDualWriteMapReader().GetMapsAsync(ct: ct);
        public Func<CancellationToken, Task<DwCountResult>> Count { get; set; } = _ => Task.FromResult(DwCountResult.Ok(100));
        public int MapCalls { get; private set; }
        public int CountCalls { get; private set; }
        public Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default) => Solutions(ct);
        public Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default) { MapCalls++; return Read(ct); }
        public Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? filter, CancellationToken ct = default) { CountCalls++; return Count(ct); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Map_cancelled_ce_leg_stops_fo_and_keeps_prior_count(bool fault)
    {
        var reader = new Maps();
        var client = new Client();
        using var vm = new DualWriteMapViewModel(reader, odata: client, metadata: new FakeMetadataService());
        await vm.InitializeCommand.ExecuteAsync(null);
        var row = vm.CountRows.First();
        row.CeCount = 7;
        var gate = new TaskCompletionSource<DwCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Count = _ => gate.Task;
        var pending = vm.CountAllRowsCommand.ExecuteAsync(null);
        vm.CountAllRowsCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late count error"));
        else gate.SetResult(DwCountResult.Ok(999));
        await pending;
        Assert.Equal(7, row.CeCount);
        Assert.Equal(0, client.Calls);
        Assert.Equal(1, reader.CountCalls);
        Assert.DoesNotContain("Counting", row.CeStatus);
        Assert.DoesNotContain("late count error", vm.LoadError ?? "");
    }

    [Fact]
    public async Task Map_cancelled_fo_field_lookup_stops_http()
    {
        var reader = new Maps();
        var client = new Client();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new Metadata { Fields = null, LoadFields = (_, _) => gate.Task };
        using var vm = new DualWriteMapViewModel(reader, odata: client, metadata: metadata);
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.CountRows.First().FoEntity = "CustomersV3";
        var pending = vm.CountAllRowsCommand.ExecuteAsync(null);
        Assert.Equal(1, metadata.FieldCalls);
        vm.CountAllRowsCommand.Cancel();
        gate.SetResult(true);
        await pending;
        Assert.Equal(0, client.Calls);
        Assert.Equal(1, reader.CountCalls);
    }

    [AvaloniaTheory]
    [InlineData("metadata")]
    [InlineData("virtual")]
    [InlineData("maps")]
    public async Task Attached_cancel_button_cancels_initial_read_and_discards_late_result(string kind)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var metadata = new Metadata { Load = async ct => { captured = ct; await gate.Task; } };
        var reader = new VirtualReader { Read = async ct => { captured = ct; await gate.Task; return await new FakeVirtualTableReader().GetVirtualTablesAsync(ct); } };
        var maps = new Maps { Solutions = async ct => { captured = ct; await gate.Task; return await new FakeDualWriteMapReader().GetSolutionsAsync(ct); } };
        using var metadataVm = new MetadataViewModel(metadata);
        using var virtualVm = new VirtualTablesViewModel(reader);
        using var mapsVm = new DualWriteMapViewModel(maps, metadata: metadata);
        UserControl view = kind switch
        {
            "metadata" => new MetadataView { DataContext = metadataVm },
            "virtual" => new VirtualTablesView { DataContext = virtualVm },
            _ => new DualWriteMapView { DataContext = mapsVm }
        };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = Assert.IsType<Button>(view.FindControl<Button>("ReadCancelButton"));
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.True(captured.CanBeCanceled);
            button.Command!.Execute(button.CommandParameter);
            Assert.True(captured.IsCancellationRequested);
        }
        finally
        {
            gate.TrySetResult();
            var task = kind switch
            {
                "metadata" => metadataVm.InitializeCommand.ExecutionTask,
                "virtual" => virtualVm.InitializeCommand.ExecutionTask,
                _ => mapsVm.InitializeCommand.ExecutionTask
            };
            if (task is not null) await task;
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        Assert.Empty(virtualVm.Tables);
        Assert.Empty(mapsVm.Maps);
        if (kind == "maps") Assert.Equal(0, maps.MapCalls);
        if (kind == "metadata") Assert.Equal(0, metadata.FieldCalls);
    }
    [Theory]
    [InlineData("metadata", false)]
    [InlineData("metadata", true)]
    [InlineData("query", false)]
    [InlineData("query", true)]
    [InlineData("post", false)]
    [InlineData("post", true)]
    public async Task Cancelled_field_fetch_does_not_read_cache_or_paint_late_error(string kind, bool fault)
    {
        var metadata = new Metadata();
        using var metadataVm = new MetadataViewModel(metadata);
        using var queryVm = new QueryBuilderViewModel(metadata, new Client());
        using var postVm = new PostBuilderViewModel(new Client(), metadata: metadata);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        metadata.Fields = null;
        metadata.LoadFields = (_, _) => gate.Task;
        CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command;
        if (kind == "metadata") command = metadataVm.LoadSelectedFieldsCommand;
        else if (kind == "query") command = queryVm.LoadSelectedFieldsCommand;
        else
        {
            postVm.UseFieldGrid = true;
            command = postVm.EnsureFieldsCommand;
        }
        var pending = kind == "post" ? command.ExecutionTask! : command.ExecuteAsync(null);
        Assert.NotNull(pending);
        var reads = metadata.FieldReads;
        var body = postVm.RequestBody;
        metadataVm.LoadError = queryVm.LoadError = postVm.LoadError = "prior error";
        command.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late fields error"));
        else gate.SetResult(true);
        await pending;
        Assert.Equal(reads, metadata.FieldReads);
        Assert.Equal("prior error", kind == "metadata" ? metadataVm.LoadError : kind == "query" ? queryVm.LoadError : postVm.LoadError);
        Assert.Equal(body, postVm.RequestBody);
        if (kind == "metadata") Assert.False(metadataVm.IsLoadingFields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Map_cancelled_catalogue_lookup_does_not_start_maps(bool fault)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new Metadata { Load = _ => gate.Task };
        var reader = new Maps();
        using var vm = new DualWriteMapViewModel(reader, metadata: metadata);
        var pending = vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(1, metadata.EntityCalls);
        var reads = metadata.EntityReads;
        vm.InitializeCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late catalogue error"));
        else gate.SetResult();
        await pending;
        Assert.Equal(0, reader.MapCalls);
        Assert.Equal(reads, metadata.EntityReads);
        Assert.DoesNotContain("late catalogue error", vm.LoadError ?? "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Map_cancelled_reload_preserves_prior_maps_and_error(bool fault)
    {
        var reader = new Maps();
        using var vm = new DualWriteMapViewModel(reader, metadata: new FakeMetadataService());
        await vm.InitializeCommand.ExecuteAsync(null);
        var maps = vm.Maps.ToArray();
        vm.LoadError = "prior error";
        var gate = new TaskCompletionSource<DwMapLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Read = _ => gate.Task;
        var pending = vm.ReloadMapsCommand.ExecuteAsync(null);
        vm.ReloadMapsCommand.Cancel();
        if (fault) gate.SetException(new InvalidOperationException("late map error"));
        else gate.SetResult(DwMapLoadResult.Ok(Array.Empty<DwMapRecord>()));
        await pending;
        Assert.Equal(maps, vm.Maps);
        Assert.Equal("prior error", vm.LoadError);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Map_old_count_cannot_clear_current_count_placeholder()
    {
        var reader = new Maps();
        using var vm = new DualWriteMapViewModel(reader, metadata: new FakeMetadataService());
        await vm.InitializeCommand.ExecuteAsync(null);
        var oldGate = new TaskCompletionSource<DwCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newGate = new TaskCompletionSource<DwCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Count = _ => reader.CountCalls == 1 ? oldGate.Task : newGate.Task;
        var old = vm.CountAllRowsCommand.ExecuteAsync(null);
        var current = vm.CountAllRowsCommand.ExecuteAsync(null);
        oldGate.SetResult(DwCountResult.Ok(9));
        await old;
        Assert.Equal("Counting…", vm.CountRows.First().CeStatus);
        Assert.Null(vm.CountRows.First().CeCount);
        vm.CountAllRowsCommand.Cancel();
        newGate.SetResult(DwCountResult.Ok(10));
        await current;
        Assert.DoesNotContain("Counting", vm.CountRows.First().CeStatus);
        Assert.Null(vm.CountRows.First().CeCount);
    }
    [Fact]
    public async Task Loader_cached_selection_supersedes_old_field_failure()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata = new Metadata { Fields = null, LoadFields = (_, _) => gate.Task };
        using var loader = new EntityCatalogLoader(metadata);
        var old = loader.EnsureFieldsAsync("A", TestContext.Current.CancellationToken);
        metadata.Fields = new[] { new EntityField("Id", "String", false) };
        Assert.False(await loader.EnsureFieldsAsync("B", TestContext.Current.CancellationToken));
        gate.SetException(new InvalidOperationException("old error"));
        Assert.False(await old);
        Assert.Null(loader.LastError);
    }

    [AvaloniaFact]
    public async Task Attached_map_cancel_button_cancels_count_without_fo_follow_on()
    {
        var reader = new Maps();
        var client = new Client();
        using var vm = new DualWriteMapViewModel(reader, odata: client, metadata: new FakeMetadataService());
        var view = new DualWriteMapView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var gate = new TaskCompletionSource<DwCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        reader.Count = ct => { captured = ct; return gate.Task; };
        var pending = vm.CountAllRowsCommand.ExecuteAsync(null);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var button = Assert.IsType<Button>(view.FindControl<Button>("ReadCancelButton"));
            Assert.True(button.IsEffectivelyVisible);
            Assert.True(button.IsEffectivelyEnabled);
            button.Command!.Execute(button.CommandParameter);
            Assert.True(captured.IsCancellationRequested);
        }
        finally
        {
            gate.TrySetResult(DwCountResult.Ok(99));
            await pending;
            window.Close();
        }
        Assert.Equal(0, client.Calls);
        Assert.Null(vm.CountRows.First().CeCount);
    }
}
