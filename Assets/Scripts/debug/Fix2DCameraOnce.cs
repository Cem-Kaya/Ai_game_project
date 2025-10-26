using UnityEngine;
public class Fix2DCameraOnce : MonoBehaviour
{
    void Start()
    {
        var cam = GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(1f, 0f, 1f, 0.2f); // visible tint
        cam.cullingMask = ~0;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 1000f;
        cam.useOcclusionCulling = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None; // Target Eye = None
        if (Mathf.Abs(transform.position.z) < 0.5f) transform.position = new Vector3(0, 0, -10);
        Debug.Log($"[Fix2DCameraOnce] Pos {transform.position}, OrthoSize={cam.orthographicSize}");
    }
}
