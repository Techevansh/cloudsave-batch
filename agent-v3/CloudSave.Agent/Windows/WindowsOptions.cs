namespace CloudSave.Agent.Windows;

/// <summary>Timeouts for the Windows adapters (seconds in the env, TimeSpan here).</summary>
internal sealed class WindowsOptions
{
    public TimeSpan OfficeOpenTimeout { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan AnalyzerOpenTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan AnalysisTimeout { get; init; } = TimeSpan.FromSeconds(600);
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan EnterFolderTimeout { get; init; } = TimeSpan.FromSeconds(10);
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
            EnableProtectedViewEdit = Flag("CLOUDSAVE_PROTECTED_VIEW_EDIT", true),
        };
    }
}
