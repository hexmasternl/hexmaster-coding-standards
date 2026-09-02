# HexMaster.CodingStandards.Mcp

The MCP server that serves HexMaster's coding standards over the Model Context Protocol.

This project is the protocol edge: HTTP transport, dependency-injection composition, the
`Tools/` folder, and `GET /health`. Everything to do with the documents themselves —
downloading them from GitHub, caching them, retrieval, the index, keyword search — lives in
`HexMaster.CodingStandards.Docs`.

## Running locally

```powershell
dotnet run --project src/HexMaster.CodingStandards.Mcp
```

The server needs outbound HTTPS: it downloads the documents from
[hexmasternl/hexmaster-coding-standards](https://github.com/hexmasternl/hexmaster-coding-standards)
on startup and refreshes them on an interval. No configuration is required — the defaults
target the public repository at `main`.

Check it is up and has content:

```powershell
curl http://localhost:6094/health
```

`Healthy` means the standards loaded. `Unhealthy` means nothing has loaded yet — usually no
network, a bad `Documents:Ref`, or GitHub being unreachable. Once content has loaded, a later
failed refresh keeps serving the cached copy and health stays green.

## Connecting a client

Point an MCP client at the server's HTTP endpoint:

```json
{
  "servers": {
    "hexmaster-coding-standards": {
      "type": "http",
      "url": "http://localhost:6094"
    }
  }
}
```

Use the `http` URL locally rather than `https`. The dev certificate trips up some MCP clients
(see [microsoft/vscode#248170](https://github.com/microsoft/vscode/issues/248170)), and the
server is designed to sit behind TLS termination anyway — in Azure, Container Apps ingress
handles HTTPS and forwards plain HTTP to the container.

For client-specific setup, see
[Use MCP servers in VS Code](https://code.visualstudio.com/docs/copilot/chat/mcp-servers) or
[Use MCP servers in Visual Studio](https://learn.microsoft.com/visualstudio/ide/mcp-servers).

## Configuration

All settings live under the `Documents` section and are overridable by environment variable
(`Documents__Ref`, and so on), which is how they are supplied in the container.

| Setting | Default | What it does |
| --- | --- | --- |
| `Owner` | `hexmasternl` | GitHub account owning the content repository |
| `Repository` | `hexmaster-coding-standards` | Content repository name |
| `Ref` | `main` | Branch, tag, or commit to serve |
| `CatalogCacheLifetime` | `00:30:00` | How long a loaded catalog is served before a read re-fetches it |
| `BodyCacheLifetime` | `00:30:00` | How long a fetched document body is served from memory |
| `RequestTimeout` | `00:00:30` | Per-request timeout |
| `AccessToken` | none | Optional; raises the GitHub rate limit from 60/hour to 5,000/hour and allows a private repository |

Never commit an access token. Locally, use `dotnet user-secrets set "Documents:AccessToken"
"<token>"`; in Azure, supply it as a container app secret.

## Adding a tool

Add a class to `Tools/` carrying the MCP tool attributes and it is picked up automatically —
`Program.cs` registers tools by assembly scan, so no registration edit is needed. Keep the
tool thin: it should translate an MCP call onto `IDocumentService` and shape the result,
with no document logic of its own.

## Notes

- The MCP transport runs in **stateless** mode deliberately. The container app scales to zero
  with HTTP-based scaling, so consecutive requests from one client can hit different replicas.
- The container listens on port **8080** (`ASPNETCORE_HTTP_PORTS`, set as a `ContainerEnvironmentVariable` in the csproj).
  Locally it uses the ports in `Properties/launchSettings.json`.
- `/health` is unauthenticated, because the Container Apps probes call it.
