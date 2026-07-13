using System.Net.Http;
using System.Threading;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
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
                return DraftAutosaveSaveResult.Succeeded();
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
                return Task.FromResult(DraftAutosaveSaveResult.Succeeded());
            },
            () => false);

        autosave.MarkDirty();
        var flushed = await autosave.FlushAsync();

        Assert.True(flushed);
        Assert.Equal(0, Volatile.Read(ref saveCount));
        Assert.False(autosave.IsDirty);
    }

    [Fact]
    public async Task FlushAsync_FailureRetainsDirtyState_AndRetryClearsError()
    {
        await using var autosave = new DraftAutosaveService();
        var saveCount = 0;

        autosave.Configure(
            cancellationToken =>
            {
                var attempt = Interlocked.Increment(ref saveCount);
                return Task.FromResult(attempt == 1
                    ? DraftAutosaveSaveResult.Failed("API rejected the draft.")
                    : DraftAutosaveSaveResult.Succeeded());
            },
            () => true);

        autosave.MarkDirty();
        var firstFlush = await autosave.FlushAsync();

        Assert.False(firstFlush);
        Assert.True(autosave.IsDirty);
        Assert.False(autosave.IsSaving);
        Assert.Equal("API rejected the draft.", autosave.LastErrorMessage);
        Assert.Null(autosave.LastSavedAt);

        var secondFlush = await autosave.FlushAsync();

        Assert.True(secondFlush);
        Assert.False(autosave.IsDirty);
        Assert.False(autosave.IsSaving);
        Assert.Null(autosave.LastErrorMessage);
        Assert.NotNull(autosave.LastSavedAt);
    }

    [Fact]
    public async Task FlushAsync_GenericException_UsesStableFallbackMessage()
    {
        await using var autosave = new DraftAutosaveService();

        autosave.Configure(
            cancellationToken => throw new InvalidOperationException("Internal persistence secret details."),
            () => true);

        autosave.MarkDirty();
        var flushed = await autosave.FlushAsync();

        Assert.False(flushed);
        Assert.True(autosave.IsDirty);
        Assert.False(autosave.IsSaving);
        Assert.Equal("Unable to save draft.", autosave.LastErrorMessage);
    }

    [Fact]
    public async Task FlushAsync_HttpRequestException_UsesTrimmedUserFacingMessage()
    {
        await using var autosave = new DraftAutosaveService();

        autosave.Configure(
            cancellationToken => throw new HttpRequestException("  Connection refused (localhost:5170)  "),
            () => true);

        autosave.MarkDirty();
        var flushed = await autosave.FlushAsync();

        Assert.False(flushed);
        Assert.True(autosave.IsDirty);
        Assert.False(autosave.IsSaving);
        Assert.Equal("Connection refused (localhost:5170)", autosave.LastErrorMessage);
    }

    [Fact]
    public async Task EditDuringInFlightSave_RemainsDirty_AndQueuesFollowUpSave()
    {
        await using var autosave = new DraftAutosaveService();
        var firstSaveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;

        autosave.Configure(
            async cancellationToken =>
            {
                var attempt = Interlocked.Increment(ref saveCount);
                if (attempt == 1)
                {
                    firstSaveStarted.TrySetResult(true);
                    await releaseFirstSave.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondSaveCompleted.TrySetResult(true);
                }

                return DraftAutosaveSaveResult.Succeeded();
            },
            () => true);

        autosave.MarkDirty();
        var firstFlush = autosave.FlushAsync();
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        autosave.MarkDirty();
        Assert.True(autosave.IsDirty);
        releaseFirstSave.TrySetResult(true);

        Assert.True(await firstFlush);

        await secondSaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForConditionAsync(() => !autosave.IsSaving && !autosave.IsDirty, TimeSpan.FromSeconds(1));

        Assert.Equal(2, Volatile.Read(ref saveCount));
        Assert.False(autosave.IsDirty);
        Assert.NotNull(autosave.LastSavedAt);
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
