using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public sealed partial class CardForgeDesignerView
    {
        string _zoneVariantId, _zoneLayerId;

        void RenderRequirements(VisualElement parent, string title, List<RequirementDefinition> requirements)
        {
            if (requirements == null) return;
            var group = Group(title.ToUpperInvariant()); parent.Add(group);
            group.Add(Note("Requirements are saved with this definition and can be evaluated by the runtime."));
            foreach (var requirement in requirements.ToList())
            {
                var local = requirement;
                var row = Col("requirement-row"); group.Add(row);
                Choice(row, "Field", Options(_store.Schema.fields, x => x.id, x => x.label), local.fieldId,
                    value => { local.fieldId = value; SaveRequirementOwner(); });
                Choice(row, "Comparison", new[] { "Equals", "Not equals", "Contains", "Greater than", "Less than" }
                    .Select(x => new KeyValuePair<string, string>(x, x)), local.comparison,
                    value => { local.comparison = value; SaveRequirementOwner(); });
                TextBox(row, "Expected value", local.expectedValue, value => { local.expectedValue = value; SaveRequirementOwner(); });
                TextBox(row, "Failure message", local.message, value => { local.message = value; SaveRequirementOwner(); });
                row.Add(Btn("Remove requirement", () => { requirements.Remove(local); SaveRequirementOwner(); Render(); }, "small-danger"));
            }
            group.Add(Btn("+ Add requirement", () => { requirements.Add(new RequirementDefinition()); SaveRequirementOwner(); Render(); }, "small-button"));
        }

        void SaveRequirementOwner()
        {
            if (_tab == Workspace.Layouts) SaveLayout();
            else if (_tab == Workspace.Database) SaveCard();
        }

        void RenderZoneStructure(VisualElement parent, ZoneDefinition zone)
        {
            var group = Group("LAYERS & VARIANTS"); parent.Add(group);
            if (zone.layers == null) zone.layers = new List<ZoneLayer>();
            if (zone.variants == null) zone.variants = new List<ZoneVariant>();
            var layerRow = Row("button-row"); group.Add(layerRow);
            layerRow.Add(Btn("+ Layer", () =>
            {
                var layer = new ZoneLayer { name = "Layer " + (zone.layers.Count + 1), order = zone.layers.Count };
                zone.layers.Add(layer); _zoneLayerId = layer.id; SaveLayout(); Render();
            }, "small-button"));
            layerRow.Add(Btn("+ Variant", () =>
            {
                var variant = new ZoneVariant { name = "Variant " + (zone.variants.Count + 1), layerId = zone.layers.FirstOrDefault()?.id ?? "", x = zone.x, y = zone.y, width = zone.width, height = zone.height, fontSize = zone.fontSize, color = zone.color, staticText = zone.staticText, staticAssetPath = zone.staticAssetPath };
                zone.variants.Add(variant); _zoneVariantId = variant.id; SaveLayout(); Render();
            }, "small-button"));
            foreach (var layer in zone.layers.OrderBy(x => x.order))
            {
                var local = layer;
                AddListButton(group, "Layer  ·  " + local.name, local.id == _zoneLayerId, () => { _zoneLayerId = local.id; Render(); }, local.visible ? local.blendMode : "Hidden");
            }
            foreach (var variant in zone.variants)
            {
                var local = variant;
                AddListButton(group, "Variant  ·  " + local.name, local.id == _zoneVariantId, () => { _zoneVariantId = local.id; Render(); }, local.requirements.Count + " requirements");
            }
            var selectedLayer = zone.layers.FirstOrDefault(x => x.id == _zoneLayerId);
            if (selectedLayer != null)
            {
                var editor = Group("SELECTED LAYER"); group.Add(editor);
                TextBox(editor, "Layer name", selectedLayer.name, value => { selectedLayer.name = value; SaveLayout(); RefreshCanvas(); });
                Choice(editor, "Blend mode", new[] { "Normal", "Multiply", "Screen", "Additive" }.Select(x => new KeyValuePair<string,string>(x, x)), selectedLayer.blendMode, value => { selectedLayer.blendMode = value; SaveLayout(); RefreshCanvas(); });
                ColorBox(editor, "Layer background", selectedLayer.backgroundColor, value => { selectedLayer.backgroundColor = value; SaveLayout(); RefreshCanvas(); });
                TextureBox(editor, "Layer artwork", selectedLayer.assetPath, value => { selectedLayer.assetPath = value; SaveLayout(); RefreshCanvas(); });
                Choice(editor, "Image mode", Enum.GetNames(typeof(ZoneImageScaleMode)).Select(x => new KeyValuePair<string,string>(x, x)), selectedLayer.imageScaleMode.ToString(), value => { Enum.TryParse(value, out selectedLayer.imageScaleMode); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice left", selectedLayer.sliceLeft, value => { selectedLayer.sliceLeft = Math.Max(0, value); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice right", selectedLayer.sliceRight, value => { selectedLayer.sliceRight = Math.Max(0, value); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice top", selectedLayer.sliceTop, value => { selectedLayer.sliceTop = Math.Max(0, value); SaveLayout(); RefreshCanvas(); });
                IntBox(editor, "Slice bottom", selectedLayer.sliceBottom, value => { selectedLayer.sliceBottom = Math.Max(0, value); SaveLayout(); RefreshCanvas(); });
                TextBox(editor, "Template", selectedLayer.templateId, value => { selectedLayer.templateId = value; SaveLayout(); RefreshCanvas(); });
                TextBox(editor, "Binding path", selectedLayer.bindingPath, value => { selectedLayer.bindingPath = value; SaveLayout(); RefreshCanvas(); });
                editor.Add(Note("Layers are composited in order. Bind a layer to a field or template to keep the container reusable."));
                FloatBox(editor, "Opacity", selectedLayer.opacity, value => { selectedLayer.opacity = UnityEngine.Mathf.Clamp01(value); SaveLayout(); RefreshCanvas(); });
                ToggleBox(editor, "Visible", selectedLayer.visible, value => { selectedLayer.visible = value; SaveLayout(); RefreshCanvas(); });
                editor.Add(Btn("Remove layer", () => { zone.layers.Remove(selectedLayer); _zoneLayerId = null; SaveLayout(); Render(); }, "small-danger"));
            }
            var selectedVariant = zone.variants.FirstOrDefault(x => x.id == _zoneVariantId);
            if (selectedVariant != null)
            {
                var editor = Group("SELECTED VARIANT"); group.Add(editor);
                TextBox(editor, "Variant name", selectedVariant.name, value => { selectedVariant.name = value; SaveLayout(); RefreshCanvas(); });
                Choice(editor, "Layer", Options(zone.layers, x => x.id, x => x.name), selectedVariant.layerId, value => { selectedVariant.layerId = value; SaveLayout(); RefreshCanvas(); });
                TextBox(editor, "Static text", selectedVariant.staticText, value => { selectedVariant.staticText = value; SaveLayout(); RefreshCanvas(); });
                TextureBox(editor, "Artwork", selectedVariant.staticAssetPath, value => { selectedVariant.staticAssetPath = value; SaveLayout(); RefreshCanvas(); });
                FloatBox(editor, "Font size", selectedVariant.fontSize, value => { selectedVariant.fontSize = UnityEngine.Mathf.Max(1,value); SaveLayout(); RefreshCanvas(); });
                ColorBox(editor, "Text color", selectedVariant.color, value => { selectedVariant.color = value; SaveLayout(); RefreshCanvas(); });
                FloatBox(editor, "X", selectedVariant.x, value => { selectedVariant.x = value; SaveLayout(); RefreshCanvas(); });
                FloatBox(editor, "Y", selectedVariant.y, value => { selectedVariant.y = value; SaveLayout(); RefreshCanvas(); });
                FloatBox(editor, "Width", selectedVariant.width, value => { selectedVariant.width = value; SaveLayout(); RefreshCanvas(); });
                FloatBox(editor, "Height", selectedVariant.height, value => { selectedVariant.height = value; SaveLayout(); RefreshCanvas(); });
                ToggleBox(editor, "Visible", selectedVariant.visible, value => { selectedVariant.visible = value; SaveLayout(); RefreshCanvas(); });
                RenderRequirements(editor, "Variant requirements", selectedVariant.requirements);
                editor.Add(Btn("Remove variant", () => { zone.variants.Remove(selectedVariant); _zoneVariantId = null; SaveLayout(); Render(); }, "small-danger"));
            }
        }
    }
}
