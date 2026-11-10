using System;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Creates the leading checkbox column, including the select-all header.</summary>
    internal static class SelectionColumnFactory
    {
        public static Column Create(Func<int, bool> isSelected, Action<int, bool> setSelected, Action<bool> setAllSelected)
        {
            return new Column
            {
                name = "selection",
                width = 36,
                resizable = false,
                sortable = false,
                stretchable = false,
                makeHeader = () => CreateHeaderToggle(setAllSelected),
                makeCell = () => CreateRowToggle(setSelected),
                bindCell = (element, index) => BindRowToggle((Toggle)element, index, isSelected(index))
            };
        }

        private static VisualElement CreateHeaderToggle(Action<bool> setAllSelected)
        {
            var toggle = new Toggle();
            toggle.RegisterValueChangedCallback(change => setAllSelected(change.newValue));
            return toggle;
        }

        private static VisualElement CreateRowToggle(Action<int, bool> setSelected)
        {
            var toggle = new Toggle();
            toggle.RegisterValueChangedCallback(change =>
            {
                if (toggle.userData is int index)
                {
                    setSelected(index, change.newValue);
                }
            });
            return toggle;
        }

        private static void BindRowToggle(Toggle toggle, int index, bool selected)
        {
            toggle.userData = index;
            toggle.SetValueWithoutNotify(selected);
        }
    }
}
