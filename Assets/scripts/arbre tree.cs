using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

public class VRVideoJsonPlaylistController : MonoBehaviour
{
    [Header("Video")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Audio vidéo")]
    [SerializeField] private AudioSource videoAudioSource;
    [SerializeField] private float videoVolume = 1f;
    [SerializeField] private bool useSpatialAudio = false;

    [Header("Root folder inside StreamingAssets")]
    [SerializeField] private string rootVideosFolderName = "Videos";

    [Header("JSON file inside Videos folder")]
    [SerializeField] private string jsonFileName = "videoTree.json";

    [Header("Dossier local externe")]
    [SerializeField] private bool useExternalLocalFolder = true;

    // Si le chemin est relatif, il sera cherché à côté du dossier du projet Unity.
    // Exemple :
    // D:/Unity/MonProjetUnity
    // D:/Unity/VideosExternes
    [SerializeField] private string externalLocalFolderPath = "VideosExternes";

    // Si true : cherche d'abord dans le dossier externe, puis dans StreamingAssets.
    // Si false : cherche d'abord dans StreamingAssets, puis dans le dossier externe.
    [SerializeField] private bool preferExternalFolder = true;

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutToBlackDuration = 2f;
    [SerializeField] private float fadeInFromBlackDuration = 2f;
    [SerializeField] private float fullBlackHold = 0.1f;

    [Header("Menu UI")]
    [SerializeField] private GameObject menuCanvas;

    [Header("UI casque - phrase vidéo")]
    [SerializeField] private Transform centerEyeAnchor;
    [SerializeField] private GameObject helmetPhraseUIRoot;
    [SerializeField] private TMP_Text helmetPhraseText;
    [SerializeField] private bool showHelmetPhrase = true;
    [SerializeField] private Key toggleHelmetPhraseKey = Key.C;
    [SerializeField] private string defaultHelmetPhrase = "";
    [SerializeField] private Vector3 helmetPhraseLocalPosition = new Vector3(0f, -0.25f, 2f);
    [SerializeField] private Vector3 helmetPhraseLocalEuler = Vector3.zero;

    [Header("PC Operator UI - TextMeshPro")]
    [SerializeField] private TMP_Text pathText;
    [SerializeField] private TMP_Text currentVideoText;
    [SerializeField] private TMP_Text choicesText;

    [Header("Options")]
    [SerializeField] private bool logBranching = true;

    [Header("VR Fade")]
    [SerializeField] private OVRScreenFade vrScreenFade;

    private VideoTreeData treeData;
    private Dictionary<string, VideoNodeData> nodeById = new Dictionary<string, VideoNodeData>();
    private Dictionary<int, string> idleVideoByKey = new Dictionary<int, string>();

    private VideoNodeData currentNode;
    private bool isTransitioning = false;
    private bool themeSelected = false;

    private bool isStartupVideoPlaying = false;
    private bool isIdleVideoPlaying = false;
    private int lastVideoLaunchKey = -1;

    private string currentVideoDisplayName = "Aucune";

    private string StreamingRootVideosFullPath => Path.Combine(Application.streamingAssetsPath, rootVideosFolderName);

    private string ExternalRootVideosFullPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(externalLocalFolderPath))
                return "";

            if (Path.IsPathRooted(externalLocalFolderPath))
                return externalLocalFolderPath;

            string projectFolder = Directory.GetParent(Application.dataPath).FullName;
            string projectParentFolder = Directory.GetParent(projectFolder).FullName;

