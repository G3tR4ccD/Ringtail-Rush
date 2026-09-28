using UnityEngine;

// Attach to a coin prefab to make it spin continuously in place. Purely
// visual - doesn't affect collection/collision at all.
public class CoinSpin : MonoBehaviour
{
    [Tooltip("Degrees per second to rotate around the given axis.")]
    public float spinSpeed = 180f;
    [Tooltip("Which local axis to spin around - Y (vertical) is the usual choice for a coin standing upright.")]
    public Vector3 spinAxis = Vector3.up;

    private void Update()
    {
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.Self);
    }
}
