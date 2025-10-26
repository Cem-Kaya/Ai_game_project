// CameraDisableTracer.cs
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class CameraDisableTracer : MonoBehaviour
{
    Camera cam;

    void Awake() { cam = GetComponent<Camera>(); Debug.Log($"[Tracer] Awake on {name}. enabled={cam.enabled}, active={gameObject.activeInHierarchy}"); }
    void OnEnable() { Debug.Log($"[Tracer] OnEnable {name}\n{Stack()}"); }
    void Start() { Debug.Log($"[Tracer] Start {name}. enabled={cam.enabled}, active={gameObject.activeInHierarchy}"); }
    void OnDisable() { Debug.LogWarning($"[Tracer] OnDisable {name}\n{Stack()}"); }

    string Stack()
    {
        // Lightweight stack for where/when the toggle happened
        return System.Environment.StackTrace;
    }
}
