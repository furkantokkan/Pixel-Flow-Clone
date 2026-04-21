using System;
using System.Collections.Generic;
using Core.Runtime.ColorAtlas;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    public enum PlaceableId
    {
        None = 0,
        ObstacleBlock = 1,
        BlockPinkBasic = 2,
        BlockBlueBasic = 3,
        BlockGreenBasic = 4,
        BlockYellowBasic = 5,
        BlockOrangeBasic = 6,
        BlockPurpleBasic = 7,
        BlockBlackBasic = 8,
        BlockRedBasic = 9,
        BlockTealBasic = 10,
        BlockGrayBasic = 11,
        BlockWhiteBasic = 12,
    }

    public enum PlaceableKind
    {
        Block = 0,
        Obstacle = 1,
    }

    public enum PigAbilityType
    {
        None = 0,
        Chain = 1,
        Special = 2,
    }

    public static class PlaceableIdUtility
    {
        public static PlaceableId GetSuggestedId(PlaceableKind kind, PigColor color)
        {
            if (kind == PlaceableKind.Obstacle)
            {
                return PlaceableId.ObstacleBlock;
            }

            return color switch
            {
                PigColor.Red => PlaceableId.BlockRedBasic,
                PigColor.Pink => PlaceableId.BlockPinkBasic,
                PigColor.Blue => PlaceableId.BlockBlueBasic,
                PigColor.Green => PlaceableId.BlockGreenBasic,
                PigColor.Yellow => PlaceableId.BlockYellowBasic,
                PigColor.Orange => PlaceableId.BlockOrangeBasic,
                PigColor.Teal => PlaceableId.BlockTealBasic,
                PigColor.Purple => PlaceableId.BlockPurpleBasic,
                PigColor.Gray => PlaceableId.BlockGrayBasic,
                PigColor.White => PlaceableId.BlockWhiteBasic,
                PigColor.Black => PlaceableId.BlockBlackBasic,
                _ => PlaceableId.None,
            };
        }
    }

    [Serializable]
    public struct PlacedObjectData
    {
        [SerializeField] private PlaceableId placeableKey;
        [SerializeField] private Vector2Int origin;
        [SerializeField] private bool hasVisualToneOverride;
        [SerializeField] private int visualToneIndex;

        public PlacedObjectData(PlaceableId placeableId, Vector2Int origin)
        {
            placeableKey = placeableId;
            this.origin = origin;
            hasVisualToneOverride = false;
            visualToneIndex = AtlasPaletteConstants.DefaultToneIndex;
        }

        public PlacedObjectData(PlaceableId placeableId, Vector2Int origin, int toneIndex)
        {
            placeableKey = placeableId;
            this.origin = origin;
            hasVisualToneOverride = true;
            visualToneIndex = AtlasPaletteConstants.ClampToneIndex(toneIndex);
        }

        public PlaceableId PlaceableId => placeableKey;
        public Vector2Int Origin => origin;
        public bool HasVisualToneOverride => hasVisualToneOverride;
        public int VisualToneIndex => AtlasPaletteConstants.ClampToneIndex(visualToneIndex);
    }

    [Serializable]
    public sealed class PlaceableDefinition
    {
        [SerializeField, InspectorName("Block ID")] private PlaceableId placeableKey;
        [SerializeField] private string displayName = "Block";
        [SerializeField, InspectorName("Type")] private PlaceableKind kind = PlaceableKind.Block;
        [SerializeField] private PigColor color = PigColor.Pink;
        [SerializeField, InspectorName("Special Condition")] private PigAbilityType pigAbility = PigAbilityType.None;
        [SerializeField] private Vector2Int gridSize = Vector2Int.one;
        [SerializeField] private UnityEngine.Color editorColor = UnityEngine.Color.white;
        [SerializeField] private string editorGlyph;
        [SerializeField] private GameObject prefab;

        public PlaceableId Id => placeableKey;
        public string DisplayName => displayName;
        public PlaceableKind Kind => kind;
        public PigColor Color => color;
        public PigAbilityType PigAbility => pigAbility;
        public Vector2Int GridSize => new(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
        public UnityEngine.Color EditorColor => editorColor;
        public string EditorGlyph => editorGlyph;
        public GameObject Prefab => prefab;

#if UNITY_EDITOR
        public void EditorAssignPrefab(GameObject value)
        {
            prefab = value;
        }
#endif

        public bool MatchesImportColor(PigColor importColor)
        {
            return kind == PlaceableKind.Block
                && GridSize == Vector2Int.one
                && color == importColor;
        }

        public string ResolveEditorGlyph()
        {
            if (!string.IsNullOrWhiteSpace(editorGlyph))
            {
                return editorGlyph.Trim();
            }

            var size = GridSize;
            if (kind == PlaceableKind.Obstacle)
            {
                return "X";
            }

            if (size.x * size.y > 1)
            {
                return $"{size.x}x{size.y}";
            }

            return string.Empty;
        }

        public bool MatchesIdentity(PlaceableId candidateId)
        {
            return placeableKey != PlaceableId.None && placeableKey == candidateId;
        }

        public void EnsureIdentity(HashSet<PlaceableId> usedIds)
        {
            if (placeableKey == PlaceableId.None)
            {
                var suggestedId = PlaceableIdUtility.GetSuggestedId(kind, color);
                if (suggestedId != PlaceableId.None
                    && (usedIds == null || !usedIds.Contains(suggestedId)))
                {
                    placeableKey = suggestedId;
                }
            }

            if (placeableKey != PlaceableId.None)
            {
                usedIds?.Add(placeableKey);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = kind == PlaceableKind.Obstacle
                    ? "Block"
                    : $"{color} Block";
            }
            else if (kind == PlaceableKind.Obstacle && string.Equals(displayName, "Obstacle", StringComparison.OrdinalIgnoreCase))
            {
                displayName = "Block";
            }
            else if (kind == PlaceableKind.Block
                && string.Equals(displayName, $"{color} Pig", StringComparison.OrdinalIgnoreCase))
            {
                displayName = $"{color} Block";
            }

            gridSize = new Vector2Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
        }

        public static PlaceableDefinition CreateBlock(PlaceableId id, string name, PigColor color, GameObject prefab = null)
        {
            return new PlaceableDefinition
            {
                placeableKey = id,
                displayName = name,
                kind = PlaceableKind.Block,
                color = color,
                pigAbility = PigAbilityType.None,
                gridSize = Vector2Int.one,
                editorColor = PigColorPaletteUtility.GetAtlasPreviewColor(color),
                prefab = prefab,
            };
        }

        public static PlaceableDefinition CreateObstacle(PlaceableId id, string name, UnityEngine.Color editorColor, string glyph = "X", GameObject prefab = null)
        {
            return new PlaceableDefinition
            {
                placeableKey = id,
                displayName = name,
                kind = PlaceableKind.Obstacle,
                color = PigColor.None,
                pigAbility = PigAbilityType.None,
                gridSize = Vector2Int.one,
                editorColor = editorColor,
                editorGlyph = glyph,
                prefab = prefab,
            };
        }
    }

    [Serializable]
    public sealed class LevelData
    {
        [SerializeField] private string levelName = "Level";
        [SerializeField] private Vector2Int gridSize = new(12, 12);
        [SerializeField, HideInInspector] private Texture2D sourceImage;
        [SerializeField] private BlockData blockData;
        [SerializeField] private ImageImportSettings importSettings = new();
        [SerializeField, InspectorName("Blocks")] private List<PlacedObjectData> placedObjects = new();
        [SerializeField, HideInInspector] private List<PigQueueEntry> pigQueue = new();
        [SerializeField, HideInInspector] private PigQueueGenerationSettings pigQueueGenerationSettings = new();

        public string LevelName
        {
            get => levelName;
            set => levelName = string.IsNullOrWhiteSpace(value) ? "Level" : value.Trim();
        }

        public Vector2Int GridSize
        {
            get => new(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
            set => gridSize = new Vector2Int(Mathf.Max(1, value.x), Mathf.Max(1, value.y));
        }

        public Texture2D SourceImage
        {
            get => sourceImage;
            set => sourceImage = value;
        }

        public BlockData BlockData
        {
            get => blockData;
            set => blockData = value;
        }

        public ImageImportSettings ImportSettings
        {
            get => importSettings ??= new ImageImportSettings();
            set => importSettings = value?.Clone() ?? new ImageImportSettings();
        }

        public List<PlacedObjectData> PlacedObjects => placedObjects ??= new List<PlacedObjectData>();
        public List<PigQueueEntry> PigQueue => pigQueue ??= new List<PigQueueEntry>();

        public PigQueueGenerationSettings PigQueueGenerationSettings
        {
            get => pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            set => pigQueueGenerationSettings = value?.Clone() ?? new PigQueueGenerationSettings();
        }

        public LevelData Clone()
        {
            var clone = new LevelData
            {
                levelName = levelName,
                gridSize = GridSize,
                sourceImage = sourceImage,
                blockData = blockData,
                importSettings = ImportSettings.Clone(),
                pigQueueGenerationSettings = PigQueueGenerationSettings.Clone(),
                placedObjects = new List<PlacedObjectData>(PlacedObjects.Count),
                pigQueue = new List<PigQueueEntry>(PigQueue.Count),
            };

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                clone.placedObjects.Add(PlacedObjects[i]);
            }

            for (int i = 0; i < PigQueue.Count; i++)
            {
                clone.pigQueue.Add(PigQueue[i]);
            }

            return clone;
        }

        public void CopyFrom(LevelData other)
        {
            if (other == null)
            {
                return;
            }

            levelName = other.LevelName;
            gridSize = other.GridSize;
            sourceImage = other.SourceImage;
            blockData = other.BlockData;
            importSettings = other.ImportSettings.Clone();
            pigQueueGenerationSettings = other.PigQueueGenerationSettings.Clone();

            PlacedObjects.Clear();
            PigQueue.Clear();

            for (int i = 0; i < other.PlacedObjects.Count; i++)
            {
                PlacedObjects.Add(other.PlacedObjects[i]);
            }

            for (int i = 0; i < other.PigQueue.Count; i++)
            {
                PigQueue.Add(other.PigQueue[i]);
            }
        }

        public void EnsureConsistency()
        {
            gridSize = GridSize;
            importSettings ??= new ImageImportSettings();
            pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            placedObjects ??= new List<PlacedObjectData>();
            pigQueue ??= new List<PigQueueEntry>();

            LevelName = levelName;
        }
    }

    [CreateAssetMenu(fileName = "LevelDatabase", menuName = "Pixel Flow/Level Database")]
    public sealed class LevelDatabase : ScriptableObject
    {
#if UNITY_EDITOR
        private const string DefaultBlockPrefabGuid = "331edf6250de4de4a909c2115614f2d7";
        private const string DefaultPigPrefabGuid = "5f47c7c5de4f2ce4dbe9606dc849868f";
#endif
        private static readonly (PlaceableId Id, string Name, PigColor Color)[] DefaultBlockPlaceables =
        {
            (PlaceableId.BlockRedBasic, "Red Block", PigColor.Red),
            (PlaceableId.BlockPinkBasic, "Pink Block", PigColor.Pink),
            (PlaceableId.BlockBlueBasic, "Blue Block", PigColor.Blue),
            (PlaceableId.BlockGreenBasic, "Green Block", PigColor.Green),
            (PlaceableId.BlockYellowBasic, "Yellow Block", PigColor.Yellow),
            (PlaceableId.BlockOrangeBasic, "Orange Block", PigColor.Orange),
            (PlaceableId.BlockTealBasic, "Teal Block", PigColor.Teal),
            (PlaceableId.BlockPurpleBasic, "Purple Block", PigColor.Purple),
            (PlaceableId.BlockGrayBasic, "Gray Block", PigColor.Gray),
            (PlaceableId.BlockWhiteBasic, "White Block", PigColor.White),
            (PlaceableId.BlockBlackBasic, "Black Block", PigColor.Black),
        };

        [SerializeField] private List<LevelData> levels = new();
        [SerializeField, InspectorName("Block Definitions")] private List<PlaceableDefinition> placeableObjects = new();

        public List<LevelData> Levels => levels ??= new List<LevelData>();
        public List<PlaceableDefinition> PlaceableObjects => placeableObjects ??= new List<PlaceableDefinition>();

        public LevelData CreateLevel(string levelName)
        {
            var level = new LevelData
            {
                LevelName = levelName,
            };

            level.EnsureConsistency();
            Levels.Add(level);
            return level;
        }

        public void RemoveLevelAt(int index)
        {
            if (index < 0 || index >= Levels.Count)
            {
                return;
            }

            Levels.RemoveAt(index);
        }

        public void SetLevelAt(int index, LevelData source)
        {
            if (index < 0 || index >= Levels.Count || source == null)
            {
                return;
            }

            Levels[index].CopyFrom(source);
        }

        public PlaceableDefinition FindPlaceable(PlaceableId placeableId)
        {
            if (placeableId == PlaceableId.None)
            {
                return null;
            }

            for (int i = 0; i < PlaceableObjects.Count; i++)
            {
                var definition = PlaceableObjects[i];
                if (definition != null && definition.MatchesIdentity(placeableId))
                {
                    return definition;
                }
            }

            return null;
        }

        public PlaceableDefinition FindPlaceable(PlacedObjectData placedObject)
        {
            return FindPlaceable(placedObject.PlaceableId);
        }

        public bool TryGetDefaultBlockPlaceable(PigColor color, out PlaceableDefinition definition)
        {
            for (int i = 0; i < PlaceableObjects.Count; i++)
            {
                var candidate = PlaceableObjects[i];
                if (candidate != null && candidate.MatchesImportColor(color))
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public bool TryGetDefaultPigPlaceable(PigColor color, out PlaceableDefinition definition)
        {
            return TryGetDefaultBlockPlaceable(color, out definition);
        }

        public void ResetDefaultPlaceables()
        {
#if UNITY_EDITOR
            var blockPrefab = LoadEditorDefaultPrefab(DefaultBlockPrefabGuid);
#else
            GameObject blockPrefab = null;
#endif

            PlaceableObjects.Clear();
            PlaceableObjects.Add(PlaceableDefinition.CreateObstacle(PlaceableId.ObstacleBlock, "Block", new UnityEngine.Color(0.15f, 0.15f, 0.15f), "X", blockPrefab));
            for (int i = 0; i < DefaultBlockPlaceables.Length; i++)
            {
                var entry = DefaultBlockPlaceables[i];
                PlaceableObjects.Add(PlaceableDefinition.CreateBlock(entry.Id, entry.Name, entry.Color, blockPrefab));
            }

            EnsureConsistency();
        }

        public void EnsureConsistency()
        {
            levels ??= new List<LevelData>();
            placeableObjects ??= new List<PlaceableDefinition>();
            var changed = false;
#if UNITY_EDITOR
            changed = EnsureBuiltinPlaceables();
#endif

            var usedIds = new HashSet<PlaceableId>();
            for (int i = 0; i < PlaceableObjects.Count; i++)
            {
                if (PlaceableObjects[i] == null)
                {
                    continue;
                }

                PlaceableObjects[i].EnsureIdentity(usedIds);
#if UNITY_EDITOR
                if (PlaceableObjects[i].Prefab == null
                    || (PlaceableObjects[i].Kind == PlaceableKind.Block
                        && IsEditorDefaultPrefab(PlaceableObjects[i].Prefab, DefaultPigPrefabGuid)))
                {
                    PlaceableObjects[i].EditorAssignPrefab(ResolveEditorDefaultPrefab(PlaceableObjects[i]));
                    changed = true;
                }
#endif
            }

            for (int i = 0; i < Levels.Count; i++)
            {
                Levels[i]?.EnsureConsistency();
            }

#if UNITY_EDITOR
            if (changed && !Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }

        private void OnValidate()
        {
            EnsureConsistency();
        }

#if UNITY_EDITOR
        private bool EnsureBuiltinPlaceables()
        {
            var changed = false;

            if (!HasPlaceable(PlaceableId.ObstacleBlock))
            {
                PlaceableObjects.Insert(0,
                    PlaceableDefinition.CreateObstacle(
                        PlaceableId.ObstacleBlock,
                        "Block",
                        new UnityEngine.Color(0.15f, 0.15f, 0.15f),
                        "X",
                        LoadEditorDefaultPrefab(DefaultBlockPrefabGuid)));
                changed = true;
            }

            var blockPrefab = LoadEditorDefaultPrefab(DefaultBlockPrefabGuid);
            for (int i = 0; i < DefaultBlockPlaceables.Length; i++)
            {
                var entry = DefaultBlockPlaceables[i];
                if (HasPlaceable(entry.Id))
                {
                    continue;
                }

                PlaceableObjects.Add(PlaceableDefinition.CreateBlock(entry.Id, entry.Name, entry.Color, blockPrefab));
                changed = true;
            }

            return changed;
        }

        private bool HasPlaceable(PlaceableId placeableId)
        {
            for (int i = 0; i < PlaceableObjects.Count; i++)
            {
                var definition = PlaceableObjects[i];
                if (definition != null && definition.MatchesIdentity(placeableId))
                {
                    return true;
                }
            }

            return false;
        }

        private static GameObject ResolveEditorDefaultPrefab(PlaceableDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            return LoadEditorDefaultPrefab(DefaultBlockPrefabGuid);
        }

        private static bool IsEditorDefaultPrefab(GameObject prefab, string guid)
        {
            if (prefab == null || string.IsNullOrWhiteSpace(guid))
            {
                return false;
            }

            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            return string.Equals(UnityEditor.AssetDatabase.AssetPathToGUID(assetPath), guid, StringComparison.OrdinalIgnoreCase);
        }

        private static GameObject LoadEditorDefaultPrefab(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(assetPath)
                ? null
                : UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        }
#endif
    }
}
