using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AgentOS.Application.Events;

/// <summary>
/// Computes the canonical JSON form of an event payload and its SHA-256 fingerprint.
/// </summary>
/// <remarks>
/// <para>
/// The deduplication policy (<see cref="Policy"/>) is fixed and documented in
/// <c>docs/EVENT-SPECIFICATION.md</c> §5. It is used to decide whether two submissions
/// that share a <c>Source + DedupeKey</c> carry the same content:
/// </para>
/// <list type="bullet">
/// <item><description>Object properties are ordered by ordinal name.</description></item>
/// <item><description>Duplicate property names keep the last occurrence, matching the
/// behaviour of <see cref="JsonDocument"/> consumers.</description></item>
/// <item><description>Array order is preserved.</description></item>
/// <item><description>No insignificant whitespace is emitted; numbers keep the lexical
/// form they were received with.</description></item>
/// <item><description>Strings are UTF-8 encoded with minimal escaping (quotes,
/// backslashes and control characters only).</description></item>
/// </list>
/// <para>
/// Changing any of these rules changes stored hashes, so it requires a new policy version
/// and a backfill migration.
/// </para>
/// </remarks>
public sealed class EventPayloadHasher
{
    /// <summary>Versioned name of the canonicalization policy.</summary>
    public const string Policy = "agentos.payload-canonical-json/v1";

    /// <summary>Length of a SHA-256 hash rendered as lowercase hexadecimal.</summary>
    public const int HashLength = 64;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = true
    };

    /// <summary>Produces both the canonical JSON text and its hash.</summary>
    public CanonicalPayload Canonicalize(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("payload must be a defined JSON value.", nameof(payload));
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteCanonical(writer, payload);
        }

        var canonicalJson = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        var hash = ComputeHash(canonicalJson);

        return new CanonicalPayload(canonicalJson, hash);
    }

    /// <summary>Hashes an already canonical payload produced by <see cref="Canonicalize"/>.</summary>
    public static string ComputeHash(string canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);

        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Canonicalizes and hashes a stored payload document.</summary>
    public CanonicalPayload CanonicalizeJson(string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        using var document = JsonDocument.Parse(payloadJson);
        return Canonicalize(document.RootElement);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element);
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new ArgumentException($"Unsupported JSON value kind '{element.ValueKind}'.", nameof(element));
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            // Last occurrence wins, so duplicate names cannot make the hash ambiguous.
            properties[property.Name] = property.Value;
        }

        writer.WriteStartObject();
        foreach (var name in properties.Keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            writer.WritePropertyName(name);
            WriteCanonical(writer, properties[name]);
        }

        writer.WriteEndObject();
    }
}

/// <summary>Result of canonicalizing a payload: the canonical JSON text and its SHA-256 hash.</summary>
public sealed record CanonicalPayload(string CanonicalJson, string Sha256);
