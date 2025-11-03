using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class LevelRotationManager : MonoBehaviour
{
    public static LevelRotationManager Instance { get; private set; }

    public enum Competitor { Agent, Human }

    [Serializable]
    public struct ScoreState
    {
        public int gems;
        public int finishes;
        public int enemyHits;
        public float bestLapTime;
        public float lastLapTime;
        public int deaths;
    }

    [Min(1)] public int winsPerLevel = 5;

    [Header("Lose Streak")]
    [Min(1)]
    [Tooltip("Advance to next level after this many consecutive losses.")]
    public int lossesToSkipLevel = 10;

    [Header("Runtime (auto filled)")]
    [Tooltip("Populated automatically from Editor list.")]
    public List<string> levelSceneNames = new List<string>();

#if UNITY_EDITOR
    [Header("Editor Scene Picker")]
    [Tooltip("Drag level scenes here in order.")]
    public List<SceneAsset> levelSceneAssets = new List<SceneAsset>();

    [Tooltip("If ON missing scenes are auto added to Build Settings in Editor.")]
    public bool autoAddToBuildSettings = true;
#endif

    [SerializeField] private int currentLevelIdx = 0;
    [SerializeField] private int winsOnThisLevel = 0;
    [SerializeField] private int consecutiveLosses = 0;
    [SerializeField] private float waitBeforeSceneChangeInSec = 3f;

    [SerializeField] private ScoreState agentLevelScore;
    [SerializeField] private ScoreState humanLevelScore;

    [SerializeField] private ScoreState agentTotalScore;
    [SerializeField] private ScoreState humanTotalScore;

    public Action<ScoreState, ScoreState> OnLevelScoresChanged;
    public Action<ScoreState, ScoreState> OnTotalScoresChanged;

    [SerializeField] private bool isTransitioning = false;
    [SerializeField] private bool winRegisteredThisLevel = false;

    public bool isPlayerWon = false;
    public bool isAgentWon = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

#if UNITY_EDITOR
        SyncSceneNamesFromAssets();
        if (autoAddToBuildSettings) EnsureInBuildSettings();
#endif

        agentTotalScore.bestLapTime = float.PositiveInfinity;
        humanTotalScore.bestLapTime = float.PositiveInfinity;

        ResetLevelScores();
    }

    private void Start()
    {
        if (levelSceneNames.Count == 0)
        {
            Debug.LogError("[LevelRotationManager] No levels configured.");
            return;
        }

        string active = SceneManager.GetActiveScene().name;
        int idx = levelSceneNames.IndexOf(active);
        if (idx >= 0) currentLevelIdx = idx;
        else LoadCurrentLevel();
    }

    public void RegisterWin()
    {
        if (levelSceneNames.Count == 0) return;
        if (isTransitioning) return;
        if (winRegisteredThisLevel) return;

        winRegisteredThisLevel = true;
        consecutiveLosses = 0;
        winsOnThisLevel++;

        if (winsOnThisLevel >= winsPerLevel)
        {
            winsOnThisLevel = 0;
            currentLevelIdx = (currentLevelIdx + 1) % levelSceneNames.Count;
            StartCoroutine(LoadAfterDelay(waitBeforeSceneChangeInSec));
        }
    }

    private IEnumerator LoadAfterDelay(float seconds)
    {

        Time.timeScale = 0f;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        Time.timeScale = 1f;
        isPlayerWon = false;
        isAgentWon = false;
        LoadCurrentLevel();
    }

    public void RegisterLoss()
    {
        if (levelSceneNames.Count == 0) return;
        if (isTransitioning) return;

        consecutiveLosses++;

        if (consecutiveLosses >= lossesToSkipLevel)
        {
            consecutiveLosses = 0;
            winsOnThisLevel = 0;
            currentLevelIdx = (currentLevelIdx + 1) % levelSceneNames.Count;
            LoadCurrentLevel();
        }
        else
        {
            LoadCurrentLevel();
        }
    }

    public void ForceNextLevel()
    {
        if (levelSceneNames.Count == 0) return;
        if (isTransitioning) return;

        winsOnThisLevel = 0;
        consecutiveLosses = 0;
        currentLevelIdx = (currentLevelIdx + 1) % levelSceneNames.Count;
        LoadCurrentLevel();
    }

    public void ResetAndReload()
    {
        if (isTransitioning) return;
        winsOnThisLevel = 0;
        consecutiveLosses = 0;
        LoadCurrentLevel();
    }

    public void RegisterGemCollected(Competitor who, int amount = 1)
    {
        ref ScoreState level = ref GetLevelRef(who);
        ref ScoreState total = ref GetTotalRef(who);

        int add = Mathf.Max(0, amount);
        level.gems += add;
        total.gems += add;

        FireScoreEvents();
    }

    public void RegisterEnemyHit(Competitor who, int amount = 1)
    {
        ref ScoreState level = ref GetLevelRef(who);
        ref ScoreState total = ref GetTotalRef(who);

        int add = Mathf.Max(0, amount);
        level.enemyHits += add;
        total.enemyHits += add;

        FireScoreEvents();
    }

    public void RegisterFinish(Competitor who, float lapSeconds)
    {
        ref ScoreState level = ref GetLevelRef(who);
        ref ScoreState total = ref GetTotalRef(who);

        level.finishes += 1;
        total.finishes += 1;

        level.lastLapTime = lapSeconds;
        total.lastLapTime = lapSeconds;

        if (level.bestLapTime <= 0f || lapSeconds < level.bestLapTime) level.bestLapTime = lapSeconds;
        if (total.bestLapTime <= 0f || lapSeconds < total.bestLapTime) total.bestLapTime = lapSeconds;

        if (who == Competitor.Human) isPlayerWon = true; else isAgentWon = true;

        FireScoreEvents();
    }

    public void RegisterDeath(Competitor who)
    {
        ref ScoreState level = ref GetLevelRef(who);
        ref ScoreState total = ref GetTotalRef(who);

        level.deaths += 1;
        total.deaths += 1;

        FireScoreEvents();
    }

    public ScoreState GetLevelScore(Competitor who) => who == Competitor.Agent ? agentLevelScore : humanLevelScore;
    public ScoreState GetTotalScore(Competitor who) => who == Competitor.Agent ? agentTotalScore : humanTotalScore;

    public int WinsLeftOnThisLevel => Mathf.Max(0, winsPerLevel - winsOnThisLevel);
    public int CurrentLossStreak => consecutiveLosses;
    public int CurrentLevelIndex => currentLevelIdx;
    public string CurrentLevelName => (levelSceneNames.Count > 0 && currentLevelIdx < levelSceneNames.Count)
        ? levelSceneNames[currentLevelIdx] : "";

    private void LoadCurrentLevel()
    {
        if (levelSceneNames.Count == 0) return;
        if (isTransitioning) return;

        isTransitioning = true;
        string sceneName = levelSceneNames[currentLevelIdx];
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
        // OnSceneLoaded will finalize
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        int idx = levelSceneNames.IndexOf(scene.name);
        if (idx >= 0) currentLevelIdx = idx;

        ResetLevelScores();

        winRegisteredThisLevel = false;
        isTransitioning = false;
    }

    private void ResetLevelScores()
    {
        agentLevelScore = new ScoreState { bestLapTime = float.PositiveInfinity };
        humanLevelScore = new ScoreState { bestLapTime = float.PositiveInfinity };
        FireScoreEvents();
    }

    private void FireScoreEvents()
    {
        OnLevelScoresChanged?.Invoke(agentLevelScore, humanLevelScore);
        OnTotalScoresChanged?.Invoke(agentTotalScore, humanTotalScore);
    }

    private ref ScoreState GetLevelRef(Competitor who)
    {
        if (who == Competitor.Agent) return ref agentLevelScore;
        return ref humanLevelScore;
    }

    private ref ScoreState GetTotalRef(Competitor who)
    {
        if (who == Competitor.Agent) return ref agentTotalScore;
        return ref humanTotalScore;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
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

        if (changed) EditorBuildSettings.scenes = list.ToArray();
    }
#endif
}
