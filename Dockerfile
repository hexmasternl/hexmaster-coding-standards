# Framework-dependent linux-x64 build on a chiselled ASP.NET base image.
#
# The base image supplies the runtime, so this deliberately does NOT publish self-contained
# or single-file: that would only inflate the image and lengthen the cold start that
# scale-to-zero already makes visible.
#
# No /docs content is copied in. The server downloads the coding standards from GitHub at
# runtime, so publishing a standard is a merge to main rather than an image build.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH=amd64
WORKDIR /source

# Restore against the project files alone, so a source-only change reuses the package layer.
COPY global.json Directory.Build.props ./
COPY src/HexMaster.CodingStandards.Mcp/HexMaster.CodingStandards.Mcp.csproj src/HexMaster.CodingStandards.Mcp/
COPY src/HexMaster.CodingStandards.Docs/HexMaster.CodingStandards.Docs.csproj src/HexMaster.CodingStandards.Docs/
RUN dotnet restore src/HexMaster.CodingStandards.Mcp/HexMaster.CodingStandards.Mcp.csproj \
    --runtime linux-x64

COPY src/ src/
RUN dotnet publish src/HexMaster.CodingStandards.Mcp/HexMaster.CodingStandards.Mcp.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --no-self-contained \
    --no-restore \
    --output /app

# Chiselled: no shell, no package manager, and a non-root user by default - a small
# attack surface for a publicly reachable endpoint.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .

# Container Apps ingress terminates TLS and forwards plain HTTP to this port.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "HexMaster.CodingStandards.Mcp.dll"]
