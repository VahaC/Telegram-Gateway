namespace AgentGateway.Api.Tests;

/// <summary>Environment variables are process-wide, so these tests run alone, after the parallel ones.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class EnvironmentVariableCollection;
