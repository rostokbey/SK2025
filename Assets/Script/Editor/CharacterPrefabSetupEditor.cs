using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CharacterPrefabSetup))]
public class CharacterPrefabSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CharacterPrefabSetup setup = (CharacterPrefabSetup)target;

        GUILayout.Space(10);
        if (GUILayout.Button("🔧 Fix Character Prefab Setup"))
        {
            setup.FixCharacterPrefabs();
        }
    }
}
