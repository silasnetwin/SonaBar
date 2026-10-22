using Xunit;

// Several integration tests exercise the single desktop clipboard. Serial execution keeps
// those tests deterministic when another test temporarily owns the STA clipboard thread.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
