using HexMaster.CodingStandards.Docs.GitHub;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// Stands in for GitHub. Each call takes the next queued outcome, so a test can script
/// "succeeds, then fails, then succeeds" and assert what the cache does in between.
/// </summary>
internal sealed class FakeArchiveSource : IContentArchiveSource
{
    private readonly Queue<Func<Stream>> _outcomes = new();

    /// <summary>How many times an archive was requested.</summary>
    public int CallCount { get; private set; }

    /// <summary>Queues a successful download of the given archive.</summary>
    public FakeArchiveSource Returning(Func<Stream> archive)
    {
        _outcomes.Enqueue(archive);
        return this;
    }

    /// <summary>Queues a failed download.</summary>
    public FakeArchiveSource Failing(string message = "GitHub is unreachable.")
    {
        _outcomes.Enqueue(() => throw new ContentUnavailableException(message));
        return this;
    }

    /// <inheritdoc />
    public Task<Stream> OpenArchiveAsync(CancellationToken cancellationToken)
    {
        CallCount++;

        if (_outcomes.Count == 0)
        {
            throw new ContentUnavailableException("No archive outcome was queued for this call.");
        }

        // The last queued outcome repeats, so a test does not have to queue one per tick.
        var outcome = _outcomes.Count == 1 ? _outcomes.Peek() : _outcomes.Dequeue();
        return Task.FromResult(outcome());
    }
}
