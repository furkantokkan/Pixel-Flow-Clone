using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PixelFlow.Runtime.Data
{
    public enum PixelFlowPlaceableId
    {
        None = 0,
        ObstacleBlock = 1,
        PigPinkBasic = 2,
        PigBlueBasic = 3,
        PigGreenBasic = 4,
        PigYellowBasic = 5,
        PigOrangeBasic = 6,
        PigPurpleBasic = 7,
        PigBlackBasic = 8,
        PigRedBasic = 9,
        PigTealBasic = 10,
        PigGrayBasic = 11,
        PigWhiteBasic = 12,
    }

    public enum PixelFlowPlaceableKind
    {
        Pig = 0,
        Obstacle = 1,
    }

    public enum PigAbilityType
    {
        None = 0,
        Chain = 1,
        Special = 2,
    }

    public static class PixelFlowPlaceableIdUtility
    {
        public static bool TryParseLegacyId(string legacyId, out PixelFlowPlaceableId placeableId)
        {
            switch (legacyId?.Trim())
            {
                case "obstacle-block":
                    placeableId = PixelFlowPlaceableId.ObstacleBlock;
                    return true;
                case "pig-pink-basic":
                    placeableId = PixelFlowPlaceableId.PigPinkBasic;
                    return true;
                case "pig-blue-basic":
                    placeableId = PixelFlowPlaceableId.PigBlueBasic;
                    return true;
                case "pig-green-basic":
                    placeableId = PixelFlowPlaceableId.PigGreenBasic;
                    return true;
                case "pig-yellow-basic":
                    placeableId = PixelFlowPlaceableId.PigYellowBasic;
                    return true;
                case "pig-orange-basic":
                    placeableId = PixelFlowPlaceableId.PigOrangeBasic;
                    return true;
                case "pig-purple-basic":
                    placeableId = PixelFlowPlaceableId.PigPurpleBasic;
                    return true;
                case "pig-black-basic":
                    placeableId = PixelFlowPlaceableId.PigBlackBasic;
                    return true;
                case "pig-red-basic":
                    placeableId = PixelFlowPlaceableId.PigRedBasic;
                    return true;
                case "pig-teal-basic":
                    placeableId = PixelFlowPlaceableId.PigTealBasic;
                    return true;
                case "pig-gray-basic":
                    placeableId = PixelFlowPlaceableId.PigGrayBasic;
                    return true;
                case "pig-white-basic":
                    placeableId = PixelFlowPlaceableId.PigWhiteBasic;
                    return true;
                default:
                    placeableId = PixelFlowPlaceableId.None;
                    return false;
            }
        }

        public static string ToLegacyId(PixelFlowPlaceableId placeableId)
        {
            return placeableId switch
            {
                PixelFlowPlaceableId.ObstacleBlock => "obstacle-block",
                PixelFlowPlaceableId.PigPinkBasic => "pig-pink-basic",
                PixelFlowPlaceableId.PigBlueBasic => "pig-blue-basic",
                PixelFlowPlaceableId.PigGreenBasic => "pig-green-basic",
                PixelFlowPlaceableId.PigYellowBasic => "pig-yellow-basic",
                PixelFlowPlaceableId.PigOrangeBasic => "pig-orange-basic",
                PixelFlowPlaceableId.PigPurpleBasic => "pig-purple-basic",
                PixelFlowPlaceableId.PigBlackBasic => "pig-black-basic",
                PixelFlowPlaceableId.PigRedBasic => "pig-red-basic",
                PixelFlowPlaceableId.PigTealBasic => "pig-teal-basic",
                PixelFlowPlaceableId.PigGrayBasic => "pig-gray-basic",
                PixelFlowPlaceableId.PigWhiteBasic => "pig-white-basic",
                _ => string.Empty,
            };
        }

        public static PixelFlowPlaceableId GetSuggestedId(PixelFlowPlaceableKind kind, PigColor color)
        {
            if (kind == PixelFlowPlaceableKind.Obstacle)
            {
                return PixelFlowPlaceableId.ObstacleBlock;
            }

            return color switch
            {
                PigColor.Red => PixelFlowPlaceableId.PigRedBasic,
                PigColor.Pink => PixelFlowPlaceableId.PigPinkBasic,
                PigColor.Blue => PixelFlowPlaceableId.PigBlueBasic,
                PigColor.Green => PixelFlowPlaceableId.PigGreenBasic,
                PigColor.Yellow => PixelFlowPlaceableId.PigYellowBasic,
                PigColor.Orange => PixelFlowPlaceableId.PigOrangeBasic,
                PigColor.Teal => PixelFlowPlaceableId.PigTealBasic,
                PigColor.Purple => PixelFlowPlaceableId.PigPurpleBasic,
                PigColor.Gray => PixelFlowPlaceableId.PigGrayBasic,
                PigColor.White => PixelFlowPlaceableId.PigWhiteBasic,
                PigColor.Black => PixelFlowPlaceableId.PigBlackBasic,
                _ => PixelFlowPlaceableId.None,
            };
        }
    }

    [Serializable]
    public struct PixelFlowPlacedObjectData
    {
        [SerializeField] private PixelFlowPlaceableId placeableKey;
        [SerializeField, HideInInspector, FormerlySerializedAs("placeableId")] private string legacyPlaceableId;
        [SerializeField] private Vector2Int origin;

        public PixelFlowPlacedObjectData(PixelFlowPlaceableId placeableId, Vector2Int origin)
        {
            placeableKey = placeableId;
            legacyPlaceableId = PixelFlowPlaceableIdUtility.ToLegacyId(placeableId);
            this.origin = origin;
        }

        public PixelFlowPlaceableId PlaceableId => placeableKey;
        public string LegacyPlaceableId => legacyPlaceableId;
        public Vector2Int Origin => origin;

        public bool EnsureIdentity()
        {
            var previousPlaceableId = placeableKey;
            var previousLegacyId = legacyPlaceableId;

            if (placeableKey == PixelFlowPlaceableId.None
                && PixelFlowPlaceableIdUtility.TryParseLegacyId(legacyPlaceableId, out var parsedId))
            {
                placeableKey = parsedId;
            }

            if (placeableKey != PixelFlowPlaceableId.None)
            {
                legacyPlaceableId = PixelFlowPlaceableIdUtility.ToLegacyId(placeableKey);
            }

            return previousPlaceableId != placeableKey
                || !string.Equals(previousLegacyId, legacyPlaceableId, StringComparison.Ordinal);
        }
    }

    [Serializable]
    public sealed class PixelFlowPlaceableDefinition
    {
        [SerializeField, InspectorName("PlacableID")] private PixelFlowPlaceableId placeableKey;
        [SerializeField, HideInInspector, FormerlySerializedAs("id")] private string legacyId;
        [SerializeField] private string displayName = "Placeable";
        [SerializeField, InspectorName("Type")] private PixelFlowPlaceableKind kind = PixelFlowPlaceableKind.Pig;
        [SerializeField] private PigColor color = PigColor.Pink;
        [SerializeField, InspectorName("Special Condition")] private PigAbilityType pigAbility = PigAbilityType.None;
        [SerializeField] private Vector2Int gridSize = Vector2Int.one;
        [SerializeField] private UnityEngine.Color editorColor = UnityEngine.Color.white;
        [SerializeField] private string editorGlyph;
        [SerializeField] private GameObject prefab;

        public PixelFlowPlaceableId Id => placeableKey;
        public string LegacyId => legacyId;
        public string DisplayName => displayName;
        public PixelFlowPlaceableKind Kind => kind;
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
            return kind == PixelFlowPlaceableKind.Pig
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
            if (kind == PixelFlowPlaceableKind.Obstacle)
            {
                return "X";
            }

            if (size.x * size.y > 1)
            {
                return $"{size.x}x{size.y}";
            }

            return string.Empty;
        }

        public bool MatchesIdentity(PixelFlowPlaceableId candidateId, string candidateLegacyId = null)
        {
            if (placeableKey != PixelFlowPlaceableId.None)
            {
                if (candidateId != PixelFlowPlaceableId.None)
                {
                    return placeableKey == candidateId;
                }

                return PixelFlowPlaceableIdUtility.TryParseLegacyId(candidateLegacyId, out var parsedCandidateId)
                    && placeableKey == parsedCandidateId;
            }

            if (candidateId != PixelFlowPlaceableId.None
                && PixelFlowPlaceableIdUtility.TryParseLegacyId(legacyId, out var parsedDefinitionId))
            {
                return parsedDefinitionId == candidateId;
            }

            return !string.IsNullOrWhiteSpace(legacyId)
                && !string.IsNullOrWhiteSpace(candidateLegacyId)
                && string.Equals(legacyId, candidateLegacyId, StringComparison.OrdinalIgnoreCase);
        }

        public void EnsureIdentity(HashSet<PixelFlowPlaceableId> usedIds)
        {
            if (placeableKey == PixelFlowPlaceableId.None
                && PixelFlowPlaceableIdUtility.TryParseLegacyId(legacyId, out var parsedLegacyId))
            {
                placeableKey = parsedLegacyId;
            }

            if (placeableKey == PixelFlowPlaceableId.None)
            {
                var suggestedId = PixelFlowPlaceableIdUtility.GetSuggestedId(kind, color);
                if (suggestedId != PixelFlowPlaceableId.None
                    && (usedIds == null || !usedIds.Contains(suggestedId)))
                {
                    placeableKey = suggestedId;
                }
            }

            if (placeableKey != PixelFlowPlaceableId.None)
            {
                legacyId = PixelFlowPlaceableIdUtility.ToLegacyId(placeableKey);
                usedIds?.Add(placeableKey);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = kind == PixelFlowPlaceableKind.Obstacle
                    ? "Obstacle"
                    : $"{color} Pig";
            }

            gridSize = new Vector2Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
        }

        public static PixelFlowPlaceableDefinition CreatePig(PixelFlowPlaceableId id, string name, PigColor color, GameObject prefab = null)
        {
            return new PixelFlowPlaceableDefinition
            {
                placeableKey = id,
                legacyId = PixelFlowPlaceableIdUtility.ToLegacyId(id),
                displayName = name,
                kind = PixelFlowPlaceableKind.Pig,
                color = color,
                pigAbility = PigAbilityType.None,
                gridSize = Vector2Int.one,
                editorColor = PigColorPaletteUtility.GetDisplayColor(color),
                prefab = prefab,
            };
        }

        public static PixelFlowPlaceableDefinition CreateObstacle(PixelFlowPlaceableId id, string name, UnityEngine.Color editorColor, string glyph = "X", GameObject prefab = null)
        {
            return new PixelFlowPlaceableDefinition
            {
                placeableKey = id,
                legacyId = PixelFlowPlaceableIdUtility.ToLegacyId(id),
                displayName = name,
                kind = PixelFlowPlaceableKind.Obstacle,
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
    public sealed class PixelFlowLevelData
    {
        [SerializeField] private string levelName = "Level";
        [SerializeField] private Vector2Int gridSize = new(12, 12);
        [SerializeField] private Texture2D sourceImage;
        [SerializeField] private PixelFlowBlockData blockData;
        [SerializeField] private ImageImportSettings importSettings = new();
        [SerializeField] private List<PixelFlowPlacedObjectData> placedObjects = new();
        [SerializeField] private List<WaitingSlotData> waitingSlots = new();
        [SerializeField] private List<PigQueueEntry> pigQueue = new();
        [SerializeField] private PigQueueGenerationSettings pigQueueGenerationSettings = new();
        [SerializeField] private ConveyorBakeData conveyorBakeData = new();

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

        public PixelFlowBlockData BlockData
        {
            get => blockData;
            set => blockData = value;
        }

        public ImageImportSettings ImportSettings
        {
            get => importSettings ??= new ImageImportSettings();
            set => importSettings = value?.Clone() ?? new ImageImportSettings();
        }

        public List<PixelFlowPlacedObjectData> PlacedObjects => placedObjects ??= new List<PixelFlowPlacedObjectData>();
        public List<WaitingSlotData> WaitingSlots => waitingSlots ??= new List<WaitingSlotData>();
        public List<PigQueueEntry> PigQueue => pigQueue ??= new List<PigQueueEntry>();

        public PigQueueGenerationSettings PigQueueGenerationSettings
        {
            get => pigQueueGenerationSettings ??= new PigQueueGenerationSettings();
            set => pigQueueGenerationSettings = value?.Clone() ?? new PigQueueGenerationSettings();
        }

        public ConveyorBakeData ConveyorBakeData
        {
            get => conveyorBakeData ??= new ConveyorBakeData();
            set => conveyorBakeData = value ?? new ConveyorBakeData();
        }

        public PixelFlowLevelData Clone()
        {
            var clone = new PixelFlowLevelData
            {
                levelName = levelName,
                gridSize = GridSize,
                sourceImage = sourceImage,
                blockData = blockData,
                importSettings = ImportSettings.Clone(),
                pigQueueGenerationSettings = PigQueueGenerationSettings.Clone(),
                conveyorBakeData = ConveyorBakeData,
                placedObjects = new List<PixelFlowPlacedObjectData>(PlacedObjects.Count),
                waitingSlots = new List<WaitingSlotData>(WaitingSlots.Count),
                pigQueue = new List<PigQueueEntry>(PigQueue.Count),
            };

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                clone.placedObjects.Add(PlacedObjects[i]);
            }

            for (int i = 0; i < WaitingSlots.Count; i++)
            {
                clone.waitingSlots.Add(WaitingSlots[i]);
            }

            for (int i = 0; i < PigQueue.Count; i++)
            {
                clone.pigQueue.Add(PigQueue[i]);
            }

            return clone;
        }

        public void CopyFrom(PixelFlowLevelData other)
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
            conveyorBakeData = other.ConveyorBakeData;

            PlacedObjects.Clear();
            WaitingSlots.Clear();
            PigQueue.Clear();

            for (int i = 0; i < other.PlacedObjects.Count; i++)
            {
                PlacedObjects.Add(other.PlacedObjects[i]);
            }

            for (int i = 0; i < other.WaitingSlots.Count; i++)
            {
                WaitingSlots.Add(other.WaitingSlots[i]);
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
            conveyorBakeData ??= new ConveyorBakeData();
            placedObjects ??= new List<PixelFlowPlacedObjectData>();
            waitingSlots ??= new List<WaitingSlotData>();
            pigQueue ??= new List<PigQueueEntry>();

            for (int i = 0; i < placedObjects.Count; i++)
            {
                var placedObject = placedObjects[i];
                if (placedObject.EnsureIdentity())
                {
                    placedObjects[i] = placedObject;
                }
            }

            LevelName = levelName;
        }
    }

    [CreateAssetMenu(fileName = "PixelFlowLevelDatabase", menuName = "Pixel Flow/Level Database")]
    public sealed class PixelFlowLevelDatabase : ScriptableObject
    {
#if UNITY_EDITOR
        private const string DefaultBlockPrefabGuid = "331edf6250de4de4a909c2115614f2d7";
        private const string DefaultPigPrefabGuid = "5f47c7c5de4f2ce4dbe9606dc849868f";
#endif
        private static readonly (PixelFlowPlaceableId Id, string Name, PigColor Color)[] DefaultPigPlaceables =
        {
            (PixelFlowPlaceableId.PigRedBasic, "Red Pig", PigColor.Red),
            (PixelFlowPlaceableId.PigPinkBasic, "Pink Pig", PigColor.Pink),
            (PixelFlowPlaceableId.PigBlueBasic, "Blue Pig", PigColor.Blue),
            (PixelFlowPlaceableId.PigGreenBasic, "Green Pig", PigColor.Green),
            (PixelFlowPlaceableId.PigYellowBasic, "Yellow Pig", PigColor.Yellow),
            (PixelFlowPlaceableId.PigOrangeBasic, "Orange Pig", PigColor.Orange),
            (PixelFlowPlaceableId.PigTealBasic, "Teal Pig", PigColor.Teal),
            (PixelFlowPlaceableId.PigPurpleBasic, "Purple Pig", PigColor.Purple),
            (PixelFlowPlaceableId.PigGrayBasic, "Gray Pig", PigColor.Gray),
            (PixelFlowPlaceableId.PigWhiteBasic, "White Pig", PigColor.White),
            (PixelFlowPlaceableId.PigBlackBasic, "Black Pig", PigColor.Black),
        };

        [SerializeField] private List<PixelFlowLevelData> levels = new();
        [SerializeField] private List<PixelFlowPlaceableDefinition> placeableObjects = new();

        public List<PixelFlowLevelData> Levels => levels ??= new List<PixelFlowLevelData>();
        public List<PixelFlowPlaceableDefinition> PlaceableObjects => placeableObjects ??= new List<PixelFlowPlaceableDefinition>();

        public PixelFlowLevelData CreateLevel(string levelName)
        {
            var level = new PixelFlowLevelData
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

        public void SetLevelAt(int index, PixelFlowLevelData source)
        {
            if (index < 0 || index >= Levels.Count || source == null)
            {
                return;
            }

            Levels[index].CopyFrom(source);
        }

        public PixelFlowPlaceableDefinition FindPlaceable(PixelFlowPlaceableId placeableId)
        {
            return FindPlaceable(placeableId, null);
        }

        public PixelFlowPlaceableDefinition FindPlaceable(PixelFlowPlacedObjectData placedObject)
        {
            return FindPlaceable(placedObject.PlaceableId, placedObject.LegacyPlaceableId);
        }

        public PixelFlowPlaceableDefinition FindPlaceable(PixelFlowPlaceableId placeableId, string legacyPlaceableId)
        {
            if (placeableId == PixelFlowPlaceableId.None && string.IsNullOrWhiteSpace(legacyPlaceableId))
            {
                return null;
            }

            for (int i = 0; i < PlaceableObjects.Count; i++)
            {
                var definition = PlaceableObjects[i];
                if (definition != null && definition.MatchesIdentity(placeableId, legacyPlaceableId))
                {
                    return definition;
                }
            }

            return null;
        }

        public bool TryGetDefaultPigPlaceable(PigColor color, out PixelFlowPlaceableDefinition definition)
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

        public void ResetDefaultPlaceables()
        {
#if UNITY_EDITOR
            var blockPrefab = LoadEditorDefaultPrefab(DefaultBlockPrefabGuid);
            var pigPrefab = LoadEditorDefaultPrefab(DefaultPigPrefabGuid);
#else
            GameObject blockPrefab = null;
            GameObject pigPrefab = null;
#endif

            PlaceableObjects.Clear();
            PlaceableObjects.Add(PixelFlowPlaceableDefinition.CreateObstacle(PixelFlowPlaceableId.ObstacleBlock, "Block", new UnityEngine.Color(0.15f, 0.15f, 0.15f), "X", blockPrefab));
            for (int i = 0; i < DefaultPigPlaceables.Length; i++)
            {
                var entry = DefaultPigPlaceables[i];
                PlaceableObjects.Add(PixelFlowPlaceableDefinition.CreatePig(entry.Id, entry.Name, entry.Color, pigPrefab));
            }

            EnsureConsistency();
        }

        public void EnsureConsistency()
        {
            levels ??= new List<PixelFlowLevelData>();
            placeableObjects ??= new List<PixelFlowPlaceableDefinition>();
            var changed = false;
#if UNITY_EDITOR
            changed = EnsureBuiltinPlaceables();
#endif

            var usedIds = new HashSet<PixelFlowPlaceableId>();
            for (int i = 0; i < PlaceableObjects.Count; i++)
            {
                if (PlaceableObjects[i] == null)
                {
                    continue;
                }

                PlaceableObjects[i].EnsureIdentity(usedIds);
#if UNITY_EDITOR
                if (PlaceableObjects[i].Prefab == null)
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

            if (!HasPlaceable(PixelFlowPlaceableId.ObstacleBlock))
            {
                PlaceableObjects.Insert(0,
                    PixelFlowPlaceableDefinition.CreateObstacle(
                        PixelFlowPlaceableId.ObstacleBlock,
                        "Block",
                        new UnityEngine.Color(0.15f, 0.15f, 0.15f),
                        "X",
                        LoadEditorDefaultPrefab(DefaultBlockPrefabGuid)));
                changed = true;
            }

            var pigPrefab = LoadEditorDefaultPrefab(DefaultPigPrefabGuid);
            for (int i = 0; i < DefaultPigPlaceables.Length; i++)
            {
                var entry = DefaultPigPlaceables[i];
                if (HasPlaceable(entry.Id))
                {
                    continue;
                }

                PlaceableObjects.Add(PixelFlowPlaceableDefinition.CreatePig(entry.Id, entry.Name, entry.Color, pigPrefab));
                changed = true;
            }

            return changed;
        }

        private bool HasPlaceable(PixelFlowPlaceableId placeableId)
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

        private static GameObject ResolveEditorDefaultPrefab(PixelFlowPlaceableDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            return LoadEditorDefaultPrefab(
                definition.Kind == PixelFlowPlaceableKind.Obstacle
                    ? DefaultBlockPrefabGuid
                    : DefaultPigPrefabGuid);
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
