
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class MagicHitVFX : MonoBehaviour
{
    [Header("Playback")]
    public bool playOnAwake = true;
    public bool destroyAfterPlay = false;
    [Min(0.1f)] public float duration = 0.5f;

    [Header("Colors")]
    public Color centerColor = new Color(0.45f, 0.9f, 1f, 1f);
    public Color ringColor = new Color(0.65f, 0.25f, 1f, 1f);
    public Color sparkColorA = Color.cyan;
    public Color sparkColorB = Color.magenta;

    [Header("Effect Size")]
    [Min(0.1f)] public float ringScale = 3.8f;
    [Min(0.1f)] public float centerScale = 1.8f;
    [Range(4, 128)] public int ringSegments = 48;
    [Range(1, 100)] public int particleCount = 24;

    private Transform centerTransform;
    private Transform ringTransform;
    private Renderer centerRenderer;
    private Renderer ringRenderer;
    private Material centerMaterial;
    private Material ringMaterial;
    private ParticleSystem sparks;
    private Mesh centerMesh;
    private Mesh ringMesh;
    private Coroutine playback;
    private float elapsed;
    private bool built;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        EnsureBuilt();
    }

    private void Start()
    {
        if (playOnAwake)
            Play();
    }

    private void EnsureBuilt()
    {
        if (built) return;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError(
                "MagicHitVFX: Sprites/Default shader bulunamadı.", this);
            enabled = false;
            return;
        }

        centerTransform = CreateChild("CenterBurst");
        centerMesh = CreateDiscMesh();
        centerRenderer = CreateMeshRenderer(
            centerTransform, centerMesh, shader, centerColor,
            out centerMaterial);

        ringTransform = CreateChild("EnergyRing");
        ringMesh = CreateRingMesh();
        ringRenderer = CreateMeshRenderer(
            ringTransform, ringMesh, shader, ringColor,
            out ringMaterial);

        sparks = CreateSparks(shader);

        centerTransform.localScale = Vector3.one * 0.01f;
        ringTransform.localScale = Vector3.one * 0.01f;
        SetAlpha(centerMaterial, 0f);
        SetAlpha(ringMaterial, 0f);

        built = true;
    }

    private Transform CreateChild(string childName)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        return child.transform;
    }

    private Renderer CreateMeshRenderer(
        Transform target, Mesh mesh, Shader shader, Color color,
        out Material material)
    {
        MeshFilter filter = target.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer =
            target.gameObject.AddComponent<MeshRenderer>();

        material = new Material(shader);
        material.name = target.name + "_RuntimeMaterial";
        material.color = color;

        renderer.sharedMaterial = material;
        renderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return renderer;
    }

    private Mesh CreateDiscMesh()
    {
        int segments = Mathf.Max(4, ringSegments);
        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[i + 1] = new Vector3(
                Mathf.Cos(angle) * 0.5f,
                Mathf.Sin(angle) * 0.5f,
                0f);

            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = (i + 1) % segments + 1;
        }

        Mesh mesh = new Mesh { name = "MagicHit_CenterMesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh CreateRingMesh()
    {
        int segments = Mathf.Max(4, ringSegments);
        Vector3[] vertices = new Vector3[segments * 2];
        int[] triangles = new int[segments * 6];

        const float outer = 0.5f;
        const float inner = 0.39f;

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float x = Mathf.Cos(angle);
            float y = Mathf.Sin(angle);

            vertices[i * 2] =
                new Vector3(x * outer, y * outer, 0f);
            vertices[i * 2 + 1] =
                new Vector3(x * inner, y * inner, 0f);

            int v = i * 2;
            int t = i * 6;
            int next = (i + 1) % segments * 2;

            triangles[t] = v;
            triangles[t + 1] = next;
            triangles[t + 2] = v + 1;

            triangles[t + 3] = v + 1;
            triangles[t + 4] = next;
            triangles[t + 5] = next + 1;
        }

        Mesh mesh = new Mesh { name = "MagicHit_RingMesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private ParticleSystem CreateSparks(Shader shader)
    {
        GameObject go = new GameObject("FlyingSparks");
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ps.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        var main = ps.main;
        main.duration = duration;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime =
            new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed =
            new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize =
            new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor =
            new ParticleSystem.MinMaxGradient(sparkColorA, sparkColorB);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 100;
        

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)particleCount)
        });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;

        var colorLifetime = ps.colorOverLifetime;
        colorLifetime.enabled = true;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.cyan, 0.4f),
                new GradientColorKey(Color.magenta, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        colorLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = new Material(shader);
        renderer.sharedMaterial.name = "MagicHit_Sparks_RuntimeMaterial";
        renderer.sharedMaterial.color = Color.white;
        renderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    public void Play()
    {
        EnsureBuilt();
        if (!built) return;

        if (playback != null)
            StopCoroutine(playback);

        elapsed = 0f;
        centerTransform.localScale = Vector3.one * 0.01f;
        ringTransform.localScale = Vector3.one * 0.01f;
        SetAlpha(centerMaterial, 1f);
        SetAlpha(ringMaterial, 1f);

        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        sparks.Play(true);

        playback = StartCoroutine(Animate());
    }

    public void PlayAt(Vector3 worldPosition)
    {
        transform.position = worldPosition;
        Play();
    }

    private IEnumerator Animate()
    {
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            centerTransform.localScale =
                Vector3.one * Mathf.Lerp(0.2f, centerScale, t);
            ringTransform.localScale =
                Vector3.one * Mathf.Lerp(0.2f, ringScale, t);

            SetAlpha(centerMaterial, 1f - t);
            SetAlpha(ringMaterial, 1f - t);

            yield return null;
        }

        SetAlpha(centerMaterial, 0f);
        SetAlpha(ringMaterial, 0f);
        playback = null;

        if (destroyAfterPlay)
            Destroy(gameObject);
    }

    private void SetAlpha(Material material, float alpha)
    {
        if (material == null) return;

        Color c = material.GetColor(ColorId);
        c.a = alpha;
        material.SetColor(ColorId, c);
    }

    private void OnDestroy()
    {
        if (centerMesh != null) Destroy(centerMesh);
        if (ringMesh != null) Destroy(ringMesh);
        if (centerMaterial != null) Destroy(centerMaterial);
        if (ringMaterial != null) Destroy(ringMaterial);

        if (sparks != null)
        {
            var r = sparks.GetComponent<ParticleSystemRenderer>();
            if (r != null && r.sharedMaterial != null)
                Destroy(r.sharedMaterial);
        }
    }
}
