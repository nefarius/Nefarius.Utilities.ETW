// Run test fixtures in parallel with each other (tests within a fixture still run sequentially
// unless the fixture itself opts in to finer parallelism).
[assembly: Parallelizable(ParallelScope.Fixtures)]
