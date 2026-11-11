namespace Crave.Analytics.Infrastructure.Auth;

public sealed record JwtUser(
    int UserId,
    string Role,
    string Email,
    int? RestaurantId,
    string? RestaurantRole,
    int[]? BranchIds)
{
    public const string ItemsKey = "JwtUser";
}

public static class SystemRole
{
    public const string SystemAdmin = "system_admin";
    public const string RestaurantUser = "restaurant_user";
}
