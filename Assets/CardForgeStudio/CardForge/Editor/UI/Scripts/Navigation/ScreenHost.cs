using System;
using System.Collections.Generic;
using CardForge.EditorUI.Dialogs;
using CardForge.EditorUI.Grid;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Navigation
{
    /// <summary>Owns one controller per screen and swaps the visible one.</summary>
    public sealed class ScreenHost
    {
        private readonly VisualElement container;
        private readonly Dictionary<string, GridScreenController> controllersByScreenId = new Dictionary<string, GridScreenController>();

        /// <summary>Forwards actions raised by any screen.</summary>
        public event Action<GridActionRequest> ActionRequested;

        /// <summary>Forwards confirmed import and export dialogs from any screen.</summary>
        public event Action<string, ImportExportResult> ImportExportConfirmed;

        /// <summary>Creates a controller for every definition.</summary>
        public ScreenHost(VisualElement container, IEnumerable<GridScreenDefinition> definitions, ModalHost modalHost)
        {
            this.container = container;
            foreach (var definition in definitions)
            {
                var controller = new GridScreenController(definition, modalHost);
                controller.ActionRequested += request => ActionRequested?.Invoke(request);
                controller.ImportExportConfirmed += (screenId, result) => ImportExportConfirmed?.Invoke(screenId, result);
                controllersByScreenId[definition.Id] = controller;
            }
        }

        /// <summary>Shows the screen with the given id.</summary>
        public void Show(string screenId)
        {
            container.Clear();
            container.Add(controllersByScreenId[screenId].Root);
        }

        /// <summary>Reloads every screen from its row source.</summary>
        public void ReloadAll()
        {
            foreach (var controller in controllersByScreenId.Values)
            {
                controller.Reload();
            }
        }
    }
}
