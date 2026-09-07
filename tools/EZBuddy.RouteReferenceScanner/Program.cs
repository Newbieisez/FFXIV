using System.Text.Json;
using EZBuddy.Core.Research;

return await RouteReferenceScannerProgram.RunAsync(args);

internal static class RouteReferenceScannerProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Any(argument => argument is "-h" or "--help" or "help"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command is not ("scan" or "mechanics" or "matrix"))
        {
            Console.Error.WriteLine("Unknown command. Expected 'scan', 'mechanics', or 'matrix'.");
            PrintUsage();
            return 2;
        }

        try
        {
            return command == "matrix"
                ? await RunMatrixCommandAsync(args).ConfigureAwait(false)
                : await RunScanCommandAsync(command, args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Reference operation failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunScanCommandAsync(string command, string[] args)
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

    private static async Task<int> RunMatrixCommandAsync(string[] args)
    {
        string? manifestDirectory = null;
        string? output = null;
        string? csvOutput = null;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--manifest-dir":
                    manifestDirectory = RequireValue(args, ref index, "--manifest-dir");
                    break;
                case "--out":
                    output = RequireValue(args, ref index, "--out");
                    break;
                case "--csv":
                    csvOutput = RequireValue(args, ref index, "--csv");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(manifestDirectory))
        {
            throw new ArgumentException("--manifest-dir is required for the matrix command.");
        }

        var directory = Path.GetFullPath(manifestDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Manifest directory does not exist: {directory}");
        }

        var routes = new List<RouteReferenceManifest>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.routes.json", SearchOption.TopDirectoryOnly))
        {
            var manifest = JsonSerializer.Deserialize<RouteReferenceManifest>(await File.ReadAllTextAsync(path).ConfigureAwait(false), JsonOptions);
            if (manifest is not null)
            {
                routes.Add(manifest);
            }
        }

        var mechanics = new List<PluginMechanicReferenceManifest>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.mechanics.json", SearchOption.TopDirectoryOnly))
        {
            var manifest = JsonSerializer.Deserialize<PluginMechanicReferenceManifest>(await File.ReadAllTextAsync(path).ConfigureAwait(false), JsonOptions);
            if (manifest is not null)
            {
                mechanics.Add(manifest);
            }
        }

        if (routes.Count == 0)
        {
            throw new InvalidDataException("No *.routes.json manifests were found.");
        }

        var matrix = new DutyCoverageMatrixBuilder().Build(routes, mechanics);
        output ??= Path.Combine(directory, "EZBuddy-duty-coverage-matrix.json");
        csvOutput ??= Path.Combine(directory, "EZBuddy-duty-coverage-matrix.csv");
        output = Path.GetFullPath(output);
        csvOutput = Path.GetFullPath(csvOutput);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Directory.CreateDirectory(Path.GetDirectoryName(csvOutput)!);
        await File.WriteAllTextAsync(output, DutyCoverageMatrixBuilder.SerializeJson(matrix)).ConfigureAwait(false);
        await File.WriteAllTextAsync(csvOutput, DutyCoverageMatrixBuilder.SerializeCsv(matrix)).ConfigureAwait(false);

        Console.WriteLine($"Duty coverage rows: {matrix.DutyCount:N0}");
        Console.WriteLine($"Rows with multiple reference sources: {matrix.WithMultipleSources:N0}");
        Console.WriteLine($"Rows with mechanic evidence: {matrix.WithMechanicEvidence:N0}");
        Console.WriteLine($"JSON matrix: {output}");
        Console.WriteLine($"CSV matrix: {csvOutput}");
        Console.WriteLine("Matrix remains reference-only: third-party coordinates and executable mechanic logic are not imported.");
        return 0;
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
        Console.WriteLine("  EZBuddy.RouteReferenceScanner matrix --manifest-dir <folder> [--out <matrix.json>] [--csv <matrix.csv>]");
        Console.WriteLine();
        Console.WriteLine("Example full RebornBuddy sweep:");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner scan --source \"My RebornBuddy folders\" --out routes.json --root \"C:\\RebornBuddy\\Profiles\" --root \"C:\\RebornBuddy\\Plugins\" --root \"C:\\RebornBuddy\\BotBases\"");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner mechanics --source \"My RebornBuddy folders\" --out mechanics.json --root \"C:\\RebornBuddy\\Profiles\" --root \"C:\\RebornBuddy\\Plugins\" --root \"C:\\RebornBuddy\\BotBases\"");
        Console.WriteLine("  EZBuddy.RouteReferenceScanner matrix --manifest-dir . --out duty-matrix.json --csv duty-matrix.csv");
        Console.WriteLine();
        Console.WriteLine("All commands preserve the clean-room boundary: no third-party waypoint coordinates or executable source bodies are exported into EZBuddy route data.");
    }
}
