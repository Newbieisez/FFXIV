using System.Collections.Concurrent;
using System.Reflection;
using EZBuddy.Core.Safety;
using ff14bot.Managers;

namespace EZBuddy.RebornBuddy.Safety;

/// <summary>
/// Passive inbound-contact source backed by RebornBuddy's public GamelogManager event. It does not
/// send chat, reply, move, log out, or inspect raw memory. Ambiguous tell entries are ignored unless
/// sender metadata proves the message did not originate from the local character.
/// </summary>
public sealed class RebornBuddyGamelogContactSource : ISocialContactSource, IDisposable
{
    private static readonly string[] SenderPropertyNames =
    [
        "Author",
        "Sender",
        "Source",
        "PlayerName",
        "Name"
    ];

    private readonly ConcurrentQueue<SocialContactSignal> _signals = new();
    private long _sequence;
    private int _disposed;

    public RebornBuddyGamelogContactSource()
    {
        GamelogManager.MessageRecevied -= OnMessageReceived;
        GamelogManager.MessageRecevied += OnMessageReceived;
    }

    public Task<IReadOnlyList<SocialContactSignal>> PollAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var drained = new List<SocialContactSignal>();
        while (_signals.TryDequeue(out var signal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            drained.Add(signal);
        }

        return Task.FromResult<IReadOnlyList<SocialContactSignal>>(drained);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        GamelogManager.MessageRecevied -= OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, ChatEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            var entry = e.ChatLogEntry;
            var typeName = entry.MessageType.ToString();
            var senderName = TryReadSender(entry) ?? string.Empty;
            var kind = Classify(typeName, senderName);
            if (!kind.HasValue)
            {
                return;
            }

            var sequence = Interlocked.Increment(ref _sequence);
            var fallbackSender = kind.Value == SocialContactKind.GmCommunication
                ? "GM communication"
                : "Incoming tell";
            var content = entry.Contents?.ToString();
            var id = $"gamelog:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:{sequence}:{typeName}";
            _signals.Enqueue(new SocialContactSignal(
                id,
                kind.Value,
                string.IsNullOrWhiteSpace(senderName) ? fallbackSender : senderName,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(content) ? null : content));
        }
        catch
        {
            // Inbound monitoring is optional. An unexpected chat-entry shape must never destabilize
            // the RebornBuddy event thread or automation engine.
        }
    }

    private static SocialContactKind? Classify(string typeName, string senderName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var normalized = typeName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        if (normalized.Contains("gamemaster", StringComparison.Ordinal) ||
            normalized.StartsWith("gm", StringComparison.Ordinal) ||
            normalized.Contains("gmmessage", StringComparison.Ordinal) ||
            normalized.Contains("gmtell", StringComparison.Ordinal))
        {
            return SocialContactKind.GmCommunication;
        }

        if (!normalized.Contains("tell", StringComparison.Ordinal))
        {
            return null;
        }

        if (normalized.Contains("outgoing", StringComparison.Ordinal) ||
            normalized.Contains("sent", StringComparison.Ordinal) ||
            normalized.Contains("totell", StringComparison.Ordinal))
        {
            return null;
        }

        if (normalized.Contains("incoming", StringComparison.Ordinal) ||
            normalized.Contains("received", StringComparison.Ordinal) ||
            normalized.Contains("fromtell", StringComparison.Ordinal))
        {
            return SocialContactKind.DirectTell;
        }

        // Some RebornBuddy builds expose a generic Tell message type. Treat it as inbound only when
        // public sender metadata exists and is clearly not the local character. Otherwise fail closed.
        var localName = ff14bot.Core.Player?.Name;
        if (!string.IsNullOrWhiteSpace(senderName) &&
            !string.Equals(senderName, localName, StringComparison.OrdinalIgnoreCase))
        {
            return SocialContactKind.DirectTell;
        }

        return null;
    }

    private static string? TryReadSender(object entry)
    {
        var type = entry.GetType();
        foreach (var propertyName in SenderPropertyNames)
        {
            PropertyInfo? property;
            try
            {
                property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            }
            catch
            {
                continue;
            }

            if (property is null || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                var value = property.GetValue(entry)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            catch
            {
            }
        }

        return null;
    }
}
