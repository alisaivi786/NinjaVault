namespace NinjaVault.Cdn
{
    /// <summary>
    /// Tenant ids are strings (GUIDs; older tenants may still have numeric ids). The server sends them as JSON
    /// strings, but a numeric JSON value is accepted too and kept as its exact text, so <c>42</c> becomes <c>"42"</c>.
    /// </summary>
    internal sealed class TenantIdJsonConverter : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadTenantId(ref reader);

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);

        internal static string? ReadTenantId(ref Utf8JsonReader reader) => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(
                reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan),
            _ => throw new JsonException($"Expected a tenant id string or number, got {reader.TokenType}.")
        };
    }

    /// <summary>List form of <see cref="TenantIdJsonConverter"/> for <see cref="CdnAccessContext.AllowedTenantIds"/>.</summary>
    internal sealed class TenantIdListJsonConverter : JsonConverter<IReadOnlyList<string>>
    {
        public override bool HandleNull => true;

        public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return [];
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"Expected an array of tenant ids, got {reader.TokenType}.");
            }

            List<string> ids = [];
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? id = TenantIdJsonConverter.ReadTenantId(ref reader);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string id in value)
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
        }
    }
}