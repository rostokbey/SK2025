using UnityEngine;

public class YourAnimator : MonoBehaviour
{
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    public void PlayAttackAnimation()
    {
        animator.SetTrigger("Attack");
    }
}
