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

        var command = args[0].Trim().ToLowerInvariant();
        if (command is not ("scan" or "mechanics"))
        {
            Console.Error.WriteLine("Unknown command. Expected 'scan' or 'mechanics'.");
            PrintUsage();
            return 2;
        }

        try
        {
            var source = command == "mechanics" ? "Local RebornBuddy Plugins" : "Local RebornBuddy Profiles";
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

            return command == "mechanics"
                ? await RunMechanicScanAsync(source, output, roots).ConfigureAwait(false)
                : await RunRouteScanAsync(source, output, roots).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reference scan failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunRouteScanAsync(string source, string? output, IReadOnlyList<string> roots)
    {
        output ??= Path.Combine(Environment.CurrentDirectory, "EZBuddy-route-reference-manifest.json");
        output = Path.GetFullPath(output);

        var scanner = new RouteReferenceScanner();
        var manifest = scanner.Scan(source, roots);
        var json = RouteReferenceScanner.Serialize(manifest);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, json).ConfigureAwait(false);

        Console.WriteLine($"Scanned route source: {manifest.SourceLabel}");
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

    private static async Task<int> RunMechanicScanAsync(string source, string? output, IReadOnlyList<string> roots)
    {
        output ??= Path.Combine(Environment.CurrentDirectory, "EZBuddy-plugin-mechanic-reference-manifest.json");
        output = Path.GetFullPath(output);

        var scanner = new PluginMechanicReferenceScanner();
        var manifest = scanner.Scan(source, roots);
        var json = PluginMechanicReferenceScanner.Serialize(manifest);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, json).ConfigureAwait(false);

        Console.WriteLine($"Scanned mechanic source: {manifest.SourceLabel}");
        Console.WriteLine($"Mechanic-bearing files: {manifest.MechanicLikeFileCount:N0}");
        Console.WriteLine($"Manifest: {output}");
        Console.WriteLine("Clean-room boundary: coordinates, vectors, destinations, source bodies, and executable mechanic implementations were not exported.");
        return 0;
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
        Console.WriteLine("EZBuddy Reference Scanner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner scan --source <label> --out <routes.json> --root <folder> [--root <folder> ...]");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner mechanics --source <label> --out <mechanics.json> --root <folder> [--root <folder> ...]");
        Console.WriteLine();
        Console.WriteLine("Example full RebornBuddy sweep:");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner scan --source \"My RebornBuddy folders\" --out routes.json --root \"C:\\RebornBuddy\\Profiles\" --root \"C:\\RebornBuddy\\Plugins\" --root \"C:\\RebornBuddy\\BotBases\"");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner mechanics --source \"My RebornBuddy folders\" --out mechanics.json --root \"C:\\RebornBuddy\\Profiles\" --root \"C:\\RebornBuddy\\Plugins\" --root \"C:\\RebornBuddy\\BotBases\"");
        Console.WriteLine();
        Console.WriteLine("Both commands inventory supported .xml/.json/.cs sources without exporting waypoint coordinates or raw source bodies.");
    }
}
