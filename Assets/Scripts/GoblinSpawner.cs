using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class GoblinSpawner : MonoBehaviour
{
    public GameObject goblinPrefab;
    public Transform portal;
    public Transform crystal;
    public int goblinsPerWave = 5;
    public float timeBetweenGoblins = 1.5f;

    IEnumerator Start()
    {
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
            goblin.GetComponent<GoblinMover>().target = crystal;
        } 
    }
}
