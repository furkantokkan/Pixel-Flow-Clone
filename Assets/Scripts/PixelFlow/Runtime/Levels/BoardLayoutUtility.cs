using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using PixelFlow.Runtime.LevelEditing;
using UnityEngine;

namespace PixelFlow.Runtime.Levels
{
    internal static class BoardLayoutUtility
    {
        private const float MinimumCellSpacing = 0.01f;
        private const float MinimumBoardPadding = 0.15f;
        private const float RelativeBoardPadding = 0.08f;
        private const float MinimumBoardFill = 0.4f;
        private const float SmartFitShrinkStartOccupancy = 0.78f;

        internal readonly struct Layout
        {
            public Layout(float cellSpacing, float sourceCenterX, float sourceCenterY, float rootScale)
            {
                CellSpacing = cellSpacing;
                SourceCenterX = sourceCenterX;
                SourceCenterY = sourceCenterY;
                RootScale = rootScale;
            }

            public float CellSpacing { get; }
            public float SourceCenterX { get; }
            public float SourceCenterY { get; }
            public float RootScale { get; }

            public float GetLocalX(float gridX)
            {
                return (gridX - SourceCenterX) * CellSpacing;
            }

            public float GetLocalZFromBottom(float gridY)
            {
                return (gridY - SourceCenterY) * CellSpacing;
            }
        }

        public static Layout ResolvePlacedObjectLayout(
            EnvironmentContext environment,
            Transform referenceSpace,
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database,
            Vector2Int fallbackGridSize,
            float baseCellSpacing,
            bool preserveFullGridBounds = false,
            float boardFill = 1f)
        {
            ResolvePlacedObjectBounds(
                placedObjects,
                database,
                fallbackGridSize,
                preserveFullGridBounds,
                out var minX,
                out var maxX,
                out var minY,
                out var maxY);

            ResolvePlacedObjectBounds(
                placedObjects,
                database,
                fallbackGridSize,
                preserveFullGridBounds: false,
                out var occupiedMinX,
                out var occupiedMaxX,
                out var occupiedMinY,
                out var occupiedMaxY);

            return ResolveLayout(
                environment,
                referenceSpace,
                minX,
                maxX,
                minY,
                maxY,
                occupiedMinX,
                occupiedMaxX,
                occupiedMinY,
                occupiedMaxY,
                baseCellSpacing,
                boardFill);
        }

        private static Layout ResolveLayout(
            EnvironmentContext environment,
            Transform referenceSpace,
            int minX,
            int maxX,
            int minY,
            int maxY,
            int occupiedMinX,
            int occupiedMaxX,
            int occupiedMinY,
            int occupiedMaxY,
            float baseCellSpacing,
            float boardFill)
        {
            var widthCells = Mathf.Max(1, maxX - minX + 1);
            var heightCells = Mathf.Max(1, maxY - minY + 1);
            var resolvedCellSpacing = Mathf.Max(MinimumCellSpacing, baseCellSpacing);
            var resolvedRootScale = 1f;

            if (TryResolvePlayableHalfExtents(environment, referenceSpace, out var playableHalfExtents))
            {
                var requiredWidth = Mathf.Max(MinimumCellSpacing, widthCells * resolvedCellSpacing);
                var requiredHeight = Mathf.Max(MinimumCellSpacing, heightCells * resolvedCellSpacing);
                var scaleX = (playableHalfExtents.x * 2f) / requiredWidth;
                var scaleY = (playableHalfExtents.y * 2f) / requiredHeight;
                resolvedRootScale = Mathf.Max(0.01f, Mathf.Min(1f, Mathf.Min(scaleX, scaleY)));
            }

            var occupiedWidthCells = Mathf.Max(1, occupiedMaxX - occupiedMinX + 1);
            var occupiedHeightCells = Mathf.Max(1, occupiedMaxY - occupiedMinY + 1);
            resolvedRootScale *= ResolveSmartFitScale(
                widthCells,
                heightCells,
                occupiedWidthCells,
                occupiedHeightCells,
                boardFill);

            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;
            return new Layout(resolvedCellSpacing, centerX, centerY, resolvedRootScale);
        }

        private static float ResolveSmartFitScale(
            int layoutWidthCells,
            int layoutHeightCells,
            int occupiedWidthCells,
            int occupiedHeightCells,
            float boardFill)
        {
            var clampedBoardFill = Mathf.Clamp(boardFill, MinimumBoardFill, 1f);
            if (clampedBoardFill >= 0.999f)
            {
                return 1f;
            }

            if (layoutWidthCells <= 0 || layoutHeightCells <= 0)
            {
                return 1f;
            }

            var occupancyX = Mathf.Clamp01(occupiedWidthCells / (float)layoutWidthCells);
            var occupancyY = Mathf.Clamp01(occupiedHeightCells / (float)layoutHeightCells);
            var dominantOccupancy = Mathf.Max(occupancyX, occupancyY);
            var shrinkT = Mathf.InverseLerp(SmartFitShrinkStartOccupancy, 1f, dominantOccupancy);
            shrinkT = Mathf.SmoothStep(0f, 1f, shrinkT);
            return Mathf.Lerp(1f, clampedBoardFill, shrinkT);
        }

