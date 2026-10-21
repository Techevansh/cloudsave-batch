using System.Diagnostics;

namespace CloudSave.Agent.Core;

/// <summary>
/// Owns the per-run / per-file state machine and the cancellation token.
///
/// A run:
///   StartContext -> Announce target (+ optional countdown) -> Traverse -> Resume bookkeeping
/// A single document cycle:
///   PrepareOpen -> Open -> WaitOffice -> EnsureAnalyzer -> StartAnalysis
///            -> AuthWait(if needed) -> WaitComplete(verified per destination) -> Close -> Persist -> Next
///
/// Design guarantees:
///  * One file's failure never aborts the run (each file and each subfolder subtree is isolated).
///  * Office is always closed (finally), even when analysis throws.
///  * GoBack always runs after entering a folder (finally), even on child failure.
///  * If Explorer cannot be verifiably returned to the expected folder, the run STOPS
///    (EXPLORER_LOCATION_LOST) instead of processing files in the wrong place; resume continues it.
///  * Transient UIA errors on read/navigation are retried with bounded backoff.
///  * Cancellation is observed promptly and surfaced as USER_CANCELLED.
///  * Resume: files already "completed" in the state store are skipped. A full pass with no
///    failures clears the folder's records, so the next run on that folder starts fresh.
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
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public AgentOrchestrator(
        IEventWriter events,
        IExplorerAdapter explorer,
        IOfficeAdapter office,
        IAnalyzerAdapter analyzer,
        IStateStore state,
        RetryPolicy? retry = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _events = events;
        _explorer = explorer;
        _office = office;
        _analyzer = analyzer;
        _state = state;
        _retry = retry ?? new RetryPolicy();
        _delay = delay ?? Task.Delay;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var counters = new Counters();
        try
        {
            var root = await _retry.ExecuteAsync(
                _explorer.GetStartContextAsync, "explorer.start", _events, cancellationToken).ConfigureAwait(false);
            counters.Root = root;

            await _events.WriteAsync(AgentEvent.Info("explorer.start", $"Start context: {root}"), cancellationToken)
                .ConfigureAwait(false);

            await AnnounceTargetAsync(root, request, cancellationToken).ConfigureAwait(false);

            await TraverseAsync(root, depth: 0, request, counters, cancellationToken).ConfigureAwait(false);

            await FinishResumeAsync(root, request, counters, cancellationToken).ConfigureAwait(false);

            return Build(counters, success: true);
        }
        catch (OperationCanceledException)
        {
            await _events.WriteAsync(
                AgentEvent.Warn("agent.cancelled", ErrorCodes.UserCancelled, "Run cancelled by user"),
                CancellationToken.None).ConfigureAwait(false);
            return Build(counters, success: false, ErrorCodes.UserCancelled, "Run cancelled by user");
        }
        catch (AgentOperationException ex)
        {
            await _events.WriteAsync(
                AgentEvent.Error("agent.exception", ex.Code, ex.Message), CancellationToken.None).ConfigureAwait(false);
            return Build(counters, success: false, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            await _events.WriteAsync(
                AgentEvent.Error("agent.exception", ErrorCodes.Unhandled, ex.Message), CancellationToken.None)
                .ConfigureAwait(false);
            return Build(counters, success: false, ErrorCodes.Unhandled, ex.Message);
        }
    }

    /// <summary>Tell the user exactly which folder will be processed, then (optionally) give them time to abort with F12.</summary>
    private async Task AnnounceTargetAsync(string root, AgentRunRequest request, CancellationToken cancellationToken)
    {
        var items = await _retry.ExecuteAsync(
            ct => _explorer.ReadItemsAsync(root, ct), "explorer.read", _events, cancellationToken).ConfigureAwait(false);

        var target = new RunTarget(
            root,
            items.Count(IsProcessable),
            items.Count(x => x.IsFolder),
            request.EnableRecursion,
            (int)Math.Round(request.StartDelay.TotalSeconds));

        await _events.WriteAsync(
            AgentEvent.Info("run.target",
                $"Target: {root} (office={target.OfficeFiles}, folders={target.Folders}, recursion={target.Recursion})", target),
            cancellationToken).ConfigureAwait(false);

        if (request.StartDelay > TimeSpan.Zero)
        {
            await _events.WriteAsync(
                AgentEvent.Info("run.countdown", $"Starting in {target.StartDelaySeconds}s (F12 cancels)", target),
                cancellationToken).ConfigureAwait(false);
            await _delay(request.StartDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FinishResumeAsync(string root, AgentRunRequest request, Counters counters, CancellationToken cancellationToken)
    {
        // A capped (test) run is never a complete pass; without resume nothing is remembered anyway.
        if (!request.EnableResume || request.MaxOfficeFiles > 0) return;

        if (counters.Failed == 0)
        {
            var removed = await _state.RemoveUnderAsync(root, cancellationToken).ConfigureAwait(false);
            await _events.WriteAsync(
                AgentEvent.Info("state.cleared",
                    $"Complete pass with no failures; cleared {removed} resume record(s). The next run on this folder starts fresh."),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _events.WriteAsync(
                AgentEvent.Info("state.kept",
                    $"{counters.Failed} failure(s). Running again on this folder retries only those; completed files are skipped."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsProcessable(ExplorerItem x) =>
        !x.IsFolder && x.Kind is DocumentKind.PowerPoint or DocumentKind.Excel;

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

        var snapshot = await _retry.ExecuteAsync(
            ct => _explorer.ReadItemsAsync(logicalFolder, ct), "explorer.read", _events, cancellationToken)
            .ConfigureAwait(false);

        var files = snapshot.Where(IsProcessable).ToArray();
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

            var folderPath = LogicalPaths.Combine(logicalFolder, folderName);
            if (depth + 1 > request.MaxDepth)
            {
                counters.Skipped++;
                await _events.WriteAsync(
                    AgentEvent.Warn("folder.skip", "MAX_DEPTH", $"Skipped {folderPath}: max depth {request.MaxDepth} reached"),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

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
                await RecordFailureAsync(counters, "folder.read", folderPath, Categorize(ex),
                    $"Could not re-read folder {folderName}: {ex.Message}").ConfigureAwait(false);
                continue;
            }

            if (folder is null)
            {
                await RecordFailureAsync(counters, "folder.missing", folderPath, ErrorCodes.ExplorerRowNotFound,
                    $"Folder disappeared: {folderName}", warn: true).ConfigureAwait(false);
                continue;
            }

            // Enter + recurse + back. A failure anywhere in the subtree is isolated
            // (logged, counted, continue with the next sibling) so it never aborts the
            // whole run, EXCEPT a lost location, which must stop the run. Cancellation
            // still propagates. GoBack runs only if we actually entered.
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
            catch (AgentOperationException ex) when (ex.Code == ErrorCodes.ExplorerLocationLost)
            {
                counters.LocationLost = true;
                throw;
            }
            catch (Exception ex)
            {
                await RecordFailureAsync(counters, "folder.failed", folder.LogicalPath, Categorize(ex),
                    $"Subtree failed at {folder.LogicalPath}: {ex.Message}").ConfigureAwait(false);
            }
            finally
            {
                if (entered && !counters.LocationLost && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await _explorer.GoBackAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        counters.LocationLost = true;
                        throw new AgentOperationException(ErrorCodes.ExplorerLocationLost,
                            $"Could not return to {logicalFolder} after {folder.LogicalPath}: {ex.Message} " +
                            "Stopped so that no file is processed in the wrong folder; run again to resume.", ex);
                    }
                    await _events.WriteAsync(AgentEvent.Info("folder.back", logicalFolder), cancellationToken)
                        .ConfigureAwait(false);
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
        var index = counters.OfficeAttempts;
        var attempts = (prior?.Attempts ?? 0) + 1;
        var clock = Stopwatch.StartNew();

        OfficeSession? session = null;
        try
        {
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, ProcessingStatus.Opening, attempts, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);

            await _events.WriteAsync(AgentEvent.Info("file.open", file.LogicalPath, new FileProgress(index, file.Name)),
                cancellationToken).ConfigureAwait(false);

            await _office.PrepareForOpenAsync(file, cancellationToken).ConfigureAwait(false);
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
            if (!analysis.Verified)
            {
                counters.Unverified++;
                await _events.WriteAsync(
                    AgentEvent.Warn("file.unverified", ErrorCodes.UploadUnverified,
                        $"Finished, but the per-destination upload lines could not be read: {file.LogicalPath}",
                        new FileProgress(index, file.Name)),
                    cancellationToken).ConfigureAwait(false);
            }

            await _events.WriteAsync(
                AgentEvent.Info("file.complete", file.LogicalPath,
                    new FileProgress(index, file.Name, Math.Round(clock.Elapsed.TotalSeconds, 1))),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = Categorize(ex);
            await _state.UpsertAsync(
                new ProcessingRecord(file.LogicalPath, ProcessingStatus.Failed, attempts, DateTimeOffset.UtcNow, ex.Message),
                CancellationToken.None).ConfigureAwait(false);
            await RecordFailureAsync(counters, "file.failed", file.LogicalPath, code, ex.Message).ConfigureAwait(false);
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

    private async Task RecordFailureAsync(
        Counters counters, string eventType, string logicalPath, string code, string message, bool warn = false)
    {
        counters.Failed++;
        var failure = new FileFailure(logicalPath, code, message);
        counters.Failures.Add(failure);
        await _events.WriteAsync(
            warn
                ? AgentEvent.Warn(eventType, code, message, failure)
                : AgentEvent.Error(eventType, code, message, failure),
            CancellationToken.None).ConfigureAwait(false);
    }

    private static AgentRunResult Build(Counters c, bool success, string? code = null, string? message = null) => new()
    {
        Success = success,
        Processed = c.Processed,
        Failed = c.Failed,
        Skipped = c.Skipped,
        Unverified = c.Unverified,
        Root = c.Root,
        Failures = c.Failures.ToList(),
        ErrorCode = code,
        Message = message,
    };

    private static string Categorize(Exception ex) =>
        ex is AgentOperationException op ? op.Code : ErrorCodes.Unexpected;

    private sealed class Counters
    {
        public string Root = "";
        public int Processed;
        public int Failed;
        public int Skipped;
        public int Unverified;
        public int OfficeAttempts;
        public bool LocationLost;
        public readonly List<FileFailure> Failures = new();
    }
}

/// <summary>Data of the run.target / run.countdown events.</summary>
public sealed record RunTarget(string Root, int OfficeFiles, int Folders, bool Recursion, int StartDelaySeconds);

/// <summary>Data of the per-file events (1-based index in this run, file name, seconds taken when complete).</summary>
public sealed record FileProgress(int Index, string Name, double? Seconds = null);
