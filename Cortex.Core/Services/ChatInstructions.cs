using System.Globalization;

namespace Cortex.Core.Services;

/// <summary>
/// Builds the base system instructions injected at the head of every chat turn.
/// The {language} placeholder is filled from the caller's device locale.
/// </summary>
public static class ChatInstructions
{
    public const string DefaultTemplate =
        """
        You are Cortex, a helpful AI assistant.
        Write your responses in {language}, unless the user writes in a different language — then respond in that language.
        Be direct and concise. Use Markdown formatting when it improves readability.
        If you are unsure or don't know something, say so honestly.
        """;

    public static string? Build(string? locale, string? template)
    {
        var tpl = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;
        if (!tpl.Contains("{language}", StringComparison.Ordinal))
            return tpl;
        return tpl.Replace("{language}", DescribeLanguage(locale), StringComparison.Ordinal);
    }

    private static string DescribeLanguage(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return "the same language the user writes in";
        try
        {
            return $"the user's language ({new CultureInfo(locale).EnglishName}, device locale {locale})";
        }
        catch (CultureNotFoundException)
        {
            return "the same language the user writes in";
        }
    }
}
