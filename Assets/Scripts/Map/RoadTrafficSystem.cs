using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pixel-based traffic simulation painted directly onto the map texture.
/// Cars are small colored dots (2–3px) that ride along highway and branch paths.
///
/// Setup:
///   1. Attach to the same GameObject as MapPainter (or any persistent GO).
///   2. Assign mapPainter and roadGenerator references.
///   3. The system auto-starts after roads are generated.
///
/// How it works:
///   - On road generation, snapshots the "clean" map texture (roads drawn, no cars).
///   - Each frame, erases previous car positions (restoring snapshot pixels),
///     advances cars along their paths, and paints new car pixels.
///   - Texture2D.Apply() is batched — called once per frame, not per car.
/// </summary>
public class RoadTrafficSystem : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // INSPECTOR
    // -------------------------------------------------------------------------

    [Header("References")]
    public MapPainter mapPainter;
    public RoadGenerator roadGenerator;

    [Header("Spawn Rates")]
    [Tooltip("Cars per 100 path-pixels on the highway.")]
    [Range(0.5f, 5f)] public float highwayCarDensity = 2f;

    [Tooltip("Cars per 100 path-pixels on branches.")]
    [Range(0.1f, 3f)] public float branchCarDensity = 0.8f;

    [Header("Car Appearance")]
    [Tooltip("Car dot radius in pixels. 1 = 3×3 diamond, 0 = single pixel.")]
    [Range(0, 2)] public int carRadius = 1;

    [Tooltip("Possible car body colors. Picked at random per car.")]
    public Color[] carColors = new Color[]
    {
        new Color(0.85f, 0.20f, 0.15f), // red
        new Color(0.15f, 0.45f, 0.85f), // blue
        new Color(0.95f, 0.85f, 0.25f), // yellow
        new Color(0.90f, 0.90f, 0.88f), // white
        new Color(0.20f, 0.20f, 0.22f), // dark
    };

    [Header("Speed")]
    [Tooltip("Base speed in path-pixels per second.")]
    [Range(5f, 80f)] public float baseSpeed = 30f;

    [Tooltip("Random speed variation ±%.")]
    [Range(0f, 0.5f)] public float speedVariation = 0.2f;

    [Header("Performance")]
    [Tooltip("How many frames between texture Apply() calls. 1 = every frame.")]
    [Range(1, 4)] public int applyInterval = 2;

    // -------------------------------------------------------------------------
    // INTERNAL
    // -------------------------------------------------------------------------

    private struct Car
    {
        public int pathIndex;       // index into allPaths
        public float position;      // 0…pathLength-1 (fractional pixel index)
        public float speed;         // pixels per second (signed: + forward, - backward)
        public Color color;
        public Vector2Int lastPixel;
    }

    private List<List<Vector2Int>> allPaths = new List<List<Vector2Int>>();
    private List<Car> cars = new List<Car>();

    private Texture2D mapTexture;
    private Color[] basePixels;       // snapshot of the map without cars
    private int texW, texH;
    private int frameCounter;
    private bool active = false;

    // reusable buffer for pixels to restore each frame
    private List<Vector2Int> dirtyPixels = new List<Vector2Int>();

    // -------------------------------------------------------------------------
    // LIFECYCLE
    // -------------------------------------------------------------------------

    void OnEnable()
    {
        RoadGenerator.OnRoadsGenerated += HandleRoadsGenerated;
    }

    void OnDisable()
    {
        RoadGenerator.OnRoadsGenerated -= HandleRoadsGenerated;
    }

    void HandleRoadsGenerated()
    {
        // Small delay: MapPainter calls decorPlacer after roads,
        // so we wait one frame to snapshot the final texture.
        StartCoroutine(InitAfterFrame());
    }

    System.Collections.IEnumerator InitAfterFrame()
    {
        yield return null; // let MapPainter + decor finish
        Initialize();
    }

    // -------------------------------------------------------------------------
    // INITIALIZATION
    // -------------------------------------------------------------------------

    void Initialize()
    {
        if (roadGenerator == null || mapPainter == null) return;

        // Grab the map texture from the SpriteRenderer
        SpriteRenderer sr = mapPainter.mapRenderer;
        if (sr == null || sr.sprite == null) return;

        mapTexture = sr.sprite.texture;
        texW = mapTexture.width;
        texH = mapTexture.height;

        // Snapshot base pixels (roads painted, no cars)
        basePixels = mapTexture.GetPixels();

        // Collect all road paths
        allPaths.Clear();
        cars.Clear();
        dirtyPixels.Clear();

        var highways = roadGenerator.GetHighwaySegments();
        if (highways != null)
        {
            foreach (var seg in highways)
                if (seg != null && seg.Count >= 10)
                    allPaths.Add(seg);
        }

        var branches = roadGenerator.GetBranchPaths();
        if (branches != null)
        {
            foreach (var seg in branches)
                if (seg != null && seg.Count >= 10)
                    allPaths.Add(seg);
        }

        // Spawn cars on each path
        int highwayPathCount = highways != null ? highways.Count : 0;

        for (int p = 0; p < allPaths.Count; p++)
        {
            float density = (p < highwayPathCount) ? highwayCarDensity : branchCarDensity;
            int count = Mathf.Max(1, Mathf.RoundToInt(allPaths[p].Count / 100f * density));

            for (int c = 0; c < count; c++)
            {
                float pos = Random.Range(0f, allPaths[p].Count - 1f);
                float spd = baseSpeed * Random.Range(1f - speedVariation, 1f + speedVariation);
                if (Random.value > 0.5f) spd = -spd; // half go in reverse direction

                Color col = carColors.Length > 0
                    ? carColors[Random.Range(0, carColors.Length)]
                    : Color.red;

                int idx = Mathf.Clamp(Mathf.RoundToInt(pos), 0, allPaths[p].Count - 1);

                cars.Add(new Car
                {
                    pathIndex = p,
                    position = pos,
                    speed = spd,
                    color = col,
                    lastPixel = allPaths[p][idx]
                });
            }
        }

        frameCounter = 0;
        active = true;
    }

    // -------------------------------------------------------------------------
    // UPDATE LOOP
    // -------------------------------------------------------------------------

    void Update()
    {
        if (!active || cars.Count == 0) return;

        float dt = Time.deltaTime;

        // 1) Erase previous car pixels → restore from base snapshot
        foreach (var px in dirtyPixels)
        {
            int idx = px.x + px.y * texW;
            if (idx >= 0 && idx < basePixels.Length)
                mapTexture.SetPixel(px.x, px.y, basePixels[idx]);
        }
        dirtyPixels.Clear();

        // 2) Advance and paint each car
        for (int i = 0; i < cars.Count; i++)
        {
            Car car = cars[i];
            List<Vector2Int> path = allPaths[car.pathIndex];
            int pathLen = path.Count;

            // Advance position
            car.position += car.speed * dt;

            // Bounce at ends
            if (car.position >= pathLen - 1)
            {
                car.position = pathLen - 1;
                car.speed = -Mathf.Abs(car.speed);
            }
            else if (car.position < 0)
            {
                car.position = 0;
                car.speed = Mathf.Abs(car.speed);
            }

            int idx = Mathf.Clamp(Mathf.RoundToInt(car.position), 0, pathLen - 1);
            Vector2Int center = path[idx];
            car.lastPixel = center;
            cars[i] = car;

            // Paint car dot
            PaintCarDot(center.x, center.y, car.color);
        }

        // 3) Apply texture (batched)
        frameCounter++;
        if (frameCounter >= applyInterval)
        {
            mapTexture.Apply();
            frameCounter = 0;
        }
    }

    // -------------------------------------------------------------------------
    // PAINTING
    // -------------------------------------------------------------------------

    void PaintCarDot(int cx, int cy, Color color)
    {
        if (carRadius <= 0)
        {
            // Single pixel
            if (cx >= 0 && cx < texW && cy >= 0 && cy < texH)
            {
                mapTexture.SetPixel(cx, cy, color);
                dirtyPixels.Add(new Vector2Int(cx, cy));
            }
            return;
        }

        // Diamond shape within radius
        for (int dx = -carRadius; dx <= carRadius; dx++)
        {
            for (int dy = -carRadius; dy <= carRadius; dy++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > carRadius) continue;
                int px = cx + dx, py = cy + dy;
                if (px < 0 || px >= texW || py < 0 || py >= texH) continue;

                mapTexture.SetPixel(px, py, color);
                dirtyPixels.Add(new Vector2Int(px, py));
            }
        }
    }

    // -------------------------------------------------------------------------
    // PUBLIC API
    // -------------------------------------------------------------------------

    /// <summary>Stop all traffic and restore the clean map texture.</summary>
    public void StopTraffic()
    {
        if (!active) return;
        active = false;

        // Restore all dirty pixels
        foreach (var px in dirtyPixels)
        {
            int idx = px.x + px.y * texW;
            if (idx >= 0 && idx < basePixels.Length)
                mapTexture.SetPixel(px.x, px.y, basePixels[idx]);
        }
        dirtyPixels.Clear();
        mapTexture.Apply();
    }

    /// <summary>Resume traffic after StopTraffic().</summary>
    public void ResumeTraffic()
    {
        if (cars.Count > 0) active = true;
    }

    /// <summary>Fully clear and re-initialize (e.g., after map regeneration).</summary>
    public void Reinitialize()
    {
        StopTraffic();
        cars.Clear();
        allPaths.Clear();
        Initialize();
    }

    /// <summary>Toggle visibility — used by UndergroundMapManager etc.</summary>
    public void SetTrafficVisible(bool visible)
    {
        if (visible) ResumeTraffic();
        else StopTraffic();
    }
}