/* using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

[System.Serializable]
public class BundleModelEntry
{
    public string documentName;
}

public class FetchBundle : MonoBehaviour
{
    FirebaseFirestore db;

    [Header("Module Settings")]
    [Tooltip("Must match Firebase collection/document name exactly e.g. FrogLifeCycle, Aquatic")]
    [SerializeField] private string moduleName;

    [Header("Models in this Bundle")]
    [SerializeField] private List<BundleModelEntry> models;

    [Header("Download Button")]
    [SerializeField] private Button downloadButton;
    [SerializeField] private TextMeshProUGUI buttonText;
    [SerializeField] private Image checkmarkIcon;
    [SerializeField] private TextMeshProUGUI progressText;

    [Header("Quality Panel")]
    [SerializeField] private GameObject qualityPanel;
    [SerializeField] private Button lowButton;
    [SerializeField] private Button highButton;

    public enum ModelQuality { Low, High }
    private ModelQuality selectedQuality;

    private string QualitySubcollection => selectedQuality == ModelQuality.Low ? "asset_low" : "asset_high";

    private bool isDownloading = false;

    private string LocalStorageFolder => Path.Combine(Application.persistentDataPath, "CachedBundles");
    private string URLRegistryPath => Path.Combine(Application.persistentDataPath, "url_registry.json");

    private Dictionary<string, string> urlRegistry = new Dictionary<string, string>();

    public enum BundleState { NotDownloaded, Downloading, Downloaded }
    private BundleState currentState = BundleState.NotDownloaded;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        Debug.Log($"[FetchBundle: {moduleName}] Initializing...");

        if (!Directory.Exists(LocalStorageFolder))
        {
            Directory.CreateDirectory(LocalStorageFolder);
            Debug.Log($"[FetchBundle: {moduleName}] Created local storage folder: {LocalStorageFolder}");
        }
        else
        {
            Debug.Log($"[FetchBundle: {moduleName}] Local storage folder found: {LocalStorageFolder}");
        }

        LoadURLRegistry();

        bool allCached = CheckAllCached();
        Debug.Log($"[FetchBundle: {moduleName}] All models cached: {allCached}");
        SetState(allCached ? BundleState.Downloaded : BundleState.NotDownloaded);

        downloadButton.onClick.AddListener(OnDownloadClicked);

        if (progressText != null)
            progressText.gameObject.SetActive(false);

        Debug.Log($"[FetchBundle: {moduleName}] Ready — {models.Count} models registered");
    }

    // ─── Download button clicked ──────────────────────────────────────────────

    void OnDownloadClicked()
    {
        if (currentState == BundleState.Downloaded)
        {
            Debug.Log($"[FetchBundle: {moduleName}] Already downloaded — ignoring click");
            return;
        }
        if (isDownloading)
        {
            Debug.Log($"[FetchBundle: {moduleName}] Already downloading — ignoring click");
            return;
        }

        Debug.Log($"[FetchBundle: {moduleName}] Download button clicked — opening quality panel");
        qualityPanel.SetActive(true);
        UpdateQualityUI();
    }

    // ─── Quality panel ────────────────────────────────────────────────────────

    public void SelectLowQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.Low;
        Debug.Log($"[FetchBundle: {moduleName}] Quality selected: LOW (asset_low)");
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(DownloadBundle());
    }

    public void SelectHighQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.High;
        Debug.Log($"[FetchBundle: {moduleName}] Quality selected: HIGH (asset_high)");
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(DownloadBundle());
    }

    void UpdateQualityUI()
    {
        lowButton.image.color = selectedQuality == ModelQuality.Low ? Color.green : Color.white;
        highButton.image.color = selectedQuality == ModelQuality.High ? Color.green : Color.white;
    }

    // ─── Button states ────────────────────────────────────────────────────────

    void SetState(BundleState state)
    {
        currentState = state;
        Debug.Log($"[FetchBundle: {moduleName}] State changed → {state}");

        switch (state)
        {
            case BundleState.NotDownloaded:
                downloadButton.interactable = true;
                downloadButton.image.color = Color.white;
                if (buttonText != null) buttonText.text = "Download";
                if (checkmarkIcon != null) checkmarkIcon.gameObject.SetActive(false);
                break;

            case BundleState.Downloading:
                downloadButton.interactable = false;
                downloadButton.image.color = Color.yellow;
                if (buttonText != null) buttonText.text = "Downloading...";
                if (checkmarkIcon != null) checkmarkIcon.gameObject.SetActive(false);
                if (progressText != null) progressText.gameObject.SetActive(true);
                break;

            case BundleState.Downloaded:
                downloadButton.interactable = false;
                downloadButton.image.color = Color.green;
                if (buttonText != null) buttonText.text = "Downloaded";
                if (checkmarkIcon != null) checkmarkIcon.gameObject.SetActive(true);
                if (progressText != null) progressText.gameObject.SetActive(false);
                break;
        }
    }

    // ─── Check all cached ─────────────────────────────────────────────────────

    bool CheckAllCached()
    {
        Debug.Log($"[FetchBundle: {moduleName}] Checking local cache...");

        foreach (var model in models)
        {
            bool lowExists = File.Exists(GetLocalFilePath(model.documentName, "asset_low"));
            bool highExists = File.Exists(GetLocalFilePath(model.documentName, "asset_high"));

            Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} — " +
                      $"asset_low: {(lowExists ? "EXISTS" : "MISSING")} | " +
                      $"asset_high: {(highExists ? "EXISTS" : "MISSING")}");

            if (!lowExists && !highExists)
            {
                Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} not cached at all");
                return false;
            }
        }

        Debug.Log($"[FetchBundle: {moduleName}] All models found in cache");
        return true;
    }

    // ─── Main download loop ───────────────────────────────────────────────────

    IEnumerator DownloadBundle()
    {
        isDownloading = true;
        SetState(BundleState.Downloading);

        int total = models.Count;
        int completed = 0;

        Debug.Log($"[FetchBundle: {moduleName}] ── Starting download ──");
        Debug.Log($"[FetchBundle: {moduleName}] Quality: {QualitySubcollection} | Models: {total}");

        foreach (var model in models)
        {
            Debug.Log($"[FetchBundle: {moduleName}] ── Processing {model.documentName} ({completed + 1}/{total}) ──");

            if (progressText != null)
                progressText.text = $"Checking {model.documentName} ({completed + 1}/{total})...";

            // 1. Fetch URL from Firestore
            Debug.Log($"[FetchBundle: {moduleName}] Fetching URL from Firestore for {model.documentName}...");

            string resolvedURL = null;
            bool urlReady = false;

            yield return StartCoroutine(FetchURL(model.documentName, url =>
            {
                resolvedURL = url;
                urlReady = true;
            }));

            yield return new WaitUntil(() => urlReady);

            if (resolvedURL == null)
            {
                Debug.LogError($"[FetchBundle: {moduleName}] URL fetch failed for {model.documentName} — skipping");
                completed++;
                continue;
            }

            Debug.Log($"[FetchBundle: {moduleName}] URL resolved: {resolvedURL}");

            // 2. Check disk cache
            string localPath = GetLocalFilePath(model.documentName, QualitySubcollection);
            bool onDisk = File.Exists(localPath);
            bool urlSame = IsURLUnchanged(model.documentName, resolvedURL);

            Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} — " +
                      $"On disk: {onDisk} | URL unchanged: {urlSame}");

            if (onDisk && urlSame)
            {
                Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} already up to date — skipping download");
                completed++;
                if (progressText != null)
                    progressText.text = $"Already cached {completed}/{total}";
                continue;
            }

            // 3. Delete stale bundle if URL changed
            if (onDisk && !urlSame)
            {
                File.Delete(localPath);
                Debug.Log($"[FetchBundle: {moduleName}] URL changed — deleted stale bundle for {model.documentName}");
            }

            // 4. Download and save
            Debug.Log($"[FetchBundle: {moduleName}] Downloading {model.documentName} from: {resolvedURL}");

            bool downloadDone = false;
            bool downloadOk = false;

            yield return StartCoroutine(DownloadAndSave(
                resolvedURL, model.documentName,
                completed + 1, total,
                result => { downloadOk = result; downloadDone = true; }));

            yield return new WaitUntil(() => downloadDone);

            if (downloadOk)
                Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} saved successfully");
            else
                Debug.LogError($"[FetchBundle: {moduleName}] {model.documentName} download failed");

            completed++;
        }

        isDownloading = false;
        SetState(BundleState.Downloaded);

        Debug.Log($"[FetchBundle: {moduleName}] ── Download complete ──");
        Debug.Log($"[FetchBundle: {moduleName}] All {total} models processed for [{QualitySubcollection}]");
    }

    // ─── Firestore URL fetch ──────────────────────────────────────────────────

    IEnumerator FetchURL(string docName, System.Action<string> onResult)
    {
        string resolvedURL = null;
        bool done = false;
        bool error = false;

        string fullPath = $"kidsxr/modules/{moduleName}/{moduleName}/{QualitySubcollection}/{docName}";
        Debug.Log($"[FetchBundle: {moduleName}] Firestore path: {fullPath}");

        db.Collection("kidsxr")
          .Document("modules")
          .Collection(moduleName)
          .Document(moduleName)
          .Collection(QualitySubcollection)
          .Document(docName)
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              if (task.IsFaulted || task.IsCanceled)
              {
                  Debug.LogError($"[FetchBundle: {moduleName}] Firestore task faulted for {docName}: {task.Exception?.Message}");
                  error = true;
              }
              else if (!task.Result.Exists)
              {
                  Debug.LogError($"[FetchBundle: {moduleName}] Document does not exist at path: {fullPath}");
                  error = true;
              }
              else
              {
                  UrlData data = task.Result.ConvertTo<UrlData>();
                  resolvedURL = data.URL;
                  Debug.Log($"[FetchBundle: {moduleName}] Firestore returned URL for {docName}: {resolvedURL}");
              }
              done = true;
          });

        yield return new WaitUntil(() => done);
        onResult?.Invoke(error ? null : resolvedURL);
    }

    // ─── Download and save to disk ────────────────────────────────────────────

    IEnumerator DownloadAndSave(string url, string docName,
        int index, int total, System.Action<bool> onDone)
    {
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SendWebRequest();

        float fakeProgress = 0f;

        while (!request.isDone)
        {
            fakeProgress += Time.deltaTime * 0.3f;
            int pct = Mathf.Clamp(Mathf.RoundToInt(fakeProgress * 100), 0, 90);
            if (progressText != null)
                progressText.text = $"Downloading {index}/{total}... {pct}%";
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[FetchBundle: {moduleName}] Download failed for {docName}: {request.error} | URL: {url}");
            if (progressText != null)
                progressText.text = $"Failed: {docName}";
            onDone?.Invoke(false);
            yield break;
        }

        if (progressText != null)
            progressText.text = $"Downloading {index}/{total}... 100%";

        string savePath = GetLocalFilePath(docName, QualitySubcollection);
        File.WriteAllBytes(savePath, request.downloadHandler.data);
        RegisterURL(docName, url);

        Debug.Log($"[FetchBundle: {moduleName}] Saved {docName} to disk — " +
                  $"Size: {request.downloadHandler.data.Length / 1024} KB | Path: {savePath}");

        onDone?.Invoke(true);
    }

    // ─── URL registry ─────────────────────────────────────────────────────────

    [System.Serializable]
    private class URLRegistryData
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();
    }

    void LoadURLRegistry()
    {
        urlRegistry.Clear();
        if (!File.Exists(URLRegistryPath))
        {
            Debug.Log($"[FetchBundle: {moduleName}] No URL registry found — starting fresh");
            return;
        }

        try
        {
            string json = File.ReadAllText(URLRegistryPath);
            URLRegistryData data = JsonUtility.FromJson<URLRegistryData>(json);
            for (int i = 0; i < data.keys.Count; i++)
                urlRegistry[data.keys[i]] = data.values[i];
            Debug.Log($"[FetchBundle: {moduleName}] URL registry loaded — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FetchBundle: {moduleName}] Failed to load URL registry: " + e.Message);
        }
    }

    void SaveURLRegistry()
    {
        try
        {
            URLRegistryData data = new URLRegistryData();
            foreach (var kvp in urlRegistry)
            {
                data.keys.Add(kvp.Key);
                data.values.Add(kvp.Value);
            }
            File.WriteAllText(URLRegistryPath, JsonUtility.ToJson(data, true));
            Debug.Log($"[FetchBundle: {moduleName}] URL registry saved — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FetchBundle: {moduleName}] Failed to save URL registry: " + e.Message);
        }
    }

    string RegistryKey(string docName)
        => $"{moduleName}_{docName}_{QualitySubcollection}";

    bool IsURLUnchanged(string docName, string url)
        => urlRegistry.TryGetValue(RegistryKey(docName), out string saved) && saved == url;

    void RegisterURL(string docName, string url)
    {
        urlRegistry[RegistryKey(docName)] = url;
        SaveURLRegistry();
    }

    string GetLocalFilePath(string docName, string quality)
        => Path.Combine(LocalStorageFolder, $"{moduleName}_{docName}_{quality}.bundle");
}*/

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

