using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace BookingApi.Service;

public class AuthService(AppDbContext context, IConfiguration config) : IAuthService
{
    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto, CancellationToken token)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Username == dto.Username, token);
        if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return null;
        var (userRole, expiresAt, jwtToken) = GenerateToken(user);
        return new AuthResponseDto(jwtToken, user.Username, userRole, expiresAt);
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterDto dto, CancellationToken token)
    {
        var userExists = await context.Users.AnyAsync(u => u.Username == dto.Username, token);
        if (userExists) return null;

        var newUser = new UserEntity()
        {
            Username = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = UserRole.User
        };

        context.Users.Add(newUser);
        try
        {
            await context.SaveChangesAsync(token);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }

        var (userRole, expiresAt, jwtToken) = GenerateToken(newUser);
        return new AuthResponseDto(jwtToken, newUser.Username, userRole, expiresAt);
    }

    private (UserRole, DateTime, string) GenerateToken(UserEntity userEntity)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userEntity.Id.ToString()),
            new Claim(ClaimTypes.Name, userEntity.Username),
            new Claim(ClaimTypes.Role, userEntity.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtSettings:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddMinutes(double.Parse(config["JwtSettings:ExpiryMinutes"]!));
        var token = new JwtSecurityToken(
            issuer: config["JwtSettings:Issuer"],
            audience: config["JwtSettings:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds
        );

        return (userEntity.Role, expiresAt, new JwtSecurityTokenHandler().WriteToken(token));
    }
}