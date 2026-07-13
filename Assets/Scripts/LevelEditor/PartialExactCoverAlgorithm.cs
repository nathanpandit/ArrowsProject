using System;
using System.Collections.Generic;
using UnityEngine;

public static class PartialExactCoverAlgorithm
{
    public static LevelData Generate(EditorGenerationRequest request)
    {
        GenerationContext context = BuildContext(request);
        if (context == null)
        {
            return null;
        }

        context.Stopwatch.Start();
        context.ArrowCount = context.EffectiveMaxArrowCount;
        context.ActiveMinArrowLength = context.MinArrowLength;
        context.ActiveMaxArrowLength = context.MaxArrowLength;
        context.LazyCandidatesByCell = new Dictionary<Cell, List<CandidateArrow>>();

        int attemptCount = 0;
        int attemptLimit = context.MaxGenerationSeconds <= 0f && context.MaxSearchFrames <= 0 ? 256 : int.MaxValue;
        while (!context.ShouldStop() && attemptCount < attemptLimit)
        {
            attemptCount++;
            int targetArrowCount = ChooseAnytimeAttemptArrowCount(context, attemptCount);
            RunAnytimeBeamAttempt(context, targetArrowCount);

            if (context.BestPartialCoveredCellCount >= context.OccupiedCells.Count)
            {
                break;
            }
        }

        context.Stopwatch.Stop();
        context.AnytimeAttemptCount = attemptCount;
        if (attemptCount >= attemptLimit && string.IsNullOrWhiteSpace(context.FailureReason))
        {
            context.Fail("internal attempt cap reached");
        }

        return BuildBestPartialOrFail(context);
    }

    private static GenerationContext BuildContext(EditorGenerationRequest request)
    {
        if (request == null)
        {
            Debug.LogWarning("Partial-exact-cover generation failed: request is null.");
            return null;
        }

        if (request.gridSize <= 0)
        {
            Debug.LogWarning("Partial-exact-cover generation failed: board size must be positive.");
            return null;
        }

        if (request.minArrowLength <= 0)
        {
            Debug.LogWarning("Partial-exact-cover generation failed: min arrow length must be positive.");
            return null;
        }

        if (request.minArrowLength > request.maxArrowLength)
        {
            Debug.LogWarning("Partial-exact-cover generation failed: min arrow length cannot exceed max arrow length.");
            return null;
        }

        if (request.targetArrowCount < 0 || request.minArrowCount < 0 || request.maxArrowCount < 0)
        {
            Debug.LogWarning("Partial-exact-cover generation failed: arrow count values cannot be negative.");
            return null;
        }

        int requestedMinArrowCount = Mathf.Max(0, request.minArrowCount);
        int requestedMaxArrowCount = Mathf.Max(0, request.maxArrowCount);
        if (requestedMinArrowCount == 0 && requestedMaxArrowCount == 0 && request.targetArrowCount > 0)
        {
            requestedMinArrowCount = request.targetArrowCount;
            requestedMaxArrowCount = request.targetArrowCount;
        }

        if (requestedMaxArrowCount < requestedMinArrowCount)
        {
            Debug.LogWarning("Partial-exact-cover generation failed: min arrow count cannot exceed max arrow count.");
            return null;
        }

        HashSet<Cell> occupiedCells = new HashSet<Cell>();
        if (request.paintedCells != null)
        {
            for (int i = 0; i < request.paintedCells.Count; i++)
            {
                GridPositionData cell = request.paintedCells[i];
                if (cell == null)
                {
                    continue;
                }

                Cell occupiedCell = new Cell(cell.x, cell.y);
                if (!IsInsideBoard(occupiedCell, request.gridSize, request.gridSize))
                {
                    Debug.LogWarning($"Partial-exact-cover generation failed: occupied cell ({cell.x}, {cell.y}) is outside the board.");
                    return null;
                }

                occupiedCells.Add(occupiedCell);
            }
        }

        int lengthMaximumArrowCount = occupiedCells.Count / request.minArrowLength;
        int effectiveMaxArrowCount = Math.Min(requestedMaxArrowCount, lengthMaximumArrowCount);
        int effectiveMinArrowCount = Math.Min(requestedMinArrowCount, effectiveMaxArrowCount);

        if (effectiveMaxArrowCount <= 0)
        {
            Debug.LogWarning($"Partial-exact-cover generation failed: no arrow can fit inside {occupiedCells.Count} painted cells with length range {request.minArrowLength}-{request.maxArrowLength}.");
            return null;
        }

        int seed = request.useRandomSeed ? request.randomSeed : Environment.TickCount;
        int requestedIterations = Mathf.Max(0, request.maxSearchIterations);
        float requestedSeconds = Mathf.Max(0f, request.maxGenerationSeconds);
        int pivotCandidateLimit = request.gridSize >= 9 ? 768 : request.gridSize >= 7 ? 1024 : 1536;
        int pivotArmLimit = request.gridSize >= 9 ? 192 : request.gridSize >= 7 ? 256 : 384;
        int searchStateCandidateLimit = request.maxCandidatesPerSearchState > 0
            ? ClampInt(request.maxCandidatesPerSearchState, 32, 192)
            : request.gridSize >= 11 ? 64 : 96;

        return new GenerationContext
        {
            BoardWidth = request.gridSize,
            BoardHeight = request.gridSize,
            RequestedMinArrowCount = requestedMinArrowCount,
            RequestedMaxArrowCount = requestedMaxArrowCount,
            EffectiveMinArrowCount = effectiveMinArrowCount,
            EffectiveMaxArrowCount = effectiveMaxArrowCount,
            ArrowCount = effectiveMaxArrowCount,
            MinArrowLength = request.minArrowLength,
            MaxArrowLength = request.maxArrowLength,
            Lives = request.lives,
            Seed = seed,
            Random = new System.Random(seed),
            OccupiedCells = occupiedCells,
            MaxSearchFrames = requestedIterations,
            MaxGenerationSeconds = requestedSeconds <= 0f ? 0f : requestedSeconds,
            MaxCandidatesPerPivotCell = pivotCandidateLimit,
            MaxCandidatesPerSearchState = searchStateCandidateLimit,
            MaxPivotArms = pivotArmLimit,
            Stopwatch = new System.Diagnostics.Stopwatch()
        };
    }

    private static List<int> BuildArrowCountAttempts(GenerationContext context)
    {
        List<int> attempts = new List<int>();
        for (int count = context.EffectiveMinArrowCount; count <= context.EffectiveMaxArrowCount; count++)
        {
            attempts.Add(count);
        }

        float preferredLength = (context.MinArrowLength + context.MaxArrowLength) * 0.5f;
        float preferredCount = context.OccupiedCells.Count / Mathf.Max(1f, preferredLength);
        Shuffle(attempts, context.Random);
        attempts.Sort((a, b) =>
        {
            float aDistance = Mathf.Abs(a - preferredCount);
            float bDistance = Mathf.Abs(b - preferredCount);
            int distanceCompare = aDistance.CompareTo(bDistance);
            return distanceCompare != 0 ? distanceCompare : a.CompareTo(b);
        });

        return attempts;
    }

    private static bool RunInitialFeasibilityChecks(GenerationContext context)
    {
        int totalCells = context.OccupiedCells.Count;
        if (totalCells < context.ArrowCount * context.MinArrowLength)
        {
            return context.Fail("cell count is too small for arrow count and minimum arrow length");
        }

        if (totalCells > context.ArrowCount * context.MaxArrowLength)
        {
            return context.Fail("cell count is too large for arrow count and maximum arrow length");
        }

        if (RemainingFeasibilityFails(context, context.OccupiedCells, context.ArrowCount))
        {
            return context.Fail("connected component feasibility check failed");
        }

        return true;
    }

