namespace ETAB.Engineering.Core.Generation;

public enum GeneratedArtifactKind
{
    CommandEnum,
    RequestDut,
    StatusDut,
    BaseFunctionBlock,
    InstanceGlobalVariableList,
    RelationWiring,
    ProgramCallStructure
}

public static class GeneratedArtifactKindExtensions
{
    public static string ToContractName(this GeneratedArtifactKind kind) => kind switch
    {
        GeneratedArtifactKind.CommandEnum => "command-enum",
        GeneratedArtifactKind.RequestDut => "request-dut",
        GeneratedArtifactKind.StatusDut => "status-dut",
        GeneratedArtifactKind.BaseFunctionBlock => "base-function-block",
        GeneratedArtifactKind.InstanceGlobalVariableList => "instance-gvl",
        GeneratedArtifactKind.RelationWiring => "relation-wiring",
        GeneratedArtifactKind.ProgramCallStructure => "program-call-structure",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
