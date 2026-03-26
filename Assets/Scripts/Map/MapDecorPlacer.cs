using System.Collections.Generic;
using UnityEngine;

public class MapDecorPlacer : MonoBehaviour
{
    [Header("General Decor")]
    [Range(8, 64)]  public int   cellSize      = 14;
    public float pixelsPerUnit                  = 100f;
    public float spriteZ                        = -0.5f;

    [Header("Spawn Rates — Per Region")]
    [Range(0, 16)] public int citiesSpawnRate       = 2;
    [Range(0, 16)] public int agriculturalSpawnRate = 2;
    [Range(0, 16)] public int urbanSpawnRate        = 2;
    [Range(0, 16)] public int industrialSpawnRate   = 2;

    [Header("Sprite Scale")]
    public Vector2 spriteScaleRange = new Vector2(0.75f, 1.25f);

    [Header("Cities Decor — Road-Aware Placement")]
    [Range(0, 20)]     public int   cityShoreBuffer             = 3;
    [Range(0, 20)]     public int   cityRegionBorderBuffer      = 5;
    public bool                     citySnapRotation            = false;
    [Range(0.05f, 2f)] public float overlapRadius               = 0.3f;
    [Range(0, 30)]     public int   cityBuildingMaxRoadDistance = 8;
    [Range(0f, 1f)]    public float cityRoadAffinityStrength    = 0.7f;

    [Header("Broken Building Sprites")]
    [Tooltip("Sprites randomly picked when a city building is cracked by an earthquake.")]
    public List<Sprite> brokenBuildingSprites = new List<Sprite>();
    [Tooltip("Tint applied to broken buildings.")]
    public Color brokenBuildingTint = new Color(0.55f, 0.45f, 0.40f, 1f);

    [Header("Day / Night Building Sprites")]
    [Tooltip("Night variants of citiesDecor (lights on). Index-matched to citiesDecor.")]
    public List<Sprite> citiesDecorNight = new List<Sprite>();
    [Tooltip("Night variants of broken building sprites. Index-matched to brokenBuildingSprites.")]
    public List<Sprite> brokenBuildingSpritesNight = new List<Sprite>();

    // =========================================================================
    // PORT SETTINGS
    // =========================================================================

    [Header("Port Settings")]
    [Tooltip("Maximum number of ports to spawn on city shorelines.")]
    [Range(0, 8)] public int maxPortCount = 2;
    [Tooltip("Port day sprites. Each index is a port variant.")]
    public List<Sprite> portSpritesDay = new List<Sprite>();
    [Tooltip("Port night sprites. Index-matched to portSpritesDay.")]
    public List<Sprite> portSpritesNight = new List<Sprite>();
    [Tooltip("Scale applied to port sprites.")]
    public Vector2 portScaleRange = new Vector2(0.9f, 1.2f);
    [Tooltip("Minimum tiles of city biome surrounding a port candidate.")]
    [Range(1, 10)] public int portCityBackingRadius = 3;
    [Tooltip("Minimum distance in tiles between two ports.")]
    [Range(10, 80)] public int portMinSeparation = 30;

    // =========================================================================
    // SHIP SETTINGS
    // =========================================================================

    [Header("Ship Settings")]
    [Tooltip("Maximum number of ships active at once across all ports.")]
    [Range(0, 20)] public int maxActiveShips = 4;
    [Tooltip("Ship day sprites. Each index is a ship variant.")]
    public List<Sprite> shipSpritesDay = new List<Sprite>();
    [Tooltip("Ship night sprites. Index-matched to shipSpritesDay.")]
    public List<Sprite> shipSpritesNight = new List<Sprite>();
    [Tooltip("Ship movement speed in world units per second.")]
    [Range(0.05f, 2f)] public float shipSpeed = 0.3f;
    [Tooltip("Scale applied to ship sprites.")]
    public Vector2 shipScaleRange = new Vector2(0.6f, 1.0f);
    [Tooltip("Seconds a ship waits at port before departing.")]
    public Vector2 shipWaitTimeRange = new Vector2(3f, 8f);
    [Tooltip("Seconds between ship spawn attempts.")]
    [Range(1f, 30f)] public float shipSpawnInterval = 5f;
    [Tooltip("How many tiles to downsample the pathfinding grid. Higher = faster but coarser.")]
    [Range(2, 16)] public int shipPathGridStep = 4;
    [Tooltip("Minimum clearance in tiles from land for ship waypoints.")]
    [Range(1, 10)] public int shipLandClearance = 3;

    // -------------------------------------------------------------------------

    private struct BuildingData
    {
        public GameObject      go;
        public SpriteRenderer  dayRenderer;
        public SpriteRenderer  nightRenderer;
        public int             tileX, tileY;
        public bool            isBroken;
        public int             spriteIndex;
        public int             brokenIndex;
        public float           baseAlpha;
    }

    private struct PortData
    {
        public GameObject     go;
        public SpriteRenderer dayRenderer;
        public SpriteRenderer nightRenderer;
        public int            tileX, tileY;
        public float          baseAlpha;
        public Vector3        worldPos;
    }

    private enum ShipState { Arriving, Waiting, Departing, Done }

    private class ShipInstance
    {
        public GameObject     go;
        public SpriteRenderer dayRenderer;
        public SpriteRenderer nightRenderer;
        public float          baseAlpha;
        public float          scale;
        public ShipState      state;
        public List<Vector3>  path;
        public int            pathIndex;
        public float          waitTimer;
        public int            portIndex;     // which port this ship targets
        public float          speed;
    }

    private List<GameObject>   decorObjects    = new List<GameObject>();
    private List<Vector2>      occupiedCenters = new List<Vector2>();
    private List<BuildingData> cityBuildings   = new List<BuildingData>();
    private List<PortData>     ports           = new List<PortData>();
    private List<ShipInstance> activeShips     = new List<ShipInstance>();

    private DayNightCycle dayNight;
    private float         prevRatio = -1f;

    // Ship spawning timer
    private float shipSpawnTimer;

