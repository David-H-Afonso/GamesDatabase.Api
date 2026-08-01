// Each test class spins up its own WebApplicationFactory, and the factory
// publishes the per-test SQLite database path through a process-global
// environment variable that Program reads at startup. Running test classes in
// parallel therefore races on that shared variable and can make two apps share
// one database. Disabling assembly parallelization keeps every test isolated.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
