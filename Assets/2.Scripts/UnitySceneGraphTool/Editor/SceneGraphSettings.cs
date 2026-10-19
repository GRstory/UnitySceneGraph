using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SceneGraph.Editor
{
    /// <summary>규칙 3 휴리스틱과 후보 필터 설정. 기본값은 PLAN.md 3.1.</summary>
    [Serializable]
    public sealed class GroupingOptions
    {
        public int maxParts = 24;
        public float maxDiagonal = 4f;
        [Tooltip("정확히 같은 이름(대소문자 무시). 끝이 *이면 접두어 매칭")]
        public List<string> stopList = new List<string> { "Environment", "Props", "Geometry", "Furniture", "Group", "---*", "[*" };
        public bool includeInactive = true;

        public bool IsStopName(string name)
        {
            foreach (var entry in stopList)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry.EndsWith("*"))
                {
                    if (name.StartsWith(entry.Substring(0, entry.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    /// <summary>공간 관계와 점수 설정. 기본값은 PLAN.md 4.1, 4.2.</summary>
    [Serializable]
    public sealed class RelationOptions
    {
        [Tooltip("tol = tolFactor × min(A.size.y, B.size.y)")]
        public float tolFactor = 0.1f;
        [Tooltip("0 허용폭 방어용 epsilon. 절대 하한은 PLAN.md 미결이라 정책값이 아니다")]
        public float tolEpsilon = 1e-4f;
        public float onFootprintOverlap = 0.5f;
        [Tooltip("on에서 A가 받침면 아래로 박혀도 되는 깊이 = onPenetrationRatio × A.size.y")]
        public float onPenetrationRatio = 0.5f;
        public float insideRatio = 0.8f;
        public float nearFactor = 1.5f;
        public float minNounScore = 0.3f;
        public int defaultLimit = 10;
        public string defaultFrameForward = "+z";
        public string defaultFrameUp = "+y";
    }

    [FilePath("ProjectSettings/UnitySceneGraphSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class SceneGraphSettings : ScriptableSingleton<SceneGraphSettings>
    {
        public GroupingOptions grouping = new GroupingOptions();
        public RelationOptions relations = new RelationOptions();
        public int thumbnailSize = 128;

        public void SaveSettings() => Save(true);
    }

    static class SceneGraphSettingsProvider
    {
        [SettingsProvider]
        static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Unity Scene Graph", SettingsScope.Project)
            {
                guiHandler = _ =>
                {
                    var settings = SceneGraphSettings.instance;
                    settings.hideFlags &= ~HideFlags.NotEditable;
                    var so = new SerializedObject(settings);
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(so.FindProperty("grouping"), true);
                    EditorGUILayout.PropertyField(so.FindProperty("relations"), true);
                    EditorGUILayout.PropertyField(so.FindProperty("thumbnailSize"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        settings.SaveSettings();
                    }
                }
            };
        }
    }
}
