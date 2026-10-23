using System.Text.Json;

namespace TokenMojo.Core;

public enum SessionStatus { None, Working, NeedsInput, Ready, Ended }

/// <summary>The &lt;session_id&gt;.status file cctokmon-hook.sh writes beside the session's bridge file.</summary>
public sealed record SessionStatusFile(SessionStatus Status, DateTimeOffset Since, DateTimeOffset? TurnStarted)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static SessionStatusFile Parse(string json)
    {
        Payload payload = JsonSerializer.Deserialize<Payload>(json, Options) ?? throw new JsonException("Status file is JSON null");
        SessionStatus status = payload.Status switch
        {
            "working" => SessionStatus.Working,
            "needs_input" => SessionStatus.NeedsInput,
            "ready" => SessionStatus.Ready,
            "ended" => SessionStatus.Ended,
            _ => throw new JsonException($"Unknown session status '{payload.Status}'"),
        };
        long since = payload.Since ?? throw new JsonException("Status file has no 'since'");
        return new SessionStatusFile(
            status,
            DateTimeOffset.FromUnixTimeSeconds(since),
            payload.TurnStarted is { } started ? DateTimeOffset.FromUnixTimeSeconds(started) : null);
    }

    private sealed record Payload(string? Status, long? Since, long? TurnStarted);
}
