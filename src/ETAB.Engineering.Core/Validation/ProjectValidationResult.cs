using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Validation;

public sealed class ProjectValidationResult
{
    public ProjectValidationResult(
        IReadOnlyList<ValidationIssue> issues,
        EtabProjectDocument? project = null)
    {
        Issues = issues;
        Project = project;
    }

    public bool IsValid => Issues.Count == 0;

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public EtabProjectDocument? Project { get; }
}
