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
    public int crystalMaxHealth = 10;

    enum State { Ready, Playing, Won, Lost }
    State state = State.Ready;
    int crystalHealth;
    int goblinsSpawned;
    int goblinsAlive;

    void Start()
    {
        crystalHealth = crystalMaxHealth;

   
        var spawner = GameObject.Find("Object Spawner");
        if (spawner != null)
        {
            foreach (var mb in spawner.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "ARInteractorSpawnTrigger")
                    mb.enabled = false;
        }

        
        GetComponent<NavMeshSurface>().BuildNavMesh();
    }

    void StartWave()
    {
        state = State.Playing;
        StartCoroutine(SpawnWave());
    }

    IEnumerator SpawnWave()
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
            goblin.GetComponent<NavMeshAgent>().Warp(hit.position);
            var mover = goblin.GetComponent<GoblinMover>();
            mover.target = crystal;
            mover.board = this;
            goblinsSpawned++;
            goblinsAlive++;
        }
    }

   
    public void OnGoblinReachedCrystal()
    {
        goblinsAlive--;
        crystalHealth--;
        if (crystalHealth <= 0)
        {
            crystalHealth = 0;
            EndGame(State.Lost);
        }
        else CheckWin();
    }

    void CheckWin()
    {
        if (state == State.Playing && goblinsSpawned >= goblinsPerWave && goblinsAlive <= 0)
            EndGame(State.Won);
    }

    void EndGame(State result)
    {
        state = result;
        if (result == State.Lost) crystal.gameObject.SetActive(false);
        StopAllCoroutines();
        foreach (var g in FindObjectsByType<GoblinMover>(FindObjectsSortMode.None))
            Destroy(g.gameObject);
    }

    void ResetGame()
    {
        StopAllCoroutines();
        crystal.gameObject.SetActive(true);
        foreach (var g in FindObjectsByType<GoblinMover>(FindObjectsSortMode.None))
            Destroy(g.gameObject);
        crystalHealth = crystalMaxHealth;
        goblinsSpawned = 0;
        goblinsAlive = 0;
        state = State.Ready;
    }

    void DrawCrystalBar(float s)
    {
        var cam = Camera.main;
        if (cam == null || crystal == null) return;
        if (!crystal.gameObject.activeSelf) return;

        Vector3 p = cam.WorldToScreenPoint(crystal.position + Vector3.up * 0.12f);
        if (p.z <= 0) return;                         // el cristal está detrás de la cámara

        float w = 220 * s, h = 26 * s;
        float x = p.x - w / 2f;
        float y = Screen.height - p.y - h / 2f;       // en OnGUI el eje Y va al revés

        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(x - 3 * s, y - 3 * s, w + 6 * s, h + 6 * s), Texture2D.whiteTexture);

        float t = (float)crystalHealth / crystalMaxHealth;
        GUI.color = Color.Lerp(Color.red, Color.green, t);
        GUI.DrawTexture(new Rect(x, y, w * t, h), Texture2D.whiteTexture);

        GUI.color = Color.white;
    }

    void OnGUI()
    {
        float s = Mathf.Min(Screen.width, Screen.height) / 1080f;
        DrawCrystalBar(s);
        var label = new GUIStyle(GUI.skin.label) { fontSize = (int)(52 * s), fontStyle = FontStyle.Bold };
        label.normal.textColor = Color.white;
        var button = new GUIStyle(GUI.skin.button) { fontSize = (int)(56 * s) };

        GUI.Box(new Rect(10 * s, 10 * s, 620 * s, 90 * s), "");
        GUI.Label(new Rect(30 * s, 25 * s, 600 * s, 70 * s), $"Crystal: {crystalHealth}/{crystalMaxHealth}", label);

        float bw = 460 * s, bh = 150 * s;
        var btnRect = new Rect(Screen.width / 2f - bw / 2f, Screen.height - bh - 120 * s, bw, bh);

        if (state == State.Ready)
        {
            if (GUI.Button(btnRect, "START", button)) StartWave();
        }
        else if (state == State.Won || state == State.Lost)
        {
            string msg = state == State.Won ? "VICTORY" : "DEFEAT";
            GUI.Box(new Rect(Screen.width / 2f - 300 * s, Screen.height / 2f - 100 * s, 600 * s, 130 * s), "");
            GUI.Label(new Rect(Screen.width / 2f - 200 * s, Screen.height / 2f - 80 * s, 500 * s, 100 * s), msg, label);
            if (GUI.Button(btnRect, "RETRY", button)) ResetGame();
        }
    }
}