using System.Text.Json.Nodes;

namespace PhotoshopMcpServer.Infrastructure;

/// <summary>
/// Shared JavaScript helpers for Photoshop ExtendScript compatibility.
/// ExtendScript is based on ES3 and lacks some modern JS features.
/// </summary>
public static class JsHelpers
{
    /// <summary>
    /// Minimal JSON.stringify polyfill for ExtendScript (ES3).
    /// Include this at the start of any script that calls _json(obj).
    /// Uses single quotes only to avoid escaping issues in C# verbatim strings.
    /// Usage in JS:  return 'OK|' + _json(result);
    /// </summary>
    public const string JsonPolyfill = @"
var _json = function(obj) {
    if (obj === null || obj === undefined) return 'null';
    var t = typeof obj;
    if (t === 'string') return '""' + obj.replace(/\\/g,'\\\\').replace(/""/g,'\\""').replace(/\n/g,'\\n').replace(/\r/g,'\\r') + '""';
    if (t === 'number' || t === 'boolean') return obj.toString();
    if (obj instanceof Array) {
        var a = [];
        for (var i = 0; i < obj.length; i++) a.push(_json(obj[i]));
        return '[' + a.join(',') + ']';
    }
    var p = [];
    for (var k in obj) {
        if (typeof obj[k] === 'function') continue;
        p.push('""' + k + '"":' + _json(obj[k]));
    }
    return '{' + p.join(',') + '}';
};
";

    /// <summary>
    /// Escapes a string for safe inclusion in a JavaScript single-quoted string literal.
    /// </summary>
    public static string EscapeJs(string s)
    {
        return s.Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }

    /// <summary>
    /// Filters layer JSON to only include requested fields.
    /// Structural fields (type, children, childrenCount) are always preserved.
    /// </summary>
    internal static string FilterLayerFields(string json, string fieldsParam)
    {
        var fieldSet = new HashSet<string>(
            fieldsParam.Split(',').Select(f => f.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var root = JsonNode.Parse(json);
        if (root is null) return json;

        var layers = root["layers"];
        if (layers is null) return json;

        FilterLayersArray(layers.AsArray(), fieldSet);

        return root.ToJsonString();
    }

    internal static void FilterLayersArray(JsonArray layers, HashSet<string> fields)
    {
        foreach (var layer in layers)
        {
            if (layer is not JsonObject obj) continue;

            var toRemove = new List<string>();
            foreach (var kvp in obj)
            {
                var key = kvp.Key;
                if (key is "type" or "children" or "childrenCount")
                    continue;
                if (!fields.Contains(key))
                    toRemove.Add(key);
            }

            foreach (var key in toRemove)
                obj.Remove(key);

            if (obj.TryGetPropertyValue("children", out var children) &&
                children is JsonArray childArray)
            {
                FilterLayersArray(childArray, fields);
            }
        }
    }
}
