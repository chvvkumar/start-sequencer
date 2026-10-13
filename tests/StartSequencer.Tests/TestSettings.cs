using Xunit;

// Several test classes point ConfigStore at a temp root; that root is process-wide state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
