using System.Collections.Generic;
using UnityEngine;

namespace SK2025.VFX
{
    public class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        [SerializeField] private List<VFXData> database = new List<VFXData>();
        [SerializeField] private Transform poolParent;
        [SerializeField] private bool verboseLog = true;

        // Enum bazlı havuzlar
        private readonly Dictionary<VFXType, Queue<VFXPlayer>> pools = new Dictionary<VFXType, Queue<VFXPlayer>>();
        private readonly Dictionary<VFXType, VFXData> dataLookup = new Dictionary<VFXType, VFXData>();

        // 311 Karakter için dinamik Prefab ve String havuzları
        private readonly Dictionary<GameObject, Queue<VFXPlayer>> prefabPools = new Dictionary<GameObject, Queue<VFXPlayer>>();

        private readonly Dictionary<string, GameObject> loadedResources = new Dictionary<string, GameObject>();
        private readonly Dictionary<VFXPlayer, GameObject> playerToPrefabKey = new Dictionary<VFXPlayer, GameObject>();


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            new GameObject("VFXManager (auto)").AddComponent<VFXManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Instance.AddToDatabase(database);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            if (poolParent == null) poolParent = transform;

            AddToDatabase(database);
            AddToDatabase(Resources.LoadAll<VFXData>("VFX"));
        }

        private void AddToDatabase(IEnumerable<VFXData> list)
        {
            if (list == null) return;
            foreach (var data in list)
            {
                if (data == null || data.prefab == null || data.type == VFXType.None) continue;
                if (dataLookup.ContainsKey(data.type)) continue;

                dataLookup[data.type] = data;
                var queue = new Queue<VFXPlayer>();
                pools[data.type] = queue;
                for (int i = 0; i < data.preloadCount; i++)
                {
                    var p = CreateNew(data.prefab, data.type, data.duration);
                    p.gameObject.SetActive(false);
                    queue.Enqueue(p);
                }
            }
        }

        private VFXPlayer CreateNew(GameObject prefab, VFXType type = VFXType.None, float duration = 2f)
        {
            var go = Instantiate(prefab, poolParent);
            var player = go.GetComponent<VFXPlayer>();
            if (player == null) player = go.AddComponent<VFXPlayer>();
            player.Setup(type, duration);
            return player;
        }

        private static Quaternion Fix(Quaternion q)
        {
            return (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) ? Quaternion.identity : q;
        }

        private static bool TryResolve(string name, out VFXType type)
        {
            type = VFXType.None;
            if (string.IsNullOrEmpty(name)) return false;
            if (!System.Enum.TryParse(name, true, out type)) return false;
            return type != VFXType.None && System.Enum.IsDefined(typeof(VFXType), type);
        }

        // ─────────────────────────────────────────────────────────────
        // 1) 311 KARAKTER İÇİN PREFAB BAZLI ÇAĞRILAR (EN PERFORMANSLI)
        // ─────────────────────────────────────────────────────────────

        public VFXPlayer Play(
            GameObject vfxPrefab,
            Vector3 position,
            Quaternion rotation = default,
            float duration = 2f)
        {
            if (vfxPrefab == null)
                return null;

            duration = Mathf.Max(0.01f, duration);

            if (!prefabPools.TryGetValue(vfxPrefab, out var queue))
            {
                queue = new Queue<VFXPlayer>();
                prefabPools[vfxPrefab] = queue;
            }

            VFXPlayer player = null;

            // Yok edilmiş havuz elemanlarını atla.
            while (queue.Count > 0 && player == null)
                player = queue.Dequeue();

            if (player == null)
            {
                player = CreateNew(vfxPrefab, VFXType.None, duration);
            }
            else
            {
                // Havuzdan tekrar kullanıldığında yeni süreyi uygula.
                player.Setup(VFXType.None, duration);
            }

            playerToPrefabKey[player] = vfxPrefab;

            player.Follow(null);
            player.transform.SetParent(poolParent, false);
            player.transform.localScale = vfxPrefab.transform.localScale;
            player.transform.SetPositionAndRotation(position, Fix(rotation));

            player.Play();
            return player;
        }

        public VFXPlayer Play(GameObject vfxPrefab, Transform target, float duration = 2f)
        {
            if (target == null || vfxPrefab == null) return null;
            var p = Play(vfxPrefab, target.position, target.rotation, duration);
            if (p != null) p.Follow(target);
            return p;
        }

        public VFXPlayer PlayProjectile(
            string prefabName,
            Vector3 startPosition,
            Vector3 targetPosition,
            float speed = 25f)
        {
            GameObject prefab = GetResourcePrefab(prefabName);

            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[VFX] Projectile prefab bulunamadı: " +
                    $"Assets/Resources/VFX/{prefabName}.prefab");
                return null;
            }

            float distance = Vector3.Distance(startPosition, targetPosition);

            // Efektin hedefe varmasına yetecek süre.
            float duration = Mathf.Clamp((distance / Mathf.Max(speed, 0.01f)) + 0.7f, 0.8f, 5f);

            Vector3 direction = targetPosition - startPosition;
            Quaternion rotation = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;

            VFXPlayer player = Play(prefab, startPosition, rotation, duration);

            if (player != null)
                player.MoveTo(targetPosition, speed);

            return player;
        }

        // ─────────────────────────────────────────────────────────────
        // 2) ENUM BAZLI ÇAĞRILAR (GENEL EFEKTLER)
        // ─────────────────────────────────────────────────────────────

        public VFXPlayer Play(VFXType type, Vector3 position, Quaternion rotation = default)
        {
            if (!dataLookup.ContainsKey(type))
            {
                PlayFallback(type.ToString(), position);
                return null;
            }

            var queue = pools[type];
            VFXPlayer player = null;
            while (queue.Count > 0 && player == null) player = queue.Dequeue();
            if (player == null) player = CreateNew(dataLookup[type].prefab, type, dataLookup[type].duration);

            player.transform.SetParent(poolParent, false);
            player.transform.SetPositionAndRotation(position, Fix(rotation));
            player.Play();
            return player;
        }

        public VFXPlayer Play(VFXType type, Transform target)
        {
            if (target == null) return null;
            var p = Play(type, target.position, target.rotation);
            if (p != null) p.Follow(target);
            return p;
        }

        // ─────────────────────────────────────────────────────────────
        // 3) BATTLEMANAGER UYUMLULUKLARI (STRING / ENUM / TRANSFORM)
        // ─────────────────────────────────────────────────────────────

        public VFXPlayer PlayVFX(VFXType type, Vector3 position, Quaternion rotation = default)
            => Play(type, position, rotation);

        public VFXPlayer PlayVFX(VFXType type, Transform target)
            => Play(type, target);

        public VFXPlayer PlayVFX(string nameOrPath, Vector3 position, Quaternion rotation = default)
        {
            // 1. Enum mu diye bak (Hit, Explosion vb.)
            if (TryResolve(nameOrPath, out var type)) return Play(type, position, rotation);

            // 2. Resources'ta bu isimde prefab var mı diye bak (Örn: Resources/VFX/Karakter_1)
            var prefab = GetResourcePrefab(nameOrPath);
            if (prefab != null) return Play(prefab, position, rotation);

            if (verboseLog) Debug.Log($"[VFX] PlayVFX('{nameOrPath}') @ {position}");
            PlayFallback(nameOrPath, position);
            return null;
        }

        public VFXPlayer PlayVFX(string nameOrPath, Transform target)
        {
            if (target == null) return null;
            if (TryResolve(nameOrPath, out var type)) return Play(type, target);

            var prefab = GetResourcePrefab(nameOrPath);
            if (prefab != null) return Play(prefab, target);

            if (verboseLog) Debug.Log($"[VFX] PlayVFX('{nameOrPath}') @ {target.name}");
            PlayFallback(nameOrPath, target.position);
            return null;
        }

        private GameObject GetResourcePrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (loadedResources.TryGetValue(name, out var p)) return p;

            // 1. Resources/VFX/ içinde ara
            var loaded = Resources.Load<GameObject>($"VFX/{name}");
            if (loaded == null)
            {
                // 2. Doğrudan Resources/ içinde ara
                loaded = Resources.Load<GameObject>(name);
            }

            if (loaded != null)
            {
                Debug.Log($"<color=green>[VFX] PREFAB BULUNDU:</color> Resources/VFX/{name}");
            }
            else
            {
                Debug.LogError($"<color=red>[VFX] PREFAB BULUNAMADI!</color> Aranan yol: 'Assets/Resources/VFX/{name}.prefab'");
            }

            loadedResources[name] = loaded;
            return loaded;
        }

        // ─────────────────────────────────────────────────────────────
        // KARAKTERE ÖZEL EFEKT
        // Önce "KarakterAdi_Eylem" arar (örn: Ates_Ciragi3D_Skill)
        // Yoksa genel efekti oynatır (örn: Skill)
        // ─────────────────────────────────────────────────────────────

        public VFXPlayer PlayCharacterVFX(string characterName, string action, Transform target)
        {
            if (target == null)
            {
                Debug.LogError("[VFX] Hedef Transform NULL!");
                return null;
            }
            return PlayCharacterVFX(characterName, action, target.position);
        }

        public VFXPlayer PlayCharacterVFX(string characterName, string action, Vector3 position)
        {
            string cleanChar = CleanName(characterName);
            string targetPrefabName = cleanChar + "_" + action;

            Debug.Log($"[VFX] Efekt aranıyor: Karakter='{cleanChar}', Eylem='{action}' -> Dosya='{targetPrefabName}' | Pos: {position}");

            var prefab = GetResourcePrefab(targetPrefabName);
            if (prefab != null)
            {
                return Play(prefab, position);
            }

            Debug.LogWarning($"[VFX] '{targetPrefabName}' bulunamadığı için genel '{action}' deneniyor.");
            return PlayVFX(action, position);
        }

        private static string CleanName(string n)
        {
            return string.IsNullOrEmpty(n) ? "" : n.Replace("(Clone)", "").Trim();
        }

        // ─────────────────────────────────────────────────────────────
        // 4) POOL GERİ DÖNÜŞÜ
        // ─────────────────────────────────────────────────────────────

        public void ReturnToPool(VFXPlayer player)
        {
            // Aynı pasif nesneyi havuza ikinci kez ekleme.
            if (player == null || !player.gameObject.activeSelf)
                return;

            player.StopImmediate();
            player.gameObject.SetActive(false);
            player.transform.SetParent(poolParent, false);

            // Karaktere özel prefab havuzu.
            if (playerToPrefabKey.TryGetValue(player, out var prefab) &&
                prefab != null &&
                prefabPools.TryGetValue(prefab, out var prefabQueue))
            {
                prefabQueue.Enqueue(player);
                return;
            }

            // Genel efektlerin enum havuzu.
            if (player.Type != VFXType.None &&
                pools.TryGetValue(player.Type, out var enumQueue))
            {
                enumQueue.Enqueue(player);
                return;
            }

            // Herhangi bir havuza ait değilse pasif nesne biriktirme.
            playerToPrefabKey.Remove(player);
            Destroy(player.gameObject);
        }

        // ───────── GEÇİCİ (FALLBACK) EFEKT ─────────
        private static Material fallbackMat;
        private static Material GetFallbackMaterial()
        {
            if (fallbackMat != null) return fallbackMat;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Particles/Standard Unlit");

            fallbackMat = new Material(shader);
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            tex.Apply();
            fallbackMat.mainTexture = tex;
            return fallbackMat;
        }

        private void PlayFallback(string label, Vector3 position)
        {
            if (verboseLog)
                Debug.LogWarning($"[VFX] '{label}' prefabı bulunamadı, geçici renkli efekt oynatılıyor @ {position}");

            var go = new GameObject("VFX_Fallback_" + label);
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Color c = Color.HSVToRGB((Mathf.Abs(label.GetHashCode()) % 100) / 100f, 0.8f, 1f);

            var main = ps.main;
            main.duration = 2f;
            main.loop = false;
            main.startLifetime = 1.5f;
            main.startSpeed = 15f;
            main.startSize = 8f;
            main.startColor = c;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.gravityModifier = -0.5f;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = GetFallbackMaterial();
            r.sortingOrder = 1000;

            ps.Play();
            Destroy(go, 3f);
        }

#if UNITY_EDITOR
        private bool panelOpen;
        private static Vector3 TestPosition()
        {
            var cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
            return cam != null ? cam.transform.position + cam.transform.forward * 5f : Vector3.zero;
        }

        private void OnGUI()
        {
            float h = Mathf.Max(30f, Screen.height * 0.05f);
            float w = Mathf.Max(110f, Screen.width * 0.18f);
            float x = Screen.width - w - 5f;
            GUI.skin.button.fontSize = Mathf.RoundToInt(h * 0.4f);

            if (GUI.Button(new Rect(x, 5, w, h), panelOpen ? "VFX [-]" : "VFX [+]"))
                panelOpen = !panelOpen;
            if (!panelOpen) return;

            float y = 10f + h;
            foreach (VFXType t in System.Enum.GetValues(typeof(VFXType)))
            {
                if (t == VFXType.None) continue;
                if (GUI.Button(new Rect(x, y, w, h), t.ToString())) Play(t, TestPosition());
                y += h + 5f;
            }
        }
#endif
    }
}