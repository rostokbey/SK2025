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
        private bool isMoving;
        private Vector3 moveTarget;
        private float moveSpeed;

        public void Setup(VFXType type, float dur)
        {
            Type = type;
            duration = dur;
            particles = GetComponentsInChildren<ParticleSystem>(true);
            vfxGraph = GetComponentInChildren<VisualEffect>(true);
        }

        // Child yapmaz, sadece pozisyonu takip eder. Hedef yok olursa efekt yerinde kalır.
        public void Follow(Transform target) { follow = target; }

        public void MoveTo(Vector3 target, float speed)
        {
            isMoving = true;
            moveTarget = target;
            moveSpeed = Mathf.Max(0.01f, speed);
        }

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
            if (follow != null)
                transform.position = follow.position;

            if (!isMoving) return;

            Vector3 direction = moveTarget - transform.position;
            if (direction.sqrMagnitude <= 0.05f)
            {
                transform.position = moveTarget;
                isMoving = false;
                return;
            }

            transform.position += direction.normalized * moveSpeed * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(direction.normalized);
        }
        

        private IEnumerator ReturnToPoolRoutine()
        {
            yield return new WaitForSeconds(duration);
            if (VFXManager.Instance != null) VFXManager.Instance.ReturnToPool(this);
        }

        public void StopImmediate()
        {
            follow = null;
            isMoving = false;
            if (particles != null)
                foreach (var ps in particles) ps.Stop();
            if (vfxGraph != null) vfxGraph.Stop();
        }
    }
}