using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Navigation
{
    /// <summary>Filters a command list and runs the chosen entry.</summary>
#if UNITY_6000_5_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class QuickOpen
    {
        public readonly struct Entry
        {
            public Entry(string group, string title, string hint, Action run)
            {
                Group = group;
                Title = title;
                Hint = hint;
                Run = run;
            }

            public string Group { get; }
            public string Title { get; }
            public string Hint { get; }
            public Action Run { get; }
        }

        static readonly HashSet<string> PinnedGroups = new HashSet<string> { "Go to", "Create", "Tools", "Recent" };

        /// <summary>Shows a searchable command list in the studio modal layer.</summary>
        public static void Show(VisualElement layer, IReadOnlyList<Entry> entries)
        {
            layer.Clear();
            var overlay = new VisualElement();
            overlay.AddToClassList("cf-modal-overlay");
            overlay.AddToClassList("cf-quick-overlay");
            overlay.RegisterCallback<MouseDownEvent>(e => {
                if (e.target == overlay) layer.Clear();
            });

            var panel = new VisualElement();
            panel.AddToClassList("cf-quick");
            var hint = new Label("Type to filter  ·  Enter opens  ·  Esc closes");
            hint.AddToClassList("cf-quick__hint");
            var field = new TextField { name = "quick-query" };
            field.AddToClassList("cf-quick__query");
            var list = new ScrollView();
            list.AddToClassList("cf-quick__list");
            panel.Add(hint);
            panel.Add(field);
            panel.Add(list);
            overlay.Add(panel);
            layer.Add(overlay);

            var shown = new List<Entry>();
            int selected = 0;

            void Render()
            {
                string query = (field.value ?? "").Trim();
                shown = entries.Where(entry => Matches(entry, query)).Take(16).ToList();
                if (selected >= shown.Count) selected = Mathf.Max(0, shown.Count - 1);
                list.Clear();
                string group = null;
                VisualElement activeRow = null;
                for (int i = 0; i < shown.Count; i++)
                {
                    var entry = shown[i];
                    if (entry.Group != group)
                    {
                        group = entry.Group;
                        var heading = new Label(group.ToUpperInvariant());
                        heading.AddToClassList("cf-quick__group");
                        list.Add(heading);
                    }
                    int index = i;
                    var row = new Button(() => Run(index));
                    row.AddToClassList("cf-quick__row");
                    if (i == selected)
                    {
                        row.AddToClassList("cf-quick__row--active");
                        activeRow = row;
                    }
                    var title = new Label(entry.Title) { pickingMode = PickingMode.Ignore };
                    title.AddToClassList("cf-quick__title");
                    row.Add(title);
                    if (!string.IsNullOrEmpty(entry.Hint))
                    {
                        var detail = new Label(entry.Hint) { pickingMode = PickingMode.Ignore };
                        detail.AddToClassList("cf-quick__detail");
                        row.Add(detail);
                    }
                    list.Add(row);
                }
                if (activeRow != null) list.schedule.Execute(() => list.ScrollTo(activeRow));
                if (shown.Count == 0)
                {
                    var empty = new Label("Nothing matches that search.");
                    empty.AddToClassList("cf-quick__empty");
                    list.Add(empty);
                }
            }

            void Run(int index)
            {
                if (index < 0 || index >= shown.Count) return;
                var action = shown[index].Run;
                layer.Clear();
                action?.Invoke();
            }

            field.RegisterValueChangedCallback(_ => { selected = 0; Render(); });
            field.RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode == KeyCode.Escape) { layer.Clear(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.DownArrow && shown.Count > 0)
                {
                    selected = Mathf.Min(selected + 1, shown.Count - 1);
                    Render();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.UpArrow && shown.Count > 0)
                {
                    selected = Mathf.Max(selected - 1, 0);
                    Render();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Run(selected);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            Render();
            panel.schedule.Execute(() => field.Focus());
        }

        static bool Matches(Entry entry, string query)
        {
            if (string.IsNullOrEmpty(query)) return PinnedGroups.Contains(entry.Group);
            return Contains(entry.Title, query) || Contains(entry.Hint, query) || Contains(entry.Group, query);
        }

        static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
