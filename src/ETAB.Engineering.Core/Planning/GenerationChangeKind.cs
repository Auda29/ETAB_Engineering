namespace ETAB.Engineering.Core.Planning;

public enum GenerationChangeKind
{
    Create,
    Update,
    Rename,
    Delete,
    Unchanged,
    Conflict
}

public static class GenerationChangeKindExtensions
{
    public static string ToContractName(this GenerationChangeKind kind) =>
        kind.ToString().ToLowerInvariant();
}
