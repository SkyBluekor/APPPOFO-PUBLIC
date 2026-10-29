using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;
using OpenCvSharp;
using AppPofo.Domain.Entities;
using AppPofo.Domain.ValueObjects;
using AppPofo.Infrastructure.SQLite;
using AppPofo.Infrastructure.SQLite.Repositories;
using AppPofo.Presentation.Common;
using AppPofo.Presentation.Screens;
using AppPofo.Presentation.Avatar;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRegistration.Alignment;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.FaceRegistration.Embedding;
using AppPofo.Presentation.FaceRecognition.Matching;

// Opt-in Editor tooling only. Never attached to saved scenes or included in players.
[InitializeOnLoad]
public static class PublicCloneRunValidation
{
    const string Active = "AppPofo.CloneSmoke.Active";
    const string Output = "AppPofo.CloneSmoke.Output";
    const string OriginalProduct = "AppPofo.CloneSmoke.OriginalProduct";
    static PublicCloneRunValidation() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!Application.isBatchMode || PlayerSettings.companyName != "PortfolioDemo") throw new InvalidOperationException("Public candidate batch validation only.");
        string output = Environment.GetEnvironmentVariable("APPPOFO_PUBLIC_CLONE_SMOKE_OUTPUT");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Explicit external evidence directory required.");
        if (!Directory.Exists(output)) throw new DirectoryNotFoundException(output);
        // Isolate real SQLite/avatars from every normal user's persistentDataPath.
        SessionState.SetString(OriginalProduct, PlayerSettings.productName);
        PlayerSettings.productName = "AppPofo-CloneSmoke-" + Guid.NewGuid().ToString("N");
        SessionState.SetString(Output, output); SessionState.SetBool(Active, true);
        EditorSceneManager.OpenScene("Assets/AppPofo/Presentation/Scenes/Bootstrap.unity");
        try { SetGameSize(); Application.runInBackground = true; EditorApplication.EnterPlaymode(); } catch { Finish(); throw; }
    }
    static void SetGameSize()
    {
        var assembly = typeof(EditorWindow).Assembly;
        var gameView = EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 }); // Standalone
        var groupType = group.GetType();
        var kind = assembly.GetType("UnityEditor.GameViewSizeType");
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var size = Activator.CreateInstance(sizeType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object[] { Enum.ToObject(kind, 1), 540, 960, "AppPoFo Public Demo" }, null);
        groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
        int index = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
        gameView.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(gameView, index);
        gameView.Repaint();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Active, false)) return;
        new GameObject("Public clone verification (Editor only)").AddComponent<PublicCloneSmokeRunner>().Output = SessionState.GetString(Output, "");
    }
    internal static void Finish() { SessionState.SetBool(Active, false); PlayerSettings.productName = SessionState.GetString(OriginalProduct, PlayerSettings.productName); }
}

