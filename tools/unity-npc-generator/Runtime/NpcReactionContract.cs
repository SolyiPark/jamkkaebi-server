using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// AI 반응 생성기의 공통 인터페이스. 규칙 기반 생성기와 SLM 생성기가 같은 모양으로 구현해,
    /// 바꿔 끼워도 부르는 쪽(요청 관리자) 코드는 그대로다.
    /// </summary>
    public interface INpcReactionGenerator
    {
        /// <summary>바로 생성할 수 있는지. 규칙 기반 생성기는 늘 true 다.</summary>
        bool IsReady { get; }

        /// <summary>전시관에 들어갈 때 부른다. SLM 생성기는 여기서 모델을 메모리에 올린다.</summary>
        Task LoadAsync(CancellationToken ct);

        /// <summary>정화 미니게임에 들어갈 때 부른다. SLM 생성기는 여기서 모델을 내린다.</summary>
        void Unload();

        /// <summary>
        /// 반응 하나를 만든다. 예외를 던지지 않고 늘 결과를 돌려준다(취소도 Failed(Cancelled)).
        /// 시간 제한은 요청 관리자가 정하고 ct 로만 전달한다.
        /// </summary>
        Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct);

        /// <summary>
        /// 도깨비를 탭해 확대할 때 부른다. SLM 생성기는 그 도깨비의 시스템 프롬프트를 모델에 미리 읽혀 두어, 다음 요청은
        /// 짧은 사용자 메시지만 읽게 한다. 규칙 기반 생성기는 할 일이 없다. 취소되면 OperationCanceledException 을 던질 수 있다.
        /// </summary>
        Task PrepareAsync(int relicId, CancellationToken ct);
    }

    public enum InteractionMode
    {
        Owner,   // 주인 — 탭, 선택지, 끌어당기기로 교류한다
        Visitor  // 손님 — 건드릴 수 없고 관람만 한다
    }

    public enum ReactionStatus
    {
        Success,
        Failed
    }

    public enum FailReason
    {
        None,
        NotReady,       // 모델이 아직 올라오지 않았다
        Cancelled,      // 요청 관리자가 취소했다 (시간 초과, NPC 전환, 화면 종료)
        InvalidOutput,  // 모델 출력이 형식·후보·길이 검사를 통과하지 못했다
        NoCandidate,    // 후보가 없다 — 부르는 쪽이 부르지 않는 게 맞다
        Unsupported,    // 이 생성기가 맡지 않는 요청 (SLM: 페르소나가 아직 없는 도깨비, 정해진 대사를 쓰는 이야기) — 규칙 기반 생성기로 대신한다
        Error           // 그 밖의 오류
    }

    public enum ReactionSource
    {
        Rule,
        Slm
    }

    /// <summary>선택지(플레이어 입력) 이름. 행동 카탈로그의 inputs 와 같다.</summary>
    public static class NpcInputs
    {
        public const string Tap = "tap";                  // 주인이 탭해 교류 시작
        public const string AskStatus = "ask_status";     // 안부 묻기
        public const string Care = "care";                // 관리하기 (관리를 마친 뒤의 반응)
        public const string ListenStory = "listen_story"; // 이야기 듣기
        public const string Drag = "drag";                // 주인이 끌어당겨 옮김
        public const string Visit = "visit";              // 손님 관람 중 (건드리지 않음)
    }

    /// <summary>행동 ID. 행동 카탈로그(actions.json)와 같다.</summary>
    public static class NpcActions
    {
        public const string Greet = "greet";                     // Idle
        public const string AnswerStatus = "answer_status";      // Idle
        public const string RecallTalk = "recall_talk";          // Idle — 대화 기억이 있어 게임이 정했을 때만 후보
        public const string WanderGreet = "wander_greet";        // Walking
        public const string WanderRecall = "wander_recall";      // Walking — 방문 기록이 있을 때만 후보
        public const string ThankJoyful = "thank_joyful";        // Positive
        public const string ThankCalm = "thank_calm";            // Idle
        public const string TellStory = "tell_story";            // Idle
        public const string StruggleProtest = "struggle_protest"; // Struggle
        public const string StrugglePlay = "struggle_play";      // Struggle

        /// <summary>
        /// AI 로 만들지 않고 늘 정해진 대사(규칙 기반 생성기)를 쓰는 행동. 이야기는 2B 모델이 사실을 지어내거나 빼먹어서,
        /// 검수한 이야기 대사 3개(default_lines.json 의 stories)를 StoryRotation 순서대로 꺼내 쓴다.
        /// </summary>
        public static bool UsesFixedLines(string actionId)
        {
            return actionId == TellStory;
        }

        /// <summary>
        /// 대사 없이 몸짓(애니메이션)만 하는 행동. 끌려갈 때의 버둥거림은 대사를 띄우지 않는다 — 규칙 기반 생성기가 이 행동을
        /// 고르되 대사는 빈 글(<see cref="ReactionResult.Line"/> == "")로 돌려주고, 부르는 쪽은 말풍선 없이 애니메이션만 재생한다.
        /// </summary>
        public static bool IsSilent(string actionId)
        {
            return actionId == StruggleProtest || actionId == StrugglePlay;
        }

        /// <summary>후보가 모두 정해진 대사를 쓰는 행동인지. 그러면 SLM 을 부르지 않는다.</summary>
        public static bool OnlyFixedLines(List<ActionCandidate> candidates)
        {
            return AllMatch(candidates, UsesFixedLines);
        }

        /// <summary>후보가 모두 대사 없는 행동인지(끌려갈 때). 그러면 SLM 을 부르지 않고 대사 없는 반응을 돌려준다.</summary>
        public static bool OnlySilent(List<ActionCandidate> candidates)
        {
            return AllMatch(candidates, IsSilent);
        }

        /// <summary>
        /// 후보가 모두 정해진 대사를 쓰거나 대사가 없는 행동인지. 그러면 모델을 부르지 않고 프롬프트도 만들지 않는다.
        /// </summary>
        public static bool SkipsModel(List<ActionCandidate> candidates)
        {
            return AllMatch(candidates, a => UsesFixedLines(a) || IsSilent(a));
        }

        private static bool AllMatch(List<ActionCandidate> candidates, Func<string, bool> test)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }
            foreach (ActionCandidate candidate in candidates)
            {
                if (candidate == null || !test(candidate.ActionId))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// 사실(Facts) 키. 값은 문자열로 넣는다.
    /// 서버 응답에서 만드는 법: last_care_days_ago 는 내 전시관의 lastPurifiedAt(마지막으로 판을 깬 시각 — 선명도와 따로 바뀐다)으로 오늘까지 며칠인지 계산하고,
    /// visit_count_30d 는 친구 전시관의 recentVisitDays 를 그대로 쓴다. care_result 의 관리 뒤 선명도는 정산 결과 응답의 clarity 다.
    /// </summary>
    public static class NpcFactKeys
    {
        public const string Clarity = "clarity";                    // CLEAR / FADED / RESEALED — 주인 장면에만 넣는다
        public const string Awakening = "awakening";                // AWAKENED / AWAKENED_PLUS / FULLY_AWAKENED
        public const string LastCareDaysAgo = "last_care_days_ago"; // 정수 (며칠 전)
        public const string VisitCount30d = "visit_count_30d";      // 정수 — 손님 장면
        public const string CareResult = "care_result";             // 관리 결과 한 줄
        public const string TimeOfDay = "time_of_day";              // 새벽 · 아침 · 오전 · 낮 · 저녁 · 밤 — 서버 시각에서 계산. 탭 인사에만 넣는다
        public const string VisitDays = "visit_days";               // 상대가 최근 30일에 방문한 날 ("오늘, 3일 전, 9일 전") — 서버의 방문 기록
        public const string VerifiedStory = "verified_story";       // 이번에 들려줄 실화 한 문장 (페르소나 stories 중 StoryRotation 이 고른 것)
        public const string StoryIndex = "story_index";             // 그 실화가 stories 의 몇 번째인지 (0부터). 프롬프트에는 싣지 않고, 규칙 기반 생성기가 같은 이야기 대사를 고를 때 쓴다
    }

    /// <summary>
    /// 상대를 부르는 이름과 친밀도. 친밀도는 주인이 이 도깨비를 관리한(관리 플레이 성공) 누적 횟수로 정한다:
    /// 3·7·12·20번을 채울 때마다 한 단계 오른다 (0~4단계). 게임의 IntimacyPolicy 기본값과 같은 기준이고, 게임 화면의 Lv.1~5 가 여기의 0~4단계다.
    /// 3단계(관리 12번)부터 닉네임으로 부르고, 0~2단계는 '발굴자'로 부른다.
    /// 손님(구경 온 친구)은 도깨비를 관리하지 않으므로 친밀도가 없다. 대신 최근 30일 방문한 날 수로 같은 단계를 매겨 호칭을 정한다 (VisitorLevelOf).
    /// </summary>
    public static class NpcAddress
    {
        /// <summary>친밀하지 않은 상대(0단계, 닉네임 없음)를 부르는 호칭.</summary>
        public const string Stranger = "발굴자";

        /// <summary>
        /// 누적 관리 성공 횟수가 이만큼 이상이면 친밀도가 한 단계씩 오른다: 3번 → 1단계, 7번 → 2단계, 12번 → 3단계, 20번 → 4단계.
        /// 게임의 IntimacyPolicy.Default.LevelUpCareCounts 와 같아야 한다 (게임 쪽 테스트가 확인한다).
        /// </summary>
        public static readonly System.Collections.ObjectModel.ReadOnlyCollection<int> LevelUpCareCounts =
            System.Array.AsReadOnly(new[] { 3, 7, 12, 20 });

        /// <summary>손님이 최근 30일에 방문한 날 수가 이만큼 이상이면 호칭 단계가 한 단계씩 오른다: 3번 → 1단계, 7번 → 2단계, 12번 → 3단계, 20번 → 4단계.</summary>
        public static readonly System.Collections.ObjectModel.ReadOnlyCollection<int> LevelUpVisits =
            System.Array.AsReadOnly(new[] { 3, 7, 12, 20 });

        /// <summary>이 단계(3단계 = 주인은 관리 12번, 손님은 30일 방문 12번) 이상이면 닉네임으로 부른다.</summary>
        public const int NicknameLevel = 3;

        /// <summary>주인이 이 도깨비를 관리한(관리 플레이 성공) 누적 횟수에서 친밀도(0~4단계)를 구한다. 횟수를 모르면 0.</summary>
        public static int IntimacyOf(int? careCount)
        {
            return LevelOf(careCount, LevelUpCareCounts);
        }

        /// <summary>손님이 최근 30일에 방문한 날 수(같은 날은 하루)에서 호칭 단계(0~4)를 구한다. 방문 기록이 없으면 0.</summary>
        public static int VisitorLevelOf(int? visitDays)
        {
            return LevelOf(visitDays, LevelUpVisits);
        }

        private static int LevelOf(int? count, System.Collections.ObjectModel.ReadOnlyCollection<int> levelUpCounts)
        {
            int level = 0;
            if (count.HasValue)
            {
                while (level < levelUpCounts.Count && count.Value >= levelUpCounts[level])
                {
                    level++;
                }
            }
            return level;
        }
    }

    public static class NpcTargets
    {
        public const string Player = "player";
        public const string None = "none";
    }

    /// <summary>후보 행동 하나와 그 행동의 대상들. 후보 필터가 만든다.</summary>
    [Serializable]
    public sealed class ActionCandidate
    {
        public string ActionId;
        public List<string> TargetIds = new List<string>();

        public ActionCandidate()
        {
        }

        public ActionCandidate(string actionId, params string[] targetIds)
        {
            ActionId = actionId;
            TargetIds = new List<string>(targetIds);
        }
    }

    [Serializable]
    public sealed class Fact
    {
        public string Key;
        public string Value;

        public Fact()
        {
        }

        public Fact(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }

    /// <summary>기기에 저장한 대화 기억. 대사 원문이 아니라 요약을 넣는다 — 원문을 주면 작은 모델이 그대로 다시 읊는다.</summary>
    [Serializable]
    public sealed class MemoryEntry
    {
        public int DaysAgo;
        public string ActionId;
        public string Summary;

        /// <summary>
        /// 꺼냈음 표시 (T/F). 게임이 기억과 함께 저장한다 — 안부를 띄운 뒤 <see cref="NpcContext.MemoryToMark"/> 의 이 값이 true 로 바뀐다.
        /// <list type="bullet">
        /// <item>보통 기억(들려준 실화 등): 안부에서 지난 대화로 꺼냈다. 다시 꺼내지 않는다.</item>
        /// <item>"지난 일을 다시 이야기했다"(recall_talk) 기억: 그 뒤 안부를 한 번 평소대로(선명도·관리 이야기) 했다. 다음 안부부터 다음 기억을 꺼낸다.</item>
        /// </list>
        /// 예전에 저장한 기억은 false 로 읽힌다 — 그 가운데 이미 꺼낸 것이 7일 안에 있으면 한 번 더 꺼낼 수 있다.
        /// </summary>
        public bool Recalled;

        public MemoryEntry()
        {
        }

        public MemoryEntry(int daysAgo, string actionId, string summary)
        {
            DaysAgo = daysAgo;
            ActionId = actionId;
            Summary = summary;
        }
    }

    public sealed class ReactionRequest
    {
        public ReactionRequest()
        {
            Candidates = new List<ActionCandidate>();
            Facts = new List<Fact>();
            Memory = new List<MemoryEntry>();
        }

        /// <summary>요청 관리자가 생성 시도마다 발급한다. 생성기는 결과에 그대로 돌려준다.</summary>
        public string RequestId { get; set; }

        /// <summary>플레이어 입력 한 번. 생성을 다시 시도해도 같다.</summary>
        public string InteractionId { get; set; }

        /// <summary>어느 도깨비인지 (서버의 relicId).</summary>
        public int RelicId { get; set; }

        /// <summary>
        /// 지금 상대(주인 장면은 주인, 손님 장면은 구경 온 친구)의 닉네임. 서버가 넘긴다. 없으면 null.
        /// 닉네임은 Familiar 일 때만 부른다 — 아니면 '발굴자'.
        /// </summary>
        public string Nickname { get; set; }

        /// <summary>
        /// 친밀도 0~4단계 (NpcAddress.IntimacyOf — 주인의 누적 관리 성공 횟수가 3·7·12·20번을 채울 때마다 한 단계). 컨텍스트 구성기가 정한다.
        /// 손님 장면에서는 최근 30일 방문한 날 수로 매긴 호칭 단계(NpcAddress.VisitorLevelOf)가 들어간다.
        /// 3단계부터 닉네임으로 부른다.
        /// </summary>
        public int Intimacy { get; set; }

        /// <summary>친밀해져(3단계 이상) 닉네임으로 부르는지. 닉네임이 없으면 친밀해도 '발굴자'로 부른다.</summary>
        public bool Familiar
        {
            get { return Intimacy >= NpcAddress.NicknameLevel && !string.IsNullOrEmpty(Nickname); }
        }

        /// <summary>프롬프트에 싣고 출력 검사가 지키는 호칭: 친밀하고 닉네임이 있으면 닉네임, 아니면 '발굴자'.</summary>
        public string AddressName
        {
            get { return Familiar && !string.IsNullOrEmpty(Nickname) ? Nickname : NpcAddress.Stranger; }
        }

        public InteractionMode Mode { get; set; }

        /// <summary>NpcInputs 중 하나.</summary>
        public string Input { get; set; }

        /// <summary>
        /// 이번 상황 한 줄. 컨텍스트 구성기가 이번에 가장 중요한 사실 하나를 여기에 직접 적는다
        /// (예: "주인이 안부를 물었다. 너는 이틀 전에 닦아 관리를 받은 일이 고맙다."). SLM 생성기가 쓰고, 규칙 기반 생성기는 쓰지 않는다.
        /// </summary>
        public string Situation { get; set; }

        /// <summary>후보 필터 결과. 비어 있으면 부르지 않는다.</summary>
        public List<ActionCandidate> Candidates { get; set; }

        /// <summary>이번 상황과 관련된 사실만 넣는다. 손님 장면에는 선명도를 넣지 않는다.</summary>
        public List<Fact> Facts { get; set; }

        public List<MemoryEntry> Memory { get; set; }

        /// <summary>행동 카탈로그·페르소나·기본 대사의 버전.</summary>
        public string ContentVersion { get; set; }

        /// <summary>
        /// 페르소나의 곁들일 거리 중 몇 번째 것을 실을지 (목록 길이로 나눈 나머지를 쓴다). 같은 상황이 거듭될 때 대사가 갈리도록
        /// 컨텍스트 구성기가 요청마다 바꾼다. 기본 0. 규칙 기반 생성기는 쓰지 않는다.
        /// </summary>
        public int Variation { get; set; }

        /// <summary>
        /// 이 도깨비가 최근에 띄운 대사. 출력 검사기가 같은 대사를 거르고(Repeated), 규칙 기반 생성기도 피한다. 요청 관리자가
        /// 이번 실행에서 띄운 것을 채워 넣고, 게임이 저장해 둔 것을 미리 넣어도 된다. <b>프롬프트에는 싣지 않는다</b> — 실으면
        /// 작은 모델이 그 대사를 그대로 옮긴다.
        /// </summary>
        public List<string> RecentLines { get; set; }

        /// <summary>
        /// 뒤에서 만드는 생성(미리 만들기·다시 만들기)인지. 요청 관리자가 붙인다. 백엔드는 이 생성보다 플레이어의 요청·미리 읽기를 먼저 모델에 넣는다
        /// (NpcPrompt.Background, LlmUnityModelBackend 의 '뒤에서 만들기는 나눠 읽는다'). 게임이 보내는 요청은 false 그대로 둔다.
        /// </summary>
        public bool Background { get; set; }

        /// <summary>키에 맞는 사실의 값. 없으면 null.</summary>
        public string FindFact(string key)
        {
            if (Facts == null)
            {
                return null;
            }
            foreach (Fact fact in Facts)
            {
                if (fact != null && fact.Key == key)
                {
                    return fact.Value;
                }
            }
            return null;
        }
    }

    public sealed class ReactionResult
    {
        private ReactionResult()
        {
        }

        /// <summary>요청의 값을 그대로 돌려준다. 요청 관리자가 늦게 온 결과를 걸러 낼 때 쓴다.</summary>
        public string RequestId { get; private set; }

        public ReactionStatus Status { get; private set; }

        /// <summary>Success 일 때. 늘 후보 안의 값이다.</summary>
        public string ActionId { get; private set; }

        /// <summary>Success 일 때. 고른 후보의 대상 중 하나다.</summary>
        public string TargetId { get; private set; }

        /// <summary>
        /// Success 일 때. 일반 텍스트로 띄운다 (리치 텍스트 끔). <b>빈 글이면 말풍선을 띄우지 않고</b> 행동의 애니메이션만 재생한다
        /// (끌려갈 때의 Struggle — <see cref="NpcActions.IsSilent"/>).
        /// </summary>
        public string Line { get; private set; }

        /// <summary>Failed 일 때.</summary>
        public FailReason FailReason { get; private set; }

        /// <summary>Failed 일 때 사람이 읽는 이유 (예: "금칙어 밀어내기:놔"). 로그와 평가용이고 화면에 띄우지 않는다. 없으면 null.</summary>
        public string FailDetail { get; private set; }

        /// <summary>어느 생성기가 만들었는지. 평가와 디버깅용.</summary>
        public ReactionSource Source { get; private set; }

        /// <summary>Success 일 때 사람이 읽는 메모 (예: "긴 대사를 문장 단위로 잘라 씀 (52자 → 18자)"). 로그와 평가용. 없으면 null.</summary>
        public string Note { get; private set; }

        public bool IsSuccess
        {
            get { return Status == ReactionStatus.Success; }
        }

        public static ReactionResult Success(string requestId, string actionId, string targetId, string line,
                                             ReactionSource source, string note = null)
        {
            return new ReactionResult
            {
                RequestId = requestId,
                Status = ReactionStatus.Success,
                ActionId = actionId,
                TargetId = targetId,
                Line = line,
                FailReason = FailReason.None,
                Source = source,
                Note = note
            };
        }

        public static ReactionResult Failed(string requestId, FailReason reason, ReactionSource source, string detail = null)
        {
            return new ReactionResult
            {
                RequestId = requestId,
                Status = ReactionStatus.Failed,
                FailReason = reason,
                FailDetail = detail,
                Source = source
            };
        }

        /// <summary>
        /// 앞 시도에서 출력 검사에 걸린 이유를 덧붙인 결과 (SLM 생성기가 다시 만들었을 때). 성공이면 Note, 실패면 FailDetail 에 붙는다.
        /// </summary>
        public ReactionResult WithEarlierRejections(IList<string> rejected)
        {
            string earlier = "앞 시도 " + rejected.Count + "번 거름: " + string.Join(" / ", new List<string>(rejected).ToArray());
            var copy = (ReactionResult)MemberwiseClone();
            if (IsSuccess)
            {
                copy.Note = Note == null ? earlier : Note + "; " + earlier;
            }
            else
            {
                copy.FailDetail = FailDetail == null ? earlier : FailDetail + " (" + earlier + ")";
            }
            return copy;
        }
    }
}