[System.Serializable]
public class BundleModelEntry
{
    public string documentName;
}

public class FetchBundle : MonoBehaviour
{
    FirebaseFirestore db;

    [Header("Module Settings")]
    [Tooltip("Must match Firebase collection/document name exactly e.g. FrogLifeCycle, Aquatic")]
    [SerializeField] private string moduleName;

    [Header("Models in this Bundle")]
    [SerializeField] private List<BundleModelEntry> models;

    [Header("Download Button")]
    [SerializeField] private Button downloadButton;
    [SerializeField] private TextMeshProUGUI buttonText;
    [SerializeField] private Image checkmarkIcon;
    [SerializeField] private TextMeshProUGUI progressText;

    [Header("Quality Panel")]
    [SerializeField] private GameObject qualityPanel;
    [SerializeField] private Button lowButton;
    [SerializeField] private Button highButton;

    [Header("Editor Debug")]
    [Tooltip("Tick to clear this module's cache on Play — untick to keep cache and test loading")]
    [SerializeField] private bool clearCacheOnPlay = false;

    public enum ModelQuality { Low, High }
    private ModelQuality selectedQuality;

    private string QualitySubcollection => selectedQuality == ModelQuality.Low ? "asset_low" : "asset_high";

    private bool isDownloading = false;

