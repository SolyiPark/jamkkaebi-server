using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jamkkaebi.Npc
{
    /// <summary>SLM 에 보낼 프롬프트 한 벌.</summary>
    public sealed class NpcPrompt
    {
        /// <summary>
        /// 시스템 프롬프트를 미리 읽힐 때(INpcModelBackend.PrepareAsync) 함께 보내는 짧은 사용자 메시지.
        /// 무엇이든 되지만, 시스템 메시지 뒤에서 갈라지므로 짧을수록 좋다.
        /// </summary>
        public const string PrepareUser = "준비";

        /// <summary>
        /// 시스템 메시지: 페르소나(이름·성격·감정 표현·말투·예시)와 공통 규칙. <b>도깨비마다 고정</b>이다 —
        /// llama.cpp 는 앞부분이 글자 하나까지 같으면 이미 읽은 것을 다시 쓰므로, 같은 도깨비와 이어 말할 때 다시 읽지 않는다.
        /// </summary>
        public string System { get; set; }

        /// <summary>사용자 메시지: 지금 상대와 태도, 이번 행동에 맞는 페르소나 칸, 상황, 알고 있는 것, 기억, 후보, 대사 길이.</summary>
        public string User { get; set; }

        /// <summary>
        /// JSON 스키마 형식 제약 (LM Studio · llama.cpp 서버의 response_format). 행동·대상은 후보 안의 값만 허용한다.
        /// </summary>
        public string JsonSchema { get; set; }

        /// <summary>
        /// GBNF 문법 형식 제약 (llama.cpp · LLMUnity 의 grammar). JSON 스키마와 달리 행동과 대상의 짝까지 막고,
        /// 대사에 줄바꿈을 넣지 못하게 한다. 형식 제약이 없는 런타임(LiteRT-LM 등)에서는 쓰지 않아도 된다 —
        /// 시험에서 제약 없이도 형식이 지켜졌다.
        /// </summary>
        public string Grammar { get; set; }

        /// <summary>후보 행동 ID (처음 나온 순서).</summary>
        public List<string> ActionIds { get; set; }

        /// <summary>후보 대상 ID (처음 나온 순서).</summary>
        public List<string> TargetIds { get; set; }
    }

    public sealed class PromptOptions
    {
        public PromptOptions()
        {
            IncludeExamples = true;
            SentenceGrammar = true;
            DefaultLineChars = 16; // 2026-11-17 21:27: 관리 감사 평균 출력 32토큰·안부 26토큰(탭 20토큰) — 20자 목표에 모델이 30자를 써서 응답 시간(토큰당 약 40ms)이 늘었다
            TargetLabels = new Dictionary<string, string>();
            // 말풍선에 맞춘 길이. 탭 인사·손님 관람은 15자: 목표를 20자로 적었더니 모델이 중앙값 25자(출력 24토큰)를 써서, 인사 한마디에 응답이 2초를 넘었다
            // (2026-11-16 시험: 응답 ms ≈ 680 + 63 × 출력 토큰). 15자로 적으면 약 19자·18토큰으로 줄어 0.3초가량 빨라진다. run.py 의 INPUT_LINE_CHARS 와 같다
            LineChars = new Dictionary<string, int>
            {
                { NpcInputs.Tap, 15 },
                { NpcInputs.Visit, 15 }
            };
            MaxSentences = new Dictionary<string, int>();
            // 모든 대사에 '2문장 이내'를 붙여 봤지만 Mi:dm 은 그대로, Kanana 는 더 잘렸다. 기본은 적지 않는다 (run.py --max-sentences 와 같다)
            DefaultMaxSentences = 0;
        }

        /// <summary>페르소나 예시 대사를 싣는지. 끄면 "예시는 말투만 참고" 규칙도 뺀다.</summary>
        public bool IncludeExamples { get; set; }

        /// <summary>
        /// 출력 형식(JSON 모양, 후보 안에서 고르기)을 프롬프트에 적는지. 기본은 false — GBNF·JSON 스키마가 형식을 강제하므로
        /// 설명이 필요 없다. 형식 제약이 없는 런타임(LlmUnityGrammar.None, LiteRT-LM 등)에서만 켠다. 사용자 메시지 끝에만 붙어
        /// 시스템 메시지는 그대로다.
        /// </summary>
        public bool DescribeFormat { get; set; }

        /// <summary>
        /// GBNF 의 대사 규칙을 "문장 단위"로 둘지 (기본 true). 문장마다 30자 안에 끝나는 부호(. ! ? ~ …)가 반드시 오고 문장은 둘까지다 — 대사가 문장 중간에서
        /// 끊기지 않고(토큰 한도로 자르는 대신 문법이 끝맺게 한다), 길게 늘어놓지 않아 쓰는 토큰도 준다. false 면 예전처럼 따옴표 안을 길이 제한 없이 쓴다
        /// (llama.cpp 가 이 문법을 못 읽는 기기에서 되돌리는 용도).
        /// </summary>
        public bool SentenceGrammar { get; set; }

        /// <summary>LineChars 에 없는 선택지의 대사 글자 수.</summary>
        public int DefaultLineChars { get; set; }

        /// <summary>player · none 말고 다른 대상(건물 등)을 보여 줄 이름. 예: "building:1" → "석굴암".</summary>
        public Dictionary<string, string> TargetLabels { get; set; }

        /// <summary>선택지별 대사 글자 수.</summary>
        public Dictionary<string, int> LineChars { get; set; }

        /// <summary>선택지별 문장 수 제한. 작은 모델은 글자 수보다 문장 수를 더 잘 지킨다.</summary>
        public Dictionary<string, int> MaxSentences { get; set; }

        /// <summary>MaxSentences 에 없는 선택지의 문장 수 제한. 0 이면 적지 않는다.</summary>
        public int DefaultMaxSentences { get; set; }
    }

    /// <summary>
    /// 요청 한 건을 SLM 프롬프트로 조립한다.
    ///
    /// <para>tools/npc-prompt-test/run.py 의 single 방식과 <b>같은 프롬프트</b>를 만든다 (Verify 에서 글자 단위로 비교한다).
    /// 문구를 바꾸면 run.py 도 같이 바꾸고 다시 시험한다. 무엇을 어디에 싣는지는 tools/npc-prompt-test/README.md 의 "프롬프트 구성".</para>
    /// <list type="bullet">
    /// <item>시스템 메시지는 도깨비마다 고정이다(SystemFor). 상대·선택지·상황에 따라 바뀌는 것은 모두 사용자 메시지에 둔다 —
    /// llama.cpp 가 같은 도깨비의 시스템 메시지를 다시 읽지 않게 하려는 것이다.</item>
    /// <item>가치관·상황별 말투(관리를 받으면)는 이번 후보 행동에 필요한 칸만 사용자 메시지에 싣는다.</item>
    /// <item>출력 형식 설명은 형식 제약이 없을 때만(PromptOptions.DescribeFormat) 싣는다.</item>
    /// <item>정해진 대사를 쓰는 요청(이야기 듣기)은 만들지 않는다(null).</item>
    /// </list>
    /// </summary>
    public sealed class PromptAssembler
    {
        // 2026-11-13 세 번째 시험: 프롬프트에 하지 말 것('~하지 않는다', '넣지 않는다')을 늘어놓으면 작은 모델이 그 말에 끌려 설명문을 읊거나 헷갈렸다.
        // 규칙은 해야 할 것만 짧게 적는다. 하지 말아야 할 것(밀어내기·낮춰 부르기·자기 이름·영어·몸짓 설명·존댓말·날씨…)은
        // 프롬프트에 적지 않고 말투 다듬기(NormalizeSpeech)와 출력 검사(NpcOutputValidator·NpcLineFilter)가 맡는다.
        // 규칙이 짧을수록 읽는 시간(응답 시간의 대부분)도 줄어든다. 규칙에 '네 몸', '네 것'을 쓰면 "네 자개는"으로 옮기므로 '네'를 쓰지 않는다.
        // 2026-11-17 18:21 시험: 모델이 자기가 전시관을 구경하러 온 것처럼 말했다("전시관을 둘러보니 참 멋진데!", "반가워서 아침부터 달려왔어", "반가워서 왔어"). 도깨비는 주인의 전시관에 살고
        // 찾아온 상대(주인·친구)를 맞이하는 쪽이다. 한 줄로 못 박는다 (시스템 메시지는 도깨비마다 한 번만 읽는다)
        // 2026-11-17 22:07 시험: 규칙의 "전시관"을 읽은 모델이 "전시관이 참 포근하네"처럼 우리 전시관의 인상을 말했다. 명사 "전시관"을 규칙·손님 상황 문장에서 뺐다
        public const string RuleBond = "- 너는 이곳에 살며, 찾아온 상대가 정이 들도록 다정하게 말한다.";
        public const string RuleFacts = "- 자료에 있는 일만 말하고, 시간대와 며칠 전은 적힌 대로 말한다.";
        public const string RuleRole = "- 관리는 상대가 너를 닦아 주는 일이다. 바라는 것은 상대에게 부탁하고, 몸과 기분은 느끼는 대로 '내 ~'라고 말한다."; // 2026-11-17: "주인"을 읽은 모델이 하인처럼 높였다 — 모델이 읽는 글에서는 호칭으로 쓴다(NpcLineChecks.OwnerToCall)
        public const string RuleLine = "- 소리 내어 하는 한국어 말만 쓴다.";
        // 도깨비는 모두 반말이라 말투를 공통 규칙으로 둔다. 어긋난 어미는 말투 다듬기가 고치고 출력 검사기가 문장마다 어미를 본다(SpeechMismatch)
        public const string RuleSpeech = "- 친한 친구에게 말하듯 편한 현대 반말로 말한다.";
        // 한 문장이면 출력 토큰이 줄어 응답이 빨라진다 (응답 시간은 읽는 토큰 + 쓰는 토큰에 비례한다)
        public const string RuleOrder = "- 지금 상황에 맞는 말을 짧은 한 문장으로, 우리말 어순대로 말한다. 묻는 말만 ?로 끝낸다.";
        // 2026-11-17: "곁들일 거리나 지금 상황의 한마디를 덧붙여"를 읽은 모델이 없는 사정을 한마디로 지어냈다("궁금한 점이 많겠네", "오늘 밤은 너무 재미있었어"). 덧붙이라고 하지 않고 짧게 말하게 한다
        public const string RuleVary = "- 첫 낱말로 시작해 매번 다르게 말한다.";
        // (규칙 줄 "부탁은 '~해 줘'처럼 바로 말한다"는 뺐다: 부탁의 꼴은 '할 일' 줄과 첫 낱말이 이미 정하고, 없으면 코드가 채운다(AskFallback). 읽는 토큰을 줄인다)

        /// <summary>프롬프트에 싣는 예시 대사 수. 예시를 줄이면 읽는 토큰이 줄어 처음 한 번의 응답이 빨라진다. run.py 의 PROMPT_EXAMPLES 와 같다.</summary>
        public const int PromptExamples = 2;

        /// <summary>
        /// 곁들일 거리 줄의 이름. 같은 상황이 거듭되면 프롬프트가 글자 하나 다르지 않아 작은 모델이 비슷한 대사를 내서,
        /// 요청마다 페르소나의 flavors 에서 하나씩 돌려 싣는다 (몇 번째인지는 ReactionRequest.Variation).
        /// </summary>
        public const string FlavorLabel = "떠올린 것";

        /// <summary>
        /// 곁들일 거리 뒤에 붙이는 말. "또렷해진 구름 무늬가 고마워요"처럼 거리를 고마움의 대상에 끼워 넣어서, 그것에 대한 느낌으로 쓰게 한다.
        /// "한마디 넣는다"로 두었더니 거리를 둘째 문장에 붙여 첫 문장은 늘 상황 문장을 옮긴 말("이틀 전에 닦아 주셔서 고마워요")이었고,
        /// 길이로 자르면 거리가 잘려 같은 대사만 남았다(2026-11-11 20:02). 바뀌는 거리로 말을 시작하게 해 앞부분이 요청마다 달라지게 한다.
        /// </summary>
        public const string FlavorHint = "(내 이야기)";

        /// <summary>
        /// 첫 낱말 줄의 이름. 관리 감사가 거의 모두 "관리해 줘서 고마워"로 같았다(2026-11-13 세 번째 시험: 같은 시나리오 4번 중 3~4번, 두 번 다시 만들어도 같아서
        /// 기본 대사로 넘어갔다). 프롬프트가 요청마다 거의 같으면 작은 모델은 같은 말을 내므로, 요청마다 다른 첫 낱말을 하나 정해 준다
        /// ("덕분에", "이제", "한결"…). 첫 낱말이 다르면 뒤도 달라진다. 목록은 상황마다 여섯이고 곁들일 거리와 같은 번호(Variation)로 돌린다.
        /// </summary>
        public const string StarterLabel = "첫 낱말";

        // 2026-11-14 시험: "잘"은 "잘 왔지? 다음엔 꼭 같이 가자!"처럼 없는 약속을 지어냈고, "오늘은"은 "오늘은 관리해 줘서 고마워"·"오늘은 말 그림 보러 왔구나?"처럼 어색했다.
        // 곁들일 거리의 힌트도 "이 느낌을 곁들인다"는 "말 그림 보러 왔구나?"처럼 거리를 일어난 일로 읽어서 "(내 이야기)"로 바꿨다
        // 2026-11-17 시험: "찾아와서"는 "찾아와서 반가워, 오늘 아침부터 부르네."처럼 방문과 부름을 뒤섞었고, "만나서"는 "만나서 반가워"로 거의 모두 같게 시작해 뺐다. "기다렸어"를 더했다
        private static readonly string[] StartersTap = { "안녕", "왔구나", "와", "어서", "드디어" }; // 2026-11-17 20:39 시험: "반가워"는 "반가워서 인사…·왔…"로 이어져 금칙어에 걸린 것이 4건이라 "어서"로 바꿨다("어서 왔어"는 말투 다듬기가 "어서 와"로 고친다) // 2026-11-17 19:43 시험: "기다렸어"는 그것만 대사로 내(S01 #2·S22 #1·S24 #1) 인사말뿐·반복으로 거르고 다시 만들었다. 이어 쓰게 되는 "드디어"(기다린 마음)로 바꿨다
        // 흐려진 안부 중 "마지막 관리가 며칠 전이었는지"를 말하는 요청(AskVariant == "days")의 첫 낱말
        private static readonly string[] StartersFadedDays = { "마지막 관리가", "마지막", "벌써", "어느새" }; // "마지막으로"는 "마지막으로 닦아주면 감사할 것 같아"(뜻이 뒤섞임)로 이어져 뺐다 (2026-11-17 21:09 시험 S03 #2)
        private static readonly string[] StartersFaded = { "살살", "괜찮으면", "시간 되면", "조심조심", "이왕이면" }; // 2026-11-17 21:45 시험 뒤: 흐려진 안부는 '이미 한 말'(상태·며칠 전) 뒤에 이어질 부탁의 말머리 // // 2026-11-17 18:21 시험: "점점"은 "점점 새로워지고 싶어", "혹시"는 "혹시, 오랜만에 깨워줘"처럼 뜬금없는 말로 이어져 "어느새"·"자꾸"로 바꿨다 // "조금"은 "조금 전 관리를 못 받아서"(금칙어)로 이어져 뺐다 (2026-11-17 시험 S03 #3)
        private static readonly string[] StartersClear = { "덕분에", "정말", "훨씬", "몸이", "개운하게", "깨끗하게" }; // 2026-11-17 21:27 시험: "이제"는 "이제 관리해 줘서 고마워"(때가 뒤섞임)로 이어져 "정말"로 바꿨다 // 2026-11-17 18:21 시험: "깨끗해졌어"·"개운해"는 그것만 대사로 내 "~하게"로 이어 쓰게 했다 // "깨끗해져서"는 "깨끗해져서 닦아줘서 고마워"처럼 '~서'를 거듭 이었다 (2026-11-17 시험 S04 #1)
        private static readonly string[] StartersVisit = { "안녕", "반가워", "와", "환영해", "마음껏 둘러봐", "천천히 둘러봐" }; // "편하게"는 "편하게 찾아와 줘서 고마워"처럼 이어졌다 (2026-11-17 19:43 시험 S05 #2) // "구경하러"는 "구경하러 온 친구를 반가이 맞아."처럼 상대를 남 얘기하듯 옮겼다 (2026-11-17 18:21 시험 S05 #4)
        // 단골에게는 "오랜만"을 부르는 말을 피한다 (3일 전에도 온 친구에게 "오랜만에 와 줘서"라고 해서 모두 거르고 기본 대사로 넘어갔다)
        private static readonly string[] StartersRecall = { "자주", "이번에도", "또", "다시", "반가워", "어서" }; // 2026-11-17 22:27 시험: "오늘도"는 "오늘도 정말 오래 만났네"(없는 일)·"오늘도 또 와줘서"(겹침)로 이어져 "어서"로 바꿨다

        /// <summary>이번 요청의 첫 낱말. 곁들일 거리가 없는 요청(지난 대화 꺼내기)은 null. run.py 의 starter_word 와 같다.</summary>
        public static string Starter(ReactionRequest request)
        {
            string key = StarterKey(request);
            if (key == null)
            {
                return null;
            }
            string[] list = key == "tap" ? StartersTap
                    : key == "faded" ? StartersFaded
                    : key == "clear" ? StartersClear
                    : request.Candidates != null && request.Candidates.Any(c => c != null && c.ActionId == NpcActions.WanderRecall) ? StartersRecall
                    : StartersVisit;
            return list[(((request.Variation + request.RelicId) % list.Length) + list.Length) % list.Length];
        }

        /// <summary>곁들일 거리 번호를 variation 으로 두었을 때의 첫 낱말 (request 는 그대로 둔다). 요청 관리자가 최근에 쓴 첫 낱말을 피해 번호를 고를 때 쓴다.</summary>
        public static string StarterAt(ReactionRequest request, int variation)
        {
            int saved = request.Variation;
            try
            {
                request.Variation = variation;
                return Starter(request);
            }
            finally
            {
                request.Variation = saved;
            }
        }

        // 작은 모델은 예시 대사와 사용자 메시지의 페르소나 칸·상황 문장을 그대로 옮겨, 같은 상황마다 같은 대사를 냈다
        public const string RuleExamples = "- 예시와 자료는 참고만 하고, 지금 상황에 맞게 새로 말한다.";

        // 기억이 있을 때만 사용자 메시지의 대사 줄에 붙인다
        public const string RuleNoRepeat = "'기억'의 일은 새 말로 떠올린다.";

        // 아래는 형식 제약이 없을 때만(DescribeFormat) 사용자 메시지 끝에 붙인다.
        // 제약이 없으면 행동 칸에 설명이나 선택지 이름을 적는 일이 있어 '영문 ID'라고 못 박는다
        public const string RuleChoose = "반드시 후보 중 하나의 행동과, 그 행동에 적힌 대상 중 하나를 고른다. 후보에 없는 것은 만들지 않는다.";
        public const string RuleChooseAction = "반드시 후보 중 하나의 행동을 고른다. 후보에 없는 것은 만들지 않는다.";
        public const string RuleJson = "JSON 한 개만 출력한다. 설명이나 다른 글은 쓰지 않는다.";
        public const string FormatLine = "출력 형식: {\"action\": \"후보의 행동 ID(영문)\", \"target\": \"대상 ID(영문)\", \"line\": \"대사\"}";

        // 응답 시간은 거의 출력 토큰 수에 비례한다(Kanana 2B 는 초당 6토큰 안팎). 대상이 하나뿐이면 target 칸을, 행동도 하나뿐이면 action 칸까지
        // 빼서 JSON 틀이 차지하는 토큰(약 20개)을 줄인다. 빠진 칸은 NpcOutputValidator 가 후보로 채운다. 형식 제약(GBNF·스키마)이 있을 때는 그대로 둔다
        public const string FormatActionLine = "출력 형식: {\"action\": \"후보의 행동 ID(영문)\", \"line\": \"대사\"}";
        public const string FormatLineOnly = "출력 형식: {\"line\": \"대사\"}";
        private const string CandidatesHeadAction = "후보 (action 에는 행동 ID 를 아래 영문 그대로 적는다):";

        private const string CandidatesHead = "후보:";
        private const string CandidatesHeadFree = "후보 (action 에는 행동 ID, target 에는 대상 ID 를 아래 영문 그대로 적는다):";

        // 장면에 따라 사용자 메시지에 싣는 페르소나 칸 (필드, 프롬프트 이름) — 이 순서로 싣는다
        private static readonly string[][] PersonaFields =
        {
            new[] { "values", "가치관" },
            new[] { "onCare", "관리를 받으면" }
        };

        // 행동마다 싣는 칸. 상관없는 칸까지 실으면 프롬프트만 길어지고, 탭 인사에 관리 감사 문구를 섞는 식으로 끌어 쓴다.
        // 좋아하는 것·싫어하는 것 칸은 뺐다(2026-11-13): 상황과 맞지 않는 말을 대사에 끼워 넣었다.
        // 끌려갈 때(struggle_*)는 대사가 없어 칸이 없다
        private static readonly Dictionary<string, string[]> ActionFields = new Dictionary<string, string[]>
        {
            { NpcActions.Greet, new string[0] },                     // 가치관은 성격(시스템 메시지)에 있다. 읽을 글을 줄인다
            { NpcActions.AnswerStatus, new[] { "onCare" } },
            { NpcActions.RecallTalk, new string[0] },                // 꺼낼 기억 하나에 집중한다
            { NpcActions.ThankJoyful, new[] { "onCare" } },
            { NpcActions.ThankCalm, new[] { "onCare" } },
            { NpcActions.WanderGreet, new string[0] },               // 20자 말풍선이라 상대 태도만으로 충분하다
            { NpcActions.WanderRecall, new string[0] }
        };

        // 관리를 못 받은 상태(선명도가 흐려짐·재봉인)에서는 '관리를 받으면' 칸을 싣지 않는다. 실으면 Mi:dm 이 닦아 주지도 않았는데
        // "닦아 주셔서 감사합니다" 하고 고마워했다 (run.py 의 UNCARED_CLARITY)
        private static readonly HashSet<string> UncaredClarity = new HashSet<string> { "흐려짐", "재봉인" };

        // 영어 키만 주면 작은 모델이 방문 횟수 같은 사실을 놓친다
        private static readonly Dictionary<string, string> FactLabels = new Dictionary<string, string>
        {
            { NpcFactKeys.VisitCount30d, "최근 30일 동안 온 횟수" },
            { NpcFactKeys.Awakening, "각성 단계" },
            { NpcFactKeys.Clarity, "몸 상태" },
            { NpcFactKeys.LastCareDaysAgo, "마지막 관리(며칠 전)" },
            { NpcFactKeys.CareResult, "관리 결과" },
            { NpcFactKeys.TimeOfDay, "지금 시간대" },
            { NpcFactKeys.VisitDays, "최근 방문한 날" }
        };

        // 게임과 규칙 기반 생성기만 쓰는 사실. 프롬프트에는 싣지 않는다
        // 방문 횟수(visit_count_30d)도 상황 문장("최근 30일 동안 7번 온 친구")에 있다. 사실 칸의 "선명도"는 "선명도가 흐려졌어"처럼 낱말을 그대로 읊어 "몸 상태"로 바꿨다
        // 지금 시간대(time_of_day)는 상황 문장("주인이 아침에 찾아와 너를 탭했다")에 있어 사실 칸에 또 싣지 않는다 — 출력 검사만 쓴다
        private static readonly HashSet<string> HiddenFactKeys = new HashSet<string> { NpcFactKeys.StoryIndex, NpcFactKeys.Awakening, NpcFactKeys.TimeOfDay, NpcFactKeys.VisitCount30d, NpcFactKeys.CareResult }; // care_result: "관리 결과: 재봉인→선명"을 읽은 모델이 "재봉인이 선명해졌네"라고 했다 (2026-11-17 21:09 시험 S09 #4). 관리했다는 사실은 상황 문장에 있다

        private readonly PersonaCatalog personas;
        private readonly ActionCatalog actions;
        private readonly PromptOptions options;

        public PromptAssembler(PersonaCatalog personas, ActionCatalog actions) : this(personas, actions, new PromptOptions())
        {
        }

        public PromptAssembler(PersonaCatalog personas, ActionCatalog actions, PromptOptions options)
        {
            if (personas == null)
            {
                throw new ArgumentNullException("personas");
            }
            if (actions == null)
            {
                throw new ArgumentNullException("actions");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            this.personas = personas;
            this.actions = actions;
            this.options = options;
        }

        /// <summary>
        /// 프롬프트를 만든다. 페르소나가 아직 없는 도깨비, 후보가 없는 요청, 정해진 대사를 쓰는 요청(이야기 듣기)이면 null —
        /// 그때는 규칙 기반 생성기로 대신한다.
        /// </summary>
        public NpcPrompt Build(ReactionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            Persona persona = personas.Find(request.RelicId);
            if (persona == null || request.Candidates == null || request.Candidates.Count == 0
                    || NpcActions.SkipsModel(request.Candidates))
            {
                return null;
            }

            List<string> actionIds = Distinct(request.Candidates.Select(c => c.ActionId));
            List<string> targetIds = Distinct(request.Candidates.SelectMany(c => c.TargetIds ?? new List<string>()));

            var head = new List<string>(PartnerLines(persona, request));
            head.AddRange(SituationLines(request));
            string flavor = Flavor(persona, request);
            if (flavor != null)
            {
                head.Add(FlavorLine(flavor));
            }
            string starter = Starter(request);
            if (starter != null)
            {
                head.Add(StarterLabel + ": " + starter);
            }
            // 모델이 적을 칸: 행동·대상이 하나뿐이면 line, 대상만 하나뿐이면 action+line, 아니면 전부 (run.py 의 output_shape)
            // 형식 제약(GBNF·스키마)이 있어도 같은 모양으로 줄인다 — 기기에서 응답 시간은 쓰는 토큰 수에 비례하고, JSON 틀 `{"action": "greet", "target": "player", "line": "`
            // 이 약 17토큰, `{"line": "` 이 약 5토큰이다. 빠진 칸은 NpcOutputValidator 가 후보로 채운다
            int shape = targetIds.Count != 1 ? 2 : (actionIds.Count == 1 ? 0 : 1);
            string user = string.Join("\n", head.ToArray()) + "\n\n" + CandidatesBlock(request, targetIds, shape) + "\n" + LineRule(request, persona);
            if (options.DescribeFormat)
            {
                user += "\n\n" + (shape == 0 ? "" : (shape == 1 ? RuleChooseAction : RuleChoose) + "\n") + RuleJson + "\n"
                        + (shape == 0 ? FormatLineOnly : (shape == 1 ? FormatActionLine : FormatLine));
            }

            return new NpcPrompt
            {
                System = SystemFor(persona),
                User = user,
                JsonSchema = Schema(actionIds, targetIds, shape),
                Grammar = Grammar(request.Candidates, shape, options.SentenceGrammar),
                ActionIds = actionIds,
                TargetIds = targetIds
            };
        }

        /// <summary>
        /// 그 도깨비의 시스템 메시지. 요청과 상관없이 늘 같다 — 탭해 확대할 때 미리 읽혀 둘 때 쓴다. 페르소나가 없으면 null.
        /// </summary>
        public string SystemFor(int relicId)
        {
            Persona persona = personas.Find(relicId);
            return persona != null ? SystemFor(persona) : null;
        }

        private string SystemFor(Persona p)
        {
            var lines = new List<string>
            {
                // 이름은 싣지 않는다: 이름을 읽은 모델이 "훨씬 선명해진 향산이,"·"향산이가 인사해"처럼 자기 이름을 말했다 (자기 이름은 검사가 지운다). 성격·말씨로 도깨비가 갈린다
                "너는 모바일 게임 '잠깨비'의 도깨비다. " + p.origin + "에 깃든 정령이다.",
                "성격: " + p.traits
            };
            // '감정 표현' 줄은 싣지 않는다 (2026-11-17 21:45 시험): "기쁘면 말 앞에 '어흠'을 붙인다"를 읽은 모델이 "다시 와 줘서 어흠, 고마워!"·"오늘 또 보니 어흠."처럼 말 가운데에 끼웠고,
            // "기쁘면 헛기침을 한다"류는 몸짓을 서술했다("폴짝폴짝 뛰는구나"). 말씨는 '말투' 줄과 예시가 맡는다. run.py 의 system_block 과 같다
            lines.Add("말투: " + p.speechStyle);
            if (options.IncludeExamples && p.examples != null && p.examples.Count > 0)
            {
                lines.Add("대사 예시:");
                lines.AddRange(p.examples.Take(PromptExamples).Select(e => "- " + e));
            }
            lines.Add("");
            lines.Add("규칙:");
            lines.Add(RuleBond);
            lines.Add(RuleFacts);
            lines.Add(RuleRole);
            lines.Add(RuleLine);
            lines.Add(RuleSpeech);
            lines.Add(RuleOrder);
            lines.Add(RuleVary);
            if (options.IncludeExamples)
            {
                lines.Add(RuleExamples);
            }
            return string.Join("\n", lines.ToArray());
        }

        // 사용자 메시지의 맨 앞: 지금 상대와 태도, 이번 후보 행동에 맞는 페르소나 칸.
        // 같은 도깨비·같은 상대면 이 부분도 앞 요청과 같아 다시 읽지 않는 범위가 늘어난다.
        // 두 태도를 다 실으면 주인에게 손님 태도로 말해서 지금 상대의 것만 싣는다
        private static bool HasBatchim(string word)
        {
            char last = word[word.Length - 1];
            return last >= '가' && last <= '힣' && (last - 0xAC00) % 28 != 0;
        }

        private static IEnumerable<string> PartnerLines(Persona p, ReactionRequest request)
        {
            // 호칭은 친밀도 3단계(주인은 관리 12번, 손님은 30일에 12번 이상 방문)부터 닉네임, 아니면 '발굴자'. 꼭 부르지 않아도 된다 (모든 대사에 넣으면 부담스럽다)
            // 닉네임·'발굴자'를 대사에 꼭 넣을 필요는 없지만 가끔은 나와야 정이 든다: 세 번에 한 번(Variation 0·3·6…)은 "한 번 불러 준다"고 적고,
            // 나머지는 호칭만 알려 준다 (적어 두면 모델이 부르기도 한다). "도윤님"처럼 '님'을 붙이면 출력 직전에 뗀다(NpcLineChecks.NormalizeSpeech)
            string address = "호칭: " + request.AddressName + (request.Variation % 3 == 0 ? "(한 번 불러 준다)" : "");
            List<string> lines = request.Mode == InteractionMode.Owner
                    // 호칭이 프롬프트에 네 번(지금 상대·호칭·대할 때·상황) 나와 "발굴자가 오래 걸리지 않네"처럼 호칭을 주어로 엉뚱한 말을 이었다 → 두 번(지금 상대·상황)으로 줄인다 (읽는 토큰도 준다)
                    ? new List<string> { "지금 상대: " + request.AddressName + (request.Variation % 3 == 0 ? "(한 번 불러 준다)" : ""), "대하는 태도: " + p.toOwner }
                    : new List<string> { "지금 상대: 친구(구경만 한다)", address, "친구를 대할 때: " + p.toVisitor };

            var wanted = new HashSet<string>();
            foreach (ActionCandidate candidate in request.Candidates)
            {
                string[] fields;
                if (candidate != null && candidate.ActionId != null && ActionFields.TryGetValue(candidate.ActionId, out fields))
                {
                    wanted.UnionWith(fields);
                }
            }
            if (request.Facts != null
                    && request.Facts.Any(f => f != null && f.Key == NpcFactKeys.Clarity && UncaredClarity.Contains(f.Value ?? "")))
            {
                wanted.Remove("onCare");
            }
            foreach (string[] field in PersonaFields)
            {
                string value = PersonaField(p, field[0]);
                if (wanted.Contains(field[0]) && !string.IsNullOrEmpty(value))
                {
                    lines.Add(field[1] + ": " + value);
                }
            }
            return lines;
        }

        /// <summary>
        /// 곁들일 거리 목록의 이름 (run.py 의 flavor_key 와 같다). 탭·손님 관람은 그 선택지, 관리하기와 선명한 안부는 clear,
        /// 흐려진 안부는 faded. 지난 대화 꺼내기는 그 기억 하나에 집중하도록 싣지 않는다(null). 끌려갈 때는 대사가 없어 거리도 없다.
        /// </summary>
        // 2026-11-17 21:09 시험: "떠올린 것: 구름 무늬" 줄을 읽은 모델이 상황과 상관없는 거리로 말했다("이제 구름 무늬처럼 부드럽게 도윤 안부에 답해 줄게", "이제 구름 무늬를 떠올리니, 오랜만에…").
        // 다양성은 첫 낱말이 맡으므로 곁들일 거리 줄은 싣지 않는다 (되돌리려면 true). run.py 의 FLAVORS_ENABLED 와 같다
        public const bool FlavorsEnabled = false;

        public static string FlavorKey(ReactionRequest request)
        {
            if (!FlavorsEnabled)
            {
                return null;
            }
            // 탭 인사·손님 관람에는 곁들일 거리를 싣지 않는다 (2026-11-14 시험: "반가워서 내 말 그림 줬어"처럼 거리를 일어난 일로 읽어 엉뚱한 말을 했다).
            // 이 둘의 다양성은 첫 낱말(Starter)이 맡는다
            return StarterKey(request) == "tap" || StarterKey(request) == "visit" ? null : StarterKey(request);
        }

        // 첫 낱말 목록의 이름: tap · visit · faded · clear. 지난 대화 꺼내기·끌어당기기는 null
        private static string StarterKey(ReactionRequest request)
        {
            switch (request.Input)
            {
                case NpcInputs.Tap: return "tap";
                case NpcInputs.Visit: return "visit";
                case NpcInputs.Care: return "clear";
                case NpcInputs.AskStatus:
                    if (request.Candidates == null || !request.Candidates.Any(c => c != null && c.ActionId == NpcActions.AnswerStatus))
                    {
                        return null;
                    }
                    return UncaredClarity.Contains(request.FindFact(NpcFactKeys.Clarity) ?? "") ? "faded" : "clear";
                default: return null;
            }
        }

        /// <summary>사용자 메시지에 싣는 곁들일 거리 한 줄. 예: "떠올린 것: 구름 무늬 (어울릴 때만 …)".</summary>
        public static string FlavorLine(string flavor)
        {
            return FlavorLabel + ": " + flavor + " " + FlavorHint;
        }

        // 이번 요청의 곁들일 거리. Variation 번째 것을 돌려 가며 쓴다 (목록 길이로 나눈 나머지). 없으면 null
        private static string Flavor(Persona p, ReactionRequest request)
        {
            if (AskVariant(request) == "days")
            {
                return null; // 며칠 전만 말하는 요청에 '흐려진 몸'류 거리를 실으면 상태를 섞어 말한다
            }
            List<string> items = p.flavors != null ? p.flavors.For(FlavorKey(request)) : null;
            if (items == null || items.Count == 0)
            {
                return null;
            }
            int index = ((request.Variation % items.Count) + items.Count) % items.Count;
            return items[index];
        }

        private static string PersonaField(Persona p, string key)
        {
            switch (key)
            {
                case "values": return p.values;
                case "onCare": return p.onCare;
                default: return null;
            }
        }

        // 탭 상황 문장의 시간대("주인이 아침에 찾아와 너를 불렀다")는 세 번에 한 번(Variation 0·3·6…)만 싣는다. 늘 싣자 탭 인사 12건 중 9건이 "오늘 아침부터 …"로 시작했고
        // ("오늘 아침부터 왜 부르는지 궁금하네", "반가워, 오늘 아침부터 부르시니 정말 반가워" — 2026-11-16 시험 S01·S22~S24) 반복·어색한 말의 가장 큰 원인이었다. run.py 의 event_text 와 같다
        private static readonly Regex TapTime = new Regex("(?:새벽|아침|오전|낮|저녁|밤)에 (?=너를)", RegexOptions.CultureInvariant);

        private static readonly Regex AskSentence = new Regex("주인에게 [^.]*달라고 부탁한다\\.", RegexOptions.CultureInvariant);

        // 흐려진 안부의 '이미 한 말'(2026-11-17 21:45 시험 뒤): 사실 문장은 서버 값으로 코드가 만들어 대사 앞에 두고, 모델은 이어서 부탁하는 한 문장만 쓴다.
        // 사실을 모델에 맡기자 "마지막 관리가 16일 전이라서, 이제는 스스로를 닦아야 해"처럼 없는 설정을 지어냈다. 요청마다 번갈아(Variation 짝수 = 상태, 홀수 = N일 전) 한 사실만 말한다.
        // run.py 의 ask_prefix 와 같다
        public const int AskLineChars = 12;

        public static string LinePrefix(ReactionRequest request)
        {
            string variant = AskVariant(request);
            if (variant == null)
            {
                return null;
            }
            int pick = ((request.Variation / 2) % 2 + 2) % 2;
            if (variant == "days")
            {
                string n = request.FindFact(NpcFactKeys.LastCareDaysAgo);
                return pick == 0 ? "마지막 관리가 " + n + "일 전이었어." : "관리받은 지 " + n + "일이 지났어.";
            }
            string clarity = request.FindFact(NpcFactKeys.Clarity);
            bool resealed = clarity == "재봉인" || clarity == "RESEALED";
            return resealed ? (pick == 0 ? "몸이 흐려지다 못해 봉인됐어." : "다시 봉인될 만큼 흐려졌어.") : (pick == 0 ? "몸이 흐려졌어." : "몸이 많이 흐려졌어.");
        }

        /// <summary>이 요청이 쓸 수 있는 '이미 한 말'(코드가 정한 앞 문장) 전부. 최근 대사에서 앞 문장을 떼어 부탁 부분끼리 견줄 때 쓴다. 흐려진 안부가 아니면 빈 목록.</summary>
        public static List<string> AskPrefixes(ReactionRequest request)
        {
            var list = new List<string>();
            if (!NpcLineChecks.NeedsAsk(request))
            {
                return list;
            }
            string n = request.FindFact(NpcFactKeys.LastCareDaysAgo);
            if (n != null)
            {
                list.Add("마지막 관리가 " + n + "일 전이었어.");
                list.Add("관리받은 지 " + n + "일이 지났어.");
            }
            list.Add("몸이 흐려졌어.");
            list.Add("몸이 많이 흐려졌어.");
            list.Add("몸이 흐려지다 못해 봉인됐어.");
            list.Add("다시 봉인될 만큼 흐려졌어.");
            return list;
        }

        // 흐려진 안부의 '이미 한 말' 꼴 전부 — 날 수는 어느 값이든(다른 날 띄운 최근 대사), 칸('{날수}')이어도 맞는다. LinePrefix·AskPrefixes 와 같은 문장이다
        private static readonly Regex AnyAskPrefix = new Regex(
                "^(?:마지막 관리가 (?:[0-9]+|\\{날수\\})일 전이었어\\.|관리받은 지 (?:[0-9]+|\\{날수\\})일이 지났어\\.|몸이 흐려졌어\\.|몸이 많이 흐려졌어\\.|몸이 흐려지다 못해 봉인됐어\\.|다시 봉인될 만큼 흐려졌어\\.)\\s*",
                RegexOptions.CultureInvariant);

        /// <summary>
        /// 대사 앞의 '이미 한 말'(흐려진 안부의 상태·며칠 전 문장)을 날 수와 상관없이 뗀다. 없으면 그대로. 요청 관리자가 첫 낱말을 고를 때
        /// 최근 대사의 부탁 말머리("몸이 흐려졌어. 살살 닦아 줄래?"의 '살살')를 보려고 쓴다.
        /// </summary>
        public static string StripAnyAskPrefix(string line)
        {
            return string.IsNullOrEmpty(line) ? line : AnyAskPrefix.Replace(line, "", 1);
        }

        /// <summary>대사 앞의 '이미 한 말'을 뗀다. 없으면 그대로.</summary>
        public static string StripAskPrefix(string line, List<string> prefixes)
        {
            if (!string.IsNullOrEmpty(line) && prefixes != null)
            {
                foreach (string prefix in prefixes)
                {
                    if (line.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return line.Substring(prefix.Length).Trim();
                    }
                }
            }
            return line;
        }

        /// <summary>흐려진 안부의 '할 일' 줄. 재봉인은 깨워 달라고, 흐려짐은 닦아 달라고 이어 부탁한다. 흐려진 안부가 아니면 null.</summary>
        public static string AskTask(ReactionRequest request)
        {
            if (AskVariant(request) == null)
            {
                return null;
            }
            string clarity = request.FindFact(NpcFactKeys.Clarity);
            return "이어서 " + (clarity == "재봉인" || clarity == "RESEALED" ? "깨워" : "닦아") + " 달라고 부탁하는 말을 한다";
        }

        // 모델이 부탁을 빼먹거나 사실을 또 말하면 코드가 부탁 문장을 채운다 (2026-11-17 22:07 시험). run.py 의 ASK_FALLBACKS·ask_fallback 과 같다
        private static readonly string[] AskFallbacks = { "살살 {v} 줄래?", "시간 되면 {v} 줄래?", "조심조심 {v} 줘.", "괜찮으면 {v} 줄래?", "이왕이면 {v} 줘." };

        public static string AskFallback(ReactionRequest request)
        {
            string clarity = request.FindFact(NpcFactKeys.Clarity);
            string verb = clarity == "재봉인" || clarity == "RESEALED" ? "깨워" : "닦아";
            int n = AskFallbacks.Length;
            return AskFallbacks[(((request.Variation + request.RelicId) % n) + n) % n].Replace("{v}", verb);
        }

        public static string EventText(ReactionRequest request)
        {
            string situation = request.Situation ?? "";
            if (request.Input == NpcInputs.Tap && ((request.Variation % 3) + 3) % 3 != 0)
            {
                return TapTime.Replace(situation, "");
            }
            if (AskVariant(request) != null)
            {
                return "주인이 너를 보러 찾아왔다."; // (22:07 시험: "안부를 물었다"를 읽은 모델이 "혹시 안부를 물어봐줘?"로 되받았다) 사실은 '이미 한 말'(LinePrefix)로 코드가 말한다 — 상황 문장에 두면 모델이 사실을 다시 풀어 쓴다
            }
            return situation;
        }

        /// <summary>
        /// 흐려진 안부 요청이 말할 사실: "state"(몸이 흐려졌다) · "days"(마지막 관리가 N일 전). 흐려진 안부가 아니면 null.
        /// "마지막 관리가 9일 전이었어. 닦아 줄래?"에 "흐려져서"까지 넣으면 어색해져서(2026-11-17 20:39 시험 뒤) 요청마다 번갈아(Variation 짝수 = 상태, 홀수 = 며칠 전) 한 사실만 프롬프트에 싣는다.
        /// 마지막 관리 사실이 없으면 늘 상태. run.py 의 ask_variant 와 같다.
        /// </summary>
        public static string AskVariant(ReactionRequest request)
        {
            if (!NpcLineChecks.NeedsAsk(request))
            {
                return null;
            }
            return ((request.Variation % 2) + 2) % 2 == 1 && request.FindFact(NpcFactKeys.LastCareDaysAgo) != null ? "days" : "state";
        }

        private IEnumerable<string> SituationLines(ReactionRequest request)
        {
            // '고른 선택지' 줄은 상황 문장과 겹치고, Mi:dm 이 '탭'을 'tap' 으로 옮겨 행동 칸에 적어서 싣지 않는다
            string call = request.Mode == InteractionMode.Owner ? request.AddressName : null;
            var lines = new List<string> { "상황: " + NpcLineChecks.OwnerToCall(EventText(request), call) };
            string prefix = LinePrefix(request);
            if (prefix != null)
            {
                lines.Add("이미 한 말: " + prefix);
            }

            string variant = AskVariant(request);
            List<Fact> facts = (request.Facts ?? new List<Fact>())
                    .Where(f => f != null && !HiddenFactKeys.Contains(f.Key ?? "")
                            && !(variant != null && (f.Key == NpcFactKeys.LastCareDaysAgo || f.Key == NpcFactKeys.Clarity))) // 상황 문장에 이미 있어 둘 다 뺀다 (읽는 토큰 감소)
                    .ToList();
            if (facts.Count > 0)
            {
                lines.Add("알고 있는 것:");
                foreach (Fact fact in facts)
                {
                    lines.Add("- " + Label(FactLabels, fact.Key) + ": " + (fact.Value ?? ""));
                }
            }

            if (request.Memory != null && request.Memory.Count > 0)
            {
                lines.Add("기억:");
                foreach (MemoryEntry memory in request.Memory)
                {
                    string when = memory.DaysAgo == 0 ? "오늘" : memory.DaysAgo + "일 전";
                    // 대사 원문이 아니라 요약을 싣는다. 원문을 주면 작은 모델이 그대로 다시 읊는다
                    string what = !string.IsNullOrEmpty(memory.Summary) ? memory.Summary : Meaning(memory.ActionId);
                    lines.Add("- " + when + ": " + NpcLineChecks.OwnerToCall(what, call));
                }
            }
            return lines;
        }

        // 'player(주인)'처럼 붙여 쓰면 형식 제약이 없을 때 그 모양 그대로 대상 칸에 적는다. ID와 뜻을 따로 보여 준다.
        // 어느 칸에 무엇을 적을지는 형식 제약이 없을 때만 말한다
        // shape: 0 = line 만, 1 = action+line, 2 = 전부(대상 칸·'대상 ID 뜻' 줄 포함)
        private string CandidatesBlock(ReactionRequest request, List<string> targetIds, int shape)
        {
            if (shape == 0)
            {
                // 행동이 하나뿐이면 고를 것이 없다 — 후보 목록 대신 할 일 한 줄 (행동 ID 도 대상 줄도 싣지 않는다)
                return "할 일: " + (AskTask(request) ?? Meaning(request.Candidates[0].ActionId));
            }
            string head = shape == 2 ? (options.DescribeFormat ? CandidatesHeadFree : CandidatesHead) : (options.DescribeFormat ? CandidatesHeadAction : CandidatesHead);
            var lines = new List<string> { head };
            foreach (ActionCandidate candidate in request.Candidates)
            {
                string targets = string.Join(", ", (candidate.TargetIds ?? new List<string>()).ToArray());
                lines.Add("- " + candidate.ActionId + ": " + Meaning(candidate.ActionId) + (shape == 2 ? " / 대상: " + targets : ""));
            }
            if (shape == 2)
            {
                lines.Add("대상 ID 뜻: " + string.Join(", ", targetIds.Select(t => t + " = " + TargetLabel(t, request.Mode)).ToArray()));
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>이 글자 수 이하면 대사 줄에 "(짧아도 된다)"를 적지 않는다. run.py 의 SHORT_LINE_CHARS 와 같다.</summary>
        public const int ShortLineChars = 15;

        // 대사 줄. 작은 모델은 글자 수를 잘 못 세서, 문장 수를 같이 주면 길이를 더 잘 지킨다 (MaxSentences)
        private string LineRule(ReactionRequest request, Persona persona)
        {
            int chars;
            if (request.Input == null || !options.LineChars.TryGetValue(request.Input, out chars))
            {
                chars = options.DefaultLineChars;
            }
            int sentences;
            if (request.Input == null || !options.MaxSentences.TryGetValue(request.Input, out sentences))
            {
                sentences = options.DefaultMaxSentences;
            }
            // 글자 수만 적으면 그 길이를 채우려고 말을 덧붙여서, 짧아도 된다고 함께 적는다. 다만 15자 이하(탭 인사·손님 관람)에는 적지 않는다 —
            // "짧게 말한다"·"(짧아도 된다)"를 읽은 모델이 첫 낱말만 대사로 냈다("기다렸어", "왔구나", "와, 도윤!" — 2026-11-17 시험 S01·S22~S24 에서 인사말뿐·반복으로 거르고 다시 만들었다)
            if (NpcLineChecks.NeedsAsk(request))
            {
                chars = Math.Min(chars, AskLineChars); // 모델은 이어지는 부탁 한 문장만 쓴다
            }
            string rule = "대사: 한국어 반말, " + chars + "자 이내" + (sentences > 0 ? ", " + sentences + "문장 이내" : "") + (chars <= ShortLineChars ? "" : "(짧아도 된다)");
            // 첫 낱말을 줄 바로 옆에서 다시 말해 준다: 시스템 규칙과 '첫 낱말' 줄만으로는 모델이 따르지 않아 감사가 "관리해 줘서 고마워"로 거듭 같았다(S07·S09·S13·S25 반복)
            // 15자 이하(탭 인사·손님 관람)는 첫 낱말에서 멈추지 않게 "이어 말한다"고 적는다 ("기다렸어"·"안녕"만 낸 대사가 인사말뿐으로 걸렸다). "마음을 전하는 한 문장"은
            // "반가워서 불렀어"·"반가워서 뛰어나왔어"처럼 '반가워서 ~'로 잇게 해서 뺐다 (2026-11-17 19:43 시험 S22)
            if (Starter(request) != null)
            {
                rule += chars <= ShortLineChars && !NpcLineChecks.NeedsAsk(request) ? ", 첫 낱말로 시작해 반가운 마음을 말한다" : NpcLineChecks.NeedsAsk(request) ? ", 첫 낱말로 시작" : ", 첫 낱말로 시작하는 한 문장";
            }
            // 흐려진 안부 9건 중 4건이 닦아 달라는 부탁을 빼먹었다(MissingAsk). 끝을 "닦아 줘"로 맺게 했더니 토막 난 말("깨워줘 닦아 줘")이, "'닦아 줄래?'로 부탁한다"로 했더니 모든 대사가
            // "…닦아 줄래?"로 끝나 같아 보였다(2026-11-17 18:21·19:43 시험). 끝말을 정해 주지 않고 바라는 마음만 말하게 한다 — 닦아 달라는 말이 들어가기만 하면 출력 검사(HasAsk)가 통과시킨다
            if (NpcLineChecks.NeedsAsk(request))
            {
                // "며칠 전"이라고 적었더니 모델이 그 낱말을 그대로 옮겼다("마지막 관리가 며칠 전이라"). 서버가 주는 값 N 으로 채워 적는다
                rule += ", 부탁 한 문장";
            }
            // 말투 다짐 줄(speechReminder)은 공통 규칙 RuleSpeech 과 겹쳐 뺐다 (2026-11-13)
            if (request.Memory != null && request.Memory.Count > 0)
            {
                rule += ". " + RuleNoRepeat;
            }
            return rule;
        }

        private string Meaning(string actionId)
        {
            ActionDefinition action = actions.Find(actionId);
            return action != null ? action.meaning : actionId;
        }

        private string TargetLabel(string targetId, InteractionMode mode)
        {
            if (targetId == NpcTargets.Player)
            {
                return mode == InteractionMode.Owner ? "주인" : "친구";
            }
            if (targetId == NpcTargets.None)
            {
                return "대상 없음";
            }
            return Label(options.TargetLabels, targetId);
        }

        private static string Label(Dictionary<string, string> labels, string key)
        {
            string label;
            return key != null && labels.TryGetValue(key, out label) ? label : key;
        }

        private static List<string> Distinct(IEnumerable<string> values)
        {
            var result = new List<string>();
            foreach (string value in values)
            {
                if (!result.Contains(value))
                {
                    result.Add(value);
                }
            }
            return result;
        }

        // shape: 0 = line 만, 1 = action+line, 2 = 전부 (run.py 의 schema_for_shape 와 같다)
        private static string Schema(List<string> actionIds, List<string> targetIds, int shape)
        {
            if (shape == 0)
            {
                return "{\"type\":\"object\",\"properties\":{\"line\":{\"type\":\"string\"}},\"required\":[\"line\"]}";
            }
            if (shape == 1)
            {
                return "{\"type\":\"object\",\"properties\":{"
                       + "\"action\":{\"type\":\"string\",\"enum\":" + JsonArray(actionIds) + "},"
                       + "\"line\":{\"type\":\"string\"}},"
                       + "\"required\":[\"action\",\"line\"]}";
            }
            return "{\"type\":\"object\",\"properties\":{"
                   + "\"action\":{\"type\":\"string\",\"enum\":" + JsonArray(actionIds) + "},"
                   + "\"target\":{\"type\":\"string\",\"enum\":" + JsonArray(targetIds) + "},"
                   + "\"line\":{\"type\":\"string\"}},"
                   + "\"required\":[\"action\",\"target\",\"line\"]}";
        }

        private static string JsonArray(IEnumerable<string> values)
        {
            return "[" + string.Join(",", values.Select(v => "\"" + JsonEscape(v) + "\"").ToArray()) + "]";
        }

        private static string JsonEscape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// 행동마다 그 행동의 대상만 허용하는 GBNF. 대사(line)는 따옴표·역슬래시·줄바꿈을 빼고 받는다.
        /// </summary>
        /// <summary>문장 하나의 최대 글자 수 (끝 부호 앞). 이를 넘기 전에 문법이 끝 부호를 내게 한다. 프롬프트의 "20자 이내"보다 넉넉하다.</summary>
        public const int SentenceMaxChars = 30;

        // 문장 규칙: 글자(끝 부호 아닌 것)를 1~30개 쓰고 끝 부호(. ! ? ~ …)를 1~3개 쓴다. GBNF 의 반복 범위({m,n})를 못 읽는 런타임에서도 돌게
        // 선택 항목을 중첩해 펼친다: b (b (b …)?)? . 맨 앞의 '…'는 허용한다 (밤바다 "…고마워.")
        private static string SentenceChain()
        {
            string chain = "b";
            for (int i = 1; i < SentenceMaxChars; i++)
            {
                chain = "b (" + chain + ")?";
            }
            return chain;
        }

        private static string Grammar(List<ActionCandidate> candidates, int shape, bool sentenceGrammar)
        {
            var grammar = new StringBuilder();
            if (shape == 0)
            {
                // 행동·대상이 하나뿐: {"line": "…"} 만 쓴다 (빠진 칸은 검사기가 후보로 채운다)
                grammar.Append("root ::= \"{\" ws \"\\\"line\\\"\" ws \":\" ws line ws \"}\"\n");
            }
            else if (shape == 1)
            {
                // 대상이 하나뿐: {"action": "…", "line": "…"}
                var actionIds = new List<string>();
                foreach (ActionCandidate candidate in candidates)
                {
                    if (!actionIds.Contains(candidate.ActionId))
                    {
                        actionIds.Add(candidate.ActionId);
                    }
                }
                grammar.Append("root ::= \"{\" ws \"\\\"action\\\"\" ws \":\" ws action ws \",\" ws \"\\\"line\\\"\" ws \":\" ws line ws \"}\"\n");
                grammar.Append("action ::= ").Append(string.Join(" | ", actionIds.Select(GbnfString).ToArray())).Append("\n");
            }
            else
            {
                var pairs = new List<string>();
                foreach (ActionCandidate candidate in candidates)
                {
                    List<string> targets = candidate.TargetIds ?? new List<string>();
                    string targetRule = targets.Count == 0
                            ? GbnfString(NpcTargets.None)
                            : "(" + string.Join(" | ", targets.Select(GbnfString).ToArray()) + ")";
                    pairs.Add(GbnfString(candidate.ActionId) + " ws \",\" ws \"\\\"target\\\"\" ws \":\" ws " + targetRule);
                }
                grammar.Append("root ::= \"{\" ws \"\\\"action\\\"\" ws \":\" ws pair ws \",\" ws \"\\\"line\\\"\" ws \":\" ws line ws \"}\"\n");
                grammar.Append("pair ::= ").Append(string.Join(" | ", pairs.Select(p => "(" + p + ")").ToArray())).Append("\n");
            }
            if (sentenceGrammar)
            {
                // 대사는 끝 부호로 끝나는 문장 하나, 또는 둘. 부호가 오기 전에는 따옴표를 닫지 못하므로 문장 중간에서 끊기지 않는다
                grammar.Append("line ::= \"\\\"\" sentence ( \" \" sentence )? \"\\\"\"\n");
                grammar.Append("sentence ::= \"…\"? ").Append(SentenceChain()).Append(" term term? term?\n");
                grammar.Append("b ::= [^\"\\\\\\n\\r.!?~…]\n");
                grammar.Append("term ::= [.!?~…]\n");
            }
            else
            {
                grammar.Append("line ::= \"\\\"\" char+ \"\\\"\"\n");
                grammar.Append("char ::= [^\"\\\\\\n\\r]\n");
            }
            grammar.Append("ws ::= [ \\t]*\n");
            return grammar.ToString();
        }

        // GBNF 안에서 JSON 문자열 하나를 그대로 내는 리터럴: "\"greet\""
        private static string GbnfString(string value)
        {
            string escaped = value.Replace("\\", "\\\\\\\\").Replace("\"", "\\\\\\\"");
            return "\"\\\"" + escaped + "\\\"\"";
        }
    }
}