    // Cached references for ship pathfinding
    private MapGenerator cachedMap;
    private float        cachedHalfW;
    private float        cachedHalfH;

    // Downsampled water navigation grid for pathfinding
    private bool[,] navGrid;       // true = navigable water
    private int     navW, navH;    // dimensions of the nav grid

    // -------------------------------------------------------------------------
    // ENTRY POINT
    // -------------------------------------------------------------------------

    public void Repaint(MapGenerator map, BiomePaintSettings settings, Texture2D mapTexture)
    {
        Clear();
        if (settings == null) { Debug.LogError("MapDecorPlacer: settings is null!"); return; }

        dayNight  = DayNightCycle.Instance;
        cachedMap = map;

        int scaledCellSize = Mathf.Max(cellSize, Mathf.RoundToInt(cellSize * (map.width / 256f)));
        int cellArea       = scaledCellSize * scaledCellSize;
        float halfW = map.width  * 0.5f / pixelsPerUnit;
        float halfH = map.height * 0.5f / pixelsPerUnit;
        cachedHalfW = halfW;
        cachedHalfH = halfH;

        var cityTilePool   = new List<Vector2Int>();
        var biomeTilePools = new Dictionary<int, List<Vector2Int>>();

        for (int x = 0; x < map.width; x++)
        for (int y = 0; y < map.height; y++)
        {
            if (!map.IsLand(x, y)) continue;
            if (map.GetFog(x, y) > 0.6f) continue;
            int b = map.GetBiome(x, y);
            if (b == 2)
            {
                if (cityRegionBorderBuffer == 0 || IsInsideRegion(map, x, y, cityRegionBorderBuffer))
                    cityTilePool.Add(new Vector2Int(x, y));
            }
            else
            {
                if (!biomeTilePools.ContainsKey(b))
                    biomeTilePools[b] = new List<Vector2Int>();
                biomeTilePools[b].Add(new Vector2Int(x, y));
            }
        }

        int cityAttempts = (cityTilePool.Count / Mathf.Max(1, cellArea)) * citiesSpawnRate;
        for (int attempt = 0; attempt < cityAttempts; attempt++)
        {
            if (cityTilePool.Count == 0) break;
            Vector2Int tile = cityTilePool[Random.Range(0, cityTilePool.Count)];
            TryPlaceCityBuilding(map, settings, tile.x, tile.y, halfW, halfH);
        }

        foreach (var kvp in biomeTilePools)
        {
            int biome = kvp.Key;
            var pool  = kvp.Value;
            int spawnRate = GetSpawnRate(biome);
            if (spawnRate == 0) continue;
            int decorAttempts = (pool.Count / Mathf.Max(1, cellArea)) * spawnRate;
            for (int attempt = 0; attempt < decorAttempts; attempt++)
            {
                Vector2Int tile = pool[Random.Range(0, pool.Count)];
                TryPlaceNatureDecor(map, settings, biome, tile.x, tile.y, halfW, halfH);
            }
        }

        // --- Port placement ---
        PlacePorts(map, halfW, halfH);

        // --- Build navigation grid for ships ---
        BuildNavGrid(map);

        // Apply initial crossfade state
        if (dayNight != null)
            ApplyCrossfade(dayNight.LightingRatio);

        shipSpawnTimer = 0f;

        Debug.Log($"MapDecorPlacer: decor={decorObjects.Count}, cityBuildings={cityBuildings.Count}, ports={ports.Count}");
    }

    int GetSpawnRate(int biome)
    {
        switch (biome)
        {
            case 1: return agriculturalSpawnRate;
            case 3: return industrialSpawnRate;
            case 4: return urbanSpawnRate;
            default: return 0;
        }
    }

    // -------------------------------------------------------------------------
    // DAY / NIGHT CROSSFADE
    // -------------------------------------------------------------------------

    void Update()
    {
        if (dayNight == null) dayNight = DayNightCycle.Instance;

        float ratio = (dayNight != null) ? dayNight.LightingRatio : 0f;

        // Crossfade buildings + ports
        if (cityBuildings.Count > 0 || ports.Count > 0)
        {
            if (Mathf.Abs(ratio - prevRatio) > 0.005f)
            {
                prevRatio = ratio;
                ApplyCrossfade(ratio);
            }
        }

        // Ship tick
        UpdateShips(ratio);
    }

    void ApplyCrossfade(float ratio)
    {
        // Buildings
        for (int i = 0; i < cityBuildings.Count; i++)
        {
            BuildingData bd = cityBuildings[i];
            if (bd.dayRenderer == null) continue;

            float baseA = bd.baseAlpha;

            Color dc      = bd.dayRenderer.color;
            dc.a          = baseA * (1f - ratio);
            bd.dayRenderer.color = dc;

            if (bd.nightRenderer != null)
            {
                Color nc = bd.nightRenderer.color;
                nc.a     = baseA * ratio;
                bd.nightRenderer.color = nc;
            }
        }

        // Ports
        for (int i = 0; i < ports.Count; i++)
        {
            PortData pd = ports[i];
            if (pd.dayRenderer == null) continue;

            float baseA = pd.baseAlpha;
            Color dc = pd.dayRenderer.color;
            dc.a = baseA * (1f - ratio);
            pd.dayRenderer.color = dc;

            if (pd.nightRenderer != null)
            {
                Color nc = pd.nightRenderer.color;
                nc.a = baseA * ratio;
                pd.nightRenderer.color = nc;
            }
        }

        // Ships
        for (int i = 0; i < activeShips.Count; i++)
        {
            ShipInstance ship = activeShips[i];
            if (ship.dayRenderer == null) continue;

            float baseA = ship.baseAlpha;
            Color dc = ship.dayRenderer.color;
            dc.a = baseA * (1f - ratio);
            ship.dayRenderer.color = dc;

            if (ship.nightRenderer != null)
            {
                Color nc = ship.nightRenderer.color;
                nc.a = baseA * ratio;
                ship.nightRenderer.color = nc;
            }
        }
    }

    // -------------------------------------------------------------------------
    // VISIBILITY
    // -------------------------------------------------------------------------

