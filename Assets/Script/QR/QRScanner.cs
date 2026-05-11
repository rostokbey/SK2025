using UnityEngine;
using ZXing;
using UnityEngine.UI;

public class QRScanner : MonoBehaviour
{
    private WebCamTexture camTexture;
    private IBarcodeReader reader;

    [Header("UI")]
    public RawImage cameraView;
    public Button restartScanButton;
    public GameObject placeholderImage;

    [Header("Behavior")]
    public bool autoStart = false;
    public bool showRestartWhenIdle = true;

    private bool scanned = false;

    void Start()
    {
        reader = new BarcodeReader();

        if (autoStart)
            StartCamera();

        if (restartScanButton != null)
        {
            restartScanButton.onClick.RemoveListener(RestartScan);
            restartScanButton.onClick.AddListener(RestartScan);

            bool cameraIdle = camTexture == null || !camTexture.isPlaying;
            restartScanButton.gameObject.SetActive(showRestartWhenIdle && cameraIdle);
        }

        if (placeholderImage != null && (camTexture == null || !camTexture.isPlaying))
            placeholderImage.SetActive(true);
    }

    void Update()
    {
        if (scanned) return;
        if (camTexture == null) return;
        if (!camTexture.isPlaying) return;
        if (!camTexture.didUpdateThisFrame) return;

        // Kamera daha hazır değilse bekle
        if (camTexture.width < 100 || camTexture.height < 100) return;

        try
        {
            Color32[] pixels = camTexture.GetPixels32();

            if (pixels == null || pixels.Length == 0) return;

            var result = reader.Decode(
                pixels,
                camTexture.width,
                camTexture.height
            );

            if (result != null)
            {
                scanned = true;
                Debug.Log("✅ QR okundu: " + result.Text);

                QRDataManager.Instance?.ParseQRData(result.Text);

                StopCamera();

                if (restartScanButton != null)
                    restartScanButton.gameObject.SetActive(true);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("QR okuma hatası: " + e.Message);
        }
    }

    public void OnPanelOpened()
    {
        if (placeholderImage != null)
            placeholderImage.SetActive(true);

        if (restartScanButton != null && showRestartWhenIdle)
            restartScanButton.gameObject.SetActive(true);
    }

    public void StartScanning()
    {
        StartCamera();
    }

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

        WebCamDevice[] devices = WebCamTexture.devices;

        if (devices.Length == 0)
        {
            Debug.LogError("Kamera bulunamadı.");
            return;
        }

        camTexture = new WebCamTexture(devices[0].name);
        camTexture.Play();

        if (cameraView != null)
        {
            cameraView.texture = camTexture;

            if (cameraView.material != null)
                cameraView.material.mainTexture = camTexture;
        }

        scanned = false;

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

    void OnDestroy()
    {
        if (camTexture != null)
        {
            camTexture.Stop();
            camTexture = null;
        }
    }
}