        private static void ResolvePlacedObjectBounds(
            IReadOnlyList<PlacedObjectData> placedObjects,
            LevelDatabase database,
            Vector2Int fallbackGridSize,
            bool preserveFullGridBounds,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY)
        {
            if (preserveFullGridBounds)
            {
                minX = 0;
                minY = 0;
                maxX = Mathf.Max(0, fallbackGridSize.x - 1);
                maxY = Mathf.Max(0, fallbackGridSize.y - 1);
                return;
            }

            var hasBounds = false;
            minX = int.MaxValue;
            minY = int.MaxValue;
            maxX = int.MinValue;
            maxY = int.MinValue;

            if (placedObjects != null)
            {
                for (int i = 0; i < placedObjects.Count; i++)
                {
                    var placedObject = placedObjects[i];
                    var definition = database != null ? database.FindPlaceable(placedObject) : null;
                    var size = definition != null ? definition.GridSize : Vector2Int.one;

                    minX = Mathf.Min(minX, placedObject.Origin.x);
                    minY = Mathf.Min(minY, placedObject.Origin.y);
                    maxX = Mathf.Max(maxX, placedObject.Origin.x + Mathf.Max(1, size.x) - 1);
                    maxY = Mathf.Max(maxY, placedObject.Origin.y + Mathf.Max(1, size.y) - 1);
                    hasBounds = true;
                }
            }

            if (hasBounds)
            {
                return;
            }

            minX = 0;
            minY = 0;
            maxX = Mathf.Max(0, fallbackGridSize.x - 1);
            maxY = Mathf.Max(0, fallbackGridSize.y - 1);
        }

        private static bool TryResolvePlayableHalfExtents(
            EnvironmentContext environment,
            Transform referenceSpace,
            out Vector2 playableHalfExtents)
        {
            playableHalfExtents = Vector2.zero;
            if (environment == null || referenceSpace == null)
            {
                return false;
            }

            if (!TryResolveBoardVisualBounds(environment, referenceSpace, out var boardBounds))
            {
                return false;
            }

            var halfWidth = Mathf.Min(Mathf.Abs(boardBounds.min.x), Mathf.Abs(boardBounds.max.x));
            var halfDepth = Mathf.Min(Mathf.Abs(boardBounds.min.z), Mathf.Abs(boardBounds.max.z));
            if (halfWidth <= 0f || halfDepth <= 0f)
            {
                return false;
            }

            var padding = Mathf.Max(MinimumBoardPadding, Mathf.Min(halfWidth, halfDepth) * RelativeBoardPadding);
            playableHalfExtents = new Vector2(
                Mathf.Max(0.5f, halfWidth - padding),
                Mathf.Max(0.5f, halfDepth - padding));
            return true;
        }

        private static bool TryResolveBoardVisualBounds(
            EnvironmentContext environment,
            Transform referenceSpace,
            out Bounds localBounds)
        {
            localBounds = default;
            var visualRoot = ResolveBoardVisualRoot(environment);
            if (visualRoot == null)
            {
                return false;
            }

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            var initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var rendererTransform = renderer.transform;
                if (environment.BlockContainer != null && rendererTransform.IsChildOf(environment.BlockContainer))
                {
                    continue;
                }

                if (environment.HoldingContainer != null && rendererTransform.IsChildOf(environment.HoldingContainer))
                {
                    continue;
                }

                if (environment.DeckContainer != null && rendererTransform.IsChildOf(environment.DeckContainer))
                {
                    continue;
                }

                if (string.Equals(renderer.gameObject.name, "Ground", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EncapsulateWorldBounds(renderer.bounds, referenceSpace, ref localBounds, ref initialized);
            }

            return initialized;
        }

        private static Transform ResolveBoardVisualRoot(EnvironmentContext environment)
        {
            if (environment == null)
            {
                return null;
            }

            var environmentRoot = environment.transform;
            var directMatch = FindNamedRendererRoot(environmentRoot, "ConveyorBelt");
            if (directMatch != null)
            {
                return directMatch;
            }

            directMatch = FindNamedRendererRoot(environmentRoot, "Board");
            if (directMatch != null)
            {
                return directMatch;
            }

            directMatch = FindNamedRendererRoot(environmentRoot, "Frame");
            if (directMatch != null)
            {
                return directMatch;
            }

            return environmentRoot;
        }

        private static Transform FindNamedRendererRoot(Transform environmentRoot, string token)
        {
            if (environmentRoot == null || string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            for (int i = 0; i < environmentRoot.childCount; i++)
            {
                var child = environmentRoot.GetChild(i);
                if (child.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (child.GetComponentInChildren<Renderer>(true) != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static void EncapsulateWorldBounds(
            Bounds worldBounds,
            Transform referenceSpace,
            ref Bounds localBounds,
            ref bool initialized)
        {
            var min = worldBounds.min;
            var max = worldBounds.max;
            var corners = new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                var localPoint = referenceSpace.InverseTransformPoint(corners[i]);
                if (!initialized)
                {
                    localBounds = new Bounds(localPoint, Vector3.zero);
                    initialized = true;
                    continue;
                }

                localBounds.Encapsulate(localPoint);
            }
        }
    }
}
