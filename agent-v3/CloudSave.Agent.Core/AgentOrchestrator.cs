namespace CloudSave.Agent.Core;

/// <summary>
/// Owns the per-run / per-file state machine and the cancellation token.
///
/// A single document cycle:
///   Discover -> Open -> WaitOffice -> EnsureAnalyzer -> StartAnalysis
///            -> AuthWait(if needed) -> WaitComplete -> Close -> Persist -> Next
///
/// Design guarantees:
///  * One file's failure never aborts the run (each file is isolated).
///  * Office is always closed (finally), even when analysis throws.
///  * GoBack always runs after entering a folder (finally), even on child failure.
///  * Transient UIA errors on read/navigation are retried with bounded backoff.
///  * Cancellation is observed promptly and surfaced as USER_CANCELLED.
///  * Resume: files already "completed" in the state store are skipped.
///  * Recursion and a file cap are request-controlled for phased testing.
/// </summary>
public sealed class AgentOrchestrator
{
    private readonly IEventWriter _events;
    private readonly IExplorerAdapter _explorer;
    private readonly IOfficeAdapter _office;
    private readonly IAnalyzerAdapter _analyzer;
    private readonly IStateStore _state;
    private readonly RetryPolicy _retry;

    public AgentOrchestrator(
        IEventWriter events,
        IExplorerAdapter explorer,
        IOfficeAdapter office,
        IAnalyzerAdapter analyzer,
        IStateStore state,
        RetryPolicy? retry = null)
    {
        _events = events;
        _explorer = explorer;
        _office = office;
        _analyzer = analyzer;
        _state = state;
        _retry = retry ?? new RetryPolicy();
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var counters = new Counters();
        try
        {
            var root = await _retry.ExecuteAsync(
                _explorer.GetStartContextAsync, "explorer.start", _events, cancellationToken).ConfigureAwait(false);

            await _events.WriteAsync(AgentEvent.Info("explorer.start", $"Start context: {root}"), cancellationToken)
                .ConfigureAwait(false);

            await TraverseAsync(root, depth: 0, request, counters, cancellationToken).ConfigureAwait(false);

            return AgentRunResult.Ok(counters.Processed, counters.Failed, counters.Skipped);
        }
        catch (OperationCanceledException)
        {
            await _events.WriteAsync(
                AgentEvent.Warn("agent.cancelled", ErrorCodes.UserCancelled, "Run cancelled by user"),
                CancellationToken.None).ConfigureAwait(false);
            return AgentRunResult.Fail(ErrorCodes.UserCancelled, "Run cancelled by user",
                counters.Processed, counters.Failed, counters.Skipped);
        }
        catch (AgentOperationException ex)
        {
            await _events.WriteAsync(
                AgentEvent.Error("agent.exception", ex.Code, ex.Message), CancellationToken.None).ConfigureAwait(false);
            return AgentRunResult.Fail(ex.Code, ex.Message, counters.Processed, counters.Failed, counters.Skipped);
        }
        catch (Exception ex)
        {
            await _events.WriteAsync(
                AgentEvent.Error("agent.exception", ErrorCodes.Unhandled, ex.Message), CancellationToken.None)
                .ConfigureAwait(false);
            return AgentRunResult.Fail(ErrorCodes.Unhandled, ex.Message,
                counters.Processed, counters.Failed, counters.Skipped);
        }
    }

    private bool CapReached(AgentRunRequest request, Counters counters) =>
        request.MaxOfficeFiles > 0 && counters.OfficeAttempts >= request.MaxOfficeFiles;

