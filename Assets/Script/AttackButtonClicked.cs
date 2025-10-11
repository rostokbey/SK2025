using UnityEngine;

public class AttackButtonClick : MonoBehaviour
{
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    public void OnAttackButtonClick()
    {
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }
    }
}
