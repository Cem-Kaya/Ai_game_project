using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class VolumeMix : MonoBehaviour
{
    [Header("UI References")]
    public Slider volumeSlider;
    
    [Header("Audio")]
    public AudioMixerGroup masterMixer; // Optional - for advanced audio control
    
    void Start()
    {
        // Load saved volume or use default
        float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 0.5f);
        volumeSlider.value = savedVolume;
        SetVolume(savedVolume);
        
        // Listen for slider changes
        volumeSlider.onValueChanged.AddListener(SetVolume);
    }
    
    public void SetVolume(float volume)
    {
        // Method 1: Simple AudioListener volume (affects everything)
        AudioListener.volume = volume;
        
        // Save the setting
        PlayerPrefs.SetFloat("MasterVolume", volume);
    }
}