    private async Task TraverseAsync(
        string logicalFolder,
        int depth,
        AgentRunRequest request,
        Counters counters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (depth > request.MaxDepth)
        {
            counters.Skipped++;
            await _events.WriteAsync(
                AgentEvent.Warn("folder.skip", "MAX_DEPTH", $"Skipped {logicalFolder}: max depth exceeded"),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var snapshot = await _retry.ExecuteAsync(
            ct => _explorer.ReadItemsAsync(logicalFolder, ct), "explorer.read", _events, cancellationToken)
            .ConfigureAwait(false);

        var files = snapshot
            .Where(x => !x.IsFolder && x.Kind is DocumentKind.PowerPoint or DocumentKind.Excel)
            .ToArray();
        var folderNames = snapshot.Where(x => x.IsFolder).Select(x => x.Name).ToArray();

        await _events.WriteAsync(
            AgentEvent.Info("folder.scan",
                $"{logicalFolder}: office={files.Length} folders={folderNames.Length} total={snapshot.Count}"),
            cancellationToken).ConfigureAwait(false);

        // 1) Process Office files first so folder navigation does not invalidate cached elements.
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CapReached(request, counters)) return;
            await ProcessFileAsync(file, request, counters, cancellationToken).ConfigureAwait(false);
        }

        if (!request.EnableRecursion) return;
        if (CapReached(request, counters)) return;

        // 2) Re-find each folder by name immediately before entering (avoid stale UIA elements).
        foreach (var folderName in folderNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CapReached(request, counters)) return;

            ExplorerItem? folder;
            try
            {
                var fresh = await _retry.ExecuteAsync(
                    ct => _explorer.ReadItemsAsync(logicalFolder, ct), "explorer.read", _events, cancellationToken)
                    .ConfigureAwait(false);
                folder = fresh.FirstOrDefault(x => x.IsFolder && string.Equals(x.Name, folderName, StringComparison.Ordinal));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                counters.Failed++;
                await _events.WriteAsync(
                    AgentEvent.Error("folder.read", Categorize(ex), $"Could not re-read folder {folderName}: {ex.Message}"),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (folder is null)
            {
                counters.Failed++;
                await _events.WriteAsync(
                    AgentEvent.Warn("folder.missing", ErrorCodes.ExplorerRowNotFound, $"Folder disappeared: {folderName}"),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Enter + recurse + back. A failure anywhere in the subtree is isolated
            // (logged, counted, continue with the next sibling) so it never aborts
            // the whole run. Cancellation still propagates. GoBack runs only if we
            // actually entered, and always runs after a successful enter.
            var entered = false;
            try
            {
                await _events.WriteAsync(AgentEvent.Info("folder.enter", folder.LogicalPath), cancellationToken)
                    .ConfigureAwait(false);
                await _explorer.EnterFolderAsync(folder, cancellationToken).ConfigureAwait(false);
                entered = true;

                await TraverseAsync(folder.LogicalPath, depth + 1, request, counters, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                counters.Failed++;
                await _events.WriteAsync(
                    AgentEvent.Error("folder.failed", Categorize(ex), $"Subtree failed at {folder.LogicalPath}: {ex.Message}"),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (entered && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await _explorer.GoBackAsync(cancellationToken).ConfigureAwait(false);
                        await _events.WriteAsync(AgentEvent.Info("folder.back", logicalFolder), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await _events.WriteAsync(
                            AgentEvent.Error("folder.back", Categorize(ex), $"GoBack failed at {logicalFolder}: {ex.Message}"),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task ProcessFileAsync(
        ExplorerItem file,
        AgentRunRequest request,
        Counters counters,
        CancellationToken cancellationToken)
    {
        var prior = await _state.GetAsync(file.LogicalPath, cancellationToken).ConfigureAwait(false);
        if (request.EnableResume && prior?.Status == ProcessingStatus.Completed)
        {
            counters.Skipped++;
            await _events.WriteAsync(AgentEvent.Info("file.skip", $"Already completed: {file.LogicalPath}"), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        counters.OfficeAttempts++;
        var attempts = (prior?.Attempts ?? 0) + 1;

        OfficeSession? session = null;
        try
        {
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, ProcessingStatus.Opening, attempts, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);

            await _events.WriteAsync(AgentEvent.Info("file.open", file.LogicalPath), cancellationToken).ConfigureAwait(false);
            await _explorer.ActivateFileAsync(file, cancellationToken).ConfigureAwait(false);

            session = await _office.WaitForDocumentAsync(file, cancellationToken).ConfigureAwait(false);
            await _events.WriteAsync(
                AgentEvent.Info("office.ready", $"{session.Kind} :: {session.Title}"), cancellationToken).ConfigureAwait(false);

            await _analyzer.EnsureAnalyzerOpenAsync(session, cancellationToken).ConfigureAwait(false);

            var analysis = await _analyzer.RunAnalysisAsync(session, cancellationToken).ConfigureAwait(false);
            if (!analysis.Success)
                throw new AgentOperationException(analysis.ErrorCode ?? ErrorCodes.AnalysisFailed,
                    analysis.Message ?? "Analysis failed");

            if (analysis.AuthenticationWasInteractive)
                await _events.WriteAsync(AgentEvent.Info("auth.interactive", $"Interactive sign-in completed for {file.LogicalPath}"),
                    cancellationToken).ConfigureAwait(false);

            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, ProcessingStatus.Completed, attempts, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);

            counters.Processed++;
            await _events.WriteAsync(AgentEvent.Info("file.complete", file.LogicalPath), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = Categorize(ex);
            counters.Failed++;
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, ProcessingStatus.Failed, attempts, DateTimeOffset.UtcNow, ex.Message),
                CancellationToken.None).ConfigureAwait(false);
            await _events.WriteAsync(
                AgentEvent.Error("file.failed", code, file.LogicalPath, new { ex.Message }), CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await _office.CloseWithoutSavingAsync(session, CancellationToken.None).ConfigureAwait(false);
                    await _events.WriteAsync(AgentEvent.Info("office.closed", file.LogicalPath), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _events.WriteAsync(
                        AgentEvent.Warn("office.close", ErrorCodes.OfficeCloseTimeout, $"Close failed for {file.LogicalPath}: {ex.Message}"),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    private static string Categorize(Exception ex) =>
        ex is AgentOperationException op ? op.Code : ErrorCodes.Unexpected;

    private sealed class Counters
    {
        public int Processed;
        public int Failed;
        public int Skipped;
        public int OfficeAttempts;
    }
}
