using UnityEngine;

public class SimpleHitFlash : MonoBehaviour
{
    public Renderer[] renderers;      // MeshRenderer veya SkinnedMeshRenderer
    public Color flashColor = Color.white;
    public float duration = 0.08f;

    Color[] _origColors;
    Material[] _mats;
    float _t;
    bool _playing;

    void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();
        _mats = System.Array.Empty<Material>();
        var mats = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers) mats.AddRange(r.materials); // instance
        _mats = mats.ToArray();
        _origColors = new Color[_mats.Length];
        for (int i = 0; i < _mats.Length; i++)
            _origColors[i] = _mats[i].HasProperty("_Color") ? _mats[i].color : Color.white;
    }

    public void Play()
    {
        if (_mats.Length == 0) return;
        _t = 0; _playing = true;
    }

    void Update()
    {
        if (!_playing) return;
        _t += Time.deltaTime / duration;
        float f = 1f - Mathf.Abs(_t * 2f - 1f); // üçgen eðri
        for (int i = 0; i < _mats.Length; i++)
            if (_mats[i].HasProperty("_Color"))
                _mats[i].color = Color.Lerp(_origColors[i], flashColor, f);
        if (_t >= 1f)
        {
            for (int i = 0; i < _mats.Length; i++)
                if (_mats[i].HasProperty("_Color")) _mats[i].color = _origColors[i];
            _playing = false;
        }
    }
}
