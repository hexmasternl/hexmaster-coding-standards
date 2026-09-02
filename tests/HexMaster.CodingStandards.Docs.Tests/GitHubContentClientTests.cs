using System.Net;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.Tests;

public class GitHubContentClientTests
{
    [Fact]
    public async Task ComposesTheContentsApiUriWithTheRefQuery()
    {
        var handler = new RecordingHandler(Ok("# A decision"));
        var client = Client(handler);

        await client.GetContentAsync("docs/ADR/0001-a-decision.md", TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.ToString().ShouldBe(
            "https://api.github.com/repos/hexmasternl/hexmaster-coding-standards/contents/docs/ADR/0001-a-decision.md?ref=main");
    }

    [Fact]
    public async Task RequestsTheRawMediaTypeSoThereIsNoJsonEnvelopeToDecode()
    {
        var handler = new RecordingHandler(Ok("# A decision"));

        await Client(handler).GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        handler.LastRequest!.Headers.Accept.Select(header => header.MediaType)
            .ShouldContain("application/vnd.github.raw");
    }

    [Fact]
    public async Task EncodesPathSegmentsInTheUri()
    {
        var handler = new RecordingHandler(Ok("# Spaced"));

        await Client(handler).GetContentAsync("docs/ADR/a document.md", TestContext.Current.CancellationToken);

        // AbsoluteUri, not ToString(): ToString() unescapes for display, so it would hide
        // whether the escaping actually survives onto the wire.
        handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldContain("a%20document.md");
        handler.LastRequest.RequestUri.PathAndQuery.ShouldContain("a%20document.md");
    }

    [Fact]
    public async Task EncodesTheRefInTheQuery()
    {
        var handler = new RecordingHandler(Ok("# On a branch"));

        await Client(handler, options => options.Ref = "feature/a branch")
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.Query.ShouldContain("ref=feature%2Fa%20branch");
    }

    [Fact]
    public async Task SendsTheTokenWhenConfigured()
    {
        var handler = new RecordingHandler(Ok("# A decision"));

        await Client(handler, options => options.AccessToken = "ghp-not-a-real-token")
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        handler.LastRequest!.Headers.Authorization.ShouldNotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
    }

    [Fact]
    public async Task OmitsTheAuthorizationHeaderEntirelyWhenNoTokenIsConfigured()
    {
        var handler = new RecordingHandler(Ok("# A decision"));

        await Client(handler).GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        handler.LastRequest!.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task ReturnsTheBodyTextOnSuccess()
    {
        var result = await Client(new RecordingHandler(Ok("# A decision\n\nBody.")))
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.Success);
        result.Content.ShouldBe("# A decision\n\nBody.");
    }

    [Fact]
    public async Task MapsNotFoundToAMissingFileOutcome()
    {
        var result = await Client(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound)))
            .GetContentAsync("docs/ADR/gone.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.NotFound);
        result.Reason!.ShouldContain("docs/ADR/gone.md");
    }

    [Fact]
    public async Task MapsAnExhaustedRateLimitToTheRateLimitedOutcome()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", "1793491200");

        var result = await Client(new RecordingHandler(response))
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.RateLimited);
        result.Reason!.ShouldContain("rate-limited");
    }

    [Fact]
    public async Task MapsTooManyRequestsToTheRateLimitedOutcome()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("retry-after", "60");

        var result = await Client(new RecordingHandler(response))
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.RateLimited);
        result.Reason!.ShouldContain("60");
    }

    [Fact]
    public async Task MapsAForbiddenResponseWithLimitRemainingToAGeneralFailure()
    {
        // 403 also means "not authorized", which is a different problem with a different fix,
        // so the rate-limit headers are what separate the two.
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "4999");

        var result = await Client(new RecordingHandler(response))
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.Failed);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task MapsOtherNonSuccessResponsesToAGeneralFailure(HttpStatusCode status)
    {
        var result = await Client(new RecordingHandler(new HttpResponseMessage(status)))
            .GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.Failed);
        result.Reason!.ShouldContain(((int)status).ToString());
    }

    [Fact]
    public async Task MapsATransportFailureToAGeneralFailure()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("socket closed"));

        var result = await Client(handler).GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.Failed);
        result.Reason.ShouldBe("GitHub could not be reached");
    }

    [Fact]
    public async Task MapsATimeoutToAGeneralFailure()
    {
        var handler = new RecordingHandler(_ => throw new TaskCanceledException("timed out"));

        var result = await Client(handler).GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

        result.Status.ShouldBe(ContentFetchStatus.Failed);
        result.Reason.ShouldBe("the request to GitHub timed out");
    }

    [Fact]
    public async Task NoFailureReasonLeaksTheTokenOrAStackTrace()
    {
        const string Token = "ghp-super-secret-value";

        var responses = new HttpResponseMessage[]
        {
            new(HttpStatusCode.NotFound),
            new(HttpStatusCode.InternalServerError),
            new(HttpStatusCode.Forbidden)
        };

        foreach (var response in responses)
        {
            var client = Client(new RecordingHandler(response), options => options.AccessToken = Token);

            var result = await client.GetContentAsync("docs/ADR/a.md", TestContext.Current.CancellationToken);

            result.Reason.ShouldNotBeNull();
            result.Reason!.ShouldNotContain(Token);
            result.Reason!.ShouldNotContain("   at ");
        }
    }

    [Fact]
    public async Task FetchesTheCatalogFromTheContentRoot()
    {
        var handler = new RecordingHandler(Ok("""{ "documents": [] }"""));

        var json = await Client(handler).GetCatalogAsync(TestContext.Current.CancellationToken);

        json.ShouldBe("""{ "documents": [] }""");
        handler.LastRequest!.RequestUri!.ToString().ShouldContain("contents/docs/index.json");
    }

    [Fact]
    public async Task ThrowsWhenTheCatalogCannotBeFetched()
    {
        var client = Client(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var exception = await Should.ThrowAsync<ContentUnavailableException>(
            () => client.GetCatalogAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("docs/index.json");
    }

    private static HttpResponseMessage Ok(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static GitHubContentClient Client(
        RecordingHandler handler,
        Action<GitHubContentOptions>? configure = null)
    {
        var options = new GitHubContentOptions();
        configure?.Invoke(options);

        return new GitHubContentClient(
            new HttpClient(handler),
            new StaticOptionsMonitor<GitHubContentOptions>(options),
            NullLogger<GitHubContentClient>.Instance);
    }

    /// <summary>
    /// Records the request and returns a scripted response, so the composed URI and headers
    /// can be asserted with no network access.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(HttpResponseMessage response) => _respond = _ => response;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}

/// <summary>An options monitor over a fixed value.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
