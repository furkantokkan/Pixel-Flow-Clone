#if UNITY_EDITOR
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace PixelFlow.Editor.LevelEditing
{
    internal readonly struct LevelGuaranteedCompletionValidationResult
    {
        public LevelGuaranteedCompletionValidationResult(bool isGuaranteed, string message, MessageType messageType)
        {
            IsGuaranteed = isGuaranteed;
            Message = message ?? string.Empty;
            MessageType = messageType;
        }

        public bool IsGuaranteed { get; }
        public string Message { get; }
        public MessageType MessageType { get; }
    }

    internal static class LevelGuaranteedCompletionValidator
    {
        public static LevelGuaranteedCompletionValidationResult Validate(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database,
            IReadOnlyList<PigQueueEntry> pigQueue)
        {
            if (database == null)
            {
                return Warning("Assign a level database before validating guaranteed completion.");
            }

            if (placedObjects == null || placedObjects.Count == 0)
            {
                return Warning("Paint blocks or place obstacles before validating guaranteed completion.");
            }

            if (pigQueue == null || pigQueue.Count == 0)
            {
                return Warning("Generate or edit a pig queue before validating guaranteed completion.");
            }

            if (!TryBuildPlacedObjectGrid(placedObjects, database, out var occupied, out var targetColors, out var targetCount, out var missingDefinitions))
            {
                return missingDefinitions > 0
                    ? Warning($"Guaranteed completion could not be validated because {missingDefinitions} placed object(s) are missing from the current database.")
                    : Warning("Current grid does not contain any valid placeable definitions to validate.");
            }

            if (missingDefinitions > 0)
            {
                return Warning($"Guaranteed completion could not be validated because {missingDefinitions} placed object(s) are missing from the current database.");
            }

            if (targetCount <= 0)
            {
                return Warning("Current grid has no target blocks, so there is nothing to validate.");
            }

            if (!TryResolveExposureLayers(occupied, targetColors, out var exposureLayers))
            {
                return Error("Guaranteed completion failed: some target blocks are permanently hidden behind blockers and never become exposed.");
            }

            var remainingAmmoByColor = new int[(int)PigColor.DarkBlue + 1];
            var totalAmmo = 0;
            for (int i = 0; i < pigQueue.Count; i++)
            {
                var entry = pigQueue[i];
                if (entry.Color == PigColor.None || entry.Ammo <= 0)
                {
                    continue;
                }

                remainingAmmoByColor[(int)entry.Color] += entry.Ammo;
                totalAmmo += entry.Ammo;
            }

            if (totalAmmo <= 0)
            {
                return Warning("Current pig queue has no usable ammo to validate.");
            }

            for (int layerIndex = 0; layerIndex < exposureLayers.Count; layerIndex++)
            {
                var layer = exposureLayers[layerIndex];
                foreach (var pair in layer)
                {
                    if (pair.Key == PigColor.None || pair.Value <= 0)
                    {
                        continue;
                    }

                    var colorIndex = (int)pair.Key;
                    var availableAmmo = remainingAmmoByColor[colorIndex];
                    if (availableAmmo < pair.Value)
                    {
                        return Error(
                            $"Guaranteed completion failed at exposed layer {layerIndex + 1}: {pair.Key} needs {pair.Value} reachable hits, but the current queue only has {availableAmmo} ammo left for that color.");
                    }

                    remainingAmmoByColor[colorIndex] = availableAmmo - pair.Value;
                }
            }

            return Success(
                $"Guaranteed completion passed. {targetCount} target blocks resolve across {exposureLayers.Count} exposed layer(s) with the current pig queue.");
        }

        private static bool TryBuildPlacedObjectGrid(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database,
            out bool[,] occupied,
            out PigColor[,] targetColors,
            out int targetCount,
            out int missingDefinitions)
        {
            occupied = null;
            targetColors = null;
            targetCount = 0;
            missingDefinitions = 0;
            if (placedObjects == null || database == null || placedObjects.Count == 0)
            {
                return false;
            }

            var hasBounds = false;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;

            for (int i = 0; i < placedObjects.Count; i++)
            {
                var placedObject = placedObjects[i];
                var definition = database.FindPlaceable(placedObject);
                if (definition == null)
                {
                    missingDefinitions++;
                    continue;
                }

                var size = definition.GridSize;
                minX = Mathf.Min(minX, placedObject.Origin.x);
                minY = Mathf.Min(minY, placedObject.Origin.y);
                maxX = Mathf.Max(maxX, placedObject.Origin.x + Mathf.Max(1, size.x) - 1);
                maxY = Mathf.Max(maxY, placedObject.Origin.y + Mathf.Max(1, size.y) - 1);
                hasBounds = true;
            }

            if (!hasBounds)
            {
                return false;
            }

            var width = Mathf.Max(1, maxX - minX + 1);
            var height = Mathf.Max(1, maxY - minY + 1);
            occupied = new bool[width, height];
            targetColors = new PigColor[width, height];

            for (int i = 0; i < placedObjects.Count; i++)
            {
                var placedObject = placedObjects[i];
                var definition = database.FindPlaceable(placedObject);
                if (definition == null)
                {
                    continue;
                }

                var size = definition.GridSize;
                for (int offsetX = 0; offsetX < size.x; offsetX++)
                {
                    for (int offsetY = 0; offsetY < size.y; offsetY++)
                    {
                        var gridX = (placedObject.Origin.x + offsetX) - minX;
                        var gridY = (placedObject.Origin.y + offsetY) - minY;
                        if (gridX < 0 || gridX >= width || gridY < 0 || gridY >= height)
                        {
                            continue;
                        }

                        occupied[gridX, gridY] = true;
                        targetColors[gridX, gridY] = definition.Kind == PlaceableKind.Block
                            ? definition.Color
                            : PigColor.None;
                        if (definition.Kind == PlaceableKind.Block && definition.Color != PigColor.None)
                        {
                            targetCount++;
                        }
                    }
                }
            }

            return true;
        }

        private static bool TryResolveExposureLayers(
            bool[,] occupied,
            PigColor[,] targetColors,
            out List<Dictionary<PigColor, int>> layers)
        {
            layers = new List<Dictionary<PigColor, int>>();
            if (occupied == null || targetColors == null)
            {
                return false;
            }

            var width = occupied.GetLength(0);
            var height = occupied.GetLength(1);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            while (HasRemainingTargets(targetColors))
            {
                var exposedTargets = new bool[width, height];
                MarkExposedTargets(occupied, targetColors, exposedTargets, width, height);
                var layerCounts = CollectAndRemoveExposedTargets(occupied, targetColors, exposedTargets, width, height);
                if (layerCounts.Count == 0)
                {
                    layers.Clear();
                    return false;
                }

                layers.Add(layerCounts);
            }

            return true;
        }

        private static void MarkExposedTargets(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int width,
            int height)
        {
            for (int y = 0; y < height; y++)
            {
                MarkFirstOccupiedInRow(occupied, targetColors, exposedTargets, y, width, leftToRight: true);
                MarkFirstOccupiedInRow(occupied, targetColors, exposedTargets, y, width, leftToRight: false);
            }

            for (int x = 0; x < width; x++)
            {
                MarkFirstOccupiedInColumn(occupied, targetColors, exposedTargets, x, height, bottomToTop: true);
                MarkFirstOccupiedInColumn(occupied, targetColors, exposedTargets, x, height, bottomToTop: false);
            }
        }

        private static void MarkFirstOccupiedInRow(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int rowIndex,
            int width,
            bool leftToRight)
        {
            if (leftToRight)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!occupied[x, rowIndex])
                    {
                        continue;
                    }

                    if (targetColors[x, rowIndex] != PigColor.None)
                    {
                        exposedTargets[x, rowIndex] = true;
                    }

                    break;
                }

                return;
            }

            for (int x = width - 1; x >= 0; x--)
            {
                if (!occupied[x, rowIndex])
                {
                    continue;
                }

                if (targetColors[x, rowIndex] != PigColor.None)
                {
                    exposedTargets[x, rowIndex] = true;
                }

                break;
            }
        }

        private static void MarkFirstOccupiedInColumn(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int columnIndex,
            int height,
            bool bottomToTop)
        {
            if (bottomToTop)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!occupied[columnIndex, y])
                    {
                        continue;
                    }

                    if (targetColors[columnIndex, y] != PigColor.None)
                    {
                        exposedTargets[columnIndex, y] = true;
                    }

                    break;
                }

                return;
            }

            for (int y = height - 1; y >= 0; y--)
            {
                if (!occupied[columnIndex, y])
                {
                    continue;
                }

                if (targetColors[columnIndex, y] != PigColor.None)
                {
                    exposedTargets[columnIndex, y] = true;
                }

                break;
            }
        }

        private static Dictionary<PigColor, int> CollectAndRemoveExposedTargets(
            bool[,] occupied,
            PigColor[,] targetColors,
            bool[,] exposedTargets,
            int width,
            int height)
        {
            var counts = new Dictionary<PigColor, int>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!exposedTargets[x, y])
                    {
                        continue;
                    }

                    var color = targetColors[x, y];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    if (!counts.TryGetValue(color, out var currentCount))
                    {
                        counts[color] = 1;
                    }
                    else
                    {
                        counts[color] = currentCount + 1;
                    }

                    targetColors[x, y] = PigColor.None;
                    occupied[x, y] = false;
                }
            }

            return counts;
        }

        private static bool HasRemainingTargets(PigColor[,] targetColors)
        {
            var width = targetColors.GetLength(0);
            var height = targetColors.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (targetColors[x, y] != PigColor.None)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static LevelGuaranteedCompletionValidationResult Success(string message)
        {
            return new LevelGuaranteedCompletionValidationResult(true, message, MessageType.Info);
        }

        private static LevelGuaranteedCompletionValidationResult Warning(string message)
        {
            return new LevelGuaranteedCompletionValidationResult(false, message, MessageType.Warning);
        }

        private static LevelGuaranteedCompletionValidationResult Error(string message)
        {
            return new LevelGuaranteedCompletionValidationResult(false, message, MessageType.Error);
        }
    }
}
#endif
