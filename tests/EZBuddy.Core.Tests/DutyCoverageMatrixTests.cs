using EZBuddy.Core.Research;

namespace EZBuddy.Core.Tests;

public sealed class DutyCoverageMatrixTests
{
    [Fact]
    public void Build_MergesRouteAndMechanicEvidenceAcrossSources()
    {
        var routes = new[]
        {
            new RouteReferenceManifest(
                "Profiles-A",
                [
                    new RouteReferenceEntry(
                        "Dungeons/HolminsterSwitch.cs",
                        "HolminsterSwitch.cs",
                        "hash-a",
                        RouteReferenceCategory.Dungeon,
                        18,
                        [676],
                        [837],
                        [1001],
                        [2001],
                        [3001],
                        ["Door", "Boss"])
                ]),
            new RouteReferenceManifest(
                "Profiles-B",
                [
                    new RouteReferenceEntry(
                        "Trust/Holminster Switch.xml",
                        "Holminster Switch.xml",
                        "hash-b",
                        RouteReferenceCategory.Dungeon,
                        31,
                        [],
                        [837],
                        [1002],
                        [],
                        [],
                        ["Chest"])
                ])
        };

        var mechanics = new[]
        {
            new PluginMechanicReferenceManifest(
                "DutyMechanic",
                [
                    new PluginMechanicReferenceEntry(
                        "Dungeons/HolminsterSwitch.cs",
                        "HolminsterSwitch.cs",
                        "mechanic-hash",
                        [3001, 3002],
                        [4001],
                        [5001],
                        [6001],
                        [7001],
                        [1001],
                        [2001],
                        ["Avoidance", "Stack"])
                ])
        };

        var matrix = new DutyCoverageMatrixBuilder().Build(routes, mechanics);

        var duty = Assert.Single(matrix.Duties);
        Assert.Equal("holminster-switch", duty.DutyKey);
        Assert.Equal(RouteReferenceCategory.Dungeon, duty.Category);
        Assert.Equal([676u], duty.QueueDutyIds);
        Assert.Equal([837u], duty.TerritoryIds);
        Assert.Equal(49, duty.RouteLikeNodeCount);
        Assert.Contains("Profiles-A", duty.RouteSources);
        Assert.Contains("Profiles-B", duty.RouteSources);
        Assert.Contains("DutyMechanic", duty.MechanicSources);
        Assert.Contains(3002u, duty.ActionIds);
        Assert.Contains(4001u, duty.StatusIds);
        Assert.Contains("Stack", duty.MechanicKinds);
        Assert.Contains("record an original EZBuddy route", duty.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_DoesNotCreateRowsForUnknownOrGenericFiles()
    {
        var routes = new[]
        {
            new RouteReferenceManifest(
                "Source",
                [
                    new RouteReferenceEntry(
                        "Helpers/PluginHelpers.cs",
                        "PluginHelpers.cs",
                        "hash-1",
                        RouteReferenceCategory.Dungeon,
                        10,
                        [], [], [], [], [], []),
                    new RouteReferenceEntry(
                        "Misc/RandomProfile.xml",
                        "RandomProfile.xml",
                        "hash-2",
                        RouteReferenceCategory.Unknown,
                        10,
                        [], [], [], [], [], [])
                ])
        };

        var matrix = new DutyCoverageMatrixBuilder().Build(routes, []);

        Assert.Empty(matrix.Duties);
    }

    [Fact]
    public void Build_KeepsHardModeAsSeparateDuty()
    {
        var routes = new[]
        {
            new RouteReferenceManifest(
                "Source",
                [
                    Entry("Dungeons/Sastasha.xml", "Sastasha.xml"),
                    Entry("Dungeons/Sastasha Hard.xml", "Sastasha Hard.xml")
                ])
        };

        var matrix = new DutyCoverageMatrixBuilder().Build(routes, []);

        Assert.Equal(2, matrix.Duties.Count);
        Assert.Contains(matrix.Duties, row => row.DutyKey == "sastasha");
        Assert.Contains(matrix.Duties, row => row.DutyKey == "sastasha-hard");
    }

    [Fact]
    public void SerializeCsv_ContainsNoCoordinateColumns()
    {
        var matrix = new DutyCoverageMatrixBuilder().Build(
            [new RouteReferenceManifest("Source", [Entry("Dungeons/Sastasha.xml", "Sastasha.xml")])],
            []);

        var csv = DutyCoverageMatrixBuilder.SerializeCsv(matrix);

        Assert.DoesNotContain("Coordinate", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vector", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Position", csv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NextAction", csv, StringComparison.Ordinal);
    }

    private static RouteReferenceEntry Entry(string path, string fileName)
        => new(
            path,
            fileName,
            Guid.NewGuid().ToString("N"),
            RouteReferenceCategory.Dungeon,
            5,
            [], [], [], [], [], ["Boss"]);
}
