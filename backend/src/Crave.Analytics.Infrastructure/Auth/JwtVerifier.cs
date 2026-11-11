using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Crave.Analytics.Infrastructure.Config;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crave.Analytics.Infrastructure.Auth;

public sealed class JwtVerifier
{
    private static readonly JsonWebTokenHandler Handler = new() { MapInboundClaims = false };
    private readonly TokenValidationParameters _parameters;

    public JwtVerifier()
    {
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Env.AccessSecret)),
            ClockSkew = TimeSpan.Zero,
        };
    }

    public async Task<JwtUser?> VerifyAsync(string token)
    {
        var result = await Handler.ValidateTokenAsync(token, _parameters);
        if (!result.IsValid)
            return null;

        return Parse(result.ClaimsIdentity);
    }

    private static JwtUser? Parse(ClaimsIdentity identity)
    {
        var claims = identity.Claims.ToLookup(claim => claim.Type, claim => claim.Value);

        int? GetInt(string name) => int.TryParse(claims[name].FirstOrDefault(), out var value) ? value : null;
        string GetText(string name) => claims[name].FirstOrDefault() ?? "";

        var userId = GetInt("userId");
        if (userId is null)
            return null;

        int[]? branchIds = null;
        var branchIdsJson = claims["branchIds"].FirstOrDefault();
        if (branchIdsJson is not null)
            branchIds = JsonSerializer.Deserialize<int[]>(branchIdsJson);

        return new JwtUser(
            userId.Value,
            GetText("role"),
            GetText("email"),
            GetInt("restaurantId"),
            claims["restaurantRole"].FirstOrDefault(),
            branchIds);
    }
}
