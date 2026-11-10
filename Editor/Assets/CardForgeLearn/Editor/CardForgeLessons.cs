using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;

namespace CardForge.Learn
{
    public sealed class LessonStep
    {
        public string Text;
        public string ActionLabel;
        public Action Action;
    }

    public sealed class Lesson
    {
        public string Id;
        public string Title;
        public string Summary;
        public List<LessonStep> Steps = new List<LessonStep>();
        /// <summary>Returns true when the project already shows this lesson's result. Null when it cannot be detected.</summary>
        public Func<bool> AutoCheck;
    }

    /// <summary>The tutorial track. Lessons only describe steps and jump to existing tools; they never change project data on their own.</summary>
    public static class CardForgeLessons
    {
        public static List<Lesson> All()
        {
            return new List<Lesson>
            {
                Tour(), FirstCard(), Frames(), Rules(), Publish(), RunServer(), ImportAndActivate(), PlayClient()
            };
        }

        // ----- actions the lessons can trigger -----------------------------------------------------------------

        public static void Menu(string path)
        {
            if (!EditorApplication.ExecuteMenuItem(path))
                EditorUtility.DisplayDialog("Menu item not found", "Could not run '" + path + "'. Check the Console for compile errors, then try again.", "OK");
        }

        public static void Reveal(string folder, string what)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                EditorUtility.DisplayDialog("Folder not found", what + " was not found. This Unity project needs to sit inside a CardForgeStudio checkout (the Editor folder next to Server and Shared).", "OK");
                return;
            }
            EditorUtility.RevealInFinder(folder);
        }

        public static void StartServer()
        {
            var launcher = CardForgeLearnPaths.ServerLauncher;
            if (launcher == null || !File.Exists(launcher))
            {
                EditorUtility.DisplayDialog("Server launcher not found", "Expected Server/TcgULtimateServer.cmd next to this Unity project's Editor folder.", "OK");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = launcher, WorkingDirectory = CardForgeLearnPaths.ServerFolder, UseShellExecute = true });
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not start the server", exception.Message, "OK");
            }
        }

        // ----- lessons ------------------------------------------------------------------------------------------

        static Lesson Tour()
        {
            return new Lesson
            {
                Id = "tour",
                Title = "1. The tour: what is in the box",
                Summary = "Five minutes to learn how the pieces fit before you click anything.",
                Steps =
                {
                    new LessonStep { Text = "CardForgeStudio is one repository with four parts: Editor (this Unity project, where you design), Server (the authoritative game server and its dashboard), Shared (the .cardforge package contract both sides read) and GamePackages (finished game packages)." },
                    new LessonStep { Text = "The flow is always the same: design cards and rules here, publish a .cardforge package, import it on the server, then play it from a client.", ActionLabel = "Show the repository folder", Action = () => Reveal(CardForgeLearnPaths.RepoRoot, "The repository folder") },
                    new LessonStep { Text = "Open the Studio. Everything you author is saved as plain JSON under Assets/CardForgeData, so it is safe to put in version control.", ActionLabel = "Open Studio", Action = () => Menu("CardForge/Open Studio") }
                }
            };
        }

        static Lesson FirstCard()
        {
            return new Lesson
            {
                Id = "first-card",
                Title = "2. Make your first card",
                Summary = "Create a block, a layout and a card, and see it rendered.",
                AutoCheck = () => CardForgeLearnPaths.CountFiles(CardForgeLearnPaths.CardForgeDataFolder, "/cards/", "*.json") > 0,
                Steps =
                {
                    new LessonStep { Text = "Fastest start: open Learn and Templates and create the Starter Duel template. It gives you ten cards and two layouts to experiment on.", ActionLabel = "Open Learn & Templates", Action = () => Menu("CardForge/Learn and Templates") },
                    new LessonStep { Text = "Open the Studio and choose Cards in the sidebar. Press New, give the card a name, and fill in its cost, power and toughness.", ActionLabel = "Open Studio", Action = () => Menu("CardForge/Open Studio") },
                    new LessonStep { Text = "The preview on the right is the real renderer. Change a value and watch the card update. Press Ctrl+K anywhere in the Studio to jump to any card or screen by name." },
                    new LessonStep { Text = "This lesson is marked done automatically once at least one card file exists." }
                }
            };
        }

        static Lesson Frames()
        {
            return new Lesson
            {
                Id = "frames",
                Title = "3. Frames, layouts and zones",
                Summary = "Change how a card looks without touching its data.",
                AutoCheck = () => CardForgeLearnPaths.CountFiles(CardForgeLearnPaths.CardForgeDataFolder, "/layouts/", "*.json") > 0,
                Steps =
                {
                    new LessonStep { Text = "A layout is a frame with zones. Each zone shows one card field: the title, the cost, the artwork. Open the layout designer from the Studio sidebar and drag a zone to move it." },
                    new LessonStep { Text = "Already have a finished card frame as an image? The Frame Composer reads it and proposes zones for you to review and edit.", ActionLabel = "Open Frame Composer", Action = () => Menu("CardForge/Frame Composer") },
                    new LessonStep { Text = "Icons and fonts come from the Icon Set Studio and the Google Fonts Library.", ActionLabel = "Open Icon Set Studio", Action = () => Menu("CardForge/Icon Set Studio") }
                }
            };
        }

        static Lesson Rules()
        {
            return new Lesson
            {
                Id = "rules",
                Title = "4. Rules: actions, phases and victory",
                Summary = "Define what players are allowed to do and how the game ends.",
                AutoCheck = () => CardForgeLearnPaths.CountFiles(CardForgeLearnPaths.CardForgeDataFolder, "turn-flows", "*.json") > 0,
                Steps =
                {
                    new LessonStep { Text = "An action is one thing a player can do: draw, deploy, strike. A turn flow lists the phases of a turn and which actions are legal in each phase." },
                    new LessonStep { Text = "Open the Studio and look at Actions and Turn flows. The Starter Duel template uses six actions in four phases, which is a good size to read in one sitting.", ActionLabel = "Open Studio", Action = () => Menu("CardForge/Open Studio") },
                    new LessonStep { Text = "Victory rules and the game's zones and resources are set in the Game Package Publisher, next lesson. A package only exports the actions its own turn flow uses, so several games can live in one project." }
                }
            };
        }

        static Lesson Publish()
        {
            return new Lesson
            {
                Id = "publish",
                Title = "5. Publish a game package",
                Summary = "Turn your block into a .cardforge file the server can import.",
                AutoCheck = () => CardForgeLearnPaths.CountFiles(CardForgeLearnPaths.GamePackagesFolder, "", "*.cardforge") > 0,
                Steps =
                {
                    new LessonStep { Text = "Open the Game Package Publisher. Pick a block, set a semantic version such as 1.0.0, choose the turn flow and review the zones, resources and victory rules.", ActionLabel = "Open Game Package Publisher", Action = () => Menu("CardForge/Game Package Publisher") },
                    new LessonStep { Text = "Validation runs before anything is written, so fix any problems it lists. A package version is immutable on the server: publish a new version number when you change anything." },
                    new LessonStep { Text = "The finished file lands in the GamePackages folder.", ActionLabel = "Show GamePackages", Action = () => Reveal(CardForgeLearnPaths.GamePackagesFolder, "The GamePackages folder") }
                }
            };
        }

        static Lesson RunServer()
        {
            return new Lesson
            {
                Id = "server",
                Title = "6. Run the server",
                Summary = "Start the dedicated server and its management dashboard.",
                Steps =
                {
                    new LessonStep { Text = "The server is a separate program (.NET 8), not part of Unity. Starting it builds it if needed, opens the dashboard and starts the API.", ActionLabel = "Start the server", Action = StartServer },
                    new LessonStep { Text = "Set the TCG_ADMIN_HANDLES environment variable to your handle before registering, so your account becomes an administrator. Package import and activation need an administrator." },
                    new LessonStep { Text = "Server manuals: getting started, security and deployment, and the protocol reference.", ActionLabel = "Show server docs", Action = () => Reveal(CardForgeLearnPaths.ServerFolder == null ? null : Path.Combine(CardForgeLearnPaths.ServerFolder, "docs"), "The server docs folder") }
                }
            };
        }

        static Lesson ImportAndActivate()
        {
            return new Lesson
            {
                Id = "import",
                Title = "7. Import and activate on the server",
                Summary = "Make your game playable by everyone connected to the server.",
                Steps =
                {
                    new LessonStep { Text = "In the server dashboard open Game packages, choose your .cardforge file and import it. The server checks the archive, the file hashes, the rule pack and the victory rules before it accepts anything." },
                    new LessonStep { Text = "Select the imported version and activate it. Activation is refused while a match is using the same game, so activate when the server is quiet. Older versions stay available for rollback." },
                    new LessonStep { Text = "New games can now use the package's game id as their rule pack. See Documentation/editor-server-workflow.md for the full checklist.", ActionLabel = "Show Documentation", Action = () => Reveal(CardForgeLearnPaths.DocumentationFolder, "The Documentation folder") }
                }
            };
        }

        static Lesson PlayClient()
        {
            return new Lesson
            {
                Id = "client",
                Title = "8. Play it from the Unity client",
                Summary = "Connect a client scene to your server and play a match.",
                Steps =
                {
                    new LessonStep { Text = "Create or refresh the client scene. It sets up the connection settings and the template UI.", ActionLabel = "Create or refresh client scene", Action = () => Menu("CardForge/Client/Create or Refresh Client Scene") },
                    new LessonStep { Text = "Press Play, register an account, then queue a match or create an AI game. Run a second client (a build, or a second editor) to play against a person." },
                    new LessonStep { Text = "Card data is read from the same package you published, so editing a card and publishing a new version is how balance changes reach players." }
                }
            };
        }
    }
}