    public void SetDecorVisible(bool visible)
    {
        foreach (var go in decorObjects)
            if (go != null) go.SetActive(visible);
    }

    // -------------------------------------------------------------------------
    // EARTHQUAKE — SPRITE SWAP
    // -------------------------------------------------------------------------

    public int MarkBuildingsBroken(HashSet<Vector2Int> crackedTiles)
    {
        if (crackedTiles == null || crackedTiles.Count == 0) return 0;

        int count = 0;
        for (int i = 0; i < cityBuildings.Count; i++)
        {
            BuildingData bd = cityBuildings[i];
            if (bd.isBroken) continue;
            if (!crackedTiles.Contains(new Vector2Int(bd.tileX, bd.tileY))) continue;

            if (bd.dayRenderer == null) continue;

            int brokenIdx = -1;
            if (brokenBuildingSprites != null && brokenBuildingSprites.Count > 0)
            {
                brokenIdx = Random.Range(0, brokenBuildingSprites.Count);

                bd.dayRenderer.sprite = brokenBuildingSprites[brokenIdx];

                if (bd.nightRenderer != null
                    && brokenBuildingSpritesNight != null
                    && brokenIdx < brokenBuildingSpritesNight.Count
                    && brokenBuildingSpritesNight[brokenIdx] != null)
                {
                    bd.nightRenderer.sprite = brokenBuildingSpritesNight[brokenIdx];
                }
                else if (bd.nightRenderer != null)
                {
                    bd.nightRenderer.sprite = brokenBuildingSprites[brokenIdx];
                }
            }

            bd.dayRenderer.color = new Color(
                brokenBuildingTint.r, brokenBuildingTint.g, brokenBuildingTint.b,
                bd.dayRenderer.color.a);

            if (bd.nightRenderer != null)
                bd.nightRenderer.color = new Color(
                    brokenBuildingTint.r, brokenBuildingTint.g, brokenBuildingTint.b,
                    bd.nightRenderer.color.a);

            bd.isBroken    = true;
            bd.brokenIndex = brokenIdx;
            cityBuildings[i] = bd;
            count++;
        }

        if (count > 0)
            Debug.Log($"MapDecorPlacer: {count} building(s) marked broken.");

        return count;
    }

    public bool IsBuildingBroken(int tileX, int tileY)
    {
        foreach (var bd in cityBuildings)
            if (bd.tileX == tileX && bd.tileY == tileY)
                return bd.isBroken;
        return false;
    }

    public List<Vector2Int> GetBrokenBuildingTiles()
    {
        var result = new List<Vector2Int>();
        foreach (var bd in cityBuildings)
            if (bd.isBroken) result.Add(new Vector2Int(bd.tileX, bd.tileY));
        return result;
    }

    // -------------------------------------------------------------------------
    // EARTHQUAKE — FULL DESTRUCTION
    // -------------------------------------------------------------------------

    public void DestroyBuildingsOnFaultLines(FaultLineGenerator faultGen)
    {
        int destroyed = 0;
        for (int i = cityBuildings.Count - 1; i >= 0; i--)
        {
            BuildingData bd = cityBuildings[i];
            if (!faultGen.IsFault(bd.tileX, bd.tileY)) continue;
            decorObjects.Remove(bd.go);
            if (bd.go != null) Destroy(bd.go);
            cityBuildings.RemoveAt(i);
            destroyed++;
        }
        Debug.Log($"MapDecorPlacer: {destroyed} building(s) destroyed by fault lines.");
    }

    public void DestroyBuildingsInRadius(FaultLineGenerator faultGen, Vector2Int epicenter, int radius)
    {
        int destroyed = 0;
        int r2        = radius * radius;
        for (int i = cityBuildings.Count - 1; i >= 0; i--)
        {
            BuildingData bd = cityBuildings[i];
            int dx = bd.tileX - epicenter.x, dy = bd.tileY - epicenter.y;
            if (dx * dx + dy * dy > r2)                continue;
            if (!faultGen.IsFault(bd.tileX, bd.tileY)) continue;
            decorObjects.Remove(bd.go);
            if (bd.go != null) Destroy(bd.go);
            cityBuildings.RemoveAt(i);
            destroyed++;
        }
        Debug.Log($"MapDecorPlacer: {destroyed} building(s) destroyed by earthquake.");
    }

    // -------------------------------------------------------------------------
    // PLACEMENT — CITIES
    // -------------------------------------------------------------------------

