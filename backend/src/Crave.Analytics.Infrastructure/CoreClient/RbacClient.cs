using System.Text.Json;

namespace Crave.Analytics.Infrastructure.CoreClient;

public sealed class RbacClient(CoreClient coreClient)
{
    public async Task<IReadOnlyList<string>> GetPermissionsByRole(string role,
        CancellationToken cancellationToken = default)
    {
        var envelope = await coreClient.GetAsync<CoreEnvelope<RolePermissions>>(
            $"/api/internal/rbac/permissions?role={Uri.EscapeDataString(role)}", cancellationToken);
        return envelope.Data?.Permissions ?? [];
    }

    private sealed record RolePermissions(string Role, IReadOnlyList<string> Permissions);
}
