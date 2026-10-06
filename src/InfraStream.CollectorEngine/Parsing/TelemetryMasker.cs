namespace InfraStream.CollectorEngine.Parsing;

/// <summary>
/// High-throughput, allocation-free masker for sensitive data.
/// </summary>
/// <remarks>
/// <para>
/// NOTE: the masker no longer masks the entire buffer — it masks
/// individual spans (field values). The parser decides which fields
/// to mask (<c>message</c> and values of sensitive attributes).
/// </para>
/// <para>
/// Masks:
/// <list type="bullet">
///   <item>values of the JSON keys <c>password</c>, <c>secret</c>,
///         <c>token</c>, <c>api_key</c>;</item>
///   <item>16-digit sequences that pass the Luhn check (credit cards).</item>
/// </list>
/// </para>
/// <para>
/// Plain-text patterns (<c>password=...</c>, <c>secret=...</c>) have been
/// removed — they are covered by the JSON-key rules.
/// </para>
/// </remarks>
public static class TelemetryMasker
{
    // ---------- JSON keys (name with quotes, WITHOUT colon) ----------
    private static readonly byte[] PasswordJsonKey = "\"password\""u8.ToArray();
    private static readonly byte[] SecretJsonKey   = "\"secret\""u8.ToArray();
    private static readonly byte[] TokenJsonKey    = "\"token\""u8.ToArray();
    private static readonly byte[] ApiKeyJsonKey   = "\"api_key\""u8.ToArray();

    private static readonly SearchValues<byte> Digits =
        SearchValues.Create("0123456789"u8);

    /// <summary>
    /// Entry point. Masks sensitive data inside the given span.
    /// The span must contain a UTF-8 JSON fragment (for example, the value
    /// of <c>message</c>).
    /// </summary>
    /// <param name="utf8">UTF-8 bytes to mask in-place.</param>
    public static void MaskInPlace(Span<byte> utf8)
    {
        if (utf8.IsEmpty) return;

        // 1. JSON keys "password"/"secret"/"token"/"api_key" — mask the value.
        MaskJsonKey(utf8, PasswordJsonKey);
        MaskJsonKey(utf8, SecretJsonKey);
        MaskJsonKey(utf8, TokenJsonKey);
        MaskJsonKey(utf8, ApiKeyJsonKey);

        // 2. Credit cards (16 digits, Luhn check).
        MaskCreditCards(utf8);
    }

    // ====================================================================
    //  JSON keys
    // ====================================================================
    /// <summary>
    /// Masks, in place, the value that follows a JSON key.
    /// </summary>
    private static void MaskJsonKey(Span<byte> msg, ReadOnlySpan<byte> key)
    {
        int startFrom = 0;
        while (startFrom < msg.Length)
        {
            int idx = msg.Slice(startFrom).IndexOf(key);
            if (idx < 0) return;

            int absIdx = startFrom + idx;
            int pos = absIdx + key.Length;

            // Skip spaces/tabs before the colon.
            while (pos < msg.Length && (msg[pos] == (byte)' ' || msg[pos] == (byte)'\t'))
                pos++;

            // Expect a colon.
            if (pos >= msg.Length || msg[pos] != (byte)':')
            {
                startFrom = pos;
                continue;
            }
            pos++;

            // Skip spaces/tabs after the colon.
            while (pos < msg.Length && (msg[pos] == (byte)' ' || msg[pos] == (byte)'\t'))
                pos++;

            // Expect an opening quote.
            if (pos >= msg.Length || msg[pos] != (byte)'"')
            {
                startFrom = pos;
                continue;
            }
            pos++;

            // Mask the value until the closing quote, honoring escaped \".
            while (pos < msg.Length)
            {
                byte b = msg[pos];
                if (b == (byte)'\\' && pos + 1 < msg.Length)
                {
                    msg[pos] = (byte)'*';
                    msg[pos + 1] = (byte)'*';
                    pos += 2;
                    continue;
                }
                if (b == (byte)'"') break;
                msg[pos] = (byte)'*';
                pos++;
            }

            startFrom = pos + 1;
        }
    }

    // ====================================================================
    //  Credit cards
    // ====================================================================
    /// <summary>
    /// Scans for 16-digit credit-card sequences and masks them in place.
    /// </summary>
    private static void MaskCreditCards(Span<byte> msg)
    {
        Span<byte> digits = stackalloc byte[16];

        int i = 0;
        while (i < msg.Length)
        {
            int offset = msg.Slice(i).IndexOfAny(Digits);
            if (offset < 0) return;

            int pos = i + offset;

            int advanced = TryMaskCreditCardAt(msg, pos);
            i = advanced > pos ? advanced : pos + 1;
        }
    }

    /// <summary>
    /// Attempts to mask a credit card starting at <paramref name="digitStart"/>.
    /// Returns the position from which scanning should continue.
    /// </summary>
    private static int TryMaskCreditCardAt(Span<byte> msg, int digitStart)
    {
        Span<byte> digits = stackalloc byte[16];
        int n = 0;
        int i = digitStart;
        int lastDigitEnd = digitStart;

        while (i < msg.Length && n < 16)
        {
            byte b = msg[i];
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                digits[n++] = b;
                i++;
                lastDigitEnd = i;
            }
            else if (b == (byte)'-' || b == (byte)' ')
            {
                i++;
            }
            else
            {
                break;
            }
        }

        // If a 17th digit follows — it is not a card number.
        if (i < msg.Length && msg[i] >= (byte)'0' && msg[i] <= (byte)'9')
        {
            while (i < msg.Length && msg[i] >= (byte)'0' && msg[i] <= (byte)'9')
                i++;
            return i;
        }

        if (n != 16 || !LuhnValid(digits))
            return lastDigitEnd;

        // Mask the digits at positions 4..11, preserving separators.
        int seen = 0;
        for (int j = digitStart; j < lastDigitEnd; j++)
        {
            byte b = msg[j];
            if (b < (byte)'0' || b > (byte)'9') continue;
            if (seen >= 4 && seen < 12)
                msg[j] = (byte)'*';
            seen++;
        }
        return lastDigitEnd;
    }

    /// <summary>
    /// Validates a 16-digit sequence using the Luhn checksum.
    /// </summary>
    private static bool LuhnValid(ReadOnlySpan<byte> digits16)
    {
        if (digits16.Length != 16) return false;

        int sum = 0;
        for (int i = 0; i < 16; i++)
        {
            int d = digits16[15 - i] - (byte)'0';
            if ((i & 1) == 1)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return sum % 10 == 0;
    }
}
