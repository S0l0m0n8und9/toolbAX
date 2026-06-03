using Xunit;

// The WPF binding TraceListener and the process pack:// registration are global state,
// so UI tests must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
