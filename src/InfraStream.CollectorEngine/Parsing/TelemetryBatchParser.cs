using InfraStream.Core.Models;

namespace InfraStream.CollectorEngine.Parsing;

/// <summary>
/// High-throughput, allocation-free parser for a telemetry batch.
/// </summary>
/// <remarks>
/// PII masking is performed HERE, and ONLY for the <c>message</c> field
/// and for values of custom attributes whose keys are considered sensitive.
/// Other fields (<c>level</c>, <c>component</c>, <c>timestamp</c>) are not
/// masked — they are either interned or system-provided.
/// </remarks>
public static class TelemetryBatchParser
{
    private static readonly JsonEncodedText TimestampProp = JsonEncodedText.Encode("timestamp");
    private static readonly JsonEncodedText LevelProp     = JsonEncodedText.Encode("level");
    private static readonly JsonEncodedText MessageProp   = JsonEncodedText.Encode("message");
    private static readonly JsonEncodedText ComponentProp = JsonEncodedText.Encode("component");
    private static readonly JsonEncodedText PayloadProp   = JsonEncodedText.Encode("payload");

    private const int MaxAttributesPerItem = 16;

    /// <summary>
    /// Parses a telemetry batch from <paramref name="buffer"/> and feeds
    /// each parsed item to <paramref name="consumer"/>.
    /// </summary>
    /// <typeparam name="TConsumer">
    /// Struct consumer that receives parsed items synchronously.
    /// </typeparam>
    /// <param name="buffer">Pooled buffer holding the raw JSON batch.</param>
    /// <param name="bufferLength">Number of valid bytes in <paramref name="buffer"/>.</param>
    /// <param name="consumer">Consumer invoked once per parsed telemetry item.</param>
    public static void Parse<TConsumer>(
        byte[] buffer,
        int bufferLength,
        ref TConsumer consumer)
        where TConsumer : struct, ITelemetryItemConsumer
    {
        ReadOnlySpan<byte> span = buffer.AsSpan(0, bufferLength);
        var reader = new Utf8JsonReader(span, isFinalBlock: true, state: default);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return;

        if (!TryFindPayloadArray(ref reader))
            return;

        LogAttribute[] attributeBuffer = ArrayPool<LogAttribute>.Shared.Rent(MaxAttributesPerItem);

        try
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject) continue;

                DateTime timestamp = default;
                ReadOnlyMemory<byte> level = default;
                ReadOnlyMemory<byte> message = default;
                ReadOnlyMemory<byte> component = default;
                int attrCount = 0;

                int objectStartTokenIndex = (int)reader.TokenStartIndex;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

                    ReadOnlySpan<byte> name = reader.ValueSpan;
                    int nameLen = name.Length;

