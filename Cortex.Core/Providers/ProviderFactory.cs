using Cortex.Core.Objects;

namespace Cortex.Core.Providers;

public sealed class ProviderFactory : IProviderFactory
{
    private readonly OpenRouterProvider _openRouter;
    private readonly OllamaProvider _ollama;
    private readonly LmStudioProvider _lmStudio;
    private readonly OpenAiProvider _openAi;
    private readonly AnthropicProvider _anthropic;
    private readonly GeminiProvider _gemini;
    private readonly XaiProvider _xai;
    private readonly MistralProvider _mistral;
    private readonly DeepSeekProvider _deepSeek;

    public ProviderFactory(
        OpenRouterProvider openRouter,
        OllamaProvider ollama,
        LmStudioProvider lmStudio,
        OpenAiProvider openAi,
        AnthropicProvider anthropic,
        GeminiProvider gemini,
        XaiProvider xai,
        MistralProvider mistral,
        DeepSeekProvider deepSeek)
    {
        _openRouter = openRouter;
        _ollama = ollama;
        _lmStudio = lmStudio;
        _openAi = openAi;
        _anthropic = anthropic;
        _gemini = gemini;
        _xai = xai;
        _mistral = mistral;
        _deepSeek = deepSeek;
    }

    public IProvider Get(ChatProviderKind kind) => kind switch
    {
        ChatProviderKind.OpenRouter => _openRouter,
        ChatProviderKind.Ollama => _ollama,
        ChatProviderKind.LmStudio => _lmStudio,
        ChatProviderKind.OpenAI => _openAi,
        ChatProviderKind.Anthropic => _anthropic,
        ChatProviderKind.Gemini => _gemini,
        ChatProviderKind.Xai => _xai,
        ChatProviderKind.Mistral => _mistral,
        ChatProviderKind.DeepSeek => _deepSeek,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
