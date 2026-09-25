using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace ToolBax.App.ViewModels;

// Limited to writes/readback: the shared VM lease is acquired before creating or replacing cancellation.
internal sealed class WriteOperationCommand(
    Func<object?, CancellationToken, Task> execute, Func<object?, bool> canExecute,
    Func<bool> enter, Action leave, Action<CancellationTokenSource?> owner) : IAsyncRelayCommand
{
    private CancellationTokenSource? _cancellation;
    private bool _wasCancelled;
    public Task? ExecutionTask { get; private set; }
    public bool IsRunning => _cancellation is not null;
    public bool CanBeCanceled => _cancellation is { IsCancellationRequested: false };
    public bool IsCancellationRequested => _cancellation?.IsCancellationRequested ?? _wasCancelled;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !IsRunning && canExecute(parameter);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public void Cancel() { _cancellation?.Cancel(); NotifyState(); }
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);
    public Task ExecuteAsync(object? parameter)
    {
        // Like Toolkit commands, direct execution reaches the VM's hard preflight guards even when
        // CanExecute is false. Admission still happens before any cancellation state is allocated.
        if (IsRunning || !enter()) return Task.CompletedTask;
        var cancellation = new CancellationTokenSource();
        _wasCancelled = false;
        _cancellation = cancellation;
        owner(cancellation);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionTask = completion.Task;
        NotifyState();
        _ = Run(parameter, cancellation, completion);
        return completion.Task;
    }
    private async Task Run(object? parameter, CancellationTokenSource cancellation, TaskCompletionSource completion)
    {
        Exception? error = null;
        try { await execute(parameter, cancellation.Token); }
        catch (Exception ex) { error = ex; }
        finally
        {
            owner(null);
            _wasCancelled = cancellation.IsCancellationRequested;
            _cancellation = null;
            cancellation.Dispose();
            leave();
            if (error is OperationCanceledException cancelled) completion.TrySetCanceled(cancelled.CancellationToken);
            else if (error is not null) completion.TrySetException(error);
            else completion.TrySetResult();
            NotifyState();
        }
    }
    private void NotifyState()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        NotifyCanExecuteChanged();
    }
}
