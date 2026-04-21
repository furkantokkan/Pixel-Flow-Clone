#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using PixelFlow.Runtime.Data;
using UnityEngine;

namespace PixelFlow.Editor.LevelEditing
{
    [Serializable]
    internal sealed class PigQueueEditorState
    {
        [SerializeField] private int primaryIndex = -1;
        [SerializeField] private int secondaryIndex = -1;
        [SerializeField] private int swapAmount = 1;
        [SerializeField] private int dragSourceIndex = -1;
        [SerializeField] private int dragHoverLinearIndex = -1;
        [SerializeField] private Vector2 dragStartMousePosition;
        [SerializeField] private bool isDragging;

        internal int PrimaryIndex => primaryIndex;
        internal int SecondaryIndex => secondaryIndex;
        internal int SwapAmount
        {
            get => swapAmount;
            set => swapAmount = value;
        }

        internal int DragSourceIndex => dragSourceIndex;
        internal int DragHoverLinearIndex => dragHoverLinearIndex;
        internal bool IsDragging => isDragging;

        internal void ClearSelection(int ammoStep)
        {
            primaryIndex = -1;
            secondaryIndex = -1;
            swapAmount = Mathf.Max(1, ammoStep);
        }

        internal void ResetDragState()
        {
            dragSourceIndex = -1;
            dragHoverLinearIndex = -1;
            dragStartMousePosition = Vector2.zero;
            isDragging = false;
        }

        internal void EnsureSelectionValid(IReadOnlyList<PigQueueEntry> queue, int ammoStep)
        {
            if (queue == null || queue.Count == 0)
            {
                ClearSelection(ammoStep);
                ResetDragState();
                return;
            }

            if (!IsValidIndex(queue, primaryIndex))
            {
                primaryIndex = -1;
            }

            if (!IsValidIndex(queue, secondaryIndex) || secondaryIndex == primaryIndex)
            {
                secondaryIndex = -1;
            }

            if (primaryIndex >= 0
                && secondaryIndex >= 0
                && queue[primaryIndex].Color != queue[secondaryIndex].Color)
            {
                secondaryIndex = -1;
            }

            swapAmount = Mathf.Max(ammoStep, swapAmount);
        }

        internal bool IsPreviewSelected(int queueIndex)
        {
            return queueIndex == primaryIndex || queueIndex == secondaryIndex;
        }

        internal string GetSelectionLabel(int queueIndex)
        {
            if (queueIndex == primaryIndex)
            {
                return "A";
            }

            if (queueIndex == secondaryIndex)
            {
                return "B";
            }

            return null;
        }

        internal bool SwapSelectedPair(IReadOnlyList<PigQueueEntry> queue)
        {
            if (!IsValidIndex(queue, primaryIndex) || !IsValidIndex(queue, secondaryIndex))
            {
                return false;
            }

            (primaryIndex, secondaryIndex) = (secondaryIndex, primaryIndex);
            return true;
        }

        internal void BeginDrag(int queueIndex, int linearIndex, Vector2 mousePosition)
        {
            dragSourceIndex = queueIndex;
            dragHoverLinearIndex = linearIndex;
            dragStartMousePosition = mousePosition;
            isDragging = false;
        }

        internal bool UpdateDragging(Vector2 mousePosition, float dragStartDistance)
        {
            if (isDragging)
            {
                return false;
            }

            isDragging = (mousePosition - dragStartMousePosition).sqrMagnitude
                >= dragStartDistance * dragStartDistance;
            return isDragging;
        }

        internal void SetDragHoverLinearIndex(int linearIndex)
        {
            dragHoverLinearIndex = linearIndex;
        }

        internal bool TrySelectPreviewEntry(
            IReadOnlyList<PigQueueEntry> queue,
            int queueIndex,
            int ammoStep,
            out string message)
        {
            message = null;
            EnsureSelectionValid(queue, ammoStep);
            if (!IsValidIndex(queue, queueIndex))
            {
                return false;
            }

            if (primaryIndex == queueIndex)
            {
                primaryIndex = secondaryIndex;
                secondaryIndex = -1;
                return true;
            }

            if (secondaryIndex == queueIndex)
            {
                secondaryIndex = -1;
                return true;
            }

            if (primaryIndex < 0)
            {
                primaryIndex = queueIndex;
                secondaryIndex = -1;
                return true;
            }

            if (secondaryIndex < 0)
            {
                if (queue[primaryIndex].Color == queue[queueIndex].Color)
                {
                    secondaryIndex = queueIndex;
                    return true;
                }

                primaryIndex = queueIndex;
                secondaryIndex = -1;
                message = "Select a second pig with the same color to transfer ammo.";
                return true;
            }

            primaryIndex = queueIndex;
            secondaryIndex = -1;
            return true;
        }

        internal bool TryGetSelectedPair(
            IReadOnlyList<PigQueueEntry> queue,
            int ammoStep,
            out int resolvedPrimaryIndex,
            out int resolvedSecondaryIndex)
        {
            EnsureSelectionValid(queue, ammoStep);
            resolvedPrimaryIndex = primaryIndex;
            resolvedSecondaryIndex = secondaryIndex;
            return IsValidIndex(queue, resolvedPrimaryIndex)
                && IsValidIndex(queue, resolvedSecondaryIndex)
                && queue[resolvedPrimaryIndex].Color == queue[resolvedSecondaryIndex].Color;
        }

        private static bool IsValidIndex(IReadOnlyList<PigQueueEntry> queue, int queueIndex)
        {
            return queue != null
                && queueIndex >= 0
                && queueIndex < queue.Count;
        }
    }
}
#endif
