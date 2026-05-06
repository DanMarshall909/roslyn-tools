

// Tests share a process-wide WorkspaceLoader.Override AsyncLocal; xunit's default
// cross-class parallelism causes races between tests that install/clear overrides.
// With the suite at ~5s, serialised execution is acceptable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
