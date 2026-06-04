using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orka.Infrastructure.Utilities;

public static class LearnerSafeContentJson
{
    private static readonly HashSet<string> BlockedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "answer",
        "answerKey",
        "answers",
        "correct",
        "correctAnswer",
        "correctAnswers",
        "correctOption",
        "correctOptionId",
        "correctOptionKey",
        "explanation",
        "explanations",
        "isCorrect",
        "key",
        "rationale",
        "rubric",
        "score",
        "scoring",
        "scoringRule",
        "solution",
        "solutions"
    };

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "alt",
        "altText",
        "asset",
        "assets",
        "body",
        "caption",
        "cells",
        "children",
        "columns",
        "content",
        "data",
        "description",
        "height",
        "headers",
        "id",
        "kind",
        "items",
        "label",
        "labels",
        "layout",
        "legend",
        "markdown",
        "media",
        "mimeType",
        "prompt",
        "sourceReadiness",
        "status",
        "rows",
        "src",
        "style",
        "table",
        "text",
        "title",
        "type",
        "unit",
        "url",
        "value",
        "values",
        "width"
    };

    private static readonly HashSet<string> AllowedAssessmentMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "axis",
        "axes",
        "band",
        "bands",
        "bucket",
        "buckets",
        "calibration",
        "cognitiveSkill",
        "concept",
        "conceptKey",
        "conceptKeys",
        "conceptLabel",
        "confidence",
        "criterion",
        "criteria",
        "diagnosticSignal",
        "diagnosticSignals",
        "difficulty",
        "evidence",
        "evidenceExpected",
        "gap",
        "gaps",
        "id",
        "kind",
        "label",
        "labels",
        "level",
        "levels",
        "mastery",
        "measure",
        "measures",
        "metadata",
        "misconception",
        "misconceptionKey",
        "misconceptionTarget",
        "mode",
        "name",
        "needsReview",
        "outcome",
        "outcomes",
        "probe",
        "probes",
        "reason",
        "reasons",
        "rule",
        "rules",
        "scoringMode",
        "severity",
        "signal",
        "signals",
        "skill",
        "skills",
        "source",
        "status",
        "subskill",
        "subskills",
        "tag",
        "tags",
        "target",
        "targets",
        "type",
        "value",
        "values",
        "weight",
        "weights"
    };

    public static string? Sanitize(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return contentJson;
        }

        try
        {
            var node = JsonNode.Parse(contentJson);
            var sanitized = SanitizeNode(node);
            return sanitized?.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? SanitizeAssessmentMetadata(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return contentJson;
        }

        try
        {
            var node = JsonNode.Parse(contentJson);
            var sanitized = SanitizeNode(node, AllowedAssessmentMetadataKeys);
            return sanitized?.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode? SanitizeNode(JsonNode? node) =>
        SanitizeNode(node, AllowedKeys);

    private static JsonNode? SanitizeNode(JsonNode? node, HashSet<string> allowedKeys)
    {
        return node switch
        {
            JsonObject obj => SanitizeObject(obj, allowedKeys),
            JsonArray array => SanitizeArray(array, allowedKeys),
            JsonValue value => JsonNode.Parse(value.ToJsonString()),
            _ => null
        };
    }

    private static JsonObject SanitizeObject(JsonObject source, HashSet<string> allowedKeys)
    {
        var target = new JsonObject();
        foreach (var property in source)
        {
            if (BlockedKeys.Contains(property.Key) || !allowedKeys.Contains(property.Key))
            {
                continue;
            }

            var value = SanitizeNode(property.Value, allowedKeys);
            if (value != null)
            {
                target[property.Key] = value;
            }
        }

        return target;
    }

    private static JsonArray SanitizeArray(JsonArray source, HashSet<string> allowedKeys)
    {
        var target = new JsonArray();
        foreach (var item in source)
        {
            var value = SanitizeNode(item, allowedKeys);
            if (value != null)
            {
                target.Add(value);
            }
        }

        return target;
    }
}
