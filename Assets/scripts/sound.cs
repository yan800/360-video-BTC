using UnityEngine;
using UnityEngine.Video;

[RequireComponent(typeof(VideoPlayer))]
[RequireComponent(typeof(AudioSource))]
public class VideoSoundFix : MonoBehaviour
{
    private VideoPlayer vp;
    private AudioSource audioSource;

    void Awake()
    {
        vp = GetComponent<VideoPlayer>();
        audioSource = GetComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;

        vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
        vp.EnableAudioTrack(0, true);
        vp.SetTargetAudioSource(0, audioSource);
    }
}