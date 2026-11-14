using UnityEngine;

/// <summary>A subtle wind motion on the crown only; trunk and collision stay still.</summary>
[DisallowMultipleComponent]
public sealed class CrownSway : MonoBehaviour
{
    [SerializeField, Min(0f)] private float angle = 1.2f;
    [SerializeField, Min(0f)] private float speed = 0.8f;
    private Quaternion originalRotation;
    private float phase;

    private void Awake()
    {
        originalRotation = transform.localRotation;
        phase = transform.position.x * 0.71f + transform.position.y * 1.13f;
    }

    private void LateUpdate()
    {
        transform.localRotation = originalRotation * Quaternion.Euler(0f, 0f,
            Mathf.Sin(Time.time * speed + phase) * angle);
    }

    private void OnDisable()
    {
        transform.localRotation = originalRotation;
    }
}
