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

    // -------------------------------------------------------------------------

    private struct BuildingData
    {
        public GameObject      go;
        public SpriteRenderer  dayRenderer;    // the original sprite renderer (day)
        public SpriteRenderer  nightRenderer;  // overlay child renderer (night)
        public int             tileX, tileY;
        public bool            isBroken;
        public int             spriteIndex;    // index into citiesDecor / citiesDecorNight
        public int             brokenIndex;    // index into brokenBuildingSprites / Night (-1 if not broken)
        public float           baseAlpha;      // original per-building alpha randomisation
    }

    private List<GameObject>   decorObjects    = new List<GameObject>();
    private List<Vector2>      occupiedCenters = new List<Vector2>();
    private List<BuildingData> cityBuildings   = new List<BuildingData>();

    private DayNightCycle dayNight;
    private float         prevRatio = -1f;

    // -------------------------------------------------------------------------
    // ENTRY POINT
    // -------------------------------------------------------------------------

    public void Repaint(MapGenerator map, BiomePaintSettings settings, Texture2D mapTexture)
    {
        Clear();
        if (settings == null) { Debug.LogError("MapDecorPlacer: settings is null!"); return; }

        dayNight = DayNightCycle.Instance;

        int scaledCellSize = Mathf.Max(cellSize, Mathf.RoundToInt(cellSize * (map.width / 256f)));
        int cellArea       = scaledCellSize * scaledCellSize;
        float halfW = map.width  * 0.5f / pixelsPerUnit;
        float halfH = map.height * 0.5f / pixelsPerUnit;

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

        // Apply initial crossfade state
        if (dayNight != null)
            ApplyCrossfade(dayNight.LightingRatio);

        Debug.Log($"MapDecorPlacer: decor={decorObjects.Count}, cityBuildings={cityBuildings.Count}");
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
        if (dayNight == null || cityBuildings.Count == 0) return;

        float ratio = dayNight.LightingRatio;

        // Skip update if ratio hasn't meaningfully changed
        if (Mathf.Abs(ratio - prevRatio) < 0.005f) return;
        prevRatio = ratio;

        ApplyCrossfade(ratio);
    }

    /// <summary>
    /// Sets the alpha of every building's day and night renderers based on the
    /// lighting ratio.  ratio=0 → day fully opaque, night fully transparent.
    /// ratio=1 → night fully opaque, day fully transparent.
    /// </summary>
    void ApplyCrossfade(float ratio)
    {
        for (int i = 0; i < cityBuildings.Count; i++)
        {
            BuildingData bd = cityBuildings[i];
            if (bd.dayRenderer == null) continue;

            float baseA = bd.baseAlpha;

            // Day renderer fades out as ratio goes up
            Color dc      = bd.dayRenderer.color;
            dc.a          = baseA * (1f - ratio);
            bd.dayRenderer.color = dc;

            // Night renderer fades in as ratio goes up
            if (bd.nightRenderer != null)
            {
                Color nc = bd.nightRenderer.color;
                nc.a     = baseA * ratio;
                bd.nightRenderer.color = nc;
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

                // Swap day renderer to broken day sprite
                bd.dayRenderer.sprite = brokenBuildingSprites[brokenIdx];

                // Swap night renderer to broken night sprite if available
                if (bd.nightRenderer != null
                    && brokenBuildingSpritesNight != null
                    && brokenIdx < brokenBuildingSpritesNight.Count
                    && brokenBuildingSpritesNight[brokenIdx] != null)
                {
                    bd.nightRenderer.sprite = brokenBuildingSpritesNight[brokenIdx];
                }
                else if (bd.nightRenderer != null)
                {
                    // No night broken variant — hide night overlay, show day only
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

        // --- Create the main GameObject with day renderer ---
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

        // --- Create night overlay child (same position, +1 sort order) ---
        SpriteRenderer nightSR = null;
        Sprite nightSprite = GetNightCitySprite(spriteIdx);
        if (nightSprite != null)
        {
            GameObject nightGo = new GameObject("NightOverlay");
            nightGo.transform.SetParent(go.transform, false);
            // Sits at exact same local position
            nightGo.transform.localPosition = Vector3.zero;
            nightGo.transform.localScale    = Vector3.one;
            nightGo.transform.localRotation = Quaternion.identity;

            nightSR              = nightGo.AddComponent<SpriteRenderer>();
            nightSR.sprite       = nightSprite;
            nightSR.sortingOrder = sortOrder + 1;
            nightSR.flipX        = false;
            nightSR.color        = new Color(1f, 1f, 1f, 0f); // starts transparent
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
        return citiesDecorNight[index]; // may be null — that's fine
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

    /// <summary>Places a simple single-renderer decor sprite (nature, non-crossfading).</summary>
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

    // -------------------------------------------------------------------------
    // CLEANUP
    // -------------------------------------------------------------------------

    public void Clear()
    {
        foreach (var go in decorObjects)
            if (go != null) Destroy(go);
        decorObjects.Clear();
        occupiedCenters.Clear();
        cityBuildings.Clear();
        prevRatio = -1f;
    }
}