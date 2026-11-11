using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    /// <summary>Single canvas used by the layout designer and by per-card live overrides.</summary>
    public sealed class CardForgeCanvas : VisualElement
    {
        public bool ApplyingTransforms { get; private set; }
        public Action GestureCompleted;
        public readonly HashSet<string> Selection = new HashSet<string>();
        public bool Snap = true, ShowGrid = true, UseVariants = true;
        public bool CompactPreview;
        public float GridSize = 8;
        public void SelectZone(string id) {
            foreach (var v in _board.Query<VisualElement>(className: "card-zone").ToList()) {
                bool selected=Selection.Contains(v.name); v.EnableInClassList("card-zone-selected", selected);
                foreach(var grip in v.Query<VisualElement>(className:"resize-grip").ToList())grip.style.display=selected?DisplayStyle.Flex:DisplayStyle.None;
                var corner=v.Q<VisualElement>(className:"resize-handle");if(corner!=null)corner.style.display=selected?DisplayStyle.Flex:DisplayStyle.None;
            }
        }
        readonly ScrollView _scroller;
        readonly VisualElement _board;
        VisualElement _surface;
        float _surfaceWidth, _surfaceHeight;
        public CardForgeCanvas()
        {
            focusable = true;
            AddToClassList("canvas-stage"); style.flexGrow = 1;
            _scroller = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scroller.style.flexGrow = 1; Add(_scroller);
            _board = new VisualElement(); _board.AddToClassList("canvas-board");
            _scroller.Add(_board);
            _scroller.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => CenterSurface());
        }
        public void Draw(LayoutDocument layout, CardDocument card, SchemaDocument schema,
            IReadOnlyDictionary<string, IconDocument> icons, float zoom, bool canDrag,
            string selectedZoneId, Action<ZoneDefinition> select, Action<ZoneDefinition, float, float, float, float> move)
        {
            Selection.RemoveWhere(id => layout == null || !layout.zones.Any(z=>z.id==id));
            _surface = null;
            _board.Clear();
            if (layout == null)
            {
                _board.Add(new Label("Choose or create a layout to preview.")); return;
            }
            float scale = Mathf.Clamp(zoom, CompactPreview ? .01f : .2f, 2.5f);
            _board.style.width = layout.width * scale + (CompactPreview ? 16 : 100);
            _board.style.height = layout.height * scale + (CompactPreview ? 16 : 110);
            var cardSurface = new VisualElement();
            cardSurface.AddToClassList("card-surface");
            cardSurface.style.position = Position.Absolute;
            cardSurface.style.left = 50; cardSurface.style.top = 38;
            cardSurface.style.width = layout.width * scale;
            cardSurface.style.height = layout.height * scale;
            cardSurface.style.backgroundColor = Parse(layout.backgroundColor, new Color(.1f,.16f,.23f));
            cardSurface.style.overflow = Overflow.Hidden;
            _board.Add(cardSurface);
            _surface = cardSurface; _surfaceWidth = layout.width * scale; _surfaceHeight = layout.height * scale;
            CenterSurface();
            if (!string.IsNullOrEmpty(layout.backgroundAssetPath))
            {
                var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(layout.backgroundAssetPath);
                if (bg != null)
                {
                    var image = new Image { image = bg, scaleMode = ScaleMode.StretchToFill };
                    image.style.position = Position.Absolute;
                    image.style.left = 0; image.style.top = 0;
                    image.style.width = Length.Percent(100); image.style.height = Length.Percent(100);
                    cardSurface.Add(image);
                }
            }
            if (ShowGrid && canDrag) {
                float step = Mathf.Max(4, GridSize) * scale;
                for(float x=0;x<layout.width*scale;x+=step) {
                    var line=new VisualElement { pickingMode=PickingMode.Ignore }; line.style.position=Position.Absolute;
                    line.style.left=x;line.style.top=0;line.style.width=1;line.style.height=Length.Percent(100);
                    line.style.backgroundColor=new Color(1,1,1,.05f);cardSurface.Add(line);
                }
                for(float y=0;y<layout.height*scale;y+=step) {
                    var line=new VisualElement { pickingMode=PickingMode.Ignore };line.style.position=Position.Absolute;
                    line.style.top=y;line.style.left=0;line.style.height=1;line.style.width=Length.Percent(100);
                    line.style.backgroundColor=new Color(1,1,1,.05f);cardSurface.Add(line);
                }
            }
            var zones = new List<ZoneDefinition>(layout.zones);
            zones.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var sourceZone in zones)
            {
                float opacity = 1;
                var zone = UseVariants && !canDrag ? CardForgeRules.Resolve(sourceZone, card, schema, out opacity) : sourceZone;
                var ov = CardForgeData.GetOverride(card, zone.id);
                if (!zone.visible || (ov != null && ov.hidden)) continue;
                float lx = zone.x + zone.offsetX + (ov == null ? 0 : ov.deltaX);
                float ly = zone.y + zone.offsetY + (ov == null ? 0 : ov.deltaY);
                float lw = Mathf.Max(10, zone.width + (ov == null ? 0 : ov.deltaWidth));
                float lh = Mathf.Max(10, zone.height + (ov == null ? 0 : ov.deltaHeight));
                var zoneView = new VisualElement { name = zone.id };
                zoneView.AddToClassList("card-zone");
                if (Selection.Contains(zone.id) || selectedZoneId == zone.id) zoneView.AddToClassList("card-zone-selected");
                zoneView.style.position = Position.Absolute;
                zoneView.style.left = lx * scale; zoneView.style.top = ly * scale;
                zoneView.style.width = lw * scale; zoneView.style.height = lh * scale;
                var template = (layout.templates ?? new List<ZoneTemplateDefinition>()).FirstOrDefault(x => x.id == zone.templateId);
                zoneView.style.backgroundColor = Parse(template == null ? zone.backgroundColor : template.backgroundColor, Color.clear);
                zoneView.style.opacity = opacity;
                cardSurface.Add(zoneView);

                if (template != null && !string.IsNullOrEmpty(template.backgroundAssetPath))
                {
                    var templateTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(template.backgroundAssetPath);
                    if (templateTexture != null)
                    {
                        var templateView = new VisualElement { pickingMode = PickingMode.Ignore };
                        templateView.style.position = Position.Absolute;
                        templateView.style.left = 0; templateView.style.top = 0;
                        templateView.style.width = Length.Percent(100); templateView.style.height = Length.Percent(100);
                        if (template.imageScaleMode == ZoneImageScaleMode.Slice)
                        {
                            templateView.style.backgroundImage = new StyleBackground(templateTexture);
                            templateView.style.unitySliceLeft = template.sliceLeft;
                            templateView.style.unitySliceRight = template.sliceRight;
                            templateView.style.unitySliceTop = template.sliceTop;
                            templateView.style.unitySliceBottom = template.sliceBottom;
                        }
                        else
                        {
                            var templateImage = new Image { image = templateTexture, scaleMode = template.imageScaleMode == ZoneImageScaleMode.Contain ? ScaleMode.ScaleToFit : template.imageScaleMode == ZoneImageScaleMode.Stretch ? ScaleMode.StretchToFill : ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                            templateImage.style.width = Length.Percent(100); templateImage.style.height = Length.Percent(100);
                            templateView.Add(templateImage);
                        }
                        zoneView.Add(templateView);
                    }
                }

                foreach (var layer in (sourceZone.layers ?? new List<ZoneLayer>()).Where(x => CardForgeRules.LayerMatches(x, card, schema)).OrderBy(x => x.order))
                {
                    var layerView = new VisualElement { pickingMode = PickingMode.Ignore };
                    layerView.AddToClassList("zone-layer-view");
                    layerView.style.position = Position.Absolute;
                    layerView.style.left = 0; layerView.style.top = 0;
                    layerView.style.width = Length.Percent(100); layerView.style.height = Length.Percent(100);
                    layerView.style.opacity = Mathf.Clamp01(layer.opacity);
                    layerView.style.backgroundColor = Parse(layer.backgroundColor, Color.clear);
                    if (!string.IsNullOrEmpty(layer.assetPath))
                    {
                        var layerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(layer.assetPath);
                        if (layerTexture != null)
                        {
                            if (layer.imageScaleMode == ZoneImageScaleMode.Slice)
                            {
                                layerView.style.backgroundImage = new StyleBackground(layerTexture);
                                layerView.style.unitySliceLeft = layer.sliceLeft;
                                layerView.style.unitySliceRight = layer.sliceRight;
                                layerView.style.unitySliceTop = layer.sliceTop;
                                layerView.style.unitySliceBottom = layer.sliceBottom;
                            }
                            else
                            {
                                var layerImage = new Image { image = layerTexture, scaleMode = layer.imageScaleMode == ZoneImageScaleMode.Contain ? ScaleMode.ScaleToFit : layer.imageScaleMode == ZoneImageScaleMode.Stretch ? ScaleMode.StretchToFill : ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                                layerImage.style.width = Length.Percent(100); layerImage.style.height = Length.Percent(100);
                                layerView.Add(layerImage);
                            }
                        }
                    }
                    zoneView.Add(layerView);
                }

                string content = zone.staticText;
                string path = zone.staticAssetPath;
                if (!string.IsNullOrEmpty(zone.fieldId))
                {
                    var definition = schema.fields.Find(d => d.id == zone.fieldId);
                    if (definition != null)
                    {
                        var value = CardForgeData.Value(card, definition);
                        if (zone.kind == ZoneKind.Icon || zone.kind == ZoneKind.Image) path = value;
                        else
                        {
                            if (definition.kind == FieldKind.Number &&
                                double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                                value = number.ToString("F" + Mathf.Clamp(definition.decimalPlaces, 0, 8), System.Globalization.CultureInfo.InvariantCulture);
                            content = (definition.prefix ?? "") + value + (definition.suffix ?? "");
                        }
                    }
                }
                if (ov != null && !string.IsNullOrEmpty(ov.textOverride)) content = ov.textOverride;
                if (ov != null && !string.IsNullOrEmpty(ov.assetPathOverride)) path = ov.assetPathOverride;
                if (zone.kind == ZoneKind.Icon || zone.kind == ZoneKind.Image)
                {
                    var texture = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null)
                    {
                        var empty = new Label(zone.kind == ZoneKind.Image ? "ARTWORK" : "ICON");
                        empty.pickingMode = PickingMode.Ignore; empty.AddToClassList("image-placeholder"); zoneView.Add(empty);
                    }
                    else
                    {
                        var graphic = new Image { image = texture, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                        graphic.style.width = Length.Percent(100); graphic.style.height = Length.Percent(100);
                        zoneView.Add(graphic);
                    }
                }
                else
                {
                    var label = new Label(content ?? "") { pickingMode = PickingMode.Ignore };
                    label.style.position = Position.Absolute;
                    label.style.left = 0; label.style.top = 0;
                    label.style.width = Length.Percent(100); label.style.height = Length.Percent(100);
                    label.style.fontSize = (ov != null && ov.fontSize > 0 ? ov.fontSize : zone.fontSize) * scale;
                    if (!string.IsNullOrWhiteSpace(zone.fontAssetPath))
                    {
                        var assignedFont = AssetDatabase.LoadAssetAtPath<Font>(zone.fontAssetPath);
                        if (assignedFont != null) label.style.unityFont = assignedFont;
                    }
                    label.style.color = Parse(ov != null && !string.IsNullOrEmpty(ov.color) ? ov.color : zone.color, Color.white);
                    label.style.unityFontStyleAndWeight = zone.bold ? FontStyle.Bold : FontStyle.Normal;
                    label.style.unityTextAlign = ResolveAlign(zone.alignment);
                    label.style.whiteSpace = WhiteSpace.Normal;
                    zoneView.Add(label);
                }
                if (!canDrag) { zoneView.pickingMode = PickingMode.Ignore; continue; }
                var handle = new VisualElement();
                handle.AddToClassList("resize-handle");
                zoneView.Add(handle);
                handle.style.display = zone.locked || !(Selection.Contains(zone.id) || selectedZoneId == zone.id) ? DisplayStyle.None : DisplayStyle.Flex;
                bool active = false, resizing = false;
                int pointer = -1;
                Vector2 down = Vector2.zero;
                float x0 = lx, y0 = ly, w0 = lw, h0 = lh;
                VisualElement captured = null;
                var groupStart = new Dictionary<ZoneDefinition, Vector4>();
                int resizeX=1, resizeY=1;
                Action<PointerDownEvent, bool, VisualElement> start = (evt, resize, owner) =>
                {
                    if (evt.button != 0) return;
                    Focus();
                    if (!evt.shiftKey && !Selection.Contains(zone.id)) Selection.Clear();
                    if(evt.shiftKey && Selection.Contains(zone.id)) Selection.Remove(zone.id); else Selection.Add(zone.id);
                    select?.Invoke(sourceZone); SelectZone(zone.id);
                    if(zone.locked) { evt.StopPropagation(); return; }
                    resizing = resize; resizeX=1;resizeY=1; down = evt.position;
                    groupStart.Clear();
                    foreach(var item in layout.zones.Where(z=>Selection.Contains(z.id)&&!z.locked)) {
                        var itemOverride=CardForgeData.GetOverride(card,item.id);
                        groupStart[item]=new Vector4(item.x+item.offsetX+(itemOverride?.deltaX??0),item.y+item.offsetY+(itemOverride?.deltaY??0),item.width+(itemOverride?.deltaWidth??0),item.height+(itemOverride?.deltaHeight??0));
                    }
                    x0 = lx; y0 = ly; w0 = lw; h0 = lh;
                    active = true; pointer = evt.pointerId; captured = owner;
                    owner.CapturePointer(pointer); evt.StopPropagation();
                };
                zoneView.RegisterCallback<PointerDownEvent>(evt => start(evt, false, zoneView));
                handle.RegisterCallback<PointerDownEvent>(evt => start(evt, true, handle));
                Action<PointerMoveEvent> dragging = evt =>
                {
                    if (!active || evt.pointerId != pointer) return;
                    Vector2 delta = ((Vector2)evt.position - down) / scale;
                    if(Snap && !evt.altKey) { float grid=Mathf.Max(1,GridSize); delta.x=Mathf.Round(delta.x/grid)*grid;delta.y=Mathf.Round(delta.y/grid)*grid; }
                    if (resizing)
                    {
                        lw = resizeX==0?w0:Mathf.Max(10,w0+delta.x*resizeX);lh=resizeY==0?h0:Mathf.Max(10,h0+delta.y*resizeY);
                        lx=resizeX<0?x0+w0-lw:x0;ly=resizeY<0?y0+h0-lh:y0;
                    }
                    else
                    {
                        lx = Mathf.Clamp(x0 + delta.x, -lw + 10, layout.width - 10);
                        ly = Mathf.Clamp(y0 + delta.y, -lh + 10, layout.height - 10);
                    }
                    zoneView.style.left = lx * scale; zoneView.style.top = ly * scale;
                    zoneView.style.width = lw * scale; zoneView.style.height = lh * scale;
                    if(!resizing) foreach(var item in groupStart) {
                        var view=cardSurface.Q<VisualElement>(item.Key.id);if(view==null)continue;
                        view.style.left=(item.Value.x+lx-x0)*scale;view.style.top=(item.Value.y+ly-y0)*scale;
                    }
                    evt.StopPropagation();
                };
                zoneView.RegisterCallback<PointerMoveEvent>(evt => dragging(evt));
                handle.RegisterCallback<PointerMoveEvent>(evt => dragging(evt));
                Action<PointerUpEvent> release = evt =>
                {
                    if (!active || evt.pointerId != pointer) return;
                    active = false; captured?.ReleasePointer(pointer);
                    ApplyingTransforms=true;
                    try {
                        if(resizing)move?.Invoke(sourceZone,lx,ly,lw,lh);
                        else foreach(var item in groupStart)move?.Invoke(item.Key,item.Value.x+lx-x0,item.Value.y+ly-y0,item.Value.z,item.Value.w);
                    } finally { ApplyingTransforms=false; }
                    GestureCompleted?.Invoke();
                    evt.StopPropagation();
                };
                zoneView.RegisterCallback<PointerUpEvent>(evt => release(evt));
                handle.RegisterCallback<PointerUpEvent>(evt => release(evt));
                if(!zone.locked) foreach(var direction in new[]{new Vector2(-1,-1),new Vector2(0,-1),new Vector2(1,-1),new Vector2(-1,0),new Vector2(1,0),new Vector2(-1,1),new Vector2(0,1)}) {
                    var grip=new VisualElement();grip.AddToClassList("resize-grip");grip.style.display=Selection.Contains(zone.id)||selectedZoneId==zone.id?DisplayStyle.Flex:DisplayStyle.None;grip.style.position=Position.Absolute;grip.style.width=7;grip.style.height=7;
                    grip.style.left=Length.Percent((direction.x+1)*50);grip.style.top=Length.Percent((direction.y+1)*50);grip.style.marginLeft=-3;grip.style.marginTop=-3;zoneView.Add(grip);
                    var d=direction;
                    grip.RegisterCallback<PointerDownEvent>(evt=>{start(evt,true,grip);resizeX=(int)d.x;resizeY=(int)d.y;});
                    grip.RegisterCallback<PointerMoveEvent>(evt=>dragging(evt));grip.RegisterCallback<PointerUpEvent>(evt=>release(evt));
                }
            }
        }
        void CenterSurface()
        {
            if (_surface == null) return;
            float viewportWidth = _scroller.contentViewport.resolvedStyle.width;
            float viewportHeight = _scroller.contentViewport.resolvedStyle.height;
            if (float.IsNaN(viewportWidth)) viewportWidth = 0;
            if (float.IsNaN(viewportHeight)) viewportHeight = 0;
            float width = Mathf.Max(_surfaceWidth + (CompactPreview ? 16 : 100), viewportWidth);
            float height = Mathf.Max(_surfaceHeight + (CompactPreview ? 16 : 110), viewportHeight);
            _board.style.minWidth = width; _board.style.minHeight = height;
            _surface.style.left = (width - _surfaceWidth) * .5f;
            _surface.style.top = (height - _surfaceHeight) * .5f;
        }
        public static Color Parse(string hex, Color fallback)
        { return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var result) ? result : fallback; }
        static TextAnchor ResolveAlign(string align)
        {
            if (Enum.TryParse(align, out TextAnchor result)) return result;
            return TextAnchor.MiddleCenter;
        }
    }
}
