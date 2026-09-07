using EZBuddy.Core.Research;

return await RouteReferenceScannerProgram.RunAsync(args);

internal static class RouteReferenceScannerProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Any(argument => argument is "-h" or "--help" or "help"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        if (!string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Unknown command. Expected 'scan'.");
            PrintUsage();
            return 2;
        }

        try
        {
            var source = "Local RebornBuddy Profiles";
            string? output = null;
            var roots = new List<string>();

            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--source":
                        source = RequireValue(args, ref index, "--source");
                        break;
                    case "--out":
                        output = RequireValue(args, ref index, "--out");
                        break;
                    case "--root":
                        roots.Add(RequireValue(args, ref index, "--root"));
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[index]}'.");
                }
            }

            if (roots.Count == 0)
            {
                throw new ArgumentException("At least one --root folder is required.");
            }

            output ??= Path.Combine(Environment.CurrentDirectory, "EZBuddy-route-reference-manifest.json");
            output = Path.GetFullPath(output);

            var scanner = new RouteReferenceScanner();
            var manifest = scanner.Scan(source, roots);
            var json = RouteReferenceScanner.Serialize(manifest);

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, json).ConfigureAwait(false);

            Console.WriteLine($"Scanned source: {manifest.SourceLabel}");
            Console.WriteLine($"Eligible source files: {manifest.FileCount:N0}");
            Console.WriteLine($"Files with route-like nodes: {manifest.RouteLikeFileCount:N0}");
            foreach (var group in manifest.Entries.GroupBy(entry => entry.Category).OrderBy(group => group.Key))
            {
                Console.WriteLine($"  {group.Key}: {group.Count():N0}");
            }

            Console.WriteLine($"Manifest: {output}");
            Console.WriteLine("Clean-room boundary: waypoint coordinates and raw source bodies were not exported.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Route reference scan failed: {exception.Message}");
            return 1;
        }
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine("EZBuddy Route Reference Scanner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner scan --source <label> --out <manifest.json> --root <folder> [--root <folder> ...]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner scan --source \"My RebornBuddy folders\" --out route-manifest.json --root \"C:\\RebornBuddy\\Profiles\" --root \"C:\\RebornBuddy\\Plugins\"");
        Console.WriteLine();
        Console.WriteLine("The scanner inventories supported .xml/.json/.cs sources but never exports waypoint coordinates or raw profile bodies.");
    }
}
