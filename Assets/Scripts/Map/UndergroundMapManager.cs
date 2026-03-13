using System;
using System.Collections.Generic;
using UnityEngine;

public class UndergroundMapManager : MonoBehaviour
{
    public static UndergroundMapManager Instance { get; private set; }

    [Header("References")]
    public MapGenerator mapGenerator;
    public MapPainter   mapPainter;

    [Header("Underground Colors")]
    [Tooltip("Base color for undiscovered land underground.")]
    public Color undiscoveredDark  = new Color(0.18f, 0.12f, 0.07f);
    public Color undiscoveredLight = new Color(0.26f, 0.18f, 0.10f);

    [Tooltip("Color for discovered (researched) land underground.")]
    public Color discoveredDark  = new Color(0.45f, 0.34f, 0.20f);
    public Color discoveredLight = new Color(0.58f, 0.45f, 0.28f);

    [Tooltip("Color for discovered petroleum tiles.")]
    public Color petroleumColor = new Color(0.02f, 0.02f, 0.02f);

    [Tooltip("Water color in underground view.")]
    public Color undergroundWater = new Color(0.06f, 0.08f, 0.14f);

    [Header("Noise")]
    [Range(0.01f, 0.1f)] public float undergroundNoiseScale = 0.04f;

    public enum ViewMode { Surface, Underground }
    public ViewMode CurrentView => currentView;

    public static event Action<ViewMode> OnViewModeChanged;

