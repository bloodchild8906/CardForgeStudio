using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    public sealed class CardForgeZoneEditor : VisualElement
    {
        readonly LayoutDocument document;
        readonly ZoneDefinition zone;
        readonly SchemaDocument schema;
        readonly IReadOnlyDictionary<string, IconDocument> icons;
        readonly Action save;
        readonly Action refresh;
        VisualElement body;

        public CardForgeZoneEditor(LayoutDocument layout, ZoneDefinition zone, SchemaDocument schema,
            IReadOnlyDictionary<string, IconDocument> icons, Action save, Action refresh)
        {
            document = layout; this.zone = zone; this.schema = schema; this.icons = icons; this.save = save; this.refresh = refresh;
            AddToClassList("zone-editor");
            var disclosure = new Foldout { text = "ZONE EDITOR", value = true }; Add(disclosure);
            body = disclosure;
            Build();
        }

        void Build()
        {
            if (document.templates == null) document.templates = new List<ZoneTemplateDefinition>();
            if (zone.layers == null) zone.layers = new List<ZoneLayer>();
            if (zone.variants == null) zone.variants = new List<ZoneVariant>();
            Text(body, "Zone name", zone.name, v => { zone.name = v; Save(); });
            var type = new EnumField("Content type", zone.kind);
            type.RegisterValueChangedCallback(e => { zone.kind = (ZoneKind)e.newValue; Save(); Rebuild(); }); body.Add(type);
            var bindings = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "Unbound / static") };
            bindings.AddRange(schema.fields.Select(f => new KeyValuePair<string, string>(f.id, f.label + " · " + f.kind)));
            Choice(body, "Data binding", bindings, zone.fieldId, v => { zone.fieldId = v; Save(); refresh?.Invoke(); });
            if (zone.kind == ZoneKind.Icon && icons != null)
                Choice(body, "Apply icon", icons.Values.Select(i => new KeyValuePair<string, string>(i.assetPath, i.name)), zone.staticAssetPath, v => { zone.staticAssetPath = v; Save(); refresh?.Invoke(); });
            if (zone.kind == ZoneKind.Icon || zone.kind == ZoneKind.Image)
                Texture(body, "Static image", zone.staticAssetPath, v => { zone.staticAssetPath = v; Save(); refresh?.Invoke(); });
            else if (string.IsNullOrEmpty(zone.fieldId)) Text(body, "Static text", zone.staticText, v => { zone.staticText = v; Save(); refresh?.Invoke(); }, true);
            if (document.templates.Count > 0)
                Choice(body, "Template", new[] { new KeyValuePair<string, string>("", "No template") }.Concat(document.templates.Select(t => new KeyValuePair<string, string>(t.id, t.name))), zone.templateId, v => { zone.templateId = v; Save(); refresh?.Invoke(); });
            Color(body, "Zone background", zone.backgroundColor, v => { zone.backgroundColor = v; Save(); refresh?.Invoke(); });
            Toggle(body, "Visible", zone.visible, v => { zone.visible = v; Save(); refresh?.Invoke(); });
            Toggle(body, "Locked", zone.locked, v => { zone.locked = v; Save(); });
            BuildLayers();
            BuildVariants();
        }

        void BuildLayers()
        {
            var group = new Foldout { text = "Layers", value = false }; body.Add(group);
            group.Add(new Label("Ordered container surfaces. Each layer can be bound, templated, or sliced independently.") { name = "zone-editor-help" });
            group.Add(Button("+ Add layer", () => { zone.layers.Add(new ZoneLayer { name = "Layer " + (zone.layers.Count + 1), order = zone.layers.Count }); Save(); Rebuild(); }, "small-button"));
            foreach (var layer in zone.layers.OrderBy(x => x.order).ToList())
            {
                var local = layer; var editor = new Foldout { text = local.name, value = false }; group.Add(editor);
                Text(editor, "Name", local.name, v => { local.name = v; Save(); Rebuild(); });
                Int(editor, "Order", local.order, v => { local.order = v; Save(); refresh?.Invoke(); });
                Toggle(editor, "Visible", local.visible, v => { local.visible = v; Save(); refresh?.Invoke(); });
                Color(editor, "Background", local.backgroundColor, v => { local.backgroundColor = v; Save(); refresh?.Invoke(); });
                Texture(editor, "Background image", local.assetPath, v => { local.assetPath = v; Save(); refresh?.Invoke(); });
                Choice(editor, "Image mode", Enum.GetNames(typeof(ZoneImageScaleMode)).Select(x => new KeyValuePair<string, string>(x, x)), local.imageScaleMode.ToString(), v => { Enum.TryParse(v, out local.imageScaleMode); Save(); Rebuild(); });
                if (local.imageScaleMode == ZoneImageScaleMode.Slice)
                {
                    Int(editor, "Slice left", local.sliceLeft, v => { local.sliceLeft = Math.Max(0, v); Save(); refresh?.Invoke(); });
                    Int(editor, "Slice right", local.sliceRight, v => { local.sliceRight = Math.Max(0, v); Save(); refresh?.Invoke(); });
                    Int(editor, "Slice top", local.sliceTop, v => { local.sliceTop = Math.Max(0, v); Save(); refresh?.Invoke(); });
                    Int(editor, "Slice bottom", local.sliceBottom, v => { local.sliceBottom = Math.Max(0, v); Save(); refresh?.Invoke(); });
                }
                Text(editor, "Binding path", local.bindingPath, v => { local.bindingPath = v; Save(); });
                if (document.templates.Count > 0)
                    Choice(editor, "Template", new[] { new KeyValuePair<string, string>("", "No template") }.Concat(document.templates.Select(t => new KeyValuePair<string, string>(t.id, t.name))), local.templateId, v => { local.templateId = v; Save(); refresh?.Invoke(); });
                Text(editor, "Required card type", local.requiredCardType, v => { local.requiredCardType = v; Save(); refresh?.Invoke(); });
                if (local.requirements == null) local.requirements = new List<RequirementDefinition>();
                Requirements(editor, local.requirements);
                editor.Add(Button("Remove layer", () => { zone.layers.Remove(local); Save(); Rebuild(); }, "small-danger"));
            }
        }

        void BuildVariants()
        {
            var group = new Foldout { text = "Variants", value = false }; body.Add(group);
            group.Add(Button("+ Add variant", () => { zone.variants.Add(new ZoneVariant { name = "Variant " + (zone.variants.Count + 1), x = zone.x, y = zone.y, width = zone.width, height = zone.height, layerId = zone.layers.FirstOrDefault()?.id ?? "" }); Save(); Rebuild(); }, "small-button"));
            foreach (var variant in zone.variants.ToList())
            {
                var local = variant; var editor = new Foldout { text = local.name, value = false }; group.Add(editor);
                Text(editor, "Name", local.name, v => { local.name = v; Save(); Rebuild(); });
                Choice(editor, "Layer", zone.layers.Select(x => new KeyValuePair<string, string>(x.id, x.name)), local.layerId, v => { local.layerId = v; Save(); refresh?.Invoke(); });
                Text(editor, "Static text", local.staticText, v => { local.staticText = v; Save(); refresh?.Invoke(); }, true);
                Texture(editor, "Artwork", local.staticAssetPath, v => { local.staticAssetPath = v; Save(); refresh?.Invoke(); });
                Float(editor, "X", local.x, v => { local.x = v; Save(); refresh?.Invoke(); });
                Float(editor, "Y", local.y, v => { local.y = v; Save(); refresh?.Invoke(); });
                Float(editor, "Width", local.width, v => { local.width = Mathf.Max(10, v); Save(); refresh?.Invoke(); });
                Float(editor, "Height", local.height, v => { local.height = Mathf.Max(10, v); Save(); refresh?.Invoke(); });
                Float(editor, "Font size", local.fontSize, v => { local.fontSize = Mathf.Max(1, v); Save(); refresh?.Invoke(); });
                Color(editor, "Text color", local.color, v => { local.color = v; Save(); refresh?.Invoke(); });
                Toggle(editor, "Visible", local.visible, v => { local.visible = v; Save(); refresh?.Invoke(); });
                if (local.requirements == null) local.requirements = new List<RequirementDefinition>();
                Requirements(editor, local.requirements);
                editor.Add(Button("Remove variant", () => { zone.variants.Remove(local); Save(); Rebuild(); }, "small-danger"));
            }
        }

        void Rebuild() { Clear(); AddToClassList("zone-editor"); var disclosure = new Foldout { text = "ZONE EDITOR", value = true }; Add(disclosure); body = disclosure; Build(); }
        void Save() { save?.Invoke(); }
        void Requirements(VisualElement parent, List<RequirementDefinition> requirements)
        {
            if (requirements == null) requirements = new List<RequirementDefinition>();
            var group = new Foldout { text = "Required when", value = false }; parent.Add(group);
            foreach (var requirement in requirements.ToList())
            {
                var local = requirement; var row = new VisualElement(); row.AddToClassList("requirement-row"); group.Add(row);
                Choice(row, "Field", schema.fields.Select(x => new KeyValuePair<string, string>(x.id, x.label)), local.fieldId, v => { local.fieldId = v; Save(); refresh?.Invoke(); });
                Choice(row, "Comparison", new[] { "Equals", "Not equals", "Contains", "Greater than", "Greater than or equal", "Less than", "Less than or equal" }.Select(x => new KeyValuePair<string, string>(x, x)), local.comparison, v => { local.comparison = v; Save(); refresh?.Invoke(); });
                Text(row, "Expected", local.expectedValue, v => { local.expectedValue = v; Save(); refresh?.Invoke(); });
                row.Add(Button("Remove", () => { requirements.Remove(local); Save(); Rebuild(); }, "small-danger"));
            }
            group.Add(Button("+ Add requirement", () => { requirements.Add(new RequirementDefinition()); Save(); Rebuild(); }, "small-button"));
        }
        Button Button(string text, Action action, string style) { var b = new Button(action) { text = text }; b.AddToClassList(style); return b; }
        static void Text(VisualElement p, string label, string value, Action<string> action, bool multiline = false) { var f = new TextField(label) { value = value ?? "", isDelayed = true, multiline = multiline }; if (multiline) f.style.minHeight = 64; f.RegisterValueChangedCallback(e => action(e.newValue)); p.Add(f); }
        static void Int(VisualElement p, string label, int value, Action<int> action) { var f = new IntegerField(label) { value = value, isDelayed = true }; f.RegisterValueChangedCallback(e => action(e.newValue)); p.Add(f); }
        static void Float(VisualElement p, string label, float value, Action<float> action) { var f = new FloatField(label) { value = value, isDelayed = true }; f.RegisterValueChangedCallback(e => action(e.newValue)); p.Add(f); }
        static void Toggle(VisualElement p, string label, bool value, Action<bool> action) { var f = new Toggle(label) { value = value }; f.RegisterValueChangedCallback(e => action(e.newValue)); p.Add(f); }
        static void Color(VisualElement p, string label, string value, Action<string> action) { var f = new ColorField(label) { value = CardForgeCanvas.Parse(value, UnityEngine.Color.white), showAlpha = true }; f.RegisterValueChangedCallback(e => action("#" + ColorUtility.ToHtmlStringRGBA(e.newValue))); p.Add(f); }
        static void Texture(VisualElement p, string label, string value, Action<string> action) { var f = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false }; f.value = string.IsNullOrEmpty(value) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(value); f.RegisterValueChangedCallback(e => action(e.newValue == null ? "" : AssetDatabase.GetAssetPath(e.newValue))); p.Add(f); }
        static void Choice(VisualElement p, string label, IEnumerable<KeyValuePair<string, string>> values, string selected, Action<string> action) { var list = values.ToList(); if (list.Count == 0) return; var labels = list.Select(x => x.Value).ToList(); var f = new DropdownField(label, labels, Mathf.Max(0, list.FindIndex(x => x.Key == selected))); f.RegisterValueChangedCallback(e => { var i = labels.IndexOf(e.newValue); if (i >= 0) action(list[i].Key); }); p.Add(f); }
    }
}
