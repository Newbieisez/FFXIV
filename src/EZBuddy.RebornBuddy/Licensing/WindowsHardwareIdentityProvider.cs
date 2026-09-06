using System.Management;
using System.Security.Cryptography;
using System.Text;
using EZBuddy.Core.Licensing;

namespace EZBuddy.RebornBuddy.Licensing;

public sealed class WindowsHardwareIdentityProvider : IHardwareIdentityProvider
{
    private readonly object _sync = new();
    private string? _cached;

    public string GetAnonymousHardwareId()
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(_cached))
            {
                return _cached;
            }

            var components = new List<string>();
            AddWmiValues(components, "SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
            AddWmiValues(components, "SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            AddWmiValues(components, "SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");

            var stableMaterial = string.Join("|", components
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal));

            if (string.IsNullOrWhiteSpace(stableMaterial))
            {
                stableMaterial = $"{Environment.MachineName}|{Environment.OSVersion.VersionString}";
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"EZBuddy-HWID-v1|{stableMaterial}"));
            _cached = Convert.ToHexString(hash);
            return _cached;
        }
    }

    private static void AddWmiValues(ICollection<string> destination, string query, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var value = item[propertyName]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    destination.Add(value);
                }
            }
        }
        catch (ManagementException)
        {
            // WMI can be restricted on hardened systems. Fallback material is used instead.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not fail startup solely because WMI is unavailable.
        }
    }
}