    private static List<LengthWindow> BuildCandidateLengthWindows(GenerationContext context)
    {
        List<LengthWindow> windows = new List<LengthWindow>();
        if (context.ArrowCount <= 0)
        {
            windows.Add(new LengthWindow(context.MinArrowLength, context.MaxArrowLength));
            return windows;
        }

        int totalCells = context.OccupiedCells.Count;
        int globalMaxLength = Math.Min(context.MaxArrowLength, totalCells - Math.Max(0, context.ArrowCount - 1) * context.MinArrowLength);
        globalMaxLength = Math.Max(context.MinArrowLength, globalMaxLength);

        int averageLength = CeilDiv(totalCells, context.ArrowCount);
        int firstMin = ClampInt(averageLength - 2, context.MinArrowLength, globalMaxLength);
        int firstMax = ClampInt(averageLength + 2, context.MinArrowLength, globalMaxLength);
        int secondMin = ClampInt(averageLength - 4, context.MinArrowLength, globalMaxLength);
        int secondMax = ClampInt(averageLength + 4, context.MinArrowLength, globalMaxLength);

        AddUniqueWindow(windows, firstMin, firstMax);
        AddUniqueWindow(windows, secondMin, secondMax);
        AddUniqueWindow(windows, context.MinArrowLength, globalMaxLength);
        return windows;
    }

