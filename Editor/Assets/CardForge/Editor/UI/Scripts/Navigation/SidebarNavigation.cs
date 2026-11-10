using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.EditorUI.Grid;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Navigation
{
    /// <summary>Persistent workspace navigation, independent of the open document.</summary>
    public sealed class SidebarNavigation
    {
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        public event Action<string> Selected;
        public SidebarNavigation(VisualElement container, IEnumerable<GridScreenDefinition> definitions)
        {
            var available = new HashSet<string>(definitions.Select(d => d.Id));
            AddGroup(container, "Content");
            Add(container, available, "layouts", "Layouts");
            Add(container, available, "cards", "Cards");
            Add(container, available, "icons", "Icons");
            AddGroup(container, "Game rules");
            Add(container, available, "turnflow", "Turn flows");
            Add(container, available, "triggers", "Triggers");
            Add(container, available, "actions", "Actions");
            Add(container, available, "abilities", "Abilities");
            AddGroup(container, "Project");
            Add(container, available, "blocks", "Blocks & editions");
            available.Add("schema");
            Add(container, available, "schema", "Data fields");
        }
        static void AddGroup(VisualElement container, string title)
        {
            var label = new Label(title); label.AddToClassList("cf-navigation__group"); container.Add(label);
        }
        void Add(VisualElement container, HashSet<string> available, string id, string title)
        {
            if (!available.Contains(id)) return;
            var button = new Button(() => Select(id)) { name = "navigate-" + id, tooltip = title };
            button.Add(new NavigationIcon(id));
            var label=new Label(title) { pickingMode=PickingMode.Ignore }; label.AddToClassList("cf-navigation__label");button.Add(label);
            button.AddToClassList("cf-navigation__item"); container.Add(button); buttons[id] = button;
        }
        public void MarkActive(string id)
        {
            foreach (var item in buttons) item.Value.EnableInClassList("cf-navigation__item--active", item.Key == id);
        }
        public void Select(string id)
        {
            if (!buttons.ContainsKey(id)) return;
            MarkActive(id); Selected?.Invoke(id);
        }

        sealed class NavigationIcon : VisualElement
        {
            public NavigationIcon(string id)
            {
                AddToClassList("cf-navigation__icon"); pickingMode=PickingMode.Ignore;
                generateVisualContent += context => {
                    var p=context.painter2D;p.strokeColor=resolvedStyle.color;p.lineWidth=1.3f;
                    void Line(params Vector2[] points) { p.BeginPath();p.MoveTo(points[0]);foreach(var point in points.Skip(1))p.LineTo(point);p.Stroke(); }
                    void Box(float x,float y,float w,float h) { Line(new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h),new Vector2(x,y)); }
                    switch(id) {
                        case "layouts":Box(2,1,12,14);Line(new Vector2(2,5),new Vector2(14,5));Box(4,7,8,4);break;
                        case "cards":Box(5,4,9,11);Line(new Vector2(3,12),new Vector2(1,2),new Vector2(10,1));break;
                        case "icons":Line(new Vector2(8,1),new Vector2(14,8),new Vector2(8,15),new Vector2(2,8),new Vector2(8,1));break;
                        case "turnflow":Box(1,1,5,5);Box(10,10,5,5);Line(new Vector2(6,3),new Vector2(12,3),new Vector2(12,10));break;
                        case "actions":Line(new Vector2(9,1),new Vector2(3,9),new Vector2(7,9),new Vector2(6,15),new Vector2(13,6),new Vector2(9,6),new Vector2(9,1));break;
                        case "abilities":Line(new Vector2(8,1),new Vector2(10,6),new Vector2(15,6),new Vector2(11,10),new Vector2(12,15),new Vector2(8,12),new Vector2(4,15),new Vector2(5,10),new Vector2(1,6),new Vector2(6,6),new Vector2(8,1));break;
                        case "blocks":Box(2,5,12,10);Line(new Vector2(2,5),new Vector2(5,1),new Vector2(11,1),new Vector2(14,5));Line(new Vector2(8,5),new Vector2(8,10));break;
                        default:for(int y=3;y<=13;y+=5){Box(1,y-1,2,2);Line(new Vector2(6,y),new Vector2(15,y));}break;
                    }
                };
            }
        }
    }
}
