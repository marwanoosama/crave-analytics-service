using Crave.Analytics.Api.Modules.Analytics.Dtos;
using Crave.Analytics.Api.Modules.Analytics.Services;
using Crave.Analytics.Infrastructure.Auth;
using Crave.Analytics.Infrastructure.Http;

namespace Crave.Analytics.Api.Modules.Analytics.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/analytics/restaurants/{restaurantId}/days",
                async ([AsParameters] RestaurantDaysRequestDto request, AnalyticsService service,
                    CancellationToken cancellationToken) =>
                    ApiResponse.SendSuccess(await service.GetDaysAsync(request, cancellationToken)))
            .AddEndpointFilter<AuthenticateFilter>()
            .AddEndpointFilter<RequireRestaurantMemberFilter>()
            .AddEndpointFilter<RbacFilter>();
}