    private static void AddUniqueWindow(List<LengthWindow> windows, int minLength, int maxLength)
    {
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].MinLength == minLength && windows[i].MaxLength == maxLength)
            {
                return;
            }
        }

        windows.Add(new LengthWindow(minLength, maxLength));
    }

    private static List<CandidateArrow> GenerateAllCandidateArrows(GenerationContext context)
    {
        List<CandidateArrow> candidates = new List<CandidateArrow>();
        HashSet<string> candidateKeys = new HashSet<string>();
        List<Cell> startCells = ToSortedCells(context.OccupiedCells);

        for (int i = 0; i < startCells.Count; i++)
        {
            if (context.ShouldStop())
            {
                break;
            }

            List<Cell> path = new List<Cell> { startCells[i] };
            HashSet<Cell> pathSet = new HashSet<Cell> { startCells[i] };
            int startCandidateCount = 0;
            DFSGeneratePath(context, path, pathSet, candidates, candidateKeys, ref startCandidateCount);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            candidates[i].Id = i + 1;
        }

        return candidates;
    }

    private static void DFSGeneratePath(
        GenerationContext context,
        List<Cell> path,
        HashSet<Cell> pathSet,
        List<CandidateArrow> candidates,
        HashSet<string> candidateKeys,
        ref int startCandidateCount)
    {
        if (context.ShouldStop() || HasReachedCandidateGenerationLimit(context, candidates, startCandidateCount))
        {
            return;
        }

        if (path.Count >= context.ActiveMinArrowLength && path.Count <= context.ActiveMaxArrowLength)
        {
            if (AddCandidateFromPath(context, path, candidates, candidateKeys))
            {
                startCandidateCount++;
            }
        }

        if (path.Count >= context.ActiveMaxArrowLength)
        {
            return;
        }

        Cell currentCell = path[path.Count - 1];
        List<Cell> neighbors = GetNeighbors(currentCell);
        Shuffle(neighbors, context.Random);
        for (int i = 0; i < neighbors.Count; i++)
        {
            if (context.ShouldStop() || HasReachedCandidateGenerationLimit(context, candidates, startCandidateCount))
            {
                return;
            }

            Cell neighbor = neighbors[i];
            if (!context.OccupiedCells.Contains(neighbor) || pathSet.Contains(neighbor))
            {
                continue;
            }

            path.Add(neighbor);
            pathSet.Add(neighbor);

            DFSGeneratePath(context, path, pathSet, candidates, candidateKeys, ref startCandidateCount);

            pathSet.Remove(neighbor);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool AddCandidateFromPath(
        GenerationContext context,
        List<Cell> path,
        List<CandidateArrow> candidates,
        HashSet<string> candidateKeys)
    {
        if (path.Count == 1)
        {
            return false;
        }

        Cell previousCell = path[path.Count - 2];
        Cell tipCell = path[path.Count - 1];
        ArrowDirection direction = DirectionFromDelta(tipCell - previousCell);
        if (direction == ArrowDirection.None)
        {
            return false;
        }

        return TryAddCandidate(context, path, direction, candidates, candidateKeys);
    }

    private static bool TryAddCandidate(
        GenerationContext context,
        List<Cell> path,
        ArrowDirection direction,
        List<CandidateArrow> candidates,
        HashSet<string> candidateKeys)
    {
        string candidateKey = BuildCandidateKey(path, direction);
        if (!candidateKeys.Add(candidateKey))
        {
            return false;
        }

        CandidateArrow candidate = new CandidateArrow
        {
            Id = context.NextCandidateId++,
            PathCells = new List<Cell>(path),
            CellSet = new HashSet<Cell>(path),
            TipCell = path[path.Count - 1],
            Direction = direction,
            Length = path.Count
        };

        if (DoesArrowSelfBlock(candidate, context.BoardWidth, context.BoardHeight))
        {
            return false;
        }

        candidates.Add(candidate);
        context.GeneratedCandidateCount++;
        return true;
    }

    private static bool HasReachedCandidateGenerationLimit(GenerationContext context, List<CandidateArrow> candidates, int startCandidateCount)
    {
        return false;
    }

    private static Dictionary<Cell, List<CandidateArrow>> BuildCandidatesByCell(GenerationContext context, List<CandidateArrow> candidates)
    {
        Dictionary<Cell, List<CandidateArrow>> candidatesByCell = new Dictionary<Cell, List<CandidateArrow>>();
        foreach (Cell occupiedCell in context.OccupiedCells)
        {
            candidatesByCell[occupiedCell] = new List<CandidateArrow>();
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CandidateArrow candidate = candidates[i];
            for (int cellIndex = 0; cellIndex < candidate.PathCells.Count; cellIndex++)
            {
                Cell cell = candidate.PathCells[cellIndex];
                candidatesByCell[cell].Add(candidate);
            }
        }

        return candidatesByCell;
    }

    private static List<CandidateArrow> GetCandidatesForCell(GenerationContext context, Cell pivotCell)
    {
        if (context.LazyCandidatesByCell == null)
        {
            context.LazyCandidatesByCell = new Dictionary<Cell, List<CandidateArrow>>();
        }

        if (context.LazyCandidatesByCell.TryGetValue(pivotCell, out List<CandidateArrow> cachedCandidates))
        {
            return cachedCandidates;
        }

        List<CandidateArrow> candidates = GenerateCandidatesContainingCell(context, pivotCell);
        context.LazyCandidatesByCell[pivotCell] = candidates;
        return candidates;
    }

    private static List<CandidateArrow> GenerateCandidatesContainingCell(GenerationContext context, Cell pivotCell)
    {
        List<CandidateArrow> candidates = new List<CandidateArrow>();
        HashSet<string> candidateKeys = new HashSet<string>();
        List<List<Cell>> arms = GeneratePivotArms(context, pivotCell);

        Shuffle(arms, context.Random);
        for (int tailArmIndex = 0; tailArmIndex < arms.Count; tailArmIndex++)
        {
            if (context.ShouldStop() || HasReachedPivotCandidateLimit(context, candidates))
            {
                break;
            }

            List<Cell> tailArm = arms[tailArmIndex];
            for (int tipArmIndex = 0; tipArmIndex < arms.Count; tipArmIndex++)
            {
                if (context.ShouldStop() || HasReachedPivotCandidateLimit(context, candidates))
                {
                    break;
                }

                List<Cell> tipArm = arms[tipArmIndex];
                int combinedLength = tailArm.Count + tipArm.Count - 1;
                if (combinedLength < context.ActiveMinArrowLength || combinedLength > context.ActiveMaxArrowLength)
                {
                    continue;
                }

                if (!ArmsOnlyOverlapAtPivot(tailArm, tipArm))
                {
                    continue;
                }

                List<Cell> path = CombineArmsIntoPath(tailArm, tipArm);
                AddCandidateFromPath(context, path, candidates, candidateKeys);
            }
        }

        SortCandidatesBySearchPreference(candidates);
        return candidates;
    }

    private static List<List<Cell>> GeneratePivotArms(GenerationContext context, Cell pivotCell)
    {
        List<List<Cell>> arms = new List<List<Cell>>();
        Queue<List<Cell>> queue = new Queue<List<Cell>>();
        queue.Enqueue(new List<Cell> { pivotCell });

        while (queue.Count > 0 && arms.Count < context.MaxPivotArms)
        {
            if (context.ShouldStop())
            {
                break;
            }

            List<Cell> path = queue.Dequeue();
            arms.Add(path);

            if (path.Count >= context.ActiveMaxArrowLength)
            {
                continue;
            }

            List<Cell> neighbors = GetNeighbors(path[path.Count - 1]);
            Shuffle(neighbors, context.Random);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Cell neighbor = neighbors[i];
                if (!context.OccupiedCells.Contains(neighbor) || path.Contains(neighbor))
                {
                    continue;
                }

                List<Cell> nextPath = new List<Cell>(path) { neighbor };
                queue.Enqueue(nextPath);
            }
        }

        return arms;
    }

    private static void DFSGeneratePivotArm(
        GenerationContext context,
        List<Cell> path,
        HashSet<Cell> pathSet,
        List<List<Cell>> arms)
    {
        if (context.ShouldStop() || arms.Count >= context.MaxPivotArms)
        {
            return;
        }

        arms.Add(new List<Cell>(path));

        if (path.Count >= context.ActiveMaxArrowLength)
        {
            return;
        }

        List<Cell> neighbors = GetNeighbors(path[path.Count - 1]);
        Shuffle(neighbors, context.Random);
        for (int i = 0; i < neighbors.Count; i++)
        {
            if (context.ShouldStop() || arms.Count >= context.MaxPivotArms)
            {
                return;
            }

            Cell neighbor = neighbors[i];
            if (!context.OccupiedCells.Contains(neighbor) || pathSet.Contains(neighbor))
            {
                continue;
            }

            path.Add(neighbor);
            pathSet.Add(neighbor);

            DFSGeneratePivotArm(context, path, pathSet, arms);

            pathSet.Remove(neighbor);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool ArmsOnlyOverlapAtPivot(List<Cell> tailArm, List<Cell> tipArm)
    {
        HashSet<Cell> tailCells = new HashSet<Cell>(tailArm);
        for (int i = 1; i < tipArm.Count; i++)
        {
            if (tailCells.Contains(tipArm[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static List<Cell> CombineArmsIntoPath(List<Cell> tailArm, List<Cell> tipArm)
    {
        List<Cell> path = new List<Cell>();
        for (int i = tailArm.Count - 1; i >= 0; i--)
        {
            path.Add(tailArm[i]);
        }

        for (int i = 1; i < tipArm.Count; i++)
        {
            path.Add(tipArm[i]);
        }

        return path;
    }

    private static bool HasReachedPivotCandidateLimit(GenerationContext context, List<CandidateArrow> candidates)
    {
        return context.MaxCandidatesPerPivotCell > 0 && candidates.Count >= context.MaxCandidatesPerPivotCell;
    }

    private static int ChooseAnytimeAttemptArrowCount(GenerationContext context, int attemptCount)
    {
        int minimumForFullCapacity = CeilDiv(context.OccupiedCells.Count, context.MaxArrowLength);
        int lowerBound = ClampInt(
            Math.Max(context.EffectiveMinArrowCount, minimumForFullCapacity - 2),
            1,
            context.EffectiveMaxArrowCount);

        if (attemptCount % 5 == 0)
        {
            return context.Random.Next(Math.Max(1, context.EffectiveMinArrowCount), context.EffectiveMaxArrowCount + 1);
        }

        if (attemptCount % 3 == 0)
        {
            return context.Random.Next(lowerBound, context.EffectiveMaxArrowCount + 1);
        }

        int highBiasWindow = Math.Max(1, Math.Min(5, context.EffectiveMaxArrowCount - lowerBound + 1));
        return context.EffectiveMaxArrowCount - context.Random.Next(highBiasWindow);
    }

    private static void RunAnytimeBeamAttempt(GenerationContext context, int targetArrowCount)
    {
        if (targetArrowCount <= 0 || context.ShouldStop())
        {
            return;
        }

        int beamWidth = GetAnytimeBeamWidth(context);
        List<SearchState> beam = new List<SearchState>
        {
            new SearchState
            {
                SelectedArrows = new List<CandidateArrow>(),
                UsedCells = new HashSet<Cell>(),
                SkippedCells = new HashSet<Cell>(),
                Graph = new DependencyGraph(),
                Score = 0f
            }
        };

        for (int depth = 0; depth < targetArrowCount && !context.ShouldStop(); depth++)
        {
            List<SearchState> nextBeam = new List<SearchState>();
            for (int stateIndex = 0; stateIndex < beam.Count && !context.ShouldStop(); stateIndex++)
            {
                context.SearchFrames++;
                AddAnytimeTransitions(context, beam[stateIndex], targetArrowCount, nextBeam);
            }

            if (nextBeam.Count == 0)
            {
                break;
            }

            SelectBestBeamStates(context, nextBeam, beamWidth);
            beam = nextBeam;

            for (int stateIndex = 0; stateIndex < beam.Count; stateIndex++)
            {
                ConsiderBestPartial(context, beam[stateIndex]);
            }

            if (context.BestPartialCoveredCellCount >= context.OccupiedCells.Count)
            {
                return;
            }
        }
    }

    private static int GetAnytimeBeamWidth(GenerationContext context)
    {
        if (context.BoardWidth >= 13)
        {
            return 14;
        }

        if (context.BoardWidth >= 10)
        {
            return 18;
        }

        if (context.BoardWidth >= 8)
        {
            return 24;
        }

        return 32;
    }

    private static void AddAnytimeTransitions(
        GenerationContext context,
        SearchState state,
        int targetArrowCount,
        List<SearchState> nextBeam)
    {
        if (state.SelectedArrows.Count >= targetArrowCount ||
            state.SelectedArrows.Count >= context.EffectiveMaxArrowCount)
        {
            return;
        }

        List<CandidateArrow> candidatePool = BuildAnytimeCandidatePool(context, state);
        for (int i = 0; i < candidatePool.Count && !context.ShouldStop(); i++)
        {
            CandidateArrow candidate = candidatePool[i];
            context.CandidateChecks++;

            if (CandidateOverlapsUsedCells(candidate, state.UsedCells))
            {
                continue;
            }

            if (!TryBuildGraphWithCandidate(candidate, state.SelectedArrows, state.Graph, context, out DependencyGraph nextGraph))
            {
                continue;
            }

            HashSet<Cell> nextUsedCells = new HashSet<Cell>(state.UsedCells);
            for (int cellIndex = 0; cellIndex < candidate.PathCells.Count; cellIndex++)
            {
                nextUsedCells.Add(candidate.PathCells[cellIndex]);
            }

            SearchState nextState = new SearchState
            {
                SelectedArrows = new List<CandidateArrow>(state.SelectedArrows) { candidate },
                UsedCells = nextUsedCells,
                SkippedCells = new HashSet<Cell>(),
                Graph = nextGraph
            };

            nextState.Score = ScoreAnytimeState(context, nextState);
            ConsiderBestPartial(context, nextState);
            nextBeam.Add(nextState);
        }
    }

    private static List<CandidateArrow> BuildAnytimeCandidatePool(GenerationContext context, SearchState state)
    {
        HashSet<Cell> remainingCells = GetRemainingCells(context.OccupiedCells, state.UsedCells);
        List<CandidateArrow> candidates = new List<CandidateArrow>();
        if (remainingCells.Count < context.ActiveMinArrowLength)
        {
            return candidates;
        }

        HashSet<string> candidateKeys = new HashSet<string>();
        List<Cell> remainingList = new List<Cell>(remainingCells);
        Shuffle(remainingList, context.Random);

        int randomCandidateTarget = Math.Max(12, context.MaxCandidatesPerSearchState);
        int randomTryLimit = randomCandidateTarget * 4;
        for (int i = 0; i < randomTryLimit && candidates.Count < randomCandidateTarget && !context.ShouldStop(); i++)
        {
            Cell startCell = remainingList[context.Random.Next(remainingList.Count)];
            TryAddRandomWalkCandidate(context, remainingCells, startCell, candidates, candidateKeys);
        }

        int pivotCandidateTarget = Math.Max(8, context.MaxCandidatesPerSearchState / 3);
        int pivotLimit = Math.Min(remainingList.Count, 6);
        for (int pivotIndex = 0; pivotIndex < pivotLimit && candidates.Count < context.MaxCandidatesPerSearchState + pivotCandidateTarget && !context.ShouldStop(); pivotIndex++)
        {
            List<CandidateArrow> pivotCandidates = GetCandidatesForCell(context, remainingList[pivotIndex]);
            if (pivotCandidates == null || pivotCandidates.Count == 0)
            {
                continue;
            }

            int addedFromPivot = 0;
            for (int candidateIndex = 0; candidateIndex < pivotCandidates.Count &&
                 addedFromPivot < pivotCandidateTarget &&
                 candidates.Count < context.MaxCandidatesPerSearchState + pivotCandidateTarget; candidateIndex++)
            {
                CandidateArrow candidate = pivotCandidates[candidateIndex];
                if (CandidateOverlapsUsedCells(candidate, state.UsedCells))
                {
                    continue;
                }

                string candidateKey = BuildCandidateKey(candidate.PathCells, candidate.Direction);
                if (!candidateKeys.Add(candidateKey))
                {
                    continue;
                }

                candidates.Add(candidate);
                addedFromPivot++;
            }
        }

        SortAnytimeCandidatePool(context, state, candidates);
        if (candidates.Count > context.MaxCandidatesPerSearchState)
        {
            candidates.RemoveRange(context.MaxCandidatesPerSearchState, candidates.Count - context.MaxCandidatesPerSearchState);
        }

        return candidates;
    }

    private static bool TryAddRandomWalkCandidate(
        GenerationContext context,
        HashSet<Cell> remainingCells,
        Cell startCell,
        List<CandidateArrow> candidates,
        HashSet<string> candidateKeys)
    {
        int maxLength = Math.Min(context.ActiveMaxArrowLength, remainingCells.Count);
        int minLength = Math.Min(context.ActiveMinArrowLength, maxLength);
        if (minLength < 2 || !remainingCells.Contains(startCell))
        {
            return false;
        }

        int lengthSpan = Math.Max(1, maxLength - minLength + 1);
        int targetLength = context.Random.NextDouble() < 0.75
            ? maxLength - context.Random.Next(Math.Min(lengthSpan, 4))
            : context.Random.Next(minLength, maxLength + 1);
        targetLength = ClampInt(targetLength, minLength, maxLength);

        List<Cell> path = new List<Cell> { startCell };
        HashSet<Cell> pathSet = new HashSet<Cell> { startCell };
        Cell previousDirection = new Cell(0, 0);

        while (path.Count < targetLength)
        {
            Cell currentCell = path[path.Count - 1];
            List<Cell> neighbors = GetNeighbors(currentCell);
            List<Cell> availableNeighbors = new List<Cell>();
            for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
            {
                Cell neighbor = neighbors[neighborIndex];
                if (remainingCells.Contains(neighbor) && !pathSet.Contains(neighbor))
                {
                    availableNeighbors.Add(neighbor);
                }
            }

            if (availableNeighbors.Count == 0)
            {
                break;
            }

            Cell nextCell = ChooseRandomWalkNextCell(context, currentCell, previousDirection, availableNeighbors, remainingCells, path.Count);
            previousDirection = nextCell - currentCell;
            path.Add(nextCell);
            pathSet.Add(nextCell);
        }

        if (path.Count < minLength)
        {
            return false;
        }

        return AddCandidateFromPath(context, path, candidates, candidateKeys);
    }

    private static Cell ChooseRandomWalkNextCell(
        GenerationContext context,
        Cell currentCell,
        Cell previousDirection,
        List<Cell> availableNeighbors,
        HashSet<Cell> remainingCells,
        int pathLength)
    {
        List<Cell> weightedNeighbors = new List<Cell>();
        for (int i = 0; i < availableNeighbors.Count; i++)
        {
            Cell neighbor = availableNeighbors[i];
            Cell direction = neighbor - currentCell;
            int weight = 3;

            if (direction == previousDirection)
            {
                weight += 3;
            }
            else if (previousDirection.X != 0 || previousDirection.Y != 0)
            {
                weight += pathLength < 3 ? 1 : 4;
            }

            int onwardCount = CountAvailableOnwardNeighbors(neighbor, currentCell, remainingCells);
            weight += onwardCount;

            for (int copyIndex = 0; copyIndex < weight; copyIndex++)
            {
                weightedNeighbors.Add(neighbor);
            }
        }

        return weightedNeighbors[context.Random.Next(weightedNeighbors.Count)];
    }

    private static int CountAvailableOnwardNeighbors(Cell cell, Cell previousCell, HashSet<Cell> remainingCells)
    {
        int count = 0;
        List<Cell> neighbors = GetNeighbors(cell);
        for (int i = 0; i < neighbors.Count; i++)
        {
            Cell neighbor = neighbors[i];
            if (neighbor != previousCell && remainingCells.Contains(neighbor))
            {
                count++;
            }
        }

        return count;
    }

    private static void SortAnytimeCandidatePool(GenerationContext context, SearchState state, List<CandidateArrow> candidates)
    {
        candidates.Sort((a, b) =>
        {
            float aScore = ScoreAnytimeCandidate(context, state, a);
            float bScore = ScoreAnytimeCandidate(context, state, b);
            int scoreCompare = bScore.CompareTo(aScore);
            if (scoreCompare != 0)
            {
                return scoreCompare;
            }

            return b.Length.CompareTo(a.Length);
        });
    }

    private static float ScoreAnytimeCandidate(GenerationContext context, SearchState state, CandidateArrow candidate)
    {
        int turnCount = CountTurns(candidate);
        int exitDistance = CountCellsToBoardExit(candidate.TipCell, candidate.Direction, context.BoardWidth, context.BoardHeight);
        return candidate.Length * 100f + turnCount * 6f - exitDistance * 0.5f;
    }

    private static float ScoreAnytimeState(GenerationContext context, SearchState state)
    {
        int turnCount = 0;
        for (int i = 0; i < state.SelectedArrows.Count; i++)
        {
            turnCount += CountTurns(state.SelectedArrows[i]);
        }

        int remainingCount = context.OccupiedCells.Count - state.UsedCells.Count;
        float noise = (float)context.Random.NextDouble() * 20f;
        return state.UsedCells.Count * 1000f +
               state.SelectedArrows.Count * 8f +
               turnCount * 2f -
               remainingCount * 0.1f +
               noise;
    }

    private static void SelectBestBeamStates(GenerationContext context, List<SearchState> states, int beamWidth)
    {
        states.Sort((a, b) => b.Score.CompareTo(a.Score));
        HashSet<string> usedSignatures = new HashSet<string>();
        for (int i = 0; i < states.Count; i++)
        {
            string signature = BuildUsedCellSignature(states[i].UsedCells);
            if (!usedSignatures.Add(signature))
            {
                states.RemoveAt(i);
                i--;
            }
        }

        if (states.Count > beamWidth)
        {
            states.RemoveRange(beamWidth, states.Count - beamWidth);
        }
    }

    private static string BuildUsedCellSignature(HashSet<Cell> cells)
    {
        List<Cell> sortedCells = ToSortedCells(cells);
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < sortedCells.Count; i++)
        {
            builder.Append(sortedCells[i].X);
            builder.Append(',');
            builder.Append(sortedCells[i].Y);
            builder.Append(';');
        }

        return builder.ToString();
    }

    private static int CountCellsToBoardExit(Cell tipCell, ArrowDirection direction, int boardWidth, int boardHeight)
    {
        int count = 0;
        Cell directionVector = DirectionToVector(direction);
        Cell cursor = tipCell + directionVector;
        while (IsInsideBoard(cursor, boardWidth, boardHeight))
        {
            count++;
            cursor += directionVector;
        }

        return count;
    }

    private static bool Search(
        GenerationContext context,
        SearchState state,
        out List<CandidateArrow> selectedArrows,
        out DependencyGraph selectedGraph)
    {
        selectedArrows = null;
        selectedGraph = null;

        ConsiderBestPartial(context, state);

        if (context.ShouldStop())
        {
            return false;
        }

        context.SearchFrames++;

        if (state.SelectedArrows.Count == context.ArrowCount)
        {
            if (state.UsedCells.Count == context.OccupiedCells.Count && state.SkippedCells.Count == 0 && state.Graph.IsAcyclic())
            {
                selectedArrows = new List<CandidateArrow>(state.SelectedArrows);
                selectedGraph = state.Graph.Clone();
                return true;
            }

            return false;
        }

        HashSet<Cell> remainingCells = GetRemainingCells(context.OccupiedCells, state.UsedCells, state.SkippedCells);
        int remainingArrowCount = context.ArrowCount - state.SelectedArrows.Count;
        if (state.SelectedArrows.Count + remainingArrowCount < context.RequestedMinArrowCount)
        {
            return false;
        }

        if (remainingCells.Count == 0)
        {
            return false;
        }

        if (context.BestPartialCoveredCellCount > 0 &&
            state.UsedCells.Count + Math.Min(remainingCells.Count, remainingArrowCount * context.ActiveMaxArrowLength) <= context.BestPartialCoveredCellCount)
        {
            return false;
        }

        ChooseMostConstrainedCell(context, state, remainingCells, out Cell pivotCell, out List<CandidateArrow> pivotCandidates);

        if (pivotCandidates != null && pivotCandidates.Count > 0)
        {
            Shuffle(pivotCandidates, context.Random);
            SortCandidatesBySearchPreference(pivotCandidates);
        }

        for (int i = 0; pivotCandidates != null && i < pivotCandidates.Count; i++)
        {
            if (context.ShouldStop())
            {
                return false;
            }

            CandidateArrow candidate = pivotCandidates[i];
            context.CandidateChecks++;

            if (CandidateOverlapsUsedCells(candidate, state.UsedCells))
            {
                continue;
            }

            if (CandidateOverlapsUsedCells(candidate, state.SkippedCells))
            {
                continue;
            }

            if (state.SelectedArrows.Count + 1 > context.ArrowCount)
            {
                continue;
            }

            if (!TryBuildGraphWithCandidate(candidate, state.SelectedArrows, state.Graph, context, out DependencyGraph nextGraph))
            {
                continue;
            }

            HashSet<Cell> nextUsedCells = new HashSet<Cell>(state.UsedCells);
            for (int cellIndex = 0; cellIndex < candidate.PathCells.Count; cellIndex++)
            {
                nextUsedCells.Add(candidate.PathCells[cellIndex]);
            }

            int nextRemainingArrowCount = context.ArrowCount - state.SelectedArrows.Count - 1;
            HashSet<Cell> nextRemainingCells = GetRemainingCells(context.OccupiedCells, nextUsedCells, state.SkippedCells);
            if (context.BestPartialCoveredCellCount > 0 &&
                nextUsedCells.Count + Math.Min(nextRemainingCells.Count, nextRemainingArrowCount * context.ActiveMaxArrowLength) <= context.BestPartialCoveredCellCount)
            {
                continue;
            }

            SearchState nextState = new SearchState
            {
                SelectedArrows = new List<CandidateArrow>(state.SelectedArrows) { candidate },
                UsedCells = nextUsedCells,
                SkippedCells = new HashSet<Cell>(state.SkippedCells),
                Graph = nextGraph
            };

            if (Search(context, nextState, out selectedArrows, out selectedGraph))
            {
                return true;
            }
        }

        SearchState skippedState = new SearchState
        {
            SelectedArrows = new List<CandidateArrow>(state.SelectedArrows),
            UsedCells = new HashSet<Cell>(state.UsedCells),
            SkippedCells = new HashSet<Cell>(state.SkippedCells) { pivotCell },
            Graph = state.Graph.Clone()
        };

        return Search(context, skippedState, out selectedArrows, out selectedGraph);
    }

    private static bool ChooseMostConstrainedCell(
        GenerationContext context,
        SearchState state,
        HashSet<Cell> remainingCells,
        out Cell pivotCell,
        out List<CandidateArrow> pivotCandidates)
    {
        pivotCell = ChooseCheapPivotCell(context, remainingCells);
        pivotCandidates = null;

        if (!remainingCells.Contains(pivotCell))
        {
            return false;
        }

        List<CandidateArrow> cellCandidates = GetCandidatesForCell(context, pivotCell);
        if (cellCandidates == null || cellCandidates.Count == 0)
        {
            return false;
        }

        pivotCandidates = new List<CandidateArrow>();
        for (int i = 0; i < cellCandidates.Count; i++)
        {
            if (context.ShouldStop())
            {
                return false;
            }

            CandidateArrow candidate = cellCandidates[i];
            if (CandidateOverlapsUsedCells(candidate, state.UsedCells))
            {
                continue;
            }

            pivotCandidates.Add(candidate);
        }

        return pivotCandidates.Count > 0;
    }

    private static Cell ChooseCheapPivotCell(GenerationContext context, HashSet<Cell> remainingCells)
    {
        Cell bestCell = FirstCell(remainingCells);
        int bestScore = int.MaxValue;

        foreach (Cell cell in remainingCells)
        {
            int freeNeighborCount = 0;
            List<Cell> neighbors = GetNeighbors(cell);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (remainingCells.Contains(neighbors[i]))
                {
                    freeNeighborCount++;
                }
            }

            int cachedCount = context.LazyCandidatesByCell != null && context.LazyCandidatesByCell.TryGetValue(cell, out List<CandidateArrow> cachedCandidates)
                ? cachedCandidates.Count
                : 1000;
            int score = freeNeighborCount * 10000 + cachedCount;
            if (score < bestScore)
            {
                bestScore = score;
                bestCell = cell;
            }
        }

        return bestCell;
    }

    private static bool TryBuildGraphWithCandidate(
        CandidateArrow candidate,
        List<CandidateArrow> selectedArrows,
        DependencyGraph currentGraph,
        GenerationContext context,
        out DependencyGraph nextGraph)
    {
        nextGraph = currentGraph.Clone();
        nextGraph.AddNode(candidate.Id);

        for (int i = 0; i < selectedArrows.Count; i++)
        {
            CandidateArrow existingArrow = selectedArrows[i];

            if (DoesArrowBlockArrow(candidate, existingArrow, context.BoardWidth, context.BoardHeight))
            {
                nextGraph.AddEdge(candidate.Id, existingArrow.Id);
            }

            if (DoesArrowBlockArrow(existingArrow, candidate, context.BoardWidth, context.BoardHeight))
            {
                nextGraph.AddEdge(existingArrow.Id, candidate.Id);
            }
        }

        return nextGraph.IsAcyclic();
    }

    private static bool RemainingFeasibilityFails(GenerationContext context, HashSet<Cell> remainingCells, int remainingArrowCount)
    {
        if (remainingArrowCount == 0)
        {
            return remainingCells.Count != 0;
        }

        int remainingCellCount = remainingCells.Count;
        if (remainingCellCount < remainingArrowCount * context.MinArrowLength)
        {
            return true;
        }

        int minLength = context.ActiveMinArrowLength > 0 ? context.ActiveMinArrowLength : context.MinArrowLength;
        int maxLength = context.ActiveMaxArrowLength > 0 ? context.ActiveMaxArrowLength : context.MaxArrowLength;

        if (remainingCellCount < remainingArrowCount * minLength)
        {
            return true;
        }

        if (remainingCellCount > remainingArrowCount * maxLength)
        {
            return true;
        }

        List<List<Cell>> components = GetConnectedComponents(remainingCells);
        int minimumNeeded = 0;
        int maximumPossible = 0;

        for (int i = 0; i < components.Count; i++)
        {
            int componentSize = components[i].Count;
            int componentMinimum = CeilDiv(componentSize, maxLength);
            int componentMaximum = componentSize / minLength;

            if (componentMinimum > componentMaximum)
            {
                return true;
            }

            minimumNeeded += componentMinimum;
            maximumPossible += componentMaximum;
        }

        return minimumNeeded > remainingArrowCount || maximumPossible < remainingArrowCount;
    }

    private static List<List<Cell>> GetConnectedComponents(HashSet<Cell> cells)
    {
        List<List<Cell>> components = new List<List<Cell>>();
        HashSet<Cell> unvisited = new HashSet<Cell>(cells);
        Queue<Cell> queue = new Queue<Cell>();

        while (unvisited.Count > 0)
        {
            Cell start = FirstCell(unvisited);
            unvisited.Remove(start);
            queue.Enqueue(start);

            List<Cell> component = new List<Cell>();
            component.Add(start);

            while (queue.Count > 0)
            {
                Cell current = queue.Dequeue();
                List<Cell> neighbors = GetNeighbors(current);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    Cell neighbor = neighbors[i];
                    if (!unvisited.Remove(neighbor))
                    {
                        continue;
                    }

                    queue.Enqueue(neighbor);
                    component.Add(neighbor);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static bool ValidateFinalLevel(
        GenerationContext context,
        LevelData levelData,
        bool requireExactCover,
        out List<DependencyData> dependencies,
        out List<int> solutionOrder)
    {
        dependencies = null;
        solutionOrder = null;

        if (levelData == null ||
            levelData.arrows == null ||
            levelData.arrows.Count == 0 ||
            levelData.arrows.Count > context.RequestedMaxArrowCount)
        {
            return false;
        }

        if (requireExactCover && levelData.arrows.Count < context.RequestedMinArrowCount)
        {
            return false;
        }

        Dictionary<int, RuntimeArrow> arrowsById = new Dictionary<int, RuntimeArrow>();
        HashSet<Cell> coveredCells = new HashSet<Cell>();

        for (int i = 0; i < levelData.arrows.Count; i++)
        {
            ArrowData arrowData = levelData.arrows[i];
            if (!TryCreateRuntimeArrow(context, arrowData, out RuntimeArrow arrow))
            {
                return false;
            }

            if (arrowsById.ContainsKey(arrow.Id))
            {
                return false;
            }

            arrowsById.Add(arrow.Id, arrow);

            for (int cellIndex = 0; cellIndex < arrow.PathCells.Count; cellIndex++)
            {
                Cell cell = arrow.PathCells[cellIndex];
                if (!context.OccupiedCells.Contains(cell) || !coveredCells.Add(cell))
                {
                    return false;
                }
            }

            if (DoesArrowSelfBlock(arrow, context.BoardWidth, context.BoardHeight))
            {
                return false;
            }
        }

        if (requireExactCover && coveredCells.Count != context.OccupiedCells.Count)
        {
            return false;
        }

        DependencyGraph graph = new DependencyGraph();
        foreach (int arrowId in arrowsById.Keys)
        {
            graph.AddNode(arrowId);
        }

        foreach (RuntimeArrow blockedArrow in arrowsById.Values)
        {
            foreach (RuntimeArrow blockerArrow in arrowsById.Values)
            {
                if (blockerArrow.Id == blockedArrow.Id)
                {
                    continue;
                }

                if (DoesArrowBlockArrow(blockerArrow, blockedArrow, context.BoardWidth, context.BoardHeight))
                {
                    graph.AddEdge(blockerArrow.Id, blockedArrow.Id);
                }
            }
        }

        if (!graph.IsAcyclic())
        {
            return false;
        }

        if (!SimulateRemoval(graph))
        {
            return false;
        }

        dependencies = graph.ToDependencyData();
        solutionOrder = graph.TopologicalSort();
        return solutionOrder.Count == levelData.arrows.Count;
    }

    private static bool TryCreateRuntimeArrow(GenerationContext context, ArrowData arrowData, out RuntimeArrow arrow)
    {
        arrow = null;
        if (arrowData == null ||
            arrowData.occupiedCells == null ||
            arrowData.occupiedCells.Count < context.MinArrowLength ||
            arrowData.occupiedCells.Count > context.MaxArrowLength ||
            arrowData.tipCell == null ||
            arrowData.tipDirection == ArrowDirection.None)
        {
            return false;
        }

        List<Cell> pathCells = new List<Cell>();
        HashSet<Cell> pathSet = new HashSet<Cell>();
        for (int i = 0; i < arrowData.occupiedCells.Count; i++)
        {
            GridPositionData gridCell = arrowData.occupiedCells[i];
            if (gridCell == null)
            {
                return false;
            }

            Cell cell = new Cell(gridCell.x, gridCell.y);
            if (!IsInsideBoard(cell, context.BoardWidth, context.BoardHeight) || !pathSet.Add(cell))
            {
                return false;
            }

            if (i > 0 && ManhattanDistance(pathCells[i - 1], cell) != 1)
            {
                return false;
            }

            pathCells.Add(cell);
        }

        Cell tipCell = new Cell(arrowData.tipCell.x, arrowData.tipCell.y);
        if (pathCells[pathCells.Count - 1] != tipCell)
        {
            return false;
        }

        if (pathCells.Count < 2)
        {
            return false;
        }

        ArrowDirection inferredDirection = DirectionFromDelta(pathCells[pathCells.Count - 1] - pathCells[pathCells.Count - 2]);
        if (inferredDirection != arrowData.tipDirection)
        {
            return false;
        }

        arrow = new RuntimeArrow
        {
            Id = arrowData.arrowId,
            PathCells = pathCells,
            CellSet = pathSet,
            TipCell = tipCell,
            Direction = arrowData.tipDirection
        };
        return true;
    }

    private static bool DoesArrowSelfBlock(CandidateArrow arrow, int boardWidth, int boardHeight)
    {
        if (arrow == null || arrow.PathCells == null || arrow.PathCells.Count <= 1)
        {
            return false;
        }

        HashSet<Cell> bodyCells = new HashSet<Cell>();
        for (int i = 0; i < arrow.PathCells.Count - 1; i++)
        {
            bodyCells.Add(arrow.PathCells[i]);
        }

        return ExitRayIntersectsCells(arrow.TipCell, arrow.Direction, bodyCells, boardWidth, boardHeight);
    }

    private static bool DoesArrowSelfBlock(RuntimeArrow arrow, int boardWidth, int boardHeight)
    {
        if (arrow == null || arrow.PathCells == null || arrow.PathCells.Count <= 1)
        {
            return false;
        }

        HashSet<Cell> bodyCells = new HashSet<Cell>();
        for (int i = 0; i < arrow.PathCells.Count - 1; i++)
        {
            bodyCells.Add(arrow.PathCells[i]);
        }

        return ExitRayIntersectsCells(arrow.TipCell, arrow.Direction, bodyCells, boardWidth, boardHeight);
    }

    private static bool DoesArrowBlockArrow(CandidateArrow blocker, CandidateArrow blocked, int boardWidth, int boardHeight)
    {
        return blocker != null &&
               blocked != null &&
               ExitRayIntersectsCells(blocked.TipCell, blocked.Direction, blocker.CellSet, boardWidth, boardHeight);
    }

    private static bool DoesArrowBlockArrow(RuntimeArrow blocker, RuntimeArrow blocked, int boardWidth, int boardHeight)
    {
        return blocker != null &&
               blocked != null &&
               ExitRayIntersectsCells(blocked.TipCell, blocked.Direction, blocker.CellSet, boardWidth, boardHeight);
    }

    private static bool ExitRayIntersectsCells(Cell tipCell, ArrowDirection direction, HashSet<Cell> cells, int boardWidth, int boardHeight)
    {
        Cell directionVector = DirectionToVector(direction);
        Cell cursor = tipCell + directionVector;

        while (IsInsideBoard(cursor, boardWidth, boardHeight))
        {
            if (cells.Contains(cursor))
            {
                return true;
            }

            cursor += directionVector;
        }

        return false;
    }

    private static bool CandidateOverlapsUsedCells(CandidateArrow candidate, HashSet<Cell> usedCells)
    {
        for (int i = 0; i < candidate.PathCells.Count; i++)
        {
            if (usedCells.Contains(candidate.PathCells[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void ConsiderBestPartial(GenerationContext context, SearchState state)
    {
        if (state == null ||
            state.SelectedArrows == null ||
            state.SelectedArrows.Count == 0 ||
            state.SelectedArrows.Count > context.RequestedMaxArrowCount ||
            state.UsedCells == null ||
            state.UsedCells.Count <= context.BestPartialCoveredCellCount)
        {
            return;
        }

        context.BestPartialCoveredCellCount = state.UsedCells.Count;
        context.BestPartialArrows = new List<CandidateArrow>(state.SelectedArrows);
        context.BestPartialGraph = state.Graph?.Clone();
    }

    private static LevelData BuildBestPartialOrFail(GenerationContext context)
    {
        if (context.BestPartialArrows != null && context.BestPartialArrows.Count > 0)
        {
            LevelData levelData = BuildLevelData(context, context.BestPartialArrows);
            if (!ValidateFinalLevel(context, levelData, false, out List<DependencyData> dependencies, out List<int> solutionOrder))
            {
                context.Fail("best partial result was rejected by the final validator");
                LogFailure(context);
                return null;
            }

            levelData.dependencies = dependencies;
            levelData.solutionOrder = solutionOrder;
            levelData.notes = $"Generated by partial-exact-cover anytime beam generator. Seed: {context.Seed}. Covered cells: {context.BestPartialCoveredCellCount}/{context.OccupiedCells.Count}. Arrow count: {context.BestPartialArrows.Count}. Attempt count: {context.AnytimeAttemptCount}. Candidate length window: {context.ActiveMinArrowLength}-{context.ActiveMaxArrowLength}. Generated candidates: {context.GeneratedCandidateCount}. Search frames: {context.SearchFrames}. Candidate checks: {context.CandidateChecks}.";
            Debug.Log($"Partial-exact-cover anytime generator returned level: {context.BestPartialCoveredCellCount}/{context.OccupiedCells.Count} cells covered across {context.AnytimeAttemptCount} attempt(s).");
            return levelData;
        }

        if (string.IsNullOrWhiteSpace(context.FailureReason))
        {
            context.Fail("no exact cover or partial solvable arrangement found under current search limits");
        }

        LogFailure(context);
        return null;
    }

    private static HashSet<Cell> GetRemainingCells(HashSet<Cell> occupiedCells, HashSet<Cell> usedCells)
    {
        HashSet<Cell> remainingCells = new HashSet<Cell>(occupiedCells);
        remainingCells.ExceptWith(usedCells);
        return remainingCells;
    }

    private static HashSet<Cell> GetRemainingCells(HashSet<Cell> occupiedCells, HashSet<Cell> usedCells, HashSet<Cell> skippedCells)
    {
        HashSet<Cell> remainingCells = new HashSet<Cell>(occupiedCells);
        remainingCells.ExceptWith(usedCells);
        if (skippedCells != null)
        {
            remainingCells.ExceptWith(skippedCells);
        }

        return remainingCells;
    }

    private static LevelData BuildLevelData(GenerationContext context, List<CandidateArrow> selectedArrows)
    {
        HashSet<Cell> coveredCells = new HashSet<Cell>();
        if (selectedArrows != null)
        {
            for (int arrowIndex = 0; arrowIndex < selectedArrows.Count; arrowIndex++)
            {
                CandidateArrow arrow = selectedArrows[arrowIndex];
                if (arrow?.PathCells == null)
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < arrow.PathCells.Count; cellIndex++)
                {
                    coveredCells.Add(arrow.PathCells[cellIndex]);
                }
            }
        }

        LevelData levelData = new LevelData
        {
            gridSize = context.BoardWidth,
            width = context.BoardWidth,
            height = context.BoardHeight,
            targetArrowCount = selectedArrows?.Count ?? 0,
            minArrowCount = context.RequestedMinArrowCount,
            maxArrowCount = context.RequestedMaxArrowCount,
            minArrowLength = context.MinArrowLength,
            maxArrowLength = context.MaxArrowLength,
            lives = context.Lives,
            arrows = new List<ArrowData>(),
            shapeCells = ToGridPositions(ToSortedCells(coveredCells)),
            dependencies = new List<DependencyData>(),
            solutionOrder = new List<int>(),
            createdAt = DateTime.UtcNow.ToString("o")
        };

        for (int i = 0; i < selectedArrows.Count; i++)
        {
            CandidateArrow candidate = selectedArrows[i];
            int arrowId = i + 1;

            levelData.arrows.Add(new ArrowData
            {
                arrowId = arrowId,
                length = candidate.PathCells.Count,
                occupiedCells = ToGridPositions(candidate.PathCells),
                tipCell = new GridPositionData(candidate.TipCell.X, candidate.TipCell.Y),
                tipDirection = candidate.Direction,
                isSolved = false
            });
        }

        return levelData;
    }

    private static bool SimulateRemoval(DependencyGraph graph)
    {
        return graph.TopologicalSort().Count == graph.NodeCount;
    }

    private static void LogFailure(GenerationContext context)
    {
        string reason = string.IsNullOrWhiteSpace(context.FailureReason)
            ? "unknown failure"
            : context.FailureReason;

        Debug.LogWarning($"Partial-exact-cover level generation failed: {reason}. Best partial coverage: {context.BestPartialCoveredCellCount}/{context.OccupiedCells.Count}. Tried arrow count range: {context.EffectiveMinArrowCount}-{context.EffectiveMaxArrowCount}. Attempt count: {context.AnytimeAttemptCount}. Search frames: {context.SearchFrames}. Generated candidates: {context.GeneratedCandidateCount}. Candidate checks: {context.CandidateChecks}. Length window: {context.ActiveMinArrowLength}-{context.ActiveMaxArrowLength}. Elapsed: {context.Stopwatch.Elapsed.TotalSeconds:0.00}s.");
    }

    private static List<Cell> ToSortedCells(HashSet<Cell> cells)
    {
        List<Cell> sortedCells = new List<Cell>(cells);
        sortedCells.Sort(CompareCells);
        return sortedCells;
    }

    private static List<GridPositionData> ToGridPositions(List<Cell> cells)
    {
        List<GridPositionData> positions = new List<GridPositionData>();
        for (int i = 0; i < cells.Count; i++)
        {
            positions.Add(new GridPositionData(cells[i].X, cells[i].Y));
        }

        return positions;
    }

    private static string BuildCandidateKey(List<Cell> path, ArrowDirection direction)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < path.Count; i++)
        {
            builder.Append(path[i].X);
            builder.Append(',');
            builder.Append(path[i].Y);
            builder.Append(';');
        }

        builder.Append(direction);
        return builder.ToString();
    }

    private static List<Cell> GetNeighbors(Cell cell)
    {
        return new List<Cell>
        {
            new Cell(cell.X + 1, cell.Y),
            new Cell(cell.X - 1, cell.Y),
            new Cell(cell.X, cell.Y + 1),
            new Cell(cell.X, cell.Y - 1)
        };
    }

    private static Cell FirstCell(HashSet<Cell> cells)
    {
        foreach (Cell cell in cells)
        {
            return cell;
        }

        return new Cell(0, 0);
    }

    private static bool IsInsideBoard(Cell cell, int boardWidth, int boardHeight)
    {
        return cell.X >= 0 && cell.X < boardWidth && cell.Y >= 0 && cell.Y < boardHeight;
    }

    private static int ManhattanDistance(Cell a, Cell b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static int CeilDiv(int value, int divisor)
    {
        return divisor <= 0 ? 0 : (value + divisor - 1) / divisor;
    }

    private static int ClampInt(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static int CompareCells(Cell a, Cell b)
    {
        int yCompare = a.Y.CompareTo(b.Y);
        return yCompare != 0 ? yCompare : a.X.CompareTo(b.X);
    }

    private static ArrowDirection DirectionFromDelta(Cell delta)
    {
        if (delta.X == 1 && delta.Y == 0)
        {
            return ArrowDirection.Right;
        }

        if (delta.X == -1 && delta.Y == 0)
        {
            return ArrowDirection.Left;
        }

        if (delta.X == 0 && delta.Y == 1)
        {
            return ArrowDirection.Up;
        }

        if (delta.X == 0 && delta.Y == -1)
        {
            return ArrowDirection.Down;
        }

        return ArrowDirection.None;
    }

    private static Cell DirectionToVector(ArrowDirection direction)
    {
        switch (direction)
        {
            case ArrowDirection.Up:
                return new Cell(0, 1);
            case ArrowDirection.Down:
                return new Cell(0, -1);
            case ArrowDirection.Left:
                return new Cell(-1, 0);
            case ArrowDirection.Right:
                return new Cell(1, 0);
            default:
                return new Cell(0, 0);
        }
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

    private static void SortCandidatesBySearchPreference(List<CandidateArrow> candidates)
    {
        candidates.Sort((a, b) =>
        {
            int lengthCompare = b.Length.CompareTo(a.Length);
            if (lengthCompare != 0)
            {
                return lengthCompare;
            }

            return CountTurns(b).CompareTo(CountTurns(a));
        });
    }

    private static int CountTurns(CandidateArrow candidate)
    {
        int turns = 0;
        for (int i = 2; i < candidate.PathCells.Count; i++)
        {
            Cell previousDelta = candidate.PathCells[i - 1] - candidate.PathCells[i - 2];
            Cell currentDelta = candidate.PathCells[i] - candidate.PathCells[i - 1];
            if (previousDelta.X != currentDelta.X || previousDelta.Y != currentDelta.Y)
            {
                turns++;
            }
        }

        return turns;
    }

    private class GenerationContext
    {
        public int BoardWidth;
        public int BoardHeight;
        public int RequestedMinArrowCount;
        public int RequestedMaxArrowCount;
        public int EffectiveMinArrowCount;
        public int EffectiveMaxArrowCount;
        public int ArrowCount;
        public int MinArrowLength;
        public int MaxArrowLength;
        public int Lives;
        public int Seed;
        public int MaxSearchFrames;
        public int MaxCandidatesPerPivotCell;
        public int MaxCandidatesPerSearchState;
        public int MaxPivotArms;
        public float MaxGenerationSeconds;
        public int ActiveMinArrowLength;
        public int ActiveMaxArrowLength;
        public int GeneratedCandidateCount;
        public int NextCandidateId;
        public int SearchFrames;
        public int CandidateChecks;
        public int BestPartialCoveredCellCount;
        public int AnytimeAttemptCount;
        public string FailureReason;
        public HashSet<Cell> OccupiedCells;
        public Dictionary<Cell, List<CandidateArrow>> CandidatesByCell;
        public Dictionary<Cell, List<CandidateArrow>> LazyCandidatesByCell;
        public List<CandidateArrow> BestPartialArrows;
        public DependencyGraph BestPartialGraph;
        public System.Random Random;
        public System.Diagnostics.Stopwatch Stopwatch;

        public bool ShouldStop()
        {
            if (MaxSearchFrames > 0 && SearchFrames >= MaxSearchFrames)
            {
                FailureReason = "search limit reached";
                return true;
            }

            if (MaxGenerationSeconds > 0f && Stopwatch.Elapsed.TotalSeconds >= MaxGenerationSeconds)
            {
                FailureReason = "generation time limit reached";
                return true;
            }

            return false;
        }

        public bool Fail(string reason)
        {
            FailureReason = reason;
            return false;
        }
    }

    private class SearchState
    {
        public List<CandidateArrow> SelectedArrows;
        public HashSet<Cell> UsedCells;
        public HashSet<Cell> SkippedCells;
        public DependencyGraph Graph;
        public float Score;
    }

    private class CandidateArrow
    {
        public int Id;
        public List<Cell> PathCells;
        public HashSet<Cell> CellSet;
        public Cell TipCell;
        public ArrowDirection Direction;
        public int Length;
    }

    private struct LengthWindow
    {
        public readonly int MinLength;
        public readonly int MaxLength;

        public LengthWindow(int minLength, int maxLength)
        {
            MinLength = minLength;
            MaxLength = maxLength;
        }
    }

    private class RuntimeArrow
    {
        public int Id;
        public List<Cell> PathCells;
        public HashSet<Cell> CellSet;
        public Cell TipCell;
        public ArrowDirection Direction;
    }

    private class DependencyGraph
    {
        private readonly HashSet<int> nodes = new HashSet<int>();
        private readonly Dictionary<int, HashSet<int>> outgoingByNode = new Dictionary<int, HashSet<int>>();

        public int NodeCount => nodes.Count;

        public void AddNode(int node)
        {
            nodes.Add(node);
            if (!outgoingByNode.ContainsKey(node))
            {
                outgoingByNode[node] = new HashSet<int>();
            }
        }

        public void AddEdge(int from, int to)
        {
            AddNode(from);
            AddNode(to);
            outgoingByNode[from].Add(to);
        }

        public bool IsAcyclic()
        {
            return TopologicalSort().Count == nodes.Count;
        }

        public List<int> TopologicalSort()
        {
            Dictionary<int, int> indegreeByNode = new Dictionary<int, int>();
            foreach (int node in nodes)
            {
                indegreeByNode[node] = 0;
            }

            foreach (KeyValuePair<int, HashSet<int>> entry in outgoingByNode)
            {
                foreach (int target in entry.Value)
                {
                    if (!indegreeByNode.ContainsKey(target))
                    {
                        indegreeByNode[target] = 0;
                    }

                    indegreeByNode[target]++;
                }
            }

            List<int> ready = new List<int>();
            foreach (KeyValuePair<int, int> entry in indegreeByNode)
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

                if (!outgoingByNode.TryGetValue(node, out HashSet<int> outgoing))
                {
                    continue;
                }

                foreach (int target in outgoing)
                {
                    indegreeByNode[target]--;
                    if (indegreeByNode[target] == 0)
                    {
                        ready.Add(target);
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
                clone.AddNode(node);
            }

            foreach (KeyValuePair<int, HashSet<int>> entry in outgoingByNode)
            {
                foreach (int target in entry.Value)
                {
                    clone.AddEdge(entry.Key, target);
                }
            }

            return clone;
        }

        public List<DependencyData> ToDependencyData()
        {
            List<DependencyData> dependencies = new List<DependencyData>();
            foreach (KeyValuePair<int, HashSet<int>> entry in outgoingByNode)
            {
                foreach (int target in entry.Value)
                {
                    dependencies.Add(new DependencyData(entry.Key, target));
                }
            }

            dependencies.Sort((a, b) =>
            {
                int blockerCompare = a.blockerArrowId.CompareTo(b.blockerArrowId);
                return blockerCompare != 0 ? blockerCompare : a.blockedArrowId.CompareTo(b.blockedArrowId);
            });

            return dependencies;
        }
    }

    private struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;

        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Cell other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is Cell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        public static Cell operator +(Cell a, Cell b)
        {
            return new Cell(a.X + b.X, a.Y + b.Y);
        }

        public static Cell operator -(Cell a, Cell b)
        {
            return new Cell(a.X - b.X, a.Y - b.Y);
        }

        public static bool operator ==(Cell a, Cell b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Cell a, Cell b)
        {
            return !a.Equals(b);
        }
    }
}
