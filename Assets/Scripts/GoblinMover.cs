using UnityEngine;
using UnityEngine.AI;

public class GoblinMover : MonoBehaviour
{
    public Transform target;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var agent = GetComponent<NavMeshAgent>();

        if (NavMesh.SamplePosition(target.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }
}
