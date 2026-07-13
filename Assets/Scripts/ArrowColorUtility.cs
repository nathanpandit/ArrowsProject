using System.Collections.Generic;
using UnityEngine;

public static class ArrowColorUtility
{
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    public static Dictionary<int, Color> AssignDistinctAdjacentColors(LevelData levelData, IList<Color> basePalette, Color fallbackColor)
    {
        Dictionary<int, HashSet<int>> adjacencyByArrowId = BuildArrowAdjacency(levelData, out List<int> arrowIds);
        List<Color> palette = BuildPalette(basePalette, fallbackColor, Mathf.Max(arrowIds.Count, 1));
        Dictionary<int, Color> colorByArrowId = new Dictionary<int, Color>();

        arrowIds.Sort((first, second) =>
        {
            int degreeCompare = adjacencyByArrowId[second].Count.CompareTo(adjacencyByArrowId[first].Count);
            return degreeCompare != 0 ? degreeCompare : first.CompareTo(second);
        });

        for (int i = 0; i < arrowIds.Count; i++)
        {
            int arrowId = arrowIds[i];
            Color selectedColor = palette[0];
            bool foundColor = false;

            for (int colorIndex = 0; colorIndex < palette.Count; colorIndex++)
            {
                Color candidateColor = palette[colorIndex];
                if (ConflictsWithColoredNeighbor(arrowId, candidateColor, adjacencyByArrowId, colorByArrowId))
                {
                    continue;
                }

                selectedColor = candidateColor;
                foundColor = true;
                break;
            }

            if (!foundColor)
            {
                selectedColor = GenerateDistinctColor(palette.Count);
                palette.Add(selectedColor);
            }

            colorByArrowId[arrowId] = selectedColor;
        }

        return colorByArrowId;
    }

    private static Dictionary<int, HashSet<int>> BuildArrowAdjacency(LevelData levelData, out List<int> arrowIds)
    {
        Dictionary<int, HashSet<int>> adjacencyByArrowId = new Dictionary<int, HashSet<int>>();
        Dictionary<Vector2Int, int> arrowIdByCell = new Dictionary<Vector2Int, int>();
        arrowIds = new List<int>();

        if (levelData?.arrows == null)
        {
            return adjacencyByArrowId;
        }

        for (int arrowIndex = 0; arrowIndex < levelData.arrows.Count; arrowIndex++)
        {
            ArrowData arrow = levelData.arrows[arrowIndex];
            if (arrow == null || arrow.arrowId < 0 || arrow.occupiedCells == null)
            {
                continue;
            }

            if (!adjacencyByArrowId.ContainsKey(arrow.arrowId))
            {
                adjacencyByArrowId.Add(arrow.arrowId, new HashSet<int>());
                arrowIds.Add(arrow.arrowId);
            }

            for (int cellIndex = 0; cellIndex < arrow.occupiedCells.Count; cellIndex++)
            {
                GridPositionData cell = arrow.occupiedCells[cellIndex];
                if (cell == null)
                {
                    continue;
                }

                arrowIdByCell[new Vector2Int(cell.x, cell.y)] = arrow.arrowId;
            }
        }

        foreach (KeyValuePair<Vector2Int, int> entry in arrowIdByCell)
        {
            for (int directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
            {
                Vector2Int neighborPosition = entry.Key + CardinalDirections[directionIndex];
                if (!arrowIdByCell.TryGetValue(neighborPosition, out int neighborArrowId) || neighborArrowId == entry.Value)
                {
                    continue;
                }

                adjacencyByArrowId[entry.Value].Add(neighborArrowId);
                adjacencyByArrowId[neighborArrowId].Add(entry.Value);
            }
        }

        return adjacencyByArrowId;
    }

    private static List<Color> BuildPalette(IList<Color> basePalette, Color fallbackColor, int requiredColorCount)
    {
        List<Color> colors = new List<Color>();
        if (basePalette != null)
        {
            for (int i = 0; i < basePalette.Count; i++)
            {
                AddColorIfUnique(colors, basePalette[i]);
            }
        }

        if (colors.Count == 0)
        {
            AddColorIfUnique(colors, fallbackColor);
        }

        int generatedIndex = 0;
        while (colors.Count < requiredColorCount)
        {
            Color generatedColor = GenerateDistinctColor(generatedIndex);
            if (AddColorIfUnique(colors, generatedColor))
            {
                generatedIndex++;
            }
            else
            {
                generatedIndex++;
            }
        }

        return colors;
    }

    private static bool AddColorIfUnique(List<Color> colors, Color color)
    {
        color.a = 1f;
        for (int i = 0; i < colors.Count; i++)
        {
            if (ColorsApproximatelyEqual(colors[i], color))
            {
                return false;
            }
        }

        colors.Add(color);
        return true;
    }

    private static bool ConflictsWithColoredNeighbor(
        int arrowId,
        Color candidateColor,
        Dictionary<int, HashSet<int>> adjacencyByArrowId,
        Dictionary<int, Color> colorByArrowId)
    {
        if (!adjacencyByArrowId.TryGetValue(arrowId, out HashSet<int> neighborIds))
        {
            return false;
        }

        foreach (int neighborId in neighborIds)
        {
            if (colorByArrowId.TryGetValue(neighborId, out Color neighborColor) &&
                ColorsApproximatelyEqual(neighborColor, candidateColor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColorsApproximatelyEqual(Color first, Color second)
    {
        const float tolerance = 0.001f;
        return Mathf.Abs(first.r - second.r) < tolerance &&
               Mathf.Abs(first.g - second.g) < tolerance &&
               Mathf.Abs(first.b - second.b) < tolerance;
    }

    private static Color GenerateDistinctColor(int index)
    {
        float hue = Mathf.Repeat(index * 0.61803398875f, 1f);
        Color color = Color.HSVToRGB(hue, 0.68f, 0.95f);
        color.a = 1f;
        return color;
    }
}
