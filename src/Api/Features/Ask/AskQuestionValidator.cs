namespace Contoso.PolicyAssistant.Api.Features.Ask;

public static class AskQuestionValidator
{
    public const int MaxQuestionLength = 1000;

    /// <summary>
    /// Returns field → error messages. Empty dictionary means valid.
    /// </summary>
    public static Dictionary<string, string[]> Validate(AskQuestionRequest? request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (request is null)
        {
            errors["request"] = ["Request body is required."];
            return errors;
        }

        var q = request.Question?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(q))
        {
            errors["question"] = ["Question is required."];
            return errors;
        }

        if (q.Length > MaxQuestionLength)
        {
            errors["question"] =
            [
                $"Question must be at most {MaxQuestionLength} characters."
            ];
        }

        return errors;
    }
}
