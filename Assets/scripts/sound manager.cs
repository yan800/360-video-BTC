using UnityEngine;
using UnityEngine.Video;

public class VRVideoAudioManager : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private AudioSource audioSource;

    [Header("Réglages audio")]
    [SerializeField] private float volume = 1f;
    [SerializeField] private bool spatialAudio = false;

    private void Awake()
    {
        SetupAudio();
    }

    private void Start()
    {
        SetupAudio();
    }

    public void SetupAudio()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (videoPlayer == null)
        {
            Debug.LogError("VRVideoAudioManager : aucun VideoPlayer assigné ou trouvé.");
            return;
        }

        if (audioSource == null)
        {
            Debug.LogError("VRVideoAudioManager : aucun AudioSource assigné ou trouvé.");
            return;
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.mute = false;
        audioSource.volume = volume;

        // Pour une vidéo 360 normale, laisse en 2D.
        // false = son normal dans le casque
        // true = son spatialisé autour d'un objet
        audioSource.spatialBlend = spatialAudio ? 1f : 0f;

        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;

        // Important : piste audio 0 de la vidéo
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, audioSource);

        Debug.Log("VRVideoAudioManager : audio vidéo configuré.");
    }

    public void PrepareAudioBeforePlay()
    {
        SetupAudio();
    }
}