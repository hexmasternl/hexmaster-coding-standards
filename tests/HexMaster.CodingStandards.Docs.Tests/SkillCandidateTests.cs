using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// The skill-candidate set: which documents are eligible, what never affects eligibility,
/// ordering, and the outcomes a caller must be able to tell apart.
/// </summary>
/// <remarks>
/// Every test here runs over a fixture catalog against a fake GitHub client - no HTTP
/// handler, no host, no network. The candidate set never leaves the cached catalog, which is
/// what makes that possible; <see cref="TouchesNoNetworkAndReadsNoBody"/> asserts it rather
/// than trusting it.
/// </remarks>
public class SkillCandidateTests
{
    [Fact]
    public async Task ASupersededDocumentIsNotACandidate()
    {
        // The reason the exclusion exists: a skill distilled from a retracted standard
        // teaches a rule nobody follows, and nothing downstream can tell.
        var candidates = await CandidatesOf(
            Entry("current", "Current", "ADR", status: "accepted"),
            Entry("replaced", "Replaced", "ADR", status: "superseded"));

        candidates.Select(candidate => candidate.Id).ShouldBe(["current"]);
    }

    [Fact]
    public async Task ADeprecatedDocumentIsNotACandidate()
    {
        var candidates = await CandidatesOf(
            Entry("current", "Current", "ADR", status: "accepted"),
            Entry("retired", "Retired", "ADR", status: "deprecated"));

        candidates.Select(candidate => candidate.Id).ShouldBe(["current"]);
    }

    [Fact]
    public async Task ADraftDocumentIsACandidateAndReportsItsStatus()
    {
        // A draft can be a real standard still being settled, so it is included - and the
        // caller can only weigh it as provisional if the status comes through.
        var candidates = await CandidatesOf(
            Entry("settled", "Settled", "ADR", status: "accepted"),
            Entry("being-settled", "Being settled", "ADR", status: "draft"));

        candidates.Select(candidate => candidate.Id).ShouldBe(["being-settled", "settled"]);
        candidates.Single(candidate => candidate.Id == "being-settled").Status
            .ShouldBe(DocumentStatus.Draft);
        candidates.Single(candidate => candidate.Id == "settled").Status
            .ShouldBe(DocumentStatus.Accepted);
    }

    [Fact]
    public async Task EveryStatusIsEitherIncludedOrExcludedDeliberately()
    {
        // Asserted over the whole enum so a status added later fails here rather than
        // silently becoming a candidate.
        var candidates = await CandidatesOf(
            Enum.GetValues<DocumentStatus>()
                .Select(status => Entry(
                    DocumentStatuses.CatalogValueFor(status),
                    "A document",
                    "ADR",
                    status: DocumentStatuses.CatalogValueFor(status)))
                .ToArray());

        candidates.Select(candidate => candidate.Id).ShouldBe(["accepted", "draft"]);
    }

    [Fact]
    public async Task CategoryDoesNotAffectEligibility()
    {
        var candidates = await CandidatesOf(
            Entry("a-decision", "A decision", "ADR"),
            Entry("a-design", "A design", "Design"),
            Entry("a-structure", "A structure", "Structure"));

        candidates.Select(candidate => candidate.Id)
            .ShouldBe(["a-decision", "a-design", "a-structure"]);
    }

    [Fact]
    public async Task SubjectMatterTagsAndTitleDoNotAffectEligibility()
    {
        // The server cannot see the caller's codebase, so it cannot know that a frontend
        // styling standard is useless to an API repository. Returning everything is what
        // leaves that judgement where it can actually be made.
        var candidates = await CandidatesOf(
            Entry("frontend-styling", "Frontend styling variables", "Design", tags: "\"css\", \"frontend\""),
            Entry("eventual-consistency", "Eventual consistency", "ADR", tags: "\"messaging\""),
            Entry("adr-template", "ADR template", "Structure", tags: "\"template\""));

        candidates.Select(candidate => candidate.Id)
            .ShouldBe(["eventual-consistency", "frontend-styling", "adr-template"]);
    }

    [Fact]
    public async Task OrdersByCategoryThenId()
    {
        var candidates = await CandidatesOf(
            Entry("z-structure", "Z structure", "Structure"),
            Entry("b-design", "B design", "Design"),
            Entry("a-design", "A design", "Design"),
            Entry("m-decision", "M decision", "ADR"));

        candidates.Select(candidate => candidate.Id)
            .ShouldBe(["m-decision", "a-design", "b-design", "z-structure"]);
    }

    [Fact]
    public async Task TwoRequestsOverTheSameCatalogAreIdentical()
    {
        var context = Catalog(
            Entry("a-decision", "A decision", "ADR"),
            Entry("a-design", "A design", "Design"));

        // Records compare structurally, so this is the payload being identical: a client can
        // diff or cache two calls over the same catalog.
        (await context.CandidatesAsync()).ShouldBe(await context.CandidatesAsync());
    }

    [Fact]
    public async Task EachEligibleDocumentAppearsExactlyOnce()
    {
        var candidates = await CandidatesOf(
            Entry("a-decision", "A decision", "ADR", tags: "\"caching\", \"performance\""),
            Entry("a-design", "A design", "Design", tags: "\"caching\""));

        candidates.Select(candidate => candidate.Id).ShouldBe(["a-decision", "a-design"]);
        candidates.Count.ShouldBe(candidates.Select(candidate => candidate.Id).Distinct().Count());
    }

    [Fact]
    public async Task AnInvalidEntryIsAbsentAndDoesNotFailTheRequest()
    {
        var candidates = await CandidatesOf(
            Entry("valid", "Valid", "ADR"),
            Entry("invalid", "Invalid", "Nonsense"));

        candidates.Select(candidate => candidate.Id).ShouldBe(["valid"]);
    }

