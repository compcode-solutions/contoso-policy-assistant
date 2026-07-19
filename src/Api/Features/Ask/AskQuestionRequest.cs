namespace Contoso.PolicyAssistant.Api.Features.Ask;

public sealed class AskQuestionRequest
{
    /// <summary>Natural-language policy question from the user.</summary>
    public string Question { get; set; } = string.Empty;
}
