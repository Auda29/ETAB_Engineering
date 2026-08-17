using System.Security;
using System.Text;
using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Generation;

internal static class TwinCatDutRenderer
{
    public static string RenderCommandEnum(
        string typeName,
        string sourceId,
        Guid twinCatGuid,
        string productVersion,
        IEnumerable<EtabCommand> commands)
    {
        var declaration = new StringBuilder();
        AppendGeneratedMarker(declaration, sourceId, GeneratedArtifactKind.CommandEnum);
        declaration.AppendLine("{attribute 'qualified_only'}");
        declaration.AppendLine("{attribute 'strict'}");
        declaration.AppendLine("{attribute 'to_string'}");
        declaration.AppendLine($"TYPE {typeName} :");
        declaration.AppendLine("(");

        var orderedCommands = commands
            .OrderBy(command => command.EnumValue)
            .ThenBy(command => command.Name, StringComparer.Ordinal)
            .ThenBy(command => command.Id, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < orderedCommands.Length; index++)
        {
            var command = orderedCommands[index];
            var suffix = index + 1 == orderedCommands.Length ? string.Empty : ",";
            declaration.AppendLine($"    {command.Name} := {command.EnumValue}{suffix}");
        }

        declaration.AppendLine(");");
        declaration.AppendLine("END_TYPE");

        return WrapDut(typeName, twinCatGuid, productVersion, declaration.ToString());
    }

    public static string RenderRequestDut(
        string typeName,
        string commandEnumName,
        EtabNode node,
        string sourceId,
        Guid twinCatGuid,
        string productVersion,
        IReadOnlyList<EtabField> payload)
    {
        var declaration = new StringBuilder();
        AppendGeneratedMarker(declaration, sourceId, GeneratedArtifactKind.RequestDut);
        declaration.AppendLine($"TYPE {typeName} :");
        declaration.AppendLine("STRUCT");
        switch (node.Kind)
        {
            case "applicationUnit":
            case "commandUnit":
                declaration.AppendLine("    bExecute : BOOL;");
                declaration.AppendLine($"    eCommand : {commandEnumName};");
                declaration.AppendLine("    nCommandID : UDINT;");
                break;

            case "recipeManager":
                declaration.AppendLine("    bExecute : BOOL;");
                declaration.AppendLine("    eCommand : ETAB.E_ETAB_RecipeCommand;");
                declaration.AppendLine("    bExternalValid : BOOL := TRUE;");
                declaration.AppendLine("    sSaveAsFileName : Tc2_System.T_MaxString;");
                break;

            case "machineLink":
                declaration.AppendLine("    bEnable : BOOL := TRUE;");
                declaration.AppendLine("    bLocalReqToken : BOOL;");
                declaration.AppendLine("    bLocalBusy : BOOL;");
                declaration.AppendLine("    bLocalError : BOOL;");
                declaration.AppendLine("    nLocalState : DINT;");
                declaration.AppendLine("    stRx : ETAB.ST_ETAB_MachineLinkData;");
                declaration.AppendLine("    bBridgeOk : BOOL := TRUE;");
                break;

            default:
                throw new InvalidOperationException($"Unsupported node kind '{node.Kind}'.");
        }
        AppendFields(declaration, payload);
        declaration.AppendLine("END_STRUCT");
        declaration.AppendLine("END_TYPE");

        return WrapDut(typeName, twinCatGuid, productVersion, declaration.ToString());
    }

    public static string RenderStatusDut(
        string typeName,
        EtabNode node,
        string sourceId,
        Guid twinCatGuid,
        string productVersion)
    {
        var declaration = new StringBuilder();
        AppendGeneratedMarker(declaration, sourceId, GeneratedArtifactKind.StatusDut);
        declaration.AppendLine($"TYPE {typeName} :");
        declaration.AppendLine("STRUCT");

        switch (node.Kind)
        {
            case "applicationUnit":
                declaration.AppendLine("    stUnit : ETAB.ST_ETAB_ApplicationUnitStatus;");
                if (node.Commands.Count > 0)
                {
                    declaration.AppendLine("    stOperation : ETAB.ST_ETAB_CommandStatus;");
                }

                break;

            case "commandUnit":
                declaration.AppendLine("    stCommand : ETAB.ST_ETAB_CommandStatus;");
                break;

            case "recipeManager":
                declaration.AppendLine("    stRecipe : ETAB.ST_ETAB_RecipeStatus;");
                break;

            case "machineLink":
                declaration.AppendLine("    stLink : ETAB.ST_ETAB_MachineLinkStatus;");
                break;

            default:
                throw new InvalidOperationException($"Unsupported node kind '{node.Kind}'.");
        }

        AppendFields(declaration, node.StatusPayload);
        declaration.AppendLine("END_STRUCT");
        declaration.AppendLine("END_TYPE");

        return WrapDut(typeName, twinCatGuid, productVersion, declaration.ToString());
    }

    private static void AppendGeneratedMarker(
        StringBuilder declaration,
        string sourceId,
        GeneratedArtifactKind kind)
    {
        declaration.AppendLine(
            $"(* <auto-generated by ETAB Engineering; source-id: {sourceId}; artifact-kind: {kind.ToContractName()}> *)");
    }

    private static void AppendFields(StringBuilder declaration, IEnumerable<EtabField> fields)
    {
        foreach (var field in fields)
        {
            var dataType = FormatDataType(field);
            var initializer = string.IsNullOrEmpty(field.DefaultValue)
                ? string.Empty
                : $" := {field.DefaultValue}";

            declaration.AppendLine($"    {field.Name} : {dataType}{initializer};");
        }
    }

    private static string FormatDataType(EtabField field)
    {
        if (field.ArrayDimensions is not { Count: > 0 })
        {
            return field.DataType;
        }

        var dimensions = string.Join(
            ", ",
            field.ArrayDimensions.Select(dimension => $"{dimension.Lower}..{dimension.Upper}"));

        return $"ARRAY[{dimensions}] OF {field.DataType}";
    }

    private static string WrapDut(
        string typeName,
        Guid twinCatGuid,
        string productVersion,
        string declaration)
    {
        var escapedName = SecurityElement.Escape(typeName);
        var escapedVersion = SecurityElement.Escape(productVersion);
        var guid = twinCatGuid.ToString("D");
        var normalizedDeclaration = declaration
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var safeDeclaration = normalizedDeclaration
            .Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal);

        return
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            $"<TcPlcObject Version=\"1.1.0.1\" ProductVersion=\"{escapedVersion}\">\n" +
            $"  <DUT Name=\"{escapedName}\" Id=\"{{{guid}}}\">\n" +
            $"    <Declaration><![CDATA[{safeDeclaration}]]></Declaration>\n" +
            $"  </DUT>\n" +
            $"</TcPlcObject>\n";
    }
}