    private string LocalStorageFolder => Path.Combine(Application.persistentDataPath, "CachedBundles");
    private string URLRegistryPath => Path.Combine(Application.persistentDataPath, "url_registry.json");

    private Dictionary<string, string> urlRegistry = new Dictionary<string, string>();

    public enum BundleState { NotDownloaded, Downloading, Downloaded }
    private BundleState currentState = BundleState.NotDownloaded;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
#if UNITY_EDITOR
        if (clearCacheOnPlay)
        {
            // Clear only this module's files — doesn't wipe other modules
            foreach (var model in models)
            {
                string lowPath = GetLocalFilePath(model.documentName, "asset_low");
                string highPath = GetLocalFilePath(model.documentName, "asset_high");
                if (File.Exists(lowPath))
                {
                    File.Delete(lowPath);
                    Debug.Log($"[FetchBundle: {moduleName}] Cleared cache: {lowPath}");
                }
                if (File.Exists(highPath))
                {
                    File.Delete(highPath);
                    Debug.Log($"[FetchBundle: {moduleName}] Cleared cache: {highPath}");
                }
            }
            Debug.Log($"[FetchBundle: {moduleName}] Editor mode: cache cleared for this module only");
        }
        else
        {
            Debug.Log($"[FetchBundle: {moduleName}] Editor mode: keeping existing cache (clearCacheOnPlay is OFF)");
        }
#endif

