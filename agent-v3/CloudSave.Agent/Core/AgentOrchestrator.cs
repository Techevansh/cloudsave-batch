namespace CloudSave.Agent.Core;

public sealed class AgentOrchestrator
{
    private readonly IEventWriter _events;
    private readonly IExplorerAdapter _explorer;
    private readonly IOfficeAdapter _office;
    private readonly IAnalyzerAdapter _analyzer;
    private readonly IStateStore _state;

    public AgentOrchestrator(
        IEventWriter events,
        IExplorerAdapter explorer,
        IOfficeAdapter office,
        IAnalyzerAdapter analyzer,
        IStateStore state)
    {
        _events = events;
        _explorer = explorer;
        _office = office;
        _analyzer = analyzer;
        _state = state;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var counters = new Counters();

        try
        {
            var root = await _explorer.GetStartContextAsync(cancellationToken);
            await _events.WriteAsync(AgentEvent.Info("explorer.start", $"Start context: {root}"), cancellationToken);

            await TraverseAsync(root, depth: 0, request.MaxDepth, counters, cancellationToken);

            return AgentRunResult.Ok(counters.Processed, counters.Failed, counters.Skipped);
        }
        catch (OperationCanceledException)
        {
            await _events.WriteAsync(AgentEvent.Warn("agent.cancelled", "USER_CANCELLED", "Run cancelled by user"), CancellationToken.None);
            return AgentRunResult.Fail("USER_CANCELLED", "Run cancelled by user", counters.Processed, counters.Failed, counters.Skipped);
        }
        catch (Exception ex)
        {
            await _events.WriteAsync(AgentEvent.Error("agent.exception", "UNHANDLED", ex.Message), CancellationToken.None);
            return AgentRunResult.Fail("UNHANDLED", ex.Message, counters.Processed, counters.Failed, counters.Skipped);
        }
    }

    private async Task TraverseAsync(
        string logicalFolder,
        int depth,
        int maxDepth,
        Counters counters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth > maxDepth)
        {
            counters.Skipped++;
            await _events.WriteAsync(
                AgentEvent.Warn("folder.skip", "MAX_DEPTH", $"Skipped {logicalFolder} because max depth was exceeded"),
                cancellationToken);
            return;
        }

        var snapshot = await _explorer.ReadItemsAsync(logicalFolder, cancellationToken);

        var files = snapshot.Where(x => !x.IsFolder && x.Kind is DocumentKind.PowerPoint or DocumentKind.Excel).ToArray();
        var folders = snapshot.Where(x => x.IsFolder).Select(x => x.Name).ToArray();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessFileAsync(file, counters, cancellationToken);
        }

        foreach (var folderName in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fresh = await _explorer.ReadItemsAsync(logicalFolder, cancellationToken);
            var folder = fresh.FirstOrDefault(x => x.IsFolder && string.Equals(x.Name, folderName, StringComparison.Ordinal));

            if (folder is null)
            {
                counters.Failed++;
                await _events.WriteAsync(
                    AgentEvent.Warn("folder.missing", "EXPLORER_ROW_NOT_FOUND", $"Folder disappeared: {folderName}"),
                    cancellationToken);
                continue;
            }

            await _explorer.EnterFolderAsync(folder, cancellationToken);
            try
            {
                await TraverseAsync(folder.LogicalPath, depth + 1, maxDepth, counters, cancellationToken);
            }
            finally
            {
                await _explorer.GoBackAsync(cancellationToken);
            }
        }
    }

    private async Task ProcessFileAsync(
        ExplorerItem file,
        Counters counters,
        CancellationToken cancellationToken)
    {
        var prior = await _state.GetAsync(file.LogicalPath, cancellationToken);
        if (prior?.Status == "completed")
        {
            counters.Skipped++;
            await _events.WriteAsync(AgentEvent.Info("file.skip", $"Already completed: {file.LogicalPath}"), cancellationToken);
            return;
        }

        var attempts = (prior?.Attempts ?? 0) + 1;

        try
        {
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, "opening", attempts, DateTimeOffset.UtcNow),
                cancellationToken);

            await _events.WriteAsync(AgentEvent.Info("file.open", file.LogicalPath), cancellationToken);
            await _explorer.ActivateFileAsync(file, cancellationToken);

            var session = await _office.WaitForDocumentAsync(file, cancellationToken);

            try
            {
                await _analyzer.EnsureAnalyzerOpenAsync(session, cancellationToken);
                var analysis = await _analyzer.RunAnalysisAsync(session, cancellationToken);

                if (!analysis.Success)
                    throw new AgentOperationException(analysis.ErrorCode ?? "ANALYSIS_FAILED", analysis.Message ?? "Analysis failed");

                await _state.UpsertAsync(
                    new ProcessingRecord(file.LogicalPath, "completed", attempts, DateTimeOffset.UtcNow),
                    cancellationToken);

                counters.Processed++;
                await _events.WriteAsync(AgentEvent.Info("file.complete", file.LogicalPath), cancellationToken);
            }
            finally
            {
                await _office.CloseWithoutSavingAsync(session, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AgentOperationException ex)
        {
            counters.Failed++;
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, "failed", attempts, DateTimeOffset.UtcNow, ex.Message),
                cancellationToken);

            await _events.WriteAsync(AgentEvent.Error("file.failed", ex.Code, file.LogicalPath, new { ex.Message }), cancellationToken);
        }
        catch (Exception ex)
        {
            counters.Failed++;
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, "failed", attempts, DateTimeOffset.UtcNow, ex.Message),
                cancellationToken);

            await _events.WriteAsync(AgentEvent.Error("file.failed", "UNEXPECTED", file.LogicalPath, new { ex.Message }), cancellationToken);
        }
    }

    private sealed class Counters
    {
        public int Processed;
        public int Failed;
        public int Skipped;
    }
}

public sealed class AgentOperationException : Exception
{
    public AgentOperationException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
