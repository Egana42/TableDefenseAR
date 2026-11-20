using UnityEngine;

public class Tower : MonoBehaviour
{
    public float range = 0.35f;     
    public float cooldown = 1f;    
    public float damage = 1f;
    float timer;

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        GoblinMover best = null;
        float bestDist = range;
        foreach (var g in FindObjectsByType<GoblinMover>(FindObjectsSortMode.None))
        {
            if (g.IsDone) continue;
            float d = Vector3.Distance(transform.position, g.transform.position);
            if (d <= bestDist) { best = g; bestDist = d; }
        }

        if (best != null)
        {
            Shoot(best);
            timer = cooldown;
        }
    }

    void Shoot(GoblinMover target)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Projectile";
        Destroy(ball.GetComponent<Collider>());     // sin collider para no estorbar
        ball.transform.localScale = Vector3.one * 0.02f;
        ball.transform.position = transform.position + Vector3.up * 0.06f;
        ball.GetComponent<Renderer>().material.color = new Color(1f, 0.5f, 0f);

        var p = ball.AddComponent<Projectile>();
        p.target = target;
        p.damage = damage;
    }
}