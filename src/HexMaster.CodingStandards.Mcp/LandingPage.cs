namespace HexMaster.CodingStandards.Mcp;

/// <summary>
/// What a browser gets when somebody opens the server's address instead of pointing an MCP
/// client at it.
/// </summary>
/// <remarks>
/// This cannot be a mapped route. <c>MapMcp()</c> owns <c>GET /</c> as the streamable HTTP
/// transport's event stream, and two endpoints on one pattern and method is an ambiguous
/// match - a request-time exception, not a build error. So it runs as middleware ahead of
/// routing and only answers requests that are clearly not MCP: the protocol requires a
/// client's <c>GET</c> to list <c>text/event-stream</c> in <c>Accept</c>, and a browser
/// asking for a page never does. Anything else falls through untouched.
///
/// The page is a constant rather than a file under <c>wwwroot</c>. It costs no static file
/// middleware, no file system read on a cold start, and nothing extra in the image - and it
/// has no external references at all, so it renders the same offline as it does behind
/// whatever CSP a client's browser applies.
/// </remarks>
internal static class LandingPage
{
    /// <summary>
    /// The repository the standards themselves live in, and the only link on the page.
    /// </summary>
    internal const string RepositoryUrl = "https://github.com/hexmasternl/hexmaster-coding-standards";

    /// <summary>
    /// The page, complete and self-contained.
    /// </summary>
    /// <remarks>
    /// A <c>$$</c> raw literal, so <see cref="RepositoryUrl"/> appears once rather than
    /// twice: interpolation is <c>{{...}}</c> here, which leaves the CSS braces alone as
    /// long as no two of them ever end up adjacent.
    /// </remarks>
    internal const string Html =
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>HexMaster Coding Standards &middot; MCP server</title>
        <style>
          :root {
            color-scheme: light dark;
            --bg: #f7f7f8;
            --panel: #ffffff;
            --ink: #16181d;
            --muted: #5d636e;
            --line: #e3e4e8;
            --accent: #2f5bea;
          }

          @media (prefers-color-scheme: dark) {
            :root {
              --bg: #0f1115;
              --panel: #171a21;
              --ink: #eceef2;
              --muted: #9aa1ad;
              --line: #262a33;
              --accent: #7f9cff;
            }
          }

          * { box-sizing: border-box; }

          body {
            margin: 0;
            min-height: 100vh;
            display: grid;
            place-items: center;
            padding: 2rem 1.25rem;
            background: var(--bg);
            color: var(--ink);
            font: 16px/1.6 ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto,
                  "Helvetica Neue", Arial, sans-serif;
            -webkit-font-smoothing: antialiased;
          }

          main {
            width: 100%;
            max-width: 40rem;
            background: var(--panel);
            border: 1px solid var(--line);
            border-radius: 14px;
            padding: 2.5rem;
          }

          .eyebrow {
            margin: 0 0 .75rem;
            font-size: .75rem;
            font-weight: 600;
            letter-spacing: .12em;
            text-transform: uppercase;
            color: var(--muted);
          }

          h1 {
            margin: 0 0 1rem;
            font-size: clamp(1.5rem, 4vw, 2rem);
            line-height: 1.2;
            letter-spacing: -.02em;
          }

          p { margin: 0 0 1rem; color: var(--muted); }

          p.lead { color: var(--ink); }

          code {
            font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas,
                         "Liberation Mono", monospace;
            font-size: .875em;
            background: var(--bg);
            border: 1px solid var(--line);
            border-radius: 5px;
            padding: .1em .4em;
          }

          a { color: var(--accent); }

          .repo {
            display: inline-flex;
            align-items: center;
            gap: .5rem;
            margin-top: .75rem;
            padding: .7rem 1.1rem;
            border: 1px solid var(--line);
            border-radius: 9px;
            font-weight: 550;
            text-decoration: none;
            color: var(--ink);
            transition: border-color .15s ease, transform .15s ease;
          }

          .repo:hover { border-color: var(--accent); transform: translateY(-1px); }

          .repo svg { width: 1.15em; height: 1.15em; fill: currentColor; }

          footer {
            margin-top: 2rem;
            padding-top: 1.25rem;
            border-top: 1px solid var(--line);
            font-size: .875rem;
            color: var(--muted);
          }
        </style>
        </head>
        <body>
        <main>
          <p class="eyebrow">Model Context Protocol server</p>
          <h1>You&rsquo;ve reached the HexMaster Coding Standards MCP server.</h1>
          <p class="lead">
            There is nothing to browse here. This address serves HexMaster&rsquo;s coding
            standards &mdash; architecture decisions, designs and conventions &mdash; to MCP
            clients rather than to browsers.
          </p>
          <p>
            To use it, point an MCP client at this same URL over HTTP. The standards
            themselves are markdown documents in the repository, and the server reads them
            from there at runtime, so publishing one is a merge rather than a deployment.
          </p>
          <a class="repo" href="{{RepositoryUrl}}">
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.4 7.4 0 0 1 2-.27c.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A7.995 7.995 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/></svg>
            hexmasternl/hexmaster-coding-standards
          </a>
          <!-- The period sits against the closing tag: a newline there collapses to a
               space and the sentence renders as "/health ." -->
          <footer>Service health is at <code>/health</code>.</footer>
        </main>
        </body>
        </html>
        """;

    /// <summary>
    /// Serves <see cref="Html"/> for a browser-style <c>GET /</c>, and leaves every other
    /// request alone.
    /// </summary>
    internal static IApplicationBuilder UseLandingPage(this IApplicationBuilder app)
    {
        return app.Use(static async (context, next) =>
        {
            if (!IsBrowserRequestForRoot(context.Request))
            {
                await next(context);
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(Html, context.RequestAborted);
        });
    }

    /// <summary>
    /// Whether a request is somebody opening the address in a browser rather than an MCP
    /// client opening the transport's event stream.
    /// </summary>
    private static bool IsBrowserRequestForRoot(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) || request.Path != "/")
        {
            return false;
        }

        // The discriminator is Accept, because the path and method are identical. The
        // protocol obliges an MCP client's GET to list text/event-stream; treating an
        // absent or unparseable header as "not MCP" is the safe way round, because the
        // wrong answer for a browser is a protocol error it cannot read, while the wrong
        // answer for a non-conforming client is a page and a 200.
        foreach (var accepted in request.Headers.Accept)
        {
            if (accepted?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
            {
                return false;
            }
        }

        return true;
    }
}
