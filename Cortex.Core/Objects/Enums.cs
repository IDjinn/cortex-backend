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

public enum MemoryScope
{
    Global,
    Project,
    Conversation
}

public enum MemorySource
{
    Manual,
    Extracted
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
