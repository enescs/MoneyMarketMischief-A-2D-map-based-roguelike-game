using UnityEngine;

// TEMP: Skill node butonundan ScientistSmuggle UI'ı açmak için test scripti.
// Geri almak için bu dosyayı sil.
public class TEMP_ScientistSmuggleOpener : MonoBehaviour
{
    private ScientistSmuggleUI smuggleUI;

    private void Start()
    {
        smuggleUI = FindFirstObjectByType<ScientistSmuggleUI>(FindObjectsInactive.Include);
    }

    // Butona bağla - Inspector'dan OnClick'e ekle
    public void OpenSmuggleUI()
    {
        if (smuggleUI == null)
            smuggleUI = FindFirstObjectByType<ScientistSmuggleUI>(FindObjectsInactive.Include);

        if (smuggleUI != null)
        {
            smuggleUI.OpenAndTriggerOffer();
        }
        else
        {
            Debug.LogError("ScientistSmuggleUI sahnede bulunamadı!");
        }
    }
}