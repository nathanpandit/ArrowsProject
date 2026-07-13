using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class LevelGeneratorAlgorithm
{
    // Legacy backtracking generator retained for reference.
    // LevelEditor now calls ExactCoverLevelGeneratorAlgorithm instead.
    private static readonly ArrowDirection[] Directions =
    {
        ArrowDirection.Up,
        ArrowDirection.Down,
        ArrowDirection.Left,
        ArrowDirection.Right
    };

    public static LevelData Generate(EditorGenerationRequest request)
    {
        if (request == null)
        {
            Debug.LogWarning("LevelGeneratorAlgorithm.Generate failed: request is null.");
            return null;
        }

        SearchContext context = BuildContext(request);
        if (context == null)
        {
            return null;
        }

        if (!IsComponentFeasible(context.ShapeCells, context.TargetArrowCount, context.MinArrowLength, context.MaxArrowLength))
        {
            Debug.LogWarning("Level generation failed: painted shape components cannot fit the requested arrow count and length range.");
            return null;
        }

        SearchState initialState = new SearchState
        {
            FreeCells = new HashSet<GeneratorCell>(context.ShapeCells),
            PlacedArrows = new List<ArrowCandidate>(),
            RemainingArrowCount = context.TargetArrowCount,
            Graph = new DependencyGraph()
        };

        context.Stopwatch.Start();
        bool success = Search(initialState, context, out List<ArrowCandidate> placedArrows);
        context.Stopwatch.Stop();

        if (!success)
        {
            string reason = string.IsNullOrWhiteSpace(context.FailureReason)
                ? "no valid arrow arrangement found"
                : context.FailureReason;
            Debug.LogWarning($"Level generation failed: {reason}. Search frames: {context.Iterations}. Candidate growth steps: {context.CandidateGenerationSteps}. Candidate cap per frame: {context.MaxCandidatesPerState}.");
            return null;
        }

        if (!TryCreateValidatedLevelData(context, placedArrows, out LevelData levelData))
        {
            Debug.LogWarning("Level generation failed: final validation rejected the generated arrows.");
            return null;
        }

        return levelData;
    }

    private static SearchContext BuildContext(EditorGenerationRequest request)
    {
        if (request.gridSize <= 0)
        {
            Debug.LogWarning("Level generation failed: grid size must be positive.");
            return null;
        }

        if (request.targetArrowCount <= 0)
        {
            Debug.LogWarning("Level generation failed: target arrow count must be positive.");
            return null;
        }

        if (request.minArrowLength <= 0 || request.maxArrowLength <= 0 || request.minArrowLength > request.maxArrowLength)
        {
            Debug.LogWarning("Level generation failed: arrow length range is invalid.");
            return null;
        }

        HashSet<GeneratorCell> shapeCells = new HashSet<GeneratorCell>();
        if (request.paintedCells != null)
        {
            for (int i = 0; i < request.paintedCells.Count; i++)
            {
                GridPositionData cell = request.paintedCells[i];
                if (cell == null)
                {
                    continue;
                }

                if (cell.x < 0 || cell.x >= request.gridSize || cell.y < 0 || cell.y >= request.gridSize)
                {
                    Debug.LogWarning($"Level generation ignored painted cell outside the grid: ({cell.x}, {cell.y}).");
                    continue;
                }

                shapeCells.Add(new GeneratorCell(cell.x, cell.y));
            }
        }

        int totalCells = shapeCells.Count;
        if (totalCells == 0)
        {
            Debug.LogWarning("Level generation failed: paint at least one shape cell before generating.");
            return null;
        }

        if (request.targetArrowCount * request.minArrowLength > totalCells)
        {
            Debug.LogWarning("Level generation failed: arrow count and minimum length require more cells than the painted shape has.");
            return null;
        }

        if (request.targetArrowCount * request.maxArrowLength < totalCells)
        {
            Debug.LogWarning("Level generation failed: arrow count and maximum length cannot cover all painted cells.");
            return null;
        }

        int requestedCandidateCap = Mathf.Max(0, request.maxCandidatesPerSearchState);
        int adaptiveCandidateCap = request.gridSize >= 7 || request.targetArrowCount >= 10 ? 2048 : 1024;
        float requestedGenerationSeconds = Mathf.Max(0f, request.maxGenerationSeconds);
        float adaptiveGenerationSeconds = request.gridSize >= 7 || request.targetArrowCount >= 10 ? 15f : 8f;

        int seed = request.useRandomSeed ? request.randomSeed : Environment.TickCount;
        return new SearchContext
        {
            GridSize = request.gridSize,
            TargetArrowCount = request.targetArrowCount,
            MinArrowLength = request.minArrowLength,
            MaxArrowLength = request.maxArrowLength,
            Lives = request.lives,
            Seed = seed,
            MaxIterations = Mathf.Max(0, request.maxSearchIterations),
            MaxGenerationSeconds = requestedGenerationSeconds <= 0f ? 0f : Mathf.Max(requestedGenerationSeconds, adaptiveGenerationSeconds),
            MaxCandidatesPerState = requestedCandidateCap == 0 ? 0 : Mathf.Max(requestedCandidateCap, adaptiveCandidateCap),
            ShapeCells = shapeCells,
            Random = new System.Random(seed),
            Stopwatch = new System.Diagnostics.Stopwatch()
        };
    }

    private static bool Search(SearchState state, SearchContext context, out List<ArrowCandidate> result)
    {
        result = null;

        if (context.ShouldStop())
        {
            return false;
        }

        context.Iterations++;

        if (state.RemainingArrowCount == 0)
        {
            if (state.FreeCells.Count == 0)
            {
                result = new List<ArrowCandidate>(state.PlacedArrows);
                return true;
            }

            return false;
        }

        if (!IsComponentFeasible(state.FreeCells, state.RemainingArrowCount, context.MinArrowLength, context.MaxArrowLength))
        {
            return false;
        }

        List<ArrowCandidate> candidates = GenerateCandidates(state, context);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (context.ShouldStop())
            {
                return false;
            }

            ArrowCandidate candidate = candidates[i];
            HashSet<GeneratorCell> newFreeCells = new HashSet<GeneratorCell>(state.FreeCells);
            bool allCellsWereFree = true;
            for (int cellIndex = 0; cellIndex < candidate.Cells.Count; cellIndex++)
            {
                if (!newFreeCells.Remove(candidate.Cells[cellIndex]))
                {
                    allCellsWereFree = false;
                    break;
                }
            }

            if (!allCellsWereFree)
            {
                continue;
            }

            int remainingAfterPlacement = state.RemainingArrowCount - 1;
            if (remainingAfterPlacement == 0)
            {
                if (newFreeCells.Count != 0)
                {
                    continue;
                }
            }
            else if (!IsComponentFeasible(newFreeCells, remainingAfterPlacement, context.MinArrowLength, context.MaxArrowLength))
            {
                continue;
            }

            if (IsArrowBlockedBy(candidate, candidate, context.GridSize))
            {
                continue;
            }

            ComputeCandidateEdges(candidate, state.PlacedArrows, context.GridSize, out HashSet<int> incoming, out HashSet<int> outgoing);
            if (state.Graph.WouldCreateCycle(candidate.ArrowId, incoming, outgoing))
            {
                continue;
            }

            DependencyGraph newGraph = state.Graph.Clone();
            newGraph.AddArrow(candidate.ArrowId);
            newGraph.AddIncomingAndOutgoing(candidate.ArrowId, incoming, outgoing);

            SearchState nextState = new SearchState
            {
                FreeCells = newFreeCells,
                PlacedArrows = new List<ArrowCandidate>(state.PlacedArrows) { candidate },
                RemainingArrowCount = remainingAfterPlacement,
                Graph = newGraph
            };

            if (Search(nextState, context, out result))
            {
                return true;
            }
        }

        return false;
    }

    private static List<ArrowCandidate> GenerateCandidates(SearchState state, SearchContext context)
    {
        List<ArrowCandidate> candidates = new List<ArrowCandidate>();
        HashSet<string> candidateIds = new HashSet<string>();
        List<FreeComponent> components = ComputeConnectedComponents(state.FreeCells, context.MinArrowLength, context.MaxArrowLength);
        components.Sort((a, b) =>
        {
            int slackCompare = a.Slack.CompareTo(b.Slack);
            return slackCompare != 0 ? slackCompare : a.Cells.Count.CompareTo(b.Cells.Count);
        });

        int freeCellCount = state.FreeCells.Count;
        int remainingAfterCandidate = state.RemainingArrowCount - 1;
        int globalMinLength = Math.Max(context.MinArrowLength, freeCellCount - remainingAfterCandidate * context.MaxArrowLength);
        int globalMaxLength = Math.Min(context.MaxArrowLength, freeCellCount - remainingAfterCandidate * context.MinArrowLength);

        for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            if (context.ShouldStop() || HasReachedCandidateLimit(context, candidates))
            {
                break;
            }

            FreeComponent component = components[componentIndex];
            int minLength = globalMinLength;
            int maxLength = Math.Min(globalMaxLength, component.Cells.Count);

            if (minLength > maxLength)
            {
                continue;
            }

            List<int> lengths = new List<int>();
            for (int length = minLength; length <= maxLength; length++)
            {
                lengths.Add(length);
            }

            Shuffle(lengths, context.Random);

            List<GeneratorCell> tipCells = new List<GeneratorCell>(component.Cells);
            Shuffle(tipCells, context.Random);

            for (int lengthIndex = 0; lengthIndex < lengths.Count; lengthIndex++)
            {
                if (context.ShouldStop() || HasReachedCandidateLimit(context, candidates))
                {
                    break;
                }

                int length = lengths[lengthIndex];

                for (int tipIndex = 0; tipIndex < tipCells.Count; tipIndex++)
                {
                    if (context.ShouldStop() || HasReachedCandidateLimit(context, candidates))
                    {
                        break;
                    }

                    GeneratorCell tipCell = tipCells[tipIndex];
                    List<ArrowDirection> directions = new List<ArrowDirection>(Directions);
                    Shuffle(directions, context.Random);

                    for (int directionIndex = 0; directionIndex < directions.Count; directionIndex++)
                    {
                        if (context.ShouldStop() || HasReachedCandidateLimit(context, candidates))
                        {
                            break;
                        }

                        GenerateCandidatesForTipDirection(
                            state,
                            context,
                            component,
                            length,
                            tipCell,
                            directions[directionIndex],
                            candidates,
                            candidateIds);
                    }
                }
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            candidates[i].Score = ScoreCandidate(state.FreeCells, candidates[i], context);
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return candidates;
    }

    private static void GenerateCandidatesForTipDirection(
        SearchState state,
        SearchContext context,
        FreeComponent component,
        int length,
        GeneratorCell tipCell,
        ArrowDirection direction,
        List<ArrowCandidate> candidates,
        HashSet<string> candidateIds)
    {
        if (!component.CellSet.Contains(tipCell) || !state.FreeCells.Contains(tipCell))
        {
            return;
        }

        if (length == 1)
        {
            return;
        }

        GeneratorCell firstBodyCell = tipCell - DirectionToVector(direction);
        if (!component.CellSet.Contains(firstBodyCell) || !state.FreeCells.Contains(firstBodyCell))
        {
            return;
        }

        List<GeneratorCell> path = new List<GeneratorCell> { tipCell, firstBodyCell };
        HashSet<GeneratorCell> pathCells = new HashSet<GeneratorCell>(path);
        GrowCandidatePath(
            state,
            context,
            component,
            length,
            tipCell,
            direction,
            path,
            pathCells,
            candidates,
            candidateIds);
    }

    private static void GrowCandidatePath(
        SearchState state,
        SearchContext context,
        FreeComponent component,
        int targetLength,
        GeneratorCell tipCell,
        ArrowDirection direction,
        List<GeneratorCell> path,
        HashSet<GeneratorCell> pathCells,
        List<ArrowCandidate> candidates,
        HashSet<string> candidateIds)
    {
        context.CandidateGenerationSteps++;

        if (context.ShouldStop() || HasReachedCandidateLimit(context, candidates))
        {
            return;
        }

        if (path.Count == targetLength)
        {
            if (!DoesArrowBlockItself(path, direction, context.GridSize) &&
                CandidateLeavesFeasibleRemainder(state, context, path))
            {
                AddCandidate(context, state.PlacedArrows.Count + 1, path, tipCell, direction, candidates, candidateIds);
            }

            return;
        }

        GeneratorCell tail = path[path.Count - 1];
        List<GeneratorCell> neighbors = GetNeighbors(tail);
        Shuffle(neighbors, context.Random);

        for (int i = 0; i < neighbors.Count; i++)
        {
            GeneratorCell nextCell = neighbors[i];
            if (!component.CellSet.Contains(nextCell) || !state.FreeCells.Contains(nextCell) || pathCells.Contains(nextCell))
            {
                continue;
            }

            path.Add(nextCell);
            pathCells.Add(nextCell);

            int remainingCellsNeeded = targetLength - path.Count;
            if (!DoesArrowBlockItself(path, direction, context.GridSize) &&
                CanGrowPathToLength(component, state.FreeCells, pathCells, nextCell, remainingCellsNeeded))
            {
                GrowCandidatePath(
                    state,
                    context,
                    component,
                    targetLength,
                    tipCell,
                    direction,
                    path,
                    pathCells,
                    candidates,
                    candidateIds);
            }

            pathCells.Remove(nextCell);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool CandidateLeavesFeasibleRemainder(SearchState state, SearchContext context, List<GeneratorCell> candidateCells)
    {
        int remainingAfterCandidate = state.RemainingArrowCount - 1;
        HashSet<GeneratorCell> remainingCells = new HashSet<GeneratorCell>(state.FreeCells);

        for (int i = 0; i < candidateCells.Count; i++)
        {
            if (!remainingCells.Remove(candidateCells[i]))
            {
                return false;
            }
        }

        if (remainingAfterCandidate == 0)
        {
            return remainingCells.Count == 0;
        }

        return IsComponentFeasible(remainingCells, remainingAfterCandidate, context.MinArrowLength, context.MaxArrowLength);
    }

    private static bool CanGrowPathToLength(
        FreeComponent component,
        HashSet<GeneratorCell> freeCells,
        HashSet<GeneratorCell> pathCells,
        GeneratorCell tail,
        int remainingCellsNeeded)
    {
        if (remainingCellsNeeded <= 0)
        {
            return true;
        }

        int reachableCells = 0;
        Queue<GeneratorCell> queue = new Queue<GeneratorCell>();
        HashSet<GeneratorCell> visited = new HashSet<GeneratorCell>();
        List<GeneratorCell> tailNeighbors = GetNeighbors(tail);

        for (int i = 0; i < tailNeighbors.Count; i++)
        {
            GeneratorCell neighbor = tailNeighbors[i];
            if (!component.CellSet.Contains(neighbor) || !freeCells.Contains(neighbor) || pathCells.Contains(neighbor) || !visited.Add(neighbor))
            {
                continue;
            }

            queue.Enqueue(neighbor);
        }

        while (queue.Count > 0)
        {
            GeneratorCell current = queue.Dequeue();
            reachableCells++;
            if (reachableCells >= remainingCellsNeeded)
            {
                return true;
            }

            List<GeneratorCell> neighbors = GetNeighbors(current);
            for (int i = 0; i < neighbors.Count; i++)
            {
                GeneratorCell neighbor = neighbors[i];
                if (!component.CellSet.Contains(neighbor) || !freeCells.Contains(neighbor) || pathCells.Contains(neighbor) || !visited.Add(neighbor))
                {
                    continue;
                }

                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private static void AddCandidate(
        SearchContext context,
        int arrowId,
        List<GeneratorCell> path,
        GeneratorCell tipCell,
        ArrowDirection direction,
        List<ArrowCandidate> candidates,
        HashSet<string> candidateIds)
    {
        if (HasReachedCandidateLimit(context, candidates))
        {
            return;
        }

        string candidateId = BuildCandidateId(path, tipCell, direction);
        if (!candidateIds.Add(candidateId))
        {
            return;
        }

        candidates.Add(new ArrowCandidate
        {
            ArrowId = arrowId,
            Cells = new List<GeneratorCell>(path),
            CellSet = new HashSet<GeneratorCell>(path),
            TipCell = tipCell,
            Direction = direction,
            CandidateId = candidateId
        });
    }

    private static bool HasReachedCandidateLimit(SearchContext context, List<ArrowCandidate> candidates)
    {
        return context.MaxCandidatesPerState > 0 && candidates.Count >= context.MaxCandidatesPerState;
    }

    private static float ScoreCandidate(HashSet<GeneratorCell> freeCells, ArrowCandidate candidate, SearchContext context)
    {
        HashSet<GeneratorCell> remaining = new HashSet<GeneratorCell>(freeCells);
        for (int i = 0; i < candidate.Cells.Count; i++)
        {
            remaining.Remove(candidate.Cells[i]);
        }

        List<FreeComponent> components = ComputeConnectedComponents(remaining, context.MinArrowLength, context.MaxArrowLength);
        int tinyComponentPenalty = 0;
        int slackPenalty = 0;
        for (int i = 0; i < components.Count; i++)
        {
            int size = components[i].Cells.Count;
            if (size > 0 && size < context.MinArrowLength)
            {
                tinyComponentPenalty += 100;
            }

            slackPenalty += Math.Max(0, components[i].Slack);
        }

        float randomNoise = (float)context.Random.NextDouble();
        return -components.Count * 10f - tinyComponentPenalty - slackPenalty + randomNoise;
    }

    private static bool IsComponentFeasible(HashSet<GeneratorCell> freeCells, int remainingArrowCount, int minLength, int maxLength)
    {
        if (remainingArrowCount == 0)
        {
            return freeCells.Count == 0;
        }

        List<FreeComponent> components = ComputeConnectedComponents(freeCells, minLength, maxLength);
        int minNeeded = 0;
        int maxAllowed = 0;

        for (int i = 0; i < components.Count; i++)
        {
            int size = components[i].Cells.Count;
            if (size > 0 && size < minLength)
            {
                return false;
            }

            int lower = CeilDiv(size, maxLength);
            int upper = size / minLength;
            if (lower > upper)
            {
                return false;
            }

            minNeeded += lower;
            maxAllowed += upper;
        }

        return minNeeded <= remainingArrowCount && maxAllowed >= remainingArrowCount;
    }

    private static List<FreeComponent> ComputeConnectedComponents(HashSet<GeneratorCell> cells, int minLength, int maxLength)
    {
        List<FreeComponent> components = new List<FreeComponent>();
        HashSet<GeneratorCell> unvisited = new HashSet<GeneratorCell>(cells);
        Queue<GeneratorCell> queue = new Queue<GeneratorCell>();

        while (unvisited.Count > 0)
        {
            GeneratorCell start = FirstCell(unvisited);
            unvisited.Remove(start);
            queue.Enqueue(start);

            FreeComponent component = new FreeComponent();
            component.Cells.Add(start);
            component.CellSet.Add(start);

            while (queue.Count > 0)
            {
                GeneratorCell current = queue.Dequeue();
                List<GeneratorCell> neighbors = GetNeighbors(current);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    GeneratorCell neighbor = neighbors[i];
                    if (!unvisited.Remove(neighbor))
                    {
                        continue;
                    }

                    queue.Enqueue(neighbor);
                    component.Cells.Add(neighbor);
                    component.CellSet.Add(neighbor);
                }
            }

            int lower = CeilDiv(component.Cells.Count, Math.Max(1, maxLength));
            int upper = component.Cells.Count / Math.Max(1, minLength);
            component.Slack = upper - lower;
            components.Add(component);
        }

        return components;
    }

    private static void ComputeCandidateEdges(
        ArrowCandidate candidate,
        List<ArrowCandidate> placedArrows,
        int gridSize,
        out HashSet<int> incoming,
        out HashSet<int> outgoing)
    {
        incoming = new HashSet<int>();
        outgoing = new HashSet<int>();

        for (int i = 0; i < placedArrows.Count; i++)
        {
            ArrowCandidate placedArrow = placedArrows[i];

            if (IsArrowBlockedBy(candidate, placedArrow, gridSize))
            {
                incoming.Add(placedArrow.ArrowId);
            }

            if (IsArrowBlockedBy(placedArrow, candidate, gridSize))
            {
                outgoing.Add(placedArrow.ArrowId);
            }
        }
    }

    private static bool IsArrowBlockedBy(ArrowCandidate movingArrow, ArrowCandidate possibleBlocker, int gridSize)
    {
        if (movingArrow == null || possibleBlocker == null)
        {
            return false;
        }

        if (movingArrow.ArrowId == possibleBlocker.ArrowId)
        {
            return DoesArrowBlockItself(movingArrow.Cells, movingArrow.Direction, gridSize);
        }

        return HasBlockingCellsInMovementPath(movingArrow.Cells, movingArrow.Direction, possibleBlocker.CellSet, gridSize);
    }

    private static bool HasBlockingCellsInMovementPath(
        List<GeneratorCell> movingCells,
        ArrowDirection direction,
        HashSet<GeneratorCell> blockerCells,
        int gridSize)
    {
        if (movingCells == null || movingCells.Count == 0 || blockerCells == null || direction == ArrowDirection.None)
        {
            return false;
        }

        GeneratorCell movementDirection = DirectionToVector(direction);

        for (int i = 0; i < movingCells.Count; i++)
        {
            GeneratorCell cursor = movingCells[i] + movementDirection;

            while (IsInsideGrid(cursor, gridSize))
            {
                if (blockerCells.Contains(cursor))
                {
                    return true;
                }

                cursor += movementDirection;
            }
        }

        return false;
    }

    private static bool DoesArrowBlockItself(List<GeneratorCell> arrowCells, ArrowDirection direction, int gridSize)
    {
        if (arrowCells == null || arrowCells.Count <= 1 || direction == ArrowDirection.None)
        {
            return false;
        }

        HashSet<GeneratorCell> arrowCellSet = new HashSet<GeneratorCell>(arrowCells);
        Dictionary<GeneratorCell, int> indexByCell = new Dictionary<GeneratorCell, int>();
        for (int i = 0; i < arrowCells.Count; i++)
        {
            indexByCell[arrowCells[i]] = i;
        }

        GeneratorCell movementDirection = DirectionToVector(direction);
        for (int i = 0; i < arrowCells.Count; i++)
        {
            GeneratorCell cursor = arrowCells[i] + movementDirection;
            int expectedAheadIndex = i - 1;

            while (expectedAheadIndex >= 0 &&
                   indexByCell.TryGetValue(cursor, out int actualAheadIndex) &&
                   actualAheadIndex == expectedAheadIndex)
            {
                cursor += movementDirection;
                expectedAheadIndex--;
            }

            while (IsInsideGrid(cursor, gridSize))
            {
                if (arrowCellSet.Contains(cursor))
                {
                    return true;
                }

                cursor += movementDirection;
            }
        }

        return false;
    }

    private static bool TryCreateValidatedLevelData(SearchContext context, List<ArrowCandidate> placedArrows, out LevelData levelData)
    {
        levelData = null;
        if (!TryValidateFinalArrows(context, placedArrows, out DependencyGraph dependencyGraph, out List<int> solutionOrder))
        {
            return false;
        }

        placedArrows.Sort((a, b) => a.ArrowId.CompareTo(b.ArrowId));

        levelData = new LevelData
        {
            gridSize = context.GridSize,
            width = context.GridSize,
            height = context.GridSize,
            targetArrowCount = context.TargetArrowCount,
            minArrowLength = context.MinArrowLength,
            maxArrowLength = context.MaxArrowLength,
            lives = context.Lives,
            createdAt = DateTime.UtcNow.ToString("o"),
            notes = $"Generated by backtracking path-arrow generator. Seed: {context.Seed}. Iterations: {context.Iterations}.",
            arrows = new List<ArrowData>(),
            shapeCells = ToSortedGridPositions(context.ShapeCells),
            dependencies = dependencyGraph.ToDependencyData(),
            solutionOrder = solutionOrder
        };

        for (int i = 0; i < placedArrows.Count; i++)
        {
            ArrowCandidate arrow = placedArrows[i];
            ArrowData arrowData = new ArrowData
            {
                arrowId = arrow.ArrowId,
                length = arrow.Cells.Count,
                occupiedCells = ToGridPositions(arrow.Cells),
                tipCell = new GridPositionData(arrow.TipCell.X, arrow.TipCell.Y),
                tipDirection = arrow.Direction,
                isSolved = false
            };

            levelData.arrows.Add(arrowData);
        }

        return true;
    }

    private static bool TryValidateFinalArrows(
        SearchContext context,
        List<ArrowCandidate> placedArrows,
        out DependencyGraph dependencyGraph,
        out List<int> solutionOrder)
    {
        dependencyGraph = new DependencyGraph();
        solutionOrder = new List<int>();

        if (placedArrows == null || placedArrows.Count != context.TargetArrowCount)
        {
            return false;
        }

        HashSet<GeneratorCell> coveredCells = new HashSet<GeneratorCell>();
        HashSet<int> arrowIds = new HashSet<int>();

        for (int i = 0; i < placedArrows.Count; i++)
        {
            ArrowCandidate arrow = placedArrows[i];
            if (!arrowIds.Add(arrow.ArrowId))
            {
                return false;
            }

            dependencyGraph.AddArrow(arrow.ArrowId);

            if (!IsLegalArrowPath(arrow, context.ShapeCells, context.MinArrowLength, context.MaxArrowLength, context.GridSize))
            {
                return false;
            }

            for (int cellIndex = 0; cellIndex < arrow.Cells.Count; cellIndex++)
            {
                GeneratorCell cell = arrow.Cells[cellIndex];
                if (!context.ShapeCells.Contains(cell) || !coveredCells.Add(cell))
                {
                    return false;
                }
            }
        }

        if (coveredCells.Count != context.ShapeCells.Count)
        {
            return false;
        }

        for (int movingIndex = 0; movingIndex < placedArrows.Count; movingIndex++)
        {
            ArrowCandidate movingArrow = placedArrows[movingIndex];
            if (IsArrowBlockedBy(movingArrow, movingArrow, context.GridSize))
            {
                return false;
            }

            for (int blockerIndex = 0; blockerIndex < placedArrows.Count; blockerIndex++)
            {
                if (movingIndex == blockerIndex)
                {
                    continue;
                }

                ArrowCandidate possibleBlocker = placedArrows[blockerIndex];
                if (IsArrowBlockedBy(movingArrow, possibleBlocker, context.GridSize))
                {
                    dependencyGraph.AddEdge(possibleBlocker.ArrowId, movingArrow.ArrowId);
                }
            }
        }

        if (!dependencyGraph.IsAcyclic())
        {
            return false;
        }

        solutionOrder = dependencyGraph.TopologicalSort();
        return solutionOrder.Count == placedArrows.Count;
    }

    private static bool IsLegalArrowPath(ArrowCandidate arrow, HashSet<GeneratorCell> shapeCells, int minLength, int maxLength, int gridSize)
    {
        if (arrow == null || arrow.Direction == ArrowDirection.None)
        {
            return false;
        }

        if (arrow.Cells == null || arrow.Cells.Count < minLength || arrow.Cells.Count > maxLength || arrow.Cells.Count < 2)
        {
            return false;
        }

        if (arrow.Cells[0] != arrow.TipCell)
        {
            return false;
        }

        HashSet<GeneratorCell> seenCells = new HashSet<GeneratorCell>();
        for (int i = 0; i < arrow.Cells.Count; i++)
        {
            GeneratorCell cell = arrow.Cells[i];
            if (!shapeCells.Contains(cell) || !seenCells.Add(cell))
            {
                return false;
            }

            if (i > 0 && ManhattanDistance(arrow.Cells[i - 1], cell) != 1)
            {
                return false;
            }
        }

        GeneratorCell expectedFirstBodyCell = arrow.TipCell - DirectionToVector(arrow.Direction);
        if (arrow.Cells[1] != expectedFirstBodyCell)
        {
            return false;
        }

        return !DoesArrowBlockItself(arrow.Cells, arrow.Direction, gridSize);
    }

    private static List<GridPositionData> ToGridPositions(List<GeneratorCell> cells)
    {
        List<GridPositionData> positions = new List<GridPositionData>();
        for (int i = 0; i < cells.Count; i++)
        {
            positions.Add(new GridPositionData(cells[i].X, cells[i].Y));
        }

        return positions;
    }

    private static List<GridPositionData> ToSortedGridPositions(HashSet<GeneratorCell> cells)
    {
        List<GeneratorCell> sortedCells = new List<GeneratorCell>(cells);
        sortedCells.Sort(CompareCells);
        return ToGridPositions(sortedCells);
    }

    private static string BuildCandidateId(List<GeneratorCell> cells, GeneratorCell tipCell, ArrowDirection direction)
    {
        List<GeneratorCell> sortedCells = new List<GeneratorCell>(cells);
        sortedCells.Sort(CompareCells);

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < sortedCells.Count; i++)
        {
            builder.Append(sortedCells[i].X);
            builder.Append(',');
            builder.Append(sortedCells[i].Y);
            builder.Append(';');
        }

        builder.Append("|tip=");
        builder.Append(tipCell.X);
        builder.Append(',');
        builder.Append(tipCell.Y);
        builder.Append("|dir=");
        builder.Append(direction);
        return builder.ToString();
    }

    private static GeneratorCell FirstCell(HashSet<GeneratorCell> cells)
    {
        foreach (GeneratorCell cell in cells)
        {
            return cell;
        }

        return new GeneratorCell(0, 0);
    }

    private static List<GeneratorCell> GetNeighbors(GeneratorCell cell)
    {
        return new List<GeneratorCell>
        {
            new GeneratorCell(cell.X + 1, cell.Y),
            new GeneratorCell(cell.X - 1, cell.Y),
            new GeneratorCell(cell.X, cell.Y + 1),
            new GeneratorCell(cell.X, cell.Y - 1)
        };
    }

    private static GeneratorCell DirectionToVector(ArrowDirection direction)
    {
        switch (direction)
        {
            case ArrowDirection.Up:
                return new GeneratorCell(0, 1);
            case ArrowDirection.Down:
                return new GeneratorCell(0, -1);
            case ArrowDirection.Left:
                return new GeneratorCell(-1, 0);
            case ArrowDirection.Right:
                return new GeneratorCell(1, 0);
            default:
                return new GeneratorCell(0, 0);
        }
    }

    private static int ManhattanDistance(GeneratorCell a, GeneratorCell b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static int CeilDiv(int value, int divisor)
    {
        if (divisor <= 0)
        {
            return 0;
        }

        return (value + divisor - 1) / divisor;
    }

    private static bool IsInsideGrid(GeneratorCell cell, int gridSize)
    {
        return cell.X >= 0 && cell.X < gridSize && cell.Y >= 0 && cell.Y < gridSize;
    }

    private static int CompareCells(GeneratorCell a, GeneratorCell b)
    {
        int yCompare = a.Y.CompareTo(b.Y);
        return yCompare != 0 ? yCompare : a.X.CompareTo(b.X);
    }

    private static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private class SearchContext
    {
        public int GridSize;
        public int TargetArrowCount;
        public int MinArrowLength;
        public int MaxArrowLength;
        public int Lives;
        public int Seed;
        public int MaxIterations;
        public int MaxCandidatesPerState;
        public float MaxGenerationSeconds;
        public int Iterations;
        public int CandidateGenerationSteps;
        public string FailureReason;
        public HashSet<GeneratorCell> ShapeCells;
        public System.Random Random;
        public System.Diagnostics.Stopwatch Stopwatch;

        public bool ShouldStop()
        {
            if (MaxIterations > 0 && Iterations >= MaxIterations)
            {
                FailureReason = "maximum search iterations reached";
                return true;
            }

            if (MaxGenerationSeconds > 0f && Stopwatch.Elapsed.TotalSeconds >= MaxGenerationSeconds)
            {
                FailureReason = "maximum generation time reached";
                return true;
            }

            return false;
        }
    }

    private class SearchState
    {
        public HashSet<GeneratorCell> FreeCells;
        public List<ArrowCandidate> PlacedArrows;
        public int RemainingArrowCount;
        public DependencyGraph Graph;
    }

    private class FreeComponent
    {
        public readonly List<GeneratorCell> Cells = new List<GeneratorCell>();
        public readonly HashSet<GeneratorCell> CellSet = new HashSet<GeneratorCell>();
        public int Slack;
    }

    private class ArrowCandidate
    {
        public int ArrowId;
        public List<GeneratorCell> Cells;
        public HashSet<GeneratorCell> CellSet;
        public GeneratorCell TipCell;
        public ArrowDirection Direction;
        public string CandidateId;
        public float Score;
    }

    private class DependencyGraph
    {
        private readonly HashSet<int> nodes = new HashSet<int>();
        private readonly Dictionary<int, HashSet<int>> outgoingByArrowId = new Dictionary<int, HashSet<int>>();

        public void AddArrow(int arrowId)
        {
            nodes.Add(arrowId);
            if (!outgoingByArrowId.ContainsKey(arrowId))
            {
                outgoingByArrowId.Add(arrowId, new HashSet<int>());
            }
        }

        public void AddEdge(int blockerArrowId, int blockedArrowId)
        {
            if (blockerArrowId == blockedArrowId)
            {
                return;
            }

            AddArrow(blockerArrowId);
            AddArrow(blockedArrowId);
            outgoingByArrowId[blockerArrowId].Add(blockedArrowId);
        }

        public void AddIncomingAndOutgoing(int arrowId, HashSet<int> incoming, HashSet<int> outgoing)
        {
            foreach (int blockerId in incoming)
            {
                AddEdge(blockerId, arrowId);
            }

            foreach (int blockedId in outgoing)
            {
                AddEdge(arrowId, blockedId);
            }
        }

        public bool WouldCreateCycle(int newArrowId, HashSet<int> incoming, HashSet<int> outgoing)
        {
            if (incoming.Count == 0 || outgoing.Count == 0)
            {
                return false;
            }

            foreach (int outgoingTarget in outgoing)
            {
                if (CanReachAny(outgoingTarget, incoming))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsAcyclic()
        {
            return TopologicalSort().Count == nodes.Count;
        }

        public List<int> TopologicalSort()
        {
            Dictionary<int, int> indegreeByArrowId = new Dictionary<int, int>();
            foreach (int node in nodes)
            {
                indegreeByArrowId[node] = 0;
            }

            foreach (KeyValuePair<int, HashSet<int>> entry in outgoingByArrowId)
            {
                foreach (int blockedId in entry.Value)
                {
                    if (!indegreeByArrowId.ContainsKey(blockedId))
                    {
                        indegreeByArrowId[blockedId] = 0;
                    }

                    indegreeByArrowId[blockedId]++;
                }
            }

            List<int> ready = new List<int>();
            foreach (KeyValuePair<int, int> entry in indegreeByArrowId)
            {
                if (entry.Value == 0)
                {
                    ready.Add(entry.Key);
                }
            }

            ready.Sort();
            List<int> order = new List<int>();
            while (ready.Count > 0)
            {
                int node = ready[0];
                ready.RemoveAt(0);
                order.Add(node);

                if (!outgoingByArrowId.TryGetValue(node, out HashSet<int> outgoing))
                {
                    continue;
                }

                foreach (int blockedId in outgoing)
                {
                    indegreeByArrowId[blockedId]--;
                    if (indegreeByArrowId[blockedId] == 0)
                    {
                        ready.Add(blockedId);
                        ready.Sort();
                    }
                }
            }

            return order;
        }

        public DependencyGraph Clone()
        {
            DependencyGraph clone = new DependencyGraph();
            foreach (int node in nodes)
            {
                clone.AddArrow(node);
            }

            foreach (KeyValuePair<int, HashSet<int>> entry in outgoingByArrowId)
            {
                foreach (int blockedId in entry.Value)
                {
                    clone.AddEdge(entry.Key, blockedId);
                }
            }

            return clone;
        }

        public List<DependencyData> ToDependencyData()
        {
            List<DependencyData> dependencies = new List<DependencyData>();
            foreach (KeyValuePair<int, HashSet<int>> entry in outgoingByArrowId)
            {
                foreach (int blockedId in entry.Value)
                {
                    dependencies.Add(new DependencyData(entry.Key, blockedId));
                }
            }

            dependencies.Sort((a, b) =>
            {
                int blockerCompare = a.blockerArrowId.CompareTo(b.blockerArrowId);
                return blockerCompare != 0 ? blockerCompare : a.blockedArrowId.CompareTo(b.blockedArrowId);
            });
            return dependencies;
        }

        private bool CanReachAny(int startArrowId, HashSet<int> targets)
        {
            HashSet<int> visited = new HashSet<int>();
            Stack<int> stack = new Stack<int>();
            stack.Push(startArrowId);

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (targets.Contains(current))
                {
                    return true;
                }

                if (!outgoingByArrowId.TryGetValue(current, out HashSet<int> outgoing))
                {
                    continue;
                }

                foreach (int next in outgoing)
                {
                    stack.Push(next);
                }
            }

            return false;
        }
    }

    private struct GeneratorCell : IEquatable<GeneratorCell>
    {
        public readonly int X;
        public readonly int Y;

        public GeneratorCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(GeneratorCell other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is GeneratorCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static bool operator ==(GeneratorCell left, GeneratorCell right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GeneratorCell left, GeneratorCell right)
        {
            return !left.Equals(right);
        }

        public static GeneratorCell operator +(GeneratorCell left, GeneratorCell right)
        {
            return new GeneratorCell(left.X + right.X, left.Y + right.Y);
        }

        public static GeneratorCell operator -(GeneratorCell left, GeneratorCell right)
        {
            return new GeneratorCell(left.X - right.X, left.Y - right.Y);
        }
    }
}
