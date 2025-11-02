using System;
using UnityEngine;

/// Attach to the AttackArea child (Collider2D set as Trigger).
/// Works with any controller that exposes:
///   public bool attackDownward;
///   public void Pogo();
/// Also calls OnAttackHitEnemy(Collider2D) if present.
/// Uses safe string equality for target tags (no CompareTag).
[RequireComponent(typeof(Collider2D))]
public class AttackAreaAgent : MonoBehaviour
{
    [Tooltip("Tags that trigger a pogo when hit during a downward attack. Leave empty for none.")]
    public string[] pogoTargetTags = { "Enemy" };

    private Component controller;          // any MonoBehaviour with the needed API
    private System.Reflection.FieldInfo attackDownwardField;
    private System.Reflection.PropertyInfo attackDownwardProp;
    private System.Reflection.MethodInfo pogoMethod;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (!col.isTrigger) col.isTrigger = true;

        // Find a parent component that has 'attackDownward' and 'Pogo()'
        var parents = GetComponentsInParent<MonoBehaviour>(true);
        foreach (var c in parents)
        {
            var t = c.GetType();

            var f = t.GetField("attackDownward", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var p = (f == null)
                ? t.GetProperty("attackDownward", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                : null;
            var m = t.GetMethod("Pogo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null);

            if ((f != null || p != null) && m != null)
            {
                controller = c;
                attackDownwardField = f;
                attackDownwardProp = p;
                pogoMethod = m;
                break;
            }
        }

        if (controller == null)
        {
            Debug.LogError("AttackAreaAgent could not find a parent controller with public 'bool attackDownward' and 'void Pogo()'.");
        }
    }

    private static bool MatchesAnyTag(GameObject go, string[] tags)
    {
        if (tags == null || tags.Length == 0) return false;
        string objTag = go.tag; // safe string compare; no tag definition required for comparison
        for (int i = 0; i < tags.Length; i++)
        {
            var t = tags[i];
            if (!string.IsNullOrEmpty(t) && objTag == t) return true;
        }
        return false;
    }

    private bool IsAttackDownward()
    {
        if (controller == null) return false;
        if (attackDownwardField != null)
            return (bool)attackDownwardField.GetValue(controller);
        if (attackDownwardProp != null && attackDownwardProp.PropertyType == typeof(bool) && attackDownwardProp.CanRead)
            return (bool)attackDownwardProp.GetValue(controller);
        return false;
    }

    private void DoPogo()
    {
        if (controller == null || pogoMethod == null) return;
        pogoMethod.Invoke(controller, null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsAttackDownward()) return;

        if (MatchesAnyTag(other.gameObject, pogoTargetTags))
        {
            DoPogo();
        }

        // Optional: call OnAttackHitEnemy if present
        var meth = controller?.GetType().GetMethod("OnAttackHitEnemy",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            null,
            new Type[] { typeof(Collider2D) },
            null);

        if (meth != null && MatchesAnyTag(other.gameObject, pogoTargetTags))
        {
            meth.Invoke(controller, new object[] { other });
        }
    }
}
