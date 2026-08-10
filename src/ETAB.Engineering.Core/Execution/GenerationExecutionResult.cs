namespace ETAB.Engineering.Core.Execution;

public sealed record GenerationExecutionIssue(string Code, string Message);

public sealed class GenerationExecutionResult
{
    public GenerationExecutionResult(
        bool success,
        int created,
        int updated,
        int renamed,
        int deleted,
        IReadOnlyList<GenerationExecutionIssue> issues)
    {
        Success = success;
        Created = created;
        Updated = updated;
        Renamed = renamed;
        Deleted = deleted;
        Issues = issues;
    }

    public bool Success { get; }

    public int Created { get; }

    public int Updated { get; }

    public int Renamed { get; }

    public int Deleted { get; }

    public IReadOnlyList<GenerationExecutionIssue> Issues { get; }
}
