using System;
using System.Collections.Generic;
using UnityEngine;

public static class PartialCoverLevelGeneratorAlgorithm
{
    public static LevelData Generate(EditorGenerationRequest request)
    {
        GenerationContext context = BuildContext(request);
        if (context == null)
        {
            return null;
        }

        context.Stopwatch.Start();

        List<int> arrowCountAttempts = BuildArrowCountAttempts(context);
        List<LengthWindow> candidateLengthWindows = BuildCandidateLengthWindows(context);
        int maxGreedyAttempts = Mathf.Clamp(context.OccupiedCells.Count / 2, 24, 128);

        for (int arrowCountIndex = 0; arrowCountIndex < arrowCountAttempts.Count; arrowCountIndex++)
        {
            if (context.ShouldStop())
            {
                break;
            }

            context.ArrowCount = arrowCountAttempts[arrowCountIndex];

            for (int windowIndex = 0; windowIndex < candidateLengthWindows.Count; windowIndex++)
            {
                if (context.ShouldStop())
                {
                    break;
                }

                LengthWindow lengthWindow = candidateLengthWindows[windowIndex];
                context.ActiveMinArrowLength = lengthWindow.MinLength;
                context.ActiveMaxArrowLength = lengthWindow.MaxLength;
                context.LazyCandidatesByCell = new Dictionary<Cell, List<CandidateArrow>>();

                int serpentineAttemptCount = Mathf.Clamp(context.OccupiedCells.Count / 24, 4, 12);
                for (int attempt = 0; attempt < serpentineAttemptCount && !context.ShouldStop(); attempt++)
                {
                    if (TryBuildSerpentineCover(context, out SearchResult serpentineResult))
                    {
                        return BuildAndValidateLevel(context, serpentineResult);
                    }
                }

                for (int attempt = 0; attempt < maxGreedyAttempts && !context.ShouldStop(); attempt++)
                {
                    context.SearchFrames++;
                    SearchResult greedyResult = BuildRandomGreedyCover(context);
                    if (greedyResult != null)
                    {
                        return BuildAndValidateLevel(context, greedyResult);
                    }
                }
            }
        }

        context.Stopwatch.Stop();
        if (string.IsNullOrWhiteSpace(context.FailureReason))
        {
            context.Fail($"no partial cover reached the requested {context.TargetCoveragePercent * 100f:0.#}% coverage");
        }

        LogFailure(context);
        return null;
    }