        db = FirebaseFirestore.DefaultInstance;
        Debug.Log($"[FetchBundle: {moduleName}] Initializing...");

        if (!Directory.Exists(LocalStorageFolder))
        {
            Directory.CreateDirectory(LocalStorageFolder);
            Debug.Log($"[FetchBundle: {moduleName}] Created local storage folder: {LocalStorageFolder}");
        }
        else
        {
            Debug.Log($"[FetchBundle: {moduleName}] Local storage folder found: {LocalStorageFolder}");
        }

        LoadURLRegistry();

        bool allCached = CheckAllCached();
        Debug.Log($"[FetchBundle: {moduleName}] All models cached: {allCached}");
        SetState(allCached ? BundleState.Downloaded : BundleState.NotDownloaded);

        downloadButton.onClick.AddListener(OnDownloadClicked);

        if (progressText != null)
            progressText.gameObject.SetActive(false);

        Debug.Log($"[FetchBundle: {moduleName}] Ready — {models.Count} models registered");
    }

    // ─── Download button clicked ──────────────────────────────────────────────

    void OnDownloadClicked()
    {
        if (currentState == BundleState.Downloaded)
        {
            Debug.Log($"[FetchBundle: {moduleName}] Already downloaded — ignoring click");
            return;
        }
        if (isDownloading)
        {
            Debug.Log($"[FetchBundle: {moduleName}] Already downloading — ignoring click");
            return;
        }

        Debug.Log($"[FetchBundle: {moduleName}] Download button clicked — opening quality panel");
        qualityPanel.SetActive(true);
        UpdateQualityUI();
    }

    // ─── Quality panel ────────────────────────────────────────────────────────

    public void SelectLowQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.Low;
        Debug.Log($"[FetchBundle: {moduleName}] Quality selected: LOW (asset_low)");
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(DownloadBundle());
    }

    public void SelectHighQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.High;
        Debug.Log($"[FetchBundle: {moduleName}] Quality selected: HIGH (asset_high)");
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(DownloadBundle());
    }

    void UpdateQualityUI()
    {
        lowButton.image.color = selectedQuality == ModelQuality.Low ? Color.green : Color.white;
        highButton.image.color = selectedQuality == ModelQuality.High ? Color.green : Color.white;
    }

    // ─── Button states ────────────────────────────────────────────────────────

    void SetState(BundleState state)
    {
        currentState = state;
        Debug.Log($"[FetchBundle: {moduleName}] State changed → {state}");

        switch (state)
        {
            case BundleState.NotDownloaded:
                downloadButton.interactable = true;
                downloadButton.image.color = Color.white;
                if (buttonText != null) buttonText.text = "Download";
                if (checkmarkIcon != null) checkmarkIcon.gameObject.SetActive(false);
                break;

            case BundleState.Downloading:
                downloadButton.interactable = false;
                downloadButton.image.color = Color.yellow;
                if (buttonText != null) buttonText.text = "Downloading...";
                if (checkmarkIcon != null) checkmarkIcon.gameObject.SetActive(false);
                if (progressText != null) progressText.gameObject.SetActive(true);
                break;

            case BundleState.Downloaded:
                downloadButton.interactable = false;
                downloadButton.image.color = Color.green;
                if (buttonText != null) buttonText.text = "Downloaded";
                if (checkmarkIcon != null) checkmarkIcon.gameObject.SetActive(true);
                if (progressText != null) progressText.gameObject.SetActive(false);
                break;
        }
    }

    // ─── Check all cached ─────────────────────────────────────────────────────

    bool CheckAllCached()
    {
        Debug.Log($"[FetchBundle: {moduleName}] Checking local cache...");

        foreach (var model in models)
        {
            bool lowExists = File.Exists(GetLocalFilePath(model.documentName, "asset_low"));
            bool highExists = File.Exists(GetLocalFilePath(model.documentName, "asset_high"));

            Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} — " +
                      $"asset_low: {(lowExists ? "EXISTS" : "MISSING")} | " +
                      $"asset_high: {(highExists ? "EXISTS" : "MISSING")}");

            if (!lowExists && !highExists)
            {
                Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} not cached at all — bundle incomplete");
                return false;
            }
        }

        Debug.Log($"[FetchBundle: {moduleName}] All models found in cache");
        return true;
    }

    // ─── Main download loop ───────────────────────────────────────────────────

    IEnumerator DownloadBundle()
    {
        isDownloading = true;
        SetState(BundleState.Downloading);

        int total = models.Count;
        int completed = 0;

        Debug.Log($"[FetchBundle: {moduleName}] ── Starting download ──");
        Debug.Log($"[FetchBundle: {moduleName}] Quality: {QualitySubcollection} | Models to process: {total}");

        foreach (var model in models)
        {
            Debug.Log($"[FetchBundle: {moduleName}] ── Processing {model.documentName} ({completed + 1}/{total}) ──");

            if (progressText != null)
                progressText.text = $"Checking {model.documentName} ({completed + 1}/{total})...";

            // 1. Fetch URL from Firestore
            Debug.Log($"[FetchBundle: {moduleName}] Fetching URL from Firestore for {model.documentName}...");

            string resolvedURL = null;
            bool urlReady = false;

            yield return StartCoroutine(FetchURL(model.documentName, url =>
            {
                resolvedURL = url;
                urlReady = true;
            }));

            yield return new WaitUntil(() => urlReady);

            if (resolvedURL == null)
            {
                Debug.LogError($"[FetchBundle: {moduleName}] URL fetch failed for {model.documentName} — skipping");
                completed++;
                continue;
            }

            Debug.Log($"[FetchBundle: {moduleName}] URL resolved for {model.documentName}: {resolvedURL}");

            // 2. Check disk cache
            string localPath = GetLocalFilePath(model.documentName, QualitySubcollection);
            bool onDisk = File.Exists(localPath);
            bool urlSame = IsURLUnchanged(model.documentName, resolvedURL);

            Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} — " +
                      $"On disk: {onDisk} | URL unchanged: {urlSame}");

            if (onDisk && urlSame)
            {
                Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} already up to date — skipping download");
                completed++;
                if (progressText != null)
                    progressText.text = $"Already cached {completed}/{total}";
                continue;
            }

            // 3. Delete stale bundle if URL changed
            if (onDisk && !urlSame)
            {
                File.Delete(localPath);
                Debug.Log($"[FetchBundle: {moduleName}] URL changed — deleted stale bundle for {model.documentName}");
            }

            // 4. Download and save to disk — no instantiation
            Debug.Log($"[FetchBundle: {moduleName}] Downloading {model.documentName} from: {resolvedURL}");

            bool downloadDone = false;
            bool downloadOk = false;

            yield return StartCoroutine(DownloadAndSave(
                resolvedURL, model.documentName,
                completed + 1, total,
                result => { downloadOk = result; downloadDone = true; }));

            yield return new WaitUntil(() => downloadDone);

            if (downloadOk)
                Debug.Log($"[FetchBundle: {moduleName}] {model.documentName} saved to disk successfully");
            else
                Debug.LogError($"[FetchBundle: {moduleName}] {model.documentName} download FAILED");

            completed++;
        }

        isDownloading = false;
        SetState(BundleState.Downloaded);

        Debug.Log($"[FetchBundle: {moduleName}] ── Download complete ──");
        Debug.Log($"[FetchBundle: {moduleName}] All {total} models processed for [{QualitySubcollection}]");
    }

    // ─── Firestore URL fetch ──────────────────────────────────────────────────

    // Path: kidsxr → modules → [moduleName] → [moduleName] → asset_low/asset_high → [docName]
    IEnumerator FetchURL(string docName, System.Action<string> onResult)
    {
        string resolvedURL = null;
        bool done = false;
        bool error = false;

        string fullPath = $"kidsxr/modules/{moduleName}/{moduleName}/{QualitySubcollection}/{docName}";
        Debug.Log($"[FetchBundle: {moduleName}] Firestore path: {fullPath}");

        db.Collection("kidsxr")
          .Document("modules")
          .Collection(moduleName)
          .Document(moduleName)
          .Collection(QualitySubcollection)
          .Document(docName)
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              if (task.IsFaulted || task.IsCanceled)
              {
                  Debug.LogError($"[FetchBundle: {moduleName}] Firestore task faulted for {docName}: {task.Exception?.Message}");
                  error = true;
              }
              else if (!task.Result.Exists)
              {
                  Debug.LogError($"[FetchBundle: {moduleName}] Document does not exist at: {fullPath}");
                  error = true;
              }
              else
              {
                  UrlData data = task.Result.ConvertTo<UrlData>();
                  resolvedURL = data.URL;
                  Debug.Log($"[FetchBundle: {moduleName}] Firestore returned URL for {docName}: {resolvedURL}");
              }
              done = true;
          });

        yield return new WaitUntil(() => done);
        onResult?.Invoke(error ? null : resolvedURL);
    }

    // ─── Download and save to disk only — no instantiate ─────────────────────

    IEnumerator DownloadAndSave(string url, string docName,
        int index, int total, System.Action<bool> onDone)
    {
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SendWebRequest();

        float fakeProgress = 0f;

        while (!request.isDone)
        {
            fakeProgress += Time.deltaTime * 0.3f;
            int pct = Mathf.Clamp(Mathf.RoundToInt(fakeProgress * 100), 0, 90);
            if (progressText != null)
                progressText.text = $"Downloading {index}/{total}... {pct}%";
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[FetchBundle: {moduleName}] Download failed for {docName}: {request.error} | URL: {url}");
            if (progressText != null)
                progressText.text = $"Failed: {docName}";
            onDone?.Invoke(false);
            yield break;
        }

        if (progressText != null)
            progressText.text = $"Downloading {index}/{total}... 100%";

        string savePath = GetLocalFilePath(docName, QualitySubcollection);
        File.WriteAllBytes(savePath, request.downloadHandler.data);
        RegisterURL(docName, url);

        Debug.Log($"[FetchBundle: {moduleName}] Saved {docName} to disk — " +
                  $"Size: {request.downloadHandler.data.Length / 1024} KB | Path: {savePath}");

        onDone?.Invoke(true);
    }

    // ─── URL registry ─────────────────────────────────────────────────────────

    [System.Serializable]
    private class URLRegistryData
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();
    }

    void LoadURLRegistry()
    {
        urlRegistry.Clear();
        if (!File.Exists(URLRegistryPath))
        {
            Debug.Log($"[FetchBundle: {moduleName}] No URL registry found — starting fresh");
            return;
        }

        try
        {
            string json = File.ReadAllText(URLRegistryPath);
            URLRegistryData data = JsonUtility.FromJson<URLRegistryData>(json);
            for (int i = 0; i < data.keys.Count; i++)
                urlRegistry[data.keys[i]] = data.values[i];
            Debug.Log($"[FetchBundle: {moduleName}] URL registry loaded — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FetchBundle: {moduleName}] Failed to load URL registry: " + e.Message);
        }
    }

    void SaveURLRegistry()
    {
        try
        {
            URLRegistryData data = new URLRegistryData();
            foreach (var kvp in urlRegistry)
            {
                data.keys.Add(kvp.Key);
                data.values.Add(kvp.Value);
            }
            File.WriteAllText(URLRegistryPath, JsonUtility.ToJson(data, true));
            Debug.Log($"[FetchBundle: {moduleName}] URL registry saved — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[FetchBundle: {moduleName}] Failed to save URL registry: " + e.Message);
        }
    }

    string RegistryKey(string docName)
        => $"{moduleName}_{docName}_{QualitySubcollection}";

    bool IsURLUnchanged(string docName, string url)
        => urlRegistry.TryGetValue(RegistryKey(docName), out string saved) && saved == url;

    void RegisterURL(string docName, string url)
    {
        urlRegistry[RegistryKey(docName)] = url;
        SaveURLRegistry();
    }

    string GetLocalFilePath(string docName, string quality)
        => Path.Combine(LocalStorageFolder, $"{moduleName}_{docName}_{quality}.bundle");
}