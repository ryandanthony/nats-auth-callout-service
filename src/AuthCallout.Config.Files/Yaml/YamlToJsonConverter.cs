// -----------------------------------------------------------------------
// <copyright file="YamlToJsonConverter.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace AuthCallout.Config.Files.Yaml;

/// <summary>
/// Converts a parsed YAML document into the equivalent <see cref="JsonElement"/> tree, so
/// <see cref="Parsing.ApplicationFileMapper"/> can map YAML and JSON files through identical
/// code (ADR 0007: "YAML and JSON same shape -&gt; identical model"). Uses YamlDotNet's
/// low-level representation model (<see cref="YamlNode"/>) rather than its object-graph
/// deserializer so mapping errors and unknown-field detection stay entirely inside
/// <see cref="Parsing.ApplicationFileMapper"/> for both formats.
/// </summary>
public static class YamlToJsonConverter
{
    /// <summary>
    /// Parses a YAML document's text and converts its single root document into a
    /// <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="yamlText">The YAML document text.</param>
    /// <returns>The equivalent JSON element tree.</returns>
    /// <exception cref="YamlDotNet.Core.YamlException">The text is not well-formed YAML.</exception>
    /// <exception cref="InvalidOperationException">The document contains no root node.</exception>
    public static JsonElement Convert(string yamlText)
    {
        ArgumentNullException.ThrowIfNull(yamlText);

        var yamlStream = new YamlStream();
        using var reader = new StringReader(yamlText);
        yamlStream.Load(reader);

        if (yamlStream.Documents.Count == 0)
        {
            // An empty file: treat as an empty object so the mapper's "required field missing"
            // rules produce a normal, deterministic rejection rather than a special-cased error.
            return JsonNode.Parse("{}")!.Deserialize<JsonElement>();
        }

        var node = ConvertNode(yamlStream.Documents[0].RootNode);
        var json = node is null ? "null" : node.ToJsonString();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static JsonNode? ConvertNode(YamlNode node) => node switch
    {
        YamlScalarNode scalar => ConvertScalar(scalar),
        YamlSequenceNode sequence => ConvertSequence(sequence),
        YamlMappingNode mapping => ConvertMapping(mapping),
        _ => throw new NotSupportedException($"Unsupported YAML node type '{node.GetType()}'."),
    };

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var array = new JsonArray();
        foreach (var child in sequence.Children)
        {
            array.Add(ConvertNode(child));
        }

        return array;
    }

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var obj = new JsonObject();
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            var key = keyNode is YamlScalarNode keyScalar
                ? keyScalar.Value ?? string.Empty
                : throw new NotSupportedException("YAML mapping keys must be scalars.");
            obj[key] = ConvertNode(valueNode);
        }

        return obj;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        // Explicit YAML 1.1 quoted/plain-scalar tags decide type unambiguously; an untagged
        // plain scalar falls back to YAML's implicit-typing rules (bool/null/number/string),
        // matching how editors and `yq` interpret the same file.
        if (scalar.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted
            or YamlDotNet.Core.ScalarStyle.DoubleQuoted
            or YamlDotNet.Core.ScalarStyle.Literal
            or YamlDotNet.Core.ScalarStyle.Folded)
        {
            return JsonValue.Create(scalar.Value);
        }

        var value = scalar.Value;
        if (value is null)
        {
            return null;
        }

        if (IsYamlNull(value))
        {
            return null;
        }

        if (TryGetBool(value, out var boolValue))
        {
            return JsonValue.Create(boolValue);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return JsonValue.Create(longValue);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return JsonValue.Create(doubleValue);
        }

        return JsonValue.Create(value);
    }

    private static bool IsYamlNull(string value) =>
        value.Length == 0
        || string.Equals(value, "~", StringComparison.Ordinal)
        || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBool(string value, out bool result)
    {
        switch (value)
        {
            case "true" or "True" or "TRUE":
                {
                    result = true;
                    return true;
                }

            case "false" or "False" or "FALSE":
                {
                    result = false;
                    return true;
                }

            default:
                {
                    result = false;
                    return false;
                }
        }
    }
}