    private static GenerationContext BuildContext(EditorGenerationRequest request)
    {
        if (request == null)
        {
            Debug.LogWarning("Partial-cover generation failed: request is null.");
            return null;
        }

        if (request.gridSize <= 0)
        {
            Debug.LogWarning("Partial-cover generation failed: board size must be positive.");
            return null;
        }

        if (request.minArrowLength <= 0 || request.minArrowLength > request.maxArrowLength)
        {
            Debug.LogWarning("Partial-cover generation failed: arrow length range is invalid.");
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
                    Debug.LogWarning($"Partial-cover generation failed: painted cell ({cell.x}, {cell.y}) is outside the board.");
                    return null;
                }

                occupiedCells.Add(occupiedCell);
            }
        }

        if (occupiedCells.Count == 0)
        {
            Debug.LogWarning("Partial-cover generation failed: no painted cells.");
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
            Debug.LogWarning("Partial-cover generation failed: min arrow count cannot exceed max arrow count.");
            return null;
        }

        float targetCoveragePercent = NormalizeCoveragePercent(request.partialCoverTargetPercent);
        int requiredCoveredCellCount = Mathf.CeilToInt(occupiedCells.Count * targetCoveragePercent);

        int coverageMinimumArrowCount = CeilDiv(requiredCoveredCellCount, request.maxArrowLength);
        int lengthMaximumArrowCount = occupiedCells.Count / request.minArrowLength;
        int effectiveMinArrowCount = Math.Max(requestedMinArrowCount, coverageMinimumArrowCount);
        int effectiveMaxArrowCount = Math.Min(requestedMaxArrowCount, lengthMaximumArrowCount);

        if (effectiveMinArrowCount > effectiveMaxArrowCount)
        {
            Debug.LogWarning($"Partial-cover generation failed: cannot cover {requiredCoveredCellCount}/{occupiedCells.Count} painted cells with arrow count range {requestedMinArrowCount}-{requestedMaxArrowCount} and length range {request.minArrowLength}-{request.maxArrowLength}.");
            return null;
        }

        int seed = request.useRandomSeed ? request.randomSeed : Environment.TickCount;
        int requestedCandidateLimit = Mathf.Max(0, request.maxCandidatesPerSearchState);
        int adaptiveCandidateLimit = request.gridSize >= 12 ? 192 : request.gridSize >= 9 ? 256 : request.gridSize >= 7 ? 384 : 512;
        int adaptiveArmLimit = request.gridSize >= 12 ? 64 : request.gridSize >= 9 ? 96 : request.gridSize >= 7 ? 128 : 192;
        float requestedSeconds = Mathf.Max(0f, request.maxGenerationSeconds);
        float adaptiveSeconds = request.gridSize >= 9 || requestedMaxArrowCount >= 12 ? 20f : 10f;

        return new GenerationContext
        {
            BoardWidth = request.gridSize,
            BoardHeight = request.gridSize,
            RequestedMinArrowCount = requestedMinArrowCount,
            RequestedMaxArrowCount = requestedMaxArrowCount,
            EffectiveMinArrowCount = effectiveMinArrowCount,
            EffectiveMaxArrowCount = effectiveMaxArrowCount,
            MinArrowLength = request.minArrowLength,
            MaxArrowLength = request.maxArrowLength,
            Lives = request.lives,
            Seed = seed,
            Random = new System.Random(seed),
            OccupiedCells = occupiedCells,
            RequiredCoveredCellCount = requiredCoveredCellCount,
            MaxSkippedCellCount = occupiedCells.Count - requiredCoveredCellCount,
            TargetCoveragePercent = targetCoveragePercent,
            MaxSearchFrames = Mathf.Max(0, request.maxSearchIterations),
            MaxGenerationSeconds = requestedSeconds <= 0f ? adaptiveSeconds : requestedSeconds,
            MaxCandidatesPerPivotCell = requestedCandidateLimit > 0 ? Mathf.Min(requestedCandidateLimit, adaptiveCandidateLimit) : adaptiveCandidateLimit,
            MaxPivotArms = adaptiveArmLimit,
            NextCandidateId = 1,
            Stopwatch = new System.Diagnostics.Stopwatch()
        };
    }

    private static float NormalizeCoveragePercent(float value)
    {
        if (value <= 0f)
        {
            return 0.85f;
        }

        if (value > 1f)
        {
            value *= 0.01f;
        }

        return Mathf.Clamp(value, 0.01f, 1f);
    }

    private static List<int> BuildArrowCountAttempts(GenerationContext context)
    {
        List<int> attempts = new List<int>();
        for (int count = context.EffectiveMinArrowCount; count <= context.EffectiveMaxArrowCount; count++)
        {
            attempts.Add(count);
        }

        float preferredLength = (context.MinArrowLength + context.MaxArrowLength) * 0.5f;
        float preferredCount = context.RequiredCoveredCellCount / Mathf.Max(1f, preferredLength);
        Shuffle(attempts, context.Random);
        attempts.Sort((a, b) =>
        {
            float aDistance = Mathf.Abs(a - preferredCount);
            float bDistance = Mathf.Abs(b - preferredCount);
            int distanceCompare = aDistance.CompareTo(bDistance);
            return distanceCompare != 0 ? distanceCompare : b.CompareTo(a);
        });

        return attempts;
    }

    private static List<LengthWindow> BuildCandidateLengthWindows(GenerationContext context)
    {
        List<LengthWindow> windows = new List<LengthWindow>();
        int averageLength = Mathf.Clamp(
            CeilDiv(context.RequiredCoveredCellCount, Mathf.Max(1, context.EffectiveMinArrowCount)),
            context.MinArrowLength,
            context.MaxArrowLength);

        AddUniqueWindow(windows, Mathf.Max(context.MinArrowLength, averageLength - 2), Mathf.Min(context.MaxArrowLength, averageLength + 3));
        AddUniqueWindow(windows, Mathf.Max(context.MinArrowLength, averageLength - 4), Mathf.Min(context.MaxArrowLength, averageLength + 5));
        AddUniqueWindow(windows, context.MinArrowLength, context.MaxArrowLength);
        return windows;
    }

    private static void AddUniqueWindow(List<LengthWindow> windows, int minLength, int maxLength)
    {
        if (minLength > maxLength)
        {
            return;
        }

        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].MinLength == minLength && windows[i].MaxLength == maxLength)
            {
                return;
            }
        }

        windows.Add(new LengthWindow(minLength, maxLength));
    }

    private static bool TryBuildSerpentineCover(GenerationContext context, out SearchResult result)
    {
        result = null;
        context.SearchFrames++;

        List<List<Cell>> sequences = BuildSerpentineSequences(context);
        if (sequences.Count == 0)
        {
            return false;
        }

        int baseChunkLength = Mathf.Clamp(
            CeilDiv(context.RequiredCoveredCellCount, Mathf.Max(1, context.ArrowCount)),
            context.ActiveMinArrowLength,
            context.ActiveMaxArrowLength);

        SearchState state = new SearchState
        {
            SelectedArrows = new List<CandidateArrow>(),
            UsedCells = new HashSet<Cell>(),
            SkippedPivotCells = new HashSet<Cell>(),
            Graph = new DependencyGraph()
        };

        for (int sequenceIndex = 0; sequenceIndex < sequences.Count; sequenceIndex++)
        {
            if (context.ShouldStop())
            {
                return false;
            }

            List<Cell> sequence = sequences[sequenceIndex];
            if (sequence.Count >= context.ActiveMinArrowLength * 2)
            {
                int maxOffset = Mathf.Min(context.ActiveMinArrowLength - 1, sequence.Count - context.ActiveMinArrowLength);
                int offset = maxOffset > 0 ? context.Random.Next(maxOffset + 1) : 0;
                if (offset > 0)
                {
                    sequence = sequence.GetRange(offset, sequence.Count - offset);
                }
            }

            int cursor = 0;
            while (cursor + context.ActiveMinArrowLength <= sequence.Count &&
                   state.SelectedArrows.Count < context.ArrowCount &&
                   (state.UsedCells.Count < context.RequiredCoveredCellCount ||
                    state.SelectedArrows.Count < context.RequestedMinArrowCount))
            {
                int remaining = sequence.Count - cursor;
                int lengthVariance = Mathf.Min(4, context.ActiveMaxArrowLength - context.ActiveMinArrowLength);
                int length = baseChunkLength;
                if (lengthVariance > 0)
                {
                    length += context.Random.Next(-lengthVariance, lengthVariance + 1);
                }

                length = Mathf.Clamp(length, context.ActiveMinArrowLength, context.ActiveMaxArrowLength);
                length = Mathf.Min(length, remaining);
                if (remaining - length > 0 && remaining - length < context.ActiveMinArrowLength)
                {
                    length = Mathf.Min(context.ActiveMaxArrowLength, remaining);
                }

                if (length < context.ActiveMinArrowLength)
                {
                    break;
                }

                List<Cell> path = sequence.GetRange(cursor, length);
                if (context.Random.Next(2) == 0)
                {
                    path.Reverse();
                }

                if (TryCreateCandidateFromPath(context, path, out CandidateArrow candidate) &&
                    !CandidateOverlapsUsedCells(candidate, state.UsedCells) &&
                    TryBuildGraphWithCandidate(candidate, state.SelectedArrows, state.Graph, context, out DependencyGraph nextGraph))
                {
                    state.Graph = nextGraph;
                    state.SelectedArrows.Add(candidate);
                    for (int i = 0; i < candidate.PathCells.Count; i++)
                    {
                        state.UsedCells.Add(candidate.PathCells[i]);
                    }

                    TrackBestProgress(context, state);
                    if (HasEnoughCoverage(context, state))
                    {
                        result = CreateSearchResult(state);
                        return true;
                    }
                }

                cursor += length;
            }
        }

        return false;
    }

    private static SearchResult BuildRandomGreedyCover(GenerationContext context)
    {
        SearchState state = new SearchState
        {
            SelectedArrows = new List<CandidateArrow>(),
            UsedCells = new HashSet<Cell>(),
            SkippedPivotCells = new HashSet<Cell>(),
            Graph = new DependencyGraph()
        };

        int failedSteps = 0;
        int maxFailedSteps = Mathf.Max(12, context.RequestedMaxArrowCount * 2);
        while (!context.ShouldStop() &&
               state.SelectedArrows.Count < context.ArrowCount &&
               failedSteps < maxFailedSteps &&
               (state.UsedCells.Count < context.RequiredCoveredCellCount ||
                state.SelectedArrows.Count < context.RequestedMinArrowCount))
        {
            if (!TryChooseGreedyCandidate(context, state, out CandidateArrow candidate, out DependencyGraph nextGraph))
            {
                failedSteps++;
                continue;
            }

            state.Graph = nextGraph;
            state.SelectedArrows.Add(candidate);
            for (int i = 0; i < candidate.PathCells.Count; i++)
            {
                state.UsedCells.Add(candidate.PathCells[i]);
            }

            TrackBestProgress(context, state);
            failedSteps = 0;
        }

        return HasEnoughCoverage(context, state) ? CreateSearchResult(state) : null;
    }

    private static bool TryChooseGreedyCandidate(
        GenerationContext context,
        SearchState state,
        out CandidateArrow bestCandidate,
        out DependencyGraph bestGraph)
    {
        bestCandidate = null;
        bestGraph = null;
        int bestScore = int.MinValue;
        int sampleCount = Mathf.Clamp(context.OccupiedCells.Count, 48, 192);

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            if (context.ShouldStop())
            {
                return false;
            }

            if (!TryGenerateRandomCandidate(context, state.UsedCells, out CandidateArrow candidate))
            {
                continue;
            }

            context.CandidateChecks++;
            if (!TryBuildGraphWithCandidate(candidate, state.SelectedArrows, state.Graph, context, out DependencyGraph nextGraph))
            {
                continue;
            }

            int score = candidate.Length * 100 - CountTurns(candidate) * 4 + context.Random.Next(11);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestCandidate = candidate;
            bestGraph = nextGraph;
        }

        return bestCandidate != null;
    }

    private static bool TryGenerateRandomCandidate(
        GenerationContext context,
        HashSet<Cell> usedCells,
        out CandidateArrow candidate)
    {
        candidate = null;
        int availableCount = context.OccupiedCells.Count - usedCells.Count;
        if (availableCount < context.ActiveMinArrowLength)
        {
            return false;
        }

        int pathAttempts = 8;
        for (int attempt = 0; attempt < pathAttempts; attempt++)
        {
            if (!TryChooseRandomAvailableCell(context, usedCells, out Cell startCell))
            {
                return false;
            }

            int maximumLength = Mathf.Min(context.ActiveMaxArrowLength, availableCount);
            int targetLength = Mathf.Clamp(
                maximumLength - context.Random.Next(0, Mathf.Min(5, maximumLength - context.ActiveMinArrowLength + 1)),
                context.ActiveMinArrowLength,
                maximumLength);

            List<Cell> path = new List<Cell> { startCell };
            HashSet<Cell> pathSet = new HashSet<Cell> { startCell };

            while (path.Count < targetLength)
            {
                List<Cell> neighbors = GetAvailablePathNeighbors(context, path[path.Count - 1], usedCells, pathSet);
                if (neighbors.Count == 0)
                {
                    break;
                }

                SortNeighborsByRandomWalkPreference(context, neighbors, usedCells, pathSet);
                Cell nextCell = neighbors[0];
                path.Add(nextCell);
                pathSet.Add(nextCell);
            }

            if (path.Count < context.ActiveMinArrowLength)
            {
                continue;
            }

            if (TryCreateCandidateFromPath(context, path, out candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateCandidateFromPath(
        GenerationContext context,
        List<Cell> path,
        out CandidateArrow candidate)
    {
        candidate = null;
        if (path == null || path.Count < context.ActiveMinArrowLength || path.Count > context.ActiveMaxArrowLength)
        {
            return false;
        }

        if (TryCreateCandidateFromOrderedPath(context, path, out candidate))
        {
            return true;
        }

        List<Cell> reversedPath = new List<Cell>(path);
        reversedPath.Reverse();
        return TryCreateCandidateFromOrderedPath(context, reversedPath, out candidate);
    }

    private static bool TryCreateCandidateFromOrderedPath(
        GenerationContext context,
        List<Cell> path,
        out CandidateArrow candidate)
    {
        candidate = null;
        Cell previousCell = path[path.Count - 2];
        Cell tipCell = path[path.Count - 1];
        ArrowDirection direction = DirectionFromDelta(tipCell - previousCell);
        if (direction == ArrowDirection.None)
        {
            return false;
        }

        CandidateArrow builtCandidate = new CandidateArrow
        {
            Id = context.NextCandidateId++,
            PathCells = new List<Cell>(path),
            CellSet = new HashSet<Cell>(path),
            TipCell = tipCell,
            Direction = direction,
            Length = path.Count
        };

        if (DoesArrowSelfBlock(builtCandidate, context.BoardWidth, context.BoardHeight))
        {
            return false;
        }

        context.GeneratedCandidateCount++;
        candidate = builtCandidate;
        return true;
    }

    private static bool HasEnoughCoverage(GenerationContext context, SearchState state)
    {
        return state.UsedCells.Count >= context.RequiredCoveredCellCount &&
               state.SelectedArrows.Count >= context.RequestedMinArrowCount &&
               state.SelectedArrows.Count <= context.RequestedMaxArrowCount;
    }

    private static List<List<Cell>> BuildSerpentineSequences(GenerationContext context)
    {
        List<List<Cell>> sequences = new List<List<Cell>>();
        bool verticalFirst = context.Random.Next(2) == 0;
        AddSerpentineSequences(context, sequences, verticalFirst);
        AddSerpentineSequences(context, sequences, !verticalFirst);
        Shuffle(sequences, context.Random);
        sequences.Sort((a, b) =>
        {
            int bucketCompare = (b.Count / Mathf.Max(1, context.ActiveMinArrowLength))
                .CompareTo(a.Count / Mathf.Max(1, context.ActiveMinArrowLength));
            return bucketCompare != 0 ? bucketCompare : context.Random.Next(-1, 2);
        });
        return sequences;
    }

    private static void AddSerpentineSequences(
        GenerationContext context,
        List<List<Cell>> sequences,
        bool vertical)
    {
        List<Cell> currentSequence = new List<Cell>();
        int outerLimit = vertical ? context.BoardWidth : context.BoardHeight;
        int innerLimit = vertical ? context.BoardHeight : context.BoardWidth;
        int outerOffset = outerLimit > 0 ? context.Random.Next(outerLimit) : 0;
        bool flipEveryOtherLine = context.Random.Next(2) == 0;
        bool firstLineForward = context.Random.Next(2) == 0;

        for (int outerStep = 0; outerStep < outerLimit; outerStep++)
        {
            int outer = (outerStep + outerOffset) % outerLimit;
            bool forward = flipEveryOtherLine
                ? firstLineForward == (outerStep % 2 == 0)
                : firstLineForward;
            for (int step = 0; step < innerLimit; step++)
            {
                int inner = forward ? step : innerLimit - 1 - step;
                int x = vertical ? outer : inner;
                int y = vertical ? inner : outer;
                Cell cell = new Cell(x, y);
                if (!context.OccupiedCells.Contains(cell))
                {
                    continue;
                }

                if (currentSequence.Count == 0 ||
                    ManhattanDistance(currentSequence[currentSequence.Count - 1], cell) == 1)
                {
                    currentSequence.Add(cell);
                    continue;
                }

                if (currentSequence.Count > 0)
                {
                    sequences.Add(currentSequence);
                }

                currentSequence = new List<Cell> { cell };
            }
        }

        if (currentSequence.Count > 0)
        {
            sequences.Add(currentSequence);
        }
    }

    private static bool TryChooseRandomAvailableCell(
        GenerationContext context,
        HashSet<Cell> usedCells,
        out Cell chosenCell)
    {
        chosenCell = new Cell(0, 0);
        int availableCount = context.OccupiedCells.Count - usedCells.Count;
        if (availableCount <= 0)
        {
            return false;
        }

        int targetIndex = context.Random.Next(availableCount);
        int currentIndex = 0;
        foreach (Cell cell in context.OccupiedCells)
        {
            if (usedCells.Contains(cell))
            {
                continue;
            }

            if (currentIndex == targetIndex)
            {
                chosenCell = cell;
                return true;
            }

            currentIndex++;
        }

        return false;
    }

    private static List<Cell> GetAvailablePathNeighbors(
        GenerationContext context,
        Cell cell,
        HashSet<Cell> usedCells,
        HashSet<Cell> pathSet)
    {
        List<Cell> availableNeighbors = new List<Cell>();
        List<Cell> neighbors = GetNeighbors(cell);
        for (int i = 0; i < neighbors.Count; i++)
        {
            Cell neighbor = neighbors[i];
            if (context.OccupiedCells.Contains(neighbor) &&
                !usedCells.Contains(neighbor) &&
                !pathSet.Contains(neighbor))
            {
                availableNeighbors.Add(neighbor);
            }
        }

        return availableNeighbors;
    }

    private static void SortNeighborsByRandomWalkPreference(
        GenerationContext context,
        List<Cell> neighbors,
        HashSet<Cell> usedCells,
        HashSet<Cell> pathSet)
    {
        Shuffle(neighbors, context.Random);
        neighbors.Sort((a, b) =>
        {
            int onwardCompare = CountAvailableOnwardNeighbors(context, b, usedCells, pathSet)
                .CompareTo(CountAvailableOnwardNeighbors(context, a, usedCells, pathSet));
            if (onwardCompare != 0)
            {
                return onwardCompare;
            }

            bool aEdge = IsOnBoardEdge(a, context.BoardWidth, context.BoardHeight);
            bool bEdge = IsOnBoardEdge(b, context.BoardWidth, context.BoardHeight);
            return aEdge == bEdge ? 0 : aEdge ? -1 : 1;
        });
    }

    private static int CountAvailableOnwardNeighbors(
        GenerationContext context,
        Cell cell,
        HashSet<Cell> usedCells,
        HashSet<Cell> pathSet)
    {
        int count = 0;
        List<Cell> neighbors = GetNeighbors(cell);
        for (int i = 0; i < neighbors.Count; i++)
        {
            Cell neighbor = neighbors[i];
            if (context.OccupiedCells.Contains(neighbor) &&
                !usedCells.Contains(neighbor) &&
                !pathSet.Contains(neighbor))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Search(GenerationContext context, SearchState state, out SearchResult result)
    {
        result = null;
        if (context.ShouldStop())
        {
            return false;
        }

        context.SearchFrames++;
        TrackBestProgress(context, state);

        if (state.UsedCells.Count >= context.RequiredCoveredCellCount &&
            state.SelectedArrows.Count >= context.RequestedMinArrowCount &&
            state.SelectedArrows.Count <= context.RequestedMaxArrowCount)
        {
            result = CreateSearchResult(state);
            return true;
        }

        int remainingArrowSlots = context.ArrowCount - state.SelectedArrows.Count;
        if (remainingArrowSlots <= 0)
        {
            return false;
        }

        if (state.SelectedArrows.Count + remainingArrowSlots < context.RequestedMinArrowCount)
        {
            return false;
        }

        if (state.UsedCells.Count + remainingArrowSlots * context.ActiveMaxArrowLength < context.RequiredCoveredCellCount)
        {
            return false;
        }

        HashSet<Cell> remainingCells = GetRemainingPivotCells(context, state);
        if (remainingCells.Count == 0)
        {
            return false;
        }

        if (state.UsedCells.Count + remainingCells.Count < context.RequiredCoveredCellCount)
        {
            return false;
        }

        Cell pivotCell = ChoosePartialCoverPivotCell(context, remainingCells);
        List<CandidateArrow> candidates = GetCandidatesForCell(context, pivotCell);
        for (int i = 0; i < candidates.Count; i++)
        {
            if (context.ShouldStop())
            {
                return false;
            }

            CandidateArrow candidate = candidates[i];
            context.CandidateChecks++;

            if (CandidateOverlapsUsedCells(candidate, state.UsedCells) ||
                CandidateOverlapsUsedCells(candidate, state.SkippedPivotCells))
            {
                continue;
            }

            if (state.SelectedArrows.Count + 1 > context.RequestedMaxArrowCount)
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

            int nextRemainingArrowSlots = context.ArrowCount - state.SelectedArrows.Count - 1;
            if (nextUsedCells.Count + nextRemainingArrowSlots * context.ActiveMaxArrowLength < context.RequiredCoveredCellCount)
            {
                continue;
            }

            SearchState nextState = new SearchState
            {
                SelectedArrows = new List<CandidateArrow>(state.SelectedArrows) { candidate },
                UsedCells = nextUsedCells,
                SkippedPivotCells = new HashSet<Cell>(state.SkippedPivotCells),
                Graph = nextGraph
            };

            if (Search(context, nextState, out result))
            {
                return true;
            }
        }

        if (state.SkippedPivotCells.Count >= context.MaxSkippedCellCount)
        {
            return false;
        }

        if (state.UsedCells.Count + remainingCells.Count - 1 < context.RequiredCoveredCellCount)
        {
            return false;
        }

        SearchState skippedState = new SearchState
        {
            SelectedArrows = new List<CandidateArrow>(state.SelectedArrows),
            UsedCells = new HashSet<Cell>(state.UsedCells),
            SkippedPivotCells = new HashSet<Cell>(state.SkippedPivotCells) { pivotCell },
            Graph = state.Graph.Clone()
        };

        return Search(context, skippedState, out result);
    }

    private static SearchResult CreateSearchResult(SearchState state)
    {
        return new SearchResult
        {
            SelectedArrows = new List<CandidateArrow>(state.SelectedArrows),
            UsedCells = new HashSet<Cell>(state.UsedCells),
            Graph = state.Graph.Clone(),
            CoveredCellCount = state.UsedCells.Count,
            TurnCount = CountTotalTurns(state.SelectedArrows)
        };
    }

    private static void TrackBestProgress(GenerationContext context, SearchState state)
    {
        if (state.UsedCells.Count > context.BestCoveredCellCount)
        {
            context.BestCoveredCellCount = state.UsedCells.Count;
        }
    }

    private static HashSet<Cell> GetRemainingPivotCells(GenerationContext context, SearchState state)
    {
        HashSet<Cell> remaining = new HashSet<Cell>(context.OccupiedCells);
        remaining.ExceptWith(state.UsedCells);
        remaining.ExceptWith(state.SkippedPivotCells);
        return remaining;
    }

    private static Cell ChoosePartialCoverPivotCell(GenerationContext context, HashSet<Cell> remainingCells)
    {
        Cell bestCell = FirstCell(remainingCells);
        int bestScore = int.MaxValue;

        foreach (Cell cell in remainingCells)
        {
            int remainingNeighborCount = 0;
            List<Cell> neighbors = GetNeighbors(cell);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (remainingCells.Contains(neighbors[i]))
                {
                    remainingNeighborCount++;
                }
            }

            int edgeBonus = IsOnBoardEdge(cell, context.BoardWidth, context.BoardHeight) ? -1 : 0;
            int randomTieBreaker = context.Random.Next(7);
            int score = remainingNeighborCount * 100 + edgeBonus + randomTieBreaker;
            if (score < bestScore)
            {
                bestScore = score;
                bestCell = cell;
            }
        }

        return bestCell;
    }

    private static LevelData BuildAndValidateLevel(GenerationContext context, SearchResult result)
    {
        context.Stopwatch.Stop();

        LevelData levelData = BuildLevelData(context, result.SelectedArrows);
        if (!ValidateFinalLevel(context, levelData, out List<DependencyData> dependencies, out List<int> solutionOrder))
        {
            context.Fail("internal error: final independent validator rejected the partial-cover level");
            LogFailure(context);
            return null;
        }

        levelData.dependencies = dependencies;
        levelData.solutionOrder = solutionOrder;
        levelData.notes = $"Generated by fast partial-cover DAG generator. Seed: {context.Seed}. Coverage: {result.CoveredCellCount}/{context.OccupiedCells.Count} ({result.CoveredCellCount * 100f / context.OccupiedCells.Count:0.#}%). Target: {context.TargetCoveragePercent * 100f:0.#}%. Search frames: {context.SearchFrames}. Candidate checks: {context.CandidateChecks}. Generated candidates: {context.GeneratedCandidateCount}.";
        return levelData;
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

    private static bool AddCandidateFromPath(
        GenerationContext context,
        List<Cell> path,
        List<CandidateArrow> candidates,
        HashSet<string> candidateKeys)
    {
        if (path.Count < 2)
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
            TipCell = tipCell,
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

    private static bool ValidateFinalLevel(
        GenerationContext context,
        LevelData levelData,
        out List<DependencyData> dependencies,
        out List<int> solutionOrder)
    {
        dependencies = null;
        solutionOrder = null;

        if (levelData == null ||
            levelData.arrows == null ||
            levelData.arrows.Count < context.RequestedMinArrowCount ||
            levelData.arrows.Count > context.RequestedMaxArrowCount)
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

        if (coveredCells.Count < context.RequiredCoveredCellCount)
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

    private static LevelData BuildLevelData(GenerationContext context, List<CandidateArrow> selectedArrows)
    {
        HashSet<Cell> coveredCells = new HashSet<Cell>();
        LevelData levelData = new LevelData
        {
            gridSize = context.BoardWidth,
            width = context.BoardWidth,
            height = context.BoardHeight,
            targetArrowCount = selectedArrows.Count,
            minArrowCount = context.RequestedMinArrowCount,
            maxArrowCount = context.RequestedMaxArrowCount,
            minArrowLength = context.MinArrowLength,
            maxArrowLength = context.MaxArrowLength,
            lives = context.Lives,
            arrows = new List<ArrowData>(),
            dependencies = new List<DependencyData>(),
            solutionOrder = new List<int>(),
            createdAt = DateTime.UtcNow.ToString("o")
        };

        for (int i = 0; i < selectedArrows.Count; i++)
        {
            CandidateArrow candidate = selectedArrows[i];
            int arrowId = i + 1;

            for (int cellIndex = 0; cellIndex < candidate.PathCells.Count; cellIndex++)
            {
                coveredCells.Add(candidate.PathCells[cellIndex]);
            }

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

        levelData.shapeCells = ToGridPositions(ToSortedCells(coveredCells));
        return levelData;
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

    private static int CountTotalTurns(List<CandidateArrow> arrows)
    {
        int totalTurns = 0;
        for (int i = 0; i < arrows.Count; i++)
        {
            totalTurns += CountTurns(arrows[i]);
        }

        return totalTurns;
    }

    private static int CountTurns(CandidateArrow candidate)
    {
        int turns = 0;
        if (candidate?.PathCells == null)
        {
            return turns;
        }

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

    private static bool IsOnBoardEdge(Cell cell, int boardWidth, int boardHeight)
    {
        return cell.X == 0 || cell.Y == 0 || cell.X == boardWidth - 1 || cell.Y == boardHeight - 1;
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

            return CountTurns(a).CompareTo(CountTurns(b));
        });
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

    private static int CompareCells(Cell a, Cell b)
    {
        int yCompare = a.Y.CompareTo(b.Y);
        return yCompare != 0 ? yCompare : a.X.CompareTo(b.X);
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

    private static void LogFailure(GenerationContext context)
    {
        string reason = string.IsNullOrWhiteSpace(context.FailureReason)
            ? "unknown failure"
            : context.FailureReason;

        Debug.LogWarning($"Partial-cover level generation failed: {reason}. Best coverage: {context.BestCoveredCellCount}/{context.OccupiedCells.Count}. Required: {context.RequiredCoveredCellCount}/{context.OccupiedCells.Count}. Search frames: {context.SearchFrames}. Generated candidates: {context.GeneratedCandidateCount}. Candidate checks: {context.CandidateChecks}. Elapsed: {context.Stopwatch.Elapsed.TotalSeconds:0.00}s.");
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
        public int MaxPivotArms;
        public float MaxGenerationSeconds;
        public int ActiveMinArrowLength;
        public int ActiveMaxArrowLength;
        public int RequiredCoveredCellCount;
        public int MaxSkippedCellCount;
        public float TargetCoveragePercent;
        public int GeneratedCandidateCount;
        public int NextCandidateId;
        public int SearchFrames;
        public int CandidateChecks;
        public int BestCoveredCellCount;
        public string FailureReason;
        public HashSet<Cell> OccupiedCells;
        public Dictionary<Cell, List<CandidateArrow>> LazyCandidatesByCell;
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
        public HashSet<Cell> SkippedPivotCells;
        public DependencyGraph Graph;
    }

    private class SearchResult
    {
        public List<CandidateArrow> SelectedArrows;
        public HashSet<Cell> UsedCells;
        public DependencyGraph Graph;
        public int CoveredCellCount;
        public int TurnCount;
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
