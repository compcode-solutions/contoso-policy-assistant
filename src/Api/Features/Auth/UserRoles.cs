using System.Security.Claims;

namespace Contoso.PolicyAssistant.Api.Features.Auth;

public static class UserRoles
{
    public static string[] From(ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
