using UnityEditor;

[CustomEditor(typeof(GameFootstepDatabase))]
public class GameFootstepDatabaseEditor : Editor
{
    [MenuItem("Assets/Game/Check All Footsteps")]
    public static void CheckAllFootsteps()
    {
        GameFootstepDatabase target;
        var guids = AssetDatabase.FindAssets("t:GameFootstepDatabase");
        string path;
        int numGUIDs = guids.Length;
        for (int i = 0; i < numGUIDs; ++i)
        {
            path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (EditorUtility.DisplayCancelableProgressBar("Check All Footstep", path, i * 1.0f / numGUIDs))
                break;

            target = AssetDatabase.LoadAssetAtPath<GameFootstepDatabase>(path);
            if (target == null)
                continue;

            foreach(var rig in target.data.rigs)
            {
                ref readonly var targetRig = ref target.database.data.rigs[rig.index];

                foreach (var foot in rig.foots)
                {
                    if(targetRig.BoneIndexOf(foot.bonePath) == -1)
                        UnityEngine.Debug.LogError(foot.bonePath, target);
                    
                    foreach(var tag in foot.tags)
                    {
                        if(tag.hybridAnimatorEventOverride == null && (tag.state > 4 || tag.state < 0))
                            UnityEngine.Debug.LogError(foot.bonePath, target);
                    }
                }
            }
        }

        EditorUtility.ClearProgressBar();
    }

    [MenuItem("Assets/Game/Rebuild All Footsteps")]
    public static void RebuildAllFootsteps()
    {
        GameFootstepDatabase target;
        var guids = AssetDatabase.FindAssets("t:GameFootstepDatabase");
        string path;
        int numGUIDs = guids.Length;
        for (int i = 0; i < numGUIDs; ++i)
        {
            path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (EditorUtility.DisplayCancelableProgressBar("Rebuild All Footstep", path, i * 1.0f / numGUIDs))
                break;

            target = AssetDatabase.LoadAssetAtPath<GameFootstepDatabase>(path);
            if (target == null)
                continue;

            target.EditorMaskDirty();
        }

        EditorUtility.ClearProgressBar();
    }

}
