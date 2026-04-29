using System.Collections.ObjectModel;

namespace ACSTranslate;



public record LanguageConfig(
    string Language, // A friendly name for the language
    string Code, // The language code as per Azure Speech (e.g. en-US, de-DE)
    string Voice, // The voice name as per Azure Speech (e.g. en-AU-DarrenNeural)
    string? WelcomeMessage = null,
    string? ConnectedMessage = null
)
{
    public string ShortCode
        => Code.Split('-')[0]; // This is not technically correct, but it's good enough for now
    public static ReadOnlyDictionary<string, string> ListLanguages()
        => new(_languages.ToDictionary(l => l.Code, l => l.Language));

    public static LanguageConfig? GetLanguageConfig(string? code)
        => code == null
            ? null
            : _languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? _languages.FirstOrDefault(l => l.Code.Split('-')[0].Equals(code, StringComparison.OrdinalIgnoreCase));
    private static readonly LanguageConfig[] _languages = [
        new LanguageConfig(
            "English",
            "en-US",
            "en-AU-DarrenNeural",
            "Hi there, I'm an AI translator that will be assisting with the call today, you will hear both my voice and the agents voice throughout the conversation, please wait while we connect you with an agent.",
            "You are now connected."
        ),
        new LanguageConfig(
            "German",
            "de-DE",
            "de-DE-ChristophNeural",
            "Hallo, ich bin ein KI-Übersetzer, der heute bei dem Anruf helfen wird. Sie werden sowohl meine Stimme als auch die Stimme des Agenten während des Gesprächs hören. Bitte warten Sie, während wir Sie mit einem Agenten verbinden.",
            "Sie sind jetzt verbunden."
        ),
        new LanguageConfig(
            "French",
            "fr-FR",
            "fr-FR-HenriNeural",
            "Bonjour, je suis un traducteur IA qui vous assistera lors de l'appel aujourd'hui. Vous entendrez à la fois ma voix et celle de l'agent tout au long de la conversation. Veuillez patienter pendant que nous vous connectons avec un agent.",
            "Vous êtes maintenant connecté."
        ),
        new LanguageConfig(
            "Spanish (Mexico)",
            "es-MX",
            "es-MX-JorgeNeural",
            "Hola, soy un traductor de IA que asistirá en la llamada de hoy. Escucharás tanto mi voz como la voz del agente durante toda la conversación. Por favor, espera mientras te conectamos con un agente.",
            "Ahora estás conectado."
        ),
        new LanguageConfig(
            "Arabic",
            "ar-SA",
            "ar-SA-HamedNeural",
            "مرحبًا، أنا مترجم ذكاء اصطناعي سأساعد في المكالمة اليوم. ستسمع صوتي وصوت الوكيل طوال المحادثة. يرجى الانتظار بينما نقوم بربطك مع وكيل.",
            "أنت متصل الآن."
        ),
        new LanguageConfig(
            "Chinese (Mandarin, Simplified)",
            "zh-CN",
            "zh-CN-YunxiNeural",
            "你好, 我是一个AI翻译器, 今天将协助通话。在整个对话过程中, 您将听到我的声音和代理的声音。请稍等, 我们正在为您连接代理",
            "您现在已连接"
        ),
        new LanguageConfig(
            "Vietnamese",
            "vi-VN",
            "vi-VN-NamMinhNeural",
            "Xin chào, tôi là một phiên dịch AI sẽ hỗ trợ cuộc gọi hôm nay. Bạn sẽ nghe thấy cả giọng của tôi và giọng của nhân viên trong suốt cuộc trò chuyện. Vui lòng chờ trong khi chúng tôi kết nối bạn với một nhân viên.",
            "Bạn đã kết nối"
        ),
    ];
}