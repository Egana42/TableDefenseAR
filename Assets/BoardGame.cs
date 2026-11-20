using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class BoardGame : MonoBehaviour
{
    public GameObject goblinPrefab;
    public Transform portal;
    public Transform crystal;
    public int goblinsPerWave = 5;
    public float timeBetweenGoblins = 1.5f;

    IEnumerator Start()
    {
        var spawner = GameObject.Find("Object Spawner");
        if (spawner != null)
        {
            foreach (var mb in spawner.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "ARInteractorSpawnTrigger")
                    mb.enabled = false;
        }

        GetComponent<NavMeshSurface>().BuildNavMesh();
        yield return new WaitForSeconds(1f);

        for (int i = 0; i < goblinsPerWave; i++)
        {
            Spawn();
            yield return new WaitForSeconds(timeBetweenGoblins);
        }
    }

    void Spawn()
    {
        if (NavMesh.SamplePosition(portal.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas))
        {
            GameObject goblin = Instantiate(goblinPrefab, hit.position, Quaternion.identity);
            goblin.GetComponent<NavMeshAgent>().Warp(hit.position);
            goblin.GetComponent<GoblinMover>().target = crystal;
        }
    }
}