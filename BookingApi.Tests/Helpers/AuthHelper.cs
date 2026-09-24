using System.Security.Claims;

namespace BookingApi.Tests.Helpers;

public static class AuthHelper
{
    public static ClaimsPrincipal CreateUser(string role, int? userId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (userId.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }
}