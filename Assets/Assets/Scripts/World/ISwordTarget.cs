using UnityEngine;

public interface ISwordTarget
{
    bool CanBeHit { get; }
    Collider2D HitCollider { get; }
    bool TakeDamage(int amount, Vector2 direction);
}
