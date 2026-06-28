using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PhotoshopMcpServer.Infrastructure;
using PhotoshopMcpServer.Services;

namespace PhotoshopMcpServer.Tools;

/// <summary>
/// Layer operations: create, read, update, delete, export.
/// To add new layer tools, add methods to this class (or create a new class
/// with [McpServerToolType] — it will be auto-discovered by WithToolsFromAssembly()).
/// </summary>
[McpServerToolType]
public sealed class LayerTools
{
    private readonly PhotoshopBridge _ps;
    private readonly ILogger<LayerTools> _logger;

    public LayerTools(PhotoshopBridge ps, ILogger<LayerTools> logger)
    {
        _ps = ps;
        _logger = logger;
    }

    // ==================================================================
    // create_text_layer
    // ==================================================================

    [McpServerTool(Name = "photoshop_create_text_layer")]
    [Description("Create a text layer in the active document.")]
    public async Task<object> CreateTextLayer(
        [Description("Text content")] string text,
        [Description("X position in pixels")] int x = 100,
        [Description("Y position in pixels")] int y = 100,
        [Description("Font size")] int size = 24,
        [Description("Red color component (0-255)")] int color_r = 0,
        [Description("Green color component (0-255)")] int color_g = 0,
        [Description("Blue color component (0-255)")] int color_b = 0)
    {
        var script = $@"
(function() {{
    try {{
        var doc = app.activeDocument;
        if (!doc) return 'ERR|No active document';
        var layer = doc.artLayers.add();
        layer.kind = LayerKind.TEXT;
        var ti = layer.textItem;
        ti.contents = '{EscapeJs(text)}';
        ti.position = [{x}, {y}];
        ti.size = {size};
        var c = new SolidColor();
        c.rgb.red = {color_r};
        c.rgb.green = {color_g};
        c.rgb.blue = {color_b};
        ti.color = c;
        return 'OK|'+layer.name;
    }} catch(e) {{
        return 'ERR|'+e.toString();
    }}
}})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            var parts = raw.Split('|');
            return new { success = true, layer_name = parts.Length > 1 ? parts[1] : "Unknown" };
        }
        catch (TimeoutException)
        {
            return new { success = false, error = "Operation timed out." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, detailed_error = ex.ToString() };
        }
    }

    // ==================================================================
    // create_solid_color_layer
    // ==================================================================

    [McpServerTool(Name = "photoshop_create_solid_color_layer")]
    [Description("Create a solid color fill layer.")]
    public async Task<object> CreateSolidColorLayer(
        [Description("Red component (0-255)")] int color_r = 255,
        [Description("Green component (0-255)")] int color_g = 0,
        [Description("Blue component (0-255)")] int color_b = 0,
        [Description("Layer name")] string name = "Color Fill")
    {
        var script = $@"
(function() {{
    try {{
        var doc = app.activeDocument;
        if (!doc) return 'ERR|No active document';
        var layer = doc.artLayers.add();
        layer.name = '{EscapeJs(name)}';
        var c = new SolidColor();
        c.rgb.red = {color_r};
        c.rgb.green = {color_g};
        c.rgb.blue = {color_b};
        doc.selection.selectAll();
        doc.selection.fill(c);
        doc.selection.deselect();
        return 'OK';
    }} catch(e) {{
        return 'ERR|'+e.toString();
    }}
}})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            return new { success = true, layer_name = name };
        }
        catch (TimeoutException)
        {
            return new { success = false, error = "Operation timed out." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    // ==================================================================
    // get_layer_info (comprehensive — all in one JS call)
    // ==================================================================

    [McpServerTool(Name = "photoshop_get_layer_info")]
    [Description("Get detailed information about a layer by name or index.")]
    public async Task<object> GetLayerInfo(
        [Description("Layer name (fuzzy match)")] string layer_name = "",
        [Description("Layer index (0-based, takes priority)")] int layer_index = -1)
    {
        if (string.IsNullOrEmpty(layer_name) && layer_index < 0)
            return new { success = false, error = "Must specify layer_name or layer_index" };

        var searchField = layer_index >= 0 ? "index" : "name";
        var searchValue = layer_index >= 0 ? layer_index.ToString() : JsonSerializer.Serialize(layer_name);
        var searchValueJson = JsonSerializer.Serialize(layer_name);

        var script = $@"
(function() {{
    var doc = app.activeDocument;
    if (!doc) return 'ERR|No active document';

    // Flatten all layers/groups
    function flatten(container, result) {{
        for (var i = 0; i < container.artLayers.length; i++)
            result.push(container.artLayers[i]);
        for (var j = 0; j < container.layerSets.length; j++) {{
            result.push(container.layerSets[j]);
            flatten(container.layerSets[j], result);
        }}
    }}
    var all = [];
    flatten(doc, all);

    var target = null;
    if ('{searchField}' === 'index') {{
        var idx = {searchValue};
        if (idx >= 0 && idx < all.length) target = all[idx];
    }} else {{
        var q = {searchValueJson};
        var ql = q.toLowerCase();
        // Priority 1: exact name, non-LayerSet
        for (var i = 0; i < all.length; i++) {{
            if (all[i].name.toLowerCase() === ql && all[i].typename !== 'LayerSet')
                {{ target = all[i]; break; }}
        }}
        // Priority 2: exact name, any type
        if (!target)
            for (var i = 0; i < all.length; i++)
                if (all[i].name.toLowerCase() === ql) {{ target = all[i]; break; }}
        // Priority 3: substring, non-LayerSet
        if (!target)
            for (var i = 0; i < all.length; i++)
                if (all[i].name.toLowerCase().indexOf(ql) !== -1 && all[i].typename !== 'LayerSet')
                    {{ target = all[i]; break; }}
        // Priority 4: substring, any type
        if (!target)
            for (var i = 0; i < all.length; i++)
                if (all[i].name.toLowerCase().indexOf(ql) !== -1)
                    {{ target = all[i]; break; }}
    }}
    if (!target) return 'ERR|Layer not found';

    var info = {{}};
    info.name = target.name;
    try {{ info.visible = target.visible; }} catch(e) {{ info.visible = true; }}
    try {{ info.kind = target.kind; }} catch(e) {{ info.kind = -1; }}
    info.typename = target.typename;

    // Detect text layers by probing textItem (kind is unreliable across PS versions)
    try {{ info.isText = target.textItem != null; }} catch(e) {{ info.isText = false; }}

    var b = target.bounds;
    info.bl = b[0].value; info.bt = b[1].value;
    info.br = b[2].value; info.bb = b[3].value;

    try {{ info.opacity = target.opacity; }} catch(e) {{ info.opacity = 100; }}
    try {{ info.blendMode = target.blendMode.toString(); }} catch(e) {{ info.blendMode = ''; }}
    try {{ info.allLocked = target.allLocked; }} catch(e) {{ info.allLocked = false; }}
    try {{ info.locked = target.locked; }} catch(e) {{ info.locked = false; }}

    // Text layer extras
    if (info.isText) {{
        // Use Action Manager for all text properties (more reliable than DOM TextItem)
        try {{
            var ref = new ActionReference();
            ref.putIdentifier(stringIDToTypeID('layer'), target.id);
            var desc = executeActionGet(ref);
            var td = desc.getObjectValue(stringIDToTypeID('textKey'));
            // Text content
            if (td.hasKey(stringIDToTypeID('textKey')))
                info.text = td.getString(stringIDToTypeID('textKey'));
            // Style range list
            var sl = td.getList(stringIDToTypeID('textStyleRange'));
            if (sl.count > 0) {{
                var sr = sl.getObjectValue(0);
                var ts = sr.getObjectValue(stringIDToTypeID('textStyle'));
                // Font name
                if (ts.hasKey(stringIDToTypeID('font')))
                    info.fontName = ts.getString(stringIDToTypeID('font'));
                // Font size
                if (ts.hasKey(stringIDToTypeID('size')))
                    info.fontSize = ts.getDouble(stringIDToTypeID('size'));
                // Walk the textStyle chain to resolve inherited color
                var cd = null;
                var styleCursor = ts;
                while (styleCursor && !cd) {{
                    if (styleCursor.hasKey(stringIDToTypeID('color')))
                        cd = styleCursor.getObjectValue(stringIDToTypeID('color'));
                    else if (styleCursor.hasKey(stringIDToTypeID('baseParentStyle')))
                        styleCursor = styleCursor.getObjectValue(stringIDToTypeID('baseParentStyle'));
                    else
                        break;
                }}
                if (cd && cd.hasKey(stringIDToTypeID('red'))) {{
                    info.tcR = Math.round(cd.getDouble(stringIDToTypeID('red')));
                    info.tcG = Math.round(cd.getDouble(stringIDToTypeID('green')));
                    info.tcB = Math.round(cd.getDouble(stringIDToTypeID('blue')));
                }}
            }}
        }} catch(e) {{ info.textError = 'AM: '+e.toString(); }}
    }}

    // LayerSet extras
    if (info.typename === 'LayerSet') {{
        try {{
            info.childrenCount = target.artLayers.length + target.layerSets.length;
        }} catch(e) {{ info.childrenCount = 0; }}
    }}

    var parts = ['OK'];
    for (var k in info) parts.push(k + '=' + info[k]);
    return parts.join('|');
}})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            // Parse pipe-delimited key=value pairs
            var props = new Dictionary<string, string>();
            var segments = raw.Split('|');
            for (int i = 1; i < segments.Length; i++)
            {
                var eqIdx = segments[i].IndexOf('=');
                if (eqIdx > 0)
                    props[segments[i][..eqIdx]] = segments[i][(eqIdx + 1)..];
            }

            var kindStr = props.GetValueOrDefault("kind", "-1");
            var kind = int.TryParse(kindStr, out var k) ? k : -1;
            var isLayerSet = props.GetValueOrDefault("typename") == "LayerSet";
            var isText = props.GetValueOrDefault("isText", "false") == "true";

            var result = new Dictionary<string, object?>
            {
                ["success"] = true,
                ["name"] = props.GetValueOrDefault("name", ""),
                ["visible"] = props.GetValueOrDefault("visible", "true") == "true",
                ["kind"] = kind,
                ["type"] = isLayerSet ? "layerSet" : "layer",
            };

            // Bounds
            if (double.TryParse(props.GetValueOrDefault("bl"), out var left) &&
                double.TryParse(props.GetValueOrDefault("bt"), out var top) &&
                double.TryParse(props.GetValueOrDefault("br"), out var right) &&
                double.TryParse(props.GetValueOrDefault("bb"), out var bottom))
            {
                result["bounds"] = new { left, top, right, bottom };
                result["width"] = right - left;
                result["height"] = bottom - top;
            }

            result["opacity"] = double.TryParse(props.GetValueOrDefault("opacity", "100"), out var op) ? op : 100.0;
            result["blend_mode"] = props.GetValueOrDefault("blendMode", "");
            result["all_locked"] = props.GetValueOrDefault("allLocked", "false") == "true";
            result["locked"] = props.GetValueOrDefault("locked", "false") == "true";

            // Text properties (detected via isText, not kind)
            if (kind == 2 || isText)
            {
                result["text"] = props.GetValueOrDefault("text", "");
                result["font_name"] = props.GetValueOrDefault("fontName", "");
                result["font_size"] = double.TryParse(props.GetValueOrDefault("fontSize"), out var fs) ? fs : 0;
                if (int.TryParse(props.GetValueOrDefault("tcR"), out var tr) &&
                    int.TryParse(props.GetValueOrDefault("tcG"), out var tg) &&
                    int.TryParse(props.GetValueOrDefault("tcB"), out var tb))
                {
                    result["text_color"] = new { red = tr, green = tg, blue = tb };
                }
                else
                {
                    result["text_color"] = null;
                }
                // Surface any JS-side errors for diagnostics
                foreach (var errKey in new[] { "textError", "fontError", "sizeError", "colorError" })
                {
                    if (props.TryGetValue(errKey, out var errVal) && !string.IsNullOrEmpty(errVal))
                        result[errKey] = errVal;
                }
            }

            // Group properties
            if (isLayerSet)
            {
                result["children_count"] = int.TryParse(props.GetValueOrDefault("childrenCount"), out var cc) ? cc : 0;
            }

            return result;
        }
        catch (TimeoutException)
        {
            return new { success = false, error = "Operation timed out." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, detailed_error = ex.ToString() };
        }
    }

    // ==================================================================
    // delete_layer
    // ==================================================================

    [McpServerTool(Name = "photoshop_delete_layer")]
    [Description("Delete a layer by name or index.")]
    public async Task<object> DeleteLayer(
        [Description("Layer name (fuzzy match)")] string layer_name = "",
        [Description("Layer index (0-based, takes priority)")] int layer_index = -1)
    {
        if (string.IsNullOrEmpty(layer_name) && layer_index < 0)
            return new { success = false, error = "Must specify layer_name or layer_index" };

        var searchField = layer_index >= 0 ? "index" : "name";
        var searchValue = layer_index >= 0 ? layer_index.ToString() : JsonSerializer.Serialize(layer_name);
        var searchValueJson = JsonSerializer.Serialize(layer_name);

        var script = $@"
(function() {{
    var doc = app.activeDocument;
    if (!doc) return 'ERR|No active document';
    function flatten(container, result) {{
        for (var i = 0; i < container.artLayers.length; i++) result.push(container.artLayers[i]);
        for (var j = 0; j < container.layerSets.length; j++) {{ result.push(container.layerSets[j]); flatten(container.layerSets[j], result); }}
    }}
    var all = []; flatten(doc, all);
    var target = null;
    if ('{searchField}' === 'index') {{
        var idx = {searchValue};
        if (idx >= 0 && idx < all.length) target = all[idx];
    }} else {{
        var q = {searchValueJson};
        var ql = q.toLowerCase();
        var match = function(t) {{ return t.name.toLowerCase().indexOf(ql) !== -1; }};
        for (var i = 0; i < all.length; i++) {{ if (all[i].name.toLowerCase() === ql) {{ target = all[i]; break; }} }}
        if (!target) for (var i = 0; i < all.length; i++) {{ if (match(all[i])) {{ target = all[i]; break; }} }}
    }}
    if (!target) return 'ERR|Layer not found';
    var name = target.name;
    target.remove();
    return 'OK|'+name;
}})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            var deletedName = raw.Split('|').Skip(1).FirstOrDefault() ?? "Unknown";
            return new { success = true, deleted_layer = deletedName, message = $"Layer '{deletedName}' deleted successfully" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    // ==================================================================
    // modify_layer
    // ==================================================================

    [McpServerTool(Name = "photoshop_modify_layer")]
    [Description("Modify layer properties: rename, reposition, change text, visibility, opacity, blend mode.")]
    public async Task<object> ModifyLayer(
        [Description("Layer name to find (fuzzy match)")] string layer_name = "",
        [Description("Layer index (0-based, takes priority)")] int layer_index = -1,
        [Description("New layer name")] string new_name = "",
        [Description("New text content (text layers only)")] string text = "",
        [Description("New X position")] int? x = null,
        [Description("New Y position")] int? y = null,
        [Description("Set visibility")] bool? visible = null,
        [Description("Set opacity (0-100)")] double? opacity = null,
        [Description("Blend mode: multiply, screen, overlay, etc.")] string blend_mode = "")
    {
        if (string.IsNullOrEmpty(layer_name) && layer_index < 0)
            return new { success = false, error = "Must specify layer_name or layer_index" };

        // Pre-validate blend mode
        var bmEnum = "";
        if (!string.IsNullOrEmpty(blend_mode))
        {
            bmEnum = BlendModeMap.GetEnumName(blend_mode) ?? "";
            if (string.IsNullOrEmpty(bmEnum))
                return new { success = false, error = $"Unknown blend mode '{blend_mode}'" };
        }

        // Build all conditionals inside JS to avoid C# string-concat syntax bugs
        var searchField = layer_index >= 0 ? "index" : "name";
        var searchValue = layer_index >= 0 ? layer_index.ToString() : JsonSerializer.Serialize(layer_name);
        var searchValueJson = JsonSerializer.Serialize(layer_name);
        var escNewName = EscapeJs(new_name);
        var escText = EscapeJs(text);
        var visVal = visible.HasValue ? (visible.Value ? "true" : "false") : "";
        var xTag = x.HasValue ? "1" : "0";
        var xVal = x.HasValue ? x.Value.ToString() : "0";
        var yTag = y.HasValue ? "1" : "0";
        var yVal = y.HasValue ? y.Value.ToString() : "0";

        var script = $@"
(function() {{
    var doc = app.activeDocument;
    if (!doc) return 'ERR|No active document';
    function flatten(c, r) {{ for (var i=0;i<c.artLayers.length;i++) r.push(c.artLayers[i]); for (var j=0;j<c.layerSets.length;j++){{r.push(c.layerSets[j]);flatten(c.layerSets[j],r);}} }}
    var all = []; flatten(doc, all);
    var target = null;
    if ('{searchField}'==='index') {{ var idx={searchValue}; if(idx>=0&&idx<all.length) target=all[idx]; }}
    else {{ var q={searchValueJson},ql=q.toLowerCase();
        for(var i=0;i<all.length;i++) if(all[i].name.toLowerCase()===ql){{target=all[i];break;}}
        if(!target) for(var i=0;i<all.length;i++) if(all[i].name.toLowerCase().indexOf(ql)!==-1){{target=all[i];break;}} }}
    if(!target) return 'ERR|Layer not found';

    var changes = [];
    var newName='{escNewName}';
    var newText='{escText}';
    var hasX={xTag}, hasY={yTag}, nx={xVal}, ny={yVal};
    var hasVis={(!string.IsNullOrEmpty(visVal) ? "1" : "0")}, visVal={(!string.IsNullOrEmpty(visVal) ? visVal : "false")};
    var hasOp={((opacity.HasValue ? "1" : "0"))}, opVal={(opacity.HasValue ? opacity.Value.ToString() : "0")};
    var bmStr='{EscapeJs(bmEnum)}';

    if (newName !== '') {{
        try {{ target.name = newName; changes.push('Renamed'); }}
        catch(e) {{ return 'ERR|Rename failed: '+e.toString(); }}
    }}
    if (newText !== '') {{
        if (target.kind !== LayerKind.TEXT) return 'ERR|Cannot set text on non-text layer';
        try {{ target.textItem.contents = newText; changes.push('Text set'); }}
        catch(e) {{ return 'ERR|Text failed: '+e.toString(); }}
    }}
    if (hasX == 1 || hasY == 1) {{
        try {{
            var b = target.bounds;
            var mx = (hasX == 1) ? nx : b[0].value;
            var my = (hasY == 1) ? ny : b[1].value;
            if (target.kind === LayerKind.TEXT) target.textItem.position = [mx, my];
            else target.translate(mx - b[0].value, my - b[1].value);
            changes.push('Moved');
        }} catch(e) {{ return 'ERR|Move failed: '+e.toString(); }}
    }}
    if (hasVis == 1) {{
        try {{ target.visible = visVal; changes.push('Visibility'); }}
        catch(e) {{ return 'ERR|Visibility failed: '+e.toString(); }}
    }}
    if (hasOp == 1) {{
        try {{ target.opacity = opVal; changes.push('Opacity'); }}
        catch(e) {{ return 'ERR|Opacity failed: '+e.toString(); }}
    }}
    if (bmStr !== '') {{
        try {{ target.blendMode = BlendMode[bmStr]; changes.push('Blend mode'); }}
        catch(e) {{ return 'ERR|Blend mode failed: '+e.toString(); }}
    }}

    if (changes.length === 0) return 'OK|'+target.name+'|No changes requested|0';
    var msg = 'Applied '+changes.length+' change(s): ';
    for (var c=0; c<changes.length; c++) {{ if (c>0) msg+=', '; msg+=changes[c]; }}
    return 'OK|'+target.name+'|'+msg+'|'+changes.length;
}})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            var parts = raw.Split('|');
            if (parts.Length >= 4)
            {
                return new
                {
                    success = true,
                    layer_name = parts[1],
                    message = parts[2],
                    changes_count = int.Parse(parts[3]),
                };
            }

            return new { success = true, message = "No changes made" };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }


    // ==================================================================
    // export_layer
    // ==================================================================

    [McpServerTool(Name = "photoshop_export_layer")]
    [Description("Export a specific layer as a PNG file to disk.")]
    public async Task<object> ExportLayer(
        [Description("Absolute output path for the PNG file")] string output_path,
        [Description("Layer name (fuzzy match)")] string layer_name = "",
        [Description("Layer index (0-based, takes priority)")] int layer_index = -1,
        [Description("Export scale factor (0-1.0], 0.25 = 25%%)")] double scale = 1.0,
        [Description("Trim transparent pixels")] bool trim = false)
    {
        // Validate
        if (string.IsNullOrEmpty(output_path))
            return new { success = false, error = "output_path is required" };

        if (scale <= 0 || scale > 1.0)
            return new { success = false, error = $"scale must be > 0 and <= 1.0, got {scale}" };

        if (string.IsNullOrEmpty(layer_name) && layer_index < 0)
            return new { success = false, error = "Must specify layer_name or layer_index" };

        var fullPath = Path.GetFullPath(output_path);
        var dir = Path.GetDirectoryName(fullPath)!;
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { return new { success = false, error = $"Cannot create directory {dir}: {ex.Message}" }; }

        var searchField = layer_index >= 0 ? "index" : "name";
        var searchValue = layer_index >= 0 ? layer_index.ToString() : JsonSerializer.Serialize(layer_name);
        var searchValueJson = JsonSerializer.Serialize(layer_name);
        var escapedPath = fullPath.Replace("\\", "\\\\");

        var script = $@"
(function() {{
    var origDialogs = app.displayDialogs;
    app.displayDialogs = DialogModes.NO;
    var origDoc, origRulerUnits;
    try {{
        origDoc = app.activeDocument;
        if (!origDoc) return 'ERR|No active document';
        origRulerUnits = app.preferences.rulerUnits;
        app.preferences.rulerUnits = Units.PIXELS;

        function findByIndex(container, targetIdx, counter) {{
            if (counter === undefined) counter = {{v:0}};
            for (var i=0;i<container.artLayers.length;i++) {{ if(counter.v===targetIdx) return container.artLayers[i]; counter.v++; }}
            for (var j=0;j<container.layerSets.length;j++) {{ if(counter.v===targetIdx) return container.layerSets[j]; counter.v++; var f=findByIndex(container.layerSets[j],targetIdx,counter); if(f) return f; }}
            return null;
        }}
        var targetLayer;
        if ('{searchField}'==='index') {{ targetLayer=findByIndex(origDoc,{searchValue}); }}
        else {{ var q={searchValueJson},ql=q.toLowerCase();
            function flatFind(c) {{
                for(var i=0;i<c.layerSets.length;i++) {{ if(c.layerSets[i].name.toLowerCase().indexOf(ql)!==-1) return c.layerSets[i]; var f=flatFind(c.layerSets[i]); if(f) return f; }}
                for(var i=0;i<c.artLayers.length;i++) if(c.artLayers[i].name.toLowerCase().indexOf(ql)!==-1) return c.artLayers[i];
                return null;
            }}
            targetLayer=flatFind(origDoc); }}
        if(!targetLayer) return 'ERR|Layer not found';
        if(targetLayer.typename==='LayerSet') return 'ERR|Cannot export a layer group';

        var bounds = targetLayer.bounds;
        var docW=origDoc.width.value, docH=origDoc.height.value;
        var left=Math.max(0,Math.floor(bounds[0].value)), top=Math.max(0,Math.floor(bounds[1].value));
        var right=Math.min(docW,Math.ceil(bounds[2].value)), bottom=Math.min(docH,Math.ceil(bounds[3].value));
        var w=right-left, h=bottom-top;
        if(w<=0||h<=0) return 'ERR|Layer has no renderable pixels';

        var origW=w, origH=h;

        // Use layer.duplicate() instead of selection.copy()+paste()
        // Works for ALL layer types: normal, text, shape, smart object
        var tempDoc=app.documents.add(w,h,origDoc.resolution,'_ps_export',NewDocumentMode.RGB,DocumentFill.TRANSPARENT);
        try{{
            // Switch to source doc for reliable cross-document duplicate
            app.activeDocument = origDoc;
            var isBg = false;
            try {{ isBg = targetLayer.isBackgroundLayer; }} catch(e) {{}}
            if (isBg) targetLayer.isBackgroundLayer = false;
            origDoc.activeLayer = targetLayer;
            targetLayer.duplicate(tempDoc, ElementPlacement.PLACEATBEGINNING);
            app.activeDocument = tempDoc;
            // Move duplicated layer to top-left corner of temp doc
            var dupLayer = tempDoc.activeLayer;
            var db = dupLayer.bounds;
            dupLayer.translate(-db[0].value, -db[1].value);

            if({scale}>0 && {scale}<1.0){{ var sPct={scale}*100; tempDoc.resizeImage(new UnitValue(sPct,'%'),new UnitValue(sPct,'%'),undefined,ResampleMethod.BICUBICSHARPER); }}
            if({(trim ? "true" : "false")}){{ tempDoc.trim(TrimType.TRANSPARENT,true,true,true,true); }}
            var f=new File('{escapedPath}'); if(f.exists) f.remove();
            var opts=new PNGSaveOptions(); opts.compression=9;
            tempDoc.saveAs(f,opts,true);
            var rw=tempDoc.width.value, rh=tempDoc.height.value;
            tempDoc.close(SaveOptions.DONOTSAVECHANGES);
            return 'OK|'+rw+'|'+rh+'|'+origW+'|'+origH;
        }}catch(e){{ tempDoc.close(SaveOptions.DONOTSAVECHANGES); throw e; }}
    }}catch(e){{ return 'ERR|'+e.toString(); }}
    finally {{
        try{{ if(origRulerUnits!==undefined) app.preferences.rulerUnits=origRulerUnits; app.displayDialogs=origDialogs;
            if(origDoc) app.activeDocument=origDoc; }}catch(e){{}}
    }}
}})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script, timeoutMs: 60_000);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
                return new { success = false, error = "Export failed — no output file generated" };

            var parts = raw.Split('|');
            int expW = 0, expH = 0, origW = 0, origH = 0;
            if (parts.Length >= 5)
            {
                int.TryParse(parts[1], out expW);
                int.TryParse(parts[2], out expH);
                int.TryParse(parts[3], out origW);
                int.TryParse(parts[4], out origH);
            }

            return new
            {
                success = true,
                output_path = fullPath,
                width = expW,
                height = expH,
                original_width = origW,
                original_height = origH,
                layer_name = layer_name,
            };
        }
        catch (TimeoutException)
        {
            return new { success = false, error = "Operation timed out." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    // ==================================================================
    // get_layers (field-filtered layer listing)
    // ==================================================================

    [McpServerTool(Name = "photoshop_get_layers")]
    [Description("Get all layers with optional field filtering. Use fields to limit output (comma-separated). Without fields, returns all properties.")]
    public async Task<object> GetLayers(
        [Description("Comma-separated field names to include. Empty = all fields.")] string fields = "")
    {
        var script = JsHelpers.JsonPolyfill + @"
(function() {
    var doc = app.activeDocument;
    if (!doc) return 'ERR|No active document';

    function collectLayer(layer, idx, parentId) {
        var info = {
            index: idx,
            id: layer.id,
            name: layer.name,
            visible: layer.visible,
            type: 'layer',
        };
        if (parentId !== undefined && parentId !== null)
            info.parentId = parentId;
        else
            info.parentId = null;
        try { info.kind = layer.kind.toString(); } catch(e) { info.kind = 'Unknown'; }
        try {
            var b = layer.bounds;
            info.bounds = { left: b[0].value, top: b[1].value, right: b[2].value, bottom: b[3].value };
            info.width = b[2].value - b[0].value;
            info.height = b[3].value - b[1].value;
        } catch(e) { info.bounds = null; info.width = 0; info.height = 0; }
        try { info.opacity = layer.opacity; } catch(e) { info.opacity = 100; }
        try { info.blendMode = layer.blendMode.toString(); } catch(e) { info.blendMode = ''; }
        try {
            // Use textItem probe to detect text layers (kind is unreliable across PS versions)
            var isText = false;
            try { isText = layer.textItem != null; } catch(e) {}
            if (isText) {
                var ti = layer.textItem;
                info.text = ti.contents;

                // Prefer Action Manager for reliable text properties
                // (DOM ti.size returns UnitValue object that fails JSON serialization;
                //  ti.color throws on inherited/mixed-format text)
                try {
                    var ref = new ActionReference();
                    ref.putIdentifier(stringIDToTypeID('layer'), layer.id);
                    var desc = executeActionGet(ref);
                    if (desc.hasKey(stringIDToTypeID('textKey'))) {
                        var td = desc.getObjectValue(stringIDToTypeID('textKey'));
                        if (td.hasKey(stringIDToTypeID('textStyleRange'))) {
                            var sl = td.getList(stringIDToTypeID('textStyleRange'));
                            if (sl.count > 0) {
                                var sr = sl.getObjectValue(0);
                                if (sr.hasKey(stringIDToTypeID('textStyle'))) {
                                    var ts = sr.getObjectValue(stringIDToTypeID('textStyle'));
                                    // Walk style chain for ALL properties (font, size, color)
                                    var sc = ts;
                                    while (sc) {
                                        if (info.font_name === undefined && sc.hasKey(stringIDToTypeID('font')))
                                            info.font_name = sc.getString(stringIDToTypeID('font'));
                                        if (info.font_size === undefined && sc.hasKey(stringIDToTypeID('impliedFontSize'))) {
                                            // impliedFontSize = size × transform scale — matches Character panel
                                            try { info.font_size = sc.getDouble(stringIDToTypeID('impliedFontSize')); }
                                            catch(e) { info.font_size = sc.getUnitDoubleValue(stringIDToTypeID('impliedFontSize')).value; }
                                        }
                                        if (info.font_size === undefined && sc.hasKey(stringIDToTypeID('size'))) {
                                            try {
                                                var ud = sc.getUnitDoubleValue(stringIDToTypeID('size'));
                                                info.font_size = ud.value;
                                            } catch(e) {
                                                info.font_size = sc.getDouble(stringIDToTypeID('size'));
                                            }
                                        }
                                        if (info.text_color === undefined && sc.hasKey(stringIDToTypeID('color'))) {
                                            var cd = sc.getObjectValue(stringIDToTypeID('color'));
                                            if (cd && cd.hasKey(stringIDToTypeID('red'))) {
                                                info.text_color = {
                                                    red: Math.round(cd.getDouble(stringIDToTypeID('red'))),
                                                    green: Math.round(cd.getDouble(stringIDToTypeID('green'))),
                                                    blue: Math.round(cd.getDouble(stringIDToTypeID('blue')))
                                                };
                                            }
                                        }
                                        if (info.font_name !== undefined &&
                                            info.font_size !== undefined &&
                                            info.text_color !== undefined)
                                            break;
                                        if (sc.hasKey(stringIDToTypeID('baseParentStyle')))
                                            sc = sc.getObjectValue(stringIDToTypeID('baseParentStyle'));
                                        else
                                            break;
                                    }
                                }
                            }
                        }
                    }
                } catch(e) {}

                // Detect layer transform from textKey.transform (always present for
                // Free-Transformed text layers — nested inside textKey, not at layer level)
                try {
                    if (desc && desc.hasKey(stringIDToTypeID('textKey'))) {
                        var td = desc.getObjectValue(stringIDToTypeID('textKey'));
                        if (td.hasKey(stringIDToTypeID('transform'))) {
                            var tf = td.getObjectValue(stringIDToTypeID('transform'));
                            var xx = tf.getDouble(stringIDToTypeID('xx'));
                            var xy = tf.getDouble(stringIDToTypeID('xy'));
                            var yx = tf.getDouble(stringIDToTypeID('yx'));
                            var yy = tf.getDouble(stringIDToTypeID('yy'));
                            var scaleX = Math.sqrt(xx * xx + xy * xy);
                            var scaleY = Math.sqrt(yx * yx + yy * yy);
                            if (Math.abs(scaleX - 1.0) > 0.001 || Math.abs(scaleY - 1.0) > 0.001) {
                                info.transform_scale_x = scaleX;
                                info.transform_scale_y = scaleY;
                                if (info.font_size !== undefined) {
                                    // font_size is now impliedFontSize (already scaled)
                                    // Store the raw pre-transform size for reference
                                    info.font_size_raw = info.font_size / scaleY;
                                }
                            }
                        }
                    }
                } catch(e) {}

                // DOM fallback for font_name / text_color
                if (info.font_name === undefined) {
                    try { info.font_name = ti.font; } catch(e) {}
                }
                // DOM fallback for font_size (AM impliedFontSize is primary)
                if (info.font_size === undefined) {
                    try { info.font_size = parseFloat(String(ti.size)); } catch(e) {}
                }
                if (info.text_color === undefined) {
                    try {
                        var c = ti.color;
                        if (c && c.rgb) {
                            info.text_color = {
                                red: Math.round(c.rgb.red),
                                green: Math.round(c.rgb.green),
                                blue: Math.round(c.rgb.blue)
                            };
                        }
                    } catch(e) {}
                }

                try { info.alignment = ti.justification.toString(); } catch(e) {}
            }
        } catch(e) {}
        try { info.allLocked = layer.allLocked; } catch(e) { info.allLocked = false; }
        try { info.locked = layer.locked; } catch(e) { info.locked = false; }
        return info;
    }

    function collectAll(container, startIdx, parentId) {
        var result = [];
        var idx = startIdx || 0;
        for (var i = 0; i < container.artLayers.length; i++) {
            result.push(collectLayer(container.artLayers[i], idx, parentId));
            idx++;
        }
        for (var j = 0; j < container.layerSets.length; j++) {
            var ls = container.layerSets[j];
            var group = {
                index: idx,
                id: ls.id,
                name: ls.name,
                visible: ls.visible,
                type: 'group',
                kind: 'LayerSet',
            };
            if (parentId !== undefined && parentId !== null)
                group.parentId = parentId;
            else
                group.parentId = null;
            try { group.opacity = ls.opacity; } catch(e) { group.opacity = 100; }
            try { group.blendMode = ls.blendMode.toString(); } catch(e) { group.blendMode = ''; }
            try {
                var b = ls.bounds;
                group.bounds = { left: b[0].value, top: b[1].value, right: b[2].value, bottom: b[3].value };
                group.width = b[2].value - b[0].value;
                group.height = b[3].value - b[1].value;
            } catch(e) { group.bounds = null; group.width = 0; group.height = 0; }
            idx++;
            var children = collectAll(ls, idx, ls.id);
            group.children = children.layers;
            group.childrenCount = children.layers.length;
            result.push(group);
            idx = children.nextIdx;
        }
        return { layers: result, nextIdx: idx };
    }

    var collected = collectAll(doc, 0, null);
    return 'OK|' + _json({ layers: collected.layers, total_count: collected.layers.length });
})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            if (raw.StartsWith("OK|"))
            {
                var json = raw[3..];

                if (!string.IsNullOrWhiteSpace(fields))
                {
                    json = JsHelpers.FilterLayerFields(json, fields);
                }

                // Parse JSON and return as object so framework serializes it properly
                var obj = JsonSerializer.Deserialize<object>(json);
                return new { success = true, layers = obj };
            }

            return new { success = false, error = $"Unexpected result: {raw}" };
        }
        catch (TimeoutException)
        {
            return new { success = false, error = "Operation timed out." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    // ==================================================================
    // debug_layer — dump all AM descriptor keys for diagnosis
    // ==================================================================

    [McpServerTool(Name = "photoshop_debug_layer")]
    [Description("Dump ALL Action Manager descriptor keys for a layer. Use this to discover where transform/font/color data lives.")]
    public async Task<object> DebugLayer(
        [Description("Layer name (fuzzy match)")] string layer_name = "",
        [Description("Layer index (0-based, takes priority)")] int layer_index = -1)
    {
        if (string.IsNullOrEmpty(layer_name) && layer_index < 0)
            return new { success = false, error = "Must specify layer_name or layer_index" };

        var searchField = layer_index >= 0 ? "index" : "name";
        var searchValue = layer_index >= 0 ? layer_index.ToString() : JsonSerializer.Serialize(layer_name);
        var searchValueJson = JsonSerializer.Serialize(layer_name);

        var script = JsHelpers.JsonPolyfill + @"
(function() {
    var doc = app.activeDocument;
    if (!doc) return 'ERR|No active document';

    // Find layer
    function flatten(c, r) { for (var i=0;i<c.artLayers.length;i++) r.push(c.artLayers[i]); for (var j=0;j<c.layerSets.length;j++){r.push(c.layerSets[j]);flatten(c.layerSets[j],r);} }
    var all = []; flatten(doc, all);
    var layer = null;
    if ('" + searchField + @"' === 'index') {
        if (" + searchValue + @" >= 0 && " + searchValue + @" < all.length) layer = all[" + searchValue + @"];
    } else {
        var q = " + searchValueJson + @", ql = q.toLowerCase();
        for (var i=0; i<all.length; i++) if (all[i].name.toLowerCase().indexOf(ql) !== -1) { layer = all[i]; break; }
    }
    if (!layer) return 'ERR|Layer not found';

    // Helper: convert TypeID to string
    function tidStr(tid) {
        try { return typeIDToStringID(tid); } catch(e) { return '0x' + tid.toString(16); }
    }

    // Helper: dump a descriptor recursively
    function dumpDesc(d, depth) {
        if (!d || depth > 4) return depth > 4 ? '_maxDepth_' : null;
        var r = {};
        try {
            var c = d.count;
            for (var i = 0; i < c; i++) {
                var key = d.getKey(i);
                var keyStr = tidStr(key);
                try {
                    var vt = d.getType(key);
                    if (vt === DescValueType.BOOLEANTYPE) { r[keyStr] = d.getBoolean(key); }
                    else if (vt === DescValueType.INTEGERTYPE) { r[keyStr] = d.getInteger(key); }
                    else if (vt === DescValueType.DOUBLETYPE) { r[keyStr] = d.getDouble(key); }
                    else if (vt === DescValueType.STRINGTYPE) { r[keyStr] = d.getString(key); }
                    else if (vt === DescValueType.UNITDOUBLE) {
                        try { var ud = d.getUnitDoubleValue(key); r[keyStr] = { _unit: ud.value, _type: tidStr(ud.type) }; }
                        catch(e) { r[keyStr] = d.getDouble(key); }
                    }
                    else if (vt === DescValueType.OBJECTTYPE) {
                        try { r[keyStr] = dumpDesc(d.getObjectValue(key), depth+1); }
                        catch(e) { r[keyStr] = { _error: e.toString() }; }
                    }
                    else if (vt === DescValueType.LISTTYPE) {
                        try {
                            var list = d.getList(key);
                            var arr = [];
                            for (var j = 0; j < list.count; j++) {
                                try { arr.push(dumpDesc(list.getObjectValue(j), depth+1)); }
                                catch(e) { arr.push({ _error: e.toString() }); }
                            }
                            r[keyStr] = arr;
                        } catch(e2) { r[keyStr] = { _error: e2.toString() }; }
                    }
                    else if (vt === DescValueType.ENUMERATEDTYPE) {
                        try { r[keyStr] = { _enum: tidStr(d.getEnumerationType(key)) + ':' + tidStr(d.getEnumerationValue(key)) }; }
                        catch(e) { r[keyStr] = { _enumError: e.toString() }; }
                    }
                    else if (vt === DescValueType.REFERENCETYPE) {
                        try { var rr = d.getReference(key); r[keyStr] = { _ref: dumpDesc(rr.getDesiredClass ? null : rr, 0) }; }
                        catch(e) { r[keyStr] = { _ref: 'unreadable' }; }
                    }
                    else { r[keyStr] = { _unknownType: vt }; }
                } catch(e2) { r[keyStr] = { _error: e2.toString() }; }
            }
        } catch(e) { r._descError = e.toString(); }
        return r;
    }

    // ---- 1) Dump full layer descriptor ----
    var result = { layer_name: layer.name, layer_id: layer.id, layer_kind: '' };
    try { result.layer_kind = layer.kind.toString(); } catch(e) {}

    try {
        var ref1 = new ActionReference();
        ref1.putIdentifier(stringIDToTypeID('layer'), layer.id);
        result.fullDescriptor = dumpDesc(executeActionGet(ref1), 0);
    } catch(e) { result.fullDescriptor_error = e.toString(); }

    // ---- 2) Try transform via putProperty stringID ----
    try {
        var r2 = new ActionReference();
        r2.putProperty(stringIDToTypeID('property'), stringIDToTypeID('transform'));
        r2.putIdentifier(stringIDToTypeID('layer'), layer.id);
        result.transform_stringID = dumpDesc(executeActionGet(r2), 0);
    } catch(e) { result.transform_stringID_error = e.toString(); }

    // ---- 3) Try transform via putProperty charID ----
    try {
        var r3 = new ActionReference();
        r3.putProperty(charIDToTypeID('Prpr'), charIDToTypeID('Trnf'));
        r3.putIdentifier(charIDToTypeID('Lyr '), layer.id);
        result.transform_charID = dumpDesc(executeActionGet(r3), 0);
    } catch(e) { result.transform_charID_error = e.toString(); }

    // ---- 4) Try transform via activate + target ----
    try {
        var prevActive = doc.activeLayer;
        doc.activeLayer = layer;
        var r4 = new ActionReference();
        r4.putEnumerated(charIDToTypeID('Lyr '), charIDToTypeID('Ordn'), charIDToTypeID('Trgt'));
        result.transform_targetDescriptor = dumpDesc(executeActionGet(r4), 0);
        try { doc.activeLayer = prevActive; } catch(e) {}
    } catch(e) { result.transform_target_error = e.toString(); }

    // ---- 5) DOM textItem properties ----
    try {
        var ti = layer.textItem;
        if (ti) {
            result.dom_textItem = {};
            try { result.dom_textItem.contents = ti.contents; } catch(e) {}
            try { var sz = ti.size; result.dom_textItem.size_value = sz.value; result.dom_textItem.size_type = tidStr(sz.type); result.dom_textItem.size_string = String(sz); } catch(e) { result.dom_textItem.size_error = e.toString(); }
            try { result.dom_textItem.font = ti.font; } catch(e) {}
            try { var c = ti.color; result.dom_textItem.color = { red: c.rgb.red, green: c.rgb.green, blue: c.rgb.blue }; } catch(e) {}
        }
    } catch(e) { result.dom_textItem_error = e.toString(); }

    return 'OK|' + _json(result);
})();
";

        try
        {
            var raw = await _ps.ExecuteJavaScriptAsync(script, timeoutMs: 30_000);
            if (raw.StartsWith("ERR|"))
                return new { success = false, error = raw[4..] };

            if (raw.StartsWith("OK|"))
            {
                var json = raw[3..];
                var obj = JsonSerializer.Deserialize<object>(json);
                return new { success = true, debug = obj };
            }

            return new { success = false, error = $"Unexpected result: {raw}" };
        }
        catch (TimeoutException)
        {
            return new { success = false, error = "Operation timed out." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, detailed_error = ex.ToString() };
        }
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private static string EscapeJs(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}

/// <summary>
/// Maps blend mode strings to Photoshop BlendMode enum names.
/// </summary>
internal static class BlendModeMap
{
    private static readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["normal"] = "NORMAL",
        ["dissolve"] = "DISSOLVE",
        ["darken"] = "DARKEN",
        ["multiply"] = "MULTIPLY",
        ["color_burn"] = "COLORBURN",
        ["linear_burn"] = "LINEARBURN",
        ["darker_color"] = "DARKERCOLOR",
        ["lighten"] = "LIGHTEN",
        ["screen"] = "SCREEN",
        ["color_dodge"] = "COLORDODGE",
        ["linear_dodge"] = "LINEARDODGE",
        ["lighter_color"] = "LIGHTERCOLOR",
        ["overlay"] = "OVERLAY",
        ["soft_light"] = "SOFTLIGHT",
        ["hard_light"] = "HARDLIGHT",
        ["vivid_light"] = "VIVIDLIGHT",
        ["linear_light"] = "LINEARLIGHT",
        ["pin_light"] = "PINLIGHT",
        ["hard_mix"] = "HARDMIX",
        ["difference"] = "DIFFERENCE",
        ["exclusion"] = "EXCLUSION",
        ["subtract"] = "SUBTRACT",
        ["divide"] = "DIVIDE",
        ["hue"] = "HUE",
        ["saturation"] = "SATURATION",
        ["color"] = "COLOR",
        ["luminosity"] = "LUMINOSITY",
    };

    public static string? GetEnumName(string blendMode)
    {
        var normalized = blendMode.Trim().Replace(" ", "_");
        return _map.GetValueOrDefault(normalized);
    }
}
