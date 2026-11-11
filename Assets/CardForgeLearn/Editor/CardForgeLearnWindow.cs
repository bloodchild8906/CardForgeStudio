using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CardForge.Learn
{
    public sealed class CardForgeLearnWindow : EditorWindow
    {
        const string LessonKey = "CardForge.Learn.SelectedLesson";
        const string StepKey = "CardForge.Learn.SelectedStep";

        List<Lesson> _lessons;
        Vector2 _lessonScroll;
        Vector2 _contentScroll;
        int _selectedLesson;
        int _selectedStep;

        public static void Open()
        {
            var window = GetWindow<CardForgeLearnWindow>();
            window.titleContent = new GUIContent("Learn & Templates");
            window.minSize = new Vector2(760, 500);
            window.Show();
        }

        void OnEnable()
        {
            _lessons = CardForgeLessons.All();
            _selectedLesson = Mathf.Clamp(EditorPrefs.GetInt(LessonKey, 0), 0, _lessons.Count - 1);
            _selectedStep = Mathf.Clamp(EditorPrefs.GetInt(StepKey, 0), 0, _lessons[_selectedLesson].Steps.Count - 1);
        }

        void OnGUI()
        {
            if (_lessons == null || _lessons.Count == 0)
                _lessons = CardForgeLessons.All();

            EditorGUILayout.LabelField("Learn CardForge", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Guided lessons, a playable starter game, and the Unity client scene template.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            DrawLessonList();
            DrawLessonContent();
            EditorGUILayout.EndHorizontal();
        }

        void DrawLessonList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(230));
            _lessonScroll = EditorGUILayout.BeginScrollView(_lessonScroll, "box");
            for (var index = 0; index < _lessons.Count; index++)
            {
                var lesson = _lessons[index];
                var status = IsComplete(lesson) ? "  [done]" : "";
                if (GUILayout.Toggle(_selectedLesson == index, lesson.Title + status, "Button", GUILayout.Height(34)))
                {
                    if (_selectedLesson != index)
                    {
                        _selectedLesson = index;
                        _selectedStep = 0;
                        SaveSelection();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawLessonContent()
        {
            var lesson = _lessons[_selectedLesson];
            EditorGUILayout.BeginVertical();
            _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
            EditorGUILayout.LabelField(lesson.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(lesson.Summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);

            var step = lesson.Steps[Mathf.Clamp(_selectedStep, 0, lesson.Steps.Count - 1)];
            EditorGUILayout.LabelField("Step " + (_selectedStep + 1) + " of " + lesson.Steps.Count, EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(step.Text, MessageType.None);
            if (step.Action != null && GUILayout.Button(step.ActionLabel, GUILayout.Height(28)))
                RunAction(step.Action);

            if (lesson.AutoCheck != null)
            {
                var complete = IsComplete(lesson);
                EditorGUILayout.HelpBox(complete ? "This lesson's result is present in the project." : "Complete the lesson steps; this result will be detected automatically.", complete ? MessageType.Info : MessageType.None);
            }

            EditorGUILayout.Space(12);
            DrawTemplates();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_selectedStep <= 0))
            {
                if (GUILayout.Button("Previous Step"))
                {
                    _selectedStep--;
                    SaveSelection();
                }
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_selectedStep >= lesson.Steps.Count - 1))
            {
                if (GUILayout.Button("Next Step"))
                {
                    _selectedStep++;
                    SaveSelection();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        void DrawTemplates()
        {
            EditorGUILayout.LabelField("Templates", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Starter Duel", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("Creates a complete two-player example: ten cards, two layouts, a four-phase turn, actions, zones, resources, and victory rules.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Create or Refresh Starter Duel", GUILayout.Height(28)))
                CreateStarterDuel();
            using (new EditorGUI.DisabledScope(CardForgeLearnPaths.RepoRoot == null))
            {
                if (GUILayout.Button("Export Starter Duel .cardforge"))
                    RunAction(() =>
                    {
                        var path = CardForgeStarterTemplate.Publish();
                        EditorUtility.DisplayDialog("Starter Duel exported", "Package saved to:\n" + path, "OK");
                    });
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Unity Client Scene", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("Create or refresh the client scene and its connection settings.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Create or Refresh Client Scene", GUILayout.Height(28)))
                RunAction(CardForgeDashboardWindow.CreateClientScene);
            EditorGUILayout.EndVertical();
        }

        void CreateStarterDuel()
        {
            if (!EditorUtility.DisplayDialog("Create Starter Duel", "This creates or refreshes the Starter Duel data in Assets/CardForgeData. Existing unrelated game data is left alone.", "Create", "Cancel"))
                return;

            try
            {
                CardForgeStarterTemplate.Generate();
                _lessons = CardForgeLessons.All();
                EditorUtility.DisplayDialog("Starter Duel created", "The Starter Duel cards, layouts, and rules are ready in the Studio.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Template creation failed", exception.Message, "OK");
            }
        }

        static bool IsComplete(Lesson lesson)
        {
            if (lesson.AutoCheck == null)
                return false;
            try
            {
                return lesson.AutoCheck();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        static void RunAction(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("CardForge action failed", exception.Message, "OK");
            }
        }

        void SaveSelection()
        {
            EditorPrefs.SetInt(LessonKey, _selectedLesson);
            EditorPrefs.SetInt(StepKey, _selectedStep);
        }
    }
}
