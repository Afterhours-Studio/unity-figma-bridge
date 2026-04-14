using UnityEditor;
using UnityEngine;

namespace Afterhours.FigmaBridge.Editor
{
    public sealed partial class FigmaBridgeEditorWindow
    {
        // ─── Log Tab ─────────────────────────────────────

        private void DrawLogTab()
        {
            // Header with count badge + clear
            Indent(() =>
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Import Log", EditorStyles.boldLabel);
                if (_logEntries.Count > 0)
                    DrawBadge($"{_logEntries.Count}", Accent);
                GUILayout.FlexibleSpace();
                if (_logEntries.Count > 0)
                {
                    DrawColorButton("Clear", BtnRed, BtnRedHover, () => _logEntries.Clear(), GUILayout.Width(60), GUILayout.Height(20));
                }
                EditorGUILayout.EndHorizontal();
            });

            GUILayout.Space(4);
            DrawSeparator();
            GUILayout.Space(4);

            if (_logEntries.Count == 0)
            {
                GUILayout.Space(40);
                GUILayout.Label("No log entries yet.\nSync a document to see activity here.", s_EmptyHint);
                return;
            }

            // Log rows
            const float rowH = 22;
            var evenBg = Pro ? C(0.22f) : C(0.94f);
            var oddBg = Pro ? C(0.20f) : C(0.92f);

            Indent(() =>
            {
                for (int i = 0; i < _logEntries.Count; i++)
                {
                    var entry = _logEntries[i];
                    var rRect = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true));

                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(rRect, i % 2 == 0 ? evenBg : oddBg);
                        // Left color indicator
                        var indicatorColor = entry.IsError ? ErrorText : SuccessText;
                        EditorGUI.DrawRect(new Rect(rRect.x, rRect.y + 4, 3, rowH - 8), indicatorColor);
                    }

                    // Timestamp
                    GUI.Label(new Rect(rRect.x, rRect.y, 62, rowH), entry.Timestamp.ToString("HH:mm:ss"), s_LogTimestamp);

                    // Message
                    var msgColor = entry.IsError ? ErrorText : (Pro ? C(0.80f) : C(0.18f));
                    s_LogMessage.normal.textColor = msgColor;
                    GUI.Label(new Rect(rRect.x + 64, rRect.y, rRect.width - 64, rowH), entry.Message, s_LogMessage);
                }
            });
        }
    }
}
