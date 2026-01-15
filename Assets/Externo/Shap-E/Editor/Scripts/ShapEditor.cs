using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections;
using UnityEngine.Networking;
using Unity.EditorCoroutines.Editor;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PainterUtils;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;

namespace AiKodexShapE
{
    public class ShapEditor : EditorWindow
    {
        string prompt = "";
        string textToMeshID = "nejnwmcwvhcax9";
        string imageToMeshID = "UnderDevelopment";
        Texture2D fromImage;
        int steps = 64;
        int cfg = 15;
        private Vector2 mainScroll;
        string generator_URL;
        private bool initDone = false;
        GUIStyle StatesLabel, headStyle, subStyle, sectionTitle, centeredLabelStyle, plusButton, style;
        private bool postFlag = false;
        private int postProgress = 0;
        private string directoryPath;
        private string modelName;
        GameObject prefabObject, defaultPrefabObject;
        Editor prefabObjectEditor, defaultPrefabObjectEditor;
        Mesh mesh;
        private MeshPreview preview, defaultPreview, decimatePreview;
        int viewingModeIndex = 0;
        float getMiddleRectX;
        readonly string[] viewingModeString = { "Prefab", "Model" };
        public enum Format
        {
            FBX,
            GLB,
            BLEND,
            OBJ,
            GLTF
        };
        public static Format format = Format.FBX;
        private bool selectionChanged = false, viewingModeIndexChanged = false;
        int previousVoiwingModeIndex = 0;
        private UnityEngine.Object previousSelection;
        private string base64encJPG;
        [SerializeField]
        private MeshUtils.SimplificationOptions simplificationOptions = MeshUtils.SimplificationOptions.Default;
        [SerializeField]
        private MeshUtils.LODLevel[] levels = null;
        private float ratio = 0.5f;
        private bool canPanDecimatePreview = false;
        private int lodCount = 3;
        private List<float> transitionHeights = new List<float>();
        private List<float> lodRatios = new List<float>();

        //Painter
        private GUIStyle titleStyle;
        private bool allowPainting = false;
        private bool changingBrushValue = false;
        private bool allowSelect = false;
        private bool isPainting = false;
        private bool isRecord = false;

        private Vector2 mousePos = Vector2.zero;
        private Vector2 lastMousePos = Vector2.zero;
        private RaycastHit curHit;


        private float brushSize = 0.1f;
        private float brushOpacity = 1f;
        private float brushFalloff = 0.1f;

        private Color brushColor;
        private float brushIntensity;

        private const float MinBrushSize = 0.01f;
        public const float MaxBrushSize = 10f;


        private int curColorChannel = (int)PaintType.All;

        private Mesh curMesh;
        private PainterObject m_target;
        private GameObject m_active;

        //Unity General
        bool playModeCurrent, playModePrevious;


        void Awake()
        {
            directoryPath = "Assets/Shap-E/Models";
            generator_URL = PlayerPrefs.GetString("ShapE_generator_URL");
            defaultPrefabObject = (GameObject)AssetDatabase.LoadAssetAtPath("Assets/Shap-E/Editor/Resources/Cube.png", typeof(GameObject));
        }
        private void OnEnable()
        {
            transitionHeights.Add(0.5f);
            transitionHeights.Add(0.17f);
            transitionHeights.Add(0.02f);
            lodRatios.Add(1f);
            lodRatios.Add(0.65f);
            lodRatios.Add(0.4225f);
            SceneView.duringSceneGui -= this.OnSceneGUI;
            SceneView.duringSceneGui += this.OnSceneGUI;
        }
        private void OnDisable()
        {
            if (preview != null)
            {
                preview.Dispose();
                preview = null;
            }
            if (defaultPreview != null)
            {
                defaultPreview.Dispose();
                defaultPreview = null;
            }
            if (decimatePreview != null)
            {
                decimatePreview.Dispose();
                decimatePreview = null;
            }
        }
        private void OnDestroy()
        {
            SceneView.duringSceneGui -= this.OnSceneGUI;
        }
        private void OnSelectionChange()
        {
            m_target = null;
            m_active = null;
            curMesh = null;
            if (Selection.activeGameObject != null)
            {
                m_target = Selection.activeGameObject.GetComponent<PainterObject>();
                curMesh = PainterUtility.GetMesh(Selection.activeGameObject);

                var activeGameObject = Selection.activeGameObject;
                if (curMesh != null)
                {
                    m_active = activeGameObject;
                }
            }
            allowSelect = (m_target == null);
        }
        [MenuItem("Window/Shap-E")]
        static void Init()
        {
            ShapEditor window = (ShapEditor)EditorWindow.GetWindow(typeof(ShapEditor));
            window.titleContent.text = "Shap-E";
            window.minSize = new Vector2(600, 600);

        }
        void InitStyles()
        {
            style = new GUIStyle("WhiteLargeLabel");
            sectionTitle = new GUIStyle("WhiteLargeLabel");
            centeredLabelStyle = new GUIStyle("WhiteLargeLabel");
            plusButton = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            subStyle = new GUIStyle("Label");
            subStyle.fontSize = 11;
            subStyle.normal.textColor = Color.white;
            sectionTitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            centeredLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            headStyle = new GUIStyle("BoldLabel");
            headStyle.fontSize = 20;
            headStyle.normal.textColor = Color.white;
            initDone = true;
            StatesLabel = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(),
                padding = new RectOffset(),
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };

