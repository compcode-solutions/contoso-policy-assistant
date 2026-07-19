using Contoso.PolicyAssistant.Api.Features.Logging;

namespace Contoso.PolicyAssistant.Api.Tests;

internal sealed class NullAiCallLogger : IAiCallLogger
{
    public void Log(AiCallRecord record) { }
}
