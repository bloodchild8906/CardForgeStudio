using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgServer.Client
{
    public sealed partial class CardForgeToolkitClient
    {
        // Unity can reconstruct a UIDocument after an editor script reload without
        // invoking Start again. Never leave the editable UXML preview in place
        // while gameplay is supposed to be running.
        private void Update()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;

            if (document == null) document = GetComponent<UIDocument>();
            var liveRoot = document?.rootVisualElement;
            if (liveRoot == null || liveRoot.panel == null) return;

            bool rootChanged = !ReferenceEquals(root, liveRoot);
            bool lostContent = content == null || !ReferenceEquals(content.panel, liveRoot.panel);
            if (!rootChanged && !lostContent) return;

            // Domain reload preserves scene components but not VisualElement
            // references. Recover required links before rebuilding the shell.
            if (api == null) api = GetComponent<TcgRestClient>();
            if (session == null) session = GetComponent<TcgClientSession>();
            if (settings == null)
                settings = api?.Settings ?? Resources.Load<TcgConnectionSettings>("TcgConnectionSettings");
            if (api != null && settings != null) api.Configure(settings);
            if (remoteAssets == null) remoteAssets = GetComponent<CardForgeRemoteAssets>();
            if (remoteAssets != null && api != null && session != null)
                remoteAssets.Configure(api, session);

            root = liveRoot;
            root.style.flexGrow = 1;
            root.style.backgroundColor = Night;
            root.style.color = Pale;
            var arenaStyle = Resources.Load<StyleSheet>("AstralRift/CardForgeArena");
            if (arenaStyle != null && !root.styleSheets.Contains(arenaStyle))
                root.styleSheets.Add(arenaStyle);
            navigation.Clear();
            BuildArenaShell();
            RegisterRuntimeShortcuts();
            Show(string.IsNullOrWhiteSpace(page) ? "Play" : page);

            if (session?.IsAuthenticated != true && api != null && settings != null)
                StartCoroutine(ConnectDemo());
        }

        private void RegisterRuntimeShortcuts()
        {
            if (root == null) return;
            root.UnregisterCallback<KeyDownEvent>(HandleRuntimeKeyboard);
            root.RegisterCallback<KeyDownEvent>(HandleRuntimeKeyboard);
        }

        private void HandleRuntimeKeyboard(KeyDownEvent evt)
        {
            if (string.IsNullOrEmpty(matchId) || page != "Play") return;
            if (evt.keyCode == KeyCode.Space && match != null && !actionBusy &&
                targetAction == null && match.activePlayerId == session?.User?.id &&
                !(evt.target is TextField) && !(evt.target is Button))
            {
                var advance = (match.allowedActions ?? Array.Empty<ClientGameActionDto>())
                    .FirstOrDefault(action => string.IsNullOrEmpty(action.sourceZone) &&
                        (action.id.IndexOf("next-phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         action.id.IndexOf("end-turn", StringComparison.OrdinalIgnoreCase) >= 0));
                if (advance != null)
                {
                    Submit(advance, null, null, null);
                    evt.StopPropagation();
                }
                return;
            }
            if (evt.keyCode != KeyCode.Escape || selected == null && targetAction == null) return;
            selected = null;
            targetAction = null;
            ShowMatch();
            evt.StopPropagation();
        }
    }
}
