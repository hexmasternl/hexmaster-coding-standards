namespace HexMaster.CodingStandards.Mcp.Tools;

/// <summary>
/// What <see cref="RecommendSkillsTool"/> tells a calling agent to do with the candidates.
/// </summary>
/// <remarks>
/// This is a prompt shipped inside a server, and it determines what downstream agents write
/// into other people's repositories. Editing it changes behaviour everywhere with no version
/// bump, no schema change, and no signal to any client, so it is reviewed as an interface
/// rather than as a string constant - see the <c>mcp-server-recommended-skills-tool</c>
/// design, "The instruction text is an interface".
///
/// Two properties of the text are load-bearing and easy to lose in an edit:
///
/// <list type="bullet">
/// <item>
/// It is <b>ordered as a procedure</b> - assess the environment, decide, retrieve, write -
/// rather than as a list of qualities a good skill has. A model given qualities writes one
/// skill per candidate; a model given steps performs the relevance step, which is the whole
/// reason the tool returns everything eligible instead of pre-filtering.
/// </item>
/// <item>
/// It <b>prescribes content and never format</b>. No file format, extension, frontmatter
/// schema, serialisation, or directory path appears here, because the consuming client knows
/// its own skill convention and this server does not. Naming one produces output the client
/// cannot load. The four content elements are what stays mandatory.
/// </item>
/// </list>
///
/// <c>SkillAuthoringInstructionTests</c> asserts that the load-bearing elements are present
/// and that no format is named, deliberately on presence rather than exact wording, so an
/// editorial improvement is not a test failure.
/// </remarks>
internal static class SkillAuthoringInstructions
{
    /// <summary>The name of the tool that returns a document's full text by id.</summary>
    /// <remarks>
    /// Named in two places in the text - once to retrieve the documents that were kept, and
    /// once inside the back-reference every generated skill must carry - so it is stated
    /// once here. If the retrieval tool is ever renamed, this is the single edit that keeps
    /// every previously generated back-reference's replacement accurate.
    /// </remarks>
    public const string RetrievalToolName = "get_document";

    /// <summary>How this server is named in a generated skill's back-reference.</summary>
    public const string ServerName = "HexMaster coding standards MCP server";

    /// <summary>
    /// The procedure, appended to the candidate list to form the tool's whole response.
    /// </summary>
    public const string Text = $"""
        # Turning these standards into skills

        Each candidate above is a HexMaster coding standard that could become a durable skill
        in the repository you are working in. Your job is to decide which ones should, and to
        write those. Work through the four steps below in order - the first two are what make
        the result worth having.

        ## 1. Assess the development environment first

        Before writing anything, examine the codebase you are working in and establish what
        it actually is: which languages and frameworks it uses, how its projects and folders
        are laid out, and which concerns it genuinely has - whether it has a user interface,
        an HTTP API, messaging, a database, deployment automation, a test suite. The candidate
        list above tells you none of this, and you cannot judge a candidate without it.

        ## 2. Decide, candidate by candidate

        Keep a candidate only when this codebase can actually exercise the standard it
        describes. Fewer relevant skills beat complete coverage.

        - **Skip any candidate that does not apply to this environment.** A skill for a
          standard this codebase cannot exercise should not be written. It is redundant, it
          competes for attention with the skills that do apply, and it teaches whatever agent
          reads the skill set that skill descriptions are not worth trusting. A standard
          about user interface styling is not worth writing for a repository with no user
          interface; one about eventual consistency is not worth writing for a repository
          with no messaging.
        - **Skip any candidate that describes how to author a document rather than stating a
          standard.** Some entries are authoring templates: they set out the expected shape
          of a document, usually saying so in their title or description. They carry no
          guidance about code and make poor skills.
        - **Treat a candidate whose status is `draft` as provisional, not authoritative.** It
          is a standard still being settled and may yet be settled differently. Keep it if it
          is relevant, and say in the skill that the standard is a draft.
        - Judge relevance from the candidate's title, description, and tags. That is what
          they are there for.

        ## 3. Retrieve the documents you kept

        This response carries metadata only - no document content. For each candidate you
        decided to keep, call the `{RetrievalToolName}` tool with that candidate's `id` to
        obtain the standard's full text, and write the skill from that text.

        Do not retrieve the candidates you discarded. Fetching only what survived step 2 is
        the point of doing step 2 first.

        ## 4. Write one skill per document you kept

        Every skill you write carries all four of the following. None of them is optional.

        1. **An identifier derived from the source document**, so the skill is traceable to
           the standard it came from.
        2. **A concise description stating when the skill applies** - the circumstances that
           should bring it into play, phrased so an agent can match it against the task in
           front of it. "Applies when adding or changing an HTTP endpoint" is useful; "about
           API design" is not, because it says what the document is about instead of when to
           reach for it.
        3. **Content distilled from the document's actual guidance** - the rules, decisions,
           and constraints the standard states, written so an agent can follow them without
           reading the original. Not a restatement of the title or description, and not a
           summary of what the document covers. Distilled rather than copied whole: the
           content is allowed to be incomplete precisely because the fourth element makes the
           complete text retrievable on demand.
        4. **A back-reference to the source.** State that the complete standard can be
           retrieved from the {ServerName} through its `{RetrievalToolName}` tool, and give
           the source document's `id`. This exists so that an agent using the skill later can
           retrieve the whole document when the distilled content is not enough to settle the
           question in front of it - and so the skill stays anchored to the authoritative
           document rather than becoming a copy that quietly drifts as the standard changes.

        How a skill is encoded and where it is placed follow the conventions of the client you
        are running in - use the shape that client already loads skills from. This response
        deliberately does not prescribe that, because your client's convention is something
        you know and this server does not. That choice does not affect the four content
        elements above, which remain mandatory whatever shape you write them in.
        """;
}
