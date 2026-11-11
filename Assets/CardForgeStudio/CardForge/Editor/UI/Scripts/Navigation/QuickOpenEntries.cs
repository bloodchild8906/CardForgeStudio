using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.Editor;
using CardForge.EditorUI.Screens;

namespace CardForge.EditorUI.Navigation
{
    /// <summary>Callbacks the studio supplies so the command palette can drive it.</summary>
    public sealed class QuickOpenCommands
    {
        /// <summary>Switches to a library screen by id.</summary>
        public Action<string> Navigate { get; set; }

        /// <summary>Starts creating a document on the given screen.</summary>
        public Action<string> CreateDocument { get; set; }

        /// <summary>Opens a document in its editor, given its screen id and document id.</summary>
        public Action<string, string> OpenDocument { get; set; }

        /// <summary>Ready-made entries for the studio tools.</summary>
        public IReadOnlyList<QuickOpen.Entry> ToolEntries { get; set; } = new List<QuickOpen.Entry>();
    }

    /// <summary>Builds the searchable command list shown by <see cref="QuickOpen"/>.</summary>
    public static class QuickOpenEntries
    {
        private const string GoToGroup = "Go to";
        private const string CreateGroup = "Create";
        private const string RecentGroup = "Recent";
        private const string UnnamedTitle = "(unnamed)";

        private sealed class DocumentRecord
        {
            public DocumentRecord(string screenId, string documentId, string name, string hint)
            {
                ScreenId = screenId;
                DocumentId = documentId;
                Name = string.IsNullOrWhiteSpace(name) ? UnnamedTitle : name;
                Hint = hint;
            }

            public string ScreenId { get; }
            public string DocumentId { get; }
            public string Name { get; }
            public string Hint { get; }
        }

        /// <summary>Creates navigation, creation, tool, recent and per-document entries for the current project.</summary>
        public static IReadOnlyList<QuickOpen.Entry> Build(LiveCatalog catalog, QuickOpenCommands commands)
        {
            var documents = IndexDocuments(catalog);
            var entries = new List<QuickOpen.Entry>();
            entries.AddRange(NavigationEntries(commands));
            entries.AddRange(CreationEntries(commands));
            entries.AddRange(commands.ToolEntries);
            entries.AddRange(RecentEntries(documents, commands));
            entries.AddRange(documents.Select(record => OpenEntry(record, StudioScreens.Title(record.ScreenId), record.Hint, commands)));
            return entries;
        }

        private static IEnumerable<QuickOpen.Entry> NavigationEntries(QuickOpenCommands commands)
        {
            foreach (string screenId in StudioScreens.NavigationOrder)
            {
                string target = screenId;
                yield return new QuickOpen.Entry(GoToGroup, StudioScreens.Title(target), "Open library", () => commands.Navigate(target));
            }
        }

        private static IEnumerable<QuickOpen.Entry> CreationEntries(QuickOpenCommands commands)
        {
            foreach (string screenId in StudioScreens.CreatableOrder)
            {
                string target = screenId;
                string noun = StudioScreens.NewDocumentNoun(target);
                yield return new QuickOpen.Entry(CreateGroup, "New " + noun, StudioScreens.Title(target), () => commands.CreateDocument(target));
            }
        }

        private static IEnumerable<QuickOpen.Entry> RecentEntries(List<DocumentRecord> documents, QuickOpenCommands commands)
        {
            foreach (var reference in RecentDocuments.Load())
            {
                var record = documents.FirstOrDefault(candidate => candidate.ScreenId == reference.ScreenId && candidate.DocumentId == reference.DocumentId);
                if (record != null) yield return OpenEntry(record, RecentGroup, StudioScreens.Title(record.ScreenId), commands);
            }
        }

        private static QuickOpen.Entry OpenEntry(DocumentRecord record, string group, string hint, QuickOpenCommands commands)
        {
            return new QuickOpen.Entry(group, record.Name, hint, () => commands.OpenDocument(record.ScreenId, record.DocumentId));
        }

        private static List<DocumentRecord> IndexDocuments(LiveCatalog catalog)
        {
            var store = catalog.Store;
            var block = catalog.Block;
            var records = new List<DocumentRecord>();
            var blockLayouts = (block?.layoutIds ?? new List<string>()).Where(store.Layouts.ContainsKey).Select(id => store.Layouts[id]);
            var blockCards = (block?.cardIds ?? new List<string>()).Where(store.Cards.ContainsKey).Select(id => store.Cards[id]);
            records.AddRange(blockLayouts.Select(layout => new DocumentRecord("layouts", layout.id, layout.name, layout.isBack ? "Back layout" : "Front layout")));
            records.AddRange(blockCards.Select(card => new DocumentRecord("cards", card.id, card.name, card.cardNumber)));
            records.AddRange(store.Icons.Values.Select(icon => new DocumentRecord("icons", icon.id, icon.name, null)));
            records.AddRange(store.TurnFlows.Values.Select(flow => new DocumentRecord("turnflow", flow.id, flow.name, null)));
            records.AddRange(store.Triggers.Values.Select(trigger => new DocumentRecord("triggers", trigger.id, trigger.name, trigger.eventType)));
            records.AddRange(store.Actions.Values.Select(action => new DocumentRecord("actions", action.id, action.name, null)));
            records.AddRange(store.Abilities.Values.Select(ability => new DocumentRecord("abilities", ability.id, ability.name, null)));
            records.AddRange(store.Blocks.Values.Select(item => new DocumentRecord("blocks", item.id, item.name, item.code)));
            return records;
        }
    }
}
