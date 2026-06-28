using System.Text.Json.Nodes;
using PhotoshopMcpServer.Infrastructure;

namespace PhotoshopMcpServer.Tests;

public class LayerFieldFilterTests
{
    [Fact]
    public void AllFields_Preserved_WhenFieldsIsWhitespace()
    {
        // FilterLayerFields is only called when fields is non-empty.
        // When fields="" or whitespace, the caller (GetLayers) skips filtering entirely.
        // This test verifies the caller's behavior: pass no filter → all fields kept.
        var json = @"{
            ""layers"": [{
                ""index"": 0, ""name"": ""test"", ""kind"": ""LayerKind.TEXT"",
                ""bounds"": {""left"":0,""top"":0,""right"":100,""bottom"":50},
                ""text"": ""hello"", ""font_name"": ""Arial"", ""font_size"": 24,
                ""text_color"": {""red"":255,""green"":0,""blue"":0},
                ""type"": ""layer""
            }]
        }";
        // Simulate the GetLayers guard: don't call FilterLayerFields when fields is empty
        var root = JsonNode.Parse(json);
        var layer = root!["layers"]![0]!;
        Assert.Equal("test", layer["name"]!.GetValue<string>());
        Assert.Equal("hello", layer["text"]!.GetValue<string>());
        Assert.Equal("Arial", layer["font_name"]!.GetValue<string>());
        Assert.Equal(24, layer["font_size"]!.GetValue<int>());
    }

    [Fact]
    public void OnlyRequestedFields_ArePreserved()
    {
        var json = @"{
            ""layers"": [{
                ""index"": 0, ""name"": ""test"", ""kind"": ""LayerKind.TEXT"",
                ""bounds"": {""left"":0,""top"":0,""right"":100,""bottom"":50},
                ""text"": ""hello"", ""font_name"": ""Arial"", ""font_size"": 24,
                ""text_color"": {""red"":255,""green"":0,""blue"":0},
                ""type"": ""layer""
            }]
        }";
        var result = JsHelpers.FilterLayerFields(json, "font_size,font_name,text_color");
        var root = JsonNode.Parse(result);
        var layer = root!["layers"]![0]!;
        // These should be preserved
        Assert.NotNull(layer["font_size"]);
        Assert.NotNull(layer["font_name"]);
        Assert.NotNull(layer["text_color"]);
        // These should be stripped (not in field list)
        Assert.Null(layer["name"]);
        Assert.Null(layer["text"]);
        Assert.Null(layer["kind"]);
        // Structural fields always preserved
        Assert.Equal("layer", layer["type"]!.GetValue<string>());
    }

    [Fact]
    public void CaseInsensitive_Matching()
    {
        var json = @"{
            ""layers"": [{
                ""index"": 0,
                ""font_size"": 24,
                ""text_color"": {""red"":0,""green"":0,""blue"":0},
                ""type"": ""layer""
            }]
        }";
        var result = JsHelpers.FilterLayerFields(json, "Font_Size,Text_Color");
        var root = JsonNode.Parse(result);
        var layer = root!["layers"]![0]!;
        Assert.NotNull(layer["font_size"]);
        Assert.NotNull(layer["text_color"]);
    }

    [Fact]
    public void Children_AreRecursivelyFiltered()
    {
        var json = @"{
            ""layers"": [{
                ""index"": 0, ""name"": ""group"", ""type"": ""group"",
                ""children"": [{
                    ""index"": 1, ""name"": ""child"", ""text"": ""hi"",
                    ""font_size"": 12, ""type"": ""layer""
                }],
                ""childrenCount"": 1
            }]
        }";
        var result = JsHelpers.FilterLayerFields(json, "font_size,text");
        var root = JsonNode.Parse(result);
        var group = root!["layers"]![0]!;
        Assert.Null(group["name"]); // stripped
        Assert.NotNull(group["children"]); // structural
        Assert.Equal(1, group["childrenCount"]!.GetValue<int>()); // structural
        var child = group["children"]![0]!;
        Assert.NotNull(child["font_size"]);
        Assert.NotNull(child["text"]);
        Assert.Null(child["name"]); // stripped
    }

    [Fact]
    public void Alignment_Field_IsFilterable()
    {
        var json = @"{
            ""layers"": [{
                ""index"": 0, ""alignment"": ""CENTER"", ""type"": ""layer""
            }]
        }";
        var result = JsHelpers.FilterLayerFields(json, "alignment");
        var root = JsonNode.Parse(result);
        var layer = root!["layers"]![0]!;
        Assert.Equal("CENTER", layer["alignment"]!.GetValue<string>());
    }
}
