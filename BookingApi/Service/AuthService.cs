using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingApi.Data;
using BookingApi.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BookingApi.Service;

public class AuthService(AppDbContext context, IConfiguration config) : IAuthService
{
    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);
        if (user == null || !user.IsActive || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return null;

        return new AuthResponseDto(GenerateToken(user), user.Username);
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterDto dto)
    {
        var userExists = await context.Users.AnyAsync(u => u.Username == dto.Username);
        if (userExists) return null;

        var newUser = new UserEntity()
        {
            Username = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = UserRole.User
        };

        context.Users.Add(newUser);
        await context.SaveChangesAsync();

        return new AuthResponseDto(GenerateToken(newUser), newUser.Username);
    }

    private string GenerateToken(UserEntity userEntity)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userEntity.Id.ToString()),
            new Claim(ClaimTypes.Name, userEntity.Username),
            new Claim(ClaimTypes.Role, userEntity.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtSettings:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["JwtSettings:Issuer"],
            audience: config["JwtSettings:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(double.Parse(config["JwtSettings:ExpiryMinutes"]!)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}