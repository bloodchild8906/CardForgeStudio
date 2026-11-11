using CardForge.EditorUI.Controls;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Display value of one grid cell.</summary>
    public readonly struct GridCell
    {
        private GridCell(string text, PillTone tone)
        {
            Text = text ?? string.Empty;
            Tone = tone;
        }

        /// <summary>Text shown in the cell.</summary>
        public string Text { get; }

        /// <summary>Tone used when the column renders pills.</summary>
        public PillTone Tone { get; }

        /// <summary>Plain text cell.</summary>
        public static GridCell FromText(string text) => new GridCell(text, PillTone.Neutral);

        /// <summary>Cell carrying a healthy status.</summary>
        public static GridCell Ok(string text) => new GridCell(text, PillTone.Ok);

        /// <summary>Cell carrying a warning status.</summary>
        public static GridCell Warning(string text) => new GridCell(text, PillTone.Warning);

        /// <summary>Cell carrying an error status.</summary>
        public static GridCell Danger(string text) => new GridCell(text, PillTone.Danger);

        /// <summary>Cell carrying an informational status.</summary>
        public static GridCell Info(string text) => new GridCell(text, PillTone.Info);

        /// <summary>Lets plain strings be used wherever a cell is expected.</summary>
        public static implicit operator GridCell(string text) => FromText(text);
    }
}
