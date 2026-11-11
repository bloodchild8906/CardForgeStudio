using System.Collections.Generic;

namespace CardForge.Editor.Graph
{
    /// <summary>Visual family of a graph node; drives the accent colour.</summary>
    public enum GraphNodeTone
    {
        /// <summary>An ordinary step.</summary>
        Process,

        /// <summary>A step that starts or gates the graph.</summary>
        Trigger,

        /// <summary>A step that changes the game.</summary>
        Effect,

        /// <summary>A turn phase.</summary>
        Phase
    }

    /// <summary>Everything the canvas needs to draw one node.</summary>
    public sealed class GraphNodeData
    {
        /// <summary>Stable identifier.</summary>
        public string Id { get; set; }

        /// <summary>Left edge in world space.</summary>
        public float X { get; set; }

        /// <summary>Top edge in world space.</summary>
        public float Y { get; set; }

        /// <summary>Small upper-case label above the title.</summary>
        public string Kicker { get; set; }

        /// <summary>Main title.</summary>
        public string Title { get; set; }

        /// <summary>Supporting text below the title.</summary>
        public string Detail { get; set; }

        /// <summary>Small line at the bottom of the node.</summary>
        public string Footer { get; set; }

        /// <summary>Accent family.</summary>
        public GraphNodeTone Tone { get; set; }

        /// <summary>Chips shown under the detail text.</summary>
        public IReadOnlyList<string> Badges { get; set; } = new string[0];
    }

    /// <summary>One directed connection between two nodes.</summary>
    public sealed class GraphLinkData
    {
        /// <summary>Id of the node the connection leaves.</summary>
        public string FromId { get; set; }

        /// <summary>Id of the node the connection enters.</summary>
        public string ToId { get; set; }

        /// <summary>Text shown on the connection.</summary>
        public string Label { get; set; }
    }

    /// <summary>A node that ended up at a new position.</summary>
    public readonly struct GraphNodeMove
    {
        /// <summary>Creates a move.</summary>
        public GraphNodeMove(string id, float x, float y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        /// <summary>Id of the node.</summary>
        public string Id { get; }

        /// <summary>New left edge.</summary>
        public float X { get; }

        /// <summary>New top edge.</summary>
        public float Y { get; }
    }
}
