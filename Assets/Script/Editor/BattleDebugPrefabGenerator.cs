#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

/// <summary>
/// Model olmadan ağ savaşını test etmek için gerekli kapsül prefabı otomatik üretir.
/// Dosya zaten varsa kullanıcı değişikliklerine dokunmaz.
/// </summary>
[InitializeOnLoad]
public static class BattleDebugPrefabGenerator
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string BattleFolder = "Assets/Resources/Battle";
    private const string PrefabPath = BattleFolder + "/DebugBattleCharacter.prefab";
    private const string MaterialPath = BattleFolder + "/DebugBattleCharacter.mat";

    static BattleDebugPrefabGenerator()
    {
        EditorApplication.delayCall += EnsurePrefabExists;
    }

    [MenuItem("Tools/SK2025/Geçici Savaş Karakterini Yeniden Oluştur")]
    public static void RecreatePrefab()
    {
        AssetDatabase.DeleteAsset(PrefabPath);
        EnsurePrefabExists();
    }

    private static void EnsurePrefabExists()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
        {
            RegisterInNetworkPrefabLists(existing);
            return;
        }

        EnsureFolder(ResourcesFolder, "Assets", "Resources");
        EnsureFolder(BattleFolder, ResourcesFolder, "Battle");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader) { color = Color.white };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        GameObject root = new GameObject("DebugBattleCharacter");
        root.AddComponent<NetworkObject>();
        root.AddComponent<Character>();
        root.AddComponent<HealthSystem>();
        root.AddComponent<DebugBattleCharacterVisual>();

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = Vector3.up;
        body.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);

        Renderer renderer = body.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        RegisterInNetworkPrefabLists(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DEBUG PREFAB] Geçici kapsül karakter oluşturuldu: " + PrefabPath);
    }

    private static void RegisterInNetworkPrefabLists(GameObject prefab)
    {
        string[] guids = AssetDatabase.FindAssets("t:NetworkPrefabsList");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (list == null || list.PrefabList.Any(entry => entry.Prefab == prefab))
                continue;

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            Debug.Log("[DEBUG PREFAB] NetworkPrefabsList'e eklendi: " + path);
        }
    }

    private static void EnsureFolder(string fullPath, string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(fullPath))
            AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
