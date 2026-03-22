using System.Text;

namespace StoatTote;

/// <summary>
/// Provides standardization utilities to prevent confusion between similar-looking characters.
/// Replaces Unicode characters that could be confused with common ASCII characters in source code.
/// </summary>
internal static class StoatStandards
{
    /// <summary>
    /// Standardizes text by replacing Unicode characters that could be confused
    /// with common ASCII characters used in source code.
    /// </summary>
    /// <param name="llmReply">The text to standardize.</param>
    /// <returns>Standardized text with confusable Unicode characters replaced by their ASCII equivalents.</returns>
    public static string StandardizeText(string llmReply)
    {
        if (string.IsNullOrEmpty(llmReply))
            return llmReply;

        var result = new StringBuilder(llmReply.Length);

        foreach (char c in llmReply)
        {
            result.Append(ReplaceConfusableChar(c));
        }

        return result.ToString();
    }

    /// <summary>
    /// Replaces a single character if it's a known confusable Unicode character.
    /// </summary>
    private static char ReplaceConfusableChar(char c)
    {
        return c switch
        {
            // Right single quotation mark ' (U+2019) → apostrophe ' (U+0027)
            // Warning: "The character U+2019 could be confused with the ASCII character U+0060"
            '\u2019' => '\'',

            // Left single quotation mark ' (U+2018) → apostrophe ' (U+0027)
            '\u2018' => '\'',

            // Left double quotation mark " (U+201C) → quote " (U+0022)
            '\u201C' => '"',

            // Right double quotation mark " (U+201D) → quote " (U+0022)
            '\u201D' => '"',

            // Left single quotation mark (fullwidth) (U+FF07) → apostrophe (U+0027)
            '\uFF07' => '\'',

            // Right single quotation mark (fullwidth) (U+FF02) → quote (U+0022)
            '\uFF02' => '"',

            // Hyphen-minus - (U+002D) - keep as is, but standardize similar dashes
            // En dash – (U+2013) → hyphen-minus - (U+002D)
            '\u2013' => '-',

            // Em dash — (U+2014) → hyphen-minus - (U+002D)
            '\u2014' => '-',

            // Figure dash ‒ (U+2012) → hyphen-minus - (U+002D)
            '\u2012' => '-',

            // Non-breaking hyphen ‑ (U+2011) → hyphen-minus - (U+002D)
            '\u2011' => '-',

            // Horizontal bar ― (U+2015) → hyphen-minus - (U+002D)
            '\u2015' => '-',

            // Small hyphen-minus ‐ (U+2010) → hyphen-minus - (U+002D)
            '\u2010' => '-',

            // Fullwidth hyphen-minus (U+FF0D) → hyphen-minus - (U+002D)
            '\uFF0D' => '-',

            // Ellipsis … (U+2026) → three periods ... (U+002E x3)
            // Note: Handled in main loop for multi-character replacement
            '\u2026' => '.',

            // Single left-pointing angle quotation mark ‹ (U+2039) → less-than < (U+003C)
            '\u2039' => '<',

            // Single right-pointing angle quotation mark › (U+203A) → greater-than > (U+003E)
            '\u203A' => '>',

            // Modifier letter apostrophe ʹ (U+02B9) → apostrophe ' (U+0027)
            '\u02B9' => '\'',

            // Modifier letter turned comma ʽ (U+02BB) → apostrophe ' (U+0027)
            '\u02BB' => '\'',

            // Right half ring ʿ (U+02BF) → apostrophe ' (U+0027)
            '\u02BF' => '\'',

            // Left half ring ʿ (U+02BE) → apostrophe ' (U+0027) - actually modifier letter right half ring
            '\u02BE' => '\'',

            // Arabic comma ، (U+060C) → comma , (U+002C)
            '\u060C' => ',',

            // Semicolon (Arabic) ؛ (U+061B) → semicolon ; (U+003B)
            '\u061B' => ';',

            // Question mark (Arabic) ؟ (U+061F) → question mark ? (U+003F)
            '\u061F' => '?',

            // Latin capital letter A with grave À (U+00C0) → A (U+0041) - keep accent
            // Only replace if specifically needed for standardization

            // Caret accent ˆ (U+02C6) → caret ^ (U+005E)
            '\u02C6' => '^',

            // Modifier letter circumflex accent ˇ (U+02C6) is same as above

            // Inverted breve ̑ (U+02C7) → not standard ASCII

            // Tilde operator ∼ (U+223C) → tilde ~ (U+007E)
            '\u223C' => '~',

            // Fullwidth tilde (U+FF5E) → tilde ~ (U+007E)
            '\uFF5E' => '~',

            // Reversed not sign ¬ (U+00AC) → caret ^ (U+005E) or pipe | (U+007C) - not direct replacement
            // Keep as is as it's context-dependent

            // Vulgar fraction one half ½ (U+00BD) → 1/2 text

            // Multiplication sign × (U+00D7) → x (U+0078)
            '\u00D7' => 'x',

            // Division sign ÷ (U+00F7) → / (U+002F)
            '\u00F7' => '/',

            // Plus-minus sign ± (U+00B1) → +/-
            // Keep as is

            // Bullet • (U+2022) → asterisk * (U+002A)
            '\u2022' => '*',

            // Black small square ▪ (U+25AA) → asterisk * (U+002A)
            '\u25AA' => '*',

            // White small square ▫ (U+25AB) → space or asterisk

            // Black circle ● (U+25CF) → asterisk * (U+002A)
            '\u25CF' => '*',

            // White circle ○ (U+25CB) → asterisk * (U+002A)
            '\u25CB' => '*',

            // Diamond ◆ (U+25C6) or ◇ (U+25C7) → asterisk *
            '\u25C6' or '\u25C7' => '*',

            // Black star ★ (U+2605) → asterisk * (U+002A)
            '\u2605' => '*',

            // White star ☆ (U+2606) → asterisk * (U+002A)
            '\u2606' => '*',

            // Heavy round-tipped rightwards arrow ❯ (U+276F) → greater-than > (U+003E)
            '\u276F' => '>',

            // Heavy round-tipped leftwards arrow ❮ (U+276E) → less-than < (U+003C)
            '\u276E' => '<',

            // Light and heavy single-line rightwards arrow ➔ (U+2794) → greater-than > (U+003E)
            '\u2794' => '>',

            // Heavy wide-headed rightwards arrow ➡ (U+1F601) → greater-than > (U+003E)
            // Note: This is an emoji, not typically wanted in code

            // Return the character unchanged if not a confusable
            _ => c
        };
    }

    /// <summary>
    /// Full standardization including multi-character replacements.
    /// Use this when you need to replace multi-character sequences like ellipsis.
    /// </summary>
    public static string StandardizeTextFull(string llmReply)
    {
        if (string.IsNullOrEmpty(llmReply))
            return llmReply;

        // First, replace multi-character sequences
        var result = llmReply;

        // Replace ellipsis … with three periods ...
        result = result.Replace("…", "...");

        // Replace en-dash surrounded by spaces (–) with hyphen
        result = result.Replace(" – ", " - ");

        // Replace em-dash surrounded by spaces (—) with hyphen
        result = result.Replace(" — ", " - ");

        // Replace plus-minus sign with +/- text
        result = result.Replace("±", "+/-");

        // Replace multiplication sign with x
        result = result.Replace("×", "x");

        // Replace division sign with /
        result = result.Replace("÷", "/");

        // Now apply single-character replacements
        return StandardizeText(result);
    }
}
