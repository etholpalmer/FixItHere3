module FixItHere.Backend.Tests.AssemblyInfo

// Each WebApplicationFactory boot deletes/recreates the shared SQLite file DB.
// Disable class-level parallelism so concurrent boots don't race on the file.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()
