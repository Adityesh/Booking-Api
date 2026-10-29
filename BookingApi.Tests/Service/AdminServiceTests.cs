using BookingApi.Data;
using BookingApi.Dto.Admin;
using BookingApi.Dto.Validators;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Tests.Service;

// "No admin exists" is a GLOBAL condition, so these tests can't share the main test database (other suites
// create admins there). Every test gets its own brand-new, empty database inside the shared container and
// drops it afterwards.
[Collection("Postgres")]
public class AdminServiceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string ValidUsername = "alice_01";
    private const string ValidPassword = "s3cret_pw";
    private const string ValidEmail = "alice@example.com";

    private string _databaseName = "";
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        _databaseName = $"admin_{Guid.NewGuid():N}";

        await using (var server = new NpgsqlConnection(fixture.ConnectionString))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", server);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = _databaseName
        }.ConnectionString;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        // Pooled connections would keep the database "in use" and block the drop.
        NpgsqlConnection.ClearAllPools();

        await using var server = new NpgsqlConnection(fixture.ConnectionString);
        await server.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", server);
        await drop.ExecuteNonQueryAsync();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    private static AdminService CreateService(AppDbContext context) => new(context, new RegisterDtoValidator());

    private async Task<AdminCreationResult> CreateAdminAsync(string username, string password)
    {
        await using var context = CreateContext();
        return await CreateService(context).CreateAdmin(userName: username, password: password, email: ValidEmail);
    }

    private async Task SeedUserAsync(string username, UserRole role)
    {
        await using var context = CreateContext();
        context.Users.Add(new UserEntity
        {
            Username = username,
            PasswordHash = "not-a-real-hash",
            Role = role,
            IsActive = true
        });
        await context.SaveChangesAsync();
    }

    // ---------- happy path ----------

    [Fact]
    public async Task EmptyDatabase_ValidCredentials_CreatesAnActiveAdmin_AndStoresAHashNotThePassword()
    {
        var result = await CreateAdminAsync(ValidUsername, ValidPassword);

        Assert.Equal(AdminCreationResult.Created, result);

        await using var context = CreateContext();
        var admin = await context.Users.SingleAsync();
        Assert.Equal(ValidUsername, admin.Username);
        Assert.Equal(UserRole.Admin, admin.Role);
        // If this fails, the service forgot to set IsActive and the admin could not log in.
        Assert.True(admin.IsActive);
        Assert.NotEqual(ValidPassword, admin.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(ValidPassword, admin.PasswordHash));
    }

    [Fact]
    public async Task CreatedAdmin_CanBeFoundByTheSameRoleQueryTheServiceUses_SoASecondRunIsIdempotent()
    {
        await CreateAdminAsync(ValidUsername, ValidPassword);

        var second = await CreateAdminAsync("someone_else", ValidPassword);

        Assert.Equal(AdminCreationResult.AdminAlreadyExists, second);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Users.CountAsync());
    }

    // ---------- an admin already exists ----------

    [Fact]
    public async Task AdminAlreadyExists_DifferentUsername_ReturnsAdminAlreadyExists_AndCreatesNothing()
    {
        await SeedUserAsync("existing_admin", UserRole.Admin);

        var result = await CreateAdminAsync(ValidUsername, ValidPassword);

        Assert.Equal(AdminCreationResult.AdminAlreadyExists, result);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.False(await context.Users.AnyAsync(u => u.Username == ValidUsername));
    }

    [Fact]
    public async Task AdminAlreadyExists_SameUsername_StillReturnsAdminAlreadyExists_NotUsernameTaken()
    {
        // The admin check comes first: "an admin exists" is the more useful answer than "that name is taken".
        await SeedUserAsync(ValidUsername, UserRole.Admin);

        var result = await CreateAdminAsync(ValidUsername, ValidPassword);

        Assert.Equal(AdminCreationResult.AdminAlreadyExists, result);
    }

    // ---------- no admin, but the username is in use ----------

    [Fact]
    public async Task NoAdmin_UsernameBelongsToANormalUser_ReturnsUsernameTaken_AndDoesNotPromoteThem()
    {
        await SeedUserAsync(ValidUsername, UserRole.User);

        var result = await CreateAdminAsync(ValidUsername, ValidPassword);

        Assert.Equal(AdminCreationResult.UsernameTaken, result);
        await using var context = CreateContext();
        var user = await context.Users.SingleAsync();
        Assert.Equal(UserRole.User, user.Role);
        Assert.False(await context.Users.AnyAsync(u => u.Role == UserRole.Admin));
    }

    [Fact]
    public async Task NoAdmin_UsernameDifferingOnlyByCase_IsNotTaken_BecauseUsernamesAreCaseSensitive()
    {
        await SeedUserAsync("Alice_01", UserRole.User);

        var result = await CreateAdminAsync("alice_01", ValidPassword);

        Assert.Equal(AdminCreationResult.Created, result);
    }

    // ---------- invalid input ----------

    [Theory]
    [InlineData("", ValidPassword)]
    [InlineData("   ", ValidPassword)]
    [InlineData(ValidUsername, "")]
    [InlineData(ValidUsername, "   ")]
    [InlineData(ValidUsername, "nosymbols123")] // password must contain one of - _ .
    public async Task InvalidCredentials_ReturnInvalidInput_AndCreateNothing(string username, string password)
    {
        var result = await CreateAdminAsync(username, password);

        Assert.Equal(AdminCreationResult.InvalidInput, result);
        await using var context = CreateContext();
        Assert.Equal(0, await context.Users.CountAsync());
    }

    [Fact]
    public async Task PasswordOver72Bytes_ReturnsInvalidInput_BecauseBCryptWouldSilentlyTruncateIt()
    {
        var tooLong = new string('a', 70) + "_xyz"; // 74 bytes

        var result = await CreateAdminAsync(ValidUsername, tooLong);

        Assert.Equal(AdminCreationResult.InvalidInput, result);
    }

    [Fact]
    public async Task InvalidInput_IsReportedBeforeTheDatabaseIsConsulted_SoItWinsOverAdminAlreadyExists()
    {
        await SeedUserAsync("existing_admin", UserRole.Admin);

        var result = await CreateAdminAsync("", "");

        Assert.Equal(AdminCreationResult.InvalidInput, result);
    }

    // ---------- race ----------

    [Fact]
    public async Task TwoSimultaneousRuns_WithTheSameUsername_ProduceExactlyOneAdmin_AndNeverThrow()
    {
        // Both runs can pass the "no admin / username free" checks before either inserts. The unique index then
        // rejects the loser, and the service must turn that into a result instead of an unhandled exception.
        var results = await Task.WhenAll(
            CreateAdminAsync(ValidUsername, ValidPassword),
            CreateAdminAsync(ValidUsername, ValidPassword));

        Assert.Equal(1, results.Count(r => r == AdminCreationResult.Created));
        Assert.All(results.Where(r => r != AdminCreationResult.Created),
            r => Assert.True(r is AdminCreationResult.UsernameTaken or AdminCreationResult.AdminAlreadyExists));

        await using var context = CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(u => u.Role == UserRole.Admin));
    }

    // ---------- cancellation ----------

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled_AndCreatesNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using (var context = CreateContext())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateService(context).CreateAdmin(userName: ValidUsername, password: ValidPassword, email: ValidEmail, token: cts.Token));
        }

        await using var check = CreateContext();
        Assert.Equal(0, await check.Users.CountAsync());
    }
}