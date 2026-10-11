using UnityEngine;

namespace SK2025.VFX
{
    [CreateAssetMenu(fileName = "VFXData_", menuName = "SK2025/VFX Data")]
    public class VFXData : ScriptableObject
    {
        public VFXType type;
        public GameObject prefab;
        public float duration = 2f;
        public int preloadCount = 5;
        public bool usePooling = true;
    }
}