    [Fact]
    public async Task ACandidateCarriesItsFullMetadata()
    {
        var candidates = await CandidatesOf(
            Entry("a-decision", "A decision", "ADR",
                description: "Decides a thing.", tags: "\"caching\", \"performance\""));

        var candidate = candidates.ShouldHaveSingleItem();
        candidate.Id.ShouldBe("a-decision");
        candidate.Title.ShouldBe("A decision");
        candidate.Description.ShouldBe("Decides a thing.");
        candidate.Category.ShouldBe(DocumentCategory.Adr);
        candidate.Status.ShouldBe(DocumentStatus.Accepted);
        candidate.Tags.ShouldBe(["caching", "performance"]);
    }

    [Fact]
    public async Task ADocumentWithNoTagsCarriesAnEmptyList()
    {
        var candidate = (await CandidatesOf(Entry("a-decision", "A decision", "ADR", tags: "")))
            .ShouldHaveSingleItem();

        candidate.Tags.ShouldNotBeNull();
        candidate.Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACatalogWhereEverythingIsExcludedSucceedsWithAnEmptySet()
    {
        var context = Catalog(
            Entry("replaced", "Replaced", "ADR", status: "superseded"),
            Entry("retired", "Retired", "ADR", status: "deprecated"));

        var result = await context.Service.GetSkillCandidatesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyCatalogSucceedsWithAnEmptySet()
    {
        var result = await Catalog().Service.GetSkillCandidatesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnloadedCatalogFailsRatherThanReturningNothing()
    {
        // "Nothing is eligible" and "I cannot tell you what is eligible" must never look the
        // same: an agent told the former writes no skills and stops asking.
        var context = new CandidateContext();
        context.Github.WithCatalogFailure();

        var result = await context.Service.GetSkillCandidatesAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.NotReady);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task AStaleCachedCatalogStillYieldsCandidates()
    {
        var context = Catalog(Entry("a-decision", "A decision", "ADR"));
        await context.CandidatesAsync();

        // The cache ages out and the refresh fails; the last good catalog stays in place.
        context.Github.WithCatalogFailure();
        context.Time.Advance(TimeSpan.FromMinutes(20));

        var result = await context.Service.GetSkillCandidatesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Select(candidate => candidate.Id).ShouldBe(["a-decision"]);
    }

    [Fact]
    public async Task ARefreshedCatalogChangesTheCandidateSet()
    {
        var context = Catalog(Entry("a-decision", "A decision", "ADR"));
        (await context.CandidatesAsync()).Select(candidate => candidate.Id).ShouldBe(["a-decision"]);

        context.ReplaceCatalog(
            Entry("a-decision", "A decision", "ADR"),
            Entry("newcomer", "Newcomer", "ADR"));

        (await context.CandidatesAsync()).Select(candidate => candidate.Id)
            .ShouldBe(["a-decision", "newcomer"]);
    }

    [Fact]
    public async Task TouchesNoNetworkAndReadsNoBody()
    {
        var context = Catalog(
            Entry("a-decision", "A decision", "ADR"),
            Entry("a-design", "A design", "Design"));

        // Warm the catalog, then count from there: the request under test is one that finds a
        // current catalog cached, which is every request inside the cache window.
        await context.CandidatesAsync();
        var catalogCalls = context.Github.CatalogCalls;

        await context.CandidatesAsync();

        // No bodies at any point: the set is metadata, and the caller fetches only the
        // documents it decides to keep.
        context.Github.TotalBodyCalls.ShouldBe(0);
        context.Github.CatalogCalls.ShouldBe(catalogCalls);
    }

    private static async Task<IReadOnlyList<DocumentSummary>> CandidatesOf(params string[] entries) =>
        await Catalog(entries).CandidatesAsync();

    private static CandidateContext Catalog(params string[] entries)
    {
        var context = new CandidateContext();
        context.Github.WithCatalog(DocumentServiceTests.CatalogJson(entries));
        return context;
    }

    private static string Entry(
        string id,
        string title,
        string category,
        string description = "A description of the document.",
        string status = "accepted",
        string tags = "") =>
        DocumentServiceTests.EntryJson(
            id,
            title,
            category,
            $"docs/ADR/{id}.md",
            description,
            status,
            tags);

    /// <summary>
    /// Assembles the service over the fake GitHub client and a controllable clock. The
    /// candidate set needs no HTTP handler of any kind - it never leaves the cached catalog.
    /// </summary>
    private sealed class CandidateContext
    {
        public CandidateContext()
        {
            Github = new FakeGitHubContentClient();
            Time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-02T10:00:00Z", null));
            Cache = new DocumentSetCache();

            var options = new StaticOptionsMonitor<GitHubContentOptions>(new GitHubContentOptions());
            var bodyCache = new DocumentBodyCache(Github, options, Time);

            var loader = new CatalogLoader(
                Github,
                Cache,
                bodyCache,
                options,
                Time,
                NullLogger<CatalogLoader>.Instance);

            Service = new DocumentService(Cache, bodyCache, loader, NullLogger<DocumentService>.Instance);
        }

        public FakeGitHubContentClient Github { get; }

        public FakeTimeProvider Time { get; }

        public DocumentSetCache Cache { get; }

        public IDocumentService Service { get; }

        /// <summary>Swaps the cached catalog, as a refresh would.</summary>
        public void ReplaceCatalog(params string[] entries) =>
            Cache.Replace(DocumentSet.FromCatalogJson(
                DocumentServiceTests.CatalogJson(entries),
                NullLogger.Instance,
                Time));

        public async Task<IReadOnlyList<DocumentSummary>> CandidatesAsync()
        {
            var result = await Service.GetSkillCandidatesAsync(TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            return result.Value!;
        }
    }
}
