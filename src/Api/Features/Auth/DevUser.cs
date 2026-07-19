namespace Contoso.PolicyAssistant.Api.Features.Auth;

public sealed record DevUser(string Username, string Password, string DisplayName, string[] Roles);

/// <summary>
/// Local stand-in for Entra ID users/app roles. Not for production.
/// </summary>
public static class DevUsers
{
    public static readonly IReadOnlyList<DevUser> All =
    [
        new("alice", "pass", "Alice Employee", ["Employee"]),
        new("bob", "pass", "Bob Supervisor", ["Supervisor", "Employee"]),
        new("admin", "pass", "Ada Admin", ["Admin", "Supervisor", "Employee"])
    ];

    public static DevUser? Find(string username, string password) =>
        All.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)
            && u.Password == password);
}
