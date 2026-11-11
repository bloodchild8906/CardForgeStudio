using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace CardForge.EditorUI.Navigation
{
    /// <summary>Remembers the most recently opened documents across editor sessions.</summary>
    public static class RecentDocuments
    {
        private const string PreferenceKey = "CardForge.RecentDocuments";
        private const int Capacity = 8;
        private const char EntrySeparator = ';';
        private const char FieldSeparator = '|';

        /// <summary>Points at one document of one screen.</summary>
        public readonly struct Reference
        {
            /// <summary>Creates a reference.</summary>
            public Reference(string screenId, string documentId)
            {
                ScreenId = screenId;
                DocumentId = documentId;
            }

            /// <summary>Screen that owns the document.</summary>
            public string ScreenId { get; }

            /// <summary>Stable identifier of the document.</summary>
            public string DocumentId { get; }
        }

        /// <summary>Moves a document to the front of the list. Documents without an id are ignored.</summary>
        public static void Record(string screenId, string documentId)
        {
            if (string.IsNullOrEmpty(screenId) || string.IsNullOrEmpty(documentId)) return;
            var references = Load().Where(reference => !IsSame(reference, screenId, documentId)).ToList();
            references.Insert(0, new Reference(screenId, documentId));
            Save(references.Take(Capacity));
        }

        /// <summary>Returns the remembered documents, newest first.</summary>
        public static IReadOnlyList<Reference> Load()
        {
            var references = new List<Reference>();
            string stored = EditorPrefs.GetString(PreferenceKey, string.Empty);
            foreach (string entry in stored.Split(new[] { EntrySeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = entry.Split(FieldSeparator);
                if (fields.Length == 2) references.Add(new Reference(fields[0], fields[1]));
            }
            return references;
        }

        private static bool IsSame(Reference reference, string screenId, string documentId)
        {
            return reference.ScreenId == screenId && reference.DocumentId == documentId;
        }

        private static void Save(IEnumerable<Reference> references)
        {
            var entries = references.Select(reference => reference.ScreenId + FieldSeparator + reference.DocumentId);
            EditorPrefs.SetString(PreferenceKey, string.Join(EntrySeparator.ToString(), entries));
        }
    }
}
