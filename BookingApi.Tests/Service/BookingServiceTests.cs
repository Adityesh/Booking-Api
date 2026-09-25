using BookingApi.Data;
using BookingApi.Dto.Booking;
using BookingApi.Service;
using BookingApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Tests.Service;

public class BookingServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_ResourceAtCapacity_ReturnsNoCapacity()
    {
        // seed a Resource with Capacity = 2
        // seed 2 existing, non-cancelled Bookings overlapping the same window
        // call CreateAsync for a 3rd overlapping request
        // assert result.Result == BookingCreationResult.NoCapacity
        // assert result.Booking is null
        var context = CreateContext();
        var startTime = DateTime.UtcNow;
        var endTime = DateTime.UtcNow.AddHours(1);
        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername"
        };

        var resource = new ResourceEntity()
        {
            Capacity = 2,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };

        BookingEntity[] bookings =
        [
            new()
            {
                StartTime = startTime,
                EndTime = endTime,
                Resource = resource,
                Status = BookingStatus.Confirmed,
                User = user,
            },
            new()
            {
                StartTime = startTime,
                EndTime = endTime,
                Resource = resource,
                Status = BookingStatus.Confirmed,
                User = user,
            }
        ];
        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.AddRange(bookings);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var createDto = new CreateBookingDto(startTime, endTime, resource.Id);
        var result = await bookingService.CreateAsync(createDto, 1, CancellationToken.None);

        Assert.Equal(BookingCreationResult.NoCapacity, result.result);
        Assert.Null(result.booking);
    }

    [Fact]
    public async Task CreateAsync_NonOverlappingTimes_Succeeds()
    {
        // seed a Resource with Capacity = 1
        // seed 1 existing Booking for, say, 9am-10am
        // call CreateAsync for a DIFFERENT window, e.g. 2pm-3pm, same resource
        // assert result.Result == BookingCreationResult.Success
        // this proves it's the OVERLAP that's checked, not just a raw booking count
        var context = CreateContext();
        var startTime = DateTime.UtcNow;
        var endTime = DateTime.UtcNow.AddHours(1);

        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername"
        };

        var resource = new ResourceEntity()
        {
            Capacity = 1,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };


        var booking = new BookingEntity()
        {
            StartTime = startTime,
            EndTime = endTime,
            Resource = resource,
            Status = BookingStatus.Confirmed,
            User = user
        };

        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var createDto = new CreateBookingDto(startTime.AddHours(2), endTime.AddHours(2), resource.Id);
        var result = await bookingService.CreateAsync(createDto, booking.UserId, CancellationToken.None);

        Assert.Equal(BookingCreationResult.Success, result.result);
        Assert.NotNull(result.booking);
    }

    [Fact]
    public async Task CancelAsync_WithinTwoHours_NonAdmin_ReturnsTooCloseToStartTime()
    {
        // seed a Booking with StartTime ~1 hour from now
        // call CancelAsync with isAdmin: false
        // assert BookingCancellationResult.TooCloseToStartTime
        var context = CreateContext();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername"
        };

        var resource = new ResourceEntity()
        {
            Capacity = 1,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };


        var booking = new BookingEntity()
        {
            StartTime = startTime,
            EndTime = endTime,
            Resource = resource,
            Status = BookingStatus.Confirmed,
            User = user,
        };

        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var result = await bookingService.CancelAsync(booking.Id, booking.UserId, false, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.TooCloseToStartTime, result);
    }

    [Fact]
    public async Task CancelAsync_WithinTwoHours_Admin_ReturnsSuccess()
    {
        // same seed, isAdmin: true this time
        // assert BookingCancellationResult.Success
        // bonus: reload the booking from context and assert Status == Cancelled
        var context = CreateContext();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var user = new UserEntity()
        {
            IsActive = true,
            Role = UserRole.Admin,
            Username = "Testusername"
        };

        var resource = new ResourceEntity()
        {
            Capacity = 2,
            IsActive = true,
            Name = "Test Resource",
            Type = ResourceType.Equipment
        };


        var booking = new BookingEntity()
        {
            StartTime = startTime,
            EndTime = endTime,
            Resource = resource,
            Status = BookingStatus.Confirmed,
            User = user
        };

        context.Users.Add(user);
        context.Resources.Add(resource);
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var bookingService = new BookingService(context);
        var result = await bookingService.CancelAsync(booking.Id, booking.UserId, true, CancellationToken.None);

        Assert.Equal(BookingCancellationResult.Success, result);
    }
}