public sealed class PublicCloneSmokeRunner : MonoBehaviour
{
    public string Output;
    [Serializable] class Evidence
    {
        public string status = "FAIL";
        public string scope = "Fresh source-only copy, official installers, real Bootstrap Play Mode, isolated real SQLite, synthetic fixture only; no physical camera/production server.";
        public string unityVersion;
        public string dataDirectory;
        public int width, height, importedGuidanceClips;
        public bool bootstrap, localProfileCreatedViaUI, sqliteReopened, faceStored, recognized, businessCardBound, guidancePlayback, offline;
        public float matchCosine;
        public List<string> screenshots = new List<string>();
        public List<string> errors = new List<string>();
    }
    Evidence evidence = new Evidence();
    void Start() { Application.runInBackground = true; Debug.Log("[PublicCloneSmoke] Runtime coroutine started."); StartCoroutine(Guarded()); }
    void OnLog(string text, string stack, LogType kind) { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) evidence.errors.Add(text); }
    IEnumerator Guarded()
    {
        Application.logMessageReceived += OnLog;
        var pending = new Stack<IEnumerator>(); pending.Push(Verify());
        while (true)
        {
            bool next = false; Exception failure = null;
            var steps = pending.Peek();
            try { next = steps.MoveNext(); } catch (Exception error) { failure = error; }
            if (failure != null) { evidence.errors.Add(failure.ToString()); break; }
            if (!next) { pending.Pop(); if (pending.Count == 0) break; continue; }
            if (steps.Current is IEnumerator nested) pending.Push(nested); else yield return steps.Current;
        }
        evidence.status = evidence.errors.Count == 0 && evidence.recognized && evidence.guidancePlayback ? "PASS" : "FAIL";
        Application.logMessageReceived -= OnLog;
        File.WriteAllText(Path.Combine(Output, "clone-runtime.json"), JsonUtility.ToJson(evidence, true));
        PublicCloneRunValidation.Finish();
        EditorApplication.Exit(evidence.status == "PASS" ? 0 : 1);
    }
    static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
    static T Find<T>() where T : Component => Resources.FindObjectsOfTypeAll<T>().Single(x => x.gameObject.scene.IsValid());
    static T Field<T>(object obj, string field) => (T)obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    IEnumerator Verify()
    {
        evidence.unityVersion = Application.unityVersion; evidence.dataDirectory = Application.persistentDataPath;
        yield return new WaitForSeconds(2);
        evidence.width = Screen.width; evidence.height = Screen.height;
        evidence.offline = CloudModeSession.Mode == ProfileDataMode.Local && !CloudProfileApi.CanRequest;
        Require(evidence.offline, "Unexpected network-enabled default.");
        var bootstrap = Find<AppPofoBootstrap>(); evidence.bootstrap = bootstrap.IsInitialized;
        Require(evidence.bootstrap, "Real Bootstrap/SQLite initialization failed.");
        var welcome = Find<WelcomeCloudModeController>();
        var welcomeRoot = welcome.gameObject;
        Canvas.ForceUpdateCanvases();
        yield return Capture("01-welcome.png");
        // Follow the same existing UI controller methods as local onboarding.
        var list = Find<ProfileListScreen>();
        Field<Button>(welcome, "startButton").onClick.Invoke();
        yield return new WaitForSeconds(.4f);
        Require(list.gameObject.activeInHierarchy, "Local Start navigation did not open the profile list.");
        list.OpenCreateProfileScreen();
        var create = Find<CreateProfileScreen>();
        Field<InputField>(create, "displayNameInput").text = "포트폴리오 데모";
        Field<InputField>(create, "headlineInput").text = "Unity · C# 개발자";
        Field<InputField>(create, "bioInput").text = "실제 앱 실행 화면 · 비식별 데모 데이터";
        create.Save(); long id = create.CurrentProfileId;
        Require(id > 0, "UI did not create a real local profile.");
        evidence.localProfileCreatedViaUI = true;
        create.ToggleSkill("Unity"); create.ToggleSkill("C#");
        yield return new WaitForSeconds(.4f);
        yield return Capture("02-onboarding-skills.png");
        create.Save(); create.SkipFaceRegistration();
        var db = new AppPofoDatabase(); db.Initialize();
        var reread = new SQLiteProfileRepository(db).Find(id);
        evidence.sqliteReopened = reread != null && reread.DisplayName == "포트폴리오 데모" && reread.Items.Count == 2;
        Require(evidence.sqliteReopened, "Profile/skills not persisted in independently reopened real SQLite.");
        // Use real ONNX output for stored face and lookup; never a fabricated vector.
        var query = ExtractRealEmbedding();
        const string model = "SFace-2021dec-public-clone-smoke";
        bootstrap.FaceEmbeddingRepository.Add(new FaceEmbedding(0, id, query, model, null, FaceEmbeddingSourceType.Image, DateTimeOffset.UtcNow));
        var persisted = new SQLiteFaceEmbeddingRepository(new AppPofoDatabase()).GetByModel(model);
        evidence.faceStored = persisted.Count == 1 && persisted[0].Values.Count == 128;
        Require(evidence.faceStored, "Real inferred face did not persist.");
        var second = ExtractRealEmbedding();
        evidence.recognized = FaceRecognitionMatcher.TryRecognize(second, persisted, .65f, out var match) && match.Matched && match.ProfileId == id;
        evidence.matchCosine = match == null ? 0 : match.Similarity;
        Require(evidence.recognized, "Real inference/SQLite/matcher lookup failed.");
        create.gameObject.SetActive(false); list.gameObject.SetActive(false);
        var card = Find<BusinessCardPanelController>(); card.Show(id);
        var texts = card.GetComponentsInChildren<Text>(false);
        evidence.businessCardBound = texts.Any(t => t.text.Contains("포트폴리오 데모"));
        Require(evidence.businessCardBound, "Real card UI did not bind the saved profile.");
        yield return new WaitForSeconds(.6f);
        yield return Capture("03-business-card.png");
        card.gameObject.SetActive(false);
        var detail = Find<ProfileDetailScreen>(); detail.Show(id);
        yield return new WaitForSeconds(.6f);
        yield return Capture("04-profile-detail.png");
        // Verify imported scene clips and the existing guidance source, without opening a webcam.
        var faceCamera = Find<FaceRegistrationCamera>();
        foreach (var name in new[] { "centerVoice", "frontVoice", "leftVoice", "rightVoice", "completeVoice", "tooDarkVoice" })
        {
            var clip = Field<AudioClip>(faceCamera, name);
            Require(clip != null && clip.length > 1 && clip.samples > 20000, "Spoken guidance clip missing: " + name);
            Require(clip.LoadAudioData(), "Guidance audio could not be loaded: " + name);
            evidence.importedGuidanceClips++;
        }
        // Use the actual referenced source. Activate only its object, not camera acquisition.
        // The application intentionally creates this source lazily when preview starts.
        // Exercise that existing initializer without starting a physical camera.
        typeof(FaceRegistrationCamera).GetMethod("EnsureGuidanceAudioSource", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(faceCamera, null);
        var source = Field<AudioSource>(faceCamera, "guidanceAudioSource");
        Require(source != null, "Guidance source not wired.");
        var playback = new GameObject("Guidance playback verification").AddComponent<AudioSource>();
        playback.volume = source.volume; playback.spatialBlend = source.spatialBlend;
        playback.clip = Field<AudioClip>(faceCamera, "frontVoice"); playback.Play();
        yield return null;
        evidence.guidancePlayback = playback.isPlaying;
        playback.Stop(); Destroy(playback.gameObject); Require(evidence.guidancePlayback, "Unity guidance source failed to start playback.");
    }
    float[] ExtractRealEmbedding()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Require(texture.LoadImage(File.ReadAllBytes("Docs/TestFixtures/synthetic-face.png")), "Synthetic fixture unavailable.");
            var yunet = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/AppPofo/Models/FaceDetection/face_detection_yunet_2026may.onnx");
            var sface = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/AppPofo/Models/FaceRecognition/face_recognition_sface_2021dec.onnx");
            Require(yunet != null && sface != null, "Models not imported after clean install.");
            using (var detector = new SentisFaceDetector(yunet, YuNetExecutionPolicy.Registration))
            using (var frame = new FaceDetectionController(detector).ProcessFrameWithCrop(texture, new CameraFrameOrientation(0, false, texture.width, texture.height)))
            {
                Require(frame.Detection.Found && frame.HasFaceCrop, "Real YuNet failed.");
                var p = frame.CanonicalLandmarks; var o = (Vector2)frame.CropOrigin;
                var local = new FaceLandmarks5(p.RightEye-o, p.LeftEye-o, p.NoseTip-o, p.RightMouthCorner-o, p.LeftMouthCorner-o);
                using (var aligned = FaceAligner.Align(frame.FaceMat, local))
                using (var recognizer = new SFaceRecognizer(sface, BackendType.CPU)) return recognizer.ExtractEmbedding(aligned);
            }
        }
        finally { Destroy(texture); }
    }
    IEnumerator Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        // Batchmode does not execute WaitForEndOfFrame. Let Unity's screenshot request
        // capture the real Game view on its next rendered frame; bound the async wait.
        string target = Path.Combine(Output, name);
        ScreenCapture.CaptureScreenshot(target);
        double deadline = Time.realtimeSinceStartupAsDouble + 20;
        while (!File.Exists(target)) { Require(Time.realtimeSinceStartupAsDouble < deadline, "Game view screenshot timed out."); yield return null; }
        Require(new FileInfo(target).Length > 10000, "Game view capture is empty.");
        evidence.screenshots.Add(name);
    }
}
