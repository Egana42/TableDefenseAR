using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class BoardGame : MonoBehaviour
{
    public GameObject goblinPrefab;
    public Transform portal;
    public Transform crystal;
    public int goblinsPerWave = 10;
    public float timeBetweenGoblins = 1.5f;
    public int crystalMaxHealth = 5;
    public int damagePerGoblin = 1;
    public int startGold = 40;
    public int towerCost = 20;
    public int goldPerCoin = 10;
    public float coinLifetime = 12f;

    enum State { Ready, Playing, Won, Lost }
    State state = State.Ready;
    int crystalHealth;
    int goblinsSpawned;
    int goblinsAlive;
    int gold;
    bool placingTower;
    readonly List<GameObject> towers = new List<GameObject>();
    class Coin { public Transform t; public float expireAt; }
    readonly List<Coin> coins = new List<Coin>();

    void Start()
    {
        crystalHealth = crystalMaxHealth;
        gold = startGold;

        
        var spawner = GameObject.Find("Object Spawner");
        if (spawner != null)
        {
            foreach (var mb in spawner.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "ARInteractorSpawnTrigger")
                    mb.enabled = false;
        }

        // Hornear el mapa de navegación en este momento
        GetComponent<NavMeshSurface>().BuildNavMesh();
    }

    // ---------- Partida ----------

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
        crystalHealth -= damagePerGoblin;
        if (crystalHealth <= 0)
        {
            crystalHealth = 0;
            EndGame(State.Lost);
        }
        else CheckWin();
    }

    public void OnGoblinKilled(Vector3 pos)
    {
        goblinsAlive--;
        SpawnCoin(pos);
        CheckWin();
    }

    void SpawnCoin(Vector3 pos)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        c.name = "Coin";
        Destroy(c.GetComponent<Collider>());
        c.transform.position = new Vector3(pos.x, pos.y + 0.03f, pos.z);
        c.transform.localScale = new Vector3(0.035f, 0.004f, 0.035f);
        c.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   
        c.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.1f);
        coins.Add(new Coin { t = c.transform, expireAt = Time.time + coinLifetime });
    }

    bool TryCollectCoin(Ray ray)
    {
        Vector3 dir = ray.direction.normalized;
        for (int i = 0; i < coins.Count; i++)
        {
            if (coins[i].t == null) continue;
            Vector3 toCoin = coins[i].t.position - ray.origin;
            if (Vector3.Dot(dir, toCoin) <= 0f) continue;                 
            if (Vector3.Cross(dir, toCoin).magnitude <= 0.05f)            
            {
                gold += goldPerCoin;
                Destroy(coins[i].t.gameObject);
                coins.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
    void Update()
    {
        for (int i = coins.Count - 1; i >= 0; i--)
        {
            if (coins[i].t == null) { coins.RemoveAt(i); continue; }
            if (Time.time >= coins[i].expireAt)
            {
                Destroy(coins[i].t.gameObject);
                coins.RemoveAt(i);
                continue;
            }
            coins[i].t.Rotate(0f, 180f * Time.deltaTime, 0f, Space.World);
        }
    }
    void CheckWin()
    {
        if (state == State.Playing && goblinsSpawned >= goblinsPerWave && goblinsAlive <= 0)
            EndGame(State.Won);
    }

    void EndGame(State result)
    {
        state = result;
        placingTower = false;
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
        foreach (var t in towers)
            if (t != null) Destroy(t);
        towers.Clear();
        foreach (var c in coins)
            if (c.t != null) Destroy(c.t.gameObject);
        coins.Clear();
        crystalHealth = crystalMaxHealth;
        gold = startGold;
        goblinsSpawned = 0;
        goblinsAlive = 0;
        placingTower = false;
        state = State.Ready;
    }

    // ---------- Torres ----------

    void PlaceTower(Vector3 pos)
    {
        if (gold < towerCost) return;
        gold -= towerCost;
        placingTower = false;

        var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        t.name = "Tower";
        t.transform.position = pos + Vector3.up * 0.04f;
        t.transform.localScale = new Vector3(0.06f, 0.04f, 0.06f);
        t.GetComponent<Renderer>().material.color = new Color(0.9f, 0.3f, 0.1f);

        // Los goblins rodean la torre
        var ob = t.AddComponent<NavMeshObstacle>();
        ob.shape = NavMeshObstacleShape.Capsule;
        ob.center = Vector3.zero;
        ob.radius = 0.5f;
        ob.height = 2f;
        ob.carving = true;

        t.AddComponent<Tower>();
        towers.Add(t);
    }

    void HandleClicks(Rect towerRect, Rect otherRect)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown) return;
        if (state != State.Ready && state != State.Playing) return;
        if (towerRect.Contains(e.mousePosition) || otherRect.Contains(e.mousePosition)) return;

        Camera cam = GetCam();
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(new Vector3(e.mousePosition.x, Screen.height - e.mousePosition.y, 0f));

        if (TryCollectCoin(ray)) return;
        if (!placingTower) return;

        foreach (var hit in Physics.RaycastAll(ray, 50f))
        {
            if (hit.collider.gameObject.name == "Floor")
            {
                PlaceTower(hit.point);
                break;
            }
        }
    }

    // ---------- Interfaz ----------

    Camera GetCam()
    {
        Camera c = Camera.main;
        if (c == null) c = FindFirstObjectByType<Camera>();
        return c;
    }

    void DrawCrystalBar(float s)
    {
        if (crystal == null || !crystal.gameObject.activeSelf) return;
        Camera cam = GetCam();
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(crystal.position + Vector3.up * 0.12f);
        if (p.z <= 0) return;

        float w = 220 * s, h = 26 * s;
        float x = p.x - w / 2f;
        float y = Screen.height - p.y - h / 2f;

        Color old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(new Rect(x - 3 * s, y - 3 * s, w + 6 * s, h + 6 * s), Texture2D.whiteTexture);
        float t = Mathf.Clamp01((float)crystalHealth / crystalMaxHealth);
        GUI.color = Color.Lerp(Color.red, Color.green, t);
        GUI.DrawTexture(new Rect(x, y, w * t, h), Texture2D.whiteTexture);
        GUI.color = old;
    }

    void OnGUI()
    {
        float s = Mathf.Min(Screen.width, Screen.height) / 1080f;
        DrawCrystalBar(s);

        var label = new GUIStyle(GUI.skin.label) { fontSize = (int)(52 * s), fontStyle = FontStyle.Bold };
        label.normal.textColor = Color.white;
        var button = new GUIStyle(GUI.skin.button) { fontSize = (int)(46 * s) };

        // HUD
        GUI.Box(new Rect(10 * s, 10 * s, 620 * s, 170 * s), "");
        GUI.Label(new Rect(30 * s, 20 * s, 600 * s, 60 * s), $"Crystal: {crystalHealth}/{crystalMaxHealth}", label);

        float hudT = Mathf.Clamp01((float)crystalHealth / crystalMaxHealth);
        Color old = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(new Rect(30 * s, 82 * s, 560 * s, 12 * s), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(Color.red, Color.green, hudT);
        GUI.DrawTexture(new Rect(30 * s, 82 * s, 560 * s * hudT, 12 * s), Texture2D.whiteTexture);
        GUI.color = old;

        GUI.Label(new Rect(30 * s, 100 * s, 600 * s, 70 * s), $"Gold: {gold}", label);

        // Botones
        float bw = 460 * s, bh = 150 * s;
        var mainRect = new Rect(Screen.width / 2f - bw / 2f, Screen.height - bh - 120 * s, bw, bh);
        var towerRect = new Rect(mainRect.x, mainRect.y - bh - 30 * s, bw, bh);

        HandleClicks(towerRect, mainRect);

        if (state == State.Ready || state == State.Playing)
        {
            GUI.enabled = placingTower || gold >= towerCost;
            string txt = placingTower ? "TAP THE BOARD" : $"TOWER ({towerCost} gold)";
            if (GUI.Button(towerRect, txt, button)) placingTower = !placingTower;
            GUI.enabled = true;
        }

        if (state == State.Ready)
        {
            if (GUI.Button(mainRect, "START", button)) StartWave();
        }
        else if (state == State.Won || state == State.Lost)
        {
            string msg = state == State.Won ? "VICTORY" : "DEFEAT";
            GUI.Box(new Rect(Screen.width / 2f - 300 * s, Screen.height / 2f - 100 * s, 600 * s, 130 * s), "");
            GUI.Label(new Rect(Screen.width / 2f - 200 * s, Screen.height / 2f - 80 * s, 500 * s, 100 * s), msg, label);
            if (GUI.Button(mainRect, "RETRY", button)) ResetGame();
        }
    }
}