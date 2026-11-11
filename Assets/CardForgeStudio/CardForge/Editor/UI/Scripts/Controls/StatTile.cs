using UnityEngine.UIElements;

namespace CardForge.EditorUI.Controls
{
    /// <summary>Compact tile showing a headline value above a caption.</summary>
    [UxmlElement]
    public partial class StatTile : VisualElement
    {
        private readonly Label valueLabel = new Label();
        private readonly Label captionLabel = new Label();

        /// <summary>Creates an empty tile.</summary>
        public StatTile()
        {
            AddToClassList("cf-tile");
            valueLabel.AddToClassList("cf-tile__value");
            captionLabel.AddToClassList("cf-tile__label");
            Add(valueLabel);
            Add(captionLabel);
        }

        /// <summary>Headline value.</summary>
        [UxmlAttribute]
        public string Value
        {
            get => valueLabel.text;
            set => valueLabel.text = value;
        }

        /// <summary>Caption under the value.</summary>
        [UxmlAttribute]
        public string Caption
        {
            get => captionLabel.text;
            set => captionLabel.text = value;
        }
    }
}
