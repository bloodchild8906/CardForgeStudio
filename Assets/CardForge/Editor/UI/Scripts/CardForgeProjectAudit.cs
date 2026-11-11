using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>Read-only inspection of digital TCG content and its cross-document references.</summary>
    public sealed class CardForgeProjectAudit : EditorWindow
    {
        sealed class Finding
        {
            public string Screen;
            public string Id;
            public string Title;
            public string Detail;
        }

        CardForgeStore store;
        Action<string, string> openDocument;
        VisualElement report;

        public static void Show(CardForgeStore source, Action<string, string> navigate)
        {
            var window = GetWindow<CardForgeProjectAudit>("CardForge · Project health");
            window.minSize = new Vector2(660, 480);
            window.store = source;
            window.openDocument = navigate;
            window.Rebuild();
            window.Focus();
        }

        public void CreateGUI() => Rebuild();

        void Rebuild()
        {
            rootVisualElement.Clear();
            if (store == null) return;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/CardForge/Editor/UI/USS/CardForge.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            var app = new VisualElement();
            app.AddToClassList("cf-app");
            app.AddToClassList("cf-audit");
            app.style.flexGrow = 1;
            rootVisualElement.Add(app);
            new ThemeController(app);

            var head = new VisualElement();
            head.AddToClassList("cf-audit__header");
            app.Add(head);
            var eyebrow = new Label("DIGITAL GAME CONTENT / INTEGRITY");
            eyebrow.AddToClassList("cf-audit__eyebrow");
            head.Add(eyebrow);
            var heading = new Label("Project health");
            heading.AddToClassList("cf-audit__heading");
            head.Add(heading);
            head.Add(new Label(
                "Check card bindings, editions, phases, triggers, actions and abilities before exporting a digital build."));

            var actions = new VisualElement();
            actions.AddToClassList("cf-audit__actions");
            head.Add(actions);
            var refresh = new Button(Rebuild) { text = "Check again" };
            refresh.AddToClassList("cf-button");
            actions.Add(refresh);
            actions.Add(new Button(() => EditorUtility.RevealInFinder(
                System.IO.Path.GetFullPath(CardForgeStore.DataRoot))) { text = "Open source data" });

            report = new ScrollView();
            report.style.flexGrow = 1;
            report.AddToClassList("cf-audit__report");
            app.Add(report);
            RenderReport();
        }

        void RenderReport()
        {
            if (report == null || store == null) return;
            report.Clear();
            var findings = new List<Finding>();

            foreach (var block in store.Blocks.Values.OrderBy(x => x.name))
                AddIssues(findings, "blocks", block.id, block.name, store.Validate(block));
            foreach (var flow in store.TurnFlows.Values.OrderBy(x => x.name))
                AddIssues(findings, "turnflow", flow.id, flow.name, store.ValidateFlow(flow));
            foreach (var trigger in store.Triggers.Values.OrderBy(x => x.name))
                AddIssues(findings, "triggers", trigger.id, trigger.name, store.ValidateTrigger(trigger));
            foreach (var action in store.Actions.Values.OrderBy(x => x.name))
                AddIssues(findings, "actions", action.id, action.name, store.ValidateAction(action));
            foreach (var ability in store.Abilities.Values.OrderBy(x => x.name))
                AddIssues(findings, "abilities", ability.id, ability.name, store.ValidateAbility(ability));
            foreach (var keyword in store.Keywords.Values.OrderBy(x => x.name))
                AddIssues(findings, "keywords", keyword.id, keyword.name, store.ValidateKeyword(keyword));

            var counters = new VisualElement(); counters.AddToClassList("cf-audit__stats"); report.Add(counters);
            Counter(counters, store.Blocks.Count.ToString(), "Editions");
            Counter(counters, store.Cards.Count.ToString(), "Cards");
            Counter(counters, store.TurnFlows.Count.ToString(), "Turn flows");
            Counter(counters, store.Actions.Count.ToString(), "Actions");
            Counter(counters, store.Abilities.Count.ToString(), "Abilities");
            Counter(counters, store.Keywords.Count.ToString(), "Keywords");
            Counter(counters, findings.Count.ToString(), "Issues");

            if (findings.Count == 0)
            {
                var success = new VisualElement(); success.AddToClassList("cf-audit__success");
                success.Add(new Label("No integrity issues found"));
                success.Add(new Label("All currently implemented structural and reference checks passed. This does not prove gameplay balance or rule execution."));
                report.Add(success);
                return;
            }
            var summary = new Label(findings.Count + " issue" + (findings.Count == 1 ? "" : "s") +
                " across " + findings.Select(f => f.Screen + "/" + f.Id).Distinct().Count() + " documents");
            summary.AddToClassList("cf-audit__summary");
            report.Add(summary);
            foreach (var group in findings.GroupBy(x => x.Screen + "/" + x.Id))
            {
                var first = group.First();
                var section = new VisualElement(); section.AddToClassList("cf-audit__group"); report.Add(section);
                var line = new VisualElement(); line.AddToClassList("cf-audit__group-head"); section.Add(line);
                var title = new Label(first.Title + "  ·  " + ScreenTitle(first.Screen));
                title.AddToClassList("cf-audit__group-title"); line.Add(title);
                var open = new Button(() =>
                {
                    openDocument?.Invoke(first.Screen, first.Id);
                }) { text = "Open editor" };
                open.AddToClassList("cf-button");
                line.Add(open);
                foreach (var issue in group)
                {
                    var label = new Label("• " + issue.Detail);
                    label.AddToClassList("cf-audit__issue");
                    section.Add(label);
                }
            }
        }

        static void AddIssues(List<Finding> findings, string screen, string id, string title, IEnumerable<string> issues)
        {
            foreach (var message in issues)
                findings.Add(new Finding { Screen = screen, Id = id, Title = title, Detail = message });
        }

        static string ScreenTitle(string id)
        {
            switch (id)
            {
                case "blocks": return "Edition";
                case "turnflow": return "Turn flow";
                case "triggers": return "Trigger";
                case "actions": return "Action";
                case "abilities": return "Ability";
                case "keywords": return "Keyword";
                default: return id;
            }
        }

        static void Counter(VisualElement parent, string value, string caption)
        {
            var item = new VisualElement(); item.AddToClassList("cf-audit__stat");
            var count = new Label(value); count.AddToClassList("cf-audit__stat-value");
            var name = new Label(caption); name.AddToClassList("cf-audit__stat-label");
            item.Add(count); item.Add(name); parent.Add(item);
        }
    }
}
