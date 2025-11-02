// FollowAgentCam.cs
using UnityEngine;

public class FollowAgentCam : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 0, -10);

    void LateUpdate()
    {
        if (target != null)
            transform.position = target.position + offset;
    }
}
