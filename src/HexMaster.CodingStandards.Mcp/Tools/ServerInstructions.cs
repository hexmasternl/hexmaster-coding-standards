namespace HexMaster.CodingStandards.Mcp.Tools;

/// <summary>
/// What the server says to a client during the MCP initialization handshake.
/// </summary>
/// <remarks>
/// The only text this server sends unsolicited. Clients typically inject it into the model's
/// system prompt, so it is paid for on every connect, in every session, including the many
/// that never mention a coding standard - which is why the budget below is a tested
/// requirement rather than an aspiration.
///
/// It sits beside <see cref="SkillAuthoringInstructions"/> on purpose. The two split by cost:
/// this text says <b>when and where</b> to generate skills and is always resident, while the
/// tool response says <b>what each skill must contain</b> and is paid for only once an agent
/// has committed to generating them. Their failure mode is drift - this text naming a
/// workflow the tool no longer describes - and proximity is the cheap defence.
///
/// Like the tool's instructions, this is reviewed as an interface: it causes agents to write
/// files into other people's repositories, and a wording change takes effect everywhere on
/// the next connect with no version bump and no signal to any client.
/// </remarks>
internal static class ServerInstructions
{
    /// <summary>
    /// The most characters the instructions may occupy.
    /// </summary>
    /// <remarks>
    /// A number rather than "keep it brief", because a budget that is not measured loses
    /// every argument with a good sentence somebody wants to add. Adding a paragraph means
    /// deciding what comes out.
    /// </remarks>
    public const int MaximumLength = 2000;

    /// <summary>
    /// The client skill conventions named in the text, as verified on 2 September 2026.
    /// </summary>
    /// <remarks>
    /// Naming other projects' paths is a bet that their conventions hold, and it is a bet
    /// that loses eventually - Cursor's has already moved once, from a single
    /// <c>.cursorrules</c> file which its current documentation no longer describes. Recorded
    /// with a date so the next person editing the text knows how old the mapping is; the
    /// fallback clause in the text is what keeps a stale row from becoming a wrong answer.
    ///
    /// <list type="bullet">
    /// <item>
    /// <b>Claude Code</b> - a project skill is <c>.claude/skills/&lt;name&gt;/SKILL.md</c>,
    /// YAML frontmatter plus a markdown body. Every frontmatter field is optional:
    /// <c>description</c> is recommended so Claude knows when to load the skill, and
    /// <c>name</c> defaults to the directory name.
    /// </item>
    /// <item>
    /// <b>GitHub Copilot</b> - path-specific instructions are
    /// <c>.github/instructions/&lt;name&gt;.instructions.md</c>, and <c>applyTo</c>
    /// frontmatter carrying glob patterns is still the mechanism.
    /// </item>
    /// <item>
    /// <b>Cursor</b> - project rules are <c>.cursor/rules/&lt;name&gt;.mdc</c>, with
    /// <c>description</c>, <c>globs</c>, and <c>alwaysApply</c> frontmatter. A plain
    /// <c>.md</c> file in that directory is ignored.
    /// </item>
    /// </list>
    ///
    /// Only the locations reach the text. The frontmatter shapes are recorded here rather
    /// than instructed there: the consuming agent knows its own client's fields, and spending
    /// the budget on three schemas that each drift independently buys nothing.
    /// </remarks>
    public const string Text = """
        HexMaster's coding standards: architecture decision records, coding designs, and file
        and project structure standards, served as markdown from a public GitHub repository.

        Workflow: find a standard through the catalog - by listing it or by tag - to get its
        id, then retrieve that document by id when you need its full text.

        On first use in a workspace: if no skills sourced from this server are present, call
        recommend_skills. It returns candidate standards and the rules for writing skills from
        them. Judge every candidate against the repository you are working in and write only
        the ones that apply to it. Where such skills already exist, do not generate them again
        - this is a once-per-workspace bootstrap, not something to do on every connection or
        in every session.

        This server returns content only. It has no access to your filesystem and leaves
        nothing behind on it; you write every file yourself, with your own tools.

        Write skills only inside your client's conventional skills location, and nowhere else
        in the repository. Conventional locations include .claude/skills/<name>/SKILL.md for
        Claude Code, .github/instructions/<name>.instructions.md for GitHub Copilot, and
        .cursor/rules/<name>.mdc for Cursor. If your client is not one of these, or its
        convention has moved on, use your client's own current convention instead.

        Before writing, say which skills you are generating and where. Once, up front - do not
        ask for approval for each skill.
        """;
}
