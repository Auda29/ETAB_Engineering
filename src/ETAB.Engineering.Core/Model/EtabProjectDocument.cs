using System.Text.Json.Serialization;

namespace ETAB.Engineering.Core.Model;

public sealed class EtabProjectDocument
{
    public required string SchemaVersion { get; init; }

    public required EtabProject Project { get; init; }

    public required List<EtabNode> Nodes { get; init; }

    public required List<EtabRelation> Relations { get; init; }

    public required EtabLayout Layout { get; init; }
}

public sealed class EtabProject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required string Prefix { get; init; }

    public required string Namespace { get; init; }

    public required EtabLibraryReference EtabLibrary { get; init; }

    [JsonPropertyName("twinCAT")]
    public required TwinCatSettings TwinCat { get; init; }

    public required GenerationSettings Generation { get; init; }
}

public sealed class EtabLibraryReference
{
    public required string Placeholder { get; init; }

    public required string Version { get; init; }
}

public sealed class TwinCatSettings
{
    public required string Version { get; init; }

    public string? PlcProject { get; init; }
}

public sealed class GenerationSettings
{
    public required string GeneratedRoot { get; init; }

    public required string ApplicationRoot { get; init; }

    public bool CreateUserStubs { get; init; }
}

public sealed class EtabNode
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public required string Name { get; init; }

    public required string SymbolStem { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required string Role { get; init; }

    public required NodeGenerationSettings Generate { get; init; }

    public required List<EtabCommand> Commands { get; init; }

    public required List<EtabField> RequestPayload { get; init; }

    public required List<EtabField> StatusPayload { get; init; }

    public ApplicationUnitSettings? ApplicationUnit { get; init; }

    public CommandUnitSettings? CommandUnit { get; init; }

    public RecipeManagerSettings? RecipeManager { get; init; }

    public MachineLinkSettings? MachineLink { get; init; }

    public MtpSettings? Mtp { get; init; }
}

public sealed class NodeGenerationSettings
{
    public bool CommandEnum { get; init; }

    public bool RequestType { get; init; }

    public bool StatusType { get; init; }

    public bool BaseFunctionBlock { get; init; }

    public bool Instance { get; init; }
}

public sealed class EtabCommand
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public uint EnumValue { get; init; }

    [JsonPropertyName("etabCommand")]
    public required string EtabCommandMapping { get; init; }
}

public sealed class EtabField
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string DataType { get; init; }

    public List<ArrayDimension>? ArrayDimensions { get; init; }

    public string? Description { get; init; }

    public string? DefaultValue { get; init; }
}

public sealed class ArrayDimension
{
    public int Lower { get; init; }

    public int Upper { get; init; }
}

public sealed class ApplicationUnitSettings
{
    public required string StartMode { get; init; }

    public required string HomingMode { get; init; }

    public required string StopMode { get; init; }

    public bool KeepRemoteControl { get; init; }

    public bool SetMachineErrorOnCommandError { get; init; }

    public required CommandUnitSettings Command { get; init; }
}

public sealed class CommandUnitSettings
{
    public int StartState { get; init; }

    public bool ResetErrorOnStart { get; init; }
}

public sealed class RecipeManagerSettings
{
    public required string DataType { get; init; }

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required string XPath { get; init; }

    public bool EnableAutoSave { get; init; }

    public bool EnableBackupFile { get; init; }

    public bool RequireExternalValidation { get; init; }
}

public sealed class MachineLinkSettings
{
    public required string BridgeType { get; init; }

    public bool IsPrimary { get; init; }

    public required string WatchdogTime { get; init; }

    public bool PrimaryWinsTie { get; init; }

    public bool AllowTokenWithoutPartnerAlive { get; init; }

    public bool ClearTxWhenDisabled { get; init; }
}

public sealed class MtpSettings
{
    public bool Exposed { get; init; }

    public string? ServiceName { get; init; }

    public required List<MtpProcedure> Procedures { get; init; }
}

public sealed class MtpProcedure
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public uint ProcedureId { get; init; }

    public required string CommandId { get; init; }
}

public sealed class EtabRelation
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    public required string SourceNodeId { get; init; }

    public required string TargetNodeId { get; init; }

    public string? Label { get; init; }
}

public sealed class EtabLayout
{
    public required List<NodeLayout> Nodes { get; init; }
}

public sealed class NodeLayout
{
    public required string NodeId { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public string? Group { get; init; }
}
