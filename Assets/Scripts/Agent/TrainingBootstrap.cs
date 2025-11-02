// Assets/Scripts/TrainingBootstrap.cs
using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MLAgents;

public static class TrainingBootstrap
{
    static int desiredW = 2560;
    static int desiredH = 1440;
    static bool desiredFullscreen = true;
    static bool usePopupWindow = true; // treat as windowed
    static int desiredFps = 240;
    static string desiredQuality = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = desiredFps;
        Application.runInBackground = true;
        Time.timeScale = 1f;

        if (Academy.IsInitialized)
            Academy.Instance.AutomaticSteppingEnabled = true;

        ReadArgs();

        // Apply once after first scene is ready, then on every subsequent scene load
        CoroutineRunner.Run(ApplyNextFrame());
        SceneManager.sceneLoaded += (_, __) => CoroutineRunner.Run(ApplyNextFrame());
    }

    static void ReadArgs()
    {
        string[] args = Environment.GetCommandLineArgs();

        desiredFps = ParseInt(args, "--fps", desiredFps);

        // quality: name or index
        string q = ParseString(args, "--quality");
        if (!string.IsNullOrEmpty(q))
        {
            desiredQuality = q;
            if (int.TryParse(q, out int qi))
                QualitySettings.SetQualityLevel(Mathf.Clamp(qi, 0, QualitySettings.names.Length - 1), true);
            else
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                    if (QualitySettings.names[i].Equals(q, StringComparison.OrdinalIgnoreCase))
                        QualitySettings.SetQualityLevel(i, true);
            }
        }

        desiredW = ParseInt(args, "-screen-width", desiredW);
        desiredH = ParseInt(args, "-screen-height", desiredH);
        desiredFullscreen = ParseInt(args, "-screen-fullscreen", desiredFullscreen ? 1 : 0) != 0;

        // Unity player arg, no value. If present, force windowed mode.
        usePopupWindow = args.Any(a => string.Equals(a, "-popupwindow", StringComparison.OrdinalIgnoreCase));
    }

    static IEnumerator ApplyNextFrame()
    {
        // wait one frame so any scene scripts finish their own resolution changes
        yield return null;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = desiredFps;

        // quality again here in case a scene changed it
        if (!string.IsNullOrEmpty(desiredQuality))
        {
            if (int.TryParse(desiredQuality, out int qi))
                QualitySettings.SetQualityLevel(Mathf.Clamp(qi, 0, QualitySettings.names.Length - 1), true);
            else
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                    if (QualitySettings.names[i].Equals(desiredQuality, StringComparison.OrdinalIgnoreCase))
                        QualitySettings.SetQualityLevel(i, true);
            }
        }

#if UNITY_2019_1_OR_NEWER
        if (usePopupWindow || !desiredFullscreen)
            Screen.fullScreenMode = FullScreenMode.Windowed;
        else
            Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
#endif
        Screen.fullScreen = desiredFullscreen && !usePopupWindow;
        Screen.SetResolution(desiredW, desiredH, Screen.fullScreen);

        Debug.Log($"[TrainingBootstrap] size {desiredW}x{desiredH} fullscreen={(Screen.fullScreen ? 1 : 0)} fps={desiredFps} popup={(usePopupWindow ? 1 : 0)}");
    }

    static int ParseInt(string[] args, string key, int fallback)
    {
        // form 1: key value
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int v)) return v;

        // form 2: --key=value
        string keyEq = key + "=";
        foreach (var a in args)
            if (a.StartsWith(keyEq, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(a.Substring(keyEq.Length), out int v)) return v;

        return fallback;
    }

    static string ParseString(string[] args, string key)
    {
        // form 1: key value
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];

        // form 2: --key=value
        string keyEq = key + "=";
        foreach (var a in args)
            if (a.StartsWith(keyEq, StringComparison.OrdinalIgnoreCase))
                return a.Substring(keyEq.Length);

        return null;
    }

    // tiny runner to start coroutines from a static context
    class CoroutineRunner : MonoBehaviour
    {
        static CoroutineRunner _instance;
        public static void Run(IEnumerator routine)
        {
            if (_instance == null)
            {
                var go = new GameObject("TrainingBootstrapRunner");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CoroutineRunner>();
            }
            _instance.StartCoroutine(routine);
        }
    }
}
