using EZBuddy.Core.Research;

namespace EZBuddy.Core.Tests;

public sealed class PluginMechanicReferenceScannerTests
{
    [Fact]
    public void CSharpPlugin_ExtractsMechanicMetadataWithoutCoordinatesOrCode()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "SideStep", "ExampleMechanic.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            internal static class ExampleMechanic
            {
                private const uint BossNpcId = 9001;
                private const uint ActionId = 44444;
                private const uint StatusId = 3210;
                private const uint TetherId = 17;
                private const uint HeadMarkerIconId = 88;
                private const uint MapEffectId = 0x2A;
                private const uint DoorObjectId = 700;
                private static readonly Vector3 SafeSpot = new(123.45f, -67.89f, 901.23f);

                void Register()
                {
                    AvoidanceManager.AddAvoid(new AvoidObjectInfo<BattleCharacter>());
                    // Gaze / stack / knockback / line of sight
                }
            }
            """);

        var manifest = new PluginMechanicReferenceScanner().Scan("Fixture", [temp.Path]);
        var entry = Assert.Single(manifest.Entries);
        var json = PluginMechanicReferenceScanner.Serialize(manifest);

        Assert.Contains(9001u, entry.NpcIds);
        Assert.Contains(44444u, entry.ActionIds);
        Assert.Contains(3210u, entry.StatusIds);
        Assert.Contains(17u, entry.TetherIds);
        Assert.Contains(88u, entry.IconIds);
        Assert.Contains(42u, entry.MapEffectIds);
        Assert.Contains(700u, entry.ObjectIds);
        Assert.Contains("Avoidance", entry.MechanicKinds);
        Assert.Contains("GazeFacing", entry.MechanicKinds);
        Assert.Contains("Stack", entry.MechanicKinds);
        Assert.Contains("Knockback", entry.MechanicKinds);
        Assert.Contains("LineOfSight", entry.MechanicKinds);

        Assert.DoesNotContain("123.45", json, StringComparison.Ordinal);
        Assert.DoesNotContain("67.89", json, StringComparison.Ordinal);
        Assert.DoesNotContain("901.23", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector3", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AvoidanceManager.AddAvoid", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonAndXmlSources_ExtractNamedMechanicIds()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "mechanic.json"), """
            {
              "spellId": 101,
              "statusId": 202,
              "tetherId": 303,
              "targetIconId": 404,
              "environmentControlId": 505,
              "bossNpcId": 606,
              "switchObjectId": 707,
              "x": 12.34,
              "y": 56.78,
              "z": 90.12
            }
            """);
        File.WriteAllText(Path.Combine(temp.Path, "mechanic.xml"), """
            <Mechanic SpellId="111" StatusId="222" BossNpcId="333" DoorObjectId="444" X="9" Y="8" Z="7" />
            """);

        var manifest = new PluginMechanicReferenceScanner().Scan("Fixture", [temp.Path]);
        var json = PluginMechanicReferenceScanner.Serialize(manifest);

        Assert.Equal(2, manifest.Entries.Count);
        Assert.Contains(manifest.Entries, entry => entry.ActionIds.Contains(101u));
        Assert.Contains(manifest.Entries, entry => entry.StatusIds.Contains(202u));
        Assert.Contains(manifest.Entries, entry => entry.TetherIds.Contains(303u));
        Assert.Contains(manifest.Entries, entry => entry.IconIds.Contains(404u));
        Assert.Contains(manifest.Entries, entry => entry.MapEffectIds.Contains(505u));
        Assert.Contains(manifest.Entries, entry => entry.NpcIds.Contains(606u));
        Assert.Contains(manifest.Entries, entry => entry.ObjectIds.Contains(707u));
        Assert.Contains(manifest.Entries, entry => entry.ActionIds.Contains(111u));
        Assert.Contains(manifest.Entries, entry => entry.StatusIds.Contains(222u));
        Assert.Contains(manifest.Entries, entry => entry.NpcIds.Contains(333u));
        Assert.Contains(manifest.Entries, entry => entry.ObjectIds.Contains(444u));

        Assert.DoesNotContain("12.34", json, StringComparison.Ordinal);
        Assert.DoesNotContain("56.78", json, StringComparison.Ordinal);
        Assert.DoesNotContain("90.12", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryScan_DropsFilesWithoutMechanicMetadata()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "README.json"), "{ \"name\": \"nothing useful here\" }");
        File.WriteAllText(Path.Combine(temp.Path, "Duty.cs"), "const uint ActionId = 555; // spread mechanic");

        var manifest = new PluginMechanicReferenceScanner().Scan("Fixture", [temp.Path]);

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("Duty.cs", entry.RelativePath);
        Assert.Contains(555u, entry.ActionIds);
        Assert.Contains("Spread", entry.MechanicKinds);
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
                // Cleanup must not hide the test result.
            }
        }
    }
}
