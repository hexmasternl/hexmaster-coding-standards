using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// Builds gzipped tarballs shaped like a GitHub repository archive, so extraction is tested
/// against the real format rather than a stand-in.
/// </summary>
internal sealed class TarGzBuilder
{
    /// <summary>The top-level folder GitHub wraps every repository archive in.</summary>
    public const string RepositoryRoot = "hexmasternl-hexmaster-coding-standards-a1b2c3d";

    private readonly List<(string Name, TarEntryType Type, string? Content, string? LinkName)> _entries = [];

    /// <summary>Adds a regular file at a path relative to the archive's top-level folder.</summary>
    public TarGzBuilder WithFile(string relativePath, string content)
    {
        _entries.Add(($"{RepositoryRoot}/{relativePath}", TarEntryType.RegularFile, content, null));
        return this;
    }

    /// <summary>Adds a regular file at a raw archive path, bypassing the top-level folder.</summary>
    public TarGzBuilder WithRawEntry(string archivePath, string content)
    {
        _entries.Add((archivePath, TarEntryType.RegularFile, content, null));
        return this;
    }

    /// <summary>Adds a symbolic link entry.</summary>
    public TarGzBuilder WithSymbolicLink(string relativePath, string target)
    {
        _entries.Add(($"{RepositoryRoot}/{relativePath}", TarEntryType.SymbolicLink, null, target));
        return this;
    }

    /// <summary>Adds a directory entry, which extraction should ignore rather than refuse.</summary>
    public TarGzBuilder WithDirectory(string relativePath)
    {
        _entries.Add(($"{RepositoryRoot}/{relativePath}", TarEntryType.Directory, null, null));
        return this;
    }

    /// <summary>Writes the archive to a fresh, readable stream positioned at the start.</summary>
    public Stream Build()
    {
        var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, type, content, linkName) in _entries)
            {
                var entry = new PaxTarEntry(type, name);

                if (linkName is not null)
                {
                    entry.LinkName = linkName;
                }

                if (content is not null)
                {
                    entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                }

                writer.WriteEntry(entry);
            }
        }

        buffer.Position = 0;
        return buffer;
    }
}
