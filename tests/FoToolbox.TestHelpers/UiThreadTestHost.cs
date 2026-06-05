using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.TestHelpers;

/// <summary>
/// Runs an async test body on a dedicated STA thread whose awaited continuations are pumped back onto
/// that same thread, so a plugin view model under test sees a single, stable "UI thread" (the WPF
/// dispatcher model). Use this for view-model flows that touch dispatcher-affine state — bound
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>s, <c>ICollectionView</c>, etc.
/// — without a visible desktop session.
/// <para>
/// Why it matters: a view model that does <c>await something</c> and then mutates a UI-bound
/// collection must resume on the UI thread. Under a real WPF dispatcher a missing/incorrect
/// <c>ConfigureAwait</c> resumes on a thread-pool thread and throws; this host reproduces that thread
/// affinity in a headless test so such bugs surface deterministically.
/// </para>
/// </summary>
public static class UiThreadTestHost
{
    /// <summary>Executes <paramref name="body"/> to completion on a pumped STA thread, rethrowing any failure.</summary>
    public static void Run(Func<Task> body)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previousContext = SynchronizationContext.Current;
            var syncContext = new PumpSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                var task = body();
                task.ContinueWith(_ => syncContext.Complete(), TaskScheduler.Default);
                syncContext.RunOnCurrentThread();
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        // Synchronous Send is already on the pump thread (callers run there), so execute inline rather
        // than throwing (the base default for a context with no target thread).
        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void RunOnCurrentThread()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work.Callback(work.State);
            }
        }

        public void Complete() => _queue.CompleteAdding();
    }
}
