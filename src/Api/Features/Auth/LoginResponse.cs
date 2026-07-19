namespace Contoso.PolicyAssistant.Api.Features.Auth;

public sealed class LoginResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public required string DisplayName { get; init; }
    public required string Username { get; init; }
    public required string[] Roles { get; init; }
    public string Note { get; init; } =
        "Dev JWT stand-in for Entra ID. Azure mapping: app roles + MSAL.";
}