    private ViewMode currentView = ViewMode.Surface;
    private bool[,]  discoveredMap;
    private Texture2D undergroundTexture;
    private Sprite    surfaceSprite;
    private Sprite    undergroundSprite;
    private bool      ready;
    private int       mapW, mapH;
    private float     noiseSeed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        // Subscribe after MapPainter finishes painting (which fires OnMapGenerated
        // indirectly through MapPainter.Paint → decorPlacer). We listen to the
        // petroleum generator event because it fires after CountryData which fires
        // after MapPainter.
        PetroleumBedGenerator.OnPetroleumGenerated += OnReady;
    }

    void OnDisable()
    {
        PetroleumBedGenerator.OnPetroleumGenerated -= OnReady;
    }

    void OnReady()
    {
        if (mapGenerator == null || mapPainter == null) return;
        mapW = mapGenerator.width;
        mapH = mapGenerator.height;
        discoveredMap = new bool[mapW, mapH];
        noiseSeed = UnityEngine.Random.Range(0f, 9999f);

        // Cache the surface sprite that MapPainter already created
        if (mapPainter.mapRenderer != null && mapPainter.mapRenderer.sprite != null)
            surfaceSprite = mapPainter.mapRenderer.sprite;

        BuildUndergroundTexture();
        ready = true;
        // Start in surface view
        currentView = ViewMode.Surface;
    }

    // === TEXTURE GENERATION ===

    void BuildUndergroundTexture()
    {
        if (undergroundTexture != null) Destroy(undergroundTexture);
        undergroundTexture = new Texture2D(mapW, mapH, TextureFormat.RGBA32, false);
        undergroundTexture.filterMode = FilterMode.Point;

        Color[] pixels = new Color[mapW * mapH];

        for (int x = 0; x < mapW; x++)
        {
            for (int y = 0; y < mapH; y++)
            {
                Color c;
                if (!mapGenerator.IsLand(x, y))
                {
                    c = undergroundWater;
                }
                else
                {
                    // Undiscovered land — dark brown with noise variation
                    c = GetUndergroundLandColor(x, y, false, false);
                }

                // Apply fog same as surface
                float fog = mapGenerator.GetFog(x, y);
                if (fog > 0f)
                {
                    Color fogC = new Color(0.12f, 0.10f, 0.08f); // dark fog for underground
                    c = Color.Lerp(c, fogC, fog);
                }

                pixels[x + y * mapW] = c;
            }
        }

        undergroundTexture.SetPixels(pixels);
        undergroundTexture.Apply();

        undergroundSprite = Sprite.Create(
            undergroundTexture,
            new Rect(0, 0, mapW, mapH),
            new Vector2(0.5f, 0.5f), 100f);
        undergroundSprite.name = "UndergroundMap";
    }

    Color GetUndergroundLandColor(int x, int y, bool discovered, bool hasPetroleum)
    {
        if (hasPetroleum && discovered)
            return petroleumColor;

        // Multi-octave noise for natural variation
        float n1 = Mathf.PerlinNoise(x * undergroundNoiseScale + noiseSeed,
                                      y * undergroundNoiseScale + noiseSeed);
        float n2 = Mathf.PerlinNoise(x * undergroundNoiseScale * 2.5f + noiseSeed + 500f,
                                      y * undergroundNoiseScale * 2.5f + noiseSeed + 500f) * 0.4f;
        float n = (n1 + n2) / 1.4f;

        if (discovered)
            return Color.Lerp(discoveredDark, discoveredLight, n);
        else
            return Color.Lerp(undiscoveredDark, undiscoveredLight, n);
    }

    // === DISCOVERY ===

    /// <summary>
    /// Marks tiles in a circle as discovered and refreshes the underground texture.
    /// Called by PetroleumSystem after a research scan completes.
    /// </summary>
    public void RevealCircle(Vector2Int center, int radius)
    {
        if (!ready) return;

        var bedGen = PetroleumBedGenerator.Instance;
        bool anyChanged = false;

        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (dx * dx + dy * dy > radius * radius) continue;
            int px = center.x + dx, py = center.y + dy;
            if (px < 0 || px >= mapW || py < 0 || py >= mapH) continue;
            if (!mapGenerator.IsLand(px, py)) continue;
            if (discoveredMap[px, py]) continue;

            discoveredMap[px, py] = true;
            anyChanged = true;

            bool hasPetroleum = bedGen != null && bedGen.IsGenerated && bedGen.HasPetroleum(px, py);
            Color c = GetUndergroundLandColor(px, py, true, hasPetroleum);

            float fog = mapGenerator.GetFog(px, py);
            if (fog > 0f)
            {
                Color fogC = new Color(0.12f, 0.10f, 0.08f);
                c = Color.Lerp(c, fogC, fog);
            }

            undergroundTexture.SetPixel(px, py, c);
        }

        if (anyChanged)
            undergroundTexture.Apply();
    }

    /// <summary>
    /// Check if a specific tile has been discovered.
    /// </summary>
    public bool IsDiscovered(int x, int y)
    {
        if (!ready || x < 0 || x >= mapW || y < 0 || y >= mapH) return false;
        return discoveredMap[x, y];
    }

    // === VIEW TOGGLE ===

    public void ToggleView()
    {
        if (!ready) return;
        SetView(currentView == ViewMode.Surface ? ViewMode.Underground : ViewMode.Surface);
    }

    public void SetView(ViewMode mode)
    {
        if (!ready) return;
        currentView = mode;

        bool isSurface = (mode == ViewMode.Surface);

        // Swap map texture
        if (mapPainter.mapRenderer != null)
            mapPainter.mapRenderer.sprite = isSurface ? surfaceSprite : undergroundSprite;

        // Toggle decor sprites (buildings, trees, etc.)
        var decorPlacer = mapPainter.GetComponent<MapDecorPlacer>();
        if (decorPlacer != null)
            decorPlacer.SetDecorVisible(isSurface);

        // Toggle pump sprites
        if (PetroleumSystem.Instance != null)
            PetroleumSystem.Instance.SetPumpsVisible(isSurface);

        OnViewModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Re-cache the surface sprite if MapPainter repaints (e.g. after petroleum
    /// research overlay on surface). Call this if surface texture changes externally.
    /// </summary>
    public void RefreshSurfaceSprite()
    {
        if (mapPainter != null && mapPainter.mapRenderer != null && mapPainter.mapRenderer.sprite != null)
        {
            // Only update if we're not currently showing underground
            if (currentView == ViewMode.Surface)
                surfaceSprite = mapPainter.mapRenderer.sprite;
        }
    }

    // === DEBUG ===

    /// <summary>Debug: reveal the entire map underground.</summary>
    public void DebugRevealAll()
    {
        if (!ready) return;
        var bedGen = PetroleumBedGenerator.Instance;

        for (int x = 0; x < mapW; x++)
        for (int y = 0; y < mapH; y++)
        {
            if (!mapGenerator.IsLand(x, y)) continue;
            discoveredMap[x, y] = true;

            bool hasPetroleum = bedGen != null && bedGen.IsGenerated && bedGen.HasPetroleum(x, y);
            Color c = GetUndergroundLandColor(x, y, true, hasPetroleum);

            float fog = mapGenerator.GetFog(x, y);
            if (fog > 0f)
            {
                Color fogC = new Color(0.12f, 0.10f, 0.08f);
                c = Color.Lerp(c, fogC, fog);
            }

            undergroundTexture.SetPixel(x, y, c);
        }

        undergroundTexture.Apply();
    }

    public bool IsReady => ready;
}