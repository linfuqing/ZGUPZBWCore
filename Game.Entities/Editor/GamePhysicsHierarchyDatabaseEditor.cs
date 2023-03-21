using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GamePhysicsHierarchyDatabase))]
public class GamePhysicsHierarchyDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var target = (GamePhysicsHierarchyDatabase)base.target;

        bool isRebuild = false;

        EditorGUI.BeginChangeCheck();
        target.root = EditorGUILayout.ObjectField("Root", target.root, typeof(Transform), true) as Transform;
        if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
        {
            if (target.root != null)
            {
                target.Create();

                if (PrefabUtility.GetPrefabInstanceStatus(target.root) == PrefabInstanceStatus.Connected)
                    target.root = PrefabUtility.GetCorrespondingObjectFromSource(target.root);
            }

            isRebuild = true;
        }

        isRebuild = GUILayout.Button("Reset") || isRebuild;
        if (isRebuild)
            target.EditorMaskDirty();

        base.OnInspectorGUI();
    }
}
