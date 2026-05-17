#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.IO;

public class CharacterPrefabSetup : MonoBehaviour
{
    [Header("Prefab Setup Options")]
    public string sourceFolder = "Assets/Resources/Prefabs3D";
    public bool applyScaleFix = true;

    public void FixCharacterPrefabs()
    {
        if (!Directory.Exists(sourceFolder))
        {
            Debug.LogError($"❌ Folder not found: {sourceFolder}");
            return;
        }

        string[] prefabPaths = Directory.GetFiles(sourceFolder, "*.prefab", SearchOption.AllDirectories);

        foreach (string path in prefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
                continue;

            if (prefab.GetComponent<Animator>() == null)
                prefab.AddComponent<Animator>();

            if (prefab.GetComponent<CapsuleCollider>() == null)
                prefab.AddComponent<CapsuleCollider>();

            if (applyScaleFix)
                prefab.transform.localScale = Vector3.one;

            EditorUtility.SetDirty(prefab);

            Debug.Log($"✅ Fixed prefab: {Path.GetFileName(path)}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("✅ Character prefab setup completed.");
    }
}

#endif