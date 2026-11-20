using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class GoblinMover : MonoBehaviour
{
    public Transform target;                      
    [HideInInspector] public BoardGame board;    
    public float reachDistance = 0.15f;           

    NavMeshAgent agent;
    bool done;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (NavMesh.SamplePosition(target.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    void Update()
    {
        if (done || target == null) return;

        Vector3 a = transform.position;
        Vector3 b = target.position;
        a.y = 0; b.y = 0;                          

        if (Vector3.Distance(a, b) <= reachDistance)
        {
            done = true;
            if (board != null) board.OnGoblinReachedCrystal();
            Destroy(gameObject);
        }
    }
}