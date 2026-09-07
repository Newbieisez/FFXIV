using EZBuddy.Core.Research;

namespace EZBuddy.Core.Tests;

public sealed class RouteReferenceScannerTests
{
    [Fact]
    public void XmlProfile_ExtractsIdsAndCountsWithoutCoordinates()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "Dungeons", "Sastasha.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            <Profile DutyId="4" ZoneId="1036">
              <HotSpot X="123.456" Y="789.012" Z="345.678" />
              <MoveTo X="111.111" Y="222.222" Z="333.333" />
              <InteractWith NpcId="42" ObjectId="9001" />
              <Boss BossId="77" SpellId="444" />
            </Profile>
            """);

        var scanner = new RouteReferenceScanner();
        var manifest = scanner.Scan("Fixture", [temp.Path]);
        var entry = Assert.Single(manifest.Entries);
        var json = RouteReferenceScanner.Serialize(manifest);

        Assert.Equal(RouteReferenceCategory.Dungeon, entry.Category);
        Assert.Contains(4u, entry.QueueDutyIds);
        Assert.Contains(1036u, entry.TerritoryIds);
        Assert.Contains(42u, entry.NpcIds);
        Assert.Contains(77u, entry.NpcIds);
        Assert.Contains(9001u, entry.ObjectIds);
        Assert.Contains(444u, entry.ActionIds);
        Assert.True(entry.WaypointLikeNodeCount >= 2);
        Assert.Contains("Boss", entry.InteractionKinds);
        Assert.Contains("Interact", entry.InteractionKinds);
        Assert.DoesNotContain("123.456", json, StringComparison.Ordinal);
        Assert.DoesNotContain("789.012", json, StringComparison.Ordinal);
        Assert.DoesNotContain("345.678", json, StringComparison.Ordinal);
        Assert.DoesNotContain("111.111", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"x\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"y\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"z\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonProfile_ExtractsAllowedIdsButNeverVectors()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "Raids", "ExampleRaid.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "queueDutyId": 900,
              "territoryId": 1200,
              "waypoints": [
                { "x": 14.25, "y": -3.5, "z": 88.75 },
                { "x": 19.75, "y": -2.25, "z": 91.5 }
              ],
              "boss": { "npcId": 7001, "actionId": 50001 },
              "door": { "dataId": 3100 }
            }
            """);

        var manifest = new RouteReferenceScanner().Scan("Fixture", [temp.Path]);
        var entry = Assert.Single(manifest.Entries);
        var json = RouteReferenceScanner.Serialize(manifest);

        Assert.Equal(RouteReferenceCategory.Raid, entry.Category);
        Assert.Contains(900u, entry.QueueDutyIds);
        Assert.Contains(1200u, entry.TerritoryIds);
        Assert.Contains(7001u, entry.NpcIds);
        Assert.Contains(50001u, entry.ActionIds);
        Assert.Contains(3100u, entry.ObjectIds);
        Assert.DoesNotContain("14.25", json, StringComparison.Ordinal);
        Assert.DoesNotContain("88.75", json, StringComparison.Ordinal);
        Assert.DoesNotContain("91.5", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpSource_ExtractsNamedIdsButDiscardsVectorCoordinates()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "Trials", "ExampleTrial.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            internal static class ExampleTrial
            {
                internal const uint DutyId = 777;
                internal const uint TerritoryId = 1555;
                internal const uint BossNpcId = 8888;
                internal const uint SpellId = 45643;
                private static readonly Vector3 Arena = new(111.1f, 222.2f, 333.3f);
                void Run() => Navigator.MoveTo(Arena);
            }
            """);

        var manifest = new RouteReferenceScanner().Scan("Fixture", [temp.Path]);
        var entry = Assert.Single(manifest.Entries);
        var json = RouteReferenceScanner.Serialize(manifest);

        Assert.Equal(RouteReferenceCategory.Trial, entry.Category);
        Assert.Contains(777u, entry.QueueDutyIds);
        Assert.Contains(1555u, entry.TerritoryIds);
        Assert.Contains(8888u, entry.NpcIds);
        Assert.Contains(45643u, entry.ActionIds);
        Assert.True(entry.WaypointLikeNodeCount >= 2);
        Assert.DoesNotContain("111.1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("222.2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("333.3", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryScan_UsesRelativePathsAndDoesNotLeakAbsoluteRoot()
    {
        using var temp = new TempDirectory();
        var child = Path.Combine(temp.Path, "Dungeon", "Route.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(child)!);
        File.WriteAllText(child, "<Profile><HotSpot X=\"1\" Y=\"2\" Z=\"3\" /></Profile>");

        var manifest = new RouteReferenceScanner().Scan("Local Pack", [temp.Path]);
        var json = RouteReferenceScanner.Serialize(manifest);
        var entry = Assert.Single(manifest.Entries);

        Assert.Equal("Dungeon/Route.xml", entry.RelativePath);
        Assert.DoesNotContain(temp.Path, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectoryScan_SkipsDtdProfileAndContinuesWithValidFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "Bad.xml"), "<!DOCTYPE Profile [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><Profile>&xxe;</Profile>");
        File.WriteAllText(Path.Combine(temp.Path, "Good.xml"), "<Profile DutyId=\"44\"><HotSpot X=\"1\" Y=\"2\" Z=\"3\" /></Profile>");

        var manifest = new RouteReferenceScanner().Scan("Fixture", [temp.Path]);

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("Good.xml", entry.RelativePath);
        Assert.Contains(44u, entry.QueueDutyIds);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EZBuddy.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide the assertion result.
            }
        }
    }
}
