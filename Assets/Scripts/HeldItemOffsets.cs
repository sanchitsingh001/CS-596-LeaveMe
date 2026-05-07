using UnityEngine;

/// <summary>
/// Optional per-item offsets applied while the item is held by PickupAndPlace.
/// Useful for items like the phone so they frame nicely in the camera view.
/// </summary>
public class HeldItemOffsets : MonoBehaviour
{
    [Tooltip("Local offset relative to the holder's holdPosition.")]
    public Vector3 localPositionOffset = Vector3.zero;

    [Tooltip("If true, the item will face the player's camera while held (useful for the phone screen).")]
    public bool faceCameraWhileHeld = false;

    [Tooltip("Which local axis should point toward the camera when faceCameraWhileHeld is enabled.")]
    public Vector3 faceCameraLocalForwardAxis = Vector3.forward;

    [Tooltip("Which local axis should point upward (screen up) when faceCameraWhileHeld is enabled.")]
    public Vector3 faceCameraLocalUpAxis = Vector3.up;

    [Tooltip("Local euler rotation offset relative to the holder's holdPosition rotation.")]
    public Vector3 localEulerOffset = Vector3.zero;
}

