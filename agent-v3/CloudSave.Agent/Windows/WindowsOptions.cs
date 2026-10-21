namespace CloudSave.Agent.Windows;

/// <summary>Timeouts for the Windows adapters (seconds in the env, TimeSpan here).</summary>
internal sealed class WindowsOptions
{
    public TimeSpan OfficeOpenTimeout { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan AnalyzerOpenTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan AnalysisTimeout { get; init; } = TimeSpan.FromSeconds(600);
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long a folder double-click may take to navigate (network drives can be slow).</summary>
    public TimeSpan EnterFolderTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long Back may take to return to the parent before the run stops as "location lost".</summary>
    public TimeSpan BackTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Upper bound for an Explorer listing to stop changing after navigation.</summary>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public bool EnableProtectedViewEdit { get; init; } = true;

    public static WindowsOptions FromEnvironment()
    {
        int Sec(string name, int fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return int.TryParse(v, out var n) && n > 0 ? n : fallback;
        }
        bool Flag(string name, bool fallback)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return bool.TryParse(v, out var b) ? b : fallback;
        }

        return new WindowsOptions
        {
            OfficeOpenTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_OFFICE_OPEN_SEC", 90)),
            AnalyzerOpenTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_ANALYZER_OPEN_SEC", 45)),
            AnalysisTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_ANALYSIS_SEC", 600)),
            CloseTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_CLOSE_SEC", 20)),
            EnterFolderTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_ENTER_FOLDER_SEC", 20)),
            BackTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_BACK_SEC", 20)),
            SettleTimeout = TimeSpan.FromSeconds(Sec("CLOUDSAVE_SETTLE_SEC", 8)),
            EnableProtectedViewEdit = Flag("CLOUDSAVE_PROTECTED_VIEW_EDIT", true),
        };
    }
}
