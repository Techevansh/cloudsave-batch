using CloudSave.Agent.Core;
using CloudSave.Agent.Core.Infrastructure;
using Xunit;

namespace CloudSave.Agent.Tests;

public sealed class JsonStateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonStateStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cloudsave-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Missing_file_loads_empty()
    {
        var store = JsonStateStore.Load(_path);
        Assert.Null(await store.GetAsync("whatever", default));
    }

    [Fact]
    public async Task Upsert_then_get_roundtrips()
    {
        var store = JsonStateStore.Load(_path);
        var rec = new ProcessingRecord("a\\b.pptx", ProcessingStatus.Completed, 2, DateTimeOffset.UtcNow, null);
        await store.UpsertAsync(rec, default);

        var got = await store.GetAsync("a\\b.pptx", default);
        Assert.NotNull(got);
        Assert.Equal(ProcessingStatus.Completed, got!.Status);
        Assert.Equal(2, got.Attempts);
    }

    [Fact]
    public async Task Persists_across_reload()
    {
        var store = JsonStateStore.Load(_path);
        await store.UpsertAsync(new ProcessingRecord("x.pptx", ProcessingStatus.Completed, 1, DateTimeOffset.UtcNow), default);

        var reloaded = JsonStateStore.Load(_path);
        var got = await reloaded.GetAsync("x.pptx", default);
        Assert.NotNull(got);
        Assert.Equal(ProcessingStatus.Completed, got!.Status);
    }

    [Fact]
    public async Task Corrupt_file_loads_empty_and_is_usable()
    {
        await File.WriteAllTextAsync(_path, "{ this is not valid json ]");
        var store = JsonStateStore.Load(_path);

        Assert.Null(await store.GetAsync("x.pptx", default));

        // Still writable after a corrupt load.
        await store.UpsertAsync(new ProcessingRecord("x.pptx", ProcessingStatus.Failed, 1, DateTimeOffset.UtcNow, "e"), default);
        var got = await store.GetAsync("x.pptx", default);
        Assert.NotNull(got);
        Assert.Equal(ProcessingStatus.Failed, got!.Status);
    }
}
