namespace Cortex.Core.Objects;

public enum AuthProvider
{
    Google,
    GitHub
}

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public enum ChatProviderKind
{
    OpenRouter,
    Ollama,
    LmStudio,
    OpenAI,
    Anthropic,
    Gemini,
    Xai,
    Mistral,
    DeepSeek
}
