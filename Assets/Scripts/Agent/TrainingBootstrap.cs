// Assets/Scripts/TrainingBootstrap.cs
using UnityEngine;
using Unity.MLAgents;

public static class TrainingBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        // vSync off so targetFrameRate takes effect
        QualitySettings.vSyncCount = 0;

        // Default: uncapped (platform default). We'll override with --fps if provided.
        Application.targetFrameRate = 80;

        // Keep stepping when window is unfocused
        Application.runInBackground = true;

        // Your game logic needs real-time scale
        Time.timeScale = 1f;

        if (Academy.IsInitialized)
            Academy.Instance.AutomaticSteppingEnabled = true;

        // Optional: force physics to 60 Hz (tweak if needed)
        // Time.fixedDeltaTime = 1f / 60f;

        // Read per-instance command-line flags
        var args = System.Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--fps="))
            {
                if (int.TryParse(arg.Substring(6), out var fps) && fps > 0)
                    Application.targetFrameRate = fps;
            }
            else if (arg.StartsWith("--quality="))
            {
                // Accept either index (0..n-1) or name (e.g., "VeryLow")
                var val = arg.Substring(10);
                if (int.TryParse(val, out var qIndex))
                {
                    qIndex = Mathf.Clamp(qIndex, 0, QualitySettings.names.Length - 1);
                    QualitySettings.SetQualityLevel(qIndex, true);
                }
                else
                {
                    for (int i = 0; i < QualitySettings.names.Length; i++)
                    {
                        if (QualitySettings.names[i].Equals(val, System.StringComparison.OrdinalIgnoreCase))
                        {
                            QualitySettings.SetQualityLevel(i, true);
                            break;
                        }
                    }
                }
            }
        }
    }
}