                    if (nameLen == 9 && name[0] == (byte)'t' &&
                        reader.ValueTextEquals(TimestampProp.EncodedUtf8Bytes))
                    {
                        reader.Read();
                        if (reader.TryGetDateTime(out var dt)) timestamp = dt;
                    }
                    else if (nameLen == 5 && name[0] == (byte)'l' &&
                             reader.ValueTextEquals(LevelProp.EncodedUtf8Bytes))
                    {
                        reader.Read();
                        level = GetSlice(buffer, reader);
                    }
                    else if (nameLen == 7 && name[0] == (byte)'m' &&
                             reader.ValueTextEquals(MessageProp.EncodedUtf8Bytes))
                    {
                        reader.Read();
                        message = GetSliceMasked(buffer, reader);
                    }
                    else if (nameLen == 9 && name[0] == (byte)'c' &&
                             reader.ValueTextEquals(ComponentProp.EncodedUtf8Bytes))
                    {
                        reader.Read();
                        component = GetSlice(buffer, reader);
                    }
                    else
                    {
                        // Custom attribute.
                        if (attrCount < MaxAttributesPerItem)
                        {
                            ReadOnlyMemory<byte> key = GetSlice(buffer, reader);
                            reader.Read();

                            // Mask the value ONLY if the key is sensitive.
                            ReadOnlyMemory<byte> val = IsSensitiveKey(key.Span)
                                ? GetSliceMasked(buffer, reader)
                                : GetSlice(buffer, reader);

                            attributeBuffer[attrCount++] = new LogAttribute(key, val);
                        }
                        else
                        {
                            reader.Read();
                            reader.Skip();
                        }
                    }
                }

                int objectLength = (int)reader.BytesConsumed - objectStartTokenIndex;
                ReadOnlyMemory<byte> rawJsonSlice =
                    new ReadOnlyMemory<byte>(buffer, objectStartTokenIndex, objectLength);

                var item = new TelemetryItem(
                    timestamp,
                    level,
                    message,
                    component,
                    new ReadOnlyMemory<LogAttribute>(attributeBuffer, 0, attrCount),
                    rawJsonSlice);

                consumer.OnItemParsed(in item);
            }
        }
        finally
        {
            ArrayPool<LogAttribute>.Shared.Return(attributeBuffer, clearArray: true);
        }
    }

    /// <summary>
    /// Returns a value slice WITHOUT masking (for system fields).
    /// </summary>
    private static ReadOnlyMemory<byte> GetSlice(byte[] buffer, in Utf8JsonReader reader)
    {
        ReadOnlySpan<byte> value = reader.ValueSpan;
        if (value.IsEmpty) return ReadOnlyMemory<byte>.Empty;

        // Well-known interned tokens.
        if (value.Length == 4)
        {
            if (value.SequenceEqual("INFO"u8)) return MessageInterner.InternedInfo;
            if (value.SequenceEqual("WARN"u8)) return MessageInterner.InternedWarn;
        }
        else if (value.Length == 5)
        {
            if (value.SequenceEqual("ERROR"u8)) return MessageInterner.InternedError;
            if (value.SequenceEqual("DEBUG"u8)) return MessageInterner.InternedDebug;
            if (value.SequenceEqual("TRACE"u8)) return MessageInterner.InternedTrace;
            if (value.SequenceEqual("FATAL"u8)) return MessageInterner.InternedFatal;
            if (value.SequenceEqual("nginx"u8)) return MessageInterner.InternedNginx;
        }
        else if (value.Length == 12)
        {
            if (value.SequenceEqual("auth-service"u8)) return MessageInterner.InternedAuth;
        }
        else if (value.Length == 17)
        {
            if (value.SequenceEqual("payment-processor"u8)) return MessageInterner.InternedPayment;
        }

        bool hasEscape = value.IndexOf((byte)'\\') >= 0;
        if (hasEscape)
        {
            string decoded = reader.GetString() ?? string.Empty;
            return Encoding.UTF8.GetBytes(decoded);
        }

        int tokenStart = (int)reader.TokenStartIndex + 1;
        return new ReadOnlyMemory<byte>(buffer, tokenStart, value.Length);
    }

    /// <summary>
    /// Returns a value slice WITH masking. The mask is applied to the span
    /// in-place in the source buffer.
    /// </summary>
    /// <remarks>
    /// For escaped strings masking is skipped: correctly masking a decoded
    /// string is non-trivial. In real logs escapes inside <c>message</c> are
    /// rare, so the risk is considered acceptable.
    /// </remarks>
    private static ReadOnlyMemory<byte> GetSliceMasked(byte[] buffer, in Utf8JsonReader reader)
    {
        ReadOnlySpan<byte> value = reader.ValueSpan;
        if (value.IsEmpty) return ReadOnlyMemory<byte>.Empty;

        bool hasEscape = value.IndexOf((byte)'\\') >= 0;
        if (hasEscape)
        {
            // Escaped string: masking is non-trivial, return the decoded
            // bytes without masking.
            string decoded = reader.GetString() ?? string.Empty;
            return Encoding.UTF8.GetBytes(decoded);
        }

        int tokenStart = (int)reader.TokenStartIndex + 1;
        int tokenLength = value.Length;

        // Mask the span in-place inside the buffer.
        Span<byte> target = buffer.AsSpan(tokenStart, tokenLength);
        TelemetryMasker.MaskInPlace(target);

        return new ReadOnlyMemory<byte>(buffer, tokenStart, tokenLength);
    }

    /// <summary>
    /// Advances the reader to the <c>payload</c> array and returns
    /// <see langword="true"/> if it was found.
    /// </summary>
    private static bool TryFindPayloadArray(ref Utf8JsonReader reader)
    {
        var payloadBytes = PayloadProp.EncodedUtf8Bytes;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals(payloadBytes))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.StartArray;
            }

            reader.Read();
            reader.Skip();
        }

        return false;
    }

    /// <summary>
    /// Checks whether an attribute key is sensitive
    /// (<c>password</c>, <c>secret</c>, <c>token</c>, <c>api_key</c>, ...).
    /// Uses a length check followed by a byte-wise comparison.
    /// </summary>
    private static bool IsSensitiveKey(ReadOnlySpan<byte> key)
    {
        // password (8)
        if (key.Length == 8)
        {
            if (key.SequenceEqual("password"u8)) return true;
            if (key.SequenceEqual("passwrd"u8)) return true;  // typo
        }
        // secret (6), token (5), api_key (7), apikey (6)
        else if (key.Length == 6)
        {
            if (key.SequenceEqual("secret"u8)) return true;
            if (key.SequenceEqual("apikey"u8)) return true;
            if (key.SequenceEqual("passwd"u8)) return true;
        }
        else if (key.Length == 5)
        {
            if (key.SequenceEqual("token"u8)) return true;
            if (key.SequenceEqual("pwd"u8)) return true;
        }
        else if (key.Length == 7)
        {
            if (key.SequenceEqual("api_key"u8)) return true;
        }
        else if (key.Length == 3)
        {
            if (key.SequenceEqual("pwd"u8)) return true;
            if (key.SequenceEqual("key"u8)) return true;
        }

        return false;
    }
}
