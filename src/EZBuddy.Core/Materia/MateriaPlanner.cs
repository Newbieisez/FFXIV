namespace EZBuddy.Core.Materia;

public sealed record MateriaStock(
    uint ItemId,
    string Name,
    string StatKey,
    int StatGain,
    int QuantityOwned,
    int ReserveQuantity = 0,
    int UnitCost = 0,
    bool CanOvermeld = true)
{
    public int SpendableQuantity => Math.Max(0, QuantityOwned - ReserveQuantity);

    public void Validate()
    {
        if (ItemId == 0 || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(StatKey) ||
            StatGain <= 0 || QuantityOwned < 0 || ReserveQuantity < 0 || UnitCost < 0)
        {
            throw new InvalidDataException($"Invalid materia stock '{Name}'.");
        }
    }
}

public sealed record MeldSlot(int Index, bool IsOvermeld)
{
    public void Validate()
    {
        if (Index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Index));
        }
    }
}

public sealed record GearMeldRequest(
    uint GearItemId,
    string GearName,
    IReadOnlyDictionary<string, int> CurrentStats,
    IReadOnlyDictionary<string, int> StatCaps,
    IReadOnlyDictionary<string, int> DesiredMinimumStats,
    IReadOnlyDictionary<string, double> StatPriorityWeights,
    IReadOnlyList<MeldSlot> AvailableSlots)
{
    public void Validate()
    {
        if (GearItemId == 0 || string.IsNullOrWhiteSpace(GearName))
        {
            throw new InvalidDataException("Gear meld requests require an item ID and name.");
        }

        foreach (var slot in AvailableSlots)
        {
            slot.Validate();
        }

        if (AvailableSlots.Select(slot => slot.Index).Distinct().Count() != AvailableSlots.Count)
        {
            throw new InvalidDataException("Meld slot indexes must be unique.");
        }

        foreach (var (stat, desired) in DesiredMinimumStats)
        {
            if (desired < 0 || !StatCaps.TryGetValue(stat, out var cap) || cap < 0 || desired > cap)
            {
                throw new InvalidDataException($"Desired target for '{stat}' is invalid or exceeds its cap.");
            }
        }
    }
}

public sealed record PlannedMeld(
    int SlotIndex,
    bool IsOvermeld,
    uint MateriaItemId,
    string MateriaName,
    string StatKey,
    int EffectiveStatGain,
    int UnitCost,
    string Reason);

public sealed record MateriaPlan(
    GearMeldRequest Request,
    IReadOnlyList<PlannedMeld> Melds,
    IReadOnlyDictionary<string, int> FinalStats,
    IReadOnlyDictionary<string, int> UnmetTargets,
    int EstimatedMateriaCost)
{
    public bool MeetsTargets => UnmetTargets.Count == 0;
}

public static class MateriaPlanner
{
    public static MateriaPlan Build(GearMeldRequest request, IEnumerable<MateriaStock> materiaInventory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materiaInventory);
        request.Validate();

        var stocks = materiaInventory.ToArray();
        foreach (var stock in stocks)
        {
            stock.Validate();
        }

        var remaining = stocks.ToDictionary(stock => stock.ItemId, stock => stock.SpendableQuantity);
        var stats = new Dictionary<string, int>(request.CurrentStats, StringComparer.OrdinalIgnoreCase);
        var melds = new List<PlannedMeld>();

        foreach (var slot in request.AvailableSlots.OrderBy(slot => slot.Index))
        {
            var deficits = GetDeficits(request, stats);
            if (deficits.Count == 0)
            {
                break;
            }

            var targetStat = deficits
                .OrderByDescending(entry => request.StatPriorityWeights.GetValueOrDefault(entry.Key, 1d))
                .ThenByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .First().Key;

            var current = stats.GetValueOrDefault(targetStat);
            var cap = request.StatCaps[targetStat];
            var desired = request.DesiredMinimumStats[targetStat];
            var needed = Math.Min(desired - current, cap - current);
            if (needed <= 0)
            {
                continue;
            }

            var candidate = stocks
                .Where(stock => string.Equals(stock.StatKey, targetStat, StringComparison.OrdinalIgnoreCase))
                .Where(stock => remaining.GetValueOrDefault(stock.ItemId) > 0)
                .Where(stock => !slot.IsOvermeld || stock.CanOvermeld)
                .Select(stock => new
                {
                    Stock = stock,
                    EffectiveGain = Math.Min(stock.StatGain, needed)
                })
                .Where(candidate => candidate.EffectiveGain > 0)
                .OrderBy(candidate => candidate.Stock.UnitCost / (double)candidate.EffectiveGain)
                .ThenBy(candidate => candidate.Stock.UnitCost)
                .ThenByDescending(candidate => candidate.EffectiveGain)
                .ThenBy(candidate => candidate.Stock.ItemId)
                .FirstOrDefault();

            if (candidate is null)
            {
                continue;
            }

            remaining[candidate.Stock.ItemId]--;
            stats[targetStat] = current + candidate.EffectiveGain;
            melds.Add(new PlannedMeld(
                slot.Index,
                slot.IsOvermeld,
                candidate.Stock.ItemId,
                candidate.Stock.Name,
                targetStat,
                candidate.EffectiveGain,
                candidate.Stock.UnitCost,
                $"Highest-priority unmet stat '{targetStat}'; selected lowest cost per effective stat point while preserving the configured reserve."));
        }

        var unmet = GetDeficits(request, stats);
        return new MateriaPlan(
            request,
            melds,
            stats,
            unmet,
            melds.Sum(meld => meld.UnitCost));
    }

    private static Dictionary<string, int> GetDeficits(GearMeldRequest request, IReadOnlyDictionary<string, int> stats)
        => request.DesiredMinimumStats
            .Select(entry => new
            {
                entry.Key,
                Deficit = Math.Max(0, entry.Value - Math.Min(stats.GetValueOrDefault(entry.Key), request.StatCaps[entry.Key]))
            })
            .Where(entry => entry.Deficit > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Deficit, StringComparer.OrdinalIgnoreCase);
}
