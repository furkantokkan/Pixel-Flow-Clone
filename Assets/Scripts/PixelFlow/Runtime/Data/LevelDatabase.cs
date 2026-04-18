using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PixelFlow.Runtime.Data
{
    public enum PlaceableId
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

    public enum PlaceableKind
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

    public static class PlaceableIdUtility
    {
        public static bool TryParseLegacyId(string legacyId, out PlaceableId placeableId)
        {
            switch (legacyId?.Trim())
            {
                case "obstacle-block":
                    placeableId = PlaceableId.ObstacleBlock;
                    return true;
                case "pig-pink-basic":
                    placeableId = PlaceableId.PigPinkBasic;
                    return true;
                case "pig-blue-basic":
                    placeableId = PlaceableId.PigBlueBasic;
                    return true;
                case "pig-green-basic":
                    placeableId = PlaceableId.PigGreenBasic;
                    return true;
                case "pig-yellow-basic":
                    placeableId = PlaceableId.PigYellowBasic;
                    return true;
                case "pig-orange-basic":
                    placeableId = PlaceableId.PigOrangeBasic;
                    return true;
                case "pig-purple-basic":
                    placeableId = PlaceableId.PigPurpleBasic;
                    return true;
                case "pig-black-basic":
                    placeableId = PlaceableId.PigBlackBasic;
                    return true;
                case "pig-red-basic":
                    placeableId = PlaceableId.PigRedBasic;
                    return true;
                case "pig-teal-basic":
                    placeableId = PlaceableId.PigTealBasic;
                    return true;
                case "pig-gray-basic":
                    placeableId = PlaceableId.PigGrayBasic;
                    return true;
                case "pig-white-basic":
                    placeableId = PlaceableId.PigWhiteBasic;
                    return true;
                default:
                    placeableId = PlaceableId.None;
                    return false;
            }
        }

        public static string ToLegacyId(PlaceableId placeableId)
        {
            return placeableId switch
            {
                PlaceableId.ObstacleBlock => "obstacle-block",
                PlaceableId.PigPinkBasic => "pig-pink-basic",
                PlaceableId.PigBlueBasic => "pig-blue-basic",
                PlaceableId.PigGreenBasic => "pig-green-basic",
                PlaceableId.PigYellowBasic => "pig-yellow-basic",
                PlaceableId.PigOrangeBasic => "pig-orange-basic",
                PlaceableId.PigPurpleBasic => "pig-purple-basic",
                PlaceableId.PigBlackBasic => "pig-black-basic",
                PlaceableId.PigRedBasic => "pig-red-basic",
                PlaceableId.PigTealBasic => "pig-teal-basic",
                PlaceableId.PigGrayBasic => "pig-gray-basic",
                PlaceableId.PigWhiteBasic => "pig-white-basic",
                _ => string.Empty,
            };
        }

        public static PlaceableId GetSuggestedId(PlaceableKind kind, PigColor color)
        {
            if (kind == PlaceableKind.Obstacle)
            {
                return PlaceableId.ObstacleBlock;
            }

            return color switch
            {
                PigColor.Red => PlaceableId.PigRedBasic,
                PigColor.Pink => PlaceableId.PigPinkBasic,
                PigColor.Blue => PlaceableId.PigBlueBasic,
                PigColor.Green => PlaceableId.PigGreenBasic,
                PigColor.Yellow => PlaceableId.PigYellowBasic,
                PigColor.Orange => PlaceableId.PigOrangeBasic,
                PigColor.Teal => PlaceableId.PigTealBasic,
                PigColor.Purple => PlaceableId.PigPurpleBasic,
                PigColor.Gray => PlaceableId.PigGrayBasic,
                PigColor.White => PlaceableId.PigWhiteBasic,
                PigColor.Black => PlaceableId.PigBlackBasic,
                _ => PlaceableId.None,
            };
        }
    }

    [Serializable]
    public struct PlacedObjectData
    {
        [SerializeField] private PlaceableId placeableKey;
        [SerializeField, HideInInspector, FormerlySerializedAs("placeableId")] private string legacyPlaceableId;
        [SerializeField] private Vector2Int origin;

        public PlacedObjectData(PlaceableId placeableId, Vector2Int origin)
        {
            placeableKey = placeableId;
            legacyPlaceableId = PlaceableIdUtility.ToLegacyId(placeableId);
            this.origin = origin;
        }

        public PlaceableId PlaceableId => placeableKey;
        public string LegacyPlaceableId => legacyPlaceableId;
        public Vector2Int Origin => origin;

        public bool EnsureIdentity()
        {
            var previousPlaceableId = placeableKey;
            var previousLegacyId = legacyPlaceableId;

            if (placeableKey == PlaceableId.None
                && PlaceableIdUtility.TryParseLegacyId(legacyPlaceableId, out var parsedId))
            {
                placeableKey = parsedId;
            }

            if (placeableKey != PlaceableId.None)
            {
                legacyPlaceableId = PlaceableIdUtility.ToLegacyId(placeableKey);
            }

            return previousPlaceableId != placeableKey
                || !string.Equals(previousLegacyId, legacyPlaceableId, StringComparison.Ordinal);
        }
    }

    [Serializable]
    public sealed class PlaceableDefinition
    {
        [SerializeField, InspectorName("PlacableID")] private PlaceableId placeableKey;
        [SerializeField, HideInInspector, FormerlySerializedAs("id")] private string legacyId;
        [SerializeField] private string displayName = "Placeable";
        [SerializeField, InspectorName("Type")] private PlaceableKind kind = PlaceableKind.Pig;
        [SerializeField] private PigColor color = PigColor.Pink;
        [SerializeField, InspectorName("Special Condition")] private PigAbilityType pigAbility = PigAbilityType.None;
        [SerializeField] private Vector2Int gridSize = Vector2Int.one;
        [SerializeField] private UnityEngine.Color editorColor = UnityEngine.Color.white;
        [SerializeField] private string editorGlyph;
        [SerializeField] private GameObject prefab;

        public PlaceableId Id => placeableKey;
        public string LegacyId => legacyId;
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
            return kind == PlaceableKind.Pig
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

        public bool MatchesIdentity(PlaceableId candidateId, string candidateLegacyId = null)
        {
            if (placeableKey != PlaceableId.None)
            {
                if (candidateId != PlaceableId.None)
                {
                    return placeableKey == candidateId;
                }

                return PlaceableIdUtility.TryParseLegacyId(candidateLegacyId, out var parsedCandidateId)
                    && placeableKey == parsedCandidateId;
            }

            if (candidateId != PlaceableId.None
                && PlaceableIdUtility.TryParseLegacyId(legacyId, out var parsedDefinitionId))
            {
                return parsedDefinitionId == candidateId;
            }

            return !string.IsNullOrWhiteSpace(legacyId)
                && !string.IsNullOrWhiteSpace(candidateLegacyId)
                && string.Equals(legacyId, candidateLegacyId, StringComparison.OrdinalIgnoreCase);
        }

        public void EnsureIdentity(HashSet<PlaceableId> usedIds)
        {
            if (placeableKey == PlaceableId.None
                && PlaceableIdUtility.TryParseLegacyId(legacyId, out var parsedLegacyId))
            {
                placeableKey = parsedLegacyId;
            }

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
                legacyId = PlaceableIdUtility.ToLegacyId(placeableKey);
                usedIds?.Add(placeableKey);
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = kind == PlaceableKind.Obstacle
                    ? "Obstacle"
                    : $"{color} Pig";
            }

            gridSize = new Vector2Int(Mathf.Max(1, gridSize.x), Mathf.Max(1, gridSize.y));
        }

        public static PlaceableDefinition CreatePig(PlaceableId id, string name, PigColor color, GameObject prefab = null)
        {
            return new PlaceableDefinition
            {
                placeableKey = id,
                legacyId = PlaceableIdUtility.ToLegacyId(id),
                displayName = name,
                kind = PlaceableKind.Pig,
                color = color,
                pigAbility = PigAbilityType.None,
                gridSize = Vector2Int.one,
                editorColor = PigColorPaletteUtility.GetDisplayColor(color),
                prefab = prefab,
            };
        }

        public static PlaceableDefinition CreateObstacle(PlaceableId id, string name, UnityEngine.Color editorColor, string glyph = "X", GameObject prefab = null)
        {
            return new PlaceableDefinition
            {
                placeableKey = id,
                legacyId = PlaceableIdUtility.ToLegacyId(id),
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
        [SerializeField] private Texture2D sourceImage;
        [SerializeField] private BlockData blockData;
        [SerializeField] private ImageImportSettings importSettings = new();
        [SerializeField] private List<PlacedObjectData> placedObjects = new();
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
                conveyorBakeData = ConveyorBakeData,
                placedObjects = new List<PlacedObjectData>(PlacedObjects.Count),
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
            placedObjects ??= new List<PlacedObjectData>();
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

    [CreateAssetMenu(fileName = "LevelDatabase", menuName = "Pixel Flow/Level Database")]
    public sealed class LevelDatabase : ScriptableObject
    {
#if UNITY_EDITOR
        private const string DefaultBlockPrefabGuid = "331edf6250de4de4a909c2115614f2d7";
        private const string DefaultPigPrefabGuid = "5f47c7c5de4f2ce4dbe9606dc849868f";
#endif
        private static readonly (PlaceableId Id, string Name, PigColor Color)[] DefaultPigPlaceables =
        {
            (PlaceableId.PigRedBasic, "Red Pig", PigColor.Red),
            (PlaceableId.PigPinkBasic, "Pink Pig", PigColor.Pink),
            (PlaceableId.PigBlueBasic, "Blue Pig", PigColor.Blue),
            (PlaceableId.PigGreenBasic, "Green Pig", PigColor.Green),
            (PlaceableId.PigYellowBasic, "Yellow Pig", PigColor.Yellow),
            (PlaceableId.PigOrangeBasic, "Orange Pig", PigColor.Orange),
            (PlaceableId.PigTealBasic, "Teal Pig", PigColor.Teal),
            (PlaceableId.PigPurpleBasic, "Purple Pig", PigColor.Purple),
            (PlaceableId.PigGrayBasic, "Gray Pig", PigColor.Gray),
            (PlaceableId.PigWhiteBasic, "White Pig", PigColor.White),
            (PlaceableId.PigBlackBasic, "Black Pig", PigColor.Black),
        };

        [SerializeField] private List<LevelData> levels = new();
        [SerializeField] private List<PlaceableDefinition> placeableObjects = new();

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
            return FindPlaceable(placeableId, null);
        }

        public PlaceableDefinition FindPlaceable(PlacedObjectData placedObject)
        {
            return FindPlaceable(placedObject.PlaceableId, placedObject.LegacyPlaceableId);
        }

        public PlaceableDefinition FindPlaceable(PlaceableId placeableId, string legacyPlaceableId)
        {
            if (placeableId == PlaceableId.None && string.IsNullOrWhiteSpace(legacyPlaceableId))
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

        public bool TryGetDefaultPigPlaceable(PigColor color, out PlaceableDefinition definition)
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
            PlaceableObjects.Add(PlaceableDefinition.CreateObstacle(PlaceableId.ObstacleBlock, "Block", new UnityEngine.Color(0.15f, 0.15f, 0.15f), "X", blockPrefab));
            for (int i = 0; i < DefaultPigPlaceables.Length; i++)
            {
                var entry = DefaultPigPlaceables[i];
                PlaceableObjects.Add(PlaceableDefinition.CreatePig(entry.Id, entry.Name, entry.Color, pigPrefab));
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

            var pigPrefab = LoadEditorDefaultPrefab(DefaultPigPrefabGuid);
            for (int i = 0; i < DefaultPigPlaceables.Length; i++)
            {
                var entry = DefaultPigPlaceables[i];
                if (HasPlaceable(entry.Id))
                {
                    continue;
                }

                PlaceableObjects.Add(PlaceableDefinition.CreatePig(entry.Id, entry.Name, entry.Color, pigPrefab));
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

            return LoadEditorDefaultPrefab(
                definition.Kind == PlaceableKind.Obstacle
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
