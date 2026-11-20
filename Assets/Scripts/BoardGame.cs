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
    public float minScale = 0.6f;
    public float maxScale = 3f;

    enum State { Setup, Ready, Playing, Won, Lost }
    State state = State.Setup;
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

        // Una vez colocado el tablero, que no salgan más al tocar
        var spawner = GameObject.Find("Object Spawner");
        if (spawner != null)
        {
            foreach (var mb in spawner.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name == "ARInteractorSpawnTrigger")
                    mb.enabled = false;
        }

        BuildBorder();
    }

    // ---------- Borde rojo (se adapta al tamaño del suelo) ----------

    void BuildBorder()
    {
        Transform floor = transform.Find("Floor");
        if (floor == null) return;

        Vector3 fs = floor.localScale;
        Vector3 fp = floor.localPosition;
        float t = 0.012f;
        float y = fp.y + fs.y / 2f + t / 2f;

        MakeBorder("Border_N", new Vector3(fp.x, y, fp.z + fs.z / 2f), new Vector3(fs.x + t, t, t));
        MakeBorder("Border_S", new Vector3(fp.x, y, fp.z - fs.z / 2f), new Vector3(fs.x + t, t, t));
        MakeBorder("Border_E", new Vector3(fp.x + fs.x / 2f, y, fp.z), new Vector3(t, t, fs.z + t));
        MakeBorder("Border_W", new Vector3(fp.x - fs.x / 2f, y, fp.z), new Vector3(t, t, fs.z + t));
    }

    void MakeBorder(string borderName, Vector3 localPos, Vector3 localScale)
    {
        // Si había uno hecho a mano, lo reemplazamos
        Transform old = transform.Find(borderName);
        if (old != null) { old.name = "old_" + borderName; Destroy(old.gameObject); }

        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = borderName;
        Destroy(b.GetComponent<Collider>());
        b.transform.SetParent(transform, false);
        b.transform.localPosition = localPos;
        b.transform.localScale = localScale;
        b.GetComponent<Renderer>().material.color = Color.red;
    }

    // ---------- Ajuste del tablero ----------

    void Rescale(float factor)
    {
        float ns = Mathf.Clamp(transform.localScale.x * factor, minScale, maxScale);
        transform.localScale = Vector3.one * ns;
    }

    void ConfirmBoard()
    {
        // Ahora que el tablero está donde debe, se construye el mapa de navegación
        GetComponent<NavMeshSurface>().BuildNavMesh();
        state = State.Ready;
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

    // ---------- Monedas ----------

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

        var ob = t.AddComponent<NavMeshObstacle>();
        ob.shape = NavMeshObstacleShape.Capsule;
        ob.center = Vector3.zero;
        ob.radius = 0.5f;
        ob.height = 2f;
        ob.carving = true;

        t.AddComponent<Tower>();
        towers.Add(t);
    }

    // ---------- Toques ----------

    Camera GetCam()
    {
        Camera c = Camera.main;
        if (c == null) c = FindFirstObjectByType<Camera>();
        return c;
    }

    void HandleClicks(Rect a, Rect b)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown) return;
        if (a.Contains(e.mousePosition) || b.Contains(e.mousePosition)) return;
        if (state == State.Won || state == State.Lost) return;

        Camera cam = GetCam();
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(new Vector3(e.mousePosition.x, Screen.height - e.mousePosition.y, 0f));

        // Fase de ajuste: tocar la mesa mueve el tablero
        if (state == State.Setup)
        {
            Plane plane = new Plane(Vector3.up, transform.position);
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 p = ray.GetPoint(dist);
                transform.position = new Vector3(p.x, transform.position.y, p.z);
            }
            return;
        }

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
        var hint = new GUIStyle(GUI.skin.label) { fontSize = (int)(38 * s), fontStyle = FontStyle.Bold, wordWrap = true };
        hint.normal.textColor = Color.white;
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

        // Rectángulos de botones
        float bw = 460 * s, bh = 150 * s;
        var mainRect = new Rect(Screen.width / 2f - bw / 2f, Screen.height - bh - 120 * s, bw, bh);
        var towerRect = new Rect(mainRect.x, mainRect.y - bh - 30 * s, bw, bh);

        float cw = 320 * s, gap = 20 * s, ch = 130 * s;
        float rowW = cw * 3 + gap * 2;
        var rowRect = new Rect((Screen.width - rowW) / 2f, mainRect.y - ch - 30 * s, rowW, ch);

        HandleClicks(state == State.Setup ? rowRect : towerRect, mainRect);

        if (state == State.Setup)
        {
            GUI.Box(new Rect(10 * s, 190 * s, Screen.width - 20 * s, 130 * s), "");
            GUI.Label(new Rect(30 * s, 200 * s, Screen.width - 60 * s, 120 * s),
                "Tap the table to move the board. Rotate and resize it, then press OK.", hint);

            if (GUI.Button(new Rect(rowRect.x, rowRect.y, cw, ch), "ROTATE", button))
                transform.Rotate(0f, 15f, 0f, Space.World);
            if (GUI.Button(new Rect(rowRect.x + cw + gap, rowRect.y, cw, ch), "SMALLER", button))
                Rescale(0.9f);
            if (GUI.Button(new Rect(rowRect.x + 2 * (cw + gap), rowRect.y, cw, ch), "BIGGER", button))
                Rescale(1.1f);
            if (GUI.Button(mainRect, "OK", button))
                ConfirmBoard();
            return;
        }

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