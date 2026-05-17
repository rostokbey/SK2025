using System.Collections.Generic;
using UnityEngine;

public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance;

    [Header("Default Settings")]
    public float defaultDestroyTime = 2f;
    public Vector3 defaultOffset = new Vector3(0f, 1.2f, 0f);
    public float testScale = 5f;

    private readonly Dictionary<string, GameObject> vfxCache = new Dictionary<string, GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void PlayVFX(string abilityName, Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("VFX hedefi null.");
            return;
        }

        PlayVFX(abilityName, target.position + defaultOffset);
    }

    public void PlayVFX(string abilityName, Vector3 position)
    {
        if (string.IsNullOrWhiteSpace(abilityName))
        {
            Debug.LogWarning("Ability name boş geldi.");
            return;
        }

        GameObject prefab = GetVFXPrefab(abilityName);

        if (prefab == null)
        {
            Debug.LogWarning($"VFX prefab bulunamadı: Resources/VFX/{abilityName}");
            return;
        }

        GameObject effect = Instantiate(prefab, position, Quaternion.identity);
        effect.transform.localScale = Vector3.one * testScale;

        Debug.Log($"✅ VFX SPAWN EDİLDİ: {abilityName} | Pos: {position}");

        // =========================
        // SFX ÇAL
        // =========================
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(abilityName);
        }

        Destroy(effect, defaultDestroyTime);
    }

    private GameObject GetVFXPrefab(string abilityName)
    {
        if (vfxCache.TryGetValue(abilityName, out GameObject cachedPrefab))
        {
            return cachedPrefab;
        }

        GameObject prefab = Resources.Load<GameObject>($"VFX/{abilityName}");

        if (prefab != null)
        {
            vfxCache.Add(abilityName, prefab);
        }

        return prefab;
    }
}