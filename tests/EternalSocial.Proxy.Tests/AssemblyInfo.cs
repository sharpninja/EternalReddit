// The endpoint tests spin up full WebApplicationFactory hosts; running the small
// suite serially avoids cross-host races for a negligible time cost.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
