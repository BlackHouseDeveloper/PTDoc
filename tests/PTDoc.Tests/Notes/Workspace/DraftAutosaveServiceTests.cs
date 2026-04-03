using System.Threading;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "Workspace")]
public sealed class DraftAutosaveServiceTests
{
    [Fact]
    public async Task MarkDirty_TriggersDebouncedSave()
    {
        await using var autosave = new DraftAutosaveService();
        var saveInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;

        autosave.Configure(
            async cancellationToken =>
            {
                Interlocked.Increment(ref saveCount);
                saveInvoked.TrySetResult(true);
                await Task.Yield();
                return true;
            },
            () => true);

        autosave.MarkDirty();

        await saveInvoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => !autosave.IsSaving && !autosave.IsDirty, TimeSpan.FromSeconds(1));

        Assert.Equal(1, Volatile.Read(ref saveCount));
        Assert.False(autosave.IsDirty);
        Assert.False(autosave.IsSaving);
        Assert.NotNull(autosave.LastSavedAt);
        Assert.Null(autosave.LastErrorMessage);
    }

    [Fact]
    public async Task FlushAsync_DoesNotSave_WhenCanSaveIsFalse()
    {
        await using var autosave = new DraftAutosaveService();
        var saveCount = 0;

        autosave.Configure(
            cancellationToken =>
            {
                Interlocked.Increment(ref saveCount);
                return Task.FromResult(true);
            },
            () => false);

        autosave.MarkDirty();
        var flushed = await autosave.FlushAsync();

        Assert.True(flushed);
        Assert.Equal(0, Volatile.Read(ref saveCount));
        Assert.False(autosave.IsDirty);
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for autosave state to settle.");
    }
}
