using Xunit;

// Process-wide GC.GetTotalMemory assertions in Challenger1StressTests cannot run concurrently
// with other test classes that allocate multi-megabyte framebuffers/GlyphAtlases.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
