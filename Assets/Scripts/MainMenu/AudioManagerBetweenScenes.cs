using UnityEngine;

public class AudioManagerBetweenScenes : MonoBehaviour
{

    public static AudioManagerBetweenScenes Instance;
    public AudioSource musicSource;

    void Awake()
    {
        // Singleton pattern - only one AudioManager exists
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Load and apply saved volume
            float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 0.5f);
            SetVolume(savedVolume);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SetVolume(float volume)
    {
        AudioListener.volume = volume;
        PlayerPrefs.SetFloat("MasterVolume", volume);
    }
}