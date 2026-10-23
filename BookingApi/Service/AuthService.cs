using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingApi.Data;
using BookingApi.Dto;
using BookingApi.Dto.Auth;
using BookingApi.Dto.RefreshToken;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace BookingApi.Service;

public class AuthService(AppDbContext context, IConfiguration config, IRefreshTokenService refreshTokenService) : IAuthService
{
    public async Task<AuthSession?> LoginAsync(LoginDto dto, CancellationToken token)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Username == dto.Username, token);
        if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return null;
        var (userRole, expiresAt, jwtToken) = GenerateToken(user.Id, user.Username, user.Role);
        var (refreshExpiresAt, refreshToken) = await refreshTokenService.IssueAsync(user.Id, token);
        return new AuthSession(new AuthResponseDto(jwtToken, user.Username, userRole, expiresAt), refreshToken, refreshExpiresAt);
    }

    public async Task<AuthSession?> RegisterAsync(RegisterDto dto, CancellationToken token)
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

        var (userRole, expiresAt, jwtToken) = GenerateToken(newUser.Id, newUser.Username, newUser.Role);
        var (refreshExpiresAt, refreshToken) = await refreshTokenService.IssueAsync(newUser.Id, token);
        return new AuthSession(new AuthResponseDto(jwtToken, newUser.Username, userRole, expiresAt), refreshToken, refreshExpiresAt);
    }

    public async Task<AuthSession?> RefreshAsync(string rawToken, CancellationToken token)
    {
        var (refreshTokenResult, newToken, refreshExpiresAt, tokenUser) = await refreshTokenService.RotateAsync(rawToken, token);

        if (refreshTokenResult != RotateRefreshTokenResult.Success || tokenUser == null || newToken == null ||
            refreshExpiresAt == null) return null;

        var (userRole, accessTokenExpiresAt, jwtToken) = GenerateToken(tokenUser.UserId, tokenUser.Username, tokenUser.Role);
        return new AuthSession(
            new AuthResponseDto(jwtToken, tokenUser.Username, userRole, accessTokenExpiresAt), newToken,
            refreshExpiresAt.Value);

    }

    private (UserRole, DateTime, string) GenerateToken(int id, string username, UserRole role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role.ToString())
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

        return (role, expiresAt, new JwtSecurityTokenHandler().WriteToken(token));
    }
}