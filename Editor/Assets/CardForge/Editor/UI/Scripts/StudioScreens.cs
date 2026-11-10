using System.Collections.Generic;

namespace CardForge.EditorUI
{
    /// <summary>Single source of truth for studio screen titles, navigation order and creatable document kinds.</summary>
    public static class StudioScreens
    {
        private const string FallbackTitle = "CardForge";

        private static readonly Dictionary<string, string> TitlesByScreenId = new Dictionary<string, string>
        {
            { "layouts", "Layouts" },
            { "cards", "Cards" },
            { "icons", "Icons" },
            { "turnflow", "Turn flows" },
            { "triggers", "Triggers" },
            { "actions", "Actions" },
            { "abilities", "Abilities" },
            { "blocks", "Blocks & editions" },
            { "keywords", "Keywords" },
            { "schema", "Data fields" }
        };

        private static readonly Dictionary<string, string> NewDocumentNounsByScreenId = new Dictionary<string, string>
        {
            { "layouts", "layout" },
            { "cards", "card" },
            { "icons", "icon" },
            { "turnflow", "turn flow" },
            { "triggers", "trigger" },
            { "actions", "action" },
            { "abilities", "ability" },
            { "blocks", "block" }
        };

        /// <summary>Screens shown in the sidebar, in display order.</summary>
        public static readonly IReadOnlyList<string> NavigationOrder = new[]
        {
            "layouts", "cards", "icons", "turnflow", "triggers", "actions", "abilities", "blocks", "schema"
        };

        /// <summary>Screens that can create a new document, in display order.</summary>
        public static readonly IReadOnlyList<string> CreatableOrder = new[]
        {
            "layouts", "cards", "icons", "turnflow", "triggers", "actions", "abilities", "blocks"
        };

        /// <summary>Human readable title of a screen, or the product name when the screen is unknown.</summary>
        public static string Title(string screenId)
        {
            return screenId != null && TitlesByScreenId.TryGetValue(screenId, out var title) ? title : FallbackTitle;
        }

        /// <summary>Singular noun used in "New ..." commands, or null when the screen cannot create documents.</summary>
        public static string NewDocumentNoun(string screenId)
        {
            return screenId != null && NewDocumentNounsByScreenId.TryGetValue(screenId, out var noun) ? noun : null;
        }
    }
}
