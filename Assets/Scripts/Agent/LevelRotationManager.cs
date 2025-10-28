using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;          // for SceneAsset, Build Settings sync
using UnityEditor.SceneManagement;
#endif

public class LevelRotationManager : MonoBehaviour
{
    public static LevelRotationManager Instance { get; private set; }

    [Min(1)]
    public int winsPerLevel = 5;

    [SerializeField] private int currentLevelIdx = 0;
    [SerializeField] private int winsOnThisLevel = 0;

    // ---- Editor-friendly scene picking ----
#if UNITY_EDITOR
    [Header("Editor Scene Picker (drag scenes here)")]
    [Tooltip("Drag your level scenes here in the desired order. In Play Mode, this list syncs to Level Scene Names.")]
    public List<SceneAsset> levelSceneAssets = new List<SceneAsset>();

    [Tooltip("If ON (Editor only), missing scenes are auto-added to Build Settings when you change this component or press Play.")]
    public bool autoAddToBuildSettings = true;
#endif

    [Header("Runtime (auto-filled) - Do not edit")]
    [Tooltip("Populated automatically from Editor list (or you can type names if not using the Editor).")]
    public List<string> levelSceneNames = new List<string>();   // used at runtime

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

#if UNITY_EDITOR
        SyncSceneNamesFromAssets();
        if (autoAddToBuildSettings) EnsureInBuildSettings();
#endif
    }

    void Start()
    {
        if (levelSceneNames.Count == 0)
        {
            Debug.LogError("[LevelRotationManager] Configure levels (drag scenes into the Editor list).");
            return;
        }

        // If you pressed Play in any level, sync to that level; otherwise load the configured one.
        string active = SceneManager.GetActiveScene().name;
        int idx = levelSceneNames.IndexOf(active);
        if (idx >= 0) currentLevelIdx = idx;
        else LoadCurrentLevel();
    }

    public void RegisterWin()
    {
        if (levelSceneNames.Count == 0) return;

        winsOnThisLevel++;
        if (winsOnThisLevel >= winsPerLevel)
        {
            winsOnThisLevel = 0;
            currentLevelIdx = (currentLevelIdx + 1) % levelSceneNames.Count; // wrap to first
            LoadCurrentLevel();
        }
    }

    public void ForceNextLevel()
    {
        if (levelSceneNames.Count == 0) return;
        winsOnThisLevel = 0;
        currentLevelIdx = (currentLevelIdx + 1) % levelSceneNames.Count;
        LoadCurrentLevel();
    }

    public void ResetAndReload()
    {
        winsOnThisLevel = 0;
        LoadCurrentLevel();
    }

    public int WinsLeftOnThisLevel => Mathf.Max(0, winsPerLevel - winsOnThisLevel);
    public int CurrentLevelIndex => currentLevelIdx;
    public string CurrentLevelName => (levelSceneNames.Count > 0 && currentLevelIdx < levelSceneNames.Count) ? levelSceneNames[currentLevelIdx] : "";

    private void LoadCurrentLevel()
    {
        if (levelSceneNames.Count == 0) return;
        string sceneName = levelSceneNames[currentLevelIdx];
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        int idx = levelSceneNames.IndexOf(scene.name);
        if (idx >= 0) currentLevelIdx = idx;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Keep runtime names in sync with the dragged SceneAssets.
        SyncSceneNamesFromAssets();
        if (autoAddToBuildSettings) EnsureInBuildSettings();
    }

    private void SyncSceneNamesFromAssets()
    {
        levelSceneNames ??= new List<string>();
        levelSceneNames.Clear();

        foreach (var sa in levelSceneAssets)
        {
            if (sa == null) continue;
            string path = AssetDatabase.GetAssetPath(sa);
            if (string.IsNullOrEmpty(path)) continue;

            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name) && !levelSceneNames.Contains(name))
                levelSceneNames.Add(name);
        }
    }

    private void EnsureInBuildSettings()
    {
        if (levelSceneAssets == null || levelSceneAssets.Count == 0) return;

        // Collect current build scenes
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        bool changed = false;
        foreach (var sa in levelSceneAssets)
        {
            if (sa == null) continue;
            string path = AssetDatabase.GetAssetPath(sa);
            if (string.IsNullOrEmpty(path)) continue;

            bool exists = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].path == path) { exists = true; break; }
            }
            if (!exists)
            {
                list.Add(new EditorBuildSettingsScene(path, true));
                changed = true;
            }
        }

        if (changed)
            EditorBuildSettings.scenes = list.ToArray();
    }
#endif
}
