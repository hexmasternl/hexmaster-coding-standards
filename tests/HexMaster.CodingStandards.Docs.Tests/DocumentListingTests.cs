using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// The listing projection: five fields, no bodies, deterministic order.
/// </summary>
public class DocumentListingTests
{
    [Fact]
    public void AnEntryCarriesExactlyFiveFields()
    {
        // Asserted structurally rather than by reading the record's definition, so adding a
        // sixth field - status especially - fails here instead of quietly widening the
        // payload every client sees.
        typeof(DocumentListEntry)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Category", "Description", "Id", "Tags", "Title"]);
    }

    [Fact]
    public void ProjectsEveryFieldFromTheCatalogEntry()
    {
        var listing = Listing(
            Entry("a-decision", "A decision", "ADR", "docs/ADR/a-decision.md",
                description: "Decides a thing.", tags: "\"caching\", \"performance\""));

        var entry = listing.ShouldHaveSingleItem();
        entry.Id.ShouldBe("a-decision");
        entry.Title.ShouldBe("A decision");
        entry.Category.ShouldBe(DocumentCategory.Adr);
        entry.Description.ShouldBe("Decides a thing.");
        entry.Tags.ShouldBe(["caching", "performance"]);
    }

    [Fact]
    public void OrdersByCategoryThenId()
    {
        var listing = Listing(
            Entry("z-structure", "Z structure", "Structure", "docs/Structures/z.md"),
            Entry("b-design", "B design", "Design", "docs/Designs/b.md"),
            Entry("a-design", "A design", "Design", "docs/Designs/a.md"),
            Entry("m-decision", "M decision", "ADR", "docs/ADR/m.md"));

        listing.Select(entry => entry.Id)
            .ShouldBe(["m-decision", "a-design", "b-design", "z-structure"]);
    }

    [Fact]
    public void RepeatedProjectionsAreIdentical()
    {
        var set = SetOf(
            Entry("a-decision", "A decision", "ADR", "docs/ADR/a.md"),
            Entry("a-design", "A design", "Design", "docs/Designs/a.md"));

        // Records compare structurally, so this is the payload being byte-identical: a client
        // can diff or cache two calls over the same catalog.
        set.Listing().ShouldBe(set.Listing());
    }

    [Fact]
    public void ADocumentWithNoTagsCarriesAnEmptyArray()
    {
        var entry = Listing(Entry("a-decision", "A decision", "ADR", "docs/ADR/a.md"))
            .ShouldHaveSingleItem();

        entry.Tags.ShouldNotBeNull();
        entry.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void AnEmptyCatalogProjectsAnEmptyListing()
    {
        // Empty is an answer, and distinct from no catalog at all.
        Listing().ShouldBeEmpty();
    }

    [Fact]
    public void AnInvalidEntryIsAbsentAndDoesNotFailTheProjection()
    {
        var listing = Listing(
            Entry("valid", "Valid", "ADR", "docs/ADR/valid.md"),
            Entry("invalid", "Invalid", "Nonsense", "docs/ADR/invalid.md"));

        listing.Select(entry => entry.Id).ShouldBe(["valid"]);
    }

    [Fact]
    public void TheIndexStillCarriesStatus()
    {
        // The listing drops status; the service's own index must not, or a caller that needs
        // to tell an accepted standard from a superseded one has nowhere left to look.
        var set = SetOf(Entry("a-decision", "A decision", "ADR", "docs/ADR/a.md"));

        set.Index().ShouldHaveSingleItem().Status.ShouldBe(DocumentStatus.Accepted);
    }

    private static IReadOnlyList<DocumentListEntry> Listing(params string[] entries) =>
        SetOf(entries).Listing();

    private static DocumentSet SetOf(params string[] entries) =>
        DocumentSet.FromCatalogJson(
            DocumentServiceTests.CatalogJson(entries),
            NullLogger.Instance,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-09-02T10:00:00Z", null)));

    private static string Entry(
        string id,
        string title,
        string category,
        string path,
        string description = "A description of the document.",
        string tags = "") =>
        DocumentServiceTests.EntryJson(id, title, category, path, description, tags: tags);
}
