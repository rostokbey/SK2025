using UnityEngine;
using ZXing;
using UnityEngine.UI;

public class QRScanner : MonoBehaviour
{
    private WebCamTexture camTexture;

    [Header("UI")]
    public RawImage cameraView;
    public Button restartScanButton;
    public GameObject placeholderImage;

    [Header("Behavior")]
    public bool autoStart = false;                 // ← QR panelinde biz başlatacağız
    public bool showRestartWhenIdle = true;        // ← Kamera kapalıyken buton görünsün

    private bool scanned = false;

    void Start()
    {
        if (autoStart) StartCamera();

        if (restartScanButton != null)
        {
            restartScanButton.onClick.AddListener(RestartScan);

            // Kamera şu an çalışmıyorsa butonu göster:
            bool cameraIdle = camTexture == null || !camTexture.isPlaying;
            restartScanButton.gameObject.SetActive(showRestartWhenIdle && cameraIdle);
        }

        // Kamera başlamadıysa placeholder açık kalsın
        if (placeholderImage != null && (camTexture == null || !camTexture.isPlaying))
            placeholderImage.SetActive(true);
    }

    void Update()
    {
        if (camTexture != null && camTexture.isPlaying && camTexture.didUpdateThisFrame && !scanned)
        {
            IBarcodeReader reader = new BarcodeReader();
            var result = reader.Decode(camTexture.GetPixels32(), camTexture.width, camTexture.height);

            if (result != null)
            {
                scanned = true;
                Debug.Log("✅ QR okundu: " + result.Text);

                QRDataManager.Instance?.ParseQRData(result.Text);
                StopCamera(); // tarayınca durdur

                // Tara butonu tekrar görünsün
                if (restartScanButton != null)
                    restartScanButton.gameObject.SetActive(true);
            }
        }
    }

    // Panel açılırken UI durumunu hazırlamak için (butona bağlayabilirsin)
    public void OnPanelOpened()
    {
        if (placeholderImage != null) placeholderImage.SetActive(true);
        if (restartScanButton != null && showRestartWhenIdle)
            restartScanButton.gameObject.SetActive(true);
    }

    public void StartScanning() => StartCamera();

    public void RestartScan()
    {
        Debug.Log("🔁 Tarama yeniden başlatılıyor...");
        StopCamera();
        StartCamera();
    }

    void StartCamera()
    {
        if (camTexture != null)
        {
            camTexture.Stop();
            camTexture = null;
        }

        camTexture = new WebCamTexture();

        if (cameraView != null)
        {
            cameraView.texture = camTexture;
            if (cameraView.material != null)            // ← NRE koruması
                cameraView.material.mainTexture = camTexture;
        }

        camTexture.Play();
        scanned = false;

        // Taramadayken butonu gizle
        if (restartScanButton != null)
            restartScanButton.gameObject.SetActive(false);

        if (placeholderImage != null)
            placeholderImage.SetActive(false);

        Debug.Log("📷 Kamera başlatıldı");
    }

    void StopCamera()
    {
        if (camTexture != null)
        {
            camTexture.Stop();
            if (cameraView != null)
            {
                cameraView.texture = null;
                if (cameraView.material != null)
                    cameraView.material.mainTexture = null;
            }
            Debug.Log("🛑 Kamera durduruldu");
        }

        if (placeholderImage != null)
            placeholderImage.SetActive(true);
    }
}
