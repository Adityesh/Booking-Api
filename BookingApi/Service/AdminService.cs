using BookingApi.Data;
using BookingApi.Dto.Admin;
using BookingApi.Dto.Auth;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApi.Service;

public class AdminService(AppDbContext context, IValidator<RegisterDto> validator) : IAdminService
{
    public async Task<AdminCreationResult> CreateAdmin(string userName, string email, string password, CancellationToken token = default)
    {
        // Same rules as public registration (format, password rules, BCrypt 72-byte limit).
        var validation = await validator.ValidateAsync(new RegisterDto( userName, email, password), token);
        if (!validation.IsValid) return AdminCreationResult.InvalidInput;

        if (await context.Users.AnyAsync(x => x.Role == UserRole.Admin, token))
            return AdminCreationResult.AdminAlreadyExists;

        // Exact match on purpose: usernames are case-sensitive, same as the unique index.
        if (await context.Users.AnyAsync(x => x.Username == userName, token))
            return AdminCreationResult.UsernameTaken;

        context.Users.Add(new UserEntity
        {
            Username = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.Admin,
            Email = email,
        });

        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race between the check above and the insert; the index is the real guarantee.
            return AdminCreationResult.UsernameTaken;
        }

        return AdminCreationResult.Created;
    }
}