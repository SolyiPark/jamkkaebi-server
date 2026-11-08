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
        Unsupported,    // 이 생성기가 맡지 않는 요청 (SLM: 페르소나가 아직 없는 도깨비) — 규칙 기반 생성기로 대신한다
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
    }

    /// <summary>
    /// 사실(Facts) 키. 값은 문자열로 넣는다.
    /// 서버 응답에서 만드는 법: last_care_days_ago 는 내 전시관의 lastPurifiedAt 으로 오늘까지 며칠인지 계산하고,
    /// visit_count_30d 는 친구 전시관의 recentVisitDays 를 그대로 쓴다.
    /// </summary>
    public static class NpcFactKeys
    {
        public const string Clarity = "clarity";                    // CLEAR / FADED / RESEALED — 주인 장면에만 넣는다
        public const string Awakening = "awakening";                // AWAKENED / AWAKENED_PLUS / FULLY_AWAKENED
        public const string LastCareDaysAgo = "last_care_days_ago"; // 정수 (며칠 전)
        public const string VisitCount30d = "visit_count_30d";      // 정수 — 손님 장면
        public const string CareResult = "care_result";             // 관리 결과 한 줄
        public const string VerifiedStory = "verified_story";       // 이번에 들려줄 실화 한 문장 (페르소나 stories 중 StoryRotation 이 고른 것)
        public const string StoryIndex = "story_index";             // 그 실화가 stories 의 몇 번째인지 (0부터). 프롬프트에는 싣지 않고, 규칙 기반 생성기가 같은 이야기 대사를 고를 때 쓴다
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

        public InteractionMode Mode { get; set; }

        /// <summary>NpcInputs 중 하나.</summary>
        public string Input { get; set; }

        /// <summary>
        /// 이번 상황 한 줄. 컨텍스트 구성기가 이번에 가장 중요한 사실 하나를 여기에 직접 적는다
        /// (예: "주인이 안부를 물었다. 이틀 전에 주인이 너를 닦아 주었다."). SLM 생성기가 쓰고, 규칙 기반 생성기는 쓰지 않는다.
        /// </summary>
        public string Situation { get; set; }

        /// <summary>후보 필터 결과. 비어 있으면 부르지 않는다.</summary>
        public List<ActionCandidate> Candidates { get; set; }

        /// <summary>이번 상황과 관련된 사실만 넣는다. 손님 장면에는 선명도를 넣지 않는다.</summary>
        public List<Fact> Facts { get; set; }

        public List<MemoryEntry> Memory { get; set; }

        /// <summary>행동 카탈로그·페르소나·기본 대사의 버전.</summary>
        public string ContentVersion { get; set; }

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

        /// <summary>Success 일 때. 일반 텍스트로 띄운다 (리치 텍스트 끔).</summary>
        public string Line { get; private set; }

        /// <summary>Failed 일 때.</summary>
        public FailReason FailReason { get; private set; }

        /// <summary>Failed 일 때 사람이 읽는 이유 (예: "금칙어 밀어내기:놔"). 로그와 평가용이고 화면에 띄우지 않는다. 없으면 null.</summary>
        public string FailDetail { get; private set; }

        /// <summary>어느 생성기가 만들었는지. 평가와 디버깅용.</summary>
        public ReactionSource Source { get; private set; }

        public bool IsSuccess
        {
            get { return Status == ReactionStatus.Success; }
        }

        public static ReactionResult Success(string requestId, string actionId, string targetId, string line,
                                             ReactionSource source)
        {
            return new ReactionResult
            {
                RequestId = requestId,
                Status = ReactionStatus.Success,
                ActionId = actionId,
                TargetId = targetId,
                Line = line,
                FailReason = FailReason.None,
                Source = source
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
    }
}