            return Path.Combine(projectParentFolder, externalLocalFolderPath);
        }
    }

    [Serializable]
    private class VideoTreeData
    {
        public string startupVideo;
        public string startNode;
        public List<IdleVideoData> idleVideos;
        public List<VideoNodeData> nodes;
    }

    [Serializable]
    private class IdleVideoData
    {
        public int key;
        public string video;
    }

    [Serializable]
    private class VideoNodeData
    {
        public string id;
        public string video;
        public string phrase;
        public List<VideoChoiceData> choices;
    }

    [Serializable]
    private class VideoChoiceData
    {
        public int key;
        public string label;
        public string target;
    }

    private void Awake()
    {
        SetupVideoAudio();
        SetupHelmetPhraseUI();

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.loopPointReached += OnVideoFinished;
        }

        if (fadeCanvasGroup != null)
            fadeCanvasGroup.alpha = 0f;

        if (vrScreenFade != null)
            vrScreenFade.SetExplicitFade(0f);
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= OnVideoFinished;
    }

    private void Start()
    {
        LoadJsonTree();
        PlayStartupVideo();
        RefreshOperatorUI();
        RefreshHelmetPhrase();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard[toggleHelmetPhraseKey].wasPressedThisFrame)
        {
            ToggleHelmetPhraseUI();
            return;
        }

        if (isTransitioning)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            ReturnToMenu();
            //StartCoroutine(ReturnToMenuRoutine());
            return;
        }

        int selectedKey = GetPressedNumericKey(keyboard);

        if (selectedKey > 0)
        {
            HandleChoice(selectedKey);
        }
    }

    private string GetExistingJsonPath()
    {
        List<string> possiblePaths = new List<string>();

        string streamingJsonPath = Path.Combine(StreamingRootVideosFullPath, jsonFileName);

        if (useExternalLocalFolder && !string.IsNullOrWhiteSpace(ExternalRootVideosFullPath))
        {
            string externalJsonPath = Path.Combine(ExternalRootVideosFullPath, jsonFileName);

            if (preferExternalFolder)
            {
                possiblePaths.Add(externalJsonPath);
                possiblePaths.Add(streamingJsonPath);
            }
            else
            {
                possiblePaths.Add(streamingJsonPath);
                possiblePaths.Add(externalJsonPath);
            }
        }
        else
        {
            possiblePaths.Add(streamingJsonPath);
        }

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "";
    }

    private string GetExistingVideoPath(string videoFileName)
    {
        if (string.IsNullOrWhiteSpace(videoFileName))
            return "";

        List<string> possiblePaths = new List<string>();

        string streamingVideoPath = Path.Combine(StreamingRootVideosFullPath, videoFileName);

        if (useExternalLocalFolder && !string.IsNullOrWhiteSpace(ExternalRootVideosFullPath))
        {
            string externalVideoPath = Path.Combine(ExternalRootVideosFullPath, videoFileName);

            if (preferExternalFolder)
            {
                possiblePaths.Add(externalVideoPath);
                possiblePaths.Add(streamingVideoPath);
            }
            else
            {
                possiblePaths.Add(streamingVideoPath);
                possiblePaths.Add(externalVideoPath);
            }
        }
        else
        {
            possiblePaths.Add(streamingVideoPath);
        }

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "";
    }

    private void SetupVideoAudio()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        if (videoAudioSource == null && videoPlayer != null)
        {
            videoAudioSource = videoPlayer.GetComponent<AudioSource>();
        }

        if (videoAudioSource == null)
        {
            videoAudioSource = GetComponent<AudioSource>();
        }

        if (videoPlayer == null)
        {
            Debug.LogError("VideoPlayer non assigné.");
            return;
        }

        if (videoAudioSource == null)
        {
            Debug.LogError("AudioSource non assigné. Ajoute un AudioSource sur le même objet que le VideoPlayer.");
            return;
        }

        videoAudioSource.playOnAwake = false;
        videoAudioSource.loop = false;
        videoAudioSource.mute = false;
        videoAudioSource.volume = videoVolume;

        videoAudioSource.spatialBlend = useSpatialAudio ? 1f : 0f;

        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;

        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, videoAudioSource);
    }

    private void SetupHelmetPhraseUI()
    {
        if (helmetPhraseUIRoot == null)
        {
            Debug.LogWarning("Helmet Phrase UI Root non assigné.");
            return;
        }

        if (centerEyeAnchor != null)
        {
            helmetPhraseUIRoot.transform.SetParent(centerEyeAnchor, false);
            helmetPhraseUIRoot.transform.localPosition = helmetPhraseLocalPosition;
            helmetPhraseUIRoot.transform.localRotation = Quaternion.Euler(helmetPhraseLocalEuler);
        }
        else
        {
            Debug.LogWarning("CenterEyeAnchor non assigné. La phrase casque ne suivra pas la caméra.");
        }

        helmetPhraseUIRoot.SetActive(showHelmetPhrase);
    }

    private void RefreshHelmetPhrase()
    {
        if (helmetPhraseUIRoot != null)
            helmetPhraseUIRoot.SetActive(showHelmetPhrase);

        if (!showHelmetPhrase)
            return;

        if (helmetPhraseText == null)
            return;

        if (currentNode == null)
        {
            helmetPhraseText.text = defaultHelmetPhrase;
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentNode.phrase))
            helmetPhraseText.text = currentNode.phrase;
        else
            helmetPhraseText.text = defaultHelmetPhrase;
    }

    public void ToggleHelmetPhraseUI()
    {
        showHelmetPhrase = !showHelmetPhrase;

        if (helmetPhraseUIRoot != null)
            helmetPhraseUIRoot.SetActive(showHelmetPhrase);

        if (showHelmetPhrase)
            RefreshHelmetPhrase();

        Debug.Log("Phrase casque : " + (showHelmetPhrase ? "affichée" : "cachée"));
    }

    private void LoadJsonTree()
    {
        string jsonPath = GetExistingJsonPath();

        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            Debug.LogError(
                "Fichier JSON introuvable.\n" +
                "Cherché dans :\n" +
                "- StreamingAssets : " + Path.Combine(StreamingRootVideosFullPath, jsonFileName) + "\n" +
                "- Dossier externe : " + Path.Combine(ExternalRootVideosFullPath, jsonFileName)
            );
            return;
        }

        Debug.Log("JSON utilisé : " + jsonPath);

        string json = File.ReadAllText(jsonPath);
        treeData = JsonUtility.FromJson<VideoTreeData>(json);

        if (treeData == null || treeData.nodes == null || treeData.nodes.Count == 0)
        {
            Debug.LogError("Le fichier JSON est vide ou mal structuré.");
            return;
        }

        nodeById.Clear();
        idleVideoByKey.Clear();

        if (treeData.idleVideos != null)
        {
            foreach (IdleVideoData idle in treeData.idleVideos)
            {
                if (idle == null)
                    continue;

                if (idle.key <= 0)
                {
                    Debug.LogWarning("Une vidéo idle a une key invalide.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(idle.video))
                {
                    Debug.LogWarning($"La vidéo idle de la touche {idle.key} n’a pas de fichier vidéo.");
                    continue;
                }

                if (!idleVideoByKey.ContainsKey(idle.key))
                    idleVideoByKey.Add(idle.key, idle.video);
                else
                    Debug.LogWarning($"Vidéo idle dupliquée pour la touche {idle.key}");
            }
        }

        foreach (VideoNodeData node in treeData.nodes)
        {
            if (string.IsNullOrWhiteSpace(node.id))
            {
                Debug.LogWarning("Un node dans le JSON n’a pas d’id.");
                continue;
            }

            if (node.choices == null)
                node.choices = new List<VideoChoiceData>();

            if (!nodeById.ContainsKey(node.id))
                nodeById.Add(node.id, node);
            else
                Debug.LogWarning($"Id dupliqué dans le JSON : {node.id}");
        }

        if (logBranching)
        {
            Debug.Log("JSON chargé avec succès.");

            foreach (KeyValuePair<int, string> idle in idleVideoByKey)
            {
                Debug.Log($"Idle vidéo : touche {idle.Key} -> {idle.Value}");
            }

            foreach (VideoNodeData node in treeData.nodes)
            {
                Debug.Log($"Node : {node.id} | video={node.video} | phrase={node.phrase} | choices={node.choices.Count}");

                foreach (VideoChoiceData choice in node.choices)
                {
                    Debug.Log($"   Touche {choice.key} -> {choice.label} -> {choice.target}");
                }
            }
        }
    }

    private void PlayStartupVideo()
    {
        if (treeData == null)
            return;

        string startupVideo = treeData.startupVideo;

        if (string.IsNullOrWhiteSpace(startupVideo))
        {
            Debug.LogError("startupVideo n’est pas défini dans le JSON.");
            return;
        }

        string startupPath = GetExistingVideoPath(startupVideo);

        if (string.IsNullOrWhiteSpace(startupPath))
        {
            Debug.LogError($"Vidéo de départ introuvable : {startupVideo}");
            currentVideoDisplayName = "Introuvable";
            RefreshOperatorUI();
            RefreshHelmetPhrase();
            return;
        }

        videoPlayer.Stop();

        SetupVideoAudio();

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = startupPath;

        // La vidéo de départ est en boucle.
        videoPlayer.isLooping = true;

        isStartupVideoPlaying = true;
        isIdleVideoPlaying = false;
        lastVideoLaunchKey = -1;

        SetupVideoAudio();

        videoPlayer.Play();

        currentNode = GetStartNode();
        themeSelected = false;
        currentVideoDisplayName = Path.GetFileName(startupPath);

        if (menuCanvas != null)
            menuCanvas.SetActive(true);

        RefreshOperatorUI();
        RefreshHelmetPhrase();
    }

    private VideoNodeData GetStartNode()
    {
        if (treeData == null)
            return null;

        if (string.IsNullOrWhiteSpace(treeData.startNode))
            return null;

        if (!nodeById.ContainsKey(treeData.startNode))
        {
            Debug.LogError($"startNode introuvable dans le JSON : {treeData.startNode}");
            return null;
        }

        return nodeById[treeData.startNode];
    }

    public void HandleChoice(int key)
    {
        if (currentNode == null)
            return;

        if (currentNode.choices == null || currentNode.choices.Count == 0)
        {
            Debug.LogWarning($"La vidéo actuelle n’a aucun choix : {currentNode.id}");
            return;
        }

        VideoChoiceData choice = currentNode.choices.FirstOrDefault(c => c.key == key);

        if (choice == null)
        {
            Debug.LogWarning($"Aucun choix assigné à la touche {key} dans le node {currentNode.id}");
            return;
        }

        if (string.IsNullOrWhiteSpace(choice.target))
        {
            Debug.LogWarning($"Le choix {key} n’a pas de target.");
            return;
        }

        if (!nodeById.ContainsKey(choice.target))
        {
            Debug.LogError($"Target introuvable dans le JSON : {choice.target}");
            return;
        }

        VideoNodeData nextNode = nodeById[choice.target];

        if (menuCanvas != null)
            menuCanvas.SetActive(false);

        themeSelected = true;

        StartCoroutine(SwitchToNodeRoutine(nextNode, key));
    }

    private IEnumerator SwitchToNodeRoutine(VideoNodeData nextNode, int launchKey)
    {
        if (nextNode == null)
            yield break;

        if (string.IsNullOrWhiteSpace(nextNode.video))
        {
            Debug.LogError($"Le node {nextNode.id} n’a pas de vidéo.");
            yield break;
        }

        string nextPath = GetExistingVideoPath(nextNode.video);

        if (string.IsNullOrWhiteSpace(nextPath))
        {
            Debug.LogError($"Vidéo introuvable : {nextNode.video}");
            yield break;
        }

        isTransitioning = true;
        RefreshOperatorUI();

        yield return StartCoroutine(FadeAll(0f, 1f, fadeOutToBlackDuration));

        if (fullBlackHold > 0f)
            yield return new WaitForSeconds(fullBlackHold);

        videoPlayer.Stop();

        SetupVideoAudio();

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = nextPath;

        // Les vidéos normales ne bouclent pas.
        videoPlayer.isLooping = false;

        isStartupVideoPlaying = false;
        isIdleVideoPlaying = false;
        lastVideoLaunchKey = launchKey;

        SetupVideoAudio();

        videoPlayer.Prepare();

        while (!videoPlayer.isPrepared)
            yield return null;

        SetupVideoAudio();

        videoPlayer.Play();

        currentNode = nextNode;
        currentVideoDisplayName = Path.GetFileName(nextPath);

        RefreshHelmetPhrase();

        if (logBranching)
            Debug.Log($"Lecture node : {currentNode.id} | vidéo : {currentVideoDisplayName} | lancée par touche {launchKey}");

        RefreshOperatorUI();

        yield return StartCoroutine(FadeAll(1f, 0f, fadeInFromBlackDuration));

        isTransitioning = false;
        RefreshOperatorUI();
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        if (isTransitioning)
            return;

        if (isStartupVideoPlaying)
            return;

        if (isIdleVideoPlaying)
            return;

        if (lastVideoLaunchKey <= 0)
        {
            Debug.LogWarning("Vidéo terminée, mais aucune touche de lancement n’est connue.");
            return;
        }

        StartCoroutine(PlayIdleVideoRoutine(lastVideoLaunchKey));
    }

    private IEnumerator PlayIdleVideoRoutine(int key)
    {
        if (!idleVideoByKey.ContainsKey(key))
        {
            Debug.LogWarning($"Aucune vidéo idle définie pour la touche {key}");
            yield break;
        }

        string idleVideoName = idleVideoByKey[key];
        string idlePath = GetExistingVideoPath(idleVideoName);

        if (string.IsNullOrWhiteSpace(idlePath))
        {
            Debug.LogError($"Vidéo idle introuvable : {idleVideoName}");
            yield break;
        }

        isTransitioning = true;
        RefreshOperatorUI();

        videoPlayer.Stop();

        SetupVideoAudio();

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = idlePath;

        // Les vidéos idle sont en boucle.
        videoPlayer.isLooping = true;

        isStartupVideoPlaying = false;
        isIdleVideoPlaying = true;

        SetupVideoAudio();

        videoPlayer.Prepare();

        while (!videoPlayer.isPrepared)
            yield return null;

        SetupVideoAudio();

        videoPlayer.Play();

        currentVideoDisplayName = Path.GetFileName(idlePath);

        if (logBranching)
            Debug.Log($"Lecture idle : touche {key} | vidéo : {currentVideoDisplayName}");

        RefreshOperatorUI();
        RefreshHelmetPhrase();

        isTransitioning = false;
    }

    public void ReturnToMenu()
    {
        StartCoroutine(ReturnToMenuRoutine());
    }   

    private IEnumerator ReturnToMenuRoutine()
    {
        if (isTransitioning)
            yield break;

        isTransitioning = true;
        RefreshOperatorUI();

        yield return StartCoroutine(FadeAll(0f, 1f, fadeOutToBlackDuration));

        if (fullBlackHold > 0f)
            yield return new WaitForSeconds(fullBlackHold);

        videoPlayer.Stop();

        currentNode = GetStartNode();
        themeSelected = false;
        currentVideoDisplayName = "Retour menu";

        isStartupVideoPlaying = false;
        isIdleVideoPlaying = false;
        lastVideoLaunchKey = -1;

        if (menuCanvas != null)
            menuCanvas.SetActive(true);

        RefreshHelmetPhrase();

        PlayStartupVideo();

        yield return StartCoroutine(FadeAll(1f, 0f, fadeInFromBlackDuration));

        isTransitioning = false;
        RefreshOperatorUI();
        RefreshHelmetPhrase();
    }

    private IEnumerator FadeAll(float startAlpha, float endAlpha, float duration)
    {
        float elapsed = 0f;

        if (fadeCanvasGroup != null)
            fadeCanvasGroup.alpha = startAlpha;

        if (vrScreenFade != null)
            vrScreenFade.SetExplicitFade(startAlpha);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = Mathf.Lerp(startAlpha, endAlpha, t);

            if (fadeCanvasGroup != null)
                fadeCanvasGroup.alpha = alpha;

            if (vrScreenFade != null)
                vrScreenFade.SetExplicitFade(alpha);

            yield return null;
        }

        if (fadeCanvasGroup != null)
            fadeCanvasGroup.alpha = endAlpha;

        if (vrScreenFade != null)
            vrScreenFade.SetExplicitFade(endAlpha);
    }

    private int GetPressedNumericKey(Keyboard keyboard)
    {
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 1;
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 2;
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 3;
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) return 4;
        if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) return 5;
        if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) return 6;
        if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) return 7;
        if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame) return 8;
        if (keyboard.digit9Key.wasPressedThisFrame || keyboard.numpad9Key.wasPressedThisFrame) return 9;

        return -1;
    }

    private void RefreshOperatorUI()
    {
        if (pathText != null)
            pathText.text = BuildPathText();

        if (currentVideoText != null)
            currentVideoText.text = BuildCurrentVideoText();

        if (choicesText != null)
            choicesText.text = BuildChoicesText();
    }

    private string BuildPathText()
    {
        if (treeData == null)
            return "État actuel : JSON introuvable";

        if (currentNode == null)
            return "État actuel : Aucun node";

        if (isIdleVideoPlaying)
            return "État actuel : Idle touche " + lastVideoLaunchKey;

        if (!themeSelected)
            return "État actuel : Menu principal";

        return "État actuel : " + currentNode.id;
    }

    private string BuildCurrentVideoText()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Vidéo en cours : " + currentVideoDisplayName);

        if (isTransitioning)
            sb.AppendLine("État : transition...");
        else if (isIdleVideoPlaying)
            sb.AppendLine("État : idle");
        else
            sb.AppendLine("État : lecture");

        return sb.ToString().TrimEnd();
    }

    private string BuildChoicesText()
    {
        StringBuilder sb = new StringBuilder();

        if (treeData == null)
        {
            sb.AppendLine("JSON non chargé.");
            return sb.ToString();
        }

        if (currentNode == null)
        {
            sb.AppendLine("Aucun node actif.");
            return sb.ToString();
        }

        if (!themeSelected)
            sb.AppendLine("Choix disponibles depuis le menu :");
        else
            sb.AppendLine("Choix disponibles :");

        sb.AppendLine();

        if (currentNode.choices == null || currentNode.choices.Count == 0)
        {
            sb.AppendLine("Aucun choix disponible.");
        }
        else
        {
            foreach (VideoChoiceData choice in currentNode.choices.OrderBy(c => c.key))
            {
                sb.AppendLine($"{choice.key} -> {choice.label}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Espace -> retour menu");
        sb.AppendLine("C -> afficher/cacher phrase casque");

        return sb.ToString().TrimEnd();
    }
}