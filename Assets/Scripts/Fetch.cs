/*using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.Rendering;

[System.Serializable]
public class ButtonModelPair
{
    public string documentName;
    public Transform parent;
}

public class Fetch : MonoBehaviour
{
    FirebaseFirestore db;

    [Header("Model Stages")]
    [SerializeField] private List<ButtonModelPair> buttonModels;
    
    [Header("Module Settings")]
    [Tooltip("Must match Firebase name and FetchBundle moduleName exactly")]
    [SerializeField] private string moduleName;  // e.g. "FrogLifeCycle"
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI downloadText;
    [SerializeField] private GameObject qualityPanel;

    [Header("Quality Buttons")]
    [SerializeField] private Button lowButton;
    [SerializeField] private Button highButton;

    private bool isDownloading = false;

    // Maps to the actual Firestore subcollection names visible in your screenshot
    private string QualitySubcollection => selectedQuality == ModelQuality.Low ? "asset_low" : "asset_high";

    private Dictionary<string, AssetBundle> bundleCache = new Dictionary<string, AssetBundle>();

    private string LocalStorageFolder => Path.Combine(Application.persistentDataPath, "CachedBundles");
    private string URLRegistryPath => Path.Combine(Application.persistentDataPath, "url_registry.json");

    private Dictionary<string, string> urlRegistry = new Dictionary<string, string>();

    public enum ModelQuality { Low, High }
    private ModelQuality selectedQuality;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
#if UNITY_EDITOR
        ClearLocalStorage();
        Debug.Log("Editor mode: cache cleared on play");
#endif

        db = FirebaseFirestore.DefaultInstance;

        if (!Directory.Exists(LocalStorageFolder))
            Directory.CreateDirectory(LocalStorageFolder);

        LoadURLRegistry();

        qualityPanel.SetActive(true);
    }

    // ─── Quality panel ────────────────────────────────────────────────────────

    public void OpenQualityPanel()
    {
        qualityPanel.SetActive(true);
        UpdateQualityUI();
    }

    public void CloseQualityPanel()
    {
        qualityPanel.SetActive(false);
    }

    public void SelectLowQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.Low;
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(LoadAllModels());
    }

    public void SelectHighQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.High;
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(LoadAllModels());
    }

    void UpdateQualityUI()
    {
        lowButton.image.color = selectedQuality == ModelQuality.Low ? Color.green : Color.white;
        highButton.image.color = selectedQuality == ModelQuality.High ? Color.green : Color.white;
    }

    // ─── URL registry (change-detection between sessions) ────────────────────

    [System.Serializable]
    private class URLRegistryData
    {
        public List<string> keys = new List<string>();
        public List<string> values = new List<string>();
    }

    void LoadURLRegistry()
    {
        urlRegistry.Clear();
        if (!File.Exists(URLRegistryPath)) return;

        try
        {
            string json = File.ReadAllText(URLRegistryPath);
            URLRegistryData data = JsonUtility.FromJson<URLRegistryData>(json);
            for (int i = 0; i < data.keys.Count; i++)
                urlRegistry[data.keys[i]] = data.values[i];
            Debug.Log($"URL registry loaded — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Failed to load URL registry: " + e.Message);
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
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Failed to save URL registry: " + e.Message);
        }
    }

    // Cache key encodes doc name + subcollection so asset_low/asset_high stay separate
    string RegistryKey(string docName)
    => $"{moduleName}_{docName}_{QualitySubcollection}";
    bool IsURLUnchanged(string docName, string url)
        => urlRegistry.TryGetValue(RegistryKey(docName), out string saved) && saved == url;

    void RegisterURL(string docName, string url)
    {
        urlRegistry[RegistryKey(docName)] = url;
        SaveURLRegistry();
    }

    // ─── Local bundle storage ─────────────────────────────────────────────────

    // string GetLocalFilePath(string docName)
    //  => Path.Combine(LocalStorageFolder, $"{docName}_{QualitySubcollection}.bundle");
    string GetLocalFilePath(string docName)
  => Path.Combine(LocalStorageFolder, $"{moduleName}_{docName}_{QualitySubcollection}.bundle");


    bool IsBundleSavedLocally(string docName)
        => File.Exists(GetLocalFilePath(docName));

    void SaveBundleLocally(string docName, byte[] data)
        => File.WriteAllBytes(GetLocalFilePath(docName), data);

    void DeleteLocalBundle(string docName)
    {
        string path = GetLocalFilePath(docName);
        if (File.Exists(path)) File.Delete(path);
        Debug.Log($"Deleted stale bundle: {docName}_{QualitySubcollection}");
    }

    // ─── Firestore fetch ──────────────────────────────────────────────────────

    // Path: kidsxr → modules → FrogLifeCycle → FrogLifeCycle → asset_low/asset_high → model_1..4
    IEnumerator FetchURL(string docName, System.Action<string> onResult)
    {
        string resolvedURL = null;
        bool done = false;
        bool error = false;

        db.Collection("kidsxr")
          .Document("modules")
          .Collection("FrogLifeCycle")
          .Document("FrogLifeCycle")
          .Collection(QualitySubcollection)  // "asset_low" or "asset_high"
          .Document(docName)                 // "model_1", "model_2", "model_3", "model_4"
          .GetSnapshotAsync()
          .ContinueWithOnMainThread(task =>
          {
              if (task.IsFaulted || task.IsCanceled)
              {
                  Debug.LogError($"Firestore error for {docName}: {task.Exception}");
                  error = true;
              }
              else if (!task.Result.Exists)
              {
                  Debug.LogError($"Document not found: {QualitySubcollection}/{docName}");
                  error = true;
              }
              else
              {
                  UrlData data = task.Result.ConvertTo<UrlData>();
                  resolvedURL = data.URL;
                  Debug.Log($"Fetched URL for {docName} [{QualitySubcollection}]: {resolvedURL}");
              }
              done = true;
          });

        yield return new WaitUntil(() => done);

        onResult?.Invoke(error ? null : resolvedURL);
    }

    // ─── Main load loop ───────────────────────────────────────────────────────

    IEnumerator LoadAllModels()
    {
        isDownloading = true;
        downloadText.gameObject.SetActive(true);

        int total = buttonModels.Count;
        int completed = 0;

        foreach (var pair in buttonModels)
        {
            string cacheKey = RegistryKey(pair.documentName);

            // Already in memory this session — skip
            if (bundleCache.ContainsKey(cacheKey))
            {
                completed++;
                downloadText.text = $"Ready {completed}/{total}";
                continue;
            }

            // 1. Resolve URL from Firestore
            downloadText.text = $"Checking {pair.documentName} ({completed + 1}/{total})...";

            string resolvedURL = null;
            bool urlReady = false;

            yield return StartCoroutine(FetchURL(pair.documentName, url =>
            {
                resolvedURL = url;
                urlReady = true;
            }));

            yield return new WaitUntil(() => urlReady);

            if (resolvedURL == null)
            {
                completed++;
                downloadText.text = $"Skipped {pair.documentName} (fetch error)";
                continue;
            }

            // 2. On disk and URL unchanged → load from disk
            if (IsBundleSavedLocally(pair.documentName) &&
                IsURLUnchanged(pair.documentName, resolvedURL))
            {
                downloadText.text = $"Loading from device ({completed + 1}/{total})...";

                bool loadDone = false;

                yield return StartCoroutine(LoadBundleFromDisk(
                    pair.documentName, pair.parent, _ => loadDone = true));

                yield return new WaitUntil(() => loadDone);
                completed++;
                downloadText.text = $"Loaded {completed}/{total}";
                continue;
            }

            // 3. URL changed or file missing → delete stale and re-download
            if (IsBundleSavedLocally(pair.documentName))
            {
                Debug.Log($"URL changed for {pair.documentName} — re-downloading");
                DeleteLocalBundle(pair.documentName);

                if (bundleCache.ContainsKey(cacheKey))
                {
                    bundleCache[cacheKey].Unload(true);
                    bundleCache.Remove(cacheKey);
                }
            }

            bool modelDone = false;

            yield return StartCoroutine(DownloadAndLoadBundle(
                resolvedURL, pair.documentName, pair.parent,
                completed + 1, total, _ => modelDone = true));

            yield return new WaitUntil(() => modelDone);

            completed++;
            downloadText.text = $"Done {completed}/{total}";
        }

        downloadText.text = "All models ready!";
        yield return new WaitForSeconds(1f);
        downloadText.gameObject.SetActive(false);

        isDownloading = false;
    }

    // ─── Load bundle from disk → instantiate under parent ────────────────────

    IEnumerator LoadBundleFromDisk(string docName, Transform parent, System.Action<bool> onDone)
    {
        string cacheKey = RegistryKey(docName);
        string path = GetLocalFilePath(docName);

        var bundleRequest = AssetBundle.LoadFromFileAsync(path);
        yield return bundleRequest;

        AssetBundle bundle = bundleRequest.assetBundle;

        if (bundle == null)
        {
            Debug.LogError($"AssetBundle.LoadFromFile failed: {path}");
            onDone?.Invoke(false);
            yield break;
        }

        string[] names = bundle.GetAllAssetNames();
        var assetRequest = bundle.LoadAssetAsync<GameObject>(names[0]);
        yield return assetRequest;

        GameObject prefab = assetRequest.asset as GameObject;

        if (prefab == null)
        {
            Debug.LogError($"No GameObject found in bundle: {docName}");
            bundle.Unload(false);
            onDone?.Invoke(false);
            yield break;
        }

        Transform targetParent = parent != null ? parent : this.transform;
        GameObject instance = Instantiate(prefab, targetParent);
        instance.SetActive(true); // FrogLifeCycleInteraction.Start() runs and takes over

        FixMaterials(instance);

        bundleCache[cacheKey] = bundle;

        Debug.Log($"Instantiated '{docName}' under '{targetParent.name}'");
        onDone?.Invoke(true);
    }

    // ─── Download → save → load ───────────────────────────────────────────────

    IEnumerator DownloadAndLoadBundle(string url, string docName, Transform parent,
        int index, int total, System.Action<bool> onDone)
    {
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SendWebRequest();

        float fakeProgress = 0f;

        while (!request.isDone)
        {
            fakeProgress += Time.deltaTime * 0.3f;
            int pct = Mathf.Clamp(Mathf.RoundToInt(fakeProgress * 100), 0, 90);
            downloadText.text = $"Downloading {index}/{total}... {pct}%";
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Download failed [{docName}]: {request.error}");
            downloadText.text = $"Failed: {docName}";
            onDone?.Invoke(false);
            yield break;
        }

        downloadText.text = $"Downloading {index}/{total}... 100%";

        SaveBundleLocally(docName, request.downloadHandler.data);
        RegisterURL(docName, url);

        bool loadDone = false;
        bool loadOk = false;

        yield return StartCoroutine(LoadBundleFromDisk(
            docName, parent, result => { loadOk = result; loadDone = true; }));

        yield return new WaitUntil(() => loadDone);
        onDone?.Invoke(loadOk);
    }

    // ─── URP material fix ─────────────────────────────────────────────────────

    void FixMaterials(GameObject root)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("URP Lit shader not found!");
            return;
        }

        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = rend.materials;

            for (int i = 0; i < mats.Length; i++)
            {
                var old = mats[i];
                if (old == null) continue;

                var mat = new Material(shader);

                if (old.mainTexture != null)
                    mat.SetTexture("_BaseMap", old.mainTexture);

                if (old.HasProperty("_Color"))
                    mat.SetColor("_BaseColor", old.color);

                if (old.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", old.GetTexture("_BumpMap"));
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (old.HasProperty("_MetallicGlossMap"))
                    mat.SetTexture("_MetallicGlossMap", old.GetTexture("_MetallicGlossMap"));

                mat.SetFloat("_Surface", 0);
                mat.renderQueue = (int)RenderQueue.Geometry;

                mats[i] = mat;
            }

            rend.materials = mats;
        }
    }

    // ─── Clear local storage ──────────────────────────────────────────────────

    public void ClearLocalStorage()
    {
        foreach (var bundle in bundleCache.Values)
            bundle.Unload(true);
        bundleCache.Clear();

        if (Directory.Exists(LocalStorageFolder))
        {
            Directory.Delete(LocalStorageFolder, recursive: true);
            Directory.CreateDirectory(LocalStorageFolder);
        }

        if (File.Exists(URLRegistryPath))
            File.Delete(URLRegistryPath);

        urlRegistry.Clear();
        Debug.Log("Local storage cleared.");
    }

    void OnDestroy()
    {
        foreach (var bundle in bundleCache.Values)
            bundle.Unload(false);
    }
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
using UnityEngine.Rendering;

[System.Serializable]
public class ButtonModelPair
{
    public string documentName;
    public Transform parent;
}

public class Fetch : MonoBehaviour
{
    FirebaseFirestore db;

    [Header("Module Settings")]
    [Tooltip("Must match Firebase name and FetchBundle moduleName exactly")]
    [SerializeField] private string moduleName;

    [Header("Model Stages")]
    [SerializeField] private List<ButtonModelPair> buttonModels;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI downloadText;
    [SerializeField] private GameObject qualityPanel;

    [Header("Quality Buttons")]
    [SerializeField] private Button lowButton;
    [SerializeField] private Button highButton;

    [Header("Editor Debug")]
    [Tooltip("Tick this to clear cache on Play in Editor — untick to keep cache and test loading")]
    [SerializeField] private bool clearCacheOnPlay = false;

    private bool isDownloading = false;

    private string QualitySubcollection => selectedQuality == ModelQuality.Low ? "asset_low" : "asset_high";

    private Dictionary<string, AssetBundle> bundleCache = new Dictionary<string, AssetBundle>();

    private string LocalStorageFolder => Path.Combine(Application.persistentDataPath, "CachedBundles");
    private string URLRegistryPath => Path.Combine(Application.persistentDataPath, "url_registry.json");

    private Dictionary<string, string> urlRegistry = new Dictionary<string, string>();

    public enum ModelQuality { Low, High }
    private ModelQuality selectedQuality;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    void Start()
    {
#if UNITY_EDITOR
        if (clearCacheOnPlay)
        {
            ClearLocalStorage();
            Debug.Log($"[Fetch: {moduleName}] Editor mode: cache cleared on play");
        }
        else
        {
            Debug.Log($"[Fetch: {moduleName}] Editor mode: keeping existing cache (clearCacheOnPlay is OFF)");
        }
#endif

        db = FirebaseFirestore.DefaultInstance;
        Debug.Log($"[Fetch: {moduleName}] Initializing...");

        if (!Directory.Exists(LocalStorageFolder))
        {
            Directory.CreateDirectory(LocalStorageFolder);
            Debug.Log($"[Fetch: {moduleName}] Created local storage folder: {LocalStorageFolder}");
        }
        else
        {
            Debug.Log($"[Fetch: {moduleName}] Local storage folder found: {LocalStorageFolder}");
        }

        LoadURLRegistry();

        qualityPanel.SetActive(true);

        Debug.Log($"[Fetch: {moduleName}] Ready — {buttonModels.Count} models registered");
    }

    // ─── Quality panel ────────────────────────────────────────────────────────

    public void OpenQualityPanel()
    {
        qualityPanel.SetActive(true);
        UpdateQualityUI();
    }

    public void CloseQualityPanel()
    {
        qualityPanel.SetActive(false);
    }

    public void SelectLowQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.Low;
        Debug.Log($"[Fetch: {moduleName}] Quality selected: LOW (asset_low)");
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(LoadAllModels());
    }

    public void SelectHighQuality()
    {
        if (isDownloading) return;
        selectedQuality = ModelQuality.High;
        Debug.Log($"[Fetch: {moduleName}] Quality selected: HIGH (asset_high)");
        UpdateQualityUI();
        qualityPanel.SetActive(false);
        StartCoroutine(LoadAllModels());
    }

    void UpdateQualityUI()
    {
        lowButton.image.color = selectedQuality == ModelQuality.Low ? Color.green : Color.white;
        highButton.image.color = selectedQuality == ModelQuality.High ? Color.green : Color.white;
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
            Debug.Log($"[Fetch: {moduleName}] No URL registry found — starting fresh");
            return;
        }

        try
        {
            string json = File.ReadAllText(URLRegistryPath);
            URLRegistryData data = JsonUtility.FromJson<URLRegistryData>(json);
            for (int i = 0; i < data.keys.Count; i++)
                urlRegistry[data.keys[i]] = data.values[i];
            Debug.Log($"[Fetch: {moduleName}] URL registry loaded — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Fetch: {moduleName}] Failed to load URL registry: " + e.Message);
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
            Debug.Log($"[Fetch: {moduleName}] URL registry saved — {urlRegistry.Count} entries");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Fetch: {moduleName}] Failed to save URL registry: " + e.Message);
        }
    }

    string RegistryKey(string docName) => $"{moduleName}_{docName}_{QualitySubcollection}";

    bool IsURLUnchanged(string docName, string url)
        => urlRegistry.TryGetValue(RegistryKey(docName), out string saved) && saved == url;

    void RegisterURL(string docName, string url)
    {
        urlRegistry[RegistryKey(docName)] = url;
        SaveURLRegistry();
    }

    // ─── Local bundle storage ─────────────────────────────────────────────────

    string GetLocalFilePath(string docName)
        => Path.Combine(LocalStorageFolder, $"{moduleName}_{docName}_{QualitySubcollection}.bundle");

    bool IsBundleSavedLocally(string docName)
        => File.Exists(GetLocalFilePath(docName));

    void SaveBundleLocally(string docName, byte[] data)
        => File.WriteAllBytes(GetLocalFilePath(docName), data);

    void DeleteLocalBundle(string docName)
    {
        string path = GetLocalFilePath(docName);
        if (File.Exists(path)) File.Delete(path);
        Debug.Log($"[Fetch: {moduleName}] Deleted stale bundle: {docName}_{QualitySubcollection}");
    }

    // ─── Firestore fetch ──────────────────────────────────────────────────────

    IEnumerator FetchURL(string docName, System.Action<string> onResult)
    {
        string resolvedURL = null;
        bool done = false;
        bool error = false;

        string fullPath = $"kidsxr/modules/{moduleName}/{moduleName}/{QualitySubcollection}/{docName}";
        Debug.Log($"[Fetch: {moduleName}] Firestore path: {fullPath}");

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
                  Debug.LogError($"[Fetch: {moduleName}] Firestore task faulted for {docName}: {task.Exception?.Message}");
                  error = true;
              }
              else if (!task.Result.Exists)
              {
                  Debug.LogError($"[Fetch: {moduleName}] Document does not exist at: {fullPath}");
                  error = true;
              }
              else
              {
                  UrlData data = task.Result.ConvertTo<UrlData>();
                  resolvedURL = data.URL;
                  Debug.Log($"[Fetch: {moduleName}] Firestore returned URL for {docName}: {resolvedURL}");
              }
              done = true;
          });

        yield return new WaitUntil(() => done);
        onResult?.Invoke(error ? null : resolvedURL);
    }

    // ─── Main load loop ───────────────────────────────────────────────────────

    IEnumerator LoadAllModels()
    {
        isDownloading = true;
        downloadText.gameObject.SetActive(true);

        int total = buttonModels.Count;
        int completed = 0;

        Debug.Log($"[Fetch: {moduleName}] ── Starting load ──");
        Debug.Log($"[Fetch: {moduleName}] Quality: {QualitySubcollection} | Models: {total}");

        foreach (var pair in buttonModels)
        {
            string cacheKey = RegistryKey(pair.documentName);

            Debug.Log($"[Fetch: {moduleName}] ── Processing {pair.documentName} ({completed + 1}/{total}) ──");

            // Already in runtime memory this session
            if (bundleCache.ContainsKey(cacheKey))
            {
                Debug.Log($"[Fetch: {moduleName}] {pair.documentName} already in runtime memory — skipping");
                completed++;
                downloadText.text = $"Ready {completed}/{total}";
                continue;
            }

            // 1. Fetch URL from Firestore
            downloadText.text = $"Checking {pair.documentName} ({completed + 1}/{total})...";

            string resolvedURL = null;
            bool urlReady = false;

            yield return StartCoroutine(FetchURL(pair.documentName, url =>
            {
                resolvedURL = url;
                urlReady = true;
            }));

            yield return new WaitUntil(() => urlReady);

            if (resolvedURL == null)
            {
                Debug.LogError($"[Fetch: {moduleName}] URL fetch failed for {pair.documentName} — skipping");
                completed++;
                downloadText.text = $"Skipped {pair.documentName} (fetch error)";
                continue;
            }

            // 2. Check disk
            bool onDisk = IsBundleSavedLocally(pair.documentName);
            bool urlSame = IsURLUnchanged(pair.documentName, resolvedURL);

            Debug.Log($"[Fetch: {moduleName}] {pair.documentName} — " +
                      $"On disk: {onDisk} | URL unchanged: {urlSame}");

            if (onDisk && urlSame)
            {
                Debug.Log($"[Fetch: {moduleName}] {pair.documentName} loading from CACHE (no download needed)");
                downloadText.text = $"Loading from device ({completed + 1}/{total})...";

                bool loadDone = false;

                yield return StartCoroutine(LoadBundleFromDisk(
                    pair.documentName, pair.parent, _ => loadDone = true));

                yield return new WaitUntil(() => loadDone);
                completed++;
                downloadText.text = $"Loaded {completed}/{total}";
                continue;
            }

            // 3. URL changed or missing — delete stale
            if (onDisk && !urlSame)
            {
                Debug.Log($"[Fetch: {moduleName}] URL changed for {pair.documentName} — deleting stale bundle");
                DeleteLocalBundle(pair.documentName);

                if (bundleCache.ContainsKey(cacheKey))
                {
                    bundleCache[cacheKey].Unload(true);
                    bundleCache.Remove(cacheKey);
                }
            }

            // 4. Download
            Debug.Log($"[Fetch: {moduleName}] {pair.documentName} — DOWNLOADING from: {resolvedURL}");

            bool modelDone = false;
            bool modelOk = false;

            yield return StartCoroutine(DownloadAndLoadBundle(
                resolvedURL, pair.documentName, pair.parent,
                completed + 1, total,
                result => { modelOk = result; modelDone = true; }));

            yield return new WaitUntil(() => modelDone);

            if (modelOk)
                Debug.Log($"[Fetch: {moduleName}] {pair.documentName} downloaded and loaded successfully");
            else
                Debug.LogError($"[Fetch: {moduleName}] {pair.documentName} download or load failed");

            completed++;
            downloadText.text = $"Done {completed}/{total}";
        }

        Debug.Log($"[Fetch: {moduleName}] ── All {total} models processed ──");

        downloadText.text = "All models ready!";
        yield return new WaitForSeconds(1f);
        downloadText.gameObject.SetActive(false);

        isDownloading = false;
    }

    // ─── Load bundle from disk → instantiate under parent ────────────────────

    IEnumerator LoadBundleFromDisk(string docName, Transform parent, System.Action<bool> onDone)
    {
        string cacheKey = RegistryKey(docName);
        string path = GetLocalFilePath(docName);

        Debug.Log($"[Fetch: {moduleName}] Loading bundle from disk: {path}");

        var bundleRequest = AssetBundle.LoadFromFileAsync(path);
        yield return bundleRequest;

        AssetBundle bundle = bundleRequest.assetBundle;

        if (bundle == null)
        {
            Debug.LogError($"[Fetch: {moduleName}] AssetBundle.LoadFromFile failed: {path}");
            onDone?.Invoke(false);
            yield break;
        }

        Debug.Log($"[Fetch: {moduleName}] Bundle loaded from disk — reading assets...");

        string[] names = bundle.GetAllAssetNames();
        var assetRequest = bundle.LoadAssetAsync<GameObject>(names[0]);
        yield return assetRequest;

        GameObject prefab = assetRequest.asset as GameObject;

        if (prefab == null)
        {
            Debug.LogError($"[Fetch: {moduleName}] No GameObject found in bundle: {docName}");
            bundle.Unload(false);
            onDone?.Invoke(false);
            yield break;
        }

        Transform targetParent = parent != null ? parent : this.transform;
        GameObject instance = Instantiate(prefab, targetParent);
        instance.SetActive(true);

        FixMaterials(instance);

        bundleCache[cacheKey] = bundle;

        Debug.Log($"[Fetch: {moduleName}] Instantiated '{docName}' under '{targetParent.name}'");
        onDone?.Invoke(true);
    }

    // ─── Download → save → load ───────────────────────────────────────────────

    IEnumerator DownloadAndLoadBundle(string url, string docName, Transform parent,
        int index, int total, System.Action<bool> onDone)
    {
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SendWebRequest();

        float fakeProgress = 0f;

        while (!request.isDone)
        {
            fakeProgress += Time.deltaTime * 0.3f;
            int pct = Mathf.Clamp(Mathf.RoundToInt(fakeProgress * 100), 0, 90);
            downloadText.text = $"Downloading {index}/{total}... {pct}%";
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Fetch: {moduleName}] Download failed [{docName}]: {request.error} | URL: {url}");
            downloadText.text = $"Failed: {docName}";
            onDone?.Invoke(false);
            yield break;
        }

        downloadText.text = $"Downloading {index}/{total}... 100%";

        SaveBundleLocally(docName, request.downloadHandler.data);
        RegisterURL(docName, url);

        Debug.Log($"[Fetch: {moduleName}] Download complete for {docName} — " +
                  $"Size: {request.downloadHandler.data.Length / 1024} KB | Saved to disk");

        bool loadDone = false;
        bool loadOk = false;

        yield return StartCoroutine(LoadBundleFromDisk(
            docName, parent, result => { loadOk = result; loadDone = true; }));

        yield return new WaitUntil(() => loadDone);
        onDone?.Invoke(loadOk);
    }

    // ─── URP material fix ─────────────────────────────────────────────────────

    void FixMaterials(GameObject root)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning($"[Fetch: {moduleName}] URP Lit shader not found!");
            return;
        }

        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = rend.materials;

            for (int i = 0; i < mats.Length; i++)
            {
                var old = mats[i];
                if (old == null) continue;

                var mat = new Material(shader);

                if (old.mainTexture != null)
                    mat.SetTexture("_BaseMap", old.mainTexture);

                if (old.HasProperty("_Color"))
                    mat.SetColor("_BaseColor", old.color);

                if (old.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", old.GetTexture("_BumpMap"));
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (old.HasProperty("_MetallicGlossMap"))
                    mat.SetTexture("_MetallicGlossMap", old.GetTexture("_MetallicGlossMap"));

                mat.SetFloat("_Surface", 0);
                mat.renderQueue = (int)RenderQueue.Geometry;

                mats[i] = mat;
            }

            rend.materials = mats;
        }

        Debug.Log($"[Fetch: {moduleName}] Materials fixed on: {root.name}");
    }

    // ─── Clear local storage ──────────────────────────────────────────────────

    public void ClearLocalStorage()
    {
        foreach (var bundle in bundleCache.Values)
            bundle.Unload(true);
        bundleCache.Clear();

        if (Directory.Exists(LocalStorageFolder))
        {
            Directory.Delete(LocalStorageFolder, recursive: true);
            Directory.CreateDirectory(LocalStorageFolder);
        }

        if (File.Exists(URLRegistryPath))
            File.Delete(URLRegistryPath);

        urlRegistry.Clear();
        Debug.Log($"[Fetch: {moduleName}] Local storage cleared.");
    }

    void OnDestroy()
    {
        foreach (var bundle in bundleCache.Values)
            bundle.Unload(false);
    }
}