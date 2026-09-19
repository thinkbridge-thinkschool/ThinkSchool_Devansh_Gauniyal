using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capstone.SharedKernel;

// Money has no parameterless constructor and no settable properties by design
// (see Money.cs) - exactly the shape System.Text.Json's default reflection-based
// serialization cannot handle for a struct: confirmed live that
// JsonSerializer.Deserialize<Money> silently produces Amount=0, Currency=""
// (bypassing the validating constructor entirely, not even throwing) rather
// than using the type's one public constructor, for a `readonly record struct`
// with manually-declared (non-positional) properties. A struct's own
// constructor-matching behaviour in System.Text.Json is apparently less
// reliable than a class/record's, so a converter is written explicitly here
// rather than depended on. Registered wherever Money is persisted as part of a
// larger JSON payload (see Capstone.Invoicing.Infrastructure.Persistence and
// Capstone.Procurement.Infrastructure.Persistence) - once registered, it
// applies automatically to every Money value anywhere in the serialized graph,
// no matter how deeply nested.
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a JSON object for {nameof(Money)}.");
        }

        decimal amount = 0;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, nameof(Money.Amount), StringComparison.OrdinalIgnoreCase))
            {
                amount = reader.GetDecimal();
            }
            else if (string.Equals(propertyName, nameof(Money.Currency), StringComparison.OrdinalIgnoreCase))
            {
                currency = reader.GetString();
            }
        }

        return new Money(amount, currency ?? throw new JsonException($"{nameof(Money.Currency)} is required."));
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(Money.Amount), value.Amount);
        writer.WriteString(nameof(Money.Currency), value.Currency);
        writer.WriteEndObject();
    }
}
