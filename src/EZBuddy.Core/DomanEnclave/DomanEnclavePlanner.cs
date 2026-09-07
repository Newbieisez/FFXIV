namespace EZBuddy.Core.DomanEnclave;

public sealed record DomanDonationCandidate(
    uint ItemId, string Name, int QuantityOwned, int ReserveQuantity,
    int DonationValueEach, bool ExplicitlyApproved);

public sealed record DomanEnclaveSnapshot(int WeeklyRemainingBudget, IReadOnlyList<DomanDonationCandidate> Candidates);

public sealed record DomanDonationPlan(
    IReadOnlyList<(uint ItemId, string Name, int Quantity, int Value)> Donations,
    int TotalValue,
    IReadOnlyList<string> Warnings);

public static class DomanEnclavePlanner
{
    public static DomanDonationPlan Build(DomanEnclaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.WeeklyRemainingBudget < 0) throw new ArgumentOutOfRangeException(nameof(snapshot));

        var remaining = snapshot.WeeklyRemainingBudget;
        var donations = new List<(uint ItemId, string Name, int Quantity, int Value)>();
        var warnings = new List<string>();

        foreach (var item in snapshot.Candidates
                     .Where(x => x.ExplicitlyApproved && x.ItemId != 0 && x.DonationValueEach > 0)
                     .OrderByDescending(x => x.DonationValueEach))
        {
            var available = Math.Max(0, item.QuantityOwned - item.ReserveQuantity);
            if (available == 0 || remaining == 0) continue;
            var quantity = Math.Min(available, remaining / item.DonationValueEach);
            if (quantity <= 0) continue;
            var value = checked(quantity * item.DonationValueEach);
            donations.Add((item.ItemId, item.Name, quantity, value));
            remaining -= value;
        }

        if (remaining > 0 && snapshot.Candidates.Any(x => !x.ExplicitlyApproved && x.QuantityOwned > x.ReserveQuantity))
            warnings.Add("Weekly donation budget remains, but EZBuddy will not donate unapproved inventory items.");

        return new DomanDonationPlan(donations, donations.Sum(x => x.Value), warnings);
    }
}