            defaultPrefabObject = (GameObject)AssetDatabase.LoadAssetAtPath("Assets/Shap-E/Editor/Resources/Cube.prefab", typeof(GameObject));

        }
        void OnGUI()
        {
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll, BackgroundStyle.Get(new Color(0, 0, 0, 0.4f)));
            if (!initDone)
                InitStyles();
            EditorGUILayout.BeginHorizontal();
            Texture logo = (Texture)AssetDatabase.LoadAssetAtPath("Assets/Shap-E/Editor/Resources/Logo.png", typeof(Texture));
            Texture infoToolTip = (Texture)AssetDatabase.LoadAssetAtPath("Assets/Shap-E/Editor/Resources/Info.png", typeof(Texture));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("              Shap-E", headStyle, GUILayout.MinHeight(23));
            EditorGUILayout.LabelField("                      Version 2.0", subStyle);
            EditorGUILayout.EndVertical();
            GUI.DrawTexture(new Rect(10, 3, 65, 65), logo, ScaleMode.StretchToFill, true, 10.0F);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);
            GUILayout.BeginVertical();
            EditorGUILayout.LabelField("Mesh Generator", sectionTitle);
            EditorGUILayout.Space(15);
            generator_URL = EditorGUILayout.TextField(new GUIContent("URL  ", infoToolTip, "Enter Generator URL"), generator_URL);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("");
            if (GUILayout.Button("Save", GUILayout.MaxWidth(40), GUILayout.MaxHeight(17)))
                PlayerPrefs.SetString("ShapE_generator_URL", generator_URL);

            GUILayout.EndHorizontal();
            EditorGUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUI.BeginDisabledGroup(fromImage != null);
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Prompt", EditorStyles.boldLabel);
            EditorStyles.textArea.wordWrap = true;
            prompt = Regex.Replace(prompt, @"\\(?!n|"")", "");
            prompt = Regex.Replace(prompt, "(?<!n)\n", "\\n");
            prompt = Regex.Replace(prompt, "(?<!\\\\)\"", "\\\"");
            EditorGUI.BeginChangeCheck();
            prompt = EditorGUILayout.TextArea(prompt, EditorStyles.textArea, GUILayout.Height(50));
            if (EditorGUI.EndChangeCheck())
                modelName = prompt.Replace(" ", "_");
            EditorGUI.EndDisabledGroup();
            //IDs
            // EditorGUI.BeginDisabledGroup(true);
            // EditorGUILayout.LabelField("Text to Mesh ID", textToMeshID, EditorStyles.textArea, GUILayout.Height(20));
            // EditorGUILayout.LabelField("Image to Mesh ID", imageToMeshID, EditorStyles.textArea, GUILayout.Height(20));
            // EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndVertical();

            // From Image - Under Development 
            // GUILayout.FlexibleSpace();
            // EditorGUILayout.BeginVertical();
            // EditorGUILayout.LabelField("From Image", EditorStyles.boldLabel, GUILayout.MaxWidth(80));
            // EditorGUI.BeginDisabledGroup(prompt != "");
            // EditorGUI.BeginChangeCheck();
            // fromImage = (Texture2D)EditorGUILayout.ObjectField(fromImage, typeof(Texture2D), false, GUILayout.Width(70), GUILayout.Height(70));
            // if (GUILayout.Button("Clear", GUILayout.MaxWidth(70)))
            //     fromImage = null;
            // if (EditorGUI.EndChangeCheck())
            // {
            //     modelName = fromImage == null ? modelName = "" : modelName = fromImage.name;
            //     cfg = 3;
            // }

            // EditorGUI.EndDisabledGroup();
            // GUILayout.FlexibleSpace();
            // EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Generation Settings", EditorStyles.boldLabel);
            steps = EditorGUILayout.IntSlider(new GUIContent("Refinement Steps", infoToolTip, "The number of iterations performed by the model to render the object."), steps, 8, 64);
            cfg = EditorGUILayout.IntSlider(new GUIContent("Guidance Scale", infoToolTip, "Determines how accurately the model represents the prompt or the image."), cfg, 1, 20);

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            modelName = EditorGUILayout.TextField("File Name", modelName);
            format = (Format)EditorGUILayout.EnumPopup("", format, GUILayout.MaxWidth(50));
            EditorGUILayout.EndHorizontal();
            directoryPath = EditorGUILayout.TextField("Model Folder", directoryPath);

            EditorGUILayout.Space(5);
            EditorGUI.BeginDisabledGroup(prompt == "" && fromImage == null || postFlag == true || modelName == "");
            if (GUILayout.Button("Generate 3D Model", GUILayout.Height(30)))
            {
                    OverwriteCheck();
                    postFlag = true;
                    postProgress = 0;
                    if (fromImage == null)
                        this.StartCoroutine(Post($"{generator_URL}", "{\"prompt\":\"" + $"{prompt}" + "\",\"steps\":\"" + $"{steps}" + "\",\"cfg\":\"" + $"{cfg}" + "\",\"fileFormat\":\"" + $"{format}" + "\"}"));
                    else
                    {
                        byte[] encJPG = fromImage.DeCompress().EncodeToJPG();
                        base64encJPG = Convert.ToBase64String(encJPG);
                        this.StartCoroutine(Post($"https://{imageToMeshID}-5000.proxy.runpod.net/data", "{\"prompt\":\"" + $"{base64encJPG}" + "\",\"steps\":\"" + $"{steps}" + "\",\"cfg\":\"" + $"{cfg}" + "\",\"fileFormat\":\"" + $"{format}" + "\"}"));
                    }
            }
            EditorGUI.EndDisabledGroup();
            Rect loading = GUILayoutUtility.GetRect(9, 9);
            if (postFlag)
            {
                GUILayout.Space(10);
                Repaint();
                EditorGUI.ProgressBar(loading, Mathf.Sqrt(++postProgress) * 0.005f, "");
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
            GUILayout.BeginVertical();
            EditorGUILayout.LabelField("Preview", sectionTitle);
            EditorGUILayout.Space(15);

            if (Selection.activeObject != previousSelection)
                selectionChanged = true;
            else
                selectionChanged = false;
            previousSelection = Selection.activeObject;

            if (viewingModeIndex != previousVoiwingModeIndex)
                viewingModeIndexChanged = true;
            else
                viewingModeIndexChanged = false;
            previousVoiwingModeIndex = viewingModeIndex;
            playModeCurrent = EditorApplication.isPlaying;
            viewingModeIndex = GUILayout.SelectionGrid(viewingModeIndex, viewingModeString, 2);
            if (viewingModeIndex == 0)
            {
                if (ValidSelectionCheck(Selection.activeGameObject))
                {
                    if (prefabObject != null || selectionChanged || viewingModeIndexChanged)
                    {

                        prefabObject = Selection.activeGameObject;
                        prefabObject.GetComponent<MeshRenderer>().sharedMaterial = (Material)AssetDatabase.LoadAssetAtPath("Assets/Shap-E/Materials/Vertex Color.mat", typeof(Material));
                        if (prefabObjectEditor == null || selectionChanged || viewingModeIndexChanged || playModeCurrent != playModePrevious)
                            prefabObjectEditor = Editor.CreateEditor(prefabObject);
                        prefabObjectEditor.OnInteractivePreviewGUI(GUILayoutUtility.GetRect(200, 200), BackgroundStyle.Get(new Color(0, 0, 0, 0.4f)));
                        defaultPrefabObjectEditor = null;
                    }
                    EditorGUILayout.LabelField(prefabObject.name);
                }
                else
                {

                    if (defaultPrefabObjectEditor == null || playModeCurrent != playModePrevious)
                        defaultPrefabObjectEditor = Editor.CreateEditor(defaultPrefabObject);

                    defaultPrefabObjectEditor.OnInteractivePreviewGUI(GUILayoutUtility.GetRect(200, 200), BackgroundStyle.Get(new Color(0, 0, 0, 0.4f)));
                    GUILayout.Label("Default Cube");
                }
                playModePrevious = playModeCurrent;
                EditorGUI.BeginDisabledGroup(m_active == null);
                if (GUILayout.Button("Export to Scene"))
                {
                    var obj = Instantiate(Selection.activeObject, Vector3.zero, Quaternion.identity);
                    obj.name = Selection.activeObject.name;
                }
                EditorGUI.EndDisabledGroup();
            }
            if (viewingModeIndex == 1)
            {
                if (ValidSelectionCheck(Selection.activeGameObject))
                {
                    MeshFilter meshFilter = Selection.activeGameObject.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        var mesh = new Mesh();
                        mesh = Selection.activeGameObject.GetComponent<MeshFilter>().sharedMesh;
                        if (preview == null)
                            preview = new UnityEditor.MeshPreview(mesh);
                        if (preview.mesh != mesh)
                            preview.mesh = mesh;

                        var rect = GUILayoutUtility.GetRect(1, 200);
                        preview.OnPreviewGUI(rect, BackgroundStyle.Get(new Color(0, 0, 0, 0.4f)));
                        preview.OnPreviewSettings();
                    }
                }
                else
                {
                    EditorGUI.BeginDisabledGroup(true);
                    mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    if (defaultPreview == null)
                        defaultPreview = new UnityEditor.MeshPreview(mesh);
                    if (defaultPreview.mesh != mesh)
                        defaultPreview.mesh = mesh;

                    var rect = GUILayoutUtility.GetRect(0, 200);
                    defaultPreview.OnPreviewGUI(rect, BackgroundStyle.Get(new Color(0, 0, 0, 0.4f)));
                    defaultPreview.OnPreviewSettings();
                    EditorGUI.BeginDisabledGroup(false);
                }
            }
            if (!ValidSelectionCheck(Selection.activeGameObject))
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("");
                getMiddleRectX = GUILayoutUtility.GetLastRect().x;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUI.Label(new Rect(getMiddleRectX - 50, GUILayoutUtility.GetLastRect().y - 145, 200, 20), "No Model Selected", "WhiteLargeLabel");
                GUI.Label(new Rect(getMiddleRectX - 100, GUILayoutUtility.GetLastRect().y - 128, 250, 20), "Select a model from the project to preview");
            }
            EditorGUILayout.Space(15);
            GUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Mesh Tools", sectionTitle);
            EditorGUILayout.Space(10);
            ratio = EditorGUILayout.Slider(new GUIContent("Ratio", infoToolTip, "The ratio of faces to keep after decimation.\n\nOn 1.0: the mesh is unchanged.\nOn 0.5: edges have been collapsed such that half the number of faces remain (see note below).\nOn 0.0: all faces have been removed.\n\nNote\nAlthough the Ratio is directly proportional to the number of remaining faces, triangles are used when calculating the ratio."), ratio, 0, 1);
            canPanDecimatePreview = EditorGUILayout.Toggle(new GUIContent("Enable Pan and Zoom", infoToolTip, "Toggle button to enable zooming (scroll), panning (middle mouse button), and light changes (right click)."), canPanDecimatePreview);
            if (ValidSelectionCheck(Selection.activeGameObject))
            {
                EditorGUI.BeginDisabledGroup(!canPanDecimatePreview);
                MeshFilter meshFilter = Selection.activeGameObject.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    var mesh = new Mesh();
                    mesh = Selection.activeGameObject.GetComponent<MeshFilter>().sharedMesh;
                    if (decimatePreview == null)
                        decimatePreview = new UnityEditor.MeshPreview(mesh);
                    if (decimatePreview.mesh != mesh)
                        decimatePreview.mesh = mesh;

                    var rect = GUILayoutUtility.GetRect(1, 400);
                    decimatePreview.OnPreviewGUI(rect, BackgroundStyle.Get(new Color(0, 0, 0, 0.4f)));
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUI.BeginDisabledGroup(m_active == null);
            if (GUILayout.Button("Decimate"))
            {
                SimplifyMeshFilter(Selection.activeGameObject.GetComponent<MeshFilter>(), ratio);
            }
            if (ValidSelectionCheck(Selection.activeGameObject))
                EditorGUILayout.LabelField("Mesh Info\nTriangles : " + Selection.activeGameObject.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3 + "\nVerts : " + Selection.activeGameObject.GetComponent<MeshFilter>().sharedMesh.vertexCount.ToString(), EditorStyles.helpBox);
            EditorGUI.EndDisabledGroup();
            //Auto Lods
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            for (int i = 0; i < lodCount; i++)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"LOD Level {i + 1}", centeredLabelStyle);
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button(new GUIContent("×", "Deletes this LOD level."), GUILayout.Width(20)))
                {
                    RemoveLODLevel(i);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                ++EditorGUI.indentLevel;
                if (i < transitionHeights.Count)
                {
                    transitionHeights[i] = EditorGUILayout.Slider(new GUIContent("Transition", infoToolTip, "Gets or sets the screen relative height to use for the transition [0-1]."), transitionHeights[i], 0, 1);
                    lodRatios[i] = EditorGUILayout.Slider(new GUIContent("LOD Ratio", infoToolTip, "Determines the quality of the model. See decimation tooltip to understand this property in detail [0-1]."), lodRatios[i], 0, 1);
                }

                --EditorGUI.indentLevel;
            }
            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", plusButton, GUILayout.Width(50)))
                AddLODLevel();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);

            //--------------------
            EditorGUI.BeginDisabledGroup(EditorUtility.IsPersistent(m_active) || m_active == null);
            if (GUILayout.Button("Generate LODs"))
            {
                AutoLodsLevels(lodCount);
                MeshUtils.LODGenerator.GenerateLODs(Selection.activeGameObject, levels, true, simplificationOptions);
            }
            EditorGUI.EndDisabledGroup();
            if (EditorUtility.IsPersistent(m_active))
                EditorGUILayout.LabelField("To generate LODs, export the model to the hierarchy and select it.", EditorStyles.helpBox);
            GUILayout.EndVertical();

            // Painter Start
            //Header
            EditorGUILayout.Space(15);
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Mesh Painter", sectionTitle);
            GUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            //Body
            GUILayout.BeginVertical(GUI.skin.box);

            if (m_target != null)
            {
                if (!m_target.isActiveAndEnabled)
                {
                    EditorGUILayout.LabelField("(Enable " + m_target.name + " to show Painter settings)");
                }
                else
                {
                    allowPainting = EditorGUILayout.Toggle("Enable Painting", allowPainting);

                    if (allowPainting)
                    {
                        Tools.current = Tool.None;
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Paint Type:", GUILayout.Width(90));
                    string[] channelName = { "All", "R", "G", "B", "A" };
                    int[] channelIds = { 0, 1, 2, 3, 4 };
                    curColorChannel = EditorGUILayout.IntPopup(curColorChannel, channelName, channelIds, GUILayout.Width(50));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (curColorChannel == (int)PaintType.All)
                    {
                        brushColor = EditorGUILayout.ColorField("Brush Color:", brushColor);
                    }
                    else
                    {
                        brushIntensity = EditorGUILayout.Slider("Intensity:", brushIntensity, 0, 1);
                    }
                    if (GUILayout.Button("Fill"))
                    {
                        FillVertexColor();
                    }
                    GUILayout.EndHorizontal();
                    brushSize = EditorGUILayout.Slider("Brush Size:", brushSize, MinBrushSize, MaxBrushSize);
                    brushOpacity = EditorGUILayout.Slider("Brush Opacity:", brushOpacity, 0, 1);
                    brushFalloff = EditorGUILayout.Slider("Brush Falloff:", brushFalloff, MinBrushSize, brushSize);
                    GUILayout.Space(5);
                    //Footer
                    GUILayout.Label("Left Mouse Button : Paint | Hover over the model and click and drag the mouse to paint.\nLeft Mouse Button + Shift : Opacity | While the key is pressed, drag Mouse horizontally to change\nLeft Mouse Button + Ctrl : Size | While the key is pressed, drag horizontally to change\nLeft Mouse Button + Shift + Ctrl : Falloff | While the keys are pressed, drag horizontally to change\nCtrl + Z/Y : Undo / Redo | You may utilize this on per session basis.", EditorStyles.helpBox);
                    Repaint();
                    GUILayout.Space(5);
                }
            }
            else if (m_active != null)
            {
                if (EditorUtility.IsPersistent(m_active))
                {
                    if (GUILayout.Button("BackUp Model and Export to Scene for Painting"))
                    {
                        string originalAssetPath = AssetDatabase.GetAssetPath(m_active);
                        string originalAssetName = m_active.name;

                        string newAssetPath = originalAssetPath.Replace(originalAssetName, originalAssetName + "_BackUp");

                        if (!AssetDatabase.CopyAsset(originalAssetPath, newAssetPath))
                            Debug.LogWarning($"Failed to copy {originalAssetPath}");
                        AssetDatabase.Refresh();
                        GameObject duplicate = Instantiate(m_active);
                        Selection.activeGameObject = duplicate;
                        allowPainting = true;

                    }
                    EditorGUILayout.LabelField("Info : Duplication will make a backup of the model file", EditorStyles.helpBox);
                    if (GUILayout.Button("Export to Scene without Duplicating"))
                    {
                        GameObject duplicate = Instantiate(m_active);
                        Selection.activeGameObject = duplicate;
                        allowPainting = true;
                    }
                    EditorGUILayout.LabelField("Warning : Vertex Colors may be lost", EditorStyles.helpBox);
                }
                else
                {
                    if (GUILayout.Button("Enable Painting for Selected Object"))
                    {
                        m_active.AddComponent<PainterObject>();
                        OnSelectionChange();
                    }
                    EditorGUILayout.LabelField("Info : Make sure you have a backup of the original model file before making changes", EditorStyles.helpBox);
                }
            }
            else
            {
                allowPainting = false;
                EditorGUILayout.LabelField("Please select the model from the scene to paint.");
            }
            GUILayout.EndVertical();
            //Painter End

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndScrollView();
        }
        IEnumerator Post(string url, string bodyJsonString)
        {
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(bodyJsonString);
            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            postProgress = 1;
            postFlag = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("There was an error in generating the model. \nPlease check your invoice/order number and try again or check the troubleshooting section in the documentation for more information." + "\nInfo: " + request.result + "\nError Code: " + request.responseCode);
            }
            else
            {
                if (request.downloadHandler.text == "Invalid Response")
                    Debug.Log("Invalid Invoice/Order Number. Please check your invoice/order number and try again");

                else if (request.downloadHandler.text == "Limit Reached")
                    Debug.Log("It seems that you may have reached the limit. To check your character usage, please click on the Status button. Please wait until the 1st of the next month to get a renewed character count. Thank you for using Shap-E for Unity.");
                else
                {
                    byte[] modelData = Convert.FromBase64String(request.downloadHandler.text);
                    File.WriteAllBytes($"Assets/Shap-E/Models/{modelName}.{format}", modelData);
                    Debug.Log($"<color=green>Inference Successful: </color>Please find the model in the {directoryPath}");
                    AssetDatabase.Refresh();
                    Selection.activeObject = (UnityEngine.Object)AssetDatabase.LoadAssetAtPath($"Assets/Shap-E/Models/{modelName}.{format}", typeof(UnityEngine.Object));
                }
            }

            request.Dispose();
        }
        IEnumerator Verify(string url, string bodyJsonString)
        {
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(bodyJsonString);
            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(request.error);
            }
            else
            {
                if (request.downloadHandler.text == "Not Verified")
                    Debug.Log("Invoice/Order number verification unsuccessful. Please check your invoice/order number and try again or contact the publisher on the email given in the documentation.");
                else
                    Debug.Log($"Your invoice is verified. You have generated {request.downloadHandler.text} objects. Thank you for choosing Shap-E for Unity!");
            }
            request.Dispose();
        }
        bool ValidSelectionCheck(GameObject selectedObject)
        {

            if (selectedObject != null && selectedObject.GetType().Equals(typeof(GameObject)) && selectedObject.GetComponent<MeshFilter>() != null)
                return true;
            else
                return false;
        }
        void OverwriteCheck()
        {
            string filePath = Path.Combine(directoryPath, modelName);
            int suffixNumber = 1;

            if (modelName[modelName.Length - 2] == '_')
                while (File.Exists(filePath + "." + format))
                {
                    modelName = modelName.Remove(modelName.Length - 1, 1) + suffixNumber;
                    filePath = Path.Combine(directoryPath, modelName);
                    suffixNumber++;
                }
    
            if(File.Exists(filePath + "." + format))
                modelName += "_1";

            if (modelName[modelName.Length - 2] == '_')
                while (File.Exists(filePath + "." + format))
                {
                    modelName = modelName.Remove(modelName.Length - 1, 1) + suffixNumber;
                    filePath = Path.Combine(directoryPath, modelName);
                    suffixNumber++;
                }

            
        }
        private void SimplifyMeshFilter(MeshFilter meshFilter, float ratio)
        {
            Mesh sourceMesh = meshFilter.sharedMesh;
            if (sourceMesh == null) // verify that the mesh filter actually has a mesh
                return;

            // Create our mesh simplifier and setup our entire mesh in it
            var meshSimplifier = new MeshUtils.MeshSimplifier();
            meshSimplifier.Initialize(sourceMesh);

            // This is where the magic happens, lets simplify!
            meshSimplifier.SimplifyMesh(ratio);

            // Create our final mesh and apply it back to our mesh filter
            meshFilter.sharedMesh = meshSimplifier.ToMesh();
        }
        private void AutoLodsLevels(int lodCount)
        {
            simplificationOptions = MeshUtils.SimplificationOptions.Default;
            levels = new MeshUtils.LODLevel[lodCount]; // Create an array to store LOD levels

            for (int i = 0; i < lodCount; i++)
            {
                levels[i] = new MeshUtils.LODLevel(transitionHeights[i], lodRatios[i])
                {
                    CombineMeshes = false,
                    CombineSubMeshes = false,
                    SkinQuality = SkinQuality.Auto,
                    ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    ReceiveShadows = true,
                    SkinnedMotionVectors = true,
                    LightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes,
                    ReflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes,
                };
            }

        }
        private void AddLODLevel()
        {
            lodCount++;
            transitionHeights.Add(0f);
            lodRatios.Add(0f);
            transitionHeights.TrimExcess();
            lodRatios.TrimExcess();
        }
        private void RemoveLODLevel(int index)
        {
            lodCount--;
            if (index >= 0 && index < transitionHeights.Count)
            {
                transitionHeights.RemoveAt(index);
                lodRatios.RemoveAt(index);
            }
            transitionHeights.TrimExcess();
            lodRatios.TrimExcess();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (allowPainting)
            {
                bool isHit = false;
                if (!allowSelect)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                }
                Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePos);
                if (m_target != null && curMesh != null)
                {
                    Matrix4x4 mtx = m_target.transform.localToWorldMatrix;
                    RaycastHit tempHit;
                    isHit = RXLookingGlass.IntersectRayMesh(worldRay, curMesh, mtx, out tempHit);
                    if (isHit)
                    {
                        if (!changingBrushValue)
                        {
                            curHit = tempHit;
                        }
                        if (isPainting && m_target.isActiveAndEnabled && !changingBrushValue)
                        {
                            PaintVertexColor();
                        }
                    }
                }

                if (isHit || changingBrushValue)
                {

                    Handles.color = getSolidDiscColor((PaintType)curColorChannel);
                    Handles.DrawSolidDisc(curHit.point, curHit.normal, brushSize);
                    Handles.color = getWireDiscColor((PaintType)curColorChannel);
                    Handles.DrawWireDisc(curHit.point, curHit.normal, brushSize);
                    Handles.DrawWireDisc(curHit.point, curHit.normal, brushFalloff);
                }
            }

            if (m_active != null && allowPainting)
            {
                ProcessInputs();
                sceneView.Repaint();
            }

        }
        void PaintVertexColor()
        {
            if (m_target && m_active)
            {
                curMesh = PainterUtility.GetMesh(m_active);
                if (curMesh)
                {
                    if (isRecord)
                    {
                        m_target.PushUndo();
                        isRecord = false;
                    }
                    Vector3[] verts = curMesh.vertices;
                    Color[] colors = new Color[0];
                    if (curMesh.colors.Length > 0)
                    {
                        colors = curMesh.colors;
                    }
                    else
                    {
                        colors = new Color[verts.Length];
                    }
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 vertPos = m_target.transform.TransformPoint(verts[i]);
                        float mag = (vertPos - curHit.point).magnitude;
                        if (mag > brushSize)
                        {
                            continue;
                        }
                        float falloff = PainterUtility.LinearFalloff(mag, brushSize);
                        falloff = Mathf.Pow(falloff, Mathf.Clamp01(1 - brushFalloff / brushSize)) * brushOpacity;
                        if (curColorChannel == (int)PaintType.All)
                        {
                            colors[i] = PainterUtility.VTXColorLerp(colors[i], brushColor, falloff);
                        }
                        else
                        {
                            colors[i] = PainterUtility.VTXOneChannelLerp(colors[i], brushIntensity, falloff, (PaintType)curColorChannel);
                        }
                    }
                    curMesh.colors = colors;
                }
                else
                {
                    OnSelectionChange();
                    Debug.LogWarning("Nothing to paint!");
                }

            }
            else
            {
                OnSelectionChange();
                Debug.LogWarning("Nothing to paint!");
            }
        }
        void FillVertexColor()
        {
            if (curMesh)
            {
                Vector3[] verts = curMesh.vertices;
                Color[] colors = new Color[0];
                if (curMesh.colors.Length > 0)
                {
                    colors = curMesh.colors;
                }
                else
                {
                    colors = new Color[verts.Length];
                }
                for (int i = 0; i < verts.Length; i++)
                {
                    if (curColorChannel == (int)PaintType.All)
                    {
                        colors[i] = brushColor;
                    }
                    else
                    {
                        colors[i] = PainterUtility.VTXOneChannelLerp(colors[i], brushIntensity, 1, (PaintType)curColorChannel);
                    }
                }
                curMesh.colors = colors;
            }
            else
            {
                Debug.LogWarning("Nothing to fill!");
            }
        }
        void ProcessInputs()
        {
            if (m_target == null)
            {
                return;
            }
            Event e = Event.current;
            mousePos = e.mousePosition;
            if (e.type == EventType.KeyDown)
            {
                if (e.isKey)
                {
                    if (e.keyCode == KeyCode.P)
                    {
                        allowPainting = !allowPainting;
                        if (allowPainting)
                        {
                            Tools.current = Tool.None;
                        }
                    }
                }
            }
            if (e.type == EventType.MouseUp)
            {
                changingBrushValue = false;
                isPainting = false;

            }
            if (lastMousePos == mousePos)
            {
                isPainting = false;
            }
            if (allowPainting)
            {
                if (e.type == EventType.MouseDrag && e.control && e.button == 0 && !e.shift)
                {
                    brushSize += e.delta.x * 0.005f;
                    brushSize = Mathf.Clamp(brushSize, MinBrushSize, MaxBrushSize);
                    brushFalloff = Mathf.Clamp(brushFalloff, MinBrushSize, brushSize);
                    changingBrushValue = true;
                }
                if (e.type == EventType.MouseDrag && !e.control && e.button == 0 && e.shift)
                {
                    brushOpacity += e.delta.x * 0.005f;
                    brushOpacity = Mathf.Clamp01(brushOpacity);
                    changingBrushValue = true;
                }
                if (e.type == EventType.MouseDrag && e.control && e.button == 0 && e.shift)
                {
                    brushFalloff += e.delta.x * 0.005f;
                    brushFalloff = Mathf.Clamp(brushFalloff, MinBrushSize, brushSize);
                    changingBrushValue = true;
                }
                if ((e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && !e.control && e.button == 0 && !e.shift && !e.alt)
                {
                    isPainting = true;
                    if (e.type == EventType.MouseDown)
                    {
                        isRecord = true;
                    }
                }
            }
            lastMousePos = mousePos;
        }
        Color getSolidDiscColor(PaintType pt)
        {
            switch (pt)
            {
                case PaintType.All:
                    return new Color(brushColor.r, brushColor.g, brushColor.b, brushOpacity);
                case PaintType.R:
                    return new Color(brushIntensity, 0, 0, brushOpacity);
                case PaintType.G:
                    return new Color(0, brushIntensity, 0, brushOpacity);
                case PaintType.B:
                    return new Color(0, 0, brushIntensity, brushOpacity);
                case PaintType.A:
                    return new Color(brushIntensity, 0, brushIntensity, brushOpacity);

            }
            return Color.white;
        }
        Color getWireDiscColor(PaintType pt)
        {
            switch (pt)
            {
                case PaintType.All:
                    return new Color(1 - brushColor.r, 1 - brushColor.g, 1 - brushColor.b, 1);
                case PaintType.R:
                    return Color.white;
                case PaintType.G:
                    return Color.white;
                case PaintType.B:
                    return Color.white;
                case PaintType.A:
                    return Color.white;
            }
            return Color.white;
        }

    }
    public static class ExtensionMethod
    {
        public static Texture2D DeCompress(this Texture2D source)
        {
            RenderTexture renderTex = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, renderTex);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            Texture2D readableText = new Texture2D(512, 512);
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            return readableText;
        }
    }
    public class RXLookingGlass
    {
        public static Type type_HandleUtility;
        protected static MethodInfo meth_IntersectRayMesh;

        static RXLookingGlass()
        {
            var editorTypes = typeof(Editor).Assembly.GetTypes();

            type_HandleUtility = editorTypes.FirstOrDefault(t => t.Name == "HandleUtility");
            meth_IntersectRayMesh = type_HandleUtility.GetMethod("IntersectRayMesh", (BindingFlags.Static | BindingFlags.NonPublic));
        }

        public static bool IntersectRayMesh(Ray ray, MeshFilter meshFilter, out RaycastHit hit)
        {
            return IntersectRayMesh(ray, meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix, out hit);
        }
        static object[] parameters = new object[4];
        public static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            parameters[0] = ray;
            parameters[1] = mesh;
            parameters[2] = matrix;
            parameters[3] = null;
            bool result = (bool)meth_IntersectRayMesh.Invoke(null, parameters);
            hit = (RaycastHit)parameters[3];
            return result;
        }
    }
    public static class BackgroundStyle
    {
        public static GUIStyle style = new GUIStyle();
        public static Texture2D texture = new Texture2D(1, 1);


        public static GUIStyle Get(Color color)
        {
            if (texture == null)
            {
                texture = new Texture2D(1, 1);
            }
            texture.SetPixel(0, 0, color);
            texture.Apply();
            style.normal.background = texture;
            return style;
        }
    }

}