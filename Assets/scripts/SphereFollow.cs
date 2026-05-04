using UnityEngine;

public class SphereFollowVRPositionOnly : MonoBehaviour
{
    [Header("Camera VR")]
    [SerializeField] private Transform vrCamera;

    [Header("Follow")]
    [SerializeField] private float smoothSpeed = 3f;
    [SerializeField] private float deadZone = 0.15f;

    [Header("Axis")]
    [SerializeField] private bool followHeight = false;

    private Vector3 targetPos;

    void Start()
    {
        targetPos = transform.position;
    }

    void LateUpdate()
    {
        if (vrCamera == null)
            return;

        Vector3 desiredPos = vrCamera.position;

        // conserve hauteur sphère
        if (!followHeight)
            desiredPos.y = transform.position.y;

        float dist = Vector3.Distance(transform.position, desiredPos);

        if (dist > deadZone)
            targetPos = desiredPos;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPos,
            smoothSpeed * Time.deltaTime
        );

        // IMPORTANT :
        // aucune rotation modifiée
    }
}