using Ess.Application.Workflow;
using Ess.Domain.Identity;
using Ess.Domain.Workflow;

namespace Ess.Infrastructure.Workflow;

/// <summary>
/// Starting approval routes, created per company the first time a document type is submitted.
/// HR can change them afterwards in the admin portal; each change creates a new version and running approvals keep theirs.
/// </summary>
public static class WorkflowDefaults
{
    public static WorkflowDefinition Create(Guid companyId, string documentType)
    {
        var baseType = documentType.Split(':')[0];
        var def = new WorkflowDefinition
        {
            CompanyId = companyId,
            Code = documentType.ToUpperInvariant().Replace(':', '-'),
            Name = baseType switch
            {
                DocumentTypes.Leave => "Leave approval",
                DocumentTypes.Expense => "Expense claim approval",
                DocumentTypes.ProfileChange => "Personal details change",
                DocumentTypes.Attendance => "Attendance correction",
                DocumentTypes.Training => "Training request",
                _ => $"{documentType} approval"
            },
            DocumentType = documentType,
            Owner = WorkflowOwner.Portal
        };

        WorkflowStep Step(int seq, string name, ApproverType type, string? role = null, int? deadline = 48, string? escalate = SystemRoles.HrManager) => new()
        {
            WorkflowDefinitionId = def.Id, Sequence = seq, Name = name, ApproverType = type, ApproverRole = role,
            Mode = StepMode.AnyOne, DeadlineHours = deadline, ReminderEveryHours = 24, EscalateToRole = escalate
        };

        switch (baseType)
        {
            case DocumentTypes.Leave:
            case DocumentTypes.Attendance:
                def.Steps.Add(Step(1, "Line manager", ApproverType.LineManager, escalate: SystemRoles.HrOfficer));
                break;
            case DocumentTypes.Expense:
                def.Steps.Add(Step(1, "Line manager", ApproverType.LineManager, escalate: SystemRoles.FinanceManager));
                def.Steps.Add(Step(2, "Finance", ApproverType.Role, SystemRoles.FinanceOfficer, escalate: SystemRoles.FinanceManager));
                break;
            case DocumentTypes.Training:
                def.Steps.Add(Step(1, "Line manager", ApproverType.LineManager, escalate: SystemRoles.HrOfficer));
                def.Steps.Add(Step(2, "HR", ApproverType.Role, SystemRoles.HrOfficer));
                break;
            default: // ProfileChange, HrRequest:*
                def.Steps.Add(Step(1, "HR", ApproverType.Role, SystemRoles.HrOfficer));
                break;
        }
        return def;
    }
}
