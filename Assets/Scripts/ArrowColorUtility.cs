using System.Collections.Generic;
using UnityEngine;

public static class ArrowColorUtility
{
    public const int DefaultColorIndex = 0;

    private static readonly Color[] DefaultPalette =
    {
        new Color(0.93f, 0.18f, 0.36f, 1f), // 1 red
        new Color(0.13f, 0.62f, 0.95f, 1f), // 2 blue
        new Color(0.49f, 0.80f, 0.32f, 1f), // 3 green
        new Color(1.00f, 0.74f, 0.20f, 1f), // 4 yellow
        new Color(0.93f, 0.32f, 0.62f, 1f), // 5 pink
        new Color(0.72f, 0.72f, 0.72f, 1f), // 6 gray
        new Color(0.58f, 0.38f, 0.90f, 1f), // 7 purple
        new Color(0.16f, 0.78f, 0.76f, 1f), // 8 cyan
        new Color(0.95f, 0.48f, 0.18f, 1f)  // 9 orange
    };

    private static readonly string[] DefaultColorNames =
    {
        "red",
        "blue",
        "green",
        "yellow",
        "pink",
        "gray",
        "purple",
        "cyan",
        "orange"
    };

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    public static int ManualColorCount => DefaultPalette.Length;

    public static bool TryGetManualColorIndex(int numberKey, out int colorIndex)
    {
        colorIndex = numberKey - 1;
        return numberKey >= 1 && numberKey <= ManualColorCount;
    }

    public static Color GetColor(int colorIndex, Color fallbackColor)
    {
        if (colorIndex < 0)
        {
            fallbackColor.a = 1f;
            return fallbackColor;
        }

        if (colorIndex < DefaultPalette.Length)
        {
            return DefaultPalette[colorIndex];
        }

        return GenerateDistinctColor(colorIndex);
    }

    public static string GetColorName(int colorIndex)
    {
        if (colorIndex >= 0 && colorIndex < DefaultColorNames.Length)
        {
            return DefaultColorNames[colorIndex];
        }

        return colorIndex >= 0 ? $"color {colorIndex + 1}" : "uncolored";
    }

    public static Dictionary<int, Color> BuildArrowColorLookup(LevelData levelData, Color fallbackColor)
    {
        Dictionary<int, Color> colorByArrowId = new Dictionary<int, Color>();
        if (levelData?.arrows == null)
        {
            return colorByArrowId;
        }

        for (int i = 0; i < levelData.arrows.Count; i++)
        {
            ArrowData arrow = levelData.arrows[i];
            if (arrow == null)
            {
                continue;
            }

            colorByArrowId[arrow.arrowId] = GetColor(arrow.colorIndex, fallbackColor);
        }

        return colorByArrowId;
    }

    public static void AssignMissingDistinctAdjacentColorIndices(LevelData levelData)
    {
        AssignDistinctAdjacentColorIndices(levelData, false);
    }

    public static void ReassignDistinctAdjacentColorIndices(LevelData levelData)
    {
        AssignDistinctAdjacentColorIndices(levelData, true);
    }

    private static void AssignDistinctAdjacentColorIndices(LevelData levelData, bool overwriteExistingColors)
    {
        Dictionary<int, HashSet<int>> adjacencyByArrowId = BuildArrowAdjacency(levelData, out List<int> arrowIds);
        if (levelData?.arrows == null || arrowIds.Count == 0)
        {
            return;
        }

        Dictionary<int, ArrowData> arrowById = new Dictionary<int, ArrowData>();
        Dictionary<int, int> colorIndexByArrowId = new Dictionary<int, int>();
        for (int i = 0; i < levelData.arrows.Count; i++)
        {
            ArrowData arrow = levelData.arrows[i];
            if (arrow == null || arrow.arrowId < 0)
            {
                continue;
            }

            arrowById[arrow.arrowId] = arrow;
            if (!overwriteExistingColors && arrow.colorIndex >= 0)
            {
                colorIndexByArrowId[arrow.arrowId] = arrow.colorIndex;
            }
        }

        arrowIds.Sort((first, second) =>
        {
            int firstDegree = adjacencyByArrowId.TryGetValue(first, out HashSet<int> firstNeighbors) ? firstNeighbors.Count : 0;
            int secondDegree = adjacencyByArrowId.TryGetValue(second, out HashSet<int> secondNeighbors) ? secondNeighbors.Count : 0;
            int degreeCompare = secondDegree.CompareTo(firstDegree);
            return degreeCompare != 0 ? degreeCompare : first.CompareTo(second);
        });

        for (int i = 0; i < arrowIds.Count; i++)
        {
            int arrowId = arrowIds[i];
            if (!arrowById.TryGetValue(arrowId, out ArrowData arrow))
            {
                continue;
            }

            if (!overwriteExistingColors && arrow.colorIndex >= 0)
            {
                continue;
            }

            int selectedColorIndex = FindFirstNonConflictingColorIndex(arrowId, adjacencyByArrowId, colorIndexByArrowId, arrowIds.Count);
            arrow.colorIndex = selectedColorIndex;
            colorIndexByArrowId[arrowId] = selectedColorIndex;
        }
    }

    private static int FindFirstNonConflictingColorIndex(
        int arrowId,
        Dictionary<int, HashSet<int>> adjacencyByArrowId,
        Dictionary<int, int> colorIndexByArrowId,
        int arrowCount)
    {
        int candidateLimit = Mathf.Max(DefaultPalette.Length, arrowCount);
        for (int colorIndex = 0; colorIndex < candidateLimit; colorIndex++)
        {
            if (!ConflictsWithColoredNeighbor(arrowId, colorIndex, adjacencyByArrowId, colorIndexByArrowId))
            {
                return colorIndex;
            }
        }

        int generatedColorIndex = candidateLimit;
        while (ConflictsWithColoredNeighbor(arrowId, generatedColorIndex, adjacencyByArrowId, colorIndexByArrowId))
        {
            generatedColorIndex++;
        }

        return generatedColorIndex;
    }

    private static bool ConflictsWithColoredNeighbor(
        int arrowId,
        int candidateColorIndex,
        Dictionary<int, HashSet<int>> adjacencyByArrowId,
        Dictionary<int, int> colorIndexByArrowId)
    {
        if (!adjacencyByArrowId.TryGetValue(arrowId, out HashSet<int> neighborIds))
        {
            return false;
        }

        foreach (int neighborId in neighborIds)
        {
            if (colorIndexByArrowId.TryGetValue(neighborId, out int neighborColorIndex) &&
                neighborColorIndex == candidateColorIndex)
            {
                return true;
            }
        }

        return false;
    }

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
