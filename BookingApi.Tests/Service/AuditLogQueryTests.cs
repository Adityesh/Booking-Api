using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.AuditLog;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

[Collection("Postgres")]
public class AuditLogQueryTests(PostgresFixture fixture)
{
    // audit_log_entries is shared by every test class in the collection, and every other test writes entries
    // stamped with the real present. So each test here owns a one-day window in its own year, centuries in the
    // past, and seeds entries only inside (or deliberately just outside) that window. No other test's data can
    // fall in it, and none of these rows can fall in anyone else's window.
    private static int _windowCounter;

    // xUnit creates a new instance of this class per test, so every test gets its own window.
    private readonly DateTime _start =
        new(1500 + Interlocked.Increment(ref _windowCounter), 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private DateTime At(int hours) => _start.AddHours(hours);
    private DateTimeOffset WindowFrom => new(_start);
    private DateTimeOffset WindowTo => new(_start.AddDays(1));

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    // ---------- seeding ----------

    private async Task<int> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = new UserEntity
        {
            Username = $"u_{Guid.NewGuid():N}",
            PasswordHash = "not-a-real-hash",
            Role = UserRole.User,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedEntryAsync(DateTime timestamp, ActionType action, int? userId, int entityId = 1)
    {
        await using var context = CreateContext();
        var entry = new AuditLogEntryEntity
        {
            Timestamp = timestamp,
            Action = action,
            EntityId = entityId,
            UserId = userId
        };
        context.AuditLogEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    // ---------- the call under test ----------

    private async Task<PagedResult<AuditLogResponseDto>> QueryAsync(
        bool systemOnly = false, int? userId = null, ActionType? action = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 20)
    {
        await using var context = CreateContext();
        var dto = new GetAuditLogDto(userId, action, from, to, systemOnly, page, pageSize);
        return await new AuditLogService(context).GetAuditLogsAsync(dto, CancellationToken.None);
    }

    // ======================= window and ordering =======================

    [Fact]
    public async Task Query_WindowOnly_ReturnsTheEntriesInsideIt_NewestFirst()
    {
        var middle = await SeedEntryAsync(At(2), ActionType.BookingCreated, null);
        var oldest = await SeedEntryAsync(At(1), ActionType.BookingCreated, null);
        var newest = await SeedEntryAsync(At(3), ActionType.BookingCreated, null);
        await SeedEntryAsync(_start.AddHours(-1), ActionType.BookingCreated, null);        // just before the window
        await SeedEntryAsync(_start.AddDays(1).AddHours(1), ActionType.BookingCreated, null);   // just after it

        var result = await QueryAsync(from: WindowFrom, to: WindowTo);

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { newest, middle, oldest }, result.Data.Select(e => e.Id).ToArray());
    }

    [Fact]
    public async Task Query_DateBounds_AreInclusiveAtFrom_AndExclusiveAtTo()
    {
        var justBeforeFrom = await SeedEntryAsync(_start.AddSeconds(-1), ActionType.BookingCreated, null);
        var exactlyAtFrom = await SeedEntryAsync(_start, ActionType.BookingCreated, null);
        var justBeforeTo = await SeedEntryAsync(_start.AddDays(1).AddSeconds(-1), ActionType.BookingCreated, null);
        var exactlyAtTo = await SeedEntryAsync(_start.AddDays(1), ActionType.BookingCreated, null);

        var result = await QueryAsync(from: WindowFrom, to: WindowTo);

        var ids = result.Data.Select(e => e.Id).ToList();
        Assert.Contains(exactlyAtFrom, ids);
        Assert.Contains(justBeforeTo, ids);
        Assert.DoesNotContain(justBeforeFrom, ids);
        Assert.DoesNotContain(exactlyAtTo, ids);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Query_BoundsGivenInAnotherTimeZone_AreComparedAsTheSameInstant()
    {
        var entryId = await SeedEntryAsync(At(1), ActionType.BookingCreated, null);
        var sameInstantElsewhere = new DateTimeOffset(At(1)).ToOffset(TimeSpan.FromHours(5.5));

        var included = await QueryAsync(from: sameInstantElsewhere, to: WindowTo);
        var excluded = await QueryAsync(from: sameInstantElsewhere.AddSeconds(1), to: WindowTo);

        Assert.Contains(included.Data, e => e.Id == entryId);
        Assert.DoesNotContain(excluded.Data, e => e.Id == entryId);
    }

    [Fact]
    public async Task Query_NothingMatches_ReturnsAnEmptyPage()
    {
        var result = await QueryAsync(from: WindowFrom, to: WindowTo);

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Count);
    }

    // ======================= individual filters =======================

    [Fact]
    public async Task Query_ActionFilter_ReturnsOnlyThatAction_AndCountsOnlyThose()
    {
        await SeedEntryAsync(At(1), ActionType.BookingCreated, null);
        await SeedEntryAsync(At(2), ActionType.BookingCreated, null);
        await SeedEntryAsync(At(3), ActionType.BookingCancelled, null);
        await SeedEntryAsync(At(4), ActionType.ResourceCreated, null);

        var result = await QueryAsync(action: ActionType.BookingCreated, from: WindowFrom, to: WindowTo);

        Assert.Equal(2, result.Count);
        Assert.All(result.Data, e => Assert.Equal(ActionType.BookingCreated, e.Action));
    }

    [Fact]
    public async Task Query_UserFilter_ReturnsOnlyThatUsersEntries_WithoutNeedingADateRange()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        await SeedEntryAsync(At(1), ActionType.BookingCreated, alice);
        await SeedEntryAsync(At(2), ActionType.BookingCancelled, alice);
        await SeedEntryAsync(At(3), ActionType.BookingCreated, bob);
        await SeedEntryAsync(At(4), ActionType.WaitlistEntryExpired, null);

        // No from/to on purpose: the user id alone isolates this test's rows, and proves the dates really are optional.
        var result = await QueryAsync(userId: alice);

        Assert.Equal(2, result.Count);
        Assert.All(result.Data, e => Assert.Equal(alice, e.UserId));
    }

    [Fact]
    public async Task Query_SystemOnly_ReturnsOnlyEntriesWithNoUser_AndPagesAndCountsThemCorrectly()
    {
        // System and user entries interleave in time. If the filter were applied after the page was cut,
        // page 1 would hold only the system entries among the three newest rows, and the count would include user rows.
        var userId = await SeedUserAsync();
        var systemIds = new List<int>();
        for (var hour = 1; hour <= 9; hour += 2)
            systemIds.Add(await SeedEntryAsync(At(hour), ActionType.WaitlistEntryExpired, null));
        for (var hour = 2; hour <= 10; hour += 2)
            await SeedEntryAsync(At(hour), ActionType.BookingCreated, userId);

        var first = await QueryAsync(systemOnly: true, from: WindowFrom, to: WindowTo, page: 1, pageSize: 3);
        var second = await QueryAsync(systemOnly: true, from: WindowFrom, to: WindowTo, page: 2, pageSize: 3);

        Assert.Equal(5, first.Count);
        Assert.Equal(5, second.Count);
        Assert.Equal(new[] { systemIds[4], systemIds[3], systemIds[2] }, first.Data.Select(e => e.Id).ToArray());
        Assert.Equal(new[] { systemIds[1], systemIds[0] }, second.Data.Select(e => e.Id).ToArray());
        Assert.All(first.Data.Concat(second.Data), e => Assert.Null(e.UserId));
    }

    [Fact]
    public async Task Query_SystemOnlyFalse_DoesNotRestrictTheResults()
    {
        // false means "no filter", not "only entries that have a user".
        var userId = await SeedUserAsync();
        await SeedEntryAsync(At(1), ActionType.WaitlistEntryExpired, null);
        await SeedEntryAsync(At(2), ActionType.BookingCreated, userId);

        var result = await QueryAsync(systemOnly: false, from: WindowFrom, to: WindowTo);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Query_CombinedFilters_AllApplyAtOnce()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var match = await SeedEntryAsync(At(1), ActionType.BookingCreated, alice);
        await SeedEntryAsync(At(2), ActionType.BookingCancelled, alice);   // wrong action
        await SeedEntryAsync(At(3), ActionType.BookingCreated, alice);     // outside the narrowed range
        await SeedEntryAsync(At(1), ActionType.BookingCreated, bob);       // wrong user

        var result = await QueryAsync(userId: alice, action: ActionType.BookingCreated,
            from: WindowFrom, to: new DateTimeOffset(At(2)));

        var only = Assert.Single(result.Data);
        Assert.Equal(match, only.Id);
        Assert.Equal(1, result.Count);
    }

    // ======================= paging =======================

    [Fact]
    public async Task Query_Pagination_SplitsTheResultsWithoutGapsOrOverlap()
    {
        var seeded = new List<(DateTime Timestamp, int Id)>();
        for (var hour = 1; hour <= 7; hour++)
            seeded.Add((At(hour), await SeedEntryAsync(At(hour), ActionType.BookingCreated, null)));
        var expected = seeded.OrderByDescending(s => s.Timestamp).Select(s => s.Id).ToList();

        var first = await QueryAsync(from: WindowFrom, to: WindowTo, page: 1, pageSize: 3);
        var second = await QueryAsync(from: WindowFrom, to: WindowTo, page: 2, pageSize: 3);
        var third = await QueryAsync(from: WindowFrom, to: WindowTo, page: 3, pageSize: 3);

        Assert.Equal(new[] { 3, 3, 1 }, new[] { first.Data.Count, second.Data.Count, third.Data.Count });
        Assert.All(new[] { first, second, third }, p => Assert.Equal(7, p.Count));   // the total never shrinks as you page
        Assert.Equal(new[] { 1, 2, 3 }, new[] { first.Page, second.Page, third.Page });
        Assert.All(new[] { first, second, third }, p => Assert.Equal(3, p.PageSize));
        Assert.Equal(expected, first.Data.Concat(second.Data).Concat(third.Data).Select(e => e.Id).ToList());
    }

    [Fact]
    public async Task Query_PageBeyondTheEnd_ReturnsNoData_ButTheRealTotal()
    {
        await SeedEntryAsync(At(1), ActionType.BookingCreated, null);
        await SeedEntryAsync(At(2), ActionType.BookingCreated, null);

        var result = await QueryAsync(from: WindowFrom, to: WindowTo, page: 5, pageSize: 10);

        Assert.Empty(result.Data);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Query_EntriesSharingATimestamp_PageStably_HighestIdFirst()
    {
        // Same timestamp for all five: without a tie-break the database may order them differently per page,
        // so an entry could appear twice or be skipped as you page through.
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
            ids.Add(await SeedEntryAsync(At(1), ActionType.BookingCreated, null));

        var paged = new List<AuditLogResponseDto>();
        for (var page = 1; page <= 3; page++)
            paged.AddRange((await QueryAsync(from: WindowFrom, to: WindowTo, page: page, pageSize: 2)).Data);

        Assert.Equal(ids.OrderByDescending(id => id).ToList(), paged.Select(e => e.Id).ToList());
    }

    // ======================= projection =======================

    [Fact]
    public async Task Query_MapsEveryFieldOfTheEntry_IncludingANullUser()
    {
        var entryId = await SeedEntryAsync(At(1), ActionType.WaitlistEntryExpired, null, entityId: 42);

        var result = await QueryAsync(from: WindowFrom, to: WindowTo);

        var item = Assert.Single(result.Data);
        Assert.Equal(entryId, item.Id);
        Assert.Equal(42, item.EntityId);
        Assert.Null(item.UserId);
        Assert.Equal(At(1), item.Timestamp);
        Assert.Equal(ActionType.WaitlistEntryExpired, item.Action);
    }
}