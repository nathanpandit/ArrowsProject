using System;
using System.Collections.Generic;
using UnityEngine;

public enum CellContentType
{
    Empty,
    ArrowBody,
    ArrowTip
}

public enum ArrowDirection
{
    None,
    Up,
    Down,
    Left,
    Right
}

[Serializable]
public class LevelData
{
    public int levelIndex;
    public int gridSize;
    public int width;
    public int height;
    public int targetArrowCount;
    public int minArrowLength;
    public int maxArrowLength;
    public int lives;
    public List<VertexData> vertices = new List<VertexData>();
    public List<EdgeData> edges = new List<EdgeData>();
    public List<ArrowData> arrows = new List<ArrowData>();
    public List<GridPositionData> shapeCells = new List<GridPositionData>();
    public List<DependencyData> dependencies = new List<DependencyData>();
    public List<int> solutionOrder = new List<int>();
    public string createdAt;
    public string notes;

    public static int GetVertexId(int x, int y, int width)
    {
        return y * width + x;
    }

    public bool IsInsideGrid(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    public VertexData GetVertexAt(int x, int y)
    {
        if (vertices == null)
        {
            return null;
        }

        int vertexId = GetVertexId(x, y, width);
        for (int i = 0; i < vertices.Count; i++)
        {
            if (vertices[i].vertexId == vertexId)
            {
                return vertices[i];
            }
        }

        return null;
    }

    public bool Validate()
    {
        bool isValid = true;

        if (width <= 0 || height <= 0 || gridSize <= 0)
        {
            Debug.LogError("LevelData validation failed: grid dimensions must be positive.");
            isValid = false;
        }

        if (minArrowLength > maxArrowLength)
        {
            Debug.LogError("LevelData validation failed: minArrowLength cannot be greater than maxArrowLength.");
            isValid = false;
        }

        if (targetArrowCount < 0)
        {
            Debug.LogError("LevelData validation failed: targetArrowCount cannot be negative.");
            isValid = false;
        }

        if (vertices == null)
        {
            Debug.LogError("LevelData validation failed: vertices list is null.");
            return false;
        }

        if (vertices.Count != width * height)
        {
            Debug.LogError($"LevelData validation failed: expected {width * height} vertices, found {vertices.Count}.");
            isValid = false;
        }

        HashSet<int> vertexIds = new HashSet<int>();
        Dictionary<int, int> tipCountByArrowId = new Dictionary<int, int>();
        HashSet<int> shapeVertexIds = new HashSet<int>();

        if (shapeCells != null)
        {
            for (int i = 0; i < shapeCells.Count; i++)
            {
                GridPositionData shapeCell = shapeCells[i];
                if (shapeCell == null || !IsInsideGrid(shapeCell.x, shapeCell.y))
                {
                    Debug.LogError("LevelData validation failed: shape cell is outside the grid.");
                    isValid = false;
                    continue;
                }

                int shapeVertexId = GetVertexId(shapeCell.x, shapeCell.y, width);
                if (!shapeVertexIds.Add(shapeVertexId))
                {
                    Debug.LogError($"LevelData validation failed: duplicate shape cell at vertex {shapeVertexId}.");
                    isValid = false;
                }
            }
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            VertexData vertex = vertices[i];
            if (vertex == null)
            {
                Debug.LogError("LevelData validation failed: a vertex entry is null.");
                isValid = false;
                continue;
            }

            if (!vertexIds.Add(vertex.vertexId))
            {
                Debug.LogError($"LevelData validation failed: duplicate vertex id {vertex.vertexId}.");
                isValid = false;
            }

            if (!IsInsideGrid(vertex.x, vertex.y))
            {
                Debug.LogError($"LevelData validation failed: vertex {vertex.vertexId} is outside the grid.");
                isValid = false;
            }

            if (vertex.neighborVertexIds == null)
            {
                Debug.LogWarning($"LevelData validation warning: vertex {vertex.vertexId} has no neighbor list.");
            }

            if (vertex.contentType == CellContentType.Empty)
            {
                if (vertex.arrowId != -1)
                {
                    Debug.LogError($"LevelData validation failed: empty vertex {vertex.vertexId} has arrowId {vertex.arrowId}.");
                    isValid = false;
                }

                if (vertex.tipDirection != ArrowDirection.None)
                {
                    Debug.LogError($"LevelData validation failed: empty vertex {vertex.vertexId} has a tip direction.");
                    isValid = false;
                }
            }
            else if (vertex.contentType == CellContentType.ArrowBody)
            {
                if (vertex.arrowId < 0)
                {
                    Debug.LogError($"LevelData validation failed: arrow body vertex {vertex.vertexId} has no arrow id.");
                    isValid = false;
                }

                if (vertex.tipDirection != ArrowDirection.None)
                {
                    Debug.LogWarning($"LevelData validation warning: arrow body vertex {vertex.vertexId} has a tip direction set.");
                }
            }
            else if (vertex.contentType == CellContentType.ArrowTip)
            {
                if (vertex.arrowId < 0)
                {
                    Debug.LogError($"LevelData validation failed: arrow tip vertex {vertex.vertexId} has no arrow id.");
                    isValid = false;
                }

                if (vertex.tipDirection == ArrowDirection.None)
                {
                    Debug.LogError($"LevelData validation failed: arrow tip vertex {vertex.vertexId} has no valid direction.");
                    isValid = false;
                }

                if (!tipCountByArrowId.ContainsKey(vertex.arrowId))
                {
                    tipCountByArrowId.Add(vertex.arrowId, 0);
                }

                tipCountByArrowId[vertex.arrowId]++;
            }
        }

        if (arrows == null)
        {
            Debug.LogError("LevelData validation failed: arrows list is null.");
            return false;
        }

        HashSet<int> arrowIds = new HashSet<int>();
        Dictionary<int, int> occupiedVertexToArrow = new Dictionary<int, int>();

        for (int i = 0; i < arrows.Count; i++)
        {
            ArrowData arrow = arrows[i];
            if (arrow == null)
            {
                Debug.LogError("LevelData validation failed: an arrow entry is null.");
                isValid = false;
                continue;
            }

            if (!arrowIds.Add(arrow.arrowId))
            {
                Debug.LogError($"LevelData validation failed: duplicate arrow id {arrow.arrowId}.");
                isValid = false;
            }

            if (arrow.occupiedCells == null || arrow.occupiedCells.Count == 0)
            {
                Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} has no occupied cells.");
                isValid = false;
                continue;
            }

            if (arrow.length != arrow.occupiedCells.Count)
            {
                Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} length does not match occupied cell count.");
                isValid = false;
            }

            if (arrow.tipCell == null)
            {
                Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} has no tip cell.");
                isValid = false;
            }

            if (arrow.tipDirection == ArrowDirection.None)
            {
                Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} has no valid tip direction.");
                isValid = false;
            }

            bool tipIsInOccupiedCells = false;
            for (int cellIndex = 0; cellIndex < arrow.occupiedCells.Count; cellIndex++)
            {
                GridPositionData cell = arrow.occupiedCells[cellIndex];
                if (cell == null || !IsInsideGrid(cell.x, cell.y))
                {
                    Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} has an occupied cell outside the grid.");
                    isValid = false;
                    continue;
                }

                int vertexId = GetVertexId(cell.x, cell.y, width);
                if (shapeVertexIds.Count > 0 && !shapeVertexIds.Contains(vertexId))
                {
                    Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} occupies non-shape vertex {vertexId}.");
                    isValid = false;
                }

                if (occupiedVertexToArrow.TryGetValue(vertexId, out int existingArrowId))
                {
                    Debug.LogError($"LevelData validation failed: vertex {vertexId} is occupied by arrows {existingArrowId} and {arrow.arrowId}.");
                    isValid = false;
                }
                else
                {
                    occupiedVertexToArrow.Add(vertexId, arrow.arrowId);
                }

                if (arrow.tipCell != null && cell.x == arrow.tipCell.x && cell.y == arrow.tipCell.y)
                {
                    tipIsInOccupiedCells = true;
                }

                VertexData occupiedVertex = GetVertexAt(cell.x, cell.y);
                if (occupiedVertex == null)
                {
                    Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} references missing vertex {vertexId}.");
                    isValid = false;
                }
                else if (occupiedVertex.arrowId != arrow.arrowId || occupiedVertex.contentType == CellContentType.Empty)
                {
                    Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} occupied cell {vertexId} does not match vertex content.");
                    isValid = false;
                }
            }

            if (!tipIsInOccupiedCells)
            {
                Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} tip cell is not in occupiedCells.");
                isValid = false;
            }

            tipCountByArrowId.TryGetValue(arrow.arrowId, out int tipCount);
            if (tipCount != 1)
            {
                Debug.LogError($"LevelData validation failed: arrow {arrow.arrowId} must have exactly one tip vertex, found {tipCount}.");
                isValid = false;
            }
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            VertexData vertex = vertices[i];
            if (vertex != null && vertex.contentType != CellContentType.Empty && !arrowIds.Contains(vertex.arrowId))
            {
                Debug.LogError($"LevelData validation failed: occupied vertex {vertex.vertexId} references missing arrow {vertex.arrowId}.");
                isValid = false;
            }
        }

        if (shapeVertexIds.Count > 0)
        {
            foreach (int shapeVertexId in shapeVertexIds)
            {
                if (!occupiedVertexToArrow.ContainsKey(shapeVertexId))
                {
                    Debug.LogError($"LevelData validation failed: shape vertex {shapeVertexId} is not occupied by any arrow.");
                    isValid = false;
                }
            }
        }

        if (dependencies != null)
        {
            Dictionary<int, HashSet<int>> dependencyGraph = new Dictionary<int, HashSet<int>>();
            foreach (int arrowId in arrowIds)
            {
                dependencyGraph[arrowId] = new HashSet<int>();
            }

            for (int i = 0; i < dependencies.Count; i++)
            {
                DependencyData dependency = dependencies[i];
                if (dependency == null)
                {
                    Debug.LogError("LevelData validation failed: dependency entry is null.");
                    isValid = false;
                    continue;
                }

                if (dependency.blockerArrowId == dependency.blockedArrowId)
                {
                    Debug.LogError($"LevelData validation failed: arrow {dependency.blockerArrowId} depends on itself.");
                    isValid = false;
                }

                if (!arrowIds.Contains(dependency.blockerArrowId) || !arrowIds.Contains(dependency.blockedArrowId))
                {
                    Debug.LogError("LevelData validation failed: dependency references a missing arrow id.");
                    isValid = false;
                    continue;
                }

                dependencyGraph[dependency.blockerArrowId].Add(dependency.blockedArrowId);
            }

            if (!IsDependencyGraphAcyclic(arrowIds, dependencyGraph))
            {
                Debug.LogError("LevelData validation failed: dependency graph contains a cycle.");
                isValid = false;
            }
        }

        if (solutionOrder != null && solutionOrder.Count > 0)
        {
            if (solutionOrder.Count != arrowIds.Count)
            {
                Debug.LogError("LevelData validation failed: solutionOrder count does not match arrow count.");
                isValid = false;
            }

            HashSet<int> solutionArrowIds = new HashSet<int>();
            for (int i = 0; i < solutionOrder.Count; i++)
            {
                int arrowId = solutionOrder[i];
                if (!arrowIds.Contains(arrowId) || !solutionArrowIds.Add(arrowId))
                {
                    Debug.LogError($"LevelData validation failed: solutionOrder contains invalid or duplicate arrow id {arrowId}.");
                    isValid = false;
                }
            }
        }

        return isValid;
    }

    private bool IsDependencyGraphAcyclic(HashSet<int> arrowIds, Dictionary<int, HashSet<int>> dependencyGraph)
    {
        Dictionary<int, int> indegreeByArrowId = new Dictionary<int, int>();
        foreach (int arrowId in arrowIds)
        {
            indegreeByArrowId[arrowId] = 0;
        }

        foreach (KeyValuePair<int, HashSet<int>> entry in dependencyGraph)
        {
            foreach (int blockedArrowId in entry.Value)
            {
                indegreeByArrowId[blockedArrowId]++;
            }
        }

        Queue<int> ready = new Queue<int>();
        foreach (KeyValuePair<int, int> entry in indegreeByArrowId)
        {
            if (entry.Value == 0)
            {
                ready.Enqueue(entry.Key);
            }
        }

        int visitedCount = 0;
        while (ready.Count > 0)
        {
            int arrowId = ready.Dequeue();
            visitedCount++;

            if (!dependencyGraph.TryGetValue(arrowId, out HashSet<int> blockedArrows))
            {
                continue;
            }

            foreach (int blockedArrowId in blockedArrows)
            {
                indegreeByArrowId[blockedArrowId]--;
                if (indegreeByArrowId[blockedArrowId] == 0)
                {
                    ready.Enqueue(blockedArrowId);
                }
            }
        }

        return visitedCount == arrowIds.Count;
    }
}

[Serializable]
public class VertexData
{
    public int vertexId;
    public int x;
    public int y;
    public CellContentType contentType = CellContentType.Empty;
    public int arrowId = -1;
    public ArrowDirection tipDirection = ArrowDirection.None;
    public List<int> neighborVertexIds = new List<int>();
}

[Serializable]
public class EdgeData
{
    public int fromVertexId;
    public int toVertexId;
}

[Serializable]
public class ArrowData
{
    public int arrowId;
    public int length;
    public List<GridPositionData> occupiedCells = new List<GridPositionData>();
    public GridPositionData tipCell;
    public ArrowDirection tipDirection = ArrowDirection.None;
    public bool isSolved;
}

[Serializable]
public class GridPositionData
{
    public int x;
    public int y;

    public GridPositionData()
    {
    }

    public GridPositionData(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

[Serializable]
public class DependencyData
{
    public int blockerArrowId;
    public int blockedArrowId;

    public DependencyData()
    {
    }

    public DependencyData(int blockerArrowId, int blockedArrowId)
    {
        this.blockerArrowId = blockerArrowId;
        this.blockedArrowId = blockedArrowId;
    }
}
