namespace ACSTranslate;

public record TranslationConfig(
    string AgentLanguage,
    string UserVoiceInAgentLanguage,
    string UserLanguage,
    string AgentVoiceInUserLanguage,
    string WelcomeMessage = "Hi there, I'm an AI translator that will be assisting with the call today, you will hear both my voice and the agents voice throughout the conversation, please wait while we connect you with an agent.",
    string ConnectedMessage = "You are now connected."
) {
    private static TranslationConfig Default => Arabic;
    public TranslatorOptions UserOptions 
        => new(AgentLanguage, UserLanguage, AgentVoiceInUserLanguage);
    public TranslatorOptions AgentOptions 
        => new(UserLanguage, AgentLanguage, UserVoiceInAgentLanguage);


    private static TranslationConfig EnglishBase => new ("en-US", "en-AU-DarrenNeural", "en-US", "en-US-AdamMultilingualNeural");
    private static TranslationConfig German => EnglishBase with {
        UserLanguage = "de-DE",
        AgentVoiceInUserLanguage = "de-DE-ChristophNeural",
        WelcomeMessage = "Hallo, ich bin ein KI-Übersetzer, der heute bei dem Anruf helfen wird. Sie werden sowohl meine Stimme als auch die Stimme des Agenten während des Gesprächs hören. Bitte warten Sie, während wir Sie mit einem Agenten verbinden.",
        ConnectedMessage = "Sie sind jetzt verbunden."
    };
    private static TranslationConfig French  => EnglishBase with {
        UserLanguage = "fr-FR",
        AgentVoiceInUserLanguage = "fr-FR-HenriNeural",
        WelcomeMessage = "Bonjour, je suis un traducteur IA qui vous assistera lors de l'appel aujourd'hui. Vous entendrez à la fois ma voix et celle de l'agent tout au long de la conversation. Veuillez patienter pendant que nous vous connectons avec un agent.",
        ConnectedMessage = "Vous êtes maintenant connecté."
    };
    private static TranslationConfig SpanishMexico => EnglishBase with {
        UserLanguage = "es-MX",
        AgentVoiceInUserLanguage = "es-MX-JorgeNeural",
        WelcomeMessage = "Hola, soy un traductor de IA que asistirá en la llamada de hoy. Escucharás tanto mi voz como la voz del agente durante toda la conversación. Por favor, espera mientras te conectamos con un agente.",
        ConnectedMessage = "Ahora estás conectado."
    };
    private static TranslationConfig Arabic => EnglishBase with {
        UserLanguage = "ar-SA",
        AgentVoiceInUserLanguage = "ar-SA-HamedNeural",
        WelcomeMessage = "مرحبًا، أنا مترجم ذكاء اصطناعي سأساعد في المكالمة اليوم. ستسمع صوتي وصوت الوكيل طوال المحادثة. يرجى الانتظار بينما نقوم بربطك مع وكيل.",
        ConnectedMessage = "أنت متصل الآن."
    };
    private static TranslationConfig ChineseMandarinSimplified => EnglishBase with {
        UserLanguage = "zh-CN",
        AgentVoiceInUserLanguage = "zh-CN-YunxiNeural",
        WelcomeMessage = "你好, 我是一个AI翻译器, 今天将协助通话。在整个对话过程中, 您将听到我的声音和代理的声音。请稍等, 我们正在为您连接代理",
        ConnectedMessage = "您现在已连接"
    };
    private static TranslationConfig Vietnamese => EnglishBase with {
        UserLanguage = "vi-VN",
        AgentVoiceInUserLanguage = "vi-VN-NamMinhNeural",
        WelcomeMessage = "Xin chào, tôi là một phiên dịch AI sẽ hỗ trợ cuộc gọi hôm nay. Bạn sẽ nghe thấy cả giọng của tôi và giọng của nhân viên trong suốt cuộc trò chuyện. Vui lòng chờ trong khi chúng tôi kết nối bạn với một nhân viên.",
        ConnectedMessage = "Bạn đã kết nối"
    };

    public static TranslationConfig GetConfig(string? language)
    {
        return language?.ToLower() switch
        {
            "de" => German,
            "de-de" => German,

            "fr" => French,
            "fr-fr" => French,

            "es" => SpanishMexico,
            "es-mx" => SpanishMexico,

            "ar" => Arabic,
            "ar-sa" => Arabic,

            "zh" => ChineseMandarinSimplified,
            "zh-cn" => ChineseMandarinSimplified,

            "vi" => Vietnamese,
            "vi-vn" => Vietnamese,

            _ => Default
        };
    }
}