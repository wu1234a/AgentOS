namespace AgentOS.Api.Tests;

/// <summary>
/// SQL Server integration tests share one disposable database, so they must not run in
/// parallel with each other (migration upgrade tests rewind the schema).
/// </summary>
[CollectionDefinition("SqlServer", DisableParallelization = true)]
public sealed class SqlServerCollection;
