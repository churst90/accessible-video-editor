using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core;

/// <summary>
/// Marker for the strongly-typed ID structs. Stable IDs are the reason the
/// project file is JSON rather than text: undo, overlay anchoring and render
/// cache invalidation all need to name an element, not a line number.
/// </summary>
public interface IStableId
{
    string Value { get; }
}

// `default(TId)` bypasses the constructor, so Value really can be null however
// the struct is declared. Rather than pretend otherwise, every read of Value
// goes through IsUnset or ToString, both of which are null-safe.

public readonly record struct SourceId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct TrackId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a <see cref="Model.SpineElement"/>.</summary>
public readonly record struct ElementId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies an <see cref="Model.OverlayItem"/>.</summary>
public readonly record struct ItemId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

public readonly record struct MarkerId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a <see cref="Model.Subclip"/> - a named range of a source.</summary>
public readonly record struct SubclipId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a <see cref="Model.SegmentGroup"/>.</summary>
public readonly record struct GroupId(string Value) : IStableId
{
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

public static class Ids
{
    // Crockford base32 minus the vowels, so generated IDs can't spell anything
    // and can be read aloud unambiguously.
    private const string Alphabet = "0123456789bcdfghjkmnpqrstvwxyz";

    public static string New(int length = 8)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(length, bytes.ToArray(), static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[source[i] % Alphabet.Length];
            }
        });
    }

    public static SourceId NewSource() => new(New());
    public static TrackId NewTrack() => new(New());
    public static ElementId NewElement() => new(New());
    public static ItemId NewItem() => new(New());
    public static MarkerId NewMarker() => new(New());
    public static SubclipId NewSubclip() => new(New());
    public static GroupId NewGroup() => new(New());
    public static Model.TakeId NewTake() => new(New());
}

/// <summary>Serialises every <see cref="IStableId"/> struct as a plain JSON string.</summary>
public sealed class StableIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsValueType && typeof(IStableId).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(StableIdJsonConverter<>).MakeGenericType(typeToConvert))!;
}

public sealed class StableIdJsonConverter<T> : JsonConverter<T> where T : struct, IStableId
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType is JsonTokenType.Null
            ? default
            : (T)Activator.CreateInstance(typeof(T), reader.GetString() ?? string.Empty)!;

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
