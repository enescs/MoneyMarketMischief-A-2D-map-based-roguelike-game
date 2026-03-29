using UnityEngine;

/// <summary>
/// Haritanın üstüne yarı saydam dalga efekti koyar.
/// Sadece su alanında görünür, kara üzerinde şeffaf.
/// </summary>
public class OceanWaveOverlay : MonoBehaviour
{
    [Header("References")]
    public MapPainter mapPainter;
    public MapGenerator mapGenerator;

    [Header("Shader")]
    public Shader oceanShader;

    [Header("Wave Settings")]
    [Range(0f, 1f)] public float intensity = 0.25f;
    public Color waveColorLight = new Color(0.3f, 0.5f, 0.7f, 0.2f);
    public Color waveColorDark = new Color(0.05f, 0.12f, 0.25f, 0.15f);
    public Color foamColor = new Color(0.8f, 0.9f, 1f, 0.3f);

    [Range(3f, 30f)] public float waveScale1 = 8f;
    [Range(5f, 40f)] public float waveScale2 = 15f;
    public Vector2 waveSpeed1 = new Vector2(0.06f, 0.04f);
    public Vector2 waveSpeed2 = new Vector2(-0.04f, 0.06f);

    [Range(10f, 50f)] public float foamScale = 25f;
    public Vector2 foamSpeed = new Vector2(0.01f, -0.015f);
    [Range(0.5f, 0.95f)] public float foamThreshold = 0.72f;

    private GameObject overlayGO;
    private SpriteRenderer overlaySR;
    private Material overlayMat;
    private bool initialized;

    void Start()
    {
        StartCoroutine(WaitAndSetup());
    }

    System.Collections.IEnumerator WaitAndSetup()
    {
        while (mapPainter == null || mapPainter.GetMapTexture() == null)
            yield return null;
        Setup();
    }

    void Setup()
    {
        if (oceanShader == null)
            oceanShader = Shader.Find("Custom/OceanWave");
        if (oceanShader == null)
        {
            Debug.LogWarning("OceanWaveOverlay: Shader bulunamadı!");
            return;
        }

        int w = mapGenerator.width, h = mapGenerator.height;

        //mask texture — R kanalı: kara=1, su=0
        Texture2D mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
        mask.filterMode = FilterMode.Bilinear;
        Color[] maskPx = new Color[w * h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float land = mapGenerator.IsLand(x, y) ? 1f : 0f;
                maskPx[x + y * w] = new Color(land, land, land, 1f);
            }
        mask.SetPixels(maskPx);
        mask.Apply();

        //beyaz sprite — shader tüm işi yapacak
        Texture2D spriteTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        spriteTex.filterMode = FilterMode.Point;
        Color[] spritePx = new Color[w * h];
        for (int i = 0; i < spritePx.Length; i++)
            spritePx[i] = Color.white;
        spriteTex.SetPixels(spritePx);
        spriteTex.Apply();

        //materyal
        overlayMat = new Material(oceanShader);
        overlayMat.SetTexture("_MaskTex", mask);
        UpdateMaterialProperties();

        //overlay GO
        if (overlayGO != null) Destroy(overlayGO);
        overlayGO = new GameObject("OceanWaveOverlay");
        overlayGO.transform.SetParent(transform);
        overlayGO.transform.localPosition = new Vector3(0f, 0f, -0.5f);

        overlaySR = overlayGO.AddComponent<SpriteRenderer>();
        overlaySR.sprite = Sprite.Create(spriteTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        overlaySR.material = overlayMat;
        overlaySR.sortingOrder = 1;

        initialized = true;
        Debug.Log("OceanWaveOverlay: Aktif.");
    }

    void Update()
    {
        if (!initialized || overlayMat == null) return;
        UpdateMaterialProperties();
    }

    void UpdateMaterialProperties()
    {
        overlayMat.SetColor("_WaveColor1", waveColorLight);
        overlayMat.SetColor("_WaveColor2", waveColorDark);
        overlayMat.SetColor("_FoamColor", foamColor);
        overlayMat.SetFloat("_WaveScale1", waveScale1);
        overlayMat.SetFloat("_WaveScale2", waveScale2);
        overlayMat.SetVector("_WaveSpeed1", waveSpeed1);
        overlayMat.SetVector("_WaveSpeed2", waveSpeed2);
        overlayMat.SetFloat("_FoamScale", foamScale);
        overlayMat.SetVector("_FoamSpeed", foamSpeed);
        overlayMat.SetFloat("_FoamThreshold", foamThreshold);
        overlayMat.SetFloat("_Intensity", intensity);
    }
}
