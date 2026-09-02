using HexMaster.CodingStandards.Docs.Catalog;

// Validates docs/index.json against the docs tree.
//
// Usage: validate-catalog [repository-root]     (defaults to the current directory)
//
// Exit codes: 0 valid, 1 the catalog and tree disagree, 2 the catalog is missing or unparseable.

var repositoryRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
repositoryRoot = Path.GetFullPath(repositoryRoot);

var catalogPath = Path.Combine(repositoryRoot, DocumentCategories.ContentRoot, "index.json");

if (!File.Exists(catalogPath))
{
    Console.Error.WriteLine($"No catalog at '{catalogPath}'. Pass the repository root as the first argument.");
    return 2;
}

CatalogParseResult parsed;
try
{
    parsed = CatalogParser.Parse(File.ReadAllText(catalogPath));
}
catch (CatalogFormatException exception)
{
    Console.Error.WriteLine($"FAIL  {exception.Message}");
    return 2;
}

var tree = new FileSystemDocumentTree(repositoryRoot);
var result = CatalogValidator.Validate(parsed.Catalog, tree, parsed.Problems);

if (result.IsValid)
{
    Console.WriteLine($"OK    {result.DocumentsValidated} document(s) validated; catalog and docs tree agree.");
    return 0;
}

Console.Error.WriteLine($"FAIL  {result.Problems.Count} catalog problem(s):");
foreach (var problem in result.Problems)
{
    Console.Error.WriteLine($"      [{problem.Kind}] {problem.Message}");
}

return 1;
