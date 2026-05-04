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

public class VRVideoPlaylistController : MonoBehaviour
{
    [Header("Video")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Startup video file name")]
    [SerializeField] private string startupVideoFileName = "video 360 deg.mov";

    [Header("Root folder inside StreamingAssets")]
    [SerializeField] private string rootVideosFolderName = "Videos";

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeOutToBlackDuration = 2f;
    [SerializeField] private float fadeInFromBlackDuration = 2f;
    [SerializeField] private float fullBlackHold = 0.1f;

    [Header("Menu UI")]
    [SerializeField] private GameObject menuCanvas;

    [Header("PC Operator UI - TextMeshPro")]
    [SerializeField] private TMP_Text pathText;
    [SerializeField] private TMP_Text currentVideoText;
    [SerializeField] private TMP_Text choicesText;

    [Header("Options")]
    [SerializeField] private bool loopVideos = true;
    [SerializeField] private bool logBranching = true;

    [Header("VR Fade")]
    [SerializeField] private OVRScreenFade vrScreenFade;

    private readonly string[] allowedExtensions = { ".mp4", ".mov", ".m4v", ".avi", ".webm" };

    private FolderNode rootNode;
    private FolderNode currentNode;

    private List<string> currentVideoPaths = new List<string>();
    private int currentIndex = 0;

    private bool isTransitioning = false;
    private bool themeSelected = false;

    private string currentVideoDisplayName = "Aucune";

    private string RootVideosFullPath => Path.Combine(Application.streamingAssetsPath, rootVideosFolderName);

    [Serializable]
    private class FolderNode
    {
        public string name;
        public string fullPath;
        public List<string> videos = new List<string>();
        public List<FolderNode> children = new List<FolderNode>();
        public FolderNode parent;
    }

    private void Awake()
    {
        if (fadeCanvasGroup != null)
            fadeCanvasGroup.alpha = 0f;

        if (vrScreenFade != null)
            vrScreenFade.SetExplicitFade(0f);
    }

    private void Start()
    {
        BuildTree();
        PlayStartupVideo();
        RefreshOperatorUI();
    }

    private void Update()
    {
        if (isTransitioning)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            StartCoroutine(ReturnToMenuRoutine());
            return;
        }

        if (!themeSelected)
        {
            HandleNumericSelectionAtRoot(keyboard);
            return;
        }

        if (currentVideoPaths == null || currentVideoPaths.Count == 0)
            return;

        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            int nextIndex = (currentIndex + 1) % currentVideoPaths.Count;
            StartCoroutine(SwitchToVideoRoutine(nextIndex));
            return;
        }

        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            int prevIndex = (currentIndex - 1 + currentVideoPaths.Count) % currentVideoPaths.Count;
            StartCoroutine(SwitchToVideoRoutine(prevIndex));
            return;
        }

        HandleNumericSelectionInCurrentNode(keyboard);
    }

    private void BuildTree()
    {
        string rootPath = RootVideosFullPath;

        if (!Directory.Exists(rootPath))
        {
            Debug.LogError($"Dossier introuvable : {rootPath}");
            rootNode = null;
            RefreshOperatorUI();
            return;
        }

        rootNode = BuildFolderNodeRecursive(rootPath, null);

        if (logBranching)
            LogNode(rootNode, 0);
    }

    private FolderNode BuildFolderNodeRecursive(string path, FolderNode parent)
    {
        FolderNode node = new FolderNode
        {
            name = Path.GetFileName(path),
            fullPath = path,
            parent = parent
        };

        string[] videoFiles = Directory
            .GetFiles(path)
            .Where(IsSupportedVideoFile)
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        node.videos.AddRange(videoFiles);

        string[] subDirs = Directory
            .GetDirectories(path)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string dir in subDirs)
        {
            node.children.Add(BuildFolderNodeRecursive(dir, node));
        }

        return node;
    }

    private bool IsSupportedVideoFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return allowedExtensions.Contains(ext);
    }

    private void HandleNumericSelectionAtRoot(Keyboard keyboard)
    {
        if (rootNode == null)
            return;

        int selectedIndex = GetPressedNumericIndex(keyboard);
        if (selectedIndex < 0)
            return;

        if (selectedIndex >= rootNode.children.Count)
        {
            Debug.LogWarning($"Aucun thème assigné à la touche {selectedIndex + 1}");
            return;
        }

        EnterNode(rootNode.children[selectedIndex]);
    }

    private void HandleNumericSelectionInCurrentNode(Keyboard keyboard)
    {
        if (currentNode == null)
            return;

        int selectedIndex = GetPressedNumericIndex(keyboard);
        if (selectedIndex < 0)
            return;

        if (selectedIndex >= currentNode.children.Count)
        {
            Debug.LogWarning($"Aucun sous-dossier assigné à la touche {selectedIndex + 1} dans {currentNode.name}");
            return;
        }

        EnterNode(currentNode.children[selectedIndex]);
    }

    private int GetPressedNumericIndex(Keyboard keyboard)
    {
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 0;
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 1;
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 2;
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) return 3;
        if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) return 4;
        if (keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame) return 5;
        if (keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame) return 6;
        if (keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame) return 7;
        if (keyboard.digit9Key.wasPressedThisFrame || keyboard.numpad9Key.wasPressedThisFrame) return 8;

        return -1;
    }

    private void EnterNode(FolderNode node)
    {
        if (node == null)
            return;

        if (node.videos == null || node.videos.Count == 0)
        {
            Debug.LogWarning($"Le dossier {node.name} ne contient aucune vidéo lisible.");
            return;
        }

        currentNode = node;
        currentVideoPaths = new List<string>(node.videos);
        currentIndex = 0;
        themeSelected = true;

        if (menuCanvas != null)
            menuCanvas.SetActive(false);

        if (logBranching)
        {
            Debug.Log($"Entrée dans le dossier : {node.name}");
            for (int i = 0; i < node.children.Count && i < 9; i++)
            {
                Debug.Log($"Touche {i + 1} -> {node.children[i].name}");
            }
        }

        RefreshOperatorUI();
        StartCoroutine(SwitchToVideoRoutine(currentIndex));
    }

    private void PlayStartupVideo()
    {
        if (rootNode == null)
            return;

        string startupPath = Path.Combine(RootVideosFullPath, startupVideoFileName);

        if (!File.Exists(startupPath))
        {
            Debug.LogError($"Vidéo de départ introuvable : {startupPath}");
            currentVideoDisplayName = "Introuvable";
            RefreshOperatorUI();
            return;
        }

        videoPlayer.Stop();
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = startupPath;
        videoPlayer.isLooping = true;
        videoPlayer.Play();

        currentNode = null;
        currentVideoPaths = new List<string>();
        currentIndex = 0;
        themeSelected = false;
        currentVideoDisplayName = Path.GetFileName(startupPath);

        if (logBranching)
        {
            Debug.Log($"Vidéo de départ lancée : {startupPath}");
            for (int i = 0; i < rootNode.children.Count && i < 9; i++)
            {
                Debug.Log($"Touche {i + 1} -> {rootNode.children[i].name}");
            }
        }

        RefreshOperatorUI();
    }

    private IEnumerator SwitchToVideoRoutine(int newIndex)
    {
        if (currentVideoPaths == null || currentVideoPaths.Count == 0)
            yield break;

        if (newIndex < 0 || newIndex >= currentVideoPaths.Count)
            yield break;

        string nextPath = currentVideoPaths[newIndex];

        if (!File.Exists(nextPath))
        {
            Debug.LogError($"Vidéo introuvable : {nextPath}");
            yield break;
        }

        isTransitioning = true;
        RefreshOperatorUI();

        yield return StartCoroutine(FadeAll(0f, 1f, fadeOutToBlackDuration));

        if (fullBlackHold > 0f)
            yield return new WaitForSeconds(fullBlackHold);

        videoPlayer.Stop();
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = nextPath;
        videoPlayer.isLooping = loopVideos;
        videoPlayer.Prepare();

        while (!videoPlayer.isPrepared)
            yield return null;

        videoPlayer.Play();
        currentIndex = newIndex;
        currentVideoDisplayName = Path.GetFileName(nextPath);

        if (logBranching)
            Debug.Log($"Lecture : {Path.GetFileName(nextPath)}");

        RefreshOperatorUI();

        yield return StartCoroutine(FadeAll(1f, 0f, fadeInFromBlackDuration));

        isTransitioning = false;
        RefreshOperatorUI();
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

        currentNode = null;
        currentVideoPaths = new List<string>();
        currentIndex = 0;
        themeSelected = false;
        currentVideoDisplayName = "Retour menu";

        if (menuCanvas != null)
            menuCanvas.SetActive(true);

        PlayStartupVideo();

        yield return StartCoroutine(FadeAll(1f, 0f, fadeInFromBlackDuration));

        isTransitioning = false;
        RefreshOperatorUI();
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
        if (rootNode == null)
            return "Etat actuelle : racine introuvable";

        if (currentNode == null)
            return "Etat actuelle : Menu principal";

        Stack<string> segments = new Stack<string>();
        FolderNode node = currentNode;

        while (node != null && node != rootNode)
        {
            segments.Push(node.name);
            node = node.parent;
        }

        return "Etat actuelle : " + string.Join(" / ", segments);
    }

    private string BuildCurrentVideoText()
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("Vidéo en cours : " + currentVideoDisplayName);

        if (themeSelected && currentVideoPaths != null && currentVideoPaths.Count > 0)
        {
            sb.AppendLine($"Index : {currentIndex + 1}/{currentVideoPaths.Count}");
        }

        if (isTransitioning)
            sb.AppendLine("État : transition...");
        else
            sb.AppendLine("État : lecture");

        return sb.ToString().TrimEnd();
    }

    private string BuildChoicesText()
    {
        StringBuilder sb = new StringBuilder();

        if (rootNode == null)
        {
            sb.Append("Aucun dossier racine trouvé.");
            return sb.ToString();
        }

        if (!themeSelected)
        {
            sb.AppendLine("Choix disponibles :");
            sb.AppendLine();

            int max = Mathf.Min(rootNode.children.Count, 9);
            if (max == 0)
            {
                sb.AppendLine("Aucun thème disponible.");
            }
            else
            {
                for (int i = 0; i < max; i++)
                {
                    sb.AppendLine($"{i + 1} -> {rootNode.children[i].name}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Espace -> retour menu");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("Sous-dossiers disponibles :");
        sb.AppendLine();

        int childCount = currentNode != null ? Mathf.Min(currentNode.children.Count, 9) : 0;

        if (childCount == 0)
        {
            sb.AppendLine("Aucun sous-dossier disponible.");
        }
        else
        {
            for (int i = 0; i < childCount; i++)
            {
                sb.AppendLine($"{i + 1} -> {currentNode.children[i].name}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Navigation :");
        sb.AppendLine("← -> vidéo précédente");
        sb.AppendLine("→ -> vidéo suivante");
        sb.AppendLine("Espace -> retour menu");

        return sb.ToString().TrimEnd();
    }

    private void LogNode(FolderNode node, int depth)
    {
        if (node == null)
            return;

        string indent = new string(' ', depth * 2);
        Debug.Log($"{indent}[Folder] {node.name} | videos={node.videos.Count} | children={node.children.Count}");

        foreach (string video in node.videos)
            Debug.Log($"{indent}  - {Path.GetFileName(video)}");

        foreach (FolderNode child in node.children)
            LogNode(child, depth + 1);
    }
}