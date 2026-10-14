namespace DumpAnalysisService.IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection
    : ICollectionFixture<DumpFixture>, ICollectionFixture<ServiceFixture>, ICollectionFixture<CrashDumpFixture>
{
    public const string Name = "Integration";
}
