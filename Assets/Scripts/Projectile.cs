using UnityEngine;

public class Projectile : MonoBehaviour
{
    public GoblinMover target;
    public float damage = 1f;
    public float speed = 1.2f;

    void Update()
    {
        if (target == null) { Destroy(gameObject); return; }

        Vector3 dest = target.transform.position + Vector3.up * 0.02f;
        transform.position = Vector3.MoveTowards(transform.position, dest, speed * Time.deltaTime);

        if (Vector3.Distance(transform.position, dest) < 0.02f)
        {
            target.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}