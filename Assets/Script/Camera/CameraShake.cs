using UnityEngine;

public class CameraShake : MonoBehaviour
{
    Vector3 basePos;
    void Awake() { basePos = transform.localPosition; }
    public void Shake(float amp = 0.2f, float time = 0.2f)
    {
        StopAllCoroutines();
        StartCoroutine(Co(amp, time));
    }
    System.Collections.IEnumerator Co(float a, float t)
    {
        float e = 0;
        while (e < t)
        {
            e += Time.deltaTime;
            float f = 1f - (e / t);
            transform.localPosition = basePos + Random.insideUnitSphere * a * f;
            yield return null;
        }
        transform.localPosition = basePos;
    }
}
