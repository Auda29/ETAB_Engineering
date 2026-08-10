using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETAB.Engineering.Core.Model;

namespace ETAB.Engineering.Core.Manifest;

public static class SemanticModelHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Compute(EtabProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var root = JsonSerializer.SerializeToNode(project, SerializerOptions)!.AsObject();
        root.Remove("layout");
        NormalizeSemanticArrays(root);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(root, writer);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void NormalizeSemanticArrays(JsonObject root)
    {
        var nodes = root["nodes"]!.AsArray()
            .Select(node => node!.AsObject())
            .OrderBy(node => node["name"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(node => node["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToArray();

        var normalizedNodes = new JsonArray();
        foreach (var sourceNode in nodes)
        {
            var node = sourceNode.DeepClone().AsObject();
            SortCommands(node);
            SortMtpProcedures(node);
            normalizedNodes.Add(node);
        }

        root["nodes"] = normalizedNodes;

        var relations = root["relations"]!.AsArray()
            .Select(relation => relation!.AsObject())
            .OrderBy(relation => relation["kind"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(relation => relation["sourceNodeId"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(relation => relation["targetNodeId"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(relation => relation["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(relation => relation.DeepClone())
            .ToArray();

        root["relations"] = new JsonArray(relations);
    }

    private static void SortCommands(JsonObject node)
    {
        var commands = node["commands"]!.AsArray()
            .Select(command => command!.AsObject())
            .OrderBy(command => command["enumValue"]!.GetValue<uint>())
            .ThenBy(command => command["name"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(command => command["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(command => command.DeepClone())
            .ToArray();

        node["commands"] = new JsonArray(commands);
    }

    private static void SortMtpProcedures(JsonObject node)
    {
        if (node["mtp"] is not JsonObject mtp || mtp["procedures"] is not JsonArray procedures)
        {
            return;
        }

        var sorted = procedures
            .Select(procedure => procedure!.AsObject())
            .OrderBy(procedure => procedure["procedureId"]!.GetValue<uint>())
            .ThenBy(procedure => procedure["name"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(procedure => procedure["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(procedure => procedure.DeepClone())
            .ToArray();

        mtp["procedures"] = new JsonArray(sorted);
    }

    private static void WriteCanonical(JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(
                             property => property.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in jsonArray)
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                break;

            case JsonValue jsonValue:
                jsonValue.WriteTo(writer);
                break;

            case null:
                writer.WriteNullValue();
                break;

            default:
                throw new InvalidOperationException($"Unsupported JSON node type '{node.GetType().Name}'.");
        }
    }
}
