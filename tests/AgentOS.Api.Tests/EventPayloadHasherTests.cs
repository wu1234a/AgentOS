using System.Text.Json;
using AgentOS.Application.Events;

namespace AgentOS.Api.Tests;

public sealed class EventPayloadHasherTests
{
    private readonly EventPayloadHasher _hasher = new();

    [Fact]
    public void Property_order_does_not_change_the_hash()
    {
        var first = Hash("""{"b":2,"a":{"y":true,"x":"v"}}""");
        var second = Hash("""{"a":{"x":"v","y":true},"b":2}""");

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal("""{"a":{"x":"v","y":true},"b":2}""", first.CanonicalJson);
    }

    [Fact]
    public void Insignificant_whitespace_does_not_change_the_hash()
    {
        var compact = Hash("""{"a":1}""");
        var spaced = Hash("{\n  \"a\" : 1\n}");

        Assert.Equal(compact.Sha256, spaced.Sha256);
    }

    [Fact]
    public void Array_order_is_significant()
    {
        Assert.NotEqual(Hash("""{"a":[1,2]}""").Sha256, Hash("""{"a":[2,1]}""").Sha256);
    }

    [Fact]
    public void Value_changes_change_the_hash()
    {
        Assert.NotEqual(Hash("""{"a":1}""").Sha256, Hash("""{"a":2}""").Sha256);
        Assert.NotEqual(Hash("""{"a":"1"}""").Sha256, Hash("""{"a":1}""").Sha256);
        Assert.NotEqual(Hash("""{"a":null}""").Sha256, Hash("""{"a":false}""").Sha256);
    }

    [Fact]
    public void Duplicate_property_names_keep_the_last_value()
    {
        var duplicated = Hash("""{"a":1,"a":2}""");
        var single = Hash("""{"a":2}""");

        Assert.Equal(single.Sha256, duplicated.Sha256);
        Assert.Equal("""{"a":2}""", duplicated.CanonicalJson);
    }

    [Fact]
    public void Nested_objects_are_canonicalized_recursively()
    {
        var first = Hash("""{"outer":{"b":[{"d":4,"c":3}],"a":1}}""");
        var second = Hash("""{"outer":{"a":1,"b":[{"c":3,"d":4}]}}""");

        Assert.Equal(first.Sha256, second.Sha256);
    }

    [Fact]
    public void Non_ascii_strings_round_trip_and_hash_deterministically()
    {
        var payload = Hash("""{"备注":"中文内容","emoji":"✅"}""");

        Assert.Contains("中文内容", payload.CanonicalJson);
        Assert.Equal(payload.Sha256, Hash("""{"emoji":"✅","备注":"中文内容"}""").Sha256);
    }

    [Fact]
    public void Hash_is_lowercase_sha256_hex()
    {
        var payload = Hash("""{"a":1}""");

        Assert.Equal(EventPayloadHasher.HashLength, payload.Sha256.Length);
        Assert.Matches("^[0-9a-f]{64}$", payload.Sha256);
    }

    [Fact]
    public void Stored_json_and_element_paths_agree()
    {
        const string json = """{"b":2,"a":1}""";

        Assert.Equal(Hash(json).Sha256, _hasher.CanonicalizeJson(json).Sha256);
    }

    private CanonicalPayload Hash(string json)
    {
        using var document = JsonDocument.Parse(json);
        return _hasher.Canonicalize(document.RootElement);
    }
}
