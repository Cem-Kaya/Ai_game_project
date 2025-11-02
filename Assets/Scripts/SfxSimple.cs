using System;
using System.Collections.Generic;
using UnityEngine;

public class SfxSimple : MonoBehaviour
{
    // --------- Singleton (persistent) ----------
    public static SfxSimple Instance { get; private set; }

    void Awake()
    {
        // If another instance exists, keep the old one (with music) and destroy this.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildMap();
        EnsureAudioSources();

        // Only auto-start default BGM if nothing is already playing
        if (_srcBgm.clip == null && defaultBgm != null)
            PlayBgm(defaultBgm, defaultBgmVolume);
    }

    void OnValidate() => BuildMap();

    // --------- Data ----------
    [Serializable]
    public class Entry
    {
        public string id = "Jump";

        [Header("Clips")]
        public AudioClip humanClip;
        public AudioClip agentClip;

        [Header("Defaults")]
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.1f, 3f)] public float basePitch = 1f;
        [Tooltip("± range in semitones per play")]
        [Range(0f, 12f)] public float randomPitchSemitones = 0f;
        [Range(0f, 1f)] public float spatialBlend = 0f;

        [Header("Per-role pitch offsets (semitones)")]
        [Range(-24f, 24f)] public float humanPitchSemitones = 0f;
        [Range(-24f, 24f)] public float agentPitchSemitones = 0f;
    }

    [Header("Banks")]
    public List<Entry> entries = new();

    [Header("BGM")]
    public AudioClip defaultBgm;
    [Range(0f, 1f)] public float defaultBgmVolume = 0.6f;

    // --------- Internals ----------
    private readonly Dictionary<string, Entry> _map =
        new(StringComparer.OrdinalIgnoreCase);

    private AudioSource _srcHuman;
    private AudioSource _srcAgent;
    private AudioSource _srcBgm;

    private void BuildMap()
    {
        _map.Clear();
        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(e.id))
                _map[e.id] = e;
        }
    }

    private void EnsureAudioSources()
    {
        _srcHuman = GetOrMakeChildSource("SFX_Human");
        _srcAgent = GetOrMakeChildSource("SFX_Agent");
        _srcBgm = GetOrMakeChildSource("BGM");

        _srcHuman.playOnAwake = false;
        _srcAgent.playOnAwake = false;

        _srcBgm.loop = true;
        _srcBgm.playOnAwake = false;
        _srcBgm.spatialBlend = 0f;   // 2D music
    }

    private AudioSource GetOrMakeChildSource(string name)
    {
        var t = transform.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }
        return t.GetComponent<AudioSource>() ?? t.gameObject.AddComponent<AudioSource>();
    }

    // --------- Public API ----------
    public void Play(string id, bool isAgent, Vector3? worldPos = null)
    {
        if (!_map.TryGetValue(id, out var e))
        {
            Debug.LogWarning($"SfxSimple: id '{id}' not found");
            return;
        }

        var clip = isAgent ? e.agentClip : e.humanClip;
        if (clip == null)
        {
            Debug.LogWarning($"SfxSimple: id '{id}' has no {(isAgent ? "Agent" : "Human")} clip");
            return;
        }

        var src = isAgent ? _srcAgent : _srcHuman;

        // spatial
        src.spatialBlend = e.spatialBlend;
        if (e.spatialBlend > 0f && worldPos.HasValue)
            src.transform.position = worldPos.Value;

        // pitch
        float pitch = e.basePitch;
        float roleSemi = isAgent ? e.agentPitchSemitones : e.humanPitchSemitones;
        pitch *= SemitoneToPitch(roleSemi);
        if (e.randomPitchSemitones > 0f)
            pitch *= SemitoneToPitch(UnityEngine.Random.Range(-e.randomPitchSemitones, e.randomPitchSemitones));
        src.pitch = Mathf.Clamp(pitch, 0.1f, 3f);

        // volume and play
        //Debug.Log($"SFX '{id}' ok clip={(isAgent ? e.agentClip : e.humanClip)?.name} vol={e.volume} pitch={src.pitch}");

        src.PlayOneShot(clip, e.volume);
    }

    public void Play(string id, LevelRotationManager.Competitor who, Vector3? worldPos = null)
        => Play(id, who == LevelRotationManager.Competitor.Agent, worldPos);

    // BGM controls (no restart on scene load)
    public void PlayBgm(AudioClip clip = null, float volume = -1f)
    {
        var c = clip != null ? clip : defaultBgm;
        if (c == null) return;

        // If same clip already playing, do not restart
        if (_srcBgm.isPlaying && _srcBgm.clip == c)
        {
            if (volume >= 0f) _srcBgm.volume = Mathf.Clamp01(volume);
            return;
        }

        // Switch track
        _srcBgm.clip = c;
        _srcBgm.volume = volume >= 0f ? Mathf.Clamp01(volume) : defaultBgmVolume;
        _srcBgm.pitch = 1f;
        _srcBgm.loop = true;
        _srcBgm.Play();
    }

    public void StopBgm(bool fadeOut = false, float fadeSeconds = 0.5f)
    {
        if (!_srcBgm.isPlaying) return;
        if (!fadeOut) { _srcBgm.Stop(); return; }
        StartCoroutine(FadeOutBgm(fadeSeconds));
    }

    private System.Collections.IEnumerator FadeOutBgm(float t)
    {
        float start = _srcBgm.volume;
        float time = 0f;
        while (time < t)
        {
            time += Time.unscaledDeltaTime;
            _srcBgm.volume = Mathf.Lerp(start, 0f, time / t);
            yield return null;
        }
        _srcBgm.Stop();
        _srcBgm.volume = start;
    }

    private static float SemitoneToPitch(float s) => Mathf.Pow(2f, s / 12f);
}
