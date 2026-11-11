using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TcgServer.Client.Editor
{
    /// <summary>
    /// Small local automation hook used by the demo launcher and CI. A request
    /// file lets the already-open Unity editor perform builds safely instead of
    /// starting a second editor against the same project.
    /// </summary>
    [InitializeOnLoad]
    public static class CardForgeBuildRequestProcessor
    {
        public const string ServerRequest = "Library/CardForgeBuildServer.request";
        public const string ClientRequest = "Library/CardForgeBuildClient.request";
        private static bool busy;

        static CardForgeBuildRequestProcessor() => EditorApplication.update += Tick;

        private static void Tick()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (File.Exists(ServerRequest))
            {
                File.Delete(ServerRequest);
                Run("server", BuildServer);
                return;
            }

            if (File.Exists(ClientRequest))
            {
                File.Delete(ClientRequest);
                Run("client", CardForgeClientBuilder.Build);
            }
        }


        private static void BuildServer()
        {
            if (!EditorApplication.ExecuteMenuItem("CardForge/Server/Build Native Windows Dedicated Server"))
                throw new InvalidOperationException("Dedicated server build menu command was not available.");
        }
        private static void Run(string label, Action build)
        {
            busy = true;
            try
            {
                Debug.Log("CARDFORGE_BUILD_REQUEST " + label);
                build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                busy = false;
            }
        }
    }
}

