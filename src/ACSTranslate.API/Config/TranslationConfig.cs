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

    //sort the lanuges alphabetically by Language
        private static readonly LanguageConfig[] _languages = [
        new LanguageConfig(
            "English",
            "en-US",
            "en-AU-DarrenNeural",
            "Hi there, I'm an AI translator that will be assisting with the call today, you will hear both my voice and the agents voice throughout the conversation, please wait while we connect you with an agent.",
            "Welcome to Telstra. How can I help you?"
        ),
        new LanguageConfig(
            "Arabic",
            "ar-SA",
            "ar-SA-HamedNeural",
            "مرحبًا، أنا مترجم ذكاء اصطناعي سأساعد في المكالمة اليوم. ستسمع صوتي وصوت الوكيل طوال المحادثة. يرجى الانتظار بينما نقوم بربطك مع وكيل.",
            "مرحبًا بكم في Telstra. كيف يمكنني مساعدتك؟"
        ),
        new LanguageConfig(
            "Chinese (Mandarin, Simplified)",
            "zh-CN",
            "zh-CN-YunxiNeural",
            "你好, 我是一个AI翻译器, 今天将协助通话。在整个对话过程中, 您将听到我的声音和代理的声音。请稍等, 我们正在为您连接代理",
            "欢迎来到Telstra。我能为您做什么？"
        ),
        new LanguageConfig(
            "Dutch",
            "nl-NL",
            "nl-NL-ColetteNeural",
            "Hallo, ik ben een AI-vertaler die vandaag zal assisteren bij het gesprek. Je zult zowel mijn stem als die van de agent horen tijdens het hele gesprek. Wacht alstublieft terwijl we u verbinden met een agent.",
            "Welkom bij Telstra. Hoe kan ik u helpen?"
        ),        
        new LanguageConfig(
            "French",
            "fr-FR",
            "fr-FR-HenriNeural",
            "Bonjour, je suis un traducteur IA qui vous assistera lors de l'appel aujourd'hui. Vous entendrez à la fois ma voix et celle de l'agent tout au long de la conversation. Veuillez patienter pendant que nous vous connectons avec un agent.",
            "Bienvenue chez Telstra. Comment puis-je vous aider ?"
        ),
        new LanguageConfig(
            "German",
            "de-DE",
            "de-DE-ChristophNeural",
            "Hallo, ich bin ein KI-Übersetzer, der heute bei dem Anruf helfen wird. Sie werden sowohl meine Stimme als auch die Stimme des Agenten während des Gesprächs hören. Bitte warten Sie, während wir Sie mit einem Agenten verbinden.",
            "Willkommen bei Telstra. Wie kann ich Ihnen helfen?"
        ),
        new LanguageConfig(
            "Hindi",
            "hi-IN",
            "hi-IN-SwaraNeural",
            "नमस्ते, मैं एक एआई अनुवादक हूँ जो आज कॉल में सहायता करेगी। आप पूरे संवाद के दौरान मेरी आवाज़ और एजेंट की आवाज़ दोनों सुनेंगे। कृपया प्रतीक्षा करें जब तक हम आपको एक एजेंट से जोड़ते हैं।",
            "Telstra में आपका स्वागत है। मैं आपकी कैसे मदद कर सकती हूँ?"
        ),
        new LanguageConfig(
            "Italian",
            "it-IT",
            "it-IT-ElsaNeural",
            "Ciao, sono un traduttore AI che assisterà alla chiamata di oggi. Sentirai sia la mia voce che quella dell'agente durante tutta la conversazione. Attendi mentre ti connettiamo con un agente.",
            "Benvenuto in Telstra. Come posso aiutarti?"
        ),
        new LanguageConfig(
            "Japanese",
            "ja-JP",
            "ja-JP-NanamiNeural",
            "こんにちは、私は今日の通話を支援するAI翻訳者です。会話の間、私の声とエージェントの声の両方が聞こえます。エージェントに接続するまでお待ちください。",
            "Telstraへようこそ。どのようにお手伝いできますか？"
        ),
        new LanguageConfig(
            "Korean",
            "ko-KR",
            "ko-KR-SunHiNeural",
            "안녕하세요, 저는 오늘 통화에 도움을 줄 AI 번역기입니다. 대화 내내 제 목소리와 상담원의 목소리를 모두 들으실 수 있습니다. 상담원과 연결되는 동안 기다려 주세요.",
            "Telstra에 오신 것을 환영합니다. 어떻게 도와드릴까요?"
        ),
        new LanguageConfig(
            "Portuguese (Brazil)",
            "pt-BR",
            "pt-BR-AntonioNeural",
            "Olá, sou um tradutor de IA que ajudará na chamada de hoje. Você ouvirá tanto a minha voz quanto a do agente durante toda a conversa. Por favor, aguarde enquanto conectamos você a um agente.",
            "Bem-vindo à Telstra. Como posso ajudá-lo?"
        ),
        new LanguageConfig(
            "Russian",
            "ru-RU",
            "ru-RU-DmitryNeural",
            "Здравствуйте, я AI-переводчик, который будет помогать в сегодняшнем звонке. Вы услышите как мой голос, так и голос агента на протяжении всего разговора. Пожалуйста, подождите, пока мы подключаем вас к агенту.",
            "Добро пожаловать в Telstra. Как я могу вам помочь?"
        ),
        new LanguageConfig(
            "Spanish (Mexico)",
            "es-MX",
            "es-MX-JorgeNeural",
            "Hola, soy un traductor de IA que asistirá en la llamada de hoy. Escucharás tanto mi voz como la voz del agente durante toda la conversación. Por favor, espera mientras te conectamos con un agente.",
            "Bienvenido a Telstra. ¿Cómo puedo ayudarte?"
        ),
        new LanguageConfig(
            "Swedish",
            "sv-SE",
            "sv-SE-SofieNeural",
            "Hej, jag är en AI-översättare som kommer att hjälpa till med samtalet idag. Du kommer att höra både min röst och agentens röst under hela konversationen. Vänligen vänta medan vi kopplar dig till en agent.",
            "Välkommen till Telstra. Hur kan jag hjälpa dig?"
        ),
        new LanguageConfig(
            "Turkish",
            "tr-TR",
            "tr-TR-EmelNeural",
            "Merhaba, bugün aramada size yardımcı olacak bir yapay zeka çevirmeniyim. Konuşma boyunca hem benim sesimi hem de temsilcinin sesini duyacaksınız. Sizi bir temsilciyle bağlarken lütfen bekleyin.",
            "Telstra'ya hoş geldiniz. Size nasıl yardımcı olabilirim?"
        ),
        new LanguageConfig(
            "Vietnamese",
            "vi-VN",
            "vi-VN-NamMinhNeural",
            "Xin chào, tôi là một phiên dịch AI sẽ hỗ trợ cuộc gọi hôm nay. Bạn sẽ nghe thấy cả giọng của tôi và giọng của nhân viên trong suốt cuộc trò chuyện. Vui lòng chờ trong khi chúng tôi kết nối bạn với một nhân viên.",
            "Chào mừng bạn đến với Telstra. Tôi có thể giúp gì cho bạn?"
        )
    ];
}