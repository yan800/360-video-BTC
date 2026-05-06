using UnityEngine;
using UnityEngine.InputSystem;

public class VRResetViewOnAlt : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private Transform rigRoot;      // OVRCameraRig
    [SerializeField] private Transform centerEye;    // CenterEyeAnchor
    [SerializeField] private Transform videoSphere;  // Sphère vidéo 360

    [Header("Options de reset")]
    [SerializeField] private bool resetRigYaw = true;
    [SerializeField] private bool resetSphereRotation = true;

    private Quaternion startRigRotation;
    private Quaternion startSphereRotation;

    void Start()
    {
        if (rigRoot == null)
            rigRoot = transform;

        startRigRotation = rigRoot.rotation;

        if (videoSphere != null)
            startSphereRotation = videoSphere.rotation;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.leftAltKey.wasPressedThisFrame ||
            keyboard.rightAltKey.wasPressedThisFrame)
        {
            ResetView();
        }
    }

    public void ResetView()
    {
        if (centerEye == null)
        {
            Debug.LogWarning("CenterEyeAnchor n'est pas assigné.");
            return;
        }

        if (resetRigYaw)
        {
            float currentHeadYaw = centerEye.eulerAngles.y;
            float startYaw = startRigRotation.eulerAngles.y;

            float correctionYaw = Mathf.DeltaAngle(currentHeadYaw, startYaw);

            rigRoot.Rotate(0f, correctionYaw, 0f, Space.World);
        }

        if (videoSphere != null && resetSphereRotation)
        {
            // Important :
            // On garde uniquement la rotation.
            // On ne touche jamais à la position de la sphère.
            videoSphere.rotation = startSphereRotation;
        }

        Debug.Log("Reset visuel effectué sans déplacement de la sphère.");
    }
}