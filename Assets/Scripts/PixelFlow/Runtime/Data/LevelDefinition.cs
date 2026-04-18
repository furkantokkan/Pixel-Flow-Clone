using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelFlow.Runtime.Data
{
    [CreateAssetMenu(fileName = "LevelDefinition", menuName = "Pixel Flow/Level Definition")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField] private Vector2Int gridSize = new(12, 12);
        [SerializeField] private PixelFlowTheme theme;
        [SerializeField] private Texture2D sourceImage;
        [SerializeField] private PixelFlowBlockData blockData;
        [SerializeField] private ImageImportSettings importSettings = new();
        [SerializeField] private List<PixelCellData> pixelCells = new();
        [SerializeField] private List<WaitingSlotData> waitingSlots = new();
        [SerializeField] private List<PigQueueEntry> pigQueue = new();
        [SerializeField] private PigQueueGenerationSettings pigQueueGenerationSettings = new();
        [SerializeField] private ConveyorBakeData conveyorBakeData = new();

        public Vector2Int GridSize => gridSize;
        public PixelFlowTheme Theme => theme;
        public Texture2D SourceImage => sourceImage;
        public PixelFlowBlockData BlockData => blockData;
        public ImageImportSettings ImportSettings => importSettings;
        public IReadOnlyList<PixelCellData> PixelCells => pixelCells;
        public IReadOnlyList<WaitingSlotData> WaitingSlots => waitingSlots;
        public IReadOnlyList<PigQueueEntry> PigQueue => pigQueue;
        public PigQueueGenerationSettings PigQueueGenerationSettings => pigQueueGenerationSettings;
        public ConveyorBakeData ConveyorBakeData => conveyorBakeData;

#if UNITY_EDITOR
        public PigColor[,] EditorCreateDisplayGrid()
        {
            var width = Mathf.Max(1, gridSize.x);
            var height = Mathf.Max(1, gridSize.y);
            var grid = new PigColor[width, height];

            for (int i = 0; i < pixelCells.Count; i++)
            {
                var cell = pixelCells[i];
                var x = cell.Position.x;
                var yView = (height - 1) - cell.Position.y;

                if (x < 0 || x >= width || yView < 0 || yView >= height)
                {
                    continue;
                }

                grid[x, yView] = cell.Color;
            }

            return grid;
        }

        public void EditorApply(
            PigColor[,] displayGrid,
            PixelFlowTheme selectedTheme,
            Texture2D newSourceImage,
            PixelFlowBlockData selectedBlockData,
            ImageImportSettings imageSettings,
            IReadOnlyList<PigQueueEntry> queueEntries,
            PigQueueGenerationSettings queueSettings)
        {
            if (displayGrid == null)
            {
                throw new ArgumentNullException(nameof(displayGrid));
            }

            gridSize = new Vector2Int(displayGrid.GetLength(0), displayGrid.GetLength(1));
            theme = selectedTheme;
            sourceImage = newSourceImage;
            blockData = selectedBlockData;
            importSettings = imageSettings?.Clone() ?? new ImageImportSettings();
            pigQueueGenerationSettings = queueSettings?.Clone() ?? new PigQueueGenerationSettings();
            pixelCells.Clear();
            pigQueue.Clear();

            for (int x = 0; x < gridSize.x; x++)
            {
                for (int yView = 0; yView < gridSize.y; yView++)
                {
                    var color = displayGrid[x, yView];
                    if (color == PigColor.None)
                    {
                        continue;
                    }

                    var yData = (gridSize.y - 1) - yView;
                    pixelCells.Add(new PixelCellData(new Vector2Int(x, yData), color));
                }
            }

            if (queueEntries != null)
            {
                for (int i = 0; i < queueEntries.Count; i++)
                {
                    pigQueue.Add(queueEntries[i]);
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }
}
