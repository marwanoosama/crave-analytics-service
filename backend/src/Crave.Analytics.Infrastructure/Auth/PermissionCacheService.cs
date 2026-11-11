using Crave.Analytics.Infrastructure.Config;
using Crave.Analytics.Infrastructure.CoreClient;

namespace Crave.Analytics.Infrastructure.Auth;

/// <summary>
/// In-process role → permissions cache (core's permission-cache.service.ts
/// analogue): TTL `RBAC_CACHE_TTL_SEC`, failures are not cached.
/// </summary>
public sealed class PermissionCacheService(RbacClient rbacClient)
{
    private sealed record Entry(IReadOnlyList<string> Permissions, DateTimeOffset CachedAt);

    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> GetPermissions(string role, CancellationToken cancellationToken)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(role, out var cached)
                && DateTimeOffset.UtcNow - cached.CachedAt < TimeSpan.FromSeconds(Env.RbacCacheTtlSec))
                return cached.Permissions;
        }

        var permissions = await rbacClient.GetPermissionsByRole(role, cancellationToken);
        lock (_cache)
            _cache[role] = new Entry(permissions, DateTimeOffset.UtcNow);
        return permissions;
    }
}
