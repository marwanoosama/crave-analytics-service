using Crave.Analytics.Infrastructure.CoreClient;
using Crave.Analytics.Infrastructure.Http;
using Microsoft.AspNetCore.Http;

namespace Crave.Analytics.Infrastructure.Auth;

/// <summary>
/// Mirrors core's rbac(): system_admin bypasses; restaurant users need the
/// `resource:action` permission via their restaurantRole; every other role is
/// denied. Core being down without a cached grant ⇒ 503.
/// </summary>
public sealed class RbacFilter(PermissionCacheService permissions) : IEndpointFilter
{
    public const string Resource = "analytics";
    public const string Action = "read";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var user = (JwtUser)context.HttpContext.Items[JwtUser.ItemsKey]!;

        if (user.Role == SystemRole.SystemAdmin)
            return await next(context);

        if (user.Role != SystemRole.RestaurantUser || user.RestaurantRole is null)
            throw new AppException("Permission denied", StatusCodes.Status403Forbidden);

        IReadOnlyList<string> granted;
        try
        {
            granted = await permissions.GetPermissions(user.RestaurantRole,
                context.HttpContext.RequestAborted);
        }
        catch (AppException error) when (error.StatusCode == StatusCodes.Status503ServiceUnavailable)
        {
            throw new AppException("Core service unavailable", StatusCodes.Status503ServiceUnavailable);
        }

        if (!granted.Contains($"{Resource}:{Action}"))
            throw new AppException("Permission denied", StatusCodes.Status403Forbidden);

        return await next(context);
    }
}