    void TryPlaceCityBuilding(MapGenerator map, BiomePaintSettings settings,
                              int tx, int ty, float halfW, float halfH)
    {
        if (settings.citiesDecor == null || settings.citiesDecor.Count == 0) return;
        if (cityShoreBuffer > 0 && !HasShoreBuffer(map, tx, ty)) return;

        if (cityBuildingMaxRoadDistance > 0 && RoadGenerator.Instance != null && RoadGenerator.Instance.IsGenerated)
        {
            int roadDist = RoadGenerator.Instance.GetDistanceToRoad(tx, ty);
            if (roadDist > cityBuildingMaxRoadDistance) return;
            if (cityRoadAffinityStrength > 0f && roadDist > 0)
            {
                float normalizedDist = (float)roadDist / cityBuildingMaxRoadDistance;
                float spawnChance    = 1f - (normalizedDist * cityRoadAffinityStrength);
                if (Random.value > spawnChance) return;
            }
        }

        int spriteIdx = Random.Range(0, settings.citiesDecor.Count);
        Sprite daySprite = settings.citiesDecor[spriteIdx];
        if (daySprite == null) return;

        float wx = transform.position.x + (tx / pixelsPerUnit) - halfW;
        float wy = transform.position.y + (ty / pixelsPerUnit) - halfH;
        if (IsOverlapping(wx, wy)) return;
        occupiedCenters.Add(new Vector2(wx, wy));

        float scale    = Random.Range(spriteScaleRange.x, spriteScaleRange.y);
        float baseA    = Random.Range(0.85f, 1f);
        int sortOrder  = 10 + (int)(wy * -100f);

        GameObject go = new GameObject("CityBuilding");
        go.transform.SetParent(transform);
        go.transform.position   = new Vector3(wx, wy, spriteZ);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        if (citySnapRotation)
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0, 4) * 90f);

        SpriteRenderer daySR = go.AddComponent<SpriteRenderer>();
        daySR.sprite       = daySprite;
        daySR.sortingOrder = sortOrder;
        daySR.flipX        = false;
        daySR.color        = new Color(1f, 1f, 1f, baseA);

        SpriteRenderer nightSR = null;
        Sprite nightSprite = GetNightCitySprite(spriteIdx);
        if (nightSprite != null)
        {
            GameObject nightGo = new GameObject("NightOverlay");
            nightGo.transform.SetParent(go.transform, false);
            nightGo.transform.localPosition = Vector3.zero;
            nightGo.transform.localScale    = Vector3.one;
            nightGo.transform.localRotation = Quaternion.identity;

            nightSR              = nightGo.AddComponent<SpriteRenderer>();
            nightSR.sprite       = nightSprite;
            nightSR.sortingOrder = sortOrder + 1;
            nightSR.flipX        = false;
            nightSR.color        = new Color(1f, 1f, 1f, 0f);
        }

        decorObjects.Add(go);
        cityBuildings.Add(new BuildingData
        {
            go            = go,
            dayRenderer   = daySR,
            nightRenderer = nightSR,
            tileX         = tx,
            tileY         = ty,
            isBroken      = false,
            spriteIndex   = spriteIdx,
            brokenIndex   = -1,
            baseAlpha     = baseA
        });
    }

    Sprite GetNightCitySprite(int index)
    {
        if (citiesDecorNight == null || index < 0 || index >= citiesDecorNight.Count)
            return null;
        return citiesDecorNight[index];
    }

    Sprite GetNightBrokenSprite(int index)
    {
        if (brokenBuildingSpritesNight == null || index < 0 || index >= brokenBuildingSpritesNight.Count)
            return null;
        return brokenBuildingSpritesNight[index];
    }

    bool HasShoreBuffer(MapGenerator map, int tx, int ty)
    {
        for (int dx = -cityShoreBuffer; dx <= cityShoreBuffer; dx++)
        for (int dy = -cityShoreBuffer; dy <= cityShoreBuffer; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > cityShoreBuffer) continue;
            if (!map.IsLand(tx + dx, ty + dy)) return false;
        }
        return true;
    }

    bool IsInsideRegion(MapGenerator map, int tx, int ty, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int nx = tx + dx, ny = ty + dy;
            if (nx < 0 || nx >= map.width || ny < 0 || ny >= map.height) return false;
            if (!map.IsLand(nx, ny) || map.GetBiome(nx, ny) != 2) return false;
        }
        return true;
    }

    bool IsOverlapping(float wx, float wy)
    {
        float minDist = overlapRadius * 2f;
        foreach (var c in occupiedCenters)
            if (Vector2.Distance(new Vector2(wx, wy), c) < minDist) return true;
        return false;
    }

    // -------------------------------------------------------------------------
    // PLACEMENT — NATURE DECOR
    // -------------------------------------------------------------------------

    void TryPlaceNatureDecor(MapGenerator map, BiomePaintSettings settings,
                             int biome, int tx, int ty, float halfW, float halfH)
    {
        Sprite sprite = PickDecorSprite(biome, settings);
        if (sprite == null) return;
        float scale = Random.Range(spriteScaleRange.x, spriteScaleRange.y);
        float wx    = transform.position.x + (tx / pixelsPerUnit) - halfW;
        float wy    = transform.position.y + (ty / pixelsPerUnit) - halfH;
        PlaceSimpleSprite("Decor", sprite, wx, wy, scale, Random.value > 0.5f, 2);
    }

    Sprite PickDecorSprite(int biome, BiomePaintSettings s)
    {
        List<Sprite> pool;
        switch (biome)
        {
            case 1: pool = s.agriculturalDecor; break;
            case 3: pool = s.industrialDecor;   break;
            case 4: pool = s.urbanDecor;        break;
            default: return null;
        }
        if (pool == null || pool.Count == 0) return null;
        return pool[Random.Range(0, pool.Count)];
    }

    GameObject PlaceSimpleSprite(string goName, Sprite sprite, float wx, float wy,
                                float scale, bool flipX, int sortOrder)
    {
        GameObject go = new GameObject(goName);
        go.transform.SetParent(transform);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.sortingOrder = sortOrder;
        sr.flipX        = flipX;
        sr.color        = new Color(1f, 1f, 1f, Random.Range(0.85f, 1f));
        go.transform.position   = new Vector3(wx, wy, spriteZ);
        go.transform.localScale = new Vector3(scale, scale, 1f);
        decorObjects.Add(go);
        return go;
    }

    // =========================================================================
    // PORT PLACEMENT
    // =========================================================================

    /// <summary>
    /// Finds city tiles adjacent to water (shore tiles) and places ports.
    /// Only spawns if the city region is NOT at the map edge (i.e. not touching
    /// the island boundary defined by fog / safe zone).
    /// </summary>
    void PlacePorts(MapGenerator map, float halfW, float halfH)
    {
        if (maxPortCount <= 0) return;
        if (portSpritesDay == null || portSpritesDay.Count == 0) return;

        // 1. Collect all city shore tiles (city biome tiles adjacent to water)
        List<Vector2Int> cityShore = new List<Vector2Int>();
        int edgeMargin = 8; // tiles from map edge considered "edge of island"

        for (int x = 0; x < map.width; x++)
        for (int y = 0; y < map.height; y++)
        {
            if (!map.IsLand(x, y)) continue;
            if (map.GetBiome(x, y) != 2) continue;
            if (map.GetFog(x, y) > 0.4f) continue;

            // Skip tiles too close to map edges
            if (x < edgeMargin || x >= map.width - edgeMargin ||
                y < edgeMargin || y >= map.height - edgeMargin)
                continue;

            // Must be adjacent to water (4-connected)
            if (!IsAdjacentToWater(map, x, y)) continue;

            // Must have enough city backing inland
            if (!HasCityBacking(map, x, y)) continue;

            cityShore.Add(new Vector2Int(x, y));
        }

        if (cityShore.Count == 0)
        {
            Debug.Log("MapDecorPlacer: No valid city shoreline for ports.");
            return;
        }

        // 2. Shuffle and greedily pick port sites with minimum separation
        ShuffleList(cityShore);
        List<Vector2Int> portTiles = new List<Vector2Int>();

        foreach (var tile in cityShore)
        {
            if (portTiles.Count >= maxPortCount) break;

            bool tooClose = false;
            foreach (var existing in portTiles)
            {
                int dx = tile.x - existing.x, dy = tile.y - existing.y;
                if (dx * dx + dy * dy < portMinSeparation * portMinSeparation)
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            portTiles.Add(tile);
        }

        // 3. Instantiate port GameObjects
        foreach (var tile in portTiles)
        {
            int spriteIdx = Random.Range(0, portSpritesDay.Count);
            Sprite daySprite = portSpritesDay[spriteIdx];
            if (daySprite == null) continue;

            float wx = transform.position.x + (tile.x / pixelsPerUnit) - halfW;
            float wy = transform.position.y + (tile.y / pixelsPerUnit) - halfH;

            float scale   = Random.Range(portScaleRange.x, portScaleRange.y);
            float baseA   = Random.Range(0.9f, 1f);
            int sortOrder = 12 + (int)(wy * -100f);

            GameObject go = new GameObject("Port");
            go.transform.SetParent(transform);
            go.transform.position   = new Vector3(wx, wy, spriteZ);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer daySR = go.AddComponent<SpriteRenderer>();
            daySR.sprite       = daySprite;
            daySR.sortingOrder = sortOrder;
            daySR.color        = new Color(1f, 1f, 1f, baseA);

            SpriteRenderer nightSR = null;
            if (portSpritesNight != null && spriteIdx < portSpritesNight.Count &&
                portSpritesNight[spriteIdx] != null)
            {
                GameObject nightGo = new GameObject("PortNight");
                nightGo.transform.SetParent(go.transform, false);
                nightGo.transform.localPosition = Vector3.zero;
                nightGo.transform.localScale    = Vector3.one;
                nightGo.transform.localRotation = Quaternion.identity;

                nightSR              = nightGo.AddComponent<SpriteRenderer>();
                nightSR.sprite       = portSpritesNight[spriteIdx];
                nightSR.sortingOrder = sortOrder + 1;
                nightSR.color        = new Color(1f, 1f, 1f, 0f);
            }

            decorObjects.Add(go);
            ports.Add(new PortData
            {
                go           = go,
                dayRenderer  = daySR,
                nightRenderer = nightSR,
                tileX        = tile.x,
                tileY        = tile.y,
                baseAlpha    = baseA,
                worldPos     = new Vector3(wx, wy, spriteZ)
            });
        }

        Debug.Log($"MapDecorPlacer: Placed {ports.Count} port(s).");
    }

    bool IsAdjacentToWater(MapGenerator map, int x, int y)
    {
        return (!map.IsLand(x + 1, y) || !map.IsLand(x - 1, y) ||
                !map.IsLand(x, y + 1) || !map.IsLand(x, y - 1));
    }

    /// <summary>
    /// Checks that behind this shore tile there are enough city-biome tiles,
    /// ensuring the port isn't on a thin sliver of city coast.
    /// </summary>
    bool HasCityBacking(MapGenerator map, int x, int y)
    {
        int count = 0;
        int r = portCityBackingRadius;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (dx * dx + dy * dy > r * r) continue;
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || nx >= map.width || ny < 0 || ny >= map.height) continue;
            if (map.IsLand(nx, ny) && map.GetBiome(nx, ny) == 2) count++;
        }
        // Require at least 60% of the circle to be city
        int totalInCircle = 0;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
            if (dx * dx + dy * dy <= r * r) totalInCircle++;

        return count >= totalInCircle * 0.6f;
    }

    // =========================================================================
    // SHIP NAVIGATION GRID (downsampled)
    // =========================================================================

    /// <summary>
    /// Builds a coarse navigation grid for A* pathfinding.
    /// Each nav cell is shipPathGridStep × shipPathGridStep tiles.
    /// A cell is navigable only if ALL tiles in it are water AND at least
    /// shipLandClearance tiles from any land.
    /// </summary>
    void BuildNavGrid(MapGenerator map)
    {
        int step = Mathf.Max(1, shipPathGridStep);
        navW = Mathf.CeilToInt((float)map.width / step);
        navH = Mathf.CeilToInt((float)map.height / step);
        navGrid = new bool[navW, navH];

        // First build a land distance field (BFS from land outward)
        // We reuse a lightweight version — only need distances up to shipLandClearance + step
        int maxDist = shipLandClearance + step + 1;
        int[,] landDist = new int[map.width, map.height];
        Queue<Vector2Int> bfsQueue = new Queue<Vector2Int>();

        for (int x = 0; x < map.width; x++)
        for (int y = 0; y < map.height; y++)
        {
            if (map.IsLand(x, y))
            {
                landDist[x, y] = 0;
                bfsQueue.Enqueue(new Vector2Int(x, y));
            }
            else
            {
                landDist[x, y] = int.MaxValue;
            }
        }

        int[] dx4 = { 1, -1, 0, 0 };
        int[] dy4 = { 0, 0, 1, -1 };
        while (bfsQueue.Count > 0)
        {
            var pos = bfsQueue.Dequeue();
            int d = landDist[pos.x, pos.y];
            if (d >= maxDist) continue;
            for (int i = 0; i < 4; i++)
            {
                int nx = pos.x + dx4[i], ny = pos.y + dy4[i];
                if (nx < 0 || nx >= map.width || ny < 0 || ny >= map.height) continue;
                if (landDist[nx, ny] <= d + 1) continue;
                landDist[nx, ny] = d + 1;
                bfsQueue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        // Now build the nav grid: each cell is navigable if all sampled points
        // are water and far enough from land
        for (int gx = 0; gx < navW; gx++)
        for (int gy = 0; gy < navH; gy++)
        {
            int tileX = gx * step + step / 2;
            int tileY = gy * step + step / 2;
            tileX = Mathf.Clamp(tileX, 0, map.width - 1);
            tileY = Mathf.Clamp(tileY, 0, map.height - 1);

            if (map.IsLand(tileX, tileY))
            {
                navGrid[gx, gy] = false;
                continue;
            }

            navGrid[gx, gy] = landDist[tileX, tileY] >= shipLandClearance;
        }
    }

    // =========================================================================
    // SHIP SPAWNING & UPDATE
    // =========================================================================

    void UpdateShips(float lightingRatio)
    {
        if (cachedMap == null || ports.Count == 0) return;
        if (shipSpritesDay == null || shipSpritesDay.Count == 0) return;

        // Spawn timer
        shipSpawnTimer -= Time.deltaTime;
        if (shipSpawnTimer <= 0f)
        {
            shipSpawnTimer = shipSpawnInterval;
            if (activeShips.Count < maxActiveShips)
                TrySpawnShip();
        }

        // Update each ship
        for (int i = activeShips.Count - 1; i >= 0; i--)
        {
            ShipInstance ship = activeShips[i];

            switch (ship.state)
            {
                case ShipState.Arriving:
                    MoveAlongPath(ship);
                    if (ship.pathIndex >= ship.path.Count)
                    {
                        ship.state     = ShipState.Waiting;
                        ship.waitTimer = Random.Range(shipWaitTimeRange.x, shipWaitTimeRange.y);
                    }
                    break;

                case ShipState.Waiting:
                    ship.waitTimer -= Time.deltaTime;
                    if (ship.waitTimer <= 0f)
                    {
                        // Build departure path (reverse: port → ocean edge)
                        Vector3 exitPoint = GetRandomOceanEdgePoint();
                        List<Vector3> depPath = FindShipPath(ship.go.transform.position, exitPoint);
                        if (depPath != null && depPath.Count > 0)
                        {
                            ship.path      = depPath;
                            ship.pathIndex = 0;
                            ship.state     = ShipState.Departing;
                        }
                        else
                        {
                            ship.state = ShipState.Done;
                        }
                    }
                    break;

                case ShipState.Departing:
                    MoveAlongPath(ship);
                    if (ship.pathIndex >= ship.path.Count)
                        ship.state = ShipState.Done;
                    break;

                case ShipState.Done:
                    break;
            }

            // Cleanup finished ships
            if (ship.state == ShipState.Done)
            {
                decorObjects.Remove(ship.go);
                if (ship.go != null) Destroy(ship.go);
                activeShips.RemoveAt(i);
            }
        }
    }

    void MoveAlongPath(ShipInstance ship)
    {
        if (ship.path == null || ship.pathIndex >= ship.path.Count) return;

        Vector3 target = ship.path[ship.pathIndex];
        Vector3 pos    = ship.go.transform.position;
        float step     = ship.speed * Time.deltaTime;
        Vector3 dir    = target - pos;

        if (dir.sqrMagnitude <= step * step)
        {
            ship.go.transform.position = target;
            ship.pathIndex++;

            // Face next waypoint
            if (ship.pathIndex < ship.path.Count)
                RotateShipToward(ship, ship.path[ship.pathIndex] - target);
        }
        else
        {
            ship.go.transform.position = pos + dir.normalized * step;
            RotateShipToward(ship, dir);
        }
    }

    void RotateShipToward(ShipInstance ship, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        ship.go.transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }

    void TrySpawnShip()
    {
        if (ports.Count == 0) return;

        int portIdx   = Random.Range(0, ports.Count);
        PortData port = ports[portIdx];

        Vector3 origin    = GetRandomOceanEdgePoint();
        Vector3 portWorld = port.worldPos;

        // Offset the destination slightly into water near the port
        Vector3 portApproach = GetWaterApproachPoint(port.tileX, port.tileY);

        List<Vector3> path = FindShipPath(origin, portApproach);
        if (path == null || path.Count == 0) return;

        // Create ship GO
        int spriteIdx = Random.Range(0, shipSpritesDay.Count);
        Sprite daySprite = shipSpritesDay[spriteIdx];
        if (daySprite == null) return;

        float scale = Random.Range(shipScaleRange.x, shipScaleRange.y);
        float baseA = Random.Range(0.85f, 1f);
        int sortOrder = 8;

        GameObject go = new GameObject("Ship");
        go.transform.SetParent(transform);
        go.transform.position   = origin;
        go.transform.localScale = new Vector3(scale, scale, 1f);

        SpriteRenderer daySR = go.AddComponent<SpriteRenderer>();
        daySR.sprite       = daySprite;
        daySR.sortingOrder = sortOrder;
        daySR.color        = new Color(1f, 1f, 1f, baseA);

        SpriteRenderer nightSR = null;
        if (shipSpritesNight != null && spriteIdx < shipSpritesNight.Count &&
            shipSpritesNight[spriteIdx] != null)
        {
            GameObject nightGo = new GameObject("ShipNight");
            nightGo.transform.SetParent(go.transform, false);
            nightGo.transform.localPosition = Vector3.zero;
            nightGo.transform.localScale    = Vector3.one;
            nightGo.transform.localRotation = Quaternion.identity;

            nightSR              = nightGo.AddComponent<SpriteRenderer>();
            nightSR.sprite       = shipSpritesNight[spriteIdx];
            nightSR.sortingOrder = sortOrder + 1;
            nightSR.color        = new Color(1f, 1f, 1f, 0f);
        }

        // Apply current day/night ratio immediately
        float ratio = (dayNight != null) ? dayNight.LightingRatio : 0f;
        daySR.color = new Color(1f, 1f, 1f, baseA * (1f - ratio));
        if (nightSR != null)
            nightSR.color = new Color(1f, 1f, 1f, baseA * ratio);

        // Face first waypoint
        if (path.Count > 1)
        {
            Vector3 dir = path[1] - path[0];
            if (dir.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                go.transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }
        }

        decorObjects.Add(go);
        activeShips.Add(new ShipInstance
        {
            go            = go,
            dayRenderer   = daySR,
            nightRenderer = nightSR,
            baseAlpha     = baseA,
            scale         = scale,
            state         = ShipState.Arriving,
            path          = path,
            pathIndex     = 0,
            waitTimer     = 0f,
            portIndex     = portIdx,
            speed         = shipSpeed * Random.Range(0.8f, 1.2f)
        });
    }

    /// <summary>
    /// Returns a random world-space point on the ocean edge of the map.
    /// </summary>
    Vector3 GetRandomOceanEdgePoint()
    {
        float margin = 0.1f; // small offset inside the map boundary
        int side = Random.Range(0, 4);
        float wx, wy;

        switch (side)
        {
            case 0: // left
                wx = transform.position.x - cachedHalfW - margin;
                wy = transform.position.y + Random.Range(-cachedHalfH * 0.8f, cachedHalfH * 0.8f);
                break;
            case 1: // right
                wx = transform.position.x + cachedHalfW + margin;
                wy = transform.position.y + Random.Range(-cachedHalfH * 0.8f, cachedHalfH * 0.8f);
                break;
            case 2: // bottom
                wx = transform.position.x + Random.Range(-cachedHalfW * 0.8f, cachedHalfW * 0.8f);
                wy = transform.position.y - cachedHalfH - margin;
                break;
            default: // top
                wx = transform.position.x + Random.Range(-cachedHalfW * 0.8f, cachedHalfW * 0.8f);
                wy = transform.position.y + cachedHalfH + margin;
                break;
        }

        return new Vector3(wx, wy, spriteZ);
    }

    /// <summary>
    /// Returns a world position in the water just off the port's shore tile.
    /// Ships approach this point rather than the exact port tile.
    /// </summary>
    Vector3 GetWaterApproachPoint(int portTileX, int portTileY)
    {
        // Find the water-side neighbor of the port tile
        int[] dx4 = { 1, -1, 0, 0 };
        int[] dy4 = { 0, 0, 1, -1 };

        for (int i = 0; i < 4; i++)
        {
            int nx = portTileX + dx4[i], ny = portTileY + dy4[i];
            if (cachedMap != null && !cachedMap.IsLand(nx, ny))
            {
                // Go a few tiles further into water
                int waterX = portTileX + dx4[i] * 3;
                int waterY = portTileY + dy4[i] * 3;
                float wx = transform.position.x + (waterX / pixelsPerUnit) - cachedHalfW;
                float wy = transform.position.y + (waterY / pixelsPerUnit) - cachedHalfH;
                return new Vector3(wx, wy, spriteZ);
            }
        }

        // Fallback: just offset slightly from the port itself
        float fwx = transform.position.x + (portTileX / pixelsPerUnit) - cachedHalfW;
        float fwy = transform.position.y + (portTileY / pixelsPerUnit) - cachedHalfH;
        return new Vector3(fwx, fwy - 0.1f, spriteZ);
    }

    // =========================================================================
    // A* PATHFINDING ON NAV GRID
    // =========================================================================

    /// <summary>
    /// Finds a path in world-space from 'from' to 'to', navigating around the island.
    /// Uses A* on the downsampled navGrid, then converts back to world coordinates.
    /// </summary>
    List<Vector3> FindShipPath(Vector3 from, Vector3 to)
    {
        if (cachedMap == null || navGrid == null) return null;

        int step = Mathf.Max(1, shipPathGridStep);

        // Convert world → tile → nav grid coords
        Vector2Int fromTile = WorldToTile(from);
        Vector2Int toTile   = WorldToTile(to);

        int fromGx = Mathf.Clamp(fromTile.x / step, 0, navW - 1);
        int fromGy = Mathf.Clamp(fromTile.y / step, 0, navH - 1);
        int toGx   = Mathf.Clamp(toTile.x / step, 0, navW - 1);
        int toGy   = Mathf.Clamp(toTile.y / step, 0, navH - 1);

        // Snap start/end to nearest navigable cell if they're on land
        fromGx = FindNearestNavigable(fromGx, fromGy, out fromGy);
        int tempToGy = toGy;
        toGx   = FindNearestNavigable(toGx, tempToGy, out toGy);

        if (fromGx < 0 || toGx < 0) return null;

        // A* search
        List<Vector2Int> gridPath = AStarSearch(fromGx, fromGy, toGx, toGy);
        if (gridPath == null || gridPath.Count == 0) return null;

        // Convert grid path → world positions
        List<Vector3> worldPath = new List<Vector3>();
        // Start with exact origin
        worldPath.Add(from);

        for (int i = 0; i < gridPath.Count; i++)
        {
            int tileX = gridPath[i].x * step + step / 2;
            int tileY = gridPath[i].y * step + step / 2;
            tileX = Mathf.Clamp(tileX, 0, cachedMap.width - 1);
            tileY = Mathf.Clamp(tileY, 0, cachedMap.height - 1);

            float wx = transform.position.x + (tileX / pixelsPerUnit) - cachedHalfW;
            float wy = transform.position.y + (tileY / pixelsPerUnit) - cachedHalfH;
            worldPath.Add(new Vector3(wx, wy, spriteZ));
        }

        // End with exact destination
        worldPath.Add(to);

        // Simplify: remove collinear points to smooth the path
        worldPath = SimplifyPath(worldPath);

        return worldPath;
    }

    Vector2Int WorldToTile(Vector3 worldPos)
    {
        float localX = worldPos.x - transform.position.x + cachedHalfW;
        float localY = worldPos.y - transform.position.y + cachedHalfH;
        int tx = Mathf.RoundToInt(localX * pixelsPerUnit);
        int ty = Mathf.RoundToInt(localY * pixelsPerUnit);
        return new Vector2Int(
            Mathf.Clamp(tx, 0, cachedMap.width - 1),
            Mathf.Clamp(ty, 0, cachedMap.height - 1));
    }

    int FindNearestNavigable(int gx, int gy, out int outGy)
    {
        // Check the cell itself first
        if (gx >= 0 && gx < navW && gy >= 0 && gy < navH && navGrid[gx, gy])
        { outGy = gy; return gx; }

        // BFS outward to find nearest navigable cell
        int searchRadius = Mathf.Max(navW, navH) / 2;
        for (int r = 1; r <= searchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue; // only perimeter
                int nx = gx + dx, ny = gy + dy;
                if (nx < 0 || nx >= navW || ny < 0 || ny >= navH) continue;
                if (navGrid[nx, ny])
                { outGy = ny; return nx; }
            }
        }

        outGy = gy;
        return -1;
    }

    List<Vector2Int> AStarSearch(int sx, int sy, int ex, int ey)
    {
        // Directions: 8-connected for smoother paths
        int[] dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };
        float[] cost8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        int maxIterations = navW * navH; // safety cap
        float[,] gScore = new float[navW, navH];
        int[,] cameFromX = new int[navW, navH];
        int[,] cameFromY = new int[navW, navH];
        bool[,] closed   = new bool[navW, navH];

        for (int x = 0; x < navW; x++)
        for (int y = 0; y < navH; y++)
        {
            gScore[x, y] = float.MaxValue;
            cameFromX[x, y] = -1;
            cameFromY[x, y] = -1;
        }

        gScore[sx, sy] = 0f;

        // Min-heap approximation using a sorted list (good enough for the small nav grid)
        var open = new SortedList<float, Vector2Int>(new DuplicateKeyComparer());
        float h0 = Heuristic(sx, sy, ex, ey);
        open.Add(h0, new Vector2Int(sx, sy));

        int iterations = 0;
        while (open.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var currentKey = open.Keys[0];
            var current    = open.Values[0];
            open.RemoveAt(0);

            int cx = current.x, cy = current.y;
            if (cx == ex && cy == ey)
                return ReconstructPath(cameFromX, cameFromY, sx, sy, ex, ey);

            if (closed[cx, cy]) continue;
            closed[cx, cy] = true;

            for (int i = 0; i < 8; i++)
            {
                int nx = cx + dx8[i], ny = cy + dy8[i];
                if (nx < 0 || nx >= navW || ny < 0 || ny >= navH) continue;
                if (!navGrid[nx, ny] || closed[nx, ny]) continue;

                // For diagonals, ensure both adjacent axis-aligned cells are navigable
                if (i >= 4)
                {
                    if (!navGrid[cx + dx8[i], cy] || !navGrid[cx, cy + dy8[i]])
                        continue;
                }

                float tentG = gScore[cx, cy] + cost8[i];
                if (tentG >= gScore[nx, ny]) continue;

                gScore[nx, ny]    = tentG;
                cameFromX[nx, ny] = cx;
                cameFromY[nx, ny] = cy;
                float f = tentG + Heuristic(nx, ny, ex, ey);
                open.Add(f, new Vector2Int(nx, ny));
            }
        }

        return null; // no path found
    }

    float Heuristic(int ax, int ay, int bx, int by)
    {
        // Octile distance for 8-connected grid
        int dx = Mathf.Abs(ax - bx), dy = Mathf.Abs(ay - by);
        return Mathf.Max(dx, dy) + 0.414f * Mathf.Min(dx, dy);
    }

    List<Vector2Int> ReconstructPath(int[,] cameFromX, int[,] cameFromY,
                                     int sx, int sy, int ex, int ey)
    {
        var path = new List<Vector2Int>();
        int cx = ex, cy = ey;
        while (cx != sx || cy != sy)
        {
            path.Add(new Vector2Int(cx, cy));
            int px = cameFromX[cx, cy], py = cameFromY[cx, cy];
            if (px < 0)
            {
                // Should not happen, but safety
                path.Clear();
                return path;
            }
            cx = px; cy = py;
        }
        path.Add(new Vector2Int(sx, sy));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Removes intermediate waypoints that are roughly collinear to produce smoother paths.
    /// </summary>
    List<Vector3> SimplifyPath(List<Vector3> path)
    {
        if (path.Count <= 2) return path;

        var simplified = new List<Vector3> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector3 prev = simplified[simplified.Count - 1];
            Vector3 next = path[i + 1];
            Vector3 curr = path[i];

            // If the angle change is significant, keep this waypoint
            Vector3 d1 = (curr - prev).normalized;
            Vector3 d2 = (next - curr).normalized;
            if (Vector3.Dot(d1, d2) < 0.95f)
                simplified.Add(curr);
        }
        simplified.Add(path[path.Count - 1]);
        return simplified;
    }

    /// <summary>
    /// Comparer that allows duplicate keys in SortedList (for A* open set).
    /// </summary>
    private class DuplicateKeyComparer : IComparer<float>
    {
        public int Compare(float x, float y)
        {
            int result = x.CompareTo(y);
            return result == 0 ? 1 : result; // never return 0 so duplicates are allowed
        }
    }

    // =========================================================================
    // PORT PUBLIC GETTERS
    // =========================================================================

    public int PortCount => ports.Count;

    public Vector2Int GetPortTile(int index)
    {
        if (index < 0 || index >= ports.Count) return Vector2Int.zero;
        return new Vector2Int(ports[index].tileX, ports[index].tileY);
    }

    public int ActiveShipCount => activeShips.Count;

    // =========================================================================
    // UTILITY
    // =========================================================================

    void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    // -------------------------------------------------------------------------
    // CLEANUP
    // -------------------------------------------------------------------------

    public void Clear()
    {
        // Destroy all active ships
        foreach (var ship in activeShips)
            if (ship.go != null) Destroy(ship.go);
        activeShips.Clear();

        foreach (var go in decorObjects)
            if (go != null) Destroy(go);
        decorObjects.Clear();
        occupiedCenters.Clear();
        cityBuildings.Clear();
        ports.Clear();
        prevRatio = -1f;
        cachedMap = null;
        navGrid   = null;
        shipSpawnTimer = 0f;
    }
}