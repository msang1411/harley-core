namespace Identity.Tests.Infrastructure;

/// <summary>
/// Puts every test class that carries [Collection(ApiCollection.Name)] into one collection,
/// so they share a single IdentityApiFactory (one API host, one PostgreSQL container).
/// Classes in the same collection run sequentially, which also keeps the process-wide
/// environment variables used by the factory safe.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<IdentityApiFactory>
{
    public const string Name = "Identity API";
}