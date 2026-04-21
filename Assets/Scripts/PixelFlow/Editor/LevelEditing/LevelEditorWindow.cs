#if UNITY_EDITOR
using Core.Runtime.ColorAtlas;
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.LevelEditing;
using PixelFlow.Runtime.Pigs;
using PixelFlow.Runtime.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PixelFlow.Editor.LevelEditing
{
    public sealed class LevelEditorWindow : EditorWindow
    {
        private const string DefaultDatabaseFolder = "Assets/Data/PixelFlow/Databases";
        private const string DefaultDatabaseAssetName = "PixelFlowLevelDatabase";
        private const string DefaultDefaultsFolder = "Assets/Data/PixelFlow/Defaults";
        private const string DefaultBlockDataAssetPath = DefaultDefaultsFolder + "/PixelFlowDefaultBlockData.asset";
        private const string CurrentLevelPrefsKey = "pixelflow.level.current_index";
        private const string DefaultBlockPrefabGuid = "331edf6250de4de4a909c2115614f2d7";
        private const string DefaultPigPrefabGuid = "5f47c7c5de4f2ce4dbe9606dc849868f";
        private const string ImportedLevelImagesFolder = "Assets/Imported Level Images";
        private const string ExampleSourceImagesFolderName = "Example Source Images";
        private const string TemporaryTestLevelRootName = "Temp";
        private const string TemporaryTestBoardRootName = "Board";
        private const string TemporaryTestDeckRootName = "Deck";
        private const string ColorAtlasTextureGuid = "2c8e7f499d401414b8215275b03a4c66";
        private const float MinGridZoom = 0.1f;
        private const float MaxGridZoom = 1f;
        private const float DefaultGridZoom = 0.3f;
        private const float MinDetailScale = 0.01f;
        private const float MaxDetailScale = 0.2f;
        private const float InitialImportedDetailScale = 0.05f;
        private const float InitialImportedAlphaThreshold = 0.1f;
        private const float PigQueueDragStartDistance = 4f;
        private const float GridPadding = 12f;
        private const int DetailedGridCellThreshold = 2048;
        private const int HighResolutionCellWarningThreshold = 4096;
        private const int MinHoldingContainerCount = 2;
        private const int MaxHoldingContainerCount = 5;
        [SerializeField] private LevelDatabase selectedDatabase;
        [SerializeField] private int selectedLevelIndex = -1;
        [SerializeField] private PigColor selectedBrushColor = PigColor.Pink;
        [SerializeField] private string workingLevelName = "Level 1";
        [SerializeField] private Texture2D sourceImage;
        [SerializeField] private BlockData selectedBlockData;
        [SerializeField] private ImageImportSettings workingImportSettings = new();
        [SerializeField] private PigQueueGenerationSettings pigQueueGenerationSettings = new();
        [SerializeField] private List<PlacedObjectData> workingPlacedObjects = new();
        [SerializeField] private List<PigQueueEntry> workingPigQueue = new();
        [SerializeField] private Vector2Int workingGridSize = new(12, 12);
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private float gridZoomScale = DefaultGridZoom;
        [SerializeField] private PigQueueEditorState pigQueueEditorState = new();

        private string statusMessage = "Create or assign a level database to begin.";
        private string pigValidationMessage = "Pig queue will be regenerated from the current grid when needed.";
        private MessageType pigValidationMessageType = MessageType.Info;
        private string[] cachedLevelNames = { "No Levels" };
        private GUIStyle cellGlyphStyle;
        private Texture2D gridPreviewTexture;
        private Color32[] gridPreviewPixels;
        private int[] cachedPlacementIndices;
        private bool[] cachedRootFlags;
        private Vector2Int cachedGridPreviewSize;
        private bool isGridPreviewDirty = true;
        private readonly Dictionary<PigColor, int> cachedPigCellCounts = new();
        private GameSceneContext editorManagedEnvironmentContext;
        private EnvironmentContext editorManagedEnvironmentInstance;

        [MenuItem("Tools/Pixel Flow/Level Editor")]
        private static void Open()
        {
            var window = GetWindow<LevelEditorWindow>("Pixel Flow Level Editor");
            window.minSize = new Vector2(960f, 720f);
            window.Show();
        }

        private void OnEnable()
        {
            TemporaryLevelSceneService.CleanupTemporaryArtifacts(
                TemporaryTestLevelRootName,
                TemporaryTestBoardRootName,
                TemporaryTestDeckRootName);
            TryAutoAssignDatabase();
            TryAutoAssignBlockData();
            selectedDatabase?.EnsureConsistency();
            EnsureEditorDefaults();
            RefreshUiState();
            EnsureEditorEnvironmentLoaded();
        }

        private void OnProjectChange()
        {
            TryAutoAssignDatabase();
            TryAutoAssignBlockData();
            selectedDatabase?.EnsureConsistency();
            EnsureEditorDefaults();
            RefreshUiState();
            Repaint();
        }

        private void OnDisable()
        {
            TemporaryLevelSceneService.CleanupTemporaryArtifacts(
                TemporaryTestLevelRootName,
                TemporaryTestBoardRootName,
                TemporaryTestDeckRootName);
            ReleaseEditorEnvironment();
            if (gridPreviewTexture != null)
            {
                DestroyImmediate(gridPreviewTexture);
                gridPreviewTexture = null;
            }
        }

        private void OnHierarchyChange()
        {
            Repaint();
        }

        private void EnsureEditorEnvironmentLoaded()
        {
            if (Application.isPlaying || !SceneContextEnvironmentUtility.TryResolveOpenSceneContext(out var sceneContext))
            {
                return;
            }

            var existingEnvironment = SceneContextEnvironmentUtility.ResolveEnvironment(sceneContext);
            if (existingEnvironment != null)
            {
                TrackEditorManagedEnvironment(sceneContext, existingEnvironment, markAsTemporary: false);

                sceneContext.InputManager?.RefreshInputCameraReference(preferSceneMain: true);
                return;
            }

            var spawnedEnvironment = sceneContext.EnsureEnvironment();
            if (spawnedEnvironment == null)
            {
                return;
            }

            TrackEditorManagedEnvironment(sceneContext, spawnedEnvironment, markAsTemporary: true);
            sceneContext.InputManager?.RefreshInputCameraReference(preferSceneMain: true);
            SceneView.RepaintAll();
        }

        private void ReleaseEditorEnvironment()
        {
            if (Application.isPlaying)
            {
                return;
            }

            editorManagedEnvironmentInstance = null;
            editorManagedEnvironmentContext = null;

            var sceneContexts = Resources.FindObjectsOfTypeAll<GameSceneContext>();
            for (int i = 0; i < sceneContexts.Length; i++)
            {
                var sceneContext = sceneContexts[i];
                if (sceneContext == null
                    || EditorUtility.IsPersistent(sceneContext)
                    || !sceneContext.gameObject.scene.IsValid())
                {
                    continue;
                }

                LevelPreviewService.ClearPreview(sceneContext, SceneContextEnvironmentUtility.ResolveTheme(sceneContext));
                var removedManagedEnvironment = SceneContextEnvironmentUtility.RemoveEditorManagedEnvironment(sceneContext);

                sceneContext.InputManager?.RefreshInputCameraReference(preferSceneMain: true);

                if (!removedManagedEnvironment)
                {
                    continue;
                }
            }

            SceneView.RepaintAll();
        }

        private void TrackEditorManagedEnvironment(
            GameSceneContext sceneContext,
            EnvironmentContext environment,
            bool markAsTemporary)
        {
            if (Application.isPlaying
                || sceneContext == null
                || environment == null
                || environment.gameObject == null
                || !environment.gameObject.scene.IsValid())
            {
                return;
            }

            if (markAsTemporary)
            {
                TemporaryLevelSceneService.ApplyTemporaryHideFlagsRecursive(environment.gameObject);
            }

            if (!SceneContextEnvironmentUtility.IsEditorManagedEnvironment(sceneContext, environment))
            {
                return;
            }

            editorManagedEnvironmentContext = sceneContext;
            editorManagedEnvironmentInstance = environment;
        }

        private void OnGUI()
        {
            HandleGridZoomInput();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            try
            {
                DrawHeader();
                EditorGUILayout.Space(8f);
                DrawImportSettings();
                EditorGUILayout.Space(8f);
                DrawPlaceableTools();
                EditorGUILayout.Space(8f);
                DrawGridSection();
                EditorGUILayout.Space(8f);
                DrawTestGenerationSection();
                EditorGUILayout.Space(8f);
                DrawPigQueueTools();
                EditorGUILayout.Space(8f);
                DrawStatusBox();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private GUIStyle CellGlyphStyle
        {
            get
            {
                cellGlyphStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip,
                    fontSize = 11,
                };
                cellGlyphStyle.normal.textColor = Color.white;
                return cellGlyphStyle;
            }
        }

        private void EnsureEditorDefaults()
        {
            workingImportSettings ??= new ImageImportSettings();
            selectedBlockData = ResolveWorkingBlockData(selectedBlockData);
            RepairDefaultBlockDataIfNeeded();
            pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            NormalizePigQueueGenerationSettings();
            workingPlacedObjects ??= new List<PlacedObjectData>();
            workingPigQueue ??= new List<PigQueueEntry>();
            pigQueueEditorState ??= new PigQueueEditorState();
            workingGridSize = ClampGridSize(workingGridSize);
            gridZoomScale = Mathf.Clamp(gridZoomScale, MinGridZoom, MaxGridZoom);
            pigQueueGenerationSettings.HoldingSlotCount = ClampHoldingContainerCount(pigQueueGenerationSettings.HoldingSlotCount);

            ClampPlacedObjectsToGrid();
        }

        private void RefreshUiState()
        {
            BuildLevelPopupNames();

            if (selectedDatabase == null)
            {
                selectedLevelIndex = -1;
                return;
            }

            if (selectedDatabase.Levels.Count > 0 && selectedLevelIndex < 0)
            {
                selectedLevelIndex = 0;
                LoadSelectedLevelIntoBuffer();
            }

            if (selectedLevelIndex >= selectedDatabase.Levels.Count)
            {
                selectedLevelIndex = selectedDatabase.Levels.Count - 1;
                LoadSelectedLevelIntoBuffer();
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Database & Level", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                var nextDatabase = (LevelDatabase)EditorGUILayout.ObjectField(
                    "Level Database",
                    selectedDatabase,
                    typeof(LevelDatabase),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyDatabaseSelection(nextDatabase);
                }

                if (selectedDatabase == null)
                {
                    EditorGUILayout.HelpBox("Assign a Pixel Flow Level Database or create one.", MessageType.Info);
                    if (GUILayout.Button("Create Level Database", GUILayout.Height(24f)))
                    {
                        CreateLevelDatabase();
                    }

                    return;
                }

                if (selectedDatabase.Levels.Count == 0)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Popup("Target Level", 0, cachedLevelNames);
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    var nextLevelIndex = EditorGUILayout.Popup("Target Level", selectedLevelIndex, cachedLevelNames);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ApplyLevelSelection(nextLevelIndex);
                    }
                }

                using (new EditorGUI.DisabledScope(selectedLevelIndex < 0))
                {
                    workingLevelName = EditorGUILayout.TextField("Level Name", workingLevelName);
                }

                DrawSourceImageRow();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create Level", GUILayout.Height(24f)))
                    {
                        CreateLevel();
                    }

                    using (new EditorGUI.DisabledScope(selectedLevelIndex < 0))
                    {
                        if (GUILayout.Button("Delete Level", GUILayout.Height(24f)))
                        {
                            DeleteSelectedLevel();
                        }
                    }

                    using (new EditorGUI.DisabledScope(selectedLevelIndex < 0))
                    {
                        if (GUILayout.Button("Save Level", GUILayout.Height(24f)))
                        {
                            SaveSelectedLevel();
                        }
                    }
                }
            }
        }

        private void DrawSourceImageRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Source Image");

                var display = GetSourceImageDisplayText();
                var displayRect = GUILayoutUtility.GetRect(
                    GUIContent.none,
                    EditorStyles.textField,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUI.SelectableLabel(displayRect, display, EditorStyles.textField);

                if (sourceImage != null)
                {
                    EditorGUIUtility.AddCursorRect(displayRect, MouseCursor.Link);
                    var current = Event.current;
                    if (current.type == EventType.MouseDown
                        && current.button == 0
                        && displayRect.Contains(current.mousePosition))
                    {
                        PingSourceImage();
                        current.Use();
                    }
                }

                using (new EditorGUI.DisabledScope(selectedLevelIndex < 0))
                {
                    if (GUILayout.Button("Select File...", GUILayout.Width(96f)))
                    {
                        PickImageFile();
                    }
                }

                using (new EditorGUI.DisabledScope(sourceImage == null))
                {
                    if (GUILayout.Button(new GUIContent("X", "Clear source image"), GUILayout.Width(26f), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                    {
                        SetSourceImage(null);
                        statusMessage = "Source image cleared.";
                    }
                }
            }
        }

        private void DrawImportSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);

                var currentColumns = workingImportSettings.TargetColumns;
                var currentRows = workingImportSettings.TargetRows;
                var currentFitMode = workingImportSettings.FitMode;
                var currentBoardFill = workingImportSettings.BoardFill;
                var currentBoardFillOverridden = workingImportSettings.BoardFillOverridden;
                var currentImageScale = workingImportSettings.ImageScale;
                var currentAlphaThreshold = workingImportSettings.AlphaThreshold;
                var currentCropTransparentBorders = workingImportSettings.CropTransparentBorders;

                EditorGUI.BeginChangeCheck();
                var nextBlockData = (BlockData)EditorGUILayout.ObjectField("Block Data", selectedBlockData, typeof(BlockData), false);
                var nextColumns = EditorGUILayout.IntField("Columns", currentColumns);
                var nextRows = EditorGUILayout.IntField("Rows", currentRows);
                var nextFitMode = (ImageFitMode)EditorGUILayout.EnumPopup("Fit Mode", workingImportSettings.FitMode);
                var nextBoardFill = EditorGUILayout.Slider("Board Fill", currentBoardFill, 0.4f, 1f);
                float nextImageScale;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Detail Scale");
                    nextImageScale = GUILayout.HorizontalSlider(currentImageScale, MinDetailScale, MaxDetailScale);
                    nextImageScale = EditorGUILayout.FloatField(nextImageScale, GUILayout.Width(54f));
                    nextImageScale = Mathf.Clamp(nextImageScale, MinDetailScale, MaxDetailScale);
                }
                var nextAlpha = EditorGUILayout.Slider("Alpha Threshold", currentAlphaThreshold, 0f, 1f);
                var nextCrop = EditorGUILayout.Toggle("Crop Transparent Borders", currentCropTransparentBorders);
                if (EditorGUI.EndChangeCheck())
                {
                    selectedBlockData = ResolveWorkingBlockData(nextBlockData);
                    RepairDefaultBlockDataIfNeeded();
                    workingImportSettings.TargetColumns = nextColumns;
                    workingImportSettings.TargetRows = nextRows;
                    workingImportSettings.FitMode = nextFitMode;
                    if (!Mathf.Approximately(currentBoardFill, nextBoardFill))
                    {
                        workingImportSettings.BoardFill = nextBoardFill;
                    }
                    workingImportSettings.ImageScale = nextImageScale;
                    workingImportSettings.AlphaThreshold = nextAlpha;
                    workingImportSettings.CropTransparentBorders = nextCrop;
                    EnsureEditorDefaults();

                    var scaleChanged = !Mathf.Approximately(currentImageScale, workingImportSettings.ImageScale);
                    if (sourceImage != null && scaleChanged)
                    {
                        ApplySourceImageScaleToImportResolution(reimportGrid: true);
                    }
                    else
                    {
                        var shouldReimportSourceImage = sourceImage != null
                            && (currentColumns != workingImportSettings.TargetColumns
                            || currentRows != workingImportSettings.TargetRows
                            || currentFitMode != workingImportSettings.FitMode
                            || !Mathf.Approximately(currentAlphaThreshold, workingImportSettings.AlphaThreshold)
                            || currentCropTransparentBorders != workingImportSettings.CropTransparentBorders);

                        if (shouldReimportSourceImage)
                        {
                            ImportImageToGrid();
                        }
                        else
                        {
                            ApplyGridResize(new Vector2Int(workingImportSettings.TargetColumns, workingImportSettings.TargetRows));
                        }
                    }
                }

                if (selectedBlockData != null)
                {
                    var hasValidBlockPrefab = selectedBlockData.TryGetBlockPrefab(out _);
                    var hasMissingBlockPrefabReference = HasMissingBlockPrefabReference(selectedBlockData);

                    if (!hasValidBlockPrefab)
                    {
                        var assetPath = AssetDatabase.GetAssetPath(selectedBlockData);
                        var isDefaultBlockData = string.Equals(assetPath, DefaultBlockDataAssetPath, StringComparison.OrdinalIgnoreCase);
                        var helpMessage = hasMissingBlockPrefabReference
                            ? isDefaultBlockData
                                ? "The default Block Data asset references a missing Block Prefab. The editor will restore it automatically when the default prefab is available."
                                : "This Block Data asset references a missing Block Prefab. Reassign the prefab on the asset to restore imports and preview data."
                            : "Assign a Block Prefab on the selected Block Data asset to preview spacing and block scale.";
                        EditorGUILayout.HelpBox(helpMessage, hasMissingBlockPrefabReference ? MessageType.Warning : MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Assign a Block Data asset to control block prefab, spacing, and preview offset.", MessageType.Info);
                }

                EditorGUILayout.LabelField($"Working Grid Size: {workingGridSize.x} x {workingGridSize.y}", EditorStyles.miniLabel);
                if (sourceImage != null)
                {
                    var scaledSourceResolution = EstimateScaledSourceResolution(sourceImage, workingImportSettings.ImageScale);
                    EditorGUILayout.LabelField($"Source Resolution: {sourceImage.width} x {sourceImage.height}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Target Grid Resolution: {workingImportSettings.TargetColumns} x {workingImportSettings.TargetRows}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Scale Preview Resolution: {scaledSourceResolution.x} x {scaledSourceResolution.y}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"Smart Fit Board Fill: {workingImportSettings.BoardFill:F2}", EditorStyles.miniLabel);
                    if (selectedLevelIndex >= 0)
                    {
                        var boardFillScopeLabel = currentBoardFillOverridden
                            ? "Board Fill currently overrides the default for this level. Save Level persists it."
                            : "Board Fill is using the default 0.63 for this level. Moving the slider creates a per-level override when you click Save Level.";
                        EditorGUILayout.LabelField(boardFillScopeLabel, EditorStyles.miniLabel);
                    }
                    EditorGUILayout.LabelField("Columns and rows can be edited directly. Changing Detail Scale also updates the sampled resolution and regenerates ammo from the new pixel count.", EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField("Lower Board Fill values shrink the generated board parent for dense images so the art stays away from the conveyor border.", EditorStyles.wordWrappedMiniLabel);
                    DrawSourceImagePreview();
                }
                else
                {
                    if (selectedLevelIndex >= 0)
                    {
                        var boardFillScopeLabel = currentBoardFillOverridden
                            ? "Board Fill currently overrides the default for this level. Save Level persists it."
                            : "Board Fill is using the default 0.63 for this level. Moving the slider creates a per-level override when you click Save Level.";
                        EditorGUILayout.LabelField(boardFillScopeLabel, EditorStyles.miniLabel);
                    }
                    EditorGUILayout.LabelField("Columns and rows define the editable grid resolution.", EditorStyles.wordWrappedMiniLabel);
                }

                var totalTargetCells = Mathf.Max(1, workingImportSettings.TargetColumns * workingImportSettings.TargetRows);
                if (totalTargetCells > HighResolutionCellWarningThreshold)
                {
                    EditorGUILayout.HelpBox(
                        "High grid resolutions create one pig cell per imported pixel and will inflate ammo counts quickly. Prefer explicit targets such as 16x16, 24x24, 32x32, or 48x48 unless you need more detail.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawSourceImagePreview()
        {
            if (sourceImage == null)
            {
                return;
            }

            var previewRect = GUILayoutUtility.GetRect(96f, 96f, GUILayout.Width(96f), GUILayout.Height(96f));
            EditorGUI.DrawPreviewTexture(previewRect, sourceImage, null, ScaleMode.ScaleToFit);
        }

        private void DrawPlaceableTools()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);

                if (selectedDatabase == null)
                {
                    EditorGUILayout.HelpBox("Assign a level database to paint pig colors.", MessageType.Info);
                    return;
                }

                DrawColorPalette();

                using (new EditorGUILayout.HorizontalScope())
                {
                    var swatchRect = GUILayoutUtility.GetRect(24f, 18f, GUILayout.Width(24f));
                    EditorGUI.DrawRect(swatchRect, GetPaletteColor(selectedBrushColor));
                    EditorGUILayout.LabelField(
                        selectedBrushColor == PigColor.None
                            ? "Selected Brush: Empty"
                            : $"Selected Brush: {selectedBrushColor}",
                        EditorStyles.miniLabel);
                }

            EditorGUILayout.LabelField("Grid painting uses direct block colors. The database only resolves the matching 1x1 block definition.", EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(selectedBrushColor == PigColor.None))
                    {
                        if (GUILayout.Button($"Fill {selectedBrushColor}", GUILayout.Height(20f)))
                        {
                            FillGridWithBrush(selectedBrushColor);
                        }
                    }

                    using (new EditorGUI.DisabledScope(selectedBrushColor == PigColor.None))
                    {
                        if (GUILayout.Button($"Fill Empty With {selectedBrushColor}", GUILayout.Height(20f)))
                        {
                            FillEmptyGridWithBrush(selectedBrushColor);
                        }
                    }

                    if (GUILayout.Button("Clear All", GUILayout.Height(20f)))
                    {
                        workingPlacedObjects.Clear();
                        InvalidateGridPreviewCache();
                        statusMessage = "Grid cleared.";
                    }
                }
            }
        }

        private void DrawColorPalette()
        {
            var paletteColors = GetPaletteBrushColors();
            const int paletteColumns = 4;
            for (int startIndex = 0; startIndex < paletteColors.Count; startIndex += paletteColumns)
            {
                DrawColorPaletteRow(paletteColors, startIndex, Mathf.Min(paletteColumns, paletteColors.Count - startIndex));
            }
        }

        private void DrawColorPaletteRow(IReadOnlyList<PigColor> paletteColors, int startIndex, int length)
        {
            if (length <= 0)
            {
                return;
            }

            const float buttonHeight = 22f;

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < length; i++)
                {
                    var color = paletteColors[startIndex + i];
                    var previousBackground = GUI.backgroundColor;
                    GUI.backgroundColor = color == selectedBrushColor
                        ? new Color(0.8f, 0.95f, 1f)
                        : Color.white;

                    var rect = GUILayoutUtility.GetRect(72f, buttonHeight, GUILayout.ExpandWidth(true));
                    if (GUI.Button(rect, GetPaletteLabel(color), EditorStyles.miniButton))
                    {
                        selectedBrushColor = color;
                    }

                    var colorStripRect = new Rect(rect.x + 1f, rect.yMax - 3f, rect.width - 2f, 2f);
                    EditorGUI.DrawRect(colorStripRect, GetPaletteColor(color));

                    GUI.backgroundColor = previousBackground;
                }
            }
        }

        private List<PigColor> GetPaletteBrushColors()
        {
            var paletteColors = new List<PigColor> { PigColor.None };
            var sourceEntries = workingImportSettings?.PaletteEntries;

            if (sourceEntries != null && sourceEntries.Count > 0)
            {
                for (int i = 0; i < sourceEntries.Count; i++)
                {
                    var entry = sourceEntries[i];
                    if (entry == null
                        || !entry.Enabled
                        || entry.Color == PigColor.None
                        || paletteColors.Contains(entry.Color))
                    {
                        continue;
                    }

                    paletteColors.Add(entry.Color);
                }
            }

            var defaults = PigColorPaletteUtility.GetDefaultBrushColors();
            for (int i = 0; i < defaults.Count; i++)
            {
                var color = defaults[i];
                if (!paletteColors.Contains(color))
                {
                    paletteColors.Add(color);
                }
            }

            return paletteColors;
        }

        private static string GetPaletteLabel(PigColor color)
        {
            return color == PigColor.None ? "Empty" : color.ToString();
        }

        private void DrawPigQueueTools()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var holdingContainerCount = GetHoldingContainerCount();
                const float swapPanelWidth = 460f;
                const float panelSpacing = 12f;
                var previewColumnWidth = Mathf.Max(360f, position.width - swapPanelWidth - 96f);

                EditorGUILayout.LabelField("Pig Queue", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Holding_Container defines the waiting slots. Deck_Container is the aligned queue parent.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("This level drives how many waiting slots stay active. The scene is updated from this value when the level is loaded or saved.", EditorStyles.wordWrappedMiniLabel);

                EditorGUI.BeginChangeCheck();
                var nextHoldingContainerCount = EditorGUILayout.IntSlider("Holding Containers", holdingContainerCount, MinHoldingContainerCount, MaxHoldingContainerCount);
                if (EditorGUI.EndChangeCheck())
                {
                    SetHoldingContainerCount(nextHoldingContainerCount, true);
                    holdingContainerCount = GetHoldingContainerCount();
                }

                DrawPigQueueGenerationSettings();

                if (workingPigQueue.Count == 0)
                {
                    EditorGUILayout.HelpBox("Pig queue is empty. Use Refresh in Deck Preview to regenerate it from the current grid.", MessageType.Info);
                }
                else
                {
                    NormalizeWorkingPigQueueSlots();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(swapPanelWidth)))
                    {
                        DrawSelectedPigQueueSwapPanel();
                    }

                    GUILayout.Space(panelSpacing);

                    using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(previewColumnWidth), GUILayout.ExpandWidth(true)))
                    {
                        DrawPigQueueInfoBar(previewColumnWidth);
                        EditorGUILayout.Space(4f);
                        DrawPigQueueLanePreview(holdingContainerCount, previewColumnWidth);
                        EditorGUILayout.LabelField(
                            "Left drag a deck card to reorder arrival order. Click cards to select same-color pigs for ammo swap.",
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        private void DrawPigQueueGenerationSettings()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Auto Generation", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "Ammo Range sets the minimum and maximum ammo a single pig can carry.\n"
                + "Pigs / Color sets the allowed pig-count range for each color.\n"
                + "Calculation: required total ammo is taken from the painted cells for that color, snapped up to the ammo step, then the editor searches for a pig count and ammo split that fits both ranges as closely as possible.",
                MessageType.Info);
            var ammoStep = GetAmmoStep();

            var autoSliderMax = GetAutomaticAmmoSliderMaximum(ammoStep, out var largestColorCellCount, out var coloredPigTypeCount);
            var autoRangeChanged = ApplyAutomaticAmmoSliderBounds(ammoStep, ammoStep, autoSliderMax);
            var currentMinAmmo = GetMinimumPigAmmo();
            var currentMaxAmmo = GetMaximumPigAmmo();
            var currentMinimumPigsPerColor = GetMinimumPigsPerColor();
            var currentMaximumPigsPerColor = GetMaximumPigsPerColor();
            var autoPigCountSliderMax = GetAutomaticPigCountSliderMaximum(ammoStep);
            var shouldRegenerateWorkingPigQueue = autoRangeChanged;

            float nextMinAmmo = currentMinAmmo;
            float nextMaxAmmo = currentMaxAmmo;
            using (var rangeChangeScope = new EditorGUI.ChangeCheckScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Ammo Range");
                    EditorGUILayout.MinMaxSlider(ref nextMinAmmo, ref nextMaxAmmo, ammoStep, autoSliderMax);
                }

                if (rangeChangeScope.changed)
                {
                    var resolvedNextMinAmmo = SnapAmmoRangeMinimum(Mathf.RoundToInt(nextMinAmmo), ammoStep, ammoStep, autoSliderMax);
                    var resolvedNextMaxAmmo = SnapAmmoRangeMaximum(Mathf.RoundToInt(nextMaxAmmo), ammoStep, resolvedNextMinAmmo, autoSliderMax);
                    ApplyPigQueueGenerationSettings(resolvedNextMinAmmo, resolvedNextMaxAmmo, ammoStep);
                    shouldRegenerateWorkingPigQueue = true;
                }
            }

            float nextMinimumPigsPerColor = currentMinimumPigsPerColor;
            float nextMaximumPigsPerColor = currentMaximumPigsPerColor;
            using (var pigCountRangeChangeScope = new EditorGUI.ChangeCheckScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Pigs / Color");
                    EditorGUILayout.MinMaxSlider(ref nextMinimumPigsPerColor, ref nextMaximumPigsPerColor, 1f, autoPigCountSliderMax);
                }

                if (pigCountRangeChangeScope.changed)
                {
                    var resolvedMinimumPigsPerColor = Mathf.Clamp(Mathf.RoundToInt(nextMinimumPigsPerColor), 1, autoPigCountSliderMax);
                    var resolvedMaximumPigsPerColor = Mathf.Clamp(Mathf.RoundToInt(nextMaximumPigsPerColor), resolvedMinimumPigsPerColor, autoPigCountSliderMax);
                    ApplyPigCountGenerationSettings(resolvedMinimumPigsPerColor, resolvedMaximumPigsPerColor);
                    shouldRegenerateWorkingPigQueue = true;
                }
            }

            if (shouldRegenerateWorkingPigQueue)
            {
                RegenerateWorkingPigQueue();
            }

            EditorGUILayout.LabelField(
                $"Selected Range: {GetMinimumPigAmmo()} - {GetMaximumPigAmmo()}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"Selected Pig Range: {GetMinimumPigsPerColor()} - {GetMaximumPigsPerColor()}",
                EditorStyles.miniLabel);

            if (coloredPigTypeCount > 0)
            {
                EditorGUILayout.LabelField(
                    $"Auto Slider Max: {autoSliderMax} (largest color requires {largestColorCellCount} cells).",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"Auto Slider Max: {autoSliderMax}. Paint pigs or import a source image to derive the range from the grid.",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                $"Resolved Settings: pigs/color {GetMinimumPigsPerColor()}-{GetMaximumPigsPerColor()} | ammo {GetMinimumPigAmmo()}-{GetMaximumPigAmmo()}",
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(selectedDatabase == null))
                {
                    if (GUILayout.Button("Regenerate Queue", GUILayout.Height(20f)))
                    {
                        RegenerateWorkingPigQueue();
                    }
                }

                using (new EditorGUI.DisabledScope(selectedDatabase == null))
                {
                    if (GUILayout.Button("Validate", GUILayout.Height(20f)))
                    {
                        ValidateWorkingGuaranteedCompletion();
                    }
                }
            }

            EditorGUILayout.HelpBox(pigValidationMessage, pigValidationMessageType);
        }

        private void DrawGridSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Cell Matrix", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Left click paints the selected pig color. Right click clears the cell.", MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Zoom", GUILayout.Width(42f));
                    gridZoomScale = EditorGUILayout.Slider(gridZoomScale, MinGridZoom, MaxGridZoom);

                    if (GUILayout.Button("Reset", GUILayout.Width(56f)))
                    {
                        gridZoomScale = DefaultGridZoom;
                    }
                }

                EditorGUILayout.LabelField("Ctrl + Mouse Wheel zooms the grid cells.", EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);

                var width = Mathf.Max(1, workingGridSize.x);
                var height = Mathf.Max(1, workingGridSize.y);
                var visibleGridWidth = Mathf.Max(1f, position.width - 84f);
                var baseCellSize = Mathf.Clamp(Mathf.Floor(visibleGridWidth / Mathf.Max(1, width)), 1f, 42f);
                var cellSize = Mathf.Max(1f, baseCellSize * gridZoomScale);
                var contentWidth = width * cellSize;
                var contentHeight = height * cellSize;
                var desiredGridWidth = contentWidth + GridPadding * 2f;
                var desiredGridHeight = contentHeight + GridPadding * 2f;
                var gridRectWidth = Mathf.Max(desiredGridWidth, visibleGridWidth);
                var gridRect = GUILayoutUtility.GetRect(
                    gridRectWidth,
                    desiredGridHeight,
                    GUILayout.ExpandWidth(true));
                var origin = new Vector2(
                    gridRect.x + Mathf.Max(GridPadding, (gridRect.width - contentWidth) * 0.5f),
                    gridRect.y + GridPadding);
                var contentRect = new Rect(origin.x, origin.y, contentWidth, contentHeight);

                EditorGUI.DrawRect(contentRect, new Color(0f, 0f, 0f, 0.12f));
                EnsureGridPreviewCache(width, height);

                if (ShouldUsePreviewGridMode(width, height))
                {
                    DrawPreviewGrid(contentRect, width, height, cellSize);
                    EditorGUILayout.LabelField("Large grid preview mode is active for performance.", EditorStyles.miniLabel);
                    return;
                }

                for (int yView = 0; yView < height; yView++)
                {
                    var gridY = (height - 1) - yView;
                    for (int x = 0; x < width; x++)
                    {
                        var rect = new Rect(origin.x + x * cellSize, origin.y + yView * cellSize, cellSize, cellSize);
                        DrawCell(rect, new Vector2Int(x, gridY), GetCachedPlacementIndex(x, gridY), IsCachedRootCell(x, gridY));
                    }
                }
            }
        }

        private void DrawTestGenerationSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Test Generation", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Current grid and deck are spawned into a temporary Temp object in the open scene. Each run clears the previous temp setup and the editor window removes it again on close.",
                    EditorStyles.wordWrappedMiniLabel);

                var canGenerate = selectedDatabase != null
                    && ((workingPlacedObjects != null && workingPlacedObjects.Count > 0)
                        || (workingPigQueue != null && workingPigQueue.Count > 0));

                using (new EditorGUI.DisabledScope(!canGenerate))
                {
                    if (GUILayout.Button("Test Generation", GUILayout.Height(24f)))
                    {
                        GenerateTemporaryTestLevel();
                        GUI.FocusControl(null);
                    }
                }

                if (!canGenerate)
                {
                    EditorGUILayout.LabelField(
                        selectedDatabase == null
                            ? "Assign a level database before generating a temporary test level."
                            : "Paint cells or build a pig queue before generating a temporary test level.",
                        EditorStyles.miniLabel);
                }
            }
        }

        private void GenerateTemporaryTestLevel()
        {
            TemporaryLevelSceneService.CleanupTemporaryArtifacts(
                TemporaryTestLevelRootName,
                TemporaryTestBoardRootName,
                TemporaryTestDeckRootName);

            if (selectedDatabase == null)
            {
                statusMessage = "Assign a level database before generating a temporary test level.";
                return;
            }

            selectedDatabase.EnsureConsistency();
            if (!TemporaryLevelSceneService.TryResolveGenerationContext(
                    out var previewTheme,
                    out var tempParent,
                    out var sceneContext,
                    out var contextMessage))
            {
                statusMessage = contextMessage;
                return;
            }

            if (workingPigQueue == null || workingPigQueue.Count == 0)
            {
                RegenerateWorkingPigQueue();
            }

            var tempRoot = TemporaryLevelSceneService.CreateTemporaryRoot(tempParent, TemporaryTestLevelRootName);
            if (!TemporaryLevelSceneService.TryInstantiateEnvironment(
                    tempRoot.transform,
                    previewTheme,
                    out var environment,
                    out contextMessage))
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
                statusMessage = contextMessage;
                return;
            }

            var generatedBlockCount = 0;
            var generatedPigCount = 0;
            var skippedPigColors = new HashSet<PigColor>();

            var boardRoot = TemporaryLevelSceneService.CreateTemporaryChildRoot(
                tempRoot.transform,
                TemporaryTestBoardRootName,
                environment != null ? environment.BlockContainer : null);
            var deckRoot = TemporaryLevelSceneService.CreateTemporaryChildRoot(
                tempRoot.transform,
                TemporaryTestDeckRootName,
                environment != null ? environment.DeckContainer : null);

            if (sceneContext != null)
            {
                LevelPreviewService.ClearPreview(sceneContext, previewTheme);
            }

            if (boardRoot != null)
            {
                generatedBlockCount = GenerateTemporaryBoard(boardRoot, environment);
            }

            if (deckRoot != null)
            {
                generatedPigCount = GenerateTemporaryDeck(deckRoot, environment, skippedPigColors);
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Repaint();

            var suffix = skippedPigColors.Count > 0
                ? $" Missing pig prefabs: {string.Join(", ", skippedPigColors)}."
                : string.Empty;
            statusMessage = $"Temporary test level generated under {TemporaryTestLevelRootName}. Blocks: {generatedBlockCount}, Pigs: {generatedPigCount}.{suffix}";
        }

        private int GenerateTemporaryBoard(Transform boardRoot, EnvironmentContext environment)
        {
            if (boardRoot == null || workingPlacedObjects == null || workingPlacedObjects.Count == 0)
            {
                return 0;
            }

            var blockPrefab = ResolveTemporaryBlockPrefab(environment);
            if (blockPrefab == null)
            {
                return 0;
            }

            var cellSpacing = selectedBlockData != null ? selectedBlockData.CellSpacing : workingImportSettings.CellSpacing;
            var verticalOffset = selectedBlockData != null ? selectedBlockData.VerticalOffset : workingImportSettings.VerticalOffset;
            var width = Mathf.Max(1, workingGridSize.x);
            var height = Mathf.Max(1, workingGridSize.y);
            var layout = BoardFitUtility.ResolvePlacedObjectLayout(
                environment,
                boardRoot,
                workingPlacedObjects,
                selectedDatabase,
                workingGridSize,
                cellSpacing,
                preserveFullGridBounds: true,
                boardFill: workingImportSettings.BoardFill);
            var baseBoardScale = boardRoot.localScale;
            boardRoot.localScale = Vector3.Scale(baseBoardScale, Vector3.one * layout.RootScale);
            var generatedBlockCount = 0;

            for (int placementIndex = 0; placementIndex < workingPlacedObjects.Count; placementIndex++)
            {
                var placedObject = workingPlacedObjects[placementIndex];
                var definition = selectedDatabase.FindPlaceable(placedObject);
                if (definition == null)
                {
                    continue;
                }

                var size = definition.GridSize;
                var blockColor = definition.Kind == PlaceableKind.Block ? definition.Color : PigColor.None;
                var fallbackColor = definition.Kind == PlaceableKind.Block
                    ? ResolvePlacedObjectPreviewColor(definition, placedObject)
                    : definition.EditorColor;

                for (int offsetX = 0; offsetX < size.x; offsetX++)
                {
                    for (int offsetY = 0; offsetY < size.y; offsetY++)
                    {
                        var gridX = placedObject.Origin.x + offsetX;
                        var gridY = placedObject.Origin.y + offsetY;
                        if (gridX < 0 || gridX >= width || gridY < 0 || gridY >= height)
                        {
                            continue;
                        }

                        var instance = PrefabUtility.InstantiatePrefab(blockPrefab, boardRoot) as GameObject;
                        if (instance == null)
                        {
                            continue;
                        }

                        TemporaryLevelSceneService.ApplyTemporaryHideFlagsRecursive(instance);
                        instance.name = $"Block_{gridX}_{gridY}_{definition.DisplayName}";
                        if (selectedBlockData == null)
                        {
                            instance.transform.localScale = workingImportSettings.BlockLocalScale;
                        }

                        instance.transform.localPosition = new Vector3(
                            layout.GetLocalX(gridX),
                            verticalOffset,
                            layout.GetLocalZFromBottom(gridY));
                        instance.transform.localRotation = Quaternion.identity;
                        ApplyTemporaryColor(
                            instance,
                            blockColor,
                            fallbackColor,
                            ResolvePlacedObjectToneIndex(placedObject, blockColor));
                        generatedBlockCount++;
                    }
                }
            }

            return generatedBlockCount;
        }

        private int GenerateTemporaryDeck(Transform deckRoot, EnvironmentContext environment, ISet<PigColor> skippedPigColors)
        {
            if (deckRoot == null || workingPigQueue == null || workingPigQueue.Count == 0)
            {
                return 0;
            }

            var holdingContainerCount = Mathf.Max(1, GetHoldingContainerCount());
            var laneEntryIndices = new List<int>[holdingContainerCount];
            for (int i = 0; i < holdingContainerCount; i++)
            {
                laneEntryIndices[i] = new List<int>();
            }

            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                var entry = workingPigQueue[i];
                var slotIndex = Mathf.Clamp(entry.SlotIndex, 0, holdingContainerCount - 1);
                laneEntryIndices[slotIndex].Add(i);
            }

            var lanePositions = ResolveTemporaryDeckLanePositions(deckRoot, environment, holdingContainerCount, out var laneSpacing);
            var depthSpacing = Mathf.Max(0.9f, laneSpacing);
            var depthDirection = ResolveTemporaryDeckDepthDirection(deckRoot, environment);
            var generatedPigCount = 0;

            for (int laneIndex = 0; laneIndex < holdingContainerCount; laneIndex++)
            {
                for (int depthIndex = 0; depthIndex < laneEntryIndices[laneIndex].Count; depthIndex++)
                {
                    var queueIndex = laneEntryIndices[laneIndex][depthIndex];
                    var entry = workingPigQueue[queueIndex];
                    var slot = environment != null
                        ? environment.GetHoldingSlot(laneIndex, activeOnly: true) ?? environment.GetHoldingSlot(laneIndex, activeOnly: false)
                        : null;
                    var pigPrefab = ResolveTemporaryPigPrefab(entry.Color, preferGeneric: !Application.isPlaying);
                    var fallbackColor = ResolveBrushPreviewColor(entry.Color);
                    if (!TryInstantiateTemporaryPig(
                            deckRoot,
                            pigPrefab,
                            queueIndex,
                            entry,
                            fallbackColor,
                            lanePositions[laneIndex],
                            depthIndex * depthSpacing * depthDirection,
                            slot,
                            out var instance))
                    {
                        var genericPigPrefab = ResolveTemporaryGenericPigPrefab();
                        if (genericPigPrefab == null
                            || genericPigPrefab == pigPrefab
                            || !TryInstantiateTemporaryPig(
                                deckRoot,
                                genericPigPrefab,
                                queueIndex,
                                entry,
                                fallbackColor,
                                lanePositions[laneIndex],
                                depthIndex * depthSpacing * depthDirection,
                                slot,
                                out instance))
                        {
                            skippedPigColors?.Add(entry.Color);
                            continue;
                        }
                    }

                    generatedPigCount++;
                }
            }

            return generatedPigCount;
        }

        private float[] ResolveTemporaryDeckLanePositions(
            Transform deckRoot,
            EnvironmentContext environment,
            int holdingContainerCount,
            out float laneSpacing)
        {
            var resolvedPositions = new float[holdingContainerCount];
            var validLaneCount = 0;
            var previousPosition = 0f;
            var totalSpacing = 0f;
            var spacingSamples = 0;

            for (int laneIndex = 0; laneIndex < holdingContainerCount; laneIndex++)
            {
                var slot = environment != null
                    ? environment.GetHoldingSlot(laneIndex, activeOnly: true) ?? environment.GetHoldingSlot(laneIndex, activeOnly: false)
                    : null;
                if (slot == null)
                {
                    resolvedPositions[laneIndex] = float.NaN;
                    continue;
                }

                var localPosition = deckRoot.InverseTransformPoint(slot.position);
                resolvedPositions[laneIndex] = localPosition.x;
                if (validLaneCount > 0)
                {
                    totalSpacing += Mathf.Abs(localPosition.x - previousPosition);
                    spacingSamples++;
                }

                previousPosition = localPosition.x;
                validLaneCount++;
            }

            laneSpacing = spacingSamples > 0
                ? Mathf.Max(0.6f, totalSpacing / spacingSamples)
                : 1.1f;

            if (validLaneCount == holdingContainerCount)
            {
                return resolvedPositions;
            }

            var halfWidth = (holdingContainerCount - 1) * 0.5f;
            for (int laneIndex = 0; laneIndex < holdingContainerCount; laneIndex++)
            {
                if (float.IsNaN(resolvedPositions[laneIndex]))
                {
                    resolvedPositions[laneIndex] = (laneIndex - halfWidth) * laneSpacing;
                }
            }

            return resolvedPositions;
        }

        private static float ResolveTemporaryDeckDepthDirection(Transform deckRoot, EnvironmentContext environment)
        {
            if (deckRoot == null || environment?.TrayEquipPos == null)
            {
                return -1f;
            }

            var trayLocalPosition = deckRoot.InverseTransformPoint(environment.TrayEquipPos.position);
            return trayLocalPosition.z >= 0f ? -1f : 1f;
        }

        private GameObject ResolveTemporaryBlockPrefab(EnvironmentContext environment)
        {
            if (selectedBlockData != null && selectedBlockData.BlockPrefab != null)
            {
                return selectedBlockData.BlockPrefab;
            }

            if (environment?.DefaultBlockData != null && environment.DefaultBlockData.BlockPrefab != null)
            {
                return environment.DefaultBlockData.BlockPrefab;
            }

            return LoadDefaultBlockPrefab();
        }

        private GameObject ResolveTemporaryPigPrefab(PigColor color, bool preferGeneric)
        {
            var genericPigPrefab = ResolveTemporaryGenericPigPrefab();
            if (preferGeneric && genericPigPrefab != null)
            {
                return genericPigPrefab;
            }

            return genericPigPrefab;
        }

        private static GameObject ResolveTemporaryGenericPigPrefab()
        {
            var defaultPigPrefab = LoadDefaultPigPrefab();
            return IsValidTemporaryPigPrefab(defaultPigPrefab)
                ? defaultPigPrefab
                : null;
        }

        private static bool IsValidTemporaryPigPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            return (prefab.GetComponentInChildren<PigController>(true) != null
                    && prefab.GetComponentInChildren<PigView>(true) != null)
                || (prefab.GetComponentInChildren<AtlasColorTarget>(true) != null
                    && prefab.GetComponentInChildren<TMP_Text>(true) != null);
        }

        private static bool TryInstantiateTemporaryPig(
            Transform deckRoot,
            GameObject pigPrefab,
            int queueIndex,
            PigQueueEntry entry,
            Color fallbackColor,
            float lanePosition,
            float depthPosition,
            Transform slotReference,
            out GameObject instance)
        {
            instance = null;
            if (deckRoot == null || pigPrefab == null)
            {
                return false;
            }

            instance = PrefabUtility.InstantiatePrefab(pigPrefab, deckRoot) as GameObject;
            if (instance == null)
            {
                return false;
            }

            TemporaryLevelSceneService.ApplyTemporaryHideFlagsRecursive(instance);
            instance.name = $"Pig_{queueIndex + 1}_{entry.Color}_{entry.Ammo}";

            var localPosition = instance.transform.localPosition;
            localPosition.x = lanePosition;
            localPosition.z = depthPosition;
            instance.transform.localPosition = localPosition;
            if (slotReference != null)
            {
                var facingDirection = -slotReference.forward;
                if (facingDirection.sqrMagnitude > 0.001f)
                {
                    instance.transform.rotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
                }
                else
                {
                    instance.transform.rotation = slotReference.rotation;
                }
            }

            if (TryConfigureTemporaryPig(instance, entry, fallbackColor))
            {
                return true;
            }

            UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            return false;
        }

        private static bool TryConfigureTemporaryPig(GameObject instance, PigQueueEntry entry, Color fallbackColor)
        {
            if (instance == null)
            {
                return false;
            }

            var pigController = instance.GetComponent<PigController>();
            if (pigController != null)
            {
                pigController.ConfigurePig(entry.Color, entry.Ammo, entry.Direction);
                pigController.SetTrayVisible(false);
                pigController.SetQueued(false, snapImmediately: false);
                pigController.ClearWaitingAnchor();
                ApplyTemporaryColor(instance, entry.Color, fallbackColor);
                instance.GetComponent<PigView>()?.ApplyEditorPreviewFacingCorrection();
                return true;
            }

            var atlasTarget = instance.GetComponentInChildren<AtlasColorTarget>(true);
            var ammoText = instance.GetComponentInChildren<TMP_Text>(true);
            var trayRoot = ResolveTemporaryTrayRoot(instance.transform);
            if (atlasTarget == null && ammoText == null)
            {
                return false;
            }

            ApplyTemporaryColor(instance, entry.Color, fallbackColor);
            instance.GetComponent<PigView>()?.ApplyEditorPreviewFacingCorrection();

            if (ammoText != null)
            {
                ammoText.text = entry.Ammo > 0 ? entry.Ammo.ToString() : string.Empty;
            }

            if (trayRoot != null)
            {
                trayRoot.gameObject.SetActive(false);
            }

            return true;
        }

        private static Transform ResolveTemporaryTrayRoot(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            var directChild = root.Find("Tray");
            if (directChild != null)
            {
                return directChild;
            }

            var children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i] != root && children[i].name == "Tray")
                {
                    return children[i];
                }
            }

            return null;
        }

        private static void ApplyTemporaryColor(GameObject target, PigColor pigColor, Color fallbackColor, int toneIndex = -1)
        {
            if (target == null)
            {
                return;
            }

            var atlasTarget = target.GetComponentInChildren<AtlasColorTarget>(true);
            if (atlasTarget != null && pigColor != PigColor.None)
            {
                if (toneIndex >= 0)
                {
                    atlasTarget.SetColor(pigColor, toneIndex);
                }
                else
                {
                    atlasTarget.SetColor(pigColor);
                }
#if UNITY_EDITOR
                EditorUtility.SetDirty(atlasTarget);
#endif
                return;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.GetComponent<TMP_Text>() != null)
                {
                    continue;
                }

                var block = new MaterialPropertyBlock();
                block.Clear();
                if (renderer.sharedMaterial != null)
                {
                    if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                    {
                        block.SetColor("_BaseColor", fallbackColor);
                    }

                    if (renderer.sharedMaterial.HasProperty("_Color"))
                    {
                        block.SetColor("_Color", fallbackColor);
                    }
                }

                renderer.SetPropertyBlock(block);
            }
        }

        private void DrawCell(Rect rect, Vector2Int gridPosition, int placementIndex, bool isRoot)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.18f));

            var fillRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            var fillColor = new Color(1f, 1f, 1f, 0.08f);
            string glyph = null;

            if (TryResolvePlacedObject(placementIndex, out var placedObject, out var definition))
            {
                fillColor = ResolvePlacedObjectPreviewColor(
                    definition,
                    placedObject,
                    definition.Kind == PlaceableKind.Obstacle ? 0.92f : 0.82f);
                glyph = isRoot ? definition.ResolveEditorGlyph() : null;
            }

            EditorGUI.DrawRect(fillRect, fillColor);

            Handles.color = new Color(0f, 0f, 0f, 0.25f);
            Handles.DrawAAPolyLine(
                1f,
                new Vector3(rect.xMin, rect.yMin),
                new Vector3(rect.xMax, rect.yMin),
                new Vector3(rect.xMax, rect.yMax),
                new Vector3(rect.xMin, rect.yMax),
                new Vector3(rect.xMin, rect.yMin));

            if (!string.IsNullOrWhiteSpace(glyph))
            {
                GUI.Label(rect, glyph, CellGlyphStyle);
            }

            var current = Event.current;
            if (!rect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                TryPlaceBrush(gridPosition, isDrag: false);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0)
            {
                TryPlaceBrush(gridPosition, isDrag: true);
                current.Use();
            }
            else if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 1)
            {
                RemovePlacedObjectAt(gridPosition);
                current.Use();
            }
        }

        private bool ShouldUsePreviewGridMode(int width, int height)
        {
            return width > 96
                || height > 96
                || (width * height) > DetailedGridCellThreshold;
        }

        private void DrawPreviewGrid(Rect contentRect, int width, int height, float cellSize)
        {
            if (gridPreviewTexture != null)
            {
                GUI.DrawTexture(contentRect, gridPreviewTexture, ScaleMode.StretchToFill, false);
            }

            var current = Event.current;
            if (!TryGetGridPositionFromMouse(contentRect, width, height, current.mousePosition, out var gridPosition))
            {
                return;
            }

            if (cellSize >= 6f)
            {
                var hoverRect = GetCellRectFromGridPosition(contentRect, width, height, gridPosition);
                Handles.color = new Color(1f, 1f, 1f, 0.45f);
                Handles.DrawAAPolyLine(
                    1.5f,
                    new Vector3(hoverRect.xMin, hoverRect.yMin),
                    new Vector3(hoverRect.xMax, hoverRect.yMin),
                    new Vector3(hoverRect.xMax, hoverRect.yMax),
                    new Vector3(hoverRect.xMin, hoverRect.yMax),
                    new Vector3(hoverRect.xMin, hoverRect.yMin));
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                TryPlaceBrush(gridPosition, isDrag: false);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0)
            {
                TryPlaceBrush(gridPosition, isDrag: true);
                current.Use();
            }
            else if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 1)
            {
                RemovePlacedObjectAt(gridPosition);
                current.Use();
            }
        }

        private bool TryGetGridPositionFromMouse(Rect contentRect, int width, int height, Vector2 mousePosition, out Vector2Int gridPosition)
        {
            if (!contentRect.Contains(mousePosition))
            {
                gridPosition = default;
                return false;
            }

            var normalizedX = Mathf.Clamp01((mousePosition.x - contentRect.x) / Mathf.Max(1f, contentRect.width));
            var normalizedY = Mathf.Clamp01((mousePosition.y - contentRect.y) / Mathf.Max(1f, contentRect.height));
            var x = Mathf.Clamp(Mathf.FloorToInt(normalizedX * width), 0, width - 1);
            var yView = Mathf.Clamp(Mathf.FloorToInt(normalizedY * height), 0, height - 1);
            gridPosition = new Vector2Int(x, (height - 1) - yView);
            return true;
        }

        private static Rect GetCellRectFromGridPosition(Rect contentRect, int width, int height, Vector2Int gridPosition)
        {
            var cellWidth = contentRect.width / Mathf.Max(1, width);
            var cellHeight = contentRect.height / Mathf.Max(1, height);
            var yView = (height - 1) - gridPosition.y;
            return new Rect(
                contentRect.x + (gridPosition.x * cellWidth),
                contentRect.y + (yView * cellHeight),
                cellWidth,
                cellHeight);
        }

        private void EnsureGridPreviewCache(int width, int height)
        {
            var cellCount = Mathf.Max(1, width * height);
            if (!isGridPreviewDirty
                && cachedGridPreviewSize.x == width
                && cachedGridPreviewSize.y == height
                && gridPreviewPixels != null
                && gridPreviewPixels.Length == cellCount
                && cachedPlacementIndices != null
                && cachedPlacementIndices.Length == cellCount
                && cachedRootFlags != null
                && cachedRootFlags.Length == cellCount)
            {
                return;
            }

            cachedGridPreviewSize = new Vector2Int(width, height);
            gridPreviewPixels = new Color32[cellCount];
            cachedPlacementIndices = new int[cellCount];
            cachedRootFlags = new bool[cellCount];
            cachedPigCellCounts.Clear();

            var emptyColor = new Color32(48, 48, 48, 255);
            for (int i = 0; i < cellCount; i++)
            {
                gridPreviewPixels[i] = emptyColor;
                cachedPlacementIndices[i] = -1;
                cachedRootFlags[i] = false;
            }

            if (selectedDatabase != null)
            {
                for (int placementIndex = 0; placementIndex < workingPlacedObjects.Count; placementIndex++)
                {
                    if (!TryResolvePlacedObject(placementIndex, out var placedObject, out var definition))
                    {
                        continue;
                    }

                    var previewColor = GetPreviewColor(definition, placedObject);
                    var size = definition.GridSize;
                    for (int dx = 0; dx < size.x; dx++)
                    {
                        for (int dy = 0; dy < size.y; dy++)
                        {
                            var x = placedObject.Origin.x + dx;
                            var y = placedObject.Origin.y + dy;
                            if (x < 0 || x >= width || y < 0 || y >= height)
                            {
                                continue;
                            }

                            var cacheIndex = GetGridCacheIndex(x, y, width);
                            cachedPlacementIndices[cacheIndex] = placementIndex;
                            cachedRootFlags[cacheIndex] = dx == 0 && dy == 0;
                            gridPreviewPixels[cacheIndex] = previewColor;

                if (definition.Kind == PlaceableKind.Block && definition.Color != PigColor.None)
                {
                    cachedPigCellCounts[definition.Color] = cachedPigCellCounts.TryGetValue(definition.Color, out var currentCount)
                        ? currentCount + 1
                                    : 1;
                            }
                        }
                    }
                }
            }

            EnsureGridPreviewTexture(width, height);
            gridPreviewTexture.SetPixels32(gridPreviewPixels);
            gridPreviewTexture.Apply(false, false);
            isGridPreviewDirty = false;
        }

        private void EnsureGridPreviewTexture(int width, int height)
        {
            if (gridPreviewTexture != null
                && gridPreviewTexture.width == width
                && gridPreviewTexture.height == height)
            {
                return;
            }

            if (gridPreviewTexture != null)
            {
                DestroyImmediate(gridPreviewTexture);
            }

            gridPreviewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "GridPreview",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static int GetGridCacheIndex(int x, int y, int width)
        {
            return (y * width) + x;
        }

        private int GetCachedPlacementIndex(int x, int y)
        {
            if (cachedPlacementIndices == null
                || x < 0 || y < 0
                || x >= cachedGridPreviewSize.x
                || y >= cachedGridPreviewSize.y)
            {
                return -1;
            }

            return cachedPlacementIndices[GetGridCacheIndex(x, y, cachedGridPreviewSize.x)];
        }

        private bool IsCachedRootCell(int x, int y)
        {
            if (cachedRootFlags == null
                || x < 0 || y < 0
                || x >= cachedGridPreviewSize.x
                || y >= cachedGridPreviewSize.y)
            {
                return false;
            }

            return cachedRootFlags[GetGridCacheIndex(x, y, cachedGridPreviewSize.x)];
        }

        private Color32 GetPreviewColor(PlaceableDefinition definition, PlacedObjectData placedObject)
        {
            return ResolvePlacedObjectPreviewColor(
                definition,
                placedObject,
                definition.Kind == PlaceableKind.Obstacle ? 0.98f : 0.92f);
        }

        private Color ResolvePlacedObjectPreviewColor(
            PlaceableDefinition definition,
            PlacedObjectData? placedObject = null,
            float alpha = 1f)
        {
            if (definition == null)
            {
                return new Color(1f, 1f, 1f, alpha);
            }

            if (definition.Kind != PlaceableKind.Block || definition.Color == PigColor.None)
            {
                var fallbackColor = definition.EditorColor;
                fallbackColor.a = alpha;
                return fallbackColor;
            }

            var toneIndex = placedObject.HasValue
                ? ResolvePlacedObjectToneIndex(placedObject.Value, definition.Color)
                : PigColorAtlasUtility.ResolveDefaultToneIndex(definition.Color);
            var previewColor = PigColorPaletteUtility.GetAtlasPreviewColor(definition.Color, toneIndex);
            previewColor.a = alpha;
            return previewColor;
        }

        private static Color ResolveBrushPreviewColor(PigColor color)
        {
            return color == PigColor.None
                ? new Color(0.16f, 0.16f, 0.16f)
                : PigColorPaletteUtility.GetAtlasPreviewColor(color);
        }

        private void InvalidateGridPreviewCache()
        {
            isGridPreviewDirty = true;
            MarkPigGuaranteeValidationDirty();
        }

        private void EnsurePlacedObjectCapacity(int minimumCapacity)
        {
            if (workingPlacedObjects == null)
            {
                workingPlacedObjects = new List<PlacedObjectData>(minimumCapacity);
                return;
            }

            if (workingPlacedObjects.Capacity < minimumCapacity)
            {
                workingPlacedObjects.Capacity = minimumCapacity;
            }
        }

        private void RemovePlacementAtIndexFast(int placementIndex)
        {
            if (workingPlacedObjects == null
                || placementIndex < 0
                || placementIndex >= workingPlacedObjects.Count)
            {
                return;
            }

            var lastIndex = workingPlacedObjects.Count - 1;
            if (placementIndex != lastIndex)
            {
                workingPlacedObjects[placementIndex] = workingPlacedObjects[lastIndex];
            }

            workingPlacedObjects.RemoveAt(lastIndex);
        }


        private void DrawStatusBox()
        {
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
        }

        private void HandleGridZoomInput()
        {
            var current = Event.current;
            if (current.type != EventType.ScrollWheel || !current.control)
            {
                return;
            }

            var zoomDelta = -current.delta.y * 0.05f;
            gridZoomScale = Mathf.Clamp(gridZoomScale + zoomDelta, MinGridZoom, MaxGridZoom);
            current.Use();
            Repaint();
        }

        private void ApplyDatabaseSelection(LevelDatabase database)
        {
            selectedDatabase = database;
            selectedLevelIndex = -1;
            selectedBrushColor = PigColor.Pink;
            InvalidateGridPreviewCache();

            if (selectedDatabase == null)
            {
                ResetWorkingLevelBuffer();
                statusMessage = "Assign a level database to continue.";
                return;
            }

            selectedDatabase.EnsureConsistency();
            SyncImportPaletteFromDatabase();

            if (selectedDatabase.Levels.Count > 0)
            {
                selectedLevelIndex = 0;
                LoadSelectedLevelIntoBuffer();
            }
            else
            {
                ResetWorkingLevelBuffer();
                statusMessage = "Database selected. Create a level to begin editing.";
            }
        }

        private void ApplyLevelSelection(int nextLevelIndex)
        {
            if (selectedDatabase == null)
            {
                selectedLevelIndex = -1;
                return;
            }

            selectedLevelIndex = Mathf.Clamp(nextLevelIndex, 0, selectedDatabase.Levels.Count - 1);
            LoadSelectedLevelIntoBuffer();
        }

        private void LoadSelectedLevelIntoBuffer()
        {
            var selectedLevel = GetSelectedLevel();
            if (selectedLevel == null)
            {
                ResetWorkingLevelBuffer();
                return;
            }

            var clone = selectedLevel.Clone();
            workingLevelName = clone.LevelName;
            sourceImage = clone.SourceImage;
            selectedBlockData = ResolveWorkingBlockData(clone.BlockData);
            workingImportSettings = clone.ImportSettings;
            pigQueueGenerationSettings = clone.PigQueueGenerationSettings;
            pigQueueGenerationSettings.HoldingSlotCount = ClampHoldingContainerCount(pigQueueGenerationSettings.HoldingSlotCount);
            workingGridSize = clone.GridSize;
            workingPlacedObjects = clone.PlacedObjects;
            workingPigQueue = new List<PigQueueEntry>(clone.PigQueue);
            EnsureEditorDefaults();
            BuildLevelPopupNames();
            SetHoldingContainerCount(pigQueueGenerationSettings.HoldingSlotCount, true);
            NormalizeWorkingPigQueueSlots();
            SyncImportPaletteFromDatabase();
            ClampPlacedObjectsToGrid();
            InvalidateGridPreviewCache();
            if (workingPigQueue.Count > 0 && workingPlacedObjects.Count > 0)
            {
                ValidateWorkingGuaranteedCompletion(updateStatusMessage: false);
            }
            else
            {
                pigValidationMessage = "Pig queue loaded.";
                pigValidationMessageType = MessageType.Info;
            }

            statusMessage = $"Loaded {workingLevelName}.";
            Repaint();
        }

        private void ResetWorkingLevelBuffer()
        {
            workingLevelName = GenerateUniqueLevelName();
            sourceImage = null;
            selectedBlockData = ResolveWorkingBlockData(selectedBlockData);
            workingImportSettings = new ImageImportSettings();
            pigQueueGenerationSettings = new PigQueueGenerationSettings();
            pigQueueGenerationSettings.HoldingSlotCount = MaxHoldingContainerCount;
            workingGridSize = new Vector2Int(workingImportSettings.TargetColumns, workingImportSettings.TargetRows);
            workingPlacedObjects = new List<PlacedObjectData>();
            workingPigQueue = new List<PigQueueEntry>();
            EnsureEditorDefaults();
            BuildLevelPopupNames();
            SetHoldingContainerCount(pigQueueGenerationSettings.HoldingSlotCount, true);
            InvalidateGridPreviewCache();
            pigValidationMessage = "Pig queue will be regenerated from the current grid when needed.";
            pigValidationMessageType = MessageType.Info;
        }

        private void CreateLevelDatabase()
        {
            EnsureFolder(DefaultDatabaseFolder);

            var databasePath = AssetDatabase.GenerateUniqueAssetPath($"{DefaultDatabaseFolder}/{DefaultDatabaseAssetName}.asset");
            var database = CreateInstance<LevelDatabase>();
            database.ResetDefaultPlaceables();
            database.CreateLevel("Level 1");

            AssetDatabase.CreateAsset(database, databasePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = database;
            ApplyDatabaseSelection(database);
            statusMessage = $"Created level database at {databasePath}.";
        }

        private void CreateLevel()
        {
            if (selectedDatabase == null)
            {
                statusMessage = "Assign or create a level database first.";
                return;
            }

            var level = selectedDatabase.CreateLevel(GenerateUniqueLevelName());
            selectedLevelIndex = selectedDatabase.Levels.Count - 1;
            EditorUtility.SetDirty(selectedDatabase);
            AssetDatabase.SaveAssets();
            LoadSelectedLevelIntoBuffer();
            statusMessage = $"Created {level.LevelName}.";
        }

        private void DeleteSelectedLevel()
        {
            if (selectedDatabase == null || selectedLevelIndex < 0 || selectedLevelIndex >= selectedDatabase.Levels.Count)
            {
                statusMessage = "No level selected to delete.";
                return;
            }

            var levelName = selectedDatabase.Levels[selectedLevelIndex].LevelName;
            if (!EditorUtility.DisplayDialog("Delete Level", $"Delete {levelName} from the database?", "Delete", "Cancel"))
            {
                return;
            }

            selectedDatabase.RemoveLevelAt(selectedLevelIndex);
            EditorUtility.SetDirty(selectedDatabase);
            AssetDatabase.SaveAssets();

            if (selectedDatabase.Levels.Count == 0)
            {
                selectedLevelIndex = -1;
                ResetWorkingLevelBuffer();
            }
            else
            {
                selectedLevelIndex = Mathf.Clamp(selectedLevelIndex, 0, selectedDatabase.Levels.Count - 1);
                LoadSelectedLevelIntoBuffer();
            }

            statusMessage = $"Deleted {levelName}.";
        }

        private void SaveSelectedLevel()
        {
            if (selectedDatabase == null)
            {
                statusMessage = "Assign or create a level database first.";
                return;
            }

            if (selectedLevelIndex < 0 || selectedLevelIndex >= selectedDatabase.Levels.Count)
            {
                statusMessage = "Create or select a level before saving.";
                return;
            }

            ValidateWorkingGuaranteedCompletion(updateStatusMessage: false);
            selectedDatabase.SetLevelAt(selectedLevelIndex, CreateWorkingLevelSnapshot());
            EditorUtility.SetDirty(selectedDatabase);
            AssetDatabase.SaveAssets();
            PlayerPrefs.SetInt(CurrentLevelPrefsKey, selectedLevelIndex);
            PlayerPrefs.Save();
            BuildLevelPopupNames();
            CleanupTemporaryTestGeneration();
            statusMessage = $"Saved {workingLevelName}, marked it as the active playtest level, and cleared any temporary test generation.";
        }

        private void CleanupTemporaryTestGeneration()
        {
            TemporaryLevelSceneService.CleanupTemporaryArtifacts(
                TemporaryTestLevelRootName,
                TemporaryTestBoardRootName,
                TemporaryTestDeckRootName);

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Repaint();
        }

        private LevelData CreateWorkingLevelSnapshot()
        {
            NormalizeWorkingPigQueueSlots();

            var snapshot = new LevelData
            {
                LevelName = workingLevelName,
                GridSize = workingGridSize,
                SourceImage = sourceImage,
                BlockData = selectedBlockData,
                ImportSettings = workingImportSettings,
                PigQueueGenerationSettings = pigQueueGenerationSettings,
            };

            snapshot.PlacedObjects.Clear();
            for (int i = 0; i < workingPlacedObjects.Count; i++)
            {
                snapshot.PlacedObjects.Add(workingPlacedObjects[i]);
            }

            snapshot.PigQueue.Clear();
            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                snapshot.PigQueue.Add(workingPigQueue[i]);
            }

            return snapshot;
        }

        private void ApplyGridResize(Vector2Int nextGridSize)
        {
            workingGridSize = ClampGridSize(nextGridSize);
            ClampPlacedObjectsToGrid();
            InvalidateGridPreviewCache();
        }

        private void ClampPlacedObjectsToGrid()
        {
            if (selectedDatabase == null || workingPlacedObjects == null)
            {
                return;
            }

            for (int i = workingPlacedObjects.Count - 1; i >= 0; i--)
            {
                var definition = selectedDatabase.FindPlaceable(workingPlacedObjects[i]);
                if (definition == null || !CanFitPlacement(workingPlacedObjects[i].Origin, definition.GridSize))
                {
                    workingPlacedObjects.RemoveAt(i);
                }
            }
        }

        private static int ResolvePlacedObjectToneIndex(PlacedObjectData placedObject, PigColor pigColor)
        {
            return placedObject.HasVisualToneOverride
                ? placedObject.VisualToneIndex
                : PigColorAtlasUtility.ResolveDefaultToneIndex(pigColor);
        }

        private void PickImageFile()
        {
            var startDirectory = GetInitialImagePickerDirectory();
            var selectedPath = EditorUtility.OpenFilePanel("Select Source Image", startDirectory, "png,jpg,jpeg,bmp,tga");

            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            if (!TryImportSelectedImage(selectedPath, out _, out var importedAssetPath))
            {
                return;
            }

            var importedTexture = LoadTextureAsset(importedAssetPath);
            if (importedTexture == null)
            {
                statusMessage = $"Image was imported but could not be loaded: {importedAssetPath}";
                return;
            }

            SetSourceImage(importedTexture, applyImportedDefaults: true);
            ImportImageToGrid();
            sourceImage = LoadTextureAsset(importedAssetPath) ?? sourceImage;
            Repaint();
        }

        private void ImportImageToGrid()
        {
            if (selectedDatabase == null)
            {
                statusMessage = "Assign a level database before importing.";
                return;
            }

            if (sourceImage == null)
            {
                statusMessage = "Assign a source image before importing.";
                return;
            }

            var sourceImageAssetPath = AssetDatabase.GetAssetPath(sourceImage);
            if (!string.IsNullOrEmpty(sourceImageAssetPath))
            {
                sourceImage = LoadTextureAsset(sourceImageAssetPath) ?? sourceImage;
            }

            if (!ImageImporter.TryImport(sourceImage, workingImportSettings, out var importedGrid, out var error))
            {
                if (!string.IsNullOrEmpty(sourceImageAssetPath))
                {
                    sourceImage = LoadTextureAsset(sourceImageAssetPath) ?? sourceImage;
                }

                statusMessage = error;
                return;
            }

            if (!string.IsNullOrEmpty(sourceImageAssetPath))
            {
                sourceImage = LoadTextureAsset(sourceImageAssetPath) ?? sourceImage;
            }

            workingGridSize = new Vector2Int(importedGrid.GetLength(0), importedGrid.GetLength(1));
            var estimatedPlacementCapacity = importedGrid.GetLength(0) * importedGrid.GetLength(1);
            if (workingPlacedObjects == null)
            {
                workingPlacedObjects = new List<PlacedObjectData>(estimatedPlacementCapacity);
            }
            else
            {
                workingPlacedObjects.Clear();
                EnsurePlacedObjectCapacity(estimatedPlacementCapacity);
            }

            var missingDefinitions = 0;
            for (int x = 0; x < importedGrid.GetLength(0); x++)
            {
                for (int yView = 0; yView < importedGrid.GetLength(1); yView++)
                {
                    var importedCell = importedGrid[x, yView];
                    if (importedCell.Color == PigColor.None)
                    {
                        continue;
                    }

                    if (!selectedDatabase.TryGetDefaultBlockPlaceable(importedCell.Color, out var placeable))
                    {
                        missingDefinitions++;
                        continue;
                    }

                    var yGrid = (importedGrid.GetLength(1) - 1) - yView;
                    workingPlacedObjects.Add(new PlacedObjectData(placeable.Id, new Vector2Int(x, yGrid), importedCell.ToneIndex));
                }
            }

            statusMessage = missingDefinitions > 0
                ? $"Imported {sourceImage.name}. {missingDefinitions} cells were skipped because the database has no 1x1 block definition for that color."
                : $"Imported {sourceImage.name} into {workingGridSize.x}x{workingGridSize.y}.";
            InvalidateGridPreviewCache();
            RegenerateWorkingPigQueue();
            Repaint();
        }

        private void FillGridWithBrush(PigColor brushColor)
        {
            if (brushColor == PigColor.None)
            {
                workingPlacedObjects.Clear();
                InvalidateGridPreviewCache();
                statusMessage = "Grid cleared.";
                Repaint();
                return;
            }

            if (!TryResolvePigBrush(brushColor, out var brush))
            {
                statusMessage = $"{brushColor} iÃ§in database iÃ§inde 1x1 pig tanÄ±mÄ± bulunamadÄ±.";
                return;
            }

            workingPlacedObjects.Clear();
            EnsurePlacedObjectCapacity(workingGridSize.x * workingGridSize.y);
            for (int y = 0; y < workingGridSize.y; y++)
            {
                for (int x = 0; x < workingGridSize.x; x++)
                {
                    workingPlacedObjects.Add(new PlacedObjectData(brush.Id, new Vector2Int(x, y)));
                }
            }

            InvalidateGridPreviewCache();
            statusMessage = $"Filled grid with {brushColor}.";
            Repaint();
        }

        private void FillEmptyGridWithBrush(PigColor brushColor)
        {
            if (brushColor == PigColor.None)
            {
                statusMessage = "Select a pig color before filling empty cells.";
                return;
            }

            if (!TryResolvePigBrush(brushColor, out var brush))
            {
                statusMessage = $"{brushColor} iÃ§in database iÃ§inde 1x1 pig tanÄ±mÄ± bulunamadÄ±.";
                return;
            }

            EnsureGridPreviewCache(Mathf.Max(1, workingGridSize.x), Mathf.Max(1, workingGridSize.y));
            EnsurePlacedObjectCapacity(workingPlacedObjects.Count + (workingGridSize.x * workingGridSize.y));

            var addedCount = 0;
            for (int y = 0; y < workingGridSize.y; y++)
            {
                for (int x = 0; x < workingGridSize.x; x++)
                {
                    if (GetCachedPlacementIndex(x, y) >= 0)
                    {
                        continue;
                    }

                    workingPlacedObjects.Add(new PlacedObjectData(brush.Id, new Vector2Int(x, y)));
                    addedCount++;
                }
            }

            if (addedCount <= 0)
            {
                statusMessage = $"No empty cells were available for {brushColor}.";
                Repaint();
                return;
            }

            InvalidateGridPreviewCache();
            statusMessage = $"Filled {addedCount} empty cells with {brushColor}.";
            Repaint();
        }

        private void TryPlaceBrush(Vector2Int gridPosition, bool isDrag)
        {
            if (selectedDatabase == null)
            {
                statusMessage = "Assign a level database before painting.";
                return;
            }

            if (selectedBrushColor == PigColor.None)
            {
                RemovePlacedObjectAt(gridPosition);
                return;
            }

            if (!TryResolvePigBrush(selectedBrushColor, out var brush))
            {
                statusMessage = $"{selectedBrushColor} iÃ§in database iÃ§inde 1x1 pig tanÄ±mÄ± bulunamadÄ±.";
                return;
            }

            if (!CanFitPlacement(gridPosition, Vector2Int.one))
            {
                if (!isDrag)
                {
                    statusMessage = $"{selectedBrushColor} does not fit at ({gridPosition.x}, {gridPosition.y}).";
                }

                return;
            }

            if (TryFindPlacementIndexAt(gridPosition, out var existingPlacementIndex))
            {
                if (TryResolvePlacedObject(existingPlacementIndex, out var existingPlacedObject, out var existingDefinition)
                    && existingDefinition.GridSize == Vector2Int.one
                    && existingPlacedObject.Origin == gridPosition)
                {
                    if (existingPlacedObject.PlaceableId == brush.Id)
                    {
                        return;
                    }

                    workingPlacedObjects[existingPlacementIndex] = new PlacedObjectData(brush.Id, gridPosition);
                    InvalidateGridPreviewCache();
                    statusMessage = $"Updated {selectedBrushColor} at ({gridPosition.x}, {gridPosition.y}).";
                    Repaint();
                    return;
                }

                RemovePlacementAtIndexFast(existingPlacementIndex);
            }

            EnsurePlacedObjectCapacity(workingPlacedObjects.Count + 1);
            workingPlacedObjects.Add(new PlacedObjectData(brush.Id, gridPosition));
            InvalidateGridPreviewCache();
            statusMessage = $"Placed {selectedBrushColor} at ({gridPosition.x}, {gridPosition.y}).";
            Repaint();
        }

        private void RemovePlacedObjectAt(Vector2Int gridPosition)
        {
            if (!TryFindPlacementIndexAt(gridPosition, out var placementIndex))
            {
                return;
            }

            if (!TryResolvePlacedObject(placementIndex, out var placedObject, out var definition))
            {
                RemovePlacementAtIndexFast(placementIndex);
                Repaint();
                return;
            }

            RemovePlacementAtIndexFast(placementIndex);
            InvalidateGridPreviewCache();
            statusMessage = $"Removed {definition.DisplayName} at ({placedObject.Origin.x}, {placedObject.Origin.y}).";
            Repaint();
        }

        private void RemoveOverlappingPlacements(Vector2Int origin, Vector2Int size)
        {
            for (int i = workingPlacedObjects.Count - 1; i >= 0; i--)
            {
                if (!TryResolvePlacedObject(i, out var placedObject, out var definition))
                {
                    workingPlacedObjects.RemoveAt(i);
                    continue;
                }

                if (FootprintsOverlap(origin, size, placedObject.Origin, definition.GridSize))
                {
                    workingPlacedObjects.RemoveAt(i);
                }
            }
        }

        private bool TryFindPlacementIndexAt(Vector2Int gridPosition, out int placementIndex)
        {
            EnsureGridPreviewCache(Mathf.Max(1, workingGridSize.x), Mathf.Max(1, workingGridSize.y));
            var cachedIndex = GetCachedPlacementIndex(gridPosition.x, gridPosition.y);
            if (cachedIndex >= 0)
            {
                placementIndex = cachedIndex;
                return true;
            }

            for (int i = workingPlacedObjects.Count - 1; i >= 0; i--)
            {
                if (!TryResolvePlacedObject(i, out var placedObject, out var definition))
                {
                    continue;
                }

                if (ContainsGridPosition(placedObject.Origin, definition.GridSize, gridPosition))
                {
                    placementIndex = i;
                    return true;
                }
            }

            placementIndex = -1;
            return false;
        }

        private bool TryFindPlacementAtOrigin(Vector2Int origin, PlaceableId placeableId, out int placementIndex)
        {
            for (int i = 0; i < workingPlacedObjects.Count; i++)
            {
                var placedObject = workingPlacedObjects[i];
                if (placedObject.Origin == origin && placedObject.PlaceableId == placeableId)
                {
                    placementIndex = i;
                    return true;
                }
            }

            placementIndex = -1;
            return false;
        }

        private bool TryResolvePlacedObject(int placementIndex, out PlacedObjectData placedObject, out PlaceableDefinition definition)
        {
            if (placementIndex < 0 || placementIndex >= workingPlacedObjects.Count || selectedDatabase == null)
            {
                placedObject = default;
                definition = null;
                return false;
            }

            placedObject = workingPlacedObjects[placementIndex];
            definition = selectedDatabase.FindPlaceable(placedObject);
            return definition != null;
        }

        private void BuildOccupancyCache(int width, int height, out int[,] occupiedPlacementIndices, out bool[,] rootFlags)
        {
            occupiedPlacementIndices = new int[width, height];
            rootFlags = new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    occupiedPlacementIndices[x, y] = -1;
                }
            }

            if (selectedDatabase == null)
            {
                return;
            }

            for (int placementIndex = 0; placementIndex < workingPlacedObjects.Count; placementIndex++)
            {
                if (!TryResolvePlacedObject(placementIndex, out var placedObject, out var definition))
                {
                    continue;
                }

                var size = definition.GridSize;
                for (int dx = 0; dx < size.x; dx++)
                {
                    for (int dy = 0; dy < size.y; dy++)
                    {
                        var x = placedObject.Origin.x + dx;
                        var y = placedObject.Origin.y + dy;
                        if (x < 0 || x >= width || y < 0 || y >= height)
                        {
                            continue;
                        }

                        occupiedPlacementIndices[x, y] = placementIndex;
                        if (dx == 0 && dy == 0)
                        {
                            rootFlags[x, y] = true;
                        }
                    }
                }
            }
        }

        private void SyncImportPaletteFromDatabase()
        {
            if (workingImportSettings == null)
            {
                return;
            }

            var paletteEntries = workingImportSettings.PaletteEntries;
            var previousEntries = new Dictionary<PigColor, PigColorPaletteEntry>();
            for (int i = 0; i < paletteEntries.Count; i++)
            {
                var entry = paletteEntries[i];
                if (entry == null
                    || entry.Color == PigColor.None
                    || previousEntries.ContainsKey(entry.Color))
                {
                    continue;
                }

                previousEntries.Add(entry.Color, entry);
            }

            paletteEntries.Clear();

            var orderedColors = PigColorPaletteUtility.GetDefaultBrushColors();
            for (int i = 0; i < orderedColors.Count; i++)
            {
                var color = orderedColors[i];
                if (color == PigColor.None)
                {
                    continue;
                }

                if (selectedDatabase != null
                    && selectedDatabase.TryGetDefaultBlockPlaceable(color, out var definition))
                {
                    var hasPreviousEntry = previousEntries.TryGetValue(color, out var previousEntry);
                    var enabled = !hasPreviousEntry || previousEntry.Enabled;
                    var displayColor = hasPreviousEntry ? previousEntry.DisplayColor : ResolveBrushPreviewColor(color);
                    paletteEntries.Add(new PigColorPaletteEntry(color, displayColor, enabled));
                    continue;
                }

                if (previousEntries.TryGetValue(color, out var entry))
                {
                    paletteEntries.Add(new PigColorPaletteEntry(color, entry.DisplayColor, entry.Enabled));
                    continue;
                }

                paletteEntries.Add(new PigColorPaletteEntry(color, PigColorPaletteUtility.GetDisplayColor(color), true));
            }
        }

        private void BuildLevelPopupNames()
        {
            if (selectedDatabase == null || selectedDatabase.Levels.Count == 0)
            {
                cachedLevelNames = new[] { "No Levels" };
                return;
            }

            cachedLevelNames = new string[selectedDatabase.Levels.Count];
            for (int i = 0; i < selectedDatabase.Levels.Count; i++)
            {
                var level = selectedDatabase.Levels[i];
                cachedLevelNames[i] = $"{i + 1}. {level.LevelName}";
            }
        }

        private void RegenerateWorkingPigQueue()
        {
            if (selectedDatabase == null)
            {
                workingPigQueue.Clear();
                ResetPigQueueSelection();
                pigValidationMessage = "Assign a level database before generating pigs.";
                pigValidationMessageType = MessageType.Warning;
                return;
            }

            NormalizePigQueueGenerationSettings();
            pigQueueGenerationSettings.HoldingSlotCount = GetHoldingContainerCount();
            workingPigQueue = PigQueueGenerator.Generate(workingPlacedObjects, selectedDatabase, pigQueueGenerationSettings);
            NormalizeWorkingPigQueueSlots();
            ResetPigQueueSelection();
            if (workingPigQueue.Count == 0)
            {
                pigValidationMessage = "No pigs were generated from the current cell matrix.";
                pigValidationMessageType = MessageType.Warning;
                statusMessage = pigValidationMessage;
                return;
            }

            ValidateWorkingGuaranteedCompletion(updateStatusMessage: false);
            statusMessage = $"Pig queue regenerated. {pigValidationMessage}";
        }

        private void ValidateWorkingGuaranteedCompletion(bool updateStatusMessage = true)
        {
            var validationResult = LevelGuaranteedCompletionValidator.Validate(
                workingPlacedObjects,
                selectedDatabase,
                workingPigQueue);
            pigValidationMessage = validationResult.Message;
            pigValidationMessageType = validationResult.MessageType;
            if (updateStatusMessage)
            {
                statusMessage = validationResult.Message;
            }
        }

        private void MarkPigGuaranteeValidationDirty()
        {
            if (workingPigQueue == null || workingPigQueue.Count == 0)
            {
                pigValidationMessage = "Pig queue will be regenerated from the current grid when needed.";
                pigValidationMessageType = MessageType.Info;
                return;
            }

            pigValidationMessage = "Grid or pig queue changed. Run Validate to refresh the guaranteed-completion result.";
            pigValidationMessageType = MessageType.Info;
        }

        private Dictionary<PigColor, int> CountPlacedPigCellsByColor()
        {
            EnsureGridPreviewCache(Mathf.Max(1, workingGridSize.x), Mathf.Max(1, workingGridSize.y));
            return cachedPigCellCounts;
        }

        private void DrawPigQueueInfoBar(float layoutWidth)
        {
            var summaries = BuildPigQueueColorSummaries();
            if (summaries.Count == 0)
            {
                return;
            }

            const float outerPadding = 8f;
            const float titleHeight = 18f;
            const float cardHeight = 48f;
            const float cardGap = 6f;
            const float minCardWidth = 124f;
            const float maxCardWidth = 156f;

            var usableWidth = Mathf.Max(minCardWidth, layoutWidth - outerPadding * 2f);
            var cardsPerRow = Mathf.Max(1, Mathf.FloorToInt((usableWidth + cardGap) / (minCardWidth + cardGap)));
            var availableCardsWidth = Mathf.Max(minCardWidth, usableWidth - ((cardsPerRow - 1) * cardGap));
            var cardWidth = Mathf.Clamp(Mathf.Floor(availableCardsWidth / cardsPerRow), minCardWidth, maxCardWidth);
            var rowCount = Mathf.CeilToInt(summaries.Count / (float)cardsPerRow);
            var infoBarHeight = outerPadding * 2f
                + titleHeight
                + rowCount * cardHeight
                + Mathf.Max(0, rowCount - 1) * cardGap;
            var infoRect = GUILayoutUtility.GetRect(10f, infoBarHeight, GUILayout.ExpandWidth(true));

            GUI.Label(
                new Rect(infoRect.x + outerPadding, infoRect.y + 3f, infoRect.width - outerPadding * 2f, titleHeight),
                "Color Summary",
                EditorStyles.miniBoldLabel);

            var cardsStartY = infoRect.y + outerPadding + titleHeight;
            for (int i = 0; i < summaries.Count; i++)
            {
                var row = i / cardsPerRow;
                var column = i % cardsPerRow;
                var cardX = infoRect.x + outerPadding + column * (cardWidth + cardGap);
                var cardY = cardsStartY + row * (cardHeight + cardGap);
                var cardRect = new Rect(cardX, cardY, cardWidth, cardHeight);
                DrawPigQueueColorSummaryCard(cardRect, summaries[i]);
            }
        }

        private List<PigQueueColorSummary> BuildPigQueueColorSummaries()
        {
            var totalAmmoByColor = new Dictionary<PigColor, int>();
            var pigCountsByColor = new Dictionary<PigColor, int>();
            if (workingPigQueue != null)
            {
                for (int i = 0; i < workingPigQueue.Count; i++)
                {
                    var entry = workingPigQueue[i];
                    if (entry.Color == PigColor.None)
                    {
                        continue;
                    }

                    totalAmmoByColor[entry.Color] = totalAmmoByColor.TryGetValue(entry.Color, out var currentAmmo)
                        ? currentAmmo + Mathf.Max(0, entry.Ammo)
                        : Mathf.Max(0, entry.Ammo);
                    pigCountsByColor[entry.Color] = pigCountsByColor.TryGetValue(entry.Color, out var currentCount)
                        ? currentCount + 1
                        : 1;
                }
            }

            var summaries = new List<PigQueueColorSummary>();
            foreach (PigColor color in Enum.GetValues(typeof(PigColor)))
            {
                if (color == PigColor.None)
                {
                    continue;
                }

                var totalAmmo = totalAmmoByColor.TryGetValue(color, out var ammoCount) ? ammoCount : 0;
                var pigCount = pigCountsByColor.TryGetValue(color, out var queueCount) ? queueCount : 0;
                if (totalAmmo <= 0 && pigCount <= 0)
                {
                    continue;
                }

                summaries.Add(new PigQueueColorSummary(color, totalAmmo, pigCount));
            }

            summaries.Sort((left, right) =>
            {
                var ammoCompare = right.TotalAmmo.CompareTo(left.TotalAmmo);
                return ammoCompare != 0 ? ammoCompare : left.Color.CompareTo(right.Color);
            });

            return summaries;
        }

        private void DrawPigQueueColorSummaryCard(Rect cardRect, PigQueueColorSummary summary)
        {
            var backgroundColor = GetPaletteColor(summary.Color);
            var borderColor = new Color(0f, 0f, 0f, 0.22f);
            var textColor = GetContrastTextColor(backgroundColor);

            EditorGUI.DrawRect(cardRect, backgroundColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), borderColor);

            var titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.UpperLeft,
            };
            titleStyle.normal.textColor = textColor;

            var valueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
            };
            valueStyle.normal.textColor = textColor;

            GUI.Label(new Rect(cardRect.x + 6f, cardRect.y + 5f, cardRect.width - 12f, 14f), GetPaletteLabel(summary.Color), titleStyle);
            GUI.Label(new Rect(cardRect.x + 6f, cardRect.y + 21f, cardRect.width - 12f, 14f), $"Total Ammo {summary.TotalAmmo}", valueStyle);
            GUI.Label(new Rect(cardRect.x + 6f, cardRect.y + 33f, cardRect.width - 12f, 14f), $"Pigs {summary.PigCount}", valueStyle);
        }

        private void ResetPigQueueSelection()
        {
            ClearPigQueueSelection();
            ResetPigQueueDragState();
        }

        private void ClearPigQueueSelection()
        {
            pigQueueEditorState.ClearSelection(GetAmmoStep());
        }

        private void EnsurePigQueueSelectionValid()
        {
            pigQueueEditorState.EnsureSelectionValid(workingPigQueue, GetAmmoStep());
            pigQueueEditorState.SwapAmount = Mathf.Max(
                GetAmmoStep(),
                SnapDownToStep(Mathf.Max(GetAmmoStep(), pigQueueEditorState.SwapAmount), GetAmmoStep()));
        }

        private bool IsValidPigQueueIndex(int queueIndex)
        {
            return workingPigQueue != null
                && queueIndex >= 0
                && queueIndex < workingPigQueue.Count;
        }

        private bool IsPigQueuePreviewSelected(int queueIndex)
        {
            return pigQueueEditorState.IsPreviewSelected(queueIndex);
        }

        private string GetPigQueuePreviewSelectionLabel(int queueIndex)
        {
            return pigQueueEditorState.GetSelectionLabel(queueIndex);
        }

        private void SelectPigQueuePreviewEntry(int queueIndex)
        {
            if (pigQueueEditorState.TrySelectPreviewEntry(workingPigQueue, queueIndex, GetAmmoStep(), out var message)
                && !string.IsNullOrEmpty(message))
            {
                statusMessage = message;
            }
        }

        private bool TryGetSelectedPigQueuePair(out int primaryIndex, out int secondaryIndex)
        {
            return pigQueueEditorState.TryGetSelectedPair(workingPigQueue, GetAmmoStep(), out primaryIndex, out secondaryIndex);
        }

        private void DrawSelectedPigQueueSwapPanel()
        {
            var hasSelectionPair = TryGetSelectedPigQueuePair(out var primaryIndex, out var secondaryIndex);
            var ammoStep = GetAmmoStep();
            pigQueueEditorState.SwapAmount = Mathf.Max(
                ammoStep,
                SnapDownToStep(Mathf.Max(ammoStep, pigQueueEditorState.SwapAmount), ammoStep));

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Ammo Swap", EditorStyles.miniBoldLabel);
                if (!hasSelectionPair)
                {
                    EditorGUILayout.LabelField(
                        "Select two same-color pigs from Deck Preview to enable ammo transfer.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.LabelField(
                        "The first selected pig becomes A, the second becomes B.",
                        EditorStyles.wordWrappedMiniLabel);
                    return;
                }

                var primaryEntry = workingPigQueue[primaryIndex];
                var secondaryEntry = workingPigQueue[secondaryIndex];
                var maxTransferFromPrimaryToSecondary = GetMaximumTransferAmountBetweenPair(primaryIndex, secondaryIndex);
                var maxTransferFromSecondaryToPrimary = GetMaximumTransferAmountBetweenPair(secondaryIndex, primaryIndex);
                var maximumTransferAmount = Mathf.Max(maxTransferFromPrimaryToSecondary, maxTransferFromSecondaryToPrimary);

                EditorGUILayout.LabelField(
                    $"Selected {primaryEntry.Color} pigs. Transfer ammo between A and B in multiples of {ammoStep}.",
                    EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawSelectedPigQueueEntrySummary("A", primaryIndex, primaryEntry);
                    GUILayout.Space(8f);
                    DrawSelectedPigQueueEntrySummary("B", secondaryIndex, secondaryEntry);
                }

                var hasAnyTransfer = maximumTransferAmount >= ammoStep;
                if (hasAnyTransfer)
                {
                    pigQueueEditorState.SwapAmount = Mathf.Clamp(
                        SnapDownToStep(pigQueueEditorState.SwapAmount, ammoStep),
                        ammoStep,
                        maximumTransferAmount);
                }
                else
                {
                    pigQueueEditorState.SwapAmount = 0;
                }

                using (new EditorGUI.DisabledScope(!hasAnyTransfer))
                {
                    var nextTransferAmount = EditorGUILayout.IntSlider(
                        "Transfer Amount",
                        pigQueueEditorState.SwapAmount,
                        hasAnyTransfer ? ammoStep : 0,
                        hasAnyTransfer ? maximumTransferAmount : 0);

                    pigQueueEditorState.SwapAmount = hasAnyTransfer
                        ? Mathf.Clamp(
                            SnapDownToStep(nextTransferAmount, ammoStep),
                            ammoStep,
                            maximumTransferAmount)
                        : 0;
                }

                if (!hasAnyTransfer)
                {
                    EditorGUILayout.HelpBox(
                        BuildAmmoTransferUnavailableMessage(primaryEntry, secondaryEntry),
                        MessageType.Info);
                }

                EditorGUILayout.LabelField(
                    $"A -> B max: {maxTransferFromPrimaryToSecondary}   B -> A max: {maxTransferFromSecondaryToPrimary}",
                    EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!CanTransferAmmoBetweenPair(primaryIndex, secondaryIndex, pigQueueEditorState.SwapAmount)))
                    {
                        if (GUILayout.Button("A -> B", GUILayout.Width(72f)))
                        {
                            if (TransferAmmoBetweenPair(primaryIndex, secondaryIndex, pigQueueEditorState.SwapAmount))
                            {
                                statusMessage = $"{pigQueueEditorState.SwapAmount} ammo moved from A to B.";
                            }
                        }
                    }

                    using (new EditorGUI.DisabledScope(!CanTransferAmmoBetweenPair(secondaryIndex, primaryIndex, pigQueueEditorState.SwapAmount)))
                    {
                        if (GUILayout.Button("B -> A", GUILayout.Width(72f)))
                        {
                            if (TransferAmmoBetweenPair(secondaryIndex, primaryIndex, pigQueueEditorState.SwapAmount))
                            {
                                statusMessage = $"{pigQueueEditorState.SwapAmount} ammo moved from B to A.";
                            }
                        }
                    }

                    if (GUILayout.Button("Clear Selection", GUILayout.Width(108f)))
                    {
                        ResetPigQueueSelection();
                    }
                }
            }
        }

        private void DrawSelectedPigQueueEntrySummary(string label, int queueIndex, PigQueueEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(140f)))
            {
                EditorGUILayout.LabelField($"{label}  #{queueIndex + 1}", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(entry.Color.ToString(), EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Ammo: {entry.Ammo}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Container: {entry.SlotIndex + 1}", EditorStyles.miniLabel);
            }
        }

        private void NormalizePigQueueGenerationSettings()
        {
            pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            pigQueueGenerationSettings.NormalizeAmmoDistributionSettings();
        }

        private int GetAmmoStep()
        {
            NormalizePigQueueGenerationSettings();
            return pigQueueGenerationSettings.AmmoStep;
        }

        private int GetMinimumPigAmmo()
        {
            NormalizePigQueueGenerationSettings();
            return pigQueueGenerationSettings.MinAmmoPerPig;
        }

        private int GetAveragePigAmmo()
        {
            NormalizePigQueueGenerationSettings();
            return pigQueueGenerationSettings.TargetAmmoPerPig;
        }

        private int GetMaximumPigAmmo()
        {
            NormalizePigQueueGenerationSettings();
            return pigQueueGenerationSettings.MaxAmmoPerPig;
        }

        private int GetMinimumPigsPerColor()
        {
            NormalizePigQueueGenerationSettings();
            return pigQueueGenerationSettings.MinPigsPerColor;
        }

        private int GetMaximumPigsPerColor()
        {
            NormalizePigQueueGenerationSettings();
            return pigQueueGenerationSettings.MaxPigsPerColor;
        }

        private void ApplyPigCountGenerationSettings(int minimumPigsPerColor, int maximumPigsPerColor)
        {
            pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            pigQueueGenerationSettings.MinPigsPerColor = Mathf.Max(1, minimumPigsPerColor);
            pigQueueGenerationSettings.MaxPigsPerColor = Mathf.Max(pigQueueGenerationSettings.MinPigsPerColor, maximumPigsPerColor);
            NormalizePigQueueGenerationSettings();
        }

        private void ApplyPigQueueGenerationSettings(int minimumAmmo, int maximumAmmo, int ammoStep)
        {
            pigQueueGenerationSettings ??= new PigQueueGenerationSettings();

            var resolvedStep = PigQueueGenerationSettings.ClampAmmoStep(ammoStep);
            var resolvedMinimumAmmo = SnapAmmoRangeMinimum(minimumAmmo, resolvedStep, resolvedStep, int.MaxValue);
            var resolvedMaximumAmmo = SnapAmmoRangeMaximum(maximumAmmo, resolvedStep, resolvedMinimumAmmo, int.MaxValue);
            var targetAmmo = Mathf.Clamp(Mathf.RoundToInt((resolvedMinimumAmmo + resolvedMaximumAmmo) * 0.5f), resolvedMinimumAmmo, resolvedMaximumAmmo);

            pigQueueGenerationSettings.AmmoStep = resolvedStep;
            pigQueueGenerationSettings.MinAmmoPerPig = resolvedMinimumAmmo;
            pigQueueGenerationSettings.MaxAmmoPerPig = resolvedMaximumAmmo;
            pigQueueGenerationSettings.TargetAmmoPerPig = targetAmmo;
            NormalizePigQueueGenerationSettings();
        }

        private bool ApplyAutomaticAmmoSliderBounds(int ammoStep, int sliderMinimum, int sliderMaximum)
        {
            NormalizePigQueueGenerationSettings();

            var resolvedMinimumAmmo = SnapAmmoRangeMinimum(pigQueueGenerationSettings.MinAmmoPerPig, ammoStep, sliderMinimum, sliderMaximum);
            var resolvedMaximumAmmo = SnapAmmoRangeMaximum(pigQueueGenerationSettings.MaxAmmoPerPig, ammoStep, resolvedMinimumAmmo, sliderMaximum);
            if (resolvedMinimumAmmo == pigQueueGenerationSettings.MinAmmoPerPig
                && resolvedMaximumAmmo == pigQueueGenerationSettings.MaxAmmoPerPig)
            {
                return false;
            }

            ApplyPigQueueGenerationSettings(resolvedMinimumAmmo, resolvedMaximumAmmo, ammoStep);
            return true;
        }

        private int GetAutomaticAmmoSliderMaximum(int ammoStep, out int largestColorCellCount, out int coloredPigTypeCount)
        {
            largestColorCellCount = 0;
            coloredPigTypeCount = 0;
            var configuredMaximum = Mathf.Max(
                ammoStep,
                pigQueueGenerationSettings?.MinAmmoPerPig ?? ammoStep,
                pigQueueGenerationSettings?.MaxAmmoPerPig ?? ammoStep);

            var pigCellCounts = CountPlacedPigCellsByColor();
            foreach (var pair in pigCellCounts)
            {
                if (pair.Key == PigColor.None || pair.Value <= 0)
                {
                    continue;
                }

                coloredPigTypeCount++;
                largestColorCellCount = Mathf.Max(largestColorCellCount, pair.Value);
            }

            if (largestColorCellCount > 0)
            {
                return Mathf.Max(configuredMaximum, SnapUpToStep(largestColorCellCount, ammoStep));
            }

            var fallbackHeadroom = Mathf.Max(
                ammoStep * 4,
                SnapUpToStep(Mathf.CeilToInt(configuredMaximum * 0.25f), ammoStep));
            var fallbackMaximum = configuredMaximum + fallbackHeadroom;
            return Mathf.Max(ammoStep * 10, SnapUpToStep(fallbackMaximum, ammoStep));
        }

        private int GetAutomaticPigCountSliderMaximum(int ammoStep)
        {
            var configuredMaximum = Mathf.Max(1, GetMaximumPigsPerColor());
            var currentMinimumAmmo = Mathf.Max(ammoStep, GetMinimumPigAmmo());
            var largestColorCellCount = 0;
            var pigCellCounts = CountPlacedPigCellsByColor();
            foreach (var pair in pigCellCounts)
            {
                if (pair.Key == PigColor.None || pair.Value <= 0)
                {
                    continue;
                }

                largestColorCellCount = Mathf.Max(largestColorCellCount, pair.Value);
            }

            if (largestColorCellCount > 0)
            {
                var largestColorAmmo = SnapUpToStep(largestColorCellCount, ammoStep);
                var minimumAmmoUnits = Mathf.Max(1, Mathf.CeilToInt(currentMinimumAmmo / (float)ammoStep));
                var totalAmmoUnits = Mathf.Max(1, largestColorAmmo / Mathf.Max(1, ammoStep));
                var maximumPossiblePigCount = Mathf.Max(1, totalAmmoUnits / minimumAmmoUnits);
                return Mathf.Max(configuredMaximum, maximumPossiblePigCount);
            }

            var fallbackHeadroom = Mathf.Max(4, Mathf.CeilToInt(configuredMaximum * 0.25f));
            return Mathf.Max(8, configuredMaximum + fallbackHeadroom);
        }

        private bool TryGetWorkingPigQueueAmmoStats(out int minAmmo, out float averageAmmo, out int maxAmmo)
        {
            minAmmo = 0;
            averageAmmo = 0f;
            maxAmmo = 0;
            if (workingPigQueue == null || workingPigQueue.Count == 0)
            {
                return false;
            }

            var totalAmmo = 0;
            minAmmo = int.MaxValue;
            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                var ammo = Mathf.Max(0, workingPigQueue[i].Ammo);
                totalAmmo += ammo;
                minAmmo = Mathf.Min(minAmmo, ammo);
                maxAmmo = Mathf.Max(maxAmmo, ammo);
            }

            averageAmmo = totalAmmo / (float)workingPigQueue.Count;
            minAmmo = minAmmo == int.MaxValue ? 0 : minAmmo;
            return true;
        }

        private void NormalizeWorkingPigQueueSlots()
        {
            if (workingPigQueue == null)
            {
                return;
            }

            var holdingContainerCount = GetHoldingContainerCount();
            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                var entry = workingPigQueue[i];
                var normalizedSlot = i % holdingContainerCount;
                if (entry.SlotIndex == normalizedSlot)
                {
                    continue;
                }

                workingPigQueue[i] = new PigQueueEntry(entry.Color, entry.Ammo, normalizedSlot, entry.Direction);
            }
        }

        private void DrawPigQueueColorLabel(PigColor color)
        {
            var swatchRect = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f));
            var swatchColor = PigColorPaletteUtility.GetDisplayColor(color);
            if (color == PigColor.Black)
            {
                swatchColor = new Color(0.16f, 0.16f, 0.16f);
            }

            var drawRect = new Rect(swatchRect.x, swatchRect.y + 2f, 14f, 14f);
            EditorGUI.DrawRect(drawRect, swatchColor);
            GUI.Label(new Rect(drawRect.x + 20f, swatchRect.y, 72f, EditorGUIUtility.singleLineHeight), color.ToString());
            GUILayout.Space(78f);
        }

        private bool HasAmmoSwapPartner(int entryIndex)
        {
            if (workingPigQueue == null || entryIndex < 0 || entryIndex >= workingPigQueue.Count)
            {
                return false;
            }

            var color = workingPigQueue[entryIndex].Color;
            if (color == PigColor.None)
            {
                return false;
            }

            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                if (i == entryIndex)
                {
                    continue;
                }

                if (workingPigQueue[i].Color == color)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanTransferAmmoToEntry(int entryIndex)
        {
            if (!HasAmmoSwapPartner(entryIndex))
            {
                return false;
            }

            var ammoStep = GetAmmoStep();
            var maximumAmmo = GetMaximumPigAmmo();
            if (workingPigQueue[entryIndex].Ammo + ammoStep > maximumAmmo)
            {
                return false;
            }

            var color = workingPigQueue[entryIndex].Color;
            var minimumAmmo = GetMinimumPigAmmo();
            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                if (i == entryIndex)
                {
                    continue;
                }

                var entry = workingPigQueue[i];
                if (entry.Color == color && entry.Ammo - ammoStep >= minimumAmmo)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanTransferAmmoFromEntry(int entryIndex)
        {
            if (!HasAmmoSwapPartner(entryIndex))
            {
                return false;
            }

            var ammoStep = GetAmmoStep();
            var minimumAmmo = GetMinimumPigAmmo();
            if (workingPigQueue[entryIndex].Ammo - ammoStep < minimumAmmo)
            {
                return false;
            }

            var maximumAmmo = GetMaximumPigAmmo();
            var color = workingPigQueue[entryIndex].Color;
            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                if (i == entryIndex)
                {
                    continue;
                }

                var entry = workingPigQueue[i];
                if (entry.Color == color && entry.Ammo + ammoStep <= maximumAmmo)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyPigAmmoEditPreservingColorTotal(int entryIndex, int requestedAmmo)
        {
            if (workingPigQueue == null || entryIndex < 0 || entryIndex >= workingPigQueue.Count)
            {
                return;
            }

            var currentEntry = workingPigQueue[entryIndex];
            var clampedAmmo = ClampAmmoRequestToConfiguredRange(requestedAmmo, currentEntry.Ammo);
            if (clampedAmmo == currentEntry.Ammo)
            {
                return;
            }

            var delta = clampedAmmo - currentEntry.Ammo;
            if (!TransferAmmoBetweenSameColor(entryIndex, delta))
            {
                statusMessage = $"{currentEntry.Color} ammo can only be redistributed between pigs of the same color and must stay inside the configured min/max range.";
            }
        }

        private bool TransferAmmoBetweenSameColor(int entryIndex, int delta)
        {
            if (delta == 0
                || workingPigQueue == null
                || entryIndex < 0
                || entryIndex >= workingPigQueue.Count)
            {
                return false;
            }

            var targetEntry = workingPigQueue[entryIndex];
            var minimumAmmo = GetMinimumPigAmmo();
            var maximumAmmo = GetMaximumPigAmmo();
            var ammoStep = GetAmmoStep();
            var peerIndices = GetSameColorPeerIndices(entryIndex, sortDescendingByAmmo: delta > 0);
            if (peerIndices.Count == 0 || delta % ammoStep != 0)
            {
                return false;
            }

            var actualDelta = 0;
            if (delta > 0)
            {
                var maxReceivable = Mathf.Max(0, maximumAmmo - targetEntry.Ammo);
                var remaining = Mathf.Min(delta, maxReceivable);
                for (int peerListIndex = 0; peerListIndex < peerIndices.Count && remaining > 0; peerListIndex++)
                {
                    var peerIndex = peerIndices[peerListIndex];
                    var peerEntry = workingPigQueue[peerIndex];
                    var transferable = Mathf.Max(0, peerEntry.Ammo - minimumAmmo);
                    transferable -= transferable % ammoStep;
                    if (transferable <= 0)
                    {
                        continue;
                    }

                    var transferAmount = Mathf.Min(remaining, transferable);
                    transferAmount -= transferAmount % ammoStep;
                    if (transferAmount <= 0)
                    {
                        continue;
                    }

                    workingPigQueue[peerIndex] = new PigQueueEntry(peerEntry.Color, peerEntry.Ammo - transferAmount, peerEntry.SlotIndex, peerEntry.Direction);
                    remaining -= transferAmount;
                    actualDelta += transferAmount;
                }

                if (actualDelta <= 0)
                {
                    return false;
                }

                workingPigQueue[entryIndex] = new PigQueueEntry(targetEntry.Color, targetEntry.Ammo + actualDelta, targetEntry.SlotIndex, targetEntry.Direction);
                MarkPigGuaranteeValidationDirty();
                return true;
            }

            var amountToGive = Mathf.Min(-delta, Mathf.Max(0, targetEntry.Ammo - minimumAmmo));
            amountToGive -= amountToGive % ammoStep;
            if (amountToGive <= 0)
            {
                return false;
            }

            var remainingToGive = amountToGive;
            for (int peerListIndex = 0; peerListIndex < peerIndices.Count && remainingToGive > 0; peerListIndex++)
            {
                var peerIndex = peerIndices[peerListIndex];
                var peerEntry = workingPigQueue[peerIndex];
                var receivable = Mathf.Max(0, maximumAmmo - peerEntry.Ammo);
                receivable -= receivable % ammoStep;
                if (receivable <= 0)
                {
                    continue;
                }

                var transferAmount = Mathf.Min(remainingToGive, receivable);
                transferAmount -= transferAmount % ammoStep;
                if (transferAmount <= 0)
                {
                    continue;
                }

                workingPigQueue[peerIndex] = new PigQueueEntry(peerEntry.Color, peerEntry.Ammo + transferAmount, peerEntry.SlotIndex, peerEntry.Direction);
                remainingToGive -= transferAmount;
                actualDelta += transferAmount;
            }

            if (actualDelta <= 0)
            {
                return false;
            }

            workingPigQueue[entryIndex] = new PigQueueEntry(targetEntry.Color, targetEntry.Ammo - actualDelta, targetEntry.SlotIndex, targetEntry.Direction);
            MarkPigGuaranteeValidationDirty();
            return true;
        }

        private bool CanTransferAmmoBetweenPair(int fromIndex, int toIndex, int requestedAmount)
        {
            if (!IsValidPigQueueIndex(fromIndex)
                || !IsValidPigQueueIndex(toIndex)
                || fromIndex == toIndex)
            {
                return false;
            }

            var fromEntry = workingPigQueue[fromIndex];
            var toEntry = workingPigQueue[toIndex];
            if (fromEntry.Color == PigColor.None || fromEntry.Color != toEntry.Color)
            {
                return false;
            }

            var transferAmount = SnapDownToStep(Mathf.Max(GetAmmoStep(), requestedAmount), GetAmmoStep());
            if (transferAmount <= 0)
            {
                return false;
            }

            return fromEntry.Ammo - transferAmount >= GetMinimumPigAmmo()
                && toEntry.Ammo + transferAmount <= GetMaximumPigAmmo();
        }

        private int GetMaximumTransferAmountBetweenPair(int fromIndex, int toIndex)
        {
            if (!IsValidPigQueueIndex(fromIndex)
                || !IsValidPigQueueIndex(toIndex)
                || fromIndex == toIndex)
            {
                return 0;
            }

            var fromEntry = workingPigQueue[fromIndex];
            var toEntry = workingPigQueue[toIndex];
            if (fromEntry.Color == PigColor.None || fromEntry.Color != toEntry.Color)
            {
                return 0;
            }

            var ammoStep = GetAmmoStep();
            var transferableFromSource = fromEntry.Ammo - GetMinimumPigAmmo();
            var receivableByTarget = GetMaximumPigAmmo() - toEntry.Ammo;
            var maximumTransfer = Mathf.Min(transferableFromSource, receivableByTarget);
            if (maximumTransfer < ammoStep)
            {
                return 0;
            }

            return SnapDownToStep(maximumTransfer, ammoStep);
        }

        private string BuildAmmoTransferUnavailableMessage(PigQueueEntry primaryEntry, PigQueueEntry secondaryEntry)
        {
            var minimumAmmo = GetMinimumPigAmmo();
            var maximumAmmo = GetMaximumPigAmmo();

            if (primaryEntry.Ammo == secondaryEntry.Ammo)
            {
                return $"A and B are both {primaryEntry.Ammo} ammo. Allowed ammo range is {minimumAmmo}-{maximumAmmo}, so no transfer is possible between equal values here.";
            }

            return $"A has {primaryEntry.Ammo} ammo and B has {secondaryEntry.Ammo}. Allowed ammo range is {minimumAmmo}-{maximumAmmo}, so any transfer would break the configured limits.";
        }

        private bool TransferAmmoBetweenPair(int fromIndex, int toIndex, int requestedAmount)
        {
            if (!CanTransferAmmoBetweenPair(fromIndex, toIndex, requestedAmount))
            {
                return false;
            }

            var transferAmount = SnapDownToStep(Mathf.Max(GetAmmoStep(), requestedAmount), GetAmmoStep());
            var fromEntry = workingPigQueue[fromIndex];
            var toEntry = workingPigQueue[toIndex];

            workingPigQueue[fromIndex] = new PigQueueEntry(
                fromEntry.Color,
                fromEntry.Ammo - transferAmount,
                fromEntry.SlotIndex,
                fromEntry.Direction);
            workingPigQueue[toIndex] = new PigQueueEntry(
                toEntry.Color,
                toEntry.Ammo + transferAmount,
                toEntry.SlotIndex,
                toEntry.Direction);
            MarkPigGuaranteeValidationDirty();
            return true;
        }

        private int ClampAmmoRequestToConfiguredRange(int requestedAmmo, int currentAmmo)
        {
            var minimumAmmo = GetMinimumPigAmmo();
            var maximumAmmo = GetMaximumPigAmmo();
            var clampedAmmo = Mathf.Clamp(requestedAmmo, minimumAmmo, maximumAmmo);
            if (clampedAmmo == currentAmmo)
            {
                return currentAmmo;
            }

            return clampedAmmo > currentAmmo
                ? Mathf.Min(maximumAmmo, SnapUpToStep(clampedAmmo, GetAmmoStep()))
                : Mathf.Max(minimumAmmo, SnapDownToStep(clampedAmmo, GetAmmoStep()));
        }

        private List<int> GetSameColorPeerIndices(int entryIndex, bool sortDescendingByAmmo)
        {
            var peers = new List<int>();
            if (workingPigQueue == null || entryIndex < 0 || entryIndex >= workingPigQueue.Count)
            {
                return peers;
            }

            var targetColor = workingPigQueue[entryIndex].Color;
            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                if (i == entryIndex || workingPigQueue[i].Color != targetColor)
                {
                    continue;
                }

                peers.Add(i);
            }

            peers.Sort((left, right) =>
            {
                var leftEntry = workingPigQueue[left];
                var rightEntry = workingPigQueue[right];
                var ammoCompare = sortDescendingByAmmo
                    ? rightEntry.Ammo.CompareTo(leftEntry.Ammo)
                    : leftEntry.Ammo.CompareTo(rightEntry.Ammo);
                return ammoCompare != 0 ? ammoCompare : left.CompareTo(right);
            });

            return peers;
        }

        private static int SnapUpToStep(int value, int step)
        {
            var positiveStep = Mathf.Max(1, step);
            var positiveValue = Mathf.Max(1, value);
            return ((positiveValue + positiveStep - 1) / positiveStep) * positiveStep;
        }

        private static int SnapAmmoRangeMinimum(int value, int step, int sliderMinimum, int sliderMaximum)
        {
            var clampedValue = Mathf.Clamp(value, sliderMinimum, sliderMaximum);
            return Mathf.Clamp(SnapDownToStep(clampedValue, step), sliderMinimum, sliderMaximum);
        }

        private static int SnapAmmoRangeMaximum(int value, int step, int minimumAmmo, int sliderMaximum)
        {
            var clampedValue = Mathf.Clamp(value, minimumAmmo, sliderMaximum);
            return Mathf.Clamp(SnapUpToStep(clampedValue, step), minimumAmmo, sliderMaximum);
        }

        private static int SnapDownToStep(int value, int step)
        {
            var positiveStep = Mathf.Max(1, step);
            var positiveValue = Mathf.Max(positiveStep, value);
            return Mathf.Max(positiveStep, (positiveValue / positiveStep) * positiveStep);
        }

        private void ResetPigQueueDragState()
        {
            pigQueueEditorState.ResetDragState();
        }

        private bool MovePigQueueEntry(int fromIndex, int requestedTargetIndex)
        {
            if (workingPigQueue == null || !IsValidPigQueueIndex(fromIndex))
            {
                return false;
            }

            var targetIndex = Mathf.Clamp(requestedTargetIndex, 0, workingPigQueue.Count);
            if (targetIndex > fromIndex)
            {
                targetIndex--;
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, workingPigQueue.Count - 1);
            if (targetIndex == fromIndex)
            {
                return false;
            }

            var movedEntry = workingPigQueue[fromIndex];
            workingPigQueue.RemoveAt(fromIndex);
            workingPigQueue.Insert(targetIndex, movedEntry);

            ClearPigQueueSelection();
            NormalizeWorkingPigQueueSlots();
            MarkPigGuaranteeValidationDirty();
            return true;
        }

        private bool SwapPigQueueEntries(int firstIndex, int secondIndex)
        {
            if (workingPigQueue == null
                || !IsValidPigQueueIndex(firstIndex)
                || !IsValidPigQueueIndex(secondIndex)
                || firstIndex == secondIndex)
            {
                return false;
            }

            var firstEntry = workingPigQueue[firstIndex];
            workingPigQueue[firstIndex] = workingPigQueue[secondIndex];
            workingPigQueue[secondIndex] = firstEntry;
            ClearPigQueueSelection();
            NormalizeWorkingPigQueueSlots();
            MarkPigGuaranteeValidationDirty();
            return true;
        }

        private bool TryGetPigQueuePreviewCellAtPosition(
            IReadOnlyList<PigQueuePreviewCell> previewCells,
            Vector2 mousePosition,
            bool requireEntry,
            out PigQueuePreviewCell previewCell)
        {
            for (int i = 0; i < previewCells.Count; i++)
            {
                var candidate = previewCells[i];
                var contains = requireEntry
                    ? candidate.HasEntry && candidate.CardRect.Contains(mousePosition)
                    : candidate.CellRect.Contains(mousePosition);
                if (!contains)
                {
                    continue;
                }

                previewCell = candidate;
                return true;
            }

            previewCell = default;
            return false;
        }

        private void HandlePigQueuePreviewInteraction(Rect previewRect, IReadOnlyList<PigQueuePreviewCell> previewCells)
        {
            var current = Event.current;
            if (workingPigQueue == null || workingPigQueue.Count == 0)
            {
                ResetPigQueueDragState();
                return;
            }

            if (current.type == EventType.KeyDown
                && current.keyCode == KeyCode.Escape
                && pigQueueEditorState.DragSourceIndex >= 0)
            {
                ResetPigQueueDragState();
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                if (TryGetPigQueuePreviewCellAtPosition(previewCells, current.mousePosition, true, out var pressedCell))
                {
                    pigQueueEditorState.BeginDrag(pressedCell.QueueIndex, pressedCell.LinearIndex, current.mousePosition);
                    GUI.FocusControl(null);
                    current.Use();
                }
                else if (!previewRect.Contains(current.mousePosition))
                {
                    ResetPigQueueDragState();
                }

                return;
            }

            if (current.type == EventType.MouseDrag && pigQueueEditorState.DragSourceIndex >= 0)
            {
                if (!pigQueueEditorState.IsDragging)
                {
                    if (pigQueueEditorState.UpdateDragging(current.mousePosition, PigQueueDragStartDistance))
                    {
                        ClearPigQueueSelection();
                    }
                }

                if (pigQueueEditorState.IsDragging)
                {
                    pigQueueEditorState.SetDragHoverLinearIndex(
                        TryGetPigQueuePreviewCellAtPosition(previewCells, current.mousePosition, false, out var hoveredCell)
                            ? hoveredCell.LinearIndex
                            : -1);
                    current.Use();
                    Repaint();
                }

                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0 && pigQueueEditorState.DragSourceIndex >= 0)
            {
                if (pigQueueEditorState.IsDragging)
                {
                    if (TryGetPigQueuePreviewCellAtPosition(previewCells, current.mousePosition, false, out var hoveredCell))
                    {
                        if (hoveredCell.HasEntry
                            && SwapPigQueueEntries(pigQueueEditorState.DragSourceIndex, hoveredCell.QueueIndex))
                        {
                            statusMessage = $"Swapped pigs #{pigQueueEditorState.DragSourceIndex + 1} and #{hoveredCell.QueueIndex + 1}.";
                        }
                        else if (!hoveredCell.HasEntry
                            && MovePigQueueEntry(pigQueueEditorState.DragSourceIndex, hoveredCell.LinearIndex))
                        {
                            statusMessage = $"Moved pig to #{Mathf.Clamp(hoveredCell.LinearIndex + 1, 1, workingPigQueue.Count)} in the deck order.";
                        }
                    }

                    ResetPigQueueDragState();
                    current.Use();
                    Repaint();
                    return;
                }

                if (TryGetPigQueuePreviewCellAtPosition(previewCells, current.mousePosition, true, out var clickedCell)
                    && clickedCell.QueueIndex == pigQueueEditorState.DragSourceIndex)
                {
                    SelectPigQueuePreviewEntry(clickedCell.QueueIndex);
                    current.Use();
                }

                ResetPigQueueDragState();
            }
        }

        private void DrawPigQueueLanePreview(int holdingContainerCount, float layoutWidth)
        {
            if (holdingContainerCount <= 0)
            {
                return;
            }

            EnsurePigQueueSelectionValid();

            var laneEntryIndices = new List<int>[holdingContainerCount];
            for (int i = 0; i < holdingContainerCount; i++)
            {
                laneEntryIndices[i] = new List<int>();
            }

            for (int i = 0; i < workingPigQueue.Count; i++)
            {
                var entry = workingPigQueue[i];
                var slotIndex = Mathf.Clamp(entry.SlotIndex, 0, holdingContainerCount - 1);
                laneEntryIndices[slotIndex].Add(i);
            }

            var maxLaneDepth = 0;
            for (int i = 0; i < laneEntryIndices.Length; i++)
            {
                if (laneEntryIndices[i].Count > maxLaneDepth)
                {
                    maxLaneDepth = laneEntryIndices[i].Count;
                }
            }

            maxLaneDepth = Mathf.Max(1, maxLaneDepth);

            const float outerPadding = 8f;
            const float rightPreviewPadding = 20f;
            const float horizontalAlignmentBias = 0.18f;
            const float titleHeight = 18f;
            const float headerHeight = 22f;
            const float rowLabelWidth = 36f;
            const float cellGap = 4f;
            const float cardHeight = 42f;

            var availableGridWidth = Mathf.Max(
                120f,
                layoutWidth - rowLabelWidth - (outerPadding * 2f) - ((holdingContainerCount - 1) * cellGap));
            var cellWidth = Mathf.Clamp(Mathf.Floor(availableGridWidth / holdingContainerCount), 58f, 92f);
            var gridWidth = rowLabelWidth + holdingContainerCount * cellWidth + ((holdingContainerCount - 1) * cellGap);
            var previewHeight = outerPadding * 2f
                + titleHeight
                + headerHeight
                + maxLaneDepth * cardHeight
                + maxLaneDepth * cellGap
                + cellGap;
            var previewRect = GUILayoutUtility.GetRect(10f, previewHeight, GUILayout.ExpandWidth(true));
            var refreshButtonWidth = 60f;
            var refreshButtonRect = new Rect(
                previewRect.x + outerPadding,
                previewRect.y + 3f,
                refreshButtonWidth,
                titleHeight);

            if (GUI.Button(refreshButtonRect, "Refresh", EditorStyles.miniButton))
            {
                RegenerateWorkingPigQueue();
                GUI.FocusControl(null);
                Repaint();
                return;
            }

            GUI.Label(
                new Rect(
                    refreshButtonRect.xMax + 8f,
                    previewRect.y + 4f,
                    previewRect.width - outerPadding * 3f - refreshButtonWidth - 8f,
                    titleHeight),
                "Deck Preview",
                EditorStyles.miniBoldLabel);

            var availableHorizontalSlack = Mathf.Max(0f, previewRect.width - gridWidth - outerPadding - rightPreviewPadding);
            var gridOriginX = previewRect.x + outerPadding + (availableHorizontalSlack * horizontalAlignmentBias);
            var headerY = previewRect.y + outerPadding + titleHeight;
            var cellsStartY = headerY + headerHeight + cellGap;
            var previewCells = new List<PigQueuePreviewCell>(maxLaneDepth * holdingContainerCount);

            for (int depthIndex = 0; depthIndex < maxLaneDepth; depthIndex++)
            {
                var rowY = cellsStartY + depthIndex * (cardHeight + cellGap);
                for (int laneIndex = 0; laneIndex < holdingContainerCount; laneIndex++)
                {
                    var cellX = gridOriginX + rowLabelWidth + laneIndex * (cellWidth + cellGap);
                    var cellRect = new Rect(cellX, rowY, cellWidth, cardHeight);
                    var linearIndex = depthIndex * holdingContainerCount + laneIndex;
                    var hasEntry = depthIndex < laneEntryIndices[laneIndex].Count;
                    var queueIndex = hasEntry ? laneEntryIndices[laneIndex][depthIndex] : -1;
                    previewCells.Add(new PigQueuePreviewCell(
                        cellRect,
                        new Rect(cellRect.x + 1f, cellRect.y + 1f, cellRect.width - 2f, cellRect.height - 2f),
                        linearIndex,
                        queueIndex,
                        hasEntry));
                }
            }

            HandlePigQueuePreviewInteraction(previewRect, previewCells);

            var topLeftRect = new Rect(gridOriginX, headerY, rowLabelWidth, headerHeight);
            EditorGUI.DrawRect(topLeftRect, new Color(0.2f, 0.22f, 0.28f));
            GUI.Label(topLeftRect, "Row", EditorStyles.centeredGreyMiniLabel);

            for (int laneIndex = 0; laneIndex < holdingContainerCount; laneIndex++)
            {
                var laneX = gridOriginX + rowLabelWidth + laneIndex * (cellWidth + cellGap);
                var headerRect = new Rect(laneX, headerY, cellWidth, headerHeight);
                EditorGUI.DrawRect(headerRect, new Color(0.24f, 0.26f, 0.34f));
                GUI.Label(headerRect, $"{laneIndex + 1}", EditorStyles.centeredGreyMiniLabel);
            }

            for (int depthIndex = 0; depthIndex < maxLaneDepth; depthIndex++)
            {
                var rowY = cellsStartY + depthIndex * (cardHeight + cellGap);
                var rowLabelRect = new Rect(gridOriginX, rowY, rowLabelWidth, cardHeight);
                EditorGUI.DrawRect(rowLabelRect, new Color(0.19f, 0.2f, 0.24f));
                GUI.Label(rowLabelRect, $"#{depthIndex + 1}", EditorStyles.centeredGreyMiniLabel);

                for (int laneIndex = 0; laneIndex < holdingContainerCount; laneIndex++)
                {
                    var previewCell = previewCells[depthIndex * holdingContainerCount + laneIndex];
                    var cellRect = previewCell.CellRect;
                    EditorGUI.DrawRect(cellRect, new Color(0.18f, 0.19f, 0.22f));

                    if (pigQueueEditorState.IsDragging && pigQueueEditorState.DragHoverLinearIndex == previewCell.LinearIndex)
                    {
                        var highlightRect = new Rect(cellRect.x + 1f, cellRect.y + 1f, cellRect.width - 2f, cellRect.height - 2f);
                        EditorGUI.DrawRect(highlightRect, new Color(1f, 1f, 1f, 0.08f));
                        Handles.DrawSolidRectangleWithOutline(highlightRect, Color.clear, new Color(1f, 1f, 1f, 0.7f));
                    }

                    if (previewCell.HasEntry)
                    {
                        var queueIndex = previewCell.QueueIndex;
                        var cardRect = previewCell.CardRect;
                        DrawPigQueuePreviewCard(cardRect, workingPigQueue[queueIndex], IsPigQueuePreviewSelected(queueIndex), GetPigQueuePreviewSelectionLabel(queueIndex));
                        EditorGUIUtility.AddCursorRect(cardRect, MouseCursor.MoveArrow);
                        if (pigQueueEditorState.IsDragging && queueIndex == pigQueueEditorState.DragSourceIndex)
                        {
                            EditorGUI.DrawRect(cardRect, new Color(0f, 0f, 0f, 0.28f));
                        }
                    }
                    else
                    {
                        EditorGUI.DrawRect(new Rect(cellRect.x + 1f, cellRect.y + 1f, cellRect.width - 2f, cellRect.height - 2f), new Color(0f, 0f, 0f, 0.12f));
                        GUI.Label(cellRect, "-", EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }

            if (pigQueueEditorState.IsDragging
                && IsValidPigQueueIndex(pigQueueEditorState.DragSourceIndex)
                && Event.current.type == EventType.Repaint)
            {
                var dragCardRect = new Rect(
                    Event.current.mousePosition.x + 14f,
                    Event.current.mousePosition.y + 14f,
                    cellWidth - 2f,
                    cardHeight - 2f);
                DrawPigQueuePreviewCard(dragCardRect, workingPigQueue[pigQueueEditorState.DragSourceIndex], true, null);
            }
        }

        private readonly struct PigQueuePreviewCell
        {
            public PigQueuePreviewCell(Rect cellRect, Rect cardRect, int linearIndex, int queueIndex, bool hasEntry)
            {
                CellRect = cellRect;
                CardRect = cardRect;
                LinearIndex = linearIndex;
                QueueIndex = queueIndex;
                HasEntry = hasEntry;
            }

            public Rect CellRect { get; }
            public Rect CardRect { get; }
            public int LinearIndex { get; }
            public int QueueIndex { get; }
            public bool HasEntry { get; }
        }

        private readonly struct PigQueueColorSummary
        {
            public PigQueueColorSummary(PigColor color, int totalAmmo, int pigCount)
            {
                Color = color;
                TotalAmmo = totalAmmo;
                PigCount = pigCount;
            }

            public PigColor Color { get; }
            public int TotalAmmo { get; }
            public int PigCount { get; }
        }

        private void DrawPigQueuePreviewCard(Rect cardRect, PigQueueEntry entry, bool isSelected, string selectionLabel)
        {
            var color = GetPaletteColor(entry.Color);
            EditorGUI.DrawRect(cardRect, color);

            var borderColor = isSelected
                ? new Color(1f, 1f, 1f, 0.92f)
                : new Color(0f, 0f, 0f, 0.22f);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), borderColor);
            EditorGUI.DrawRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), borderColor);

            var ammoStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = 12,
            };
            ammoStyle.normal.textColor = GetContrastTextColor(color);

            var colorStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.LowerCenter,
            };
            colorStyle.normal.textColor = ammoStyle.normal.textColor;

            GUI.Label(new Rect(cardRect.x, cardRect.y + 3f, cardRect.width, 16f), entry.Ammo.ToString(), ammoStyle);
            GUI.Label(new Rect(cardRect.x + 2f, cardRect.y + 20f, cardRect.width - 4f, 14f), GetPigQueuePreviewColorLabel(entry.Color), colorStyle);

            if (!string.IsNullOrWhiteSpace(selectionLabel))
            {
                var badgeRect = new Rect(cardRect.x + 4f, cardRect.y + 4f, 16f, 14f);
                var badgeBackground = new Color(0f, 0f, 0f, 0.3f);
                EditorGUI.DrawRect(badgeRect, badgeBackground);

                var badgeStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                badgeStyle.normal.textColor = GetContrastTextColor(badgeBackground);
                GUI.Label(badgeRect, selectionLabel, badgeStyle);
            }
        }

        private static string GetPigQueuePreviewColorLabel(PigColor color)
        {
            var label = color.ToString();
            return label.Length <= 5 ? label : label.Substring(0, 5);
        }

        private Color GetContrastTextColor(Color background)
        {
            var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
            return luminance > 0.58f ? new Color(0.1f, 0.1f, 0.1f) : Color.white;
        }

        private int GetHoldingContainerCount()
        {
            pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            pigQueueGenerationSettings.HoldingSlotCount = ClampHoldingContainerCount(pigQueueGenerationSettings.HoldingSlotCount);
            return pigQueueGenerationSettings.HoldingSlotCount;
        }

        private int ClampHoldingContainerCount(int value)
        {
            return Mathf.Clamp(value, MinHoldingContainerCount, MaxHoldingContainerCount);
        }

        private void SetHoldingContainerCount(int desiredCount, bool applyToScene)
        {
            var clampedCount = ClampHoldingContainerCount(desiredCount);
            pigQueueGenerationSettings.HoldingSlotCount = clampedCount;

            if (applyToScene)
            {
                if (TryApplyHoldingContainerCountToOpenScene(clampedCount, out var applyMessage))
                {
                    statusMessage = applyMessage;
                }
                else
                {
                    statusMessage = applyMessage;
                }
            }

            NormalizeWorkingPigQueueSlots();
        }

        private bool TryApplyHoldingContainerCountToOpenScene(int desiredCount, out string message)
        {
            if (!TryResolveEnvironmentFromSceneContext(out var environment, out message))
            {
                return false;
            }

            environment.ResolveMissingReferences();
            var holdingContainer = environment.HoldingContainer;
            if (holdingContainer == null)
            {
                message = "Resolved environment is missing Holding_Container.";
                return false;
            }

            var availableChildren = holdingContainer.childCount;
            if (availableChildren < MinHoldingContainerCount)
            {
                message = "Holding_Container needs at least 2 child objects.";
                return false;
            }

            var appliedCount = Mathf.Clamp(desiredCount, MinHoldingContainerCount, Mathf.Min(MaxHoldingContainerCount, availableChildren));
            for (int i = 0; i < availableChildren; i++)
            {
                var child = holdingContainer.GetChild(i).gameObject;
                var shouldBeActive = i < appliedCount;
                if (child.activeSelf == shouldBeActive)
                {
                    continue;
                }

                Undo.RecordObject(child, "Update Holding Container Count");
                child.SetActive(shouldBeActive);
                EditorUtility.SetDirty(child);
            }

            EditorUtility.SetDirty(holdingContainer);
            if (holdingContainer.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(holdingContainer.gameObject.scene);
            }

            appliedCount = environment.ApplyHoldingContainerCount(appliedCount, MinHoldingContainerCount, MaxHoldingContainerCount);
            pigQueueGenerationSettings.HoldingSlotCount = appliedCount;
            NormalizeWorkingPigQueueSlots();
            message = environment.DeckContainer != null
                ? $"Holding containers set to {appliedCount}. Deck_Container alignment is available."
                : $"Holding containers set to {appliedCount}, but Deck_Container is missing.";
            return true;
        }

        private bool TryResolveEnvironmentFromSceneContext(out EnvironmentContext environment, out string message)
        {
            environment = null;
            message = "Could not find SceneContext or EnvironmentContext in the open scene.";

            var sceneContexts = UnityEngine.Object.FindObjectsByType<GameSceneContext>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sceneContexts.Length; i++)
            {
                var context = sceneContexts[i];
                if (context == null || !context.gameObject.scene.IsValid())
                {
                    continue;
                }

                var existingEnvironment = SceneContextEnvironmentUtility.ResolveEnvironment(context);
                if (context.TryResolveEnvironment(null, out environment, out message) && environment != null)
                {
                    TrackEditorManagedEnvironment(
                        context,
                        environment,
                        markAsTemporary: existingEnvironment == null);
                    return true;
                }
            }

            var environments = UnityEngine.Object.FindObjectsByType<EnvironmentContext>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < environments.Length; i++)
            {
                var candidate = environments[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                environment = candidate;
                message = null;
                return true;
            }

            return false;
        }
        private bool TryResolvePigBrush(PigColor color, out PlaceableDefinition definition)
        {
            if (selectedDatabase == null || color == PigColor.None)
            {
                definition = null;
                return false;
            }

            return selectedDatabase.TryGetDefaultBlockPlaceable(color, out definition);
        }

        private Color GetPaletteColor(PigColor color)
        {
            if (color == PigColor.None)
            {
                return new Color(0.16f, 0.16f, 0.16f);
            }

            if (TryResolvePigBrush(color, out var definition))
            {
                return ResolveBrushPreviewColor(color);
            }

            return ResolveBrushPreviewColor(color);
        }

        private LevelData GetSelectedLevel()
        {
            if (selectedDatabase == null || selectedLevelIndex < 0 || selectedLevelIndex >= selectedDatabase.Levels.Count)
            {
                return null;
            }

            return selectedDatabase.Levels[selectedLevelIndex];
        }

        private bool CanFitPlacement(Vector2Int origin, Vector2Int size)
        {
            return origin.x >= 0
                && origin.y >= 0
                && origin.x + size.x <= workingGridSize.x
                && origin.y + size.y <= workingGridSize.y;
        }

        private static bool ContainsGridPosition(Vector2Int origin, Vector2Int size, Vector2Int gridPosition)
        {
            return gridPosition.x >= origin.x
                && gridPosition.y >= origin.y
                && gridPosition.x < origin.x + size.x
                && gridPosition.y < origin.y + size.y;
        }

        private static bool FootprintsOverlap(Vector2Int aOrigin, Vector2Int aSize, Vector2Int bOrigin, Vector2Int bSize)
        {
            return aOrigin.x < bOrigin.x + bSize.x
                && aOrigin.x + aSize.x > bOrigin.x
                && aOrigin.y < bOrigin.y + bSize.y
                && aOrigin.y + aSize.y > bOrigin.y;
        }

        private string GenerateUniqueLevelName()
        {
            if (selectedDatabase == null)
            {
                return "Level 1";
            }

            var number = Mathf.Max(1, selectedDatabase.Levels.Count + 1);
            while (true)
            {
                var candidate = $"Level {number}";
                var exists = false;
                for (int i = 0; i < selectedDatabase.Levels.Count; i++)
                {
                    if (string.Equals(selectedDatabase.Levels[i].LevelName, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    return candidate;
                }

                number++;
            }
        }

        private void TryAutoAssignDatabase()
        {
            if (selectedDatabase != null)
            {
                return;
            }

            var guids = AssetDatabase.FindAssets("t:LevelDatabase");
            if (guids == null || guids.Length == 0)
            {
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            selectedDatabase = AssetDatabase.LoadAssetAtPath<LevelDatabase>(assetPath);
        }

        private void TryAutoAssignBlockData()
        {
            if (selectedBlockData != null)
            {
                return;
            }

            var guids = AssetDatabase.FindAssets("t:BlockData");
            if (guids == null || guids.Length == 0)
            {
                selectedBlockData = CreateDefaultBlockDataAsset();
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            selectedBlockData = AssetDatabase.LoadAssetAtPath<BlockData>(assetPath);
        }

        private BlockData ResolveWorkingBlockData(BlockData preferred)
        {
            if (preferred != null)
            {
                return preferred;
            }

            TryAutoAssignBlockData();
            return selectedBlockData;
        }

        private void RepairDefaultBlockDataIfNeeded()
        {
            if (selectedBlockData == null || !HasMissingBlockPrefabReference(selectedBlockData))
            {
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(selectedBlockData);
            if (!string.Equals(assetPath, DefaultBlockDataAssetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (TryAssignDefaultBlockPrefab(selectedBlockData))
            {
                statusMessage = $"Repaired missing block prefab on {selectedBlockData.name}.";
            }
        }

        private BlockData CreateDefaultBlockDataAsset()
        {
            EnsureFolder(DefaultDefaultsFolder);

            var blockData = AssetDatabase.LoadAssetAtPath<BlockData>(DefaultBlockDataAssetPath);
            if (blockData == null)
            {
                blockData = CreateInstance<BlockData>();
                AssetDatabase.CreateAsset(blockData, DefaultBlockDataAssetPath);
            }

            var blockPrefab = LoadDefaultBlockPrefab();

            var serializedObject = new SerializedObject(blockData);
            serializedObject.FindProperty("blockPrefab").objectReferenceValue = blockPrefab;
            serializedObject.FindProperty("cellSpacing").floatValue = 0.6f;
            serializedObject.FindProperty("verticalOffset").floatValue = 0.5f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(blockData);
            AssetDatabase.SaveAssets();
            return blockData;
        }

        private static bool HasMissingBlockPrefabReference(BlockData blockData)
        {
            if (blockData == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(blockData);
            var blockPrefabProperty = serializedObject.FindProperty("blockPrefab");
            return blockPrefabProperty != null
                && blockPrefabProperty.objectReferenceValue == null
                && blockPrefabProperty.objectReferenceInstanceIDValue != 0;
        }

        private static bool TryAssignDefaultBlockPrefab(BlockData blockData)
        {
            if (blockData == null)
            {
                return false;
            }

            var blockPrefab = LoadDefaultBlockPrefab();
            if (blockPrefab == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(blockData);
            var blockPrefabProperty = serializedObject.FindProperty("blockPrefab");
            if (blockPrefabProperty == null)
            {
                return false;
            }

            blockPrefabProperty.objectReferenceValue = blockPrefab;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(blockData);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static GameObject LoadDefaultBlockPrefab()
        {
            var blockPrefabPath = AssetDatabase.GUIDToAssetPath(DefaultBlockPrefabGuid);
            return string.IsNullOrWhiteSpace(blockPrefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(blockPrefabPath);
        }

        private static GameObject LoadDefaultPigPrefab()
        {
            var pigPrefabPath = AssetDatabase.GUIDToAssetPath(DefaultPigPrefabGuid);
            return string.IsNullOrWhiteSpace(pigPrefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(pigPrefabPath);
        }

        private bool TryImportSelectedImage(string selectedPath, out Texture2D importedTexture, out string importedAssetPath)
        {
            importedTexture = null;
            importedAssetPath = null;

            if (!File.Exists(selectedPath))
            {
                statusMessage = "Selected image file does not exist.";
                return false;
            }

            EnsureFolder(ImportedLevelImagesFolder);

            var relativeSourcePath = FileUtil.GetProjectRelativePath(selectedPath);
            var normalizedRelativeSourcePath = string.IsNullOrEmpty(relativeSourcePath)
                ? null
                : relativeSourcePath.Replace('\\', '/');
            var isUnityAssetPath = !string.IsNullOrEmpty(normalizedRelativeSourcePath)
                && (string.Equals(normalizedRelativeSourcePath, "Assets", StringComparison.OrdinalIgnoreCase)
                    || normalizedRelativeSourcePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));

            if (isUnityAssetPath
                && normalizedRelativeSourcePath.StartsWith(ImportedLevelImagesFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                importedAssetPath = normalizedRelativeSourcePath;
            }
            else
            {
                var fileName = Path.GetFileName(selectedPath);
                var existingImportedAssetPath = $"{ImportedLevelImagesFolder}/{fileName}";
                var existingImportedAbsolutePath = Path.Combine(Directory.GetCurrentDirectory(), existingImportedAssetPath);
                if (File.Exists(existingImportedAbsolutePath))
                {
                    if (!AssetDatabase.DeleteAsset(existingImportedAssetPath))
                    {
                        statusMessage = $"Existing imported image could not be replaced: {existingImportedAssetPath}";
                        return false;
                    }
                }

                importedAssetPath = existingImportedAssetPath;

                if (isUnityAssetPath)
                {
                    var error = AssetDatabase.MoveAsset(normalizedRelativeSourcePath, importedAssetPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        statusMessage = $"Image could not be moved into {ImportedLevelImagesFolder}: {error}";
                        return false;
                    }
                }
                else
                {
                    var destinationAbsolutePath = Path.Combine(Directory.GetCurrentDirectory(), importedAssetPath);
                    var destinationDirectory = Path.GetDirectoryName(destinationAbsolutePath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    try
                    {
                        File.Copy(selectedPath, destinationAbsolutePath, false);
                    }
                    catch (Exception exception)
                    {
                        statusMessage = $"Image could not be copied into the project: {exception.Message}";
                        return false;
                    }

                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }

            AssetDatabase.ImportAsset(importedAssetPath, ImportAssetOptions.ForceSynchronousImport);
            importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(importedAssetPath);
            if (importedTexture == null)
            {
                statusMessage = $"Image was moved, but Unity could not import it as a texture: {importedAssetPath}";
                return false;
            }

            return true;
        }

        private string GetInitialImagePickerDirectory()
        {
            var exampleSourceImagesDirectory = Path.Combine(Directory.GetCurrentDirectory(), ExampleSourceImagesFolderName);
            if (Directory.Exists(exampleSourceImagesDirectory))
            {
                return exampleSourceImagesDirectory;
            }

            if (sourceImage != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(sourceImage);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var absoluteAssetPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                    var assetDirectory = Path.GetDirectoryName(absoluteAssetPath);
                    if (!string.IsNullOrEmpty(assetDirectory) && Directory.Exists(assetDirectory))
                    {
                        return assetDirectory;
                    }
                }
            }

            return Application.dataPath;
        }

        private static Texture2D LoadTextureAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private void SetSourceImage(Texture2D texture, bool applyImportedDefaults = false)
        {
            sourceImage = texture;

            if (sourceImage != null && applyImportedDefaults)
            {
                ApplyInitialImportedImageSettings();
                return;
            }

            ApplyGridResize(new Vector2Int(workingImportSettings.TargetColumns, workingImportSettings.TargetRows));
        }

        private void ApplyInitialImportedImageSettings()
        {
            workingImportSettings ??= new ImageImportSettings();
            workingImportSettings.ImageScale = InitialImportedDetailScale;
            workingImportSettings.AlphaThreshold = InitialImportedAlphaThreshold;
            ApplySourceImageScaleToImportResolution(reimportGrid: false);
        }

        private void ApplySourceImageScaleToImportResolution(bool reimportGrid)
        {
            if (sourceImage == null || workingImportSettings == null)
            {
                return;
            }

            var scaledSourceResolution = EstimateScaledSourceResolution(sourceImage, workingImportSettings.ImageScale);
            workingImportSettings.TargetColumns = scaledSourceResolution.x;
            workingImportSettings.TargetRows = scaledSourceResolution.y;

            if (reimportGrid)
            {
                ImportImageToGrid();
                return;
            }

            ApplyGridResize(scaledSourceResolution);
        }

        private static Vector2Int EstimateScaledSourceResolution(Texture2D texture, float scale)
        {
            if (texture == null)
            {
                return Vector2Int.one;
            }

            var clampedScale = Mathf.Clamp(scale, MinDetailScale, MaxDetailScale);
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(texture.width * clampedScale)),
                Mathf.Max(1, Mathf.RoundToInt(texture.height * clampedScale)));
        }

        private void PingSourceImage()
        {
            if (sourceImage != null)
            {
                EditorGUIUtility.PingObject(sourceImage);
            }
        }

        private string GetSourceImageDisplayText()
        {
            if (sourceImage != null)
            {
                var assetPath = AssetDatabase.GetAssetPath(sourceImage);
                return string.IsNullOrEmpty(assetPath) ? sourceImage.name : assetPath;
            }

            return "No source image selected";
        }

        private static Vector2Int ClampGridSize(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private static void Swap<T>(List<T> list, int a, int b)
        {
            if (list == null || a < 0 || b < 0 || a >= list.Count || b >= list.Count || a == b)
            {
                return;
            }

            (list[a], list[b]) = (list[b], list[a]);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var parts = folderPath.Split('/');
            var current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
#endif
