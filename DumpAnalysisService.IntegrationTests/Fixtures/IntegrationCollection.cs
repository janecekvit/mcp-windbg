namespace DumpAnalysisService.IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection
    : ICollectionFixture<DumpFixture>, ICollectionFixture<ServiceFixture>
{
    public const string Name = "Integration";
}
