using Crave.Analytics.Infrastructure.Http;
using Microsoft.AspNetCore.Http;

namespace Crave.Analytics.Infrastructure.Auth;

public sealed class AuthenticateFilter(JwtVerifier verifier) : IEndpointFilter
{
    public const string CookieName = "access_token";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) 
    {
        var http = context.HttpContext;
        var token = http.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(token))
        {
            var header = http.Request.Headers.Authorization.FirstOrDefault();
            if (header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = header["Bearer ".Length..].Trim();
        }

        var user = string.IsNullOrEmpty(token) ? null : await verifier.VerifyAsync(token);
        if (user is null)
            throw new AppException("User not authenticated", StatusCodes.Status401Unauthorized);

        http.Items[JwtUser.ItemsKey] = user;
        return await next(context);
    }
}
