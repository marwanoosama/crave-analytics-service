using Crave.Analytics.Infrastructure.Http;
using Microsoft.AspNetCore.Http;

namespace Crave.Analytics.Infrastructure.Auth;

public sealed class RequireRestaurantMemberFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = (JwtUser)context.HttpContext.Items[JwtUser.ItemsKey]!;
        if (user.Role == SystemRole.SystemAdmin)
            return await next(context);

        var routeValue = context.HttpContext.Request.RouteValues["restaurantId"]?.ToString();
        if (!int.TryParse(routeValue, out var restaurantId) || user.RestaurantId != restaurantId)
            throw new AppException("Permission denied", StatusCodes.Status403Forbidden);

        return await next(context);
    }
}
