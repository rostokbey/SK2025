using UnityEngine;

/// <summary>Geçici kapsülü tarafa ve HP durumuna göre renklendirir.</summary>
public class DebugBattleCharacterVisual : MonoBehaviour
{
    [SerializeField] private Renderer bodyRenderer;

    private Character character;
    private HealthSystem health;
    private Material materialInstance;
    private bool lastSide;
    private bool initialized;
    private int lastHp = int.MinValue;

    private void Awake()
    {
        character = GetComponent<Character>();
        health = GetComponent<HealthSystem>();

        if (bodyRenderer == null)
            bodyRenderer = GetComponentInChildren<Renderer>();

        if (bodyRenderer != null)
            materialInstance = bodyRenderer.material;
    }

    private void Update()
    {
        if (character == null || health == null || materialInstance == null)
            return;

        bool side = character.isPlayer;
        int hp = health.currentHP.Value;
        if (initialized && side == lastSide && hp == lastHp)
            return;

        initialized = true;
        lastSide = side;
        lastHp = hp;

        Color aliveColor = side
            ? new Color(0.15f, 0.85f, 0.30f)
            : new Color(0.95f, 0.20f, 0.20f);

        materialInstance.color = hp > 0 ? aliveColor : Color.gray;
    }
}
