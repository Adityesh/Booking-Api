namespace BookingApi.Tests.Fixtures;

// One Postgres container shared by every test class marked [Collection("Postgres")].
// Classes in the same collection run one after another, so they can't step on each other's data,
// and the suite only pays the container start-up cost once.
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}