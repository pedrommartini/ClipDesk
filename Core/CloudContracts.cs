using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClipDesk.Core;

public static partial class CloudRules
{
    public const long DatabaseFileLimit = 30_000_000;
    public static bool UsesDrive(long size) => size > DatabaseFileLimit;
    public static string CanonicalGuidId(string value) => Guid.TryParse(value, out var id) ? id.ToString("N") : value;
    [GeneratedRegex("^[a-z0-9_]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
    public static bool IsUsernameValid(string name) => UsernamePattern().IsMatch(name)
        && name is not ("admin" or "clipdesk" or "support" or "suporte" or "system");
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    public static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    // Compare just fields touched by the operation, preserving independent remote changes.
    public static JsonObject Merge(JsonObject current, JsonObject basis, JsonObject desired)
    {
        var result = (JsonObject)current.DeepClone();
        foreach (var key in basis.Select(p => p.Key).Union(desired.Select(p => p.Key)))
        {
            if (Same(basis[key], desired[key])) continue;
            if (!Same(current[key], basis[key]) && !Same(current[key], desired[key]))
                throw new SyncConflictException(key);
            if (desired.ContainsKey(key)) result[key] = desired[key]?.DeepClone();
            else result.Remove(key);
        }
        return result;
    }
}

public sealed class SyncConflictException(string field) : Exception($"Alterações concorrentes no campo {field}.");
public sealed record CloudUser(string Id, string? Username, string Name, string? Picture);
public sealed record LoginChallenge(string Nonce);
public sealed record GoogleLogin(string IdToken, string Nonce);
public sealed record CloudSession(string Token, CloudUser User);
public sealed record UsernameRequest(string Username);
public sealed record CloudEntity(string Id, string Kind, string? WorkspaceId, long Version, JsonObject Data, bool Deleted = false);
public sealed record SyncOperation(string OperationId, string EntityId, string Kind, string? WorkspaceId,
    long BaseVersion, JsonObject BaseData, JsonObject Data, bool Deleted = false);
public sealed record SyncResult(CloudEntity Entity);
public sealed record InviteRequest(string Username);
public sealed record CloudInvitation(string Id, string WorkspaceId, string WorkspaceName, string FromUsername, string FromName, string? FromPicture);
public sealed record CloudMember(string UserId, string Username, string? Picture, string Role);
public sealed record DriveGrant(string FileId, string Email,string WorkspaceId="",long MembershipVersion=0);
public sealed record AttachmentRegistration(string Id, string? WorkspaceId, string Name, long Size, string Sha256, bool Drive);
public sealed record DriveCompletion(string FileId);
public sealed record PresenceMessage(string WorkspaceId, double X, double Y, string? ItemId,
    double ViewCenterX = 0, double ViewCenterY = 0, double Zoom = 1, PresenceDrag[]? Drags = null);
public sealed record CloudPresence(string UserId, string Username, string? Picture, string WorkspaceId, double X, double Y, string? ItemId,
    double ViewCenterX = 0, double ViewCenterY = 0, double Zoom = 1, PresenceDrag[]? Drags = null);
// Ephemeral geometry only; the regular entity sync remains authoritative.
public sealed record PresenceDrag(string Id, double X, double Y);

public sealed class CloudAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public bool IsDrive { get; set; }
    public bool Uploaded { get; set; }
    public string? DriveFileId { get; set; }
    public string? StoragePath { get; set; }
    // LocalPath is never included in a network projection.
    public string? LocalPath { get; set; }
    public string? RelativePath { get; set; }
}
