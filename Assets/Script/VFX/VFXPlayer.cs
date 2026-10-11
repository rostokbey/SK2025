using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

namespace SK2025.VFX
{
    public class VFXPlayer : MonoBehaviour
    {
        public VFXType Type { get; private set; }
        private ParticleSystem[] particles;
        private VisualEffect vfxGraph;
        private float duration;
        private Coroutine returnRoutine;
        private Transform follow;

        public void Setup(VFXType type, float dur)
        {
            Type = type;
            duration = dur;
            particles = GetComponentsInChildren<ParticleSystem>(true);
            vfxGraph = GetComponentInChildren<VisualEffect>(true);
        }

        // Child yapmaz, sadece pozisyonu takip eder. Hedef yok olursa efekt yerinde kalır.
        public void Follow(Transform target) { follow = target; }

        public void Play()
        {
            gameObject.SetActive(true);
            if (returnRoutine != null) StopCoroutine(returnRoutine);

            if (particles != null)
                foreach (var ps in particles) { ps.Clear(); ps.Play(); }

            if (vfxGraph != null) { vfxGraph.Reinit(); vfxGraph.Play(); }

            returnRoutine = StartCoroutine(ReturnToPoolRoutine());
        }

        private void LateUpdate()
        {
            if (follow != null) transform.position = follow.position;
        }

        private IEnumerator ReturnToPoolRoutine()
        {
            yield return new WaitForSeconds(duration);
            if (VFXManager.Instance != null) VFXManager.Instance.ReturnToPool(this);
        }

        public void StopImmediate()
        {
            follow = null;
            if (particles != null)
                foreach (var ps in particles) ps.Stop();
            if (vfxGraph != null) vfxGraph.Stop();
        }
    }
}