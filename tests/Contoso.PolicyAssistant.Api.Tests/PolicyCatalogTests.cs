using Contoso.PolicyAssistant.Api.Features.Policies;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class PolicyCatalogTests
{
    private static PolicyCatalog SampleCatalog() => new(
    [
        new PolicyDocument
        {
            Id = "leave",
            Title = "Leave",
            AllowedRoles = ["Employee", "Supervisor", "Admin"],
            FileName = "leave.md",
            BodyMarkdown = "x"
        },
        new PolicyDocument
        {
            Id = "safety-escalate",
            Title = "Workplace Safety Escalation",
            AllowedRoles = ["Supervisor", "Admin"],
            FileName = "safety-escalate.md",
            BodyMarkdown = "y"
        }
    ]);

    [Fact]
    public void Employee_cannot_see_supervisor_only_sop()
    {
        var visible = SampleCatalog().GetVisibleTo(["Employee"]);
        Assert.Single(visible);
        Assert.Equal("leave", visible[0].Id);
    }

    [Fact]
    public void Supervisor_sees_escalate_sop()
    {
        var visible = SampleCatalog().GetVisibleTo(["Supervisor", "Employee"]);
        Assert.Equal(2, visible.Count);
        Assert.Contains(visible, p => p.Id == "safety-escalate");
    }
}
