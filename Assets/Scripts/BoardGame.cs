using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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

    // ---------- Mesa real (modo automático) ----------
    public GameObject treePrefab;          // opcional: un solo árbol para todo (se usa si las listas están vacías)
    public GameObject[] outerTreePrefabs;  // tipos de árbol del anillo exterior (se elige uno al azar)
    public GameObject[] innerTreePrefabs;  // tipos de árbol de los muros de dentro (si está vacía, usa los de fuera)
    public float treeHeight = 0.08f;       // altura base de un árbol modelado (m); los de dentro usan innerTreeScale sobre esto
    public float outerTreeScale = 1f;      // multiplicador de tamaño del anillo exterior
    public float innerTreeScale = 0.7f;    // los de dentro, más bajitos
    public float treeBlockRadius = 0.015f;  // radio REAL que bloquea al goblin (el árbol puede verse mucho más grande)
    public float ringOffset = 0.08f;       // distancia del anillo de árboles al borde de la mesa; el mapa se diseña hasta el anillo
    public float treeSpacing = 0.07f;      // separación de los árboles del borde (m)
    public float minRectX = 0.25f;         // tamaño mínimo aceptable de la mesa (m)
    public float minRectZ = 0.18f;
    public float maxRectX = 1.6f;          // tamaño máximo del campo de juego (m): mesas enormes se recortan
    public float maxRectZ = 1.0f;
    public Vector2 fakeTableSize = new Vector2(1.6f, 1.0f);   // mesa falsa del editor (m)
    public bool hideTemplateUI = true;     // oculta el menú de cubos de la plantilla AR
    public float clearRadius = 0.16f;      // radio sin árboles alrededor del portal y del cristal
    bool mapLocked;
    bool pathWasOk = true;
    string blockedMsg = "";
    float blockedUntil;

    public bool simulateTableInEditor = true;   // en el editor: genera una mesa falsa al tocar
    bool fakeInit;
    Vector2 fakeOrigin;
    float fakeY;

    ARPlaneManager planeManager;
    ARRaycastManager raycastManager;
    bool autoMode;
    bool planePicked;
    Vector2 tableSize;
    string scanMsg = "Move your phone slowly to scan the table, then tap it.";
    readonly List<GameObject> forest = new List<GameObject>();
    readonly List<GameObject> innerTrees = new List<GameObject>();

    // Texto flotante "+10" al recoger una moneda
    class Popup { public Vector3 pos; public float t0; public int amount; }
    readonly List<Popup> popups = new List<Popup>();

    // ---------- Puntería del jugador ----------
    public float arrowCooldown = 1.0f;
    public float arrowSpeed = 1.5f;
    public int arrowDamage = 1;
    public float aimRadius = 0.07f;
    public float maxShootDistance = 1.2f;

    class Arrow { public Transform t; public GoblinMover target; }
    readonly List<Arrow> arrows = new List<Arrow>();
    float nextShotTime;
    bool aimingAtGoblin;

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

        planeManager = FindFirstObjectByType<ARPlaneManager>();
        raycastManager = FindFirstObjectByType<ARRaycastManager>();
        bool realAR = planeManager != null && raycastManager != null;
        bool editorSim = Application.isEditor && simulateTableInEditor;
        autoMode = realAR || editorSim;
        if (editorSim)
            scanMsg = "EDITOR TEST: tap the screen to generate a fake table.";

        if (hideTemplateUI)
        {
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.renderMode != RenderMode.WorldSpace) cv.gameObject.SetActive(false);
        }

        BuildBorder();

        // En modo automático no hay tablero visible: solo la mesa real y los árboles
        if (autoMode) SetVisible(false);
    }

    void SetVisible(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = v;
    }

    void ShowEndpoints()
    {
        if (portal != null)
            foreach (var r in portal.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        if (crystal != null)
            foreach (var r in crystal.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
    }

    void ClearForest()
    {
        foreach (var t in forest)
            if (t != null) { t.SetActive(false); Destroy(t); }
        forest.Clear();
        innerTrees.Clear();
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
        if (autoMode && planePicked)
        {
            LockTable();
        }
        else
        {
            // Ahora que el tablero está donde debe, se construye el mapa de navegación
            GetComponent<NavMeshSurface>().BuildNavMesh();
            mapLocked = true;
        }
        state = State.Ready;
    }

    // ---------- Mesa real: elegir superficie, ajustar rectángulo, árboles ----------

    bool TryPickPlane(Vector2 screenPos)
    {
        if (Application.isEditor && simulateTableInEditor) return PickFakeTable();
        if (raycastManager == null || planeManager == null) return false;
        var hits = new List<ARRaycastHit>();
        if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
        {
            scanMsg = "No surface there. Keep scanning and tap again.";
            return false;
        }
        foreach (var h in hits)
        {
            var plane = planeManager.GetPlane(h.trackableId);
            if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp) continue;
            if (FitRectangle(plane, h.pose.position.y))
            {
                planePicked = true;
                return true;
            }
        }
        scanMsg = "That surface is too small or not flat. Tap the table.";
        return false;
    }

    bool FitRectangle(ARPlane plane, float y)
    {
        // Contorno de la superficie en coordenadas del mundo (x, z)
        var pts = new List<Vector2>();
        foreach (var b in plane.boundary)
        {
            Vector3 w = plane.transform.TransformPoint(new Vector3(b.x, 0f, b.y));
            pts.Add(new Vector2(w.x, w.z));
        }
        return FitPolygon(pts, y);
    }

    // Solo para probar en el editor: una "mesa" irregular, girada al azar en cada toque
    bool PickFakeTable()
    {
        if (!fakeInit)
        {
            fakeOrigin = new Vector2(transform.position.x, transform.position.z);
            fakeY = transform.position.y;
            fakeInit = true;
        }
        Vector2[] shape =
        {
            new Vector2(0f, 0f), new Vector2(0.9f, 0.05f), new Vector2(0.95f, 0.55f),
            new Vector2(0.5f, 0.65f), new Vector2(0.05f, 0.5f)
        };
        float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        var pts = new List<Vector2>();
        foreach (var p in shape)
        {
            Vector2 q = new Vector2(p.x / 0.95f * fakeTableSize.x, p.y / 0.65f * fakeTableSize.y);
            Vector2 r = new Vector2(q.x * Mathf.Cos(ang) - q.y * Mathf.Sin(ang),
                                    q.x * Mathf.Sin(ang) + q.y * Mathf.Cos(ang));
            pts.Add(r + fakeOrigin);
        }
        if (!FitPolygon(pts, fakeY)) { scanMsg = "Fake table too small."; return false; }
        planePicked = true;
        return true;
    }

    bool FitPolygon(List<Vector2> pts, float y)
    {
        if (pts.Count < 3) return false;

        // 1) Rectángulo girado de menor área que contiene la forma
        float bestArea = float.MaxValue, bestAng = 0f;
        float bMinU = 0f, bMaxU = 0f, bMinV = 0f, bMaxV = 0f;
        for (int deg = 0; deg < 90; deg += 3)
        {
            float th = deg * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
            Vector2 v = new Vector2(-Mathf.Sin(th), Mathf.Cos(th));
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in pts)
            {
                float pu = Vector2.Dot(p, u), pv = Vector2.Dot(p, v);
                if (pu < minU) minU = pu;
                if (pu > maxU) maxU = pu;
                if (pv < minV) minV = pv;
                if (pv > maxV) maxV = pv;
            }
            float area = (maxU - minU) * (maxV - minV);
            if (area < bestArea)
            {
                bestArea = area; bestAng = th;
                bMinU = minU; bMaxU = maxU; bMinV = minV; bMaxV = maxV;
            }
        }

        Vector2 U = new Vector2(Mathf.Cos(bestAng), Mathf.Sin(bestAng));
        Vector2 V = new Vector2(-Mathf.Sin(bestAng), Mathf.Cos(bestAng));
        float cu = (bMinU + bMaxU) / 2f, cv = (bMinV + bMaxV) / 2f;
        float hu = (bMaxU - bMinU) / 2f, hv = (bMaxV - bMinV) / 2f;

        // 2) Encogerlo hasta que quepa dentro de la superficie
        float k = 1f;
        for (; k > 0.3f; k -= 0.05f)
            if (RectInside(pts, U, V, cu, cv, hu * k, hv * k)) break;
        hu *= k; hv *= k;

        float sx = 2f * hu, sz = 2f * hv;
        Vector2 center = U * cu + V * cv;

        // El lado largo va de portal a cristal
        if (sx < sz)
        {
            float tmp = sx; sx = sz; sz = tmp;
            Vector2 newU = V;
            V = -U;
            U = newU;
        }

        if (sx < minRectX || sz < minRectZ) return false;
        // recorta a un campo manejable, centrado en el rectángulo encontrado
        sx = Mathf.Min(sx, maxRectX);
        sz = Mathf.Min(sz, maxRectZ);

        transform.localScale = Vector3.one;
        transform.position = new Vector3(center.x, y, center.y);
        transform.rotation = Quaternion.LookRotation(new Vector3(V.x, 0f, V.y), Vector3.up);
        ApplyRect(sx, sz);
        ShowEndpoints();
        BuildMap(Random.Range(0, layouts.Length));   // mapa visible ya al elegir la mesa
        return true;
    }

    bool RectInside(List<Vector2> poly, Vector2 U, Vector2 V, float cu, float cv, float hu, float hv)
    {
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                if (i == 0 && j == 0) continue;
                Vector2 p = U * (cu + i * hu) + V * (cv + j * hv);
                if (!PointInPoly(p, poly)) return false;
            }
        return true;
    }

    bool PointInPoly(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    void ApplyRect(float sx, float sz)
    {
        tableSize = new Vector2(sx, sz);
        Transform floor = transform.Find("Floor");
        if (floor != null)
        {
            float ex = autoMode ? 2f * ringOffset : 0f;   // en modo mesa el suelo (invisible) llega hasta los árboles
            floor.localScale = new Vector3(sx + ex, floor.localScale.y, sz + ex);
            floor.localPosition = new Vector3(0f, floor.localPosition.y, 0f);
        }
        float m = 0.07f;
        if (portal != null)
            portal.localPosition = new Vector3(-sx / 2f + m, portal.localPosition.y, 0f);
        if (crystal != null)
            crystal.localPosition = new Vector3(sx / 2f - m, crystal.localPosition.y, 0f);
    }

    void LockTable() { StartCoroutine(LockTableRoutine()); }

    IEnumerator LockTableRoutine()
    {
        // Ya no se buscan más superficies: la mesa queda guardada
        if (planeManager != null)
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.None;
            planeManager.SetTrackablesActive(false);
        }

        // Si el diseño no deja camino, se prueban otros; al final, mapa sin muros
        mapLocked = false;
        var surface = GetComponent<NavMeshSurface>();
        surface.minRegionArea = 0f;   // por defecto borra zonas de menos de 2 m2: una mesa entera cae ahí
        if (!autoRetryMap)
        {
            // Sin cambiar de mapa: se hornea una vez y se deja como está
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            yield return new WaitForSeconds(1.5f);   // el carving es asíncrono
            pathWasOk = PathOk();
            Debug.Log("[Map] mapa fijo, diseño " + curLayout + ", camino " + (pathWasOk ? "OK" : "NO"));
            mapLocked = true;
            yield break;
        }
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            // El carving de los árboles es asíncrono y tarda: se sondea hasta 3 s en vez de mirar una sola vez
            bool good = false;
            for (float w = 0f; w < 3f && !good; w += 0.3f)
            {
                yield return new WaitForSeconds(0.3f);
                good = PathOk(w > 2.6f);
            }
            if (good)
            {
                Debug.Log("[Map] OK en intento " + attempt + ", diseño " + curLayout);
                pathWasOk = true;
                mapLocked = true;
                yield break;
            }
            Debug.Log("[Map] intento " + attempt + " falla con diseño " + curLayout);
            // Mismo diseño con otras posiciones, luego el siguiente diseño; al final, sin muros
            int next;
            if (attempt >= 5) next = -1;
            else if (attempt % 2 == 0) next = curLayout;
            else next = (curLayout + 1) % layouts.Length;
            BuildMap(next);
            yield return null;
        }
        surface.BuildNavMesh();
        yield return new WaitForSeconds(0.8f);
        mapLocked = true;
    }

    bool PathOk(bool log = true)
    {
        if (portal == null || crystal == null) return false;
        if (!NavMesh.SamplePosition(portal.position, out NavMeshHit a, 0.3f, NavMesh.AllAreas))
        { if (log) Debug.Log("[Map] portal sin NavMesh cerca"); return false; }
        if (!NavMesh.SamplePosition(crystal.position, out NavMeshHit b, 0.3f, NavMesh.AllAreas))
        { if (log) Debug.Log("[Map] cristal sin NavMesh cerca"); return false; }
        var path = new NavMeshPath();
        bool found = NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
        if (!(found && path.status == NavMeshPathStatus.PathComplete))
        { if (log) Debug.Log("[Map] sin camino: " + path.status + " (portal a " + a.distance.ToString("F2") + " m, cristal a " + b.distance.ToString("F2") + " m)"); return false; }
        return true;
    }

    // ---------- Diseños de mapa ----------
    // Coordenadas normalizadas: x e z van de -0.5 a 0.5 (x = lado largo de la mesa).
    // walls = líneas de árboles; p* = zona donde puede aparecer el portal; c* = zona del cristal.
    class Layout
    {
        public Vector2[][] walls;
        public Vector2 pMin, pMax, cMin, cMax;
    }

    static Vector2 P(float x, float z) { return new Vector2(x, z); }

    static readonly Layout[] layouts =
    {
        // 0 · Estrecho: dos muros que se acercan desde arriba y abajo y dejan un paso en medio
        new Layout
        {
            walls = new[]
            {
                new[] { P(-0.22f, 0.5f), P(-0.10f, 0.32f), P(0f, 0.22f), P(0.10f, 0.32f), P(0.22f, 0.5f) },
                new[] { P(-0.22f, -0.5f), P(-0.10f, -0.32f), P(0f, -0.22f), P(0.10f, -0.32f), P(0.22f, -0.5f) }
            },
            pMin = P(-0.45f, -0.3f), pMax = P(-0.3f, 0.3f),
            cMin = P(0.3f, -0.3f),  cMax = P(0.45f, 0.3f)
        },
        // 1 · Cueva: un arco rodea el cristal y hay que dar la vuelta para entrar
        new Layout
        {
            walls = new[]
            {
                new[] { P(0.36f, -0.15f), P(0.2f, -0.17f), P(0.1f, -0.1f), P(0.07f, 0f),
                        P(0.1f, 0.1f), P(0.2f, 0.17f), P(0.36f, 0.15f) }
            },
            pMin = P(-0.45f, -0.3f), pMax = P(-0.25f, 0.3f),
            cMin = P(0.17f, -0.04f), cMax = P(0.27f, 0.04f)
        },
        // 2 · Península: un muro cuelga de un lado y hay que rodear su punta
        new Layout
        {
            walls = new[]
            {
                new[] { P(0f, 0.5f), P(0f, 0.25f), P(0.02f, 0.05f), P(0.12f, -0.08f), P(0.28f, -0.1f) }
            },
            pMin = P(-0.45f, -0.1f), pMax = P(-0.2f, 0.3f),
            cMin = P(0.12f, 0.12f),  cMax = P(0.4f, 0.4f)
        },
        // 3 · Zigzag: un muro cuelga de arriba y otro sube de abajo; el camino hace una S
        new Layout
        {
            walls = new[]
            {
                new[] { P(-0.12f, 0.5f), P(-0.12f, -0.12f) },
                new[] { P(0.12f, -0.5f), P(0.12f, 0.12f) }
            },
            pMin = P(-0.45f, -0.3f), pMax = P(-0.28f, 0.3f),
            cMin = P(0.28f, -0.3f),  cMax = P(0.45f, 0.3f)
        },
        // 4 · Isla: un bloque en el centro; los goblins lo rodean por arriba o por abajo
        new Layout
        {
            walls = new[]
            {
                new[] { P(-0.07f, 0f), P(0f, 0.14f), P(0.07f, 0f), P(0f, -0.14f), P(-0.07f, 0f) }
            },
            pMin = P(-0.45f, -0.3f), pMax = P(-0.3f, 0.3f),
            cMin = P(0.3f, -0.3f),   cMax = P(0.45f, 0.3f)
        },
        // 5 · Embudo: dos muros diagonales que se cierran hacia el cristal
        new Layout
        {
            walls = new[]
            {
                new[] { P(-0.3f, 0.5f), P(0f, 0.22f), P(0.2f, 0.12f) },
                new[] { P(-0.3f, -0.5f), P(0f, -0.22f), P(0.2f, -0.12f) }
            },
            pMin = P(-0.45f, -0.3f), pMax = P(-0.3f, 0.3f),
            cMin = P(0.3f, -0.2f),   cMax = P(0.45f, 0.2f)
        },
        // 6 · Eslalon: tres tramos cortos alternados que obligan a zigzaguear
        new Layout
        {
            walls = new[]
            {
                new[] { P(-0.15f, 0.5f), P(-0.15f, 0.05f) },
                new[] { P(0f, -0.5f), P(0f, -0.05f) },
                new[] { P(0.15f, 0.5f), P(0.15f, 0.05f) }
            },
            pMin = P(-0.45f, -0.3f), pMax = P(-0.3f, 0.3f),
            cMin = P(0.3f, -0.3f),   cMax = P(0.45f, 0.3f)
        }
    };

    static void FlipX(ref Vector2 min, ref Vector2 max)
    {
        float a = -max.x, b = -min.x;
        min.x = a; max.x = b;
    }

    static void FlipZ(ref Vector2 min, ref Vector2 max)
    {
        float a = -max.y, b = -min.y;
        min.y = a; max.y = b;
    }

    // index -1 = mapa sin muros (solo el anillo de árboles). Se llama al elegir la mesa
    // y también al fallar el camino al confirmar.
    int curLayout = -1;

    void BuildMap(int index)
    {
        curLayout = index;
        ClearForest();

        Transform floor = transform.Find("Floor");
        if (floor == null) return;

        float sx = tableSize.x, sz = tableSize.y;
        float hx = sx / 2f, hz = sz / 2f;
        float topY = floor.localPosition.y + floor.localScale.y / 2f;
        float off = ringOffset;

        // Anillo exterior: delimita la mesa
        for (float x = -hx - off; x <= hx + off + 0.001f; x += treeSpacing)
        {
            AddTree(new Vector3(x, topY, hz + off), false);
            AddTree(new Vector3(x, topY, -hz - off), false);
        }
        for (float z = -hz; z <= hz + 0.001f; z += treeSpacing)
        {
            AddTree(new Vector3(hx + off, topY, z), false);
            AddTree(new Vector3(-hx - off, topY, z), false);
        }

        // El diseño del mapa usa el espacio hasta el anillo de árboles (más pasillo para los goblins)
        sx += 2f * off; sz += 2f * off;
        hx = sx / 2f; hz = sz / 2f;

        // Portal y cristal: por defecto en los dos extremos
        float m = 0.10f;
        Vector2 pPos = new Vector2(-hx + m, 0f);
        Vector2 cPos = new Vector2(hx - m, 0f);
        bool fx = false, fz = false;
        Layout L = null;

        if (index >= 0)
        {
            L = layouts[index];
            fx = Random.value < 0.5f;
            fz = Random.value < 0.5f;

            Vector2 pMin = L.pMin, pMax = L.pMax, cMin = L.cMin, cMax = L.cMax;
            if (fx) { FlipX(ref pMin, ref pMax); FlipX(ref cMin, ref cMax); }
            if (fz) { FlipZ(ref pMin, ref pMax); FlipZ(ref cMin, ref cMax); }

            // Posiciones al azar dentro de sus zonas, con distancia mínima entre ellos
            float minDist = Mathf.Max(0.25f, 0.45f * sx);
            bool ok = false;
            for (int i = 0; i < 40 && !ok; i++)
            {
                Vector2 pp = ZonePoint(pMin, pMax, sx, sz, m);
                Vector2 cc = ZonePoint(cMin, cMax, sx, sz, m);
                if (Vector2.Distance(pp, cc) >= minDist) { pPos = pp; cPos = cc; ok = true; }
            }
            if (!ok)
            {
                pPos = ZonePoint(pMin, pMax, sx, sz, m);
                cPos = ZonePoint(cMin, cMax, sx, sz, m);
            }

            // Muros de árboles
            foreach (var wall in L.walls)
            {
                for (int k = 0; k < wall.Length - 1; k++)
                {
                    Vector2 a = WallPoint(wall[k], fx, fz, sx, sz);
                    Vector2 b = WallPoint(wall[k + 1], fx, fz, sx, sz);
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / treeSpacing));
                    for (int t = 0; t < n; t++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, (float)t / n);
                        if (Vector2.Distance(p, pPos) < clearRadius || Vector2.Distance(p, cPos) < clearRadius) continue;
                        AddTree(new Vector3(p.x, topY, p.y), true);
                    }
                }
                Vector2 last = WallPoint(wall[wall.Length - 1], fx, fz, sx, sz);
                if (Vector2.Distance(last, pPos) >= clearRadius && Vector2.Distance(last, cPos) >= clearRadius)
                    AddTree(new Vector3(last.x, topY, last.y), true);
            }
        }

        if (portal != null)
            portal.localPosition = new Vector3(pPos.x, portal.localPosition.y, pPos.y);
        if (crystal != null)
            crystal.localPosition = new Vector3(cPos.x, crystal.localPosition.y, cPos.y);
    }

    static Vector2 ZonePoint(Vector2 min, Vector2 max, float sx, float sz, float margin)
    {
        float x = Random.Range(min.x, max.x) * sx;
        float z = Random.Range(min.y, max.y) * sz;
        x = Mathf.Clamp(x, -sx / 2f + margin, sx / 2f - margin);
        z = Mathf.Clamp(z, -sz / 2f + margin, sz / 2f - margin);
        return new Vector2(x, z);
    }

    static Vector2 WallPoint(Vector2 n, bool fx, bool fz, float sx, float sz)
    {
        float x = fx ? -n.x : n.x;
        float z = fz ? -n.y : n.y;
        return new Vector2(x * sx, z * sz);
    }

    void AddTree(Vector3 localPos, bool inner)
    {
        if (!inner)
            localPos += new Vector3(Random.Range(-0.01f, 0.01f), 0f, Random.Range(-0.01f, 0.01f));
        GameObject tree = MakeTree(localPos, Random.Range(0.85f, 1.2f) * (inner ? innerTreeScale : outerTreeScale), inner);
        forest.Add(tree);
        if (inner) innerTrees.Add(tree);
    }

    GameObject MakeTree(Vector3 localPos, float scale, bool inner)
    {
        GameObject tree;
        GameObject chosen = treePrefab;
        GameObject[] list = (inner && innerTreePrefabs != null && innerTreePrefabs.Length > 0) ? innerTreePrefabs : outerTreePrefabs;
        if (list != null && list.Length > 0) chosen = list[Random.Range(0, list.Length)];
        if (chosen != null)
        {
            tree = Instantiate(chosen);
            // Se normaliza el tamaño: da igual lo grande que sea el modelo, mide treeHeight (m) de alto
            var rends = tree.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds bb = rends[0].bounds;
                for (int r = 1; r < rends.Length; r++) bb.Encapsulate(rends[r].bounds);
                float rootS = tree.transform.localScale.x;
                if (bb.size.y > 0.0001f && rootS > 0.0001f)
                    scale *= treeHeight * rootS / bb.size.y;
            }
        }
        else
        {
            // Árbol provisional hecho con formas simples
            tree = new GameObject("Tree");
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            trunk.transform.localScale = new Vector3(0.012f, 0.015f, 0.012f);
            trunk.GetComponent<Renderer>().material.color = new Color(0.45f, 0.3f, 0.15f);

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(crown.GetComponent<Collider>());
            crown.transform.SetParent(tree.transform, false);
            crown.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            crown.transform.localScale = Vector3.one * 0.05f;
            crown.GetComponent<Renderer>().material.color = new Color(0.2f, 0.55f, 0.2f);
        }

        tree.name = inner ? "Tree_inner" : "Tree";
        tree.transform.SetParent(transform, false);
        tree.transform.localPosition = localPos;
        tree.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        tree.transform.localScale = Vector3.one * scale;

        // Bloqueador alto e invisible que se hornea dentro del NavMesh (sin carving ni esperas)
        var blk = new GameObject("NavBlocker");
        blk.layer = 2;                       // Ignore Raycast: no estorba al tocar la pantalla
        blk.transform.SetParent(tree.transform, false);
        var col = blk.AddComponent<CapsuleCollider>();
        col.radius = treeBlockRadius / scale;
        col.height = 2f / scale;
        col.center = new Vector3(0f, 1f / scale, 0f);
        var mod = blk.AddComponent<NavMeshModifier>();
        mod.overrideArea = true;
        mod.area = 1;                        // 1 = Not Walkable

        // Además, carving (esto es lo que ya comprobaste que bloquea a los goblins)
        var ob = blk.AddComponent<NavMeshObstacle>();
        ob.shape = NavMeshObstacleShape.Capsule;
        ob.center = new Vector3(0f, 1f / scale, 0f);
        ob.radius = treeBlockRadius / scale;
        ob.height = 2f / scale;
        ob.carveOnlyStationary = false;
        ob.carving = true;
        return tree;
    }

    // ---------- Partida ----------

    void StartWave()
    {
        if (autoMode && !mapLocked) return;   // el mapa aún se está calculando
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
            StartCoroutine(DebugGoblin(goblin));
        }
        else Debug.Log("[Goblin] no hay NavMesh a menos de 0.5 m del portal");
    }

    IEnumerator DebugGoblin(GameObject g)
    {
        yield return new WaitForSeconds(1f);
        if (g == null) yield break;
        var ag = g.GetComponent<NavMeshAgent>();
        if (ag == null) { Debug.Log("[Goblin] sin NavMeshAgent"); yield break; }
        Debug.Log("[Goblin] onMesh=" + ag.isOnNavMesh + " hasPath=" + ag.hasPath + " status=" + ag.pathStatus +
                  " pending=" + ag.pathPending + " resta=" + ag.remainingDistance.ToString("F2") +
                  " vel=" + ag.velocity.magnitude.ToString("F2") + " speed=" + ag.speed +
                  " radio=" + ag.radius + " tipo=" + ag.agentTypeID + " stopped=" + ag.isStopped);
        // Dibuja en la Scene/Game (Gizmos): camino del goblin en ROJO, destino alcanzable en AMARILLO
        var cs = ag.path.corners;
        for (int i = 0; i + 1 < cs.Length; i++) Debug.DrawLine(cs[i] + Vector3.up * 0.02f, cs[i + 1] + Vector3.up * 0.02f, Color.red, 30f);
        if (NavMesh.SamplePosition(crystal.position, out NavMeshHit hc, 0.5f, NavMesh.AllAreas))
            Debug.DrawLine(hc.position, hc.position + Vector3.up * 0.5f, Color.yellow, 30f);
        if (NavMesh.SamplePosition(portal.position, out NavMeshHit hp, 0.5f, NavMesh.AllAreas))
            Debug.DrawLine(hp.position, hp.position + Vector3.up * 0.5f, Color.magenta, 30f);
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
        ClearArrows();
        state = result;
        placingTower = false;
        if (result == State.Lost) crystal.gameObject.SetActive(false);
        StopAllCoroutines();
        foreach (var g in FindObjectsByType<GoblinMover>(FindObjectsSortMode.None))
            Destroy(g.gameObject);
    }

    void ResetGame()
    {
        ClearArrows();
        popups.Clear();
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
                popups.Add(new Popup { pos = coins[i].t.position, t0 = Time.time, amount = goldPerCoin });
                Destroy(coins[i].t.gameObject);
                coins.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    void Update()
    {
        DrawNavMesh();
        UpdateAim();
        UpdateArrows();
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

    // ---------- Puntería ----------

    GoblinMover FindGoblinInCrosshair(Ray ray)
    {
        GoblinMover best = null;
        float bestDist = float.MaxValue;
        foreach (var g in FindObjectsByType<GoblinMover>(FindObjectsSortMode.None))
        {
            if (g.IsDone) continue;
            Vector3 toG = g.transform.position + Vector3.up * 0.02f - ray.origin;
            float along = Vector3.Dot(ray.direction, toG);
            if (along <= 0f || along > maxShootDistance) continue;
            if (Vector3.Cross(ray.direction, toG).magnitude > aimRadius) continue;
            if (along < bestDist) { best = g; bestDist = along; }
        }
        return best;
    }

    void UpdateAim()
    {
        aimingAtGoblin = false;
        if (state != State.Ready && state != State.Playing) return;
        Camera cam = GetCam();
        if (cam == null) return;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        TryCollectCoin(ray);                    // moneda bajo la mira: se recoge sola
        if (state != State.Playing) return;

        var target = FindGoblinInCrosshair(ray);
        aimingAtGoblin = target != null;
        if (target != null && Time.time >= nextShotTime)
        {
            nextShotTime = Time.time + arrowCooldown;
            FireArrow(cam, target);
        }
    }

    void FireArrow(Camera cam, GoblinMover target)
    {
        var a = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        a.name = "Arrow";
        Destroy(a.GetComponent<Collider>());
        a.transform.localScale = new Vector3(0.008f, 0.03f, 0.008f);
        a.GetComponent<Renderer>().material.color = new Color(0.95f, 0.95f, 0.8f);
        a.transform.position = cam.transform.position + cam.transform.forward * 0.15f - cam.transform.up * 0.05f;
        arrows.Add(new Arrow { t = a.transform, target = target });
    }

    void UpdateArrows()
    {
        for (int i = arrows.Count - 1; i >= 0; i--)
        {
            var a = arrows[i];
            if (a.t == null) { arrows.RemoveAt(i); continue; }
            if (a.target == null || a.target.IsDone)
            {
                Destroy(a.t.gameObject);
                arrows.RemoveAt(i);
                continue;
            }
            Vector3 goal = a.target.transform.position + Vector3.up * 0.02f;
            Vector3 to = goal - a.t.position;
            float step = arrowSpeed * Time.deltaTime;
            if (to.magnitude <= step + 0.02f)
            {
                a.target.TakeDamage(arrowDamage);
                Destroy(a.t.gameObject);
                arrows.RemoveAt(i);
                continue;
            }
            a.t.position += to.normalized * step;
            a.t.rotation = Quaternion.LookRotation(to.normalized) * Quaternion.Euler(90f, 0f, 0f);
        }
    }

    void ClearArrows()
    {
        foreach (var a in arrows)
            if (a.t != null) Destroy(a.t.gameObject);
        arrows.Clear();
    }

    void DrawCrosshair(float s)
    {
        if (state != State.Ready && state != State.Playing) return;
        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        float len = 28 * s, th = 4 * s, gap = 10 * s;
        bool ready = Time.time >= nextShotTime;

        Color old = GUI.color;
        GUI.color = aimingAtGoblin ? Color.red : (ready ? Color.white : new Color(1f, 1f, 1f, 0.45f));
        GUI.DrawTexture(new Rect(cx - gap - len, cy - th / 2f, len, th), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + gap, cy - th / 2f, len, th), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - th / 2f, cy - gap - len, th, len), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - th / 2f, cy + gap, th, len), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - th, cy - th, th * 2f, th * 2f), Texture2D.whiteTexture);
        GUI.color = old;
    }

    // "+10" dorado que sube y se desvanece donde estaba la moneda
    void DrawPopups(float s)
    {
        Camera cam = GetCam();
        if (cam == null) return;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = (int)(64 * s),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        Color old = GUI.color;
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            float age = Time.time - popups[i].t0;
            if (age > 1f) { popups.RemoveAt(i); continue; }
            Vector3 p = cam.WorldToScreenPoint(popups[i].pos);
            if (p.z <= 0) continue;
            float x = p.x;
            float y = Screen.height - p.y - age * 120f * s;
            GUI.color = new Color(1f, 0.85f, 0.1f, 1f - age);
            GUI.Label(new Rect(x - 100 * s, y - 40 * s, 200 * s, 80 * s), "+" + popups[i].amount, style);
        }
        GUI.color = old;
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
        if (mapLocked) StartCoroutine(CheckTowerBlocks(t));
    }

    IEnumerator CheckTowerBlocks(GameObject t)
    {
        yield return new WaitForSeconds(0.7f);   // el carving del NavMesh es asíncrono
        if (t == null) yield break;
        if (pathWasOk && !PathOk())   // solo si el mapa tenía camino antes de la torre
        {
            towers.Remove(t);
            Destroy(t);
            gold += towerCost;
            blockedMsg = "Can't block the path!";
            blockedUntil = Time.time + 2f;
        }
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
            if (autoMode)
            {
                TryPickPlane(new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y));
                return;
            }
            Plane plane = new Plane(Vector3.up, transform.position);
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 p = ray.GetPoint(dist);
                transform.position = new Vector3(p.x, transform.position.y, p.z);
            }
            return;
        }

        // Las monedas ya no se recogen tocando: se recogen apuntando con la mira
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

    public bool autoRetryMap = true;    // true: si no hay camino, prueba otros diseños automáticamente
    public bool debugNavMesh = true;   // dibuja el NavMesh (Scene view, o Game view con Gizmos activado)

    void DrawNavMesh()
    {
        if (!debugNavMesh || !mapLocked) return;
        var tri = NavMesh.CalculateTriangulation();
        for (int i = 0; i + 2 < tri.indices.Length; i += 3)
        {
            Vector3 p0 = tri.vertices[tri.indices[i]] + Vector3.up * 0.01f;
            Vector3 p1 = tri.vertices[tri.indices[i + 1]] + Vector3.up * 0.01f;
            Vector3 p2 = tri.vertices[tri.indices[i + 2]] + Vector3.up * 0.01f;
            Debug.DrawLine(p0, p1, Color.cyan);
            Debug.DrawLine(p1, p2, Color.cyan);
            Debug.DrawLine(p2, p0, Color.cyan);
        }
    }

    void OnGUI()
    {
        if (Time.time < blockedUntil)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.04f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.red;
            GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, Screen.height * 0.08f), blockedMsg, st);
        }
        float s = Mathf.Min(Screen.width, Screen.height) / 1080f;
        DrawCrystalBar(s);
        DrawCrosshair(s);
        DrawPopups(s);

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
            string setupMsg;
            if (autoMode)
                setupMsg = planePicked
                    ? $"Table: {tableSize.x * 100f:0} x {tableSize.y * 100f:0} cm. Tap again to change the surface or shuffle the map, or press OK."
                    : scanMsg;
            else
                setupMsg = "Tap the table to move the board. Rotate and resize it, then press OK.";

            GUI.Box(new Rect(10 * s, 190 * s, Screen.width - 20 * s, 130 * s), "");
            GUI.Label(new Rect(30 * s, 200 * s, Screen.width - 60 * s, 120 * s), setupMsg, hint);

            if (autoMode)
            {
                if (GUI.Button(new Rect(rowRect.x, rowRect.y, cw, ch), "MANUAL", button))
                {
                    ClearForest();
                    SetVisible(true);
                    BuildBorder();
                    planePicked = false;
                    autoMode = false;
                }
                GUI.enabled = planePicked;
                if (GUI.Button(mainRect, "OK", button))
                    ConfirmBoard();
                GUI.enabled = true;
                return;
            }

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
