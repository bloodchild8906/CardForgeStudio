using UnityEngine.UIElements;

namespace CardForge.EditorUI.Controls
{
    /// <summary>Rounded label whose colour is driven by a semantic tone.</summary>
    [UxmlElement]
    public partial class StatusPill : Label
    {
        private const string BaseClassName = "cf-pill";

        private PillTone tone;
        private string activeToneClassName;

        /// <summary>Creates a neutral pill.</summary>
        public StatusPill()
        {
            AddToClassList(BaseClassName);
            ApplyToneClass();
        }

        /// <summary>Semantic tone of the pill.</summary>
        [UxmlAttribute]
        public PillTone Tone
        {
            get => tone;
            set
            {
                tone = value;
                ApplyToneClass();
            }
        }

        private void ApplyToneClass()
        {
            if (activeToneClassName != null)
            {
                RemoveFromClassList(activeToneClassName);
            }
            activeToneClassName = $"{BaseClassName}--{tone.ToString().ToLowerInvariant()}";
            AddToClassList(activeToneClassName);
        }
    }
}
