using System.Text.RegularExpressions;

namespace AgenticAI.Backend.Services;

public sealed partial class GuardrailPolicy : IGuardrailPolicy
{
    [GeneratedRegex("(ignore\\s+previous\\s+instructions|jailbreak|system\\s+prompt)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PromptInjectionRegex();

    [GeneratedRegex("(\\b\\d{3}-\\d{2}-\\d{4}\\b|\\b(?:\\d[ -]*?){13,16}\\b)", RegexOptions.Compiled)]
    private static partial Regex PiiRegex();

    public void ValidatePrompt(string prompt)
    {
        if (PromptInjectionRegex().IsMatch(prompt))
        {
            throw new InvalidOperationException("Prompt blocked by guardrail policy.");
        }

        if (PiiRegex().IsMatch(prompt))
        {
            throw new InvalidOperationException("Prompt blocked because it appears to contain PII.");
        }
    }

    public string ValidateCompletion(string output)
    {
        return PiiRegex().Replace(output, "[REDACTED]");
    }
}
