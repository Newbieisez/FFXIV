namespace EZBuddy.Core.Economy;

public sealed record CurrencySnapshot(
    string Key,
    string DisplayName,
    int Current,
    int Cap,
    int ProjectedIncoming = 0,
    int SafetyBuffer = 0)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        if (Current < 0 || Cap <= 0 || Current > Cap || ProjectedIncoming < 0 || SafetyBuffer < 0 || SafetyBuffer >= Cap)
        {
            throw new ArgumentOutOfRangeException(nameof(Current), $"Invalid currency snapshot for {DisplayName}.");
        }
    }

    public int ProjectedTotal => Current + ProjectedIncoming;
    public int SafeCeiling => Cap - SafetyBuffer;
    public bool IsAtRisk => ProjectedTotal > SafeCeiling;
}

public sealed record CurrencySpendOption(
    string CurrencyKey,
    string Key,
    string DisplayName,
    int Cost,
    int Priority,
    int MinimumReserve = 0,
    bool ExplicitlyApproved = false,
    int MaximumPurchases = 1)
{
    public void Validate()
    {
        if (Cost <= 0 || MinimumReserve < 0 || MaximumPurchases <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Cost));
        }
    }
}

public sealed record CurrencySpendPlanItem(
    string CurrencyKey,
    string OptionKey,
    string DisplayName,
    int Quantity,
    int TotalCost,
    string Reason);

public sealed record CurrencyCapPlan(
    IReadOnlyList<CurrencySpendPlanItem> SpendItems,
    IReadOnlyList<string> Warnings);

public static class CurrencyCapManager
{
    public static CurrencyCapPlan Build(
        IEnumerable<CurrencySnapshot> currencies,
        IEnumerable<CurrencySpendOption> spendOptions)
    {
        ArgumentNullException.ThrowIfNull(currencies);
        ArgumentNullException.ThrowIfNull(spendOptions);

        var options = spendOptions.ToArray();
        foreach (var option in options)
        {
            option.Validate();
        }

        var plans = new List<CurrencySpendPlanItem>();
        var warnings = new List<string>();

        foreach (var currency in currencies)
        {
            currency.Validate();
            if (!currency.IsAtRisk)
            {
                continue;
            }

            var projected = currency.ProjectedTotal;
            var approved = options
                .Where(option => string.Equals(option.CurrencyKey, currency.Key, StringComparison.OrdinalIgnoreCase) && option.ExplicitlyApproved)
                .OrderByDescending(option => option.Priority)
                .ThenBy(option => option.Cost)
                .ToArray();

            foreach (var option in approved)
            {
                var bought = 0;
                while (bought < option.MaximumPurchases &&
                       projected > currency.SafeCeiling &&
                       projected - option.Cost >= option.MinimumReserve)
                {
                    projected -= option.Cost;
                    bought++;
                }

                if (bought > 0)
                {
                    plans.Add(new CurrencySpendPlanItem(
                        currency.Key,
                        option.Key,
                        option.DisplayName,
                        bought,
                        bought * option.Cost,
                        $"Projected {currency.DisplayName} would exceed the safe ceiling of {currency.SafeCeiling:N0}."));
                }

                if (projected <= currency.SafeCeiling)
                {
                    break;
                }
            }

            if (projected > currency.SafeCeiling)
            {
                warnings.Add($"{currency.DisplayName}: projected total {currency.ProjectedTotal:N0} exceeds safe ceiling {currency.SafeCeiling:N0}, but approved spend rules cannot reduce it enough.");
            }
        }

        return new CurrencyCapPlan(plans, warnings);
    }
}
