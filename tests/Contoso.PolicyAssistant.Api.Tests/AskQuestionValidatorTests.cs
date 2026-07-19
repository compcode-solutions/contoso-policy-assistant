using Contoso.PolicyAssistant.Api.Features.Ask;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class AskQuestionValidatorTests
{
    [Fact]
    public void Empty_question_is_invalid()
    {
        var errors = AskQuestionValidator.Validate(new AskQuestionRequest { Question = "   " });
        Assert.True(errors.ContainsKey("question"));
        Assert.Contains("required", errors["question"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Null_body_is_invalid()
    {
        var errors = AskQuestionValidator.Validate(null);
        Assert.True(errors.ContainsKey("request"));
    }

    [Fact]
    public void Too_long_question_is_invalid()
    {
        var q = new string('a', AskQuestionValidator.MaxQuestionLength + 1);
        var errors = AskQuestionValidator.Validate(new AskQuestionRequest { Question = q });
        Assert.True(errors.ContainsKey("question"));
    }

    [Fact]
    public void Valid_question_has_no_errors()
    {
        var errors = AskQuestionValidator.Validate(
            new AskQuestionRequest { Question = "How many leave days do I get?" });
        Assert.Empty(errors);
    }
}
