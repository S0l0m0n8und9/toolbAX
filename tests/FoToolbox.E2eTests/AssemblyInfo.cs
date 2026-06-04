using Xunit;

// Only one app instance / desktop interaction at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
