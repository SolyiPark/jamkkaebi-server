using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jamkkaebi.Npc
{
    /// <summary>NpcContextBuilder가 대사 요청을 만들 때 받는 입력 양식</summary>
    public sealed class NpcContextInput
    {
        public NpcContextInput()
        {
            Memory = new List<MemoryEntry>();
        }

        public int RelicId { get; set; }

        public InteractionMode Mode { get; set; }

        public string Input { get; set; }

        /// <summary>오늘 날짜. GET /api/exhibition 응답의 today(서버 GameClock, KST 자정 기준)를 쓴다. 기기 시계를 쓰지 않는다.</summary>
        public DateTime Today { get; set; }

        /// <summary>내 전시관 spirits[].clarity</summary>
        public string Clarity { get; set; }

        /// <summary>spirits[].awakeningStage (AWAKENED · AWAKENED_PLUS · FULLY_AWAKENED).</summary>
        public string AwakeningStage { get; set; }

        /// <summary>
        /// 마지막으로 정화(관리 플레이 포함)에 성공한 시각 — 내 전시관 spirits[].lastPurifiedAt. 관리하기면 이번 관리 <b>전</b> 값이다.
        /// 서버는 판을 깰 때마다 이 값을 바꾸고 선명도는 따로 둔 기준 시각으로 센다. 그래서 재봉인에서 보통 난이도로 한 판 깨 흐려짐이 된 정령은
        /// 흐려짐이면서 이 값이 최근이다(7일 안). 흐려짐·재봉인이라고 늘 오래 방치된 것이 아니다.
        /// </summary>
        public DateTime? LastPurifiedAt { get; set; }

        /// <summary>
        /// 관리하기에서만 쓴다: 이번 관리 <b>뒤</b> 선명도. 정산 결과 응답(PurificationResultResponse)의 clarity 를 그대로 넘긴다.
        /// 보통 난이도는 한 칸만 오른다(재봉인 → 흐려짐 → 선명), 어려움·쉬움은 선명. 페이즈가 남은 중간 판은 서버(UserRelic.clearCarePlay)가
        /// 선명도·lastPurifiedAt 을 그대로 둬 관리 전과 같은 값(재봉인 → 재봉인, 흐려짐 → 흐려짐)이 온다. 비우면 선명으로 본다.
        /// </summary>
        public string ClarityAfterCare { get; set; }

        /// <summary>
        /// 주인이 이 도깨비를 관리한(관리 플레이 성공) 누적 횟수. 작업대·정화 응답의 careCount 를 그대로 넘긴다. 모르면 null.
        /// 주인 장면의 친밀도는 이 횟수로 정한다(3·7·12·20번을 채울 때마다 한 단계, NpcAddress.IntimacyOf). 손님 장면에서는 쓰지 않는다.
        /// </summary>
        public int? CareCount { get; set; }

        /// <summary>친구 전시관의 recentVisitDays (지금 열람 포함, 첫 방문이면 1). VisitDates 를 주면 그것으로 센다.</summary>
        public int? RecentVisitDays { get; set; }

        /// <summary>
        /// 서버 시각(KST, 시·분 포함). 탭 인사에 지금 시간대(새벽·아침·오전·낮·저녁·밤)를 말하게 하는 데 쓴다. 기기 시계를 쓰지 않는다.
        /// 없으면 시간대 이야기를 하지 않는다.
        /// </summary>
        public DateTime? Now { get; set; }

        /// <summary>지금 상대의 닉네임 (서버). 주인 장면은 주인, 손님 장면은 구경 온 친구. 없으면 늘 '발굴자'로 부른다.</summary>
        public string Nickname { get; set; }

        /// <summary>
        /// 지금 상대가 최근 30일에 방문한 날들 (서버의 방문 기록, 지금 방문을 포함해도 된다). 같은 날 여러 번이면 하루로 센다.
        /// 주인 장면은 GET /api/exhibition 의 recentVisitDates(주인이 내 전시관을 연 날), 손님 장면은 친구 전시관의 recentVisitDates(그 친구가 내 전시관에 온 날)를 그대로 넘긴다.
        /// 손님 장면의 호칭 단계는 이 날 수로 정한다(3·7·12·20번을 채울 때마다 한 단계, NpcAddress.VisitorLevelOf). 주인 장면의 친밀도는 CareCount 로 정하고 이 기록은 쓰지 않는다.
        /// 3단계(12번)부터 닉네임으로 부르고, 0~2단계는 '발굴자'로 부른다.
        /// 대사에는 이 날짜 그대로 "N일 전"이 들어간다.
        /// </summary>
        public List<DateTime> VisitDates { get; set; }

        /// <summary>기기에 저장한 대화 기억</summary>
        public List<MemoryEntry> Memory { get; set; }

        /// <summary>페르소나가 없는 도깨비의 realStory (기본값).</summary>
        public string RealStory { get; set; }
    }

    /// <summary>컨텍스트 구성기가 만든 것.</summary>
    public sealed class NpcContext
    {
        /// <summary>요청 관리자에 넘길 요청. 후보가 비어 있으면 띄울 반응이 없는 상황이다(버림).</summary>
        public ReactionRequest Request { get; internal set; }

        /// <summary>
        /// 상황 문장에 넣은 가장 중요한 사실이 무엇이었는지.
        /// faded · resealed · recent_care · recall · long_gap_care · story · first_visit · regular_visit · none
        /// </summary>
        public string KeyFact { get; internal set; }

        /// <summary>반응을 띄운 뒤 대화 기억에 남길 것. 없으면 null.</summary>
        public MemoryEntry MemoryToSave { get; internal set; }

        /// <summary>
        /// 반응을 띄우면 꺼냈음(<see cref="MemoryEntry.Recalled"/>)을 true 로 바꿀 기억 — 입력(NpcContextInput.Memory)에 있던 그 기억이다. 없으면 null.
        /// 지난 대화를 꺼낸 안부는 그 기억, 지난 대화를 꺼낸 다음 안부는 그때 남긴 "지난 일을 다시 이야기했다" 기억이다.
        /// <see cref="Finish"/> 가 띄웠으면 이 객체의 값을 true 로 바꾸고, 못 띄웠으면 이 값을 null 로 비운다.
        /// 그래서 게임은 Finish 뒤에 이 값이 null 이 아니면 저장소의 같은 기억에 Recalled = true 를 저장하면 된다.
        /// </summary>
        public MemoryEntry MemoryToMark { get; internal set; }

        private StoryRotation rotation;
        private StoryTicket story;
        private int finished;

        /// <summary>이 요청에 담긴 실화 번호. 이야기 듣기가 아니거나 자료가 없으면 -1.</summary>
        public int StoryIndex
        {
            get { return story != null ? story.Index : -1; }
        }

        /// <summary>
        /// 요청 관리자의 결과로 마무리한다. 요청 하나에 한 번 부른다.
        /// <list type="bullet">
        /// <item>반응을 띄웠으면 대화 기억에 남길 것을 돌려주고, <see cref="MemoryToMark"/> 의 꺼냈음 표시를 true 로 바꾼다.</item>
        /// <item>띄우지 못했으면 이 요청에 고른 실화를 "안 들려줌"으로 되돌리고 null.</item>
        /// </list>
        /// 두 번째부터는 아무것도 하지 않고 null 을 돌려준다.
        /// </summary>
        public MemoryEntry Finish(NpcResponse response)
        {
            if (System.Threading.Interlocked.Exchange(ref finished, 1) == 1)
            {
                return null;
            }
            if (response == null || !response.ShouldShow)
            {
                if (story != null)
                {
                    rotation.Return(story);
                }
                MemoryToMark = null;   // 못 띄웠으면 표시하지 않는다 — 게임이 이 값을 보고 저장하지 않게
                return null;
            }
            if (MemoryToMark != null)
            {
                MemoryToMark.Recalled = true;
            }
            return MemoryToSave;
        }

        internal void HoldStory(StoryRotation storyRotation, StoryTicket ticket)
        {
            rotation = storyRotation;
            story = ticket;
        }
    }

    /// <summary>
    /// 컨텍스트 구성기. 게임 상태(NpcContextInput)를 반응 요청(ReactionRequest)으로 바꾼다.
    /// 무엇을 AI 에게 보여 줄지는 AI 가 아니라 이 규칙이 정한다 — 작은 모델은 목록에서 중요한 사실을 스스로 고르지 못한다.
    ///
    /// <para>하는 일</para>
    /// <list type="number">
    /// <item>후보 정하기: 선택지와 상대(주인/손님)에 맞는 행동만 넣는다. 손님은 도깨비를 건드릴 수 없어 관람(visit)만 된다.</item>
    /// <item>가장 중요한 사실 하나를 상황 문장에 직접 적는다. 안부 묻기는 흐려짐·재봉인 → 3일 안 관리 → 지난 대화 → 없음 순서로 본다.</item>
    /// <item>사실(Facts)은 이번 상황과 관련된 것만, 한국어 값으로 넣는다. 손님 장면에는 선명도를 넣지 않는다.</item>
    /// <item>대화 기억은 이번 행동과 관련된 것만 넣는다 (지난 대화 꺼내기는 그 기억 하나).</item>
    /// <item>이야기 듣기는 StoryRotation 으로 실화를 돌려 가며 골라 verified_story · story_index 를 넣는다. 이야기는 AI 를 부르지 않고
    /// 규칙 기반 생성기가 그 번호의 이야기 대사를 쓴다.</item>
    /// </list>
    /// 문턱값은 규칙 기반 생성기와 같다(3일, 2번). 그래서 AI 가 실패해 기본 대사로 넘어가도 같은 내용으로 대답한다.
    /// </summary>
    public sealed class NpcContextBuilder
    {
        /// <summary>이 일수 안에 관리를 받았으면 안부에서 그 관리를 떠올린다 (규칙 기반 생성기와 같다).</summary>
        public const int RecentCareDays = RuleBasedReactionGenerator.RecentCareDays;

        /// <summary>최근 30일에 이만큼 이상 온 손님은 단골이다 (규칙 기반 생성기와 같다).</summary>
        public const int RegularVisitorDays = RuleBasedReactionGenerator.RegularVisitorDays;

        /// <summary>관리 전 이만큼 지났으면 "오랜만에" 관리한 것이다. 서버에서 7일이 지나면 흐려진다.</summary>
        public const int LongGapDays = 7;

        /// <summary>
        /// 서버에서 선명도 기준 시각 뒤 이만큼 지나면 재봉인된다 (ClarityPolicy.resealedAfter 기본 14일). 7일(LongGapDays)이 지나면 흐려진다.
        /// 기준 시각은 마지막 정화 시각과 다르다 — 재봉인에서 한 칸 올라 흐려짐이 되면 기준 시각은 "지금 - 7일"이라, 마지막 정화 뒤 7일이면 다시 재봉인된다.
        /// </summary>
        public const int ResealedAfterDays = 14;

        /// <summary>이 일수 안의 대화 기억만 꺼낸다.</summary>
        public const int RecallWindowDays = 7;

        private readonly PersonaCatalog personas;
        private readonly StoryRotation stories;

        // 도깨비·상대·선택지마다 이번 앱 실행에서 몇 번째 요청인지. 곁들일 거리를 돌리는 데 쓴다 (Variation)
        private readonly Dictionary<string, int> turns = new Dictionary<string, int>();
        private readonly object turnsGate = new object();

        public NpcContextBuilder(PersonaCatalog personas) : this(personas, new StoryRotation())
        {
        }

        /// <param name="stories">들려준 실화 기록. 규칙 기반 생성기에도 같은 것을 넘기면 기록을 함께 쓴다.</param>
        public NpcContextBuilder(PersonaCatalog personas, StoryRotation stories)
        {
            if (personas == null)
            {
                throw new ArgumentNullException("personas");
            }
            if (stories == null)
            {
                throw new ArgumentNullException("stories");
            }
            this.personas = personas;
            this.stories = stories;
        }

        public NpcContext Build(NpcContextInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }
            var request = new ReactionRequest
            {
                RelicId = input.RelicId,
                Mode = input.Mode,
                Input = input.Input,
                ContentVersion = personas.contentVersion,
                Variation = NextVariation(input)
            };
            var context = new NpcContext { Request = request, KeyFact = "none" };
            bool owner = input.Mode == InteractionMode.Owner;

            // 친밀도: 주인이 이 도깨비를 관리한 누적 횟수가 3·7·12·20번을 채울 때마다 한 단계 (게임의 IntimacyPolicy 와 같은 기준).
            // 손님은 관리하지 않으므로 최근 30일 방문한 날 수로 같은 단계를 매긴다. 3단계부터 닉네임으로 부르고, 0~2단계(또는 닉네임 없음)는 '발굴자'
            request.Nickname = CleanNickname(input.Nickname);
            request.Intimacy = owner ? NpcAddress.IntimacyOf(input.CareCount) : NpcAddress.VisitorLevelOf(VisitCount(input));

            if (input.Input == NpcInputs.Visit)
            {
                if (!owner)
                {
                    Visit(input, context);
                }
                return context;
            }
            if (!owner)
            {
                // 손님은 도깨비를 건드릴 수 없다. 후보가 없으면 요청 관리자가 아무것도 띄우지 않는다
                request.Situation = "손님은 너를 건드릴 수 없다.";
                return context;
            }

            switch (input.Input)
            {
                case NpcInputs.Tap:
                    // 서버 시각의 시간대를 주인이 탭한 때로 녹인다 ("주인이 아침에 너를 탭했다"). 별도 문장으로 "인사에 시간대를 곁들인다"고 했더니
                    // "오늘 아침이라 반가워"처럼 시간대를 반가운 이유로 말하고, "아침에 일찍 찾아뵈었어"처럼 주체를 바꿨다(2026-11-13 시험 S01).
                    // 탭한 때로 적으면 "아침에 찾아줘서 반가워"처럼 자연스럽게 나온다. '화면이 너에게 가까이 다가왔다'는 "화면이 가까워져서"로 옮겨 뺐다
                    if (input.Now.HasValue)
                    {
                        string when = TimeOfDay(input.Now.Value);
                        // 2026-11-17 20:39 시험: "너를 불렀다"를 읽은 모델이 부른 쪽을 뒤바꿔("발굴자를 불러서 반가워") 말했다. 주인이 도깨비를 '보러 찾아왔다'고 적어 방문한 쪽이 주인임을 분명히 한다
                        // ('찾아와'를 넣어 방문한 쪽이 주인임을 분명히 한다 ("아침에 날 깨워줘서", "아침부터 반갑게 맞이해줘서"처럼 주체가 뒤바뀐 말을 줄인다)
                        request.Situation = "주인이 " + when + "에 너를 보러 찾아왔다.";
                        request.Facts.Add(new Fact(NpcFactKeys.TimeOfDay, when));
                    }
                    else
                    {
                        request.Situation = "주인이 너를 보러 찾아왔다.";
                    }
                    AddAwakening(input, request);
                    Candidates(request, NpcActions.Greet);
                    break;
                case NpcInputs.AskStatus:
                    AskStatus(input, context);
                    break;
                case NpcInputs.Care:
                    Care(input, context);
                    break;
                case NpcInputs.ListenStory:
                    Story(input, context);
                    break;
                case NpcInputs.Drag:
                    // 끌려갈 때는 대사를 띄우지 않는다: 두 후보 모두 대사 없는 행동(NpcActions.IsSilent)이라 모델을 부르지 않고
                    // 규칙 기반 생성기가 행동 하나를 골라 빈 대사로 돌려준다 (이 상황 문장은 프롬프트에 실리지 않는다)
                    request.Situation = "주인이 너를 손가락으로 끌어당겨 다른 자리로 옮기고 있다.";
                    Candidates(request, NpcActions.StruggleProtest, NpcActions.StrugglePlay);
                    break;
                default:
                    request.Situation = "";
                    break;
            }
            return context;
        }

        /// <summary>
        /// 미리 만들 요청들 (NpcRequestManager.Prefetch 에 넘긴다). 지금 상태(state)에서 곧 부를 가능성이 큰 선택지의 요청을 만든다:
        /// 주인 장면은 탭 인사 · 안부 묻기 · 관리하기(관리 뒤 반응, 지금 선명도가 관리 전 값), 손님 장면은 손님 관람.
        /// 이야기 듣기·끌어당기기는 모델을 쓰지 않아 넣지 않는다. state.Input 은 무시한다. 대화 기억을 저장하거나 이야기를 고르는 일은 하지 않는다.
        /// </summary>
        public List<ReactionRequest> PrefetchRequests(NpcContextInput state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            var requests = new List<ReactionRequest>();
            string[] inputs = state.Mode == InteractionMode.Owner
                    ? new[] { NpcInputs.Tap, NpcInputs.AskStatus, NpcInputs.Care }
                    : new[] { NpcInputs.Visit };
            foreach (string input in inputs)
            {
                var copy = new NpcContextInput
                {
                    RelicId = state.RelicId, Mode = state.Mode, Input = input, Today = state.Today, Clarity = state.Clarity,
                    ClarityAfterCare = input == NpcInputs.Care ? StepUp(state.Clarity) : null,   // 보통 난이도로 한 판 깬 뒤
                    AwakeningStage = state.AwakeningStage, LastPurifiedAt = state.LastPurifiedAt, RecentVisitDays = state.RecentVisitDays,
                    CareCount = state.CareCount, Now = state.Now, Nickname = state.Nickname, VisitDates = state.VisitDates, RealStory = state.RealStory,
                    Memory = state.Memory != null ? new List<MemoryEntry>(state.Memory) : new List<MemoryEntry>()
                };
                ReactionRequest request = Build(copy).Request;
                if (request.Candidates.Count > 0 && !NpcActions.SkipsModel(request.Candidates))
                {
                    requests.Add(request);
                }
            }
            return requests;
        }

        /// <summary>
        /// <b>서버 값을 기다리지 않고</b> 미리 만들 요청들 (NpcRequestManager.PrefetchTemplate 에 넘긴다). 서버가 넘겨주는 값(시간대·닉네임·방문 횟수)은 견본으로 채운다:
        /// 시간대는 낮(state.Now 가 없으면 Today 13시), 호칭은 '발굴자'(닉네임 없음), 손님 방문은 처음 온 친구(1번)와 단골(3번) 두 가지.
        /// 만든 대사에서 견본 값에 달린 말은 칸으로 남고(NpcLineSlots), 서버 값이 온 뒤의 요청이 그 칸에 실제 값을 넣어 완성한다.
        /// 주인 장면은 탭 인사, 선명도(state.Clarity)를 알면 안부 묻기·관리하기도. 손님 장면은 손님 관람. 선명도·마지막 관리·기억은 어떤 말을 할지 정하므로 견본이 아니라 state 의 값을 쓴다.
        /// 단 관리하기(정화)는 다음 정화 때 쓸 대사라 선명·흐려짐·재봉인 세 상태를 모두 만든다(CareSamples) — 주인 장면은 탭 인사·안부·정화 셋으로 5개.
        /// 지금 재봉인이거나 한 칸 올라 흐려짐이면, 마저 닦아 선명이 되는 정화도 만든다 — 6개.
        /// 안부가 지난 대화 꺼내기면 그다음 안부(꺼낸 뒤 한 번 쉬는 평소 안부)도 만든다 — 하나 더.
        /// </summary>
        public List<ReactionRequest> PrefetchTemplates(NpcContextInput state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            var requests = new List<ReactionRequest>();
            var inputs = new List<string>();
            if (state.Mode == InteractionMode.Owner)
            {
                inputs.Add(NpcInputs.Tap);
                if (!string.IsNullOrEmpty(state.Clarity))
                {
                    inputs.Add(NpcInputs.AskStatus);
                    inputs.Add(NpcInputs.Care);
                }
            }
            else
            {
                inputs.Add(NpcInputs.Visit);
            }
            // 손님 방문은 처음 온 친구와 단골이 다른 후보(인사 / 알아보기)를 쓰므로 견본을 둘 만든다
            var visitSamples = new List<List<DateTime>>();
            if (state.Mode == InteractionMode.Owner)
            {
                visitSamples.Add(null);
            }
            else
            {
                visitSamples.Add(new List<DateTime> { state.Today });
                visitSamples.Add(new List<DateTime> { state.Today, state.Today.AddDays(-2), state.Today.AddDays(-5) });
            }
            foreach (string input in inputs)
            {
                foreach (ClaritySample clarity in CareSamples(input, state))
                foreach (List<DateTime> visits in visitSamples)
                {
                    var copy = new NpcContextInput
                    {
                        RelicId = state.RelicId, Mode = state.Mode, Input = input, Today = state.Today, Clarity = clarity.Before,
                        ClarityAfterCare = clarity.After,
                        AwakeningStage = state.AwakeningStage, LastPurifiedAt = clarity.LastPurifiedAt, RealStory = state.RealStory,
                        Now = state.Now.HasValue ? state.Now : state.Today.Date.AddHours(13),
                        Nickname = null,
                        VisitDates = visits,
                        Memory = state.Memory != null ? new List<MemoryEntry>(state.Memory) : new List<MemoryEntry>()
                    };
                    NpcContext built = Build(copy);
                    ReactionRequest request = built.Request;
                    if (request.Candidates.Count > 0 && !NpcActions.SkipsModel(request.Candidates))
                    {
                        requests.Add(request);
                    }
                    if (built.KeyFact == "recall")
                    {
                        // 지난 대화를 꺼낸 다음 안부는 평소 안부다(꺼낸 뒤 한 번 쉰다). 기억을 꺼내는 대사는 쓴 뒤 다시 만들지 않으므로
                        // 그다음 안부의 대사도 미리 만들어 둔다 — 기억이 없을 때와 같은 요청이다
                        copy.Memory = new List<MemoryEntry>();
                        ReactionRequest next = Build(copy).Request;
                        if (next.Candidates.Count > 0 && !NpcActions.SkipsModel(next.Candidates))
                        {
                            requests.Add(next);
                        }
                    }
                }
            }
            return requests;
        }

        // 견본 하나: 관리 전 선명도, 마지막 정화 시각, 관리 뒤 선명도(관리하기만, 아니면 null)
        private sealed class ClaritySample
        {
            public ClaritySample(string before, DateTime? lastPurifiedAt, string after)
            {
                Before = before;
                LastPurifiedAt = lastPurifiedAt;
                After = after;
            }

            public string Before { get; private set; }

            public DateTime? LastPurifiedAt { get; private set; }

            public string After { get; private set; }
        }

        // 정화(관리)는 지금 상태가 아니라 <b>다음 정화 때의 상태</b>로 쓸 대사다. 다음 정화가 언제일지 모르므로 세 상태(선명·흐려짐·재봉인)를 모두 견본으로 만든다.
        // 날 수는 견본(1·9·16일)이고 대사에서는 {날수} 칸으로 남는다. 관리 뒤 선명도는 보통 난이도로 한 판 깬 값이다(재봉인 → 흐려짐, 흐려짐 → 선명) —
        // 어려움으로 재봉인에서 바로 선명이 되면 견본과 달라 그 자리에서 만든다.
        // 지금 재봉인이거나 한 칸 올라 흐려짐이면(PartlyRestored) 마저 닦는 정화(최근에 관리받은 흐려짐 → 선명)가 곧 온다 — 그 견본도 만든다.
        // 다른 선택지는 지금 상태 하나만 쓴다
        private static List<ClaritySample> CareSamples(string input, NpcContextInput state)
        {
            var samples = new List<ClaritySample>();
            if (input != NpcInputs.Care)
            {
                samples.Add(new ClaritySample(state.Clarity, state.LastPurifiedAt, null));
                return samples;
            }
            DateTime today = state.Today.Date;
            samples.Add(new ClaritySample("CLEAR", today.AddDays(-1), "CLEAR"));
            samples.Add(new ClaritySample("FADED", today.AddDays(-(LongGapDays + 2)), "CLEAR"));
            samples.Add(new ClaritySample("RESEALED", today.AddDays(-(ResealedAfterDays + 2)), "FADED"));
            string now = Upper(state.Clarity);
            if (now == "RESEALED" || PartlyRestored(now, DaysSince(state.LastPurifiedAt, state.Today)))
            {
                samples.Add(new ClaritySample("FADED", today, "CLEAR"));
            }
            return samples;
        }

        /// <summary>
        /// 흐려짐인데 마지막 정화가 7일(LongGapDays) 안인지: 재봉인에서 보통 난이도로 한 칸 올라 흐려짐이 된 정령이다.
        /// 오래 방치한 것이 아니라 아직 덜 나은 것이다 (방치해서 흐려지려면 마지막 정화 뒤 7일이 지나야 한다).
        /// </summary>
        public static bool PartlyRestored(string clarity, int? daysSinceCare)
        {
            return Upper(clarity) == "FADED" && daysSinceCare.HasValue && daysSinceCare.Value < LongGapDays;
        }

        // 보통 난이도로 한 판 깬 뒤의 선명도: 재봉인 → 흐려짐, 그 밖은 선명. 모르면 null(선명으로 본다)
        private static string StepUp(string clarity)
        {
            string value = Upper(clarity);
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            return value == "RESEALED" ? "FADED" : "CLEAR";
        }

        // ---------- 선택지별 ----------

        private void AskStatus(NpcContextInput input, NpcContext context)
        {
            ReactionRequest request = context.Request;
            string clarity = Upper(input.Clarity);
            int? days = DaysSince(input.LastPurifiedAt, input.Today);
            string situation = "주인이 안부를 물었다.";
            // 지난 대화를 꺼낸 뒤 아직 안부를 평소대로 한 적이 없으면(가장 최근 recall_talk 에 표시가 없으면) 이번 안부는 평소대로 하고,
            // 띄운 뒤 그 recall_talk 에 표시해 한 번 쉰 것을 남긴다 — 안부마다 기억만 꺼내지 않고 선명도·관리 이야기도 하게 (①②④ 모두 쉰 것으로 친다)
            MemoryEntry rest = PendingRest(input.Memory);
            context.MemoryToMark = rest;

            if (clarity == "FADED" || clarity == "RESEALED")
            {
                // ① 흐려짐·재봉인: 관리를 부탁해야 하므로 가장 먼저 본다
                bool resealed = clarity == "RESEALED";
                // 재봉인은 "다시 봉인되었다"만 적으면 Gemma 가 관리를 부탁하지 않았다(0/3). 흐려진 끝에 봉인됐고 닦아 줘야
                // 깨어난다는 것까지 적으면 4/4 부탁했다. 흐려짐도 "흐려졌다"만 적으면 Mi:dm 이 상태만 말하고 부탁하지 않았다(0/3).
                // 며칠인지는 '오래'로 줄인다(며칠은 사실 칸에 있다). "9일 동안 관리를 못 받아 흐려졌어요."처럼 앞 문장이 길면
                // Kanana 의 부탁이 40자를 한 글자 넘겨 잘렸다(S03 0/3, 2026-11-11). 부탁을 이유보다 앞에 두는 것도 해 봤지만
                // Kanana 는 그래도 흐려진 이야기부터 했다. 작은 모델은 상황 문장을 그대로 옮기므로 '~서'로 잇지 않고 짧은 문장
                // 둘로 쓴다 ("~해 흐려져서, ~" 를 옮겨 "닦아 주셔서 선명해져서"처럼 '~서'가 겹쳤다)
                // 재봉인에서 한 칸 올라 흐려짐이 된 정령(PartlyRestored)은 방금 관리받았다 — "오래 관리를 받지 못해"가 아니라 아직 덜 나은 것으로 적는다.
                // 날짜는 적지 않는다(상황 문장은 미리 만든 대사의 열쇠라, 날짜가 들어가면 날마다 다른 열쇠가 된다. 날 수는 사실 칸에 있다)
                if (!resealed && PartlyRestored(clarity, days))
                {
                    request.Situation = situation + " 너는 최근에 관리를 받아 다시 깨어났지만 아직 흐리다. 주인에게 마저 닦아 달라고 부탁한다.";
                }
                else
                {
                    request.Situation = situation + " 너는 오래 관리를 받지 못해 "
                            + (resealed ? "흐려지다 못해 다시 봉인되었다. 주인에게 깨워 달라고 부탁한다."
                                        : "흐려졌다. 주인에게 닦아 달라고 부탁한다.");
                }
                context.KeyFact = resealed ? "resealed" : "faded";
                AddClarity(input, request);
                AddLastCare(days, request);
                Candidates(request, NpcActions.AnswerStatus);
                return;
            }
            if (days.HasValue && days.Value <= RecentCareDays)
            {
                // ② 최근 관리: 주인이 해 준 일을 떠올리게 한다. 일어난 일만 적으면 Mi:dm 이 "덕분에 더 맑아졌어요"처럼
                // 언제 무엇을 해 줬는지 빼고 고마워했다(S04 2/3). 고마워할 일을 적되, '주인이'를 넣으면 Kanana 가 주인에게
                // "주인이 닦아 관리해 주셔서"라고 그대로 옮겨서, 받은 쪽에서 쓴다 (도깨비는 닦이는 쪽이다). "받은 덕분에 선명해서
                // 고맙다"는 "닦아 주셔서 선명해져서 고마워요"로 옮겨 '~서'가 겹쳐서, 고마운 일 하나만 적는다(선명은 사실 칸에 있다)
                request.Situation = situation + " 너는 " + WhenAt(days.Value) + " 닦아 관리를 받았다. 고마움을 전한다.";
                context.KeyFact = "recent_care";
                AddClarity(input, request);
                AddLastCare(days, request);
                Candidates(request, NpcActions.AnswerStatus);
                return;
            }
            MemoryEntry recall = rest == null ? RecallTarget(input.Memory) : null;
            if (recall != null)
            {
                // ③ 지난 대화: 게임이 꺼내기로 정한다. 그 기억 하나만 주고, 상관없는 상태(선명도)는 뺀다. 띄우면 그 기억에 꺼냈음을 표시한다
                request.Situation = situation + " " + RecallSentence(recall);
                request.Memory.Add(recall);
                context.KeyFact = "recall";
                context.MemoryToMark = recall;
                context.MemoryToSave = new MemoryEntry(0, NpcActions.RecallTalk, "주인과 지난 일을 다시 이야기했다");
                Candidates(request, NpcActions.RecallTalk);
                return;
            }
            // ④ 그 밖: 평소 상태
            request.Situation = situation;
            AddClarity(input, request);
            AddLastCare(days, request);
            Candidates(request, NpcActions.AnswerStatus);
        }

        private static void Care(NpcContextInput input, NpcContext context)
        {
            ReactionRequest request = context.Request;
            string before = Upper(input.Clarity);
            string after = string.IsNullOrEmpty(input.ClarityAfterCare) ? "CLEAR" : Upper(input.ClarityAfterCare);
            int? days = DaysSince(input.LastPurifiedAt, input.Today);
            bool partly = PartlyRestored(before, days);
            // 흐려짐·재봉인이어도 한 칸 올라 흐려짐이 된 정령(partly)은 며칠 전에 관리받았다 — "오랜만에"가 아니다
            bool longGap = (days.HasValue && days.Value >= LongGapDays) || ((before == "FADED" || before == "RESEALED") && !partly);

            string situation = longGap
                    ? "주인이 오랜만에 너를 관리해 주었다."
                    : "주인이 너를 관리해 주었다.";
            // 관리 결과 사실은 프롬프트에 싣지 않으므로(HiddenFactKeys) 다 낫지 않았으면 상황 문장에 적는다 — 적지 않으면 재봉인에서 흐려짐까지만 올랐는데
            // 다 나은 것처럼 고마워한다. 한 칸 올라 흐려짐이던 정령이 마저 선명해졌으면 그것도 적는다(선명 → 선명과 같은 프롬프트가 되지 않게)
            if (after == "RESEALED")
            {
                // 페이즈가 남은 중간 판: 서버가 재봉인 그대로 돌려준다 — 깨어났다고 적으면 정산 결과와 어긋난다 (Copilot 리뷰)
                situation += " 너는 아직 봉인되어 있다.";
            }
            else if (after != "CLEAR")
            {
                // "다시 깨어났지만"은 재봉인 → 흐려짐에서만. 흐려짐 그대로(중간 판)면 아직 흐리다고만 적는다
                situation += before == "RESEALED" ? " 너는 다시 깨어났지만 아직 흐리다." : " 너는 아직 흐리다.";
            }
            else if (partly)
            {
                situation += " 너는 다시 맑아졌다.";
            }
            request.Situation = situation;
            string result = string.IsNullOrEmpty(before) ? ClarityLabel(after) : ClarityLabel(before) + "→" + ClarityLabel(after);
            request.Facts.Add(new Fact(NpcFactKeys.CareResult, result));
            if (longGap)
            {
                context.KeyFact = "long_gap_care";
                AddLastCare(days, request);
            }
            Candidates(request, NpcActions.ThankJoyful, NpcActions.ThankCalm);
        }

        private void Story(NpcContextInput input, NpcContext context)
        {
            ReactionRequest request = context.Request;
            request.Situation = "주인이 이야기 듣기를 골랐다.";
            context.KeyFact = "story";
            Candidates(request, NpcActions.TellStory);

            Persona persona = personas.Find(input.RelicId);
            string fact = null;
            string summary = null;
            int index = -1;
            StoryTicket ticket = null;
            if (persona != null && persona.stories != null && persona.stories.Count > 0)
            {
                ticket = stories.Take(input.RelicId, persona.stories.Count);
                StoryPiece piece = persona.stories[ticket.Index];
                fact = piece.fact;
                summary = piece.MemorySummary();
            }
            else
            {
                List<string> sentences = Sentences(input.RealStory);
                if (sentences.Count > 0)
                {
                    ticket = stories.Take(input.RelicId, sentences.Count);
                    fact = sentences[ticket.Index];
                    summary = "주인에게 내 내력 이야기를 들려줬다";
                }
            }
            if (fact == null)
            {
                return; // 자료가 없으면 기본 이야기 대사로 대신된다
            }
            // 들려준 것으로 먼저 기록하고, 반응을 띄우지 못하면 context.Finish 가 되돌린다
            index = ticket.Index;
            context.HoldStory(stories, ticket);
            request.Facts.Add(new Fact(NpcFactKeys.VerifiedStory, fact));
            request.Facts.Add(new Fact(NpcFactKeys.StoryIndex, index.ToString(CultureInfo.InvariantCulture)));
            // 들려준 이야기는 기억에 남겨, 다음 안부에서 지난 대화로 꺼낼 수 있게 한다
            context.MemoryToSave = new MemoryEntry(0, NpcActions.TellStory, summary);
        }

        private static void Visit(NpcContextInput input, NpcContext context)
        {
            ReactionRequest request = context.Request;
            int visits = Math.Max(1, VisitCount(input) ?? 1);
            // 상황 문장은 짧게 둔다: "친구는 너를 건드릴 수 없고 구경만 한다. 너는 걸어 다니며 짧은 말풍선을 띄운다."를 달았더니 모델이 "너는 순찰을 돌며 친구에게 인사해."처럼
            // 옮겼고, 읽을 글도 길어졌다 (구경만 한다는 것은 프롬프트의 '지금 상대: 친구(구경만 한다)' 에 있다)
            // 2026-11-17 18:21 시험: "처음 온 친구가 전시관을 둘러보고 있다"를 읽은 모델이 자기가 구경하는 것처럼 말했다("전시관을 둘러보니 참 멋진데!"). '우리 전시관'으로 도깨비가 사는 곳임을 밝힌다
            const string watching = "";
            if (visits >= RegularVisitorDays)
            {
                request.Situation = "최근 30일 동안 " + visits + "번 온 친구가 와 있다." + watching; // 22:07 시험: "전시관"을 읽은 모델이 "전시관이 참 포근하네"처럼 전시관의 인상을 말해 명사를 뺐다
                context.KeyFact = "regular_visit";
                // 단골에게 인사 후보까지 주면 모델이 "처음 온 친구라서 반가워"라고 하는 인사를 골랐다(2026-11-12 11:45 시험, S13 말발굽 4번 중 2번).
                // 알아보기 하나만 두면 행동 칸도 빠져 출력 토큰이 줄어든다
                Candidates(request, NpcActions.WanderRecall);
            }
            else
            {
                request.Situation = "처음 온 친구가 와 있다." + watching;
                context.KeyFact = "first_visit";
                Candidates(request, NpcActions.WanderGreet);
            }
            // 선명도는 방치 상태라 손님에게 보여 주지 않는다
            request.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, visits.ToString(CultureInfo.InvariantCulture)));
            // 방문 기록이 있으면 며칠 전에 왔는지 그대로 싣는다 — 대사의 "N일 전"은 이 기록과 같아야 한다 (출력 검사가 본다)
            string days = VisitDaysText(input);
            if (days != null)
            {
                request.Facts.Add(new Fact(NpcFactKeys.VisitDays, days));
            }
            AddAwakening(input, request);
        }

        // ---------- 시간대·호칭·방문 기록 ----------

        /// <summary>
        /// 서버 시각의 시간대: 새벽(0~5시) · 아침(5~9) · 오전(9~12) · 낮(12~17) · 저녁(17~21) · 밤(21~24).
        /// run.py 의 time_of_day 와 같다.
        /// </summary>
        public static string TimeOfDay(DateTime now)
        {
            int hour = now.Hour;
            return hour < 5 ? "새벽" : hour < 9 ? "아침" : hour < 12 ? "오전" : hour < 17 ? "낮" : hour < 21 ? "저녁" : "밤";
        }

        // 닉네임: 앞뒤 공백·줄바꿈·제어 문자를 없애고 20자로 자른다. 비면 null
        private static string CleanNickname(string nickname)
        {
            if (string.IsNullOrEmpty(nickname))
            {
                return null;
            }
            var builder = new System.Text.StringBuilder();
            foreach (char c in nickname)
            {
                if (!char.IsControl(c))
                {
                    builder.Append(c);
                }
            }
            string clean = builder.ToString().Trim();
            if (clean.Length > 20)
            {
                clean = clean.Substring(0, 20);
            }
            return clean.Length == 0 ? null : clean;
        }

        // 최근 30일(오늘 포함)의 방문한 날 수. 방문 기록(VisitDates)이 있으면 그것으로, 없으면 RecentVisitDays
        private static int? VisitCount(NpcContextInput input)
        {
            // 방문 기록(VisitDates)을 받았으면 비었거나 30일 밖이어도 그 날 수(0 포함)다. RecentVisitDays 는 기록을 안 주는 호출자용이다
            return input.VisitDates != null ? VisitDaysAgo(input).Count : input.RecentVisitDays;
        }

        // 방문한 날들을 며칠 전인지로 (0 = 오늘), 가까운 날부터, 같은 날은 하나. 30일 밖·미래는 뺀다
        private static List<int> VisitDaysAgo(NpcContextInput input)
        {
            var result = new List<int>();
            if (input.VisitDates != null)
            {
                foreach (DateTime date in input.VisitDates)
                {
                    int days = (input.Today.Date - date.Date).Days;
                    if (days >= 0 && days < 30 && !result.Contains(days))
                    {
                        result.Add(days);
                    }
                }
            }
            result.Sort();
            return result;
        }

        // "오늘, 3일 전, 9일 전" (가까운 10일까지). 기록이 없으면 null
        private static string VisitDaysText(NpcContextInput input)
        {
            List<int> ago = VisitDaysAgo(input);
            if (ago.Count == 0)
            {
                return null;
            }
            var parts = new List<string>();
            foreach (int days in ago.GetRange(0, Math.Min(10, ago.Count)))
            {
                parts.Add(days == 0 ? "오늘" : days == 1 ? "어제" : days + "일 전");
            }
            return string.Join(", ", parts.ToArray());
        }

        // ---------- 곁들일 거리 ----------

        // 같은 도깨비·상대·선택지로 거듭 부르면 1씩 늘어, 프롬프트 조립기가 곁들일 거리를 차례로 바꿔 싣는다.
        // 시작점은 날마다 옮긴다 — 실행할 때마다 0부터 세면, 앱을 새로 켠 날의 첫 인사가 늘 같은 거리로 시작한다
        private int NextVariation(NpcContextInput input)
        {
            string key = input.RelicId.ToString(CultureInfo.InvariantCulture) + ":" + input.Mode + ":" + input.Input;
            int turn;
            lock (turnsGate)
            {
                turns.TryGetValue(key, out turn);
                turns[key] = turn + 1;
            }
            int day = (int)(input.Today.Date.Ticks / TimeSpan.TicksPerDay % 1000);
            return day + turn;
        }

        // ---------- 지난 대화 ----------

        // 꺼낼 기억: 7일 안, 요약이 있고, 지난 대화 꺼내기 자신이 아니고, 아직 꺼내지 않은(Recalled false) 것 중 <b>가장 오래된 것</b>.
        // 먼저 들어온 기억부터 차례로 꺼낸다(Queue) — 새 이야기를 들려줘도 앞의 기억이 7일을 넘겨 빠지기 전에 차례가 온다.
        // 예전에는 꺼낸 기억을 표시하지 않아 늘 가장 최근 것만 골랐고, 같은 것을 다시 고르지 않으려고 꺼낸 뒤에는 새 이야기가 올 때까지 꺼내지 않았다
        private static MemoryEntry RecallTarget(List<MemoryEntry> memory)
        {
            List<MemoryEntry> recent = Recent(memory);
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                MemoryEntry entry = recent[i];
                if (entry.ActionId != NpcActions.RecallTalk && !entry.Recalled && !string.IsNullOrEmpty(entry.Summary))
                {
                    return entry;
                }
            }
            return null;
        }

        // 가장 최근에 지난 대화를 꺼낸 뒤 아직 한 번도 쉬지 않았으면 그때 남긴 recall_talk 기억, 아니면 null.
        // 가장 최근 기억만 보지 않는다 — 꺼낸 뒤 이야기를 들려줘 그 기억이 앞에 와도, 다음 안부는 쉬어야 한다.
        // 가장 최근 recall_talk 하나만 본다 — 표시 없이 저장된 옛 recall_talk 가 여럿이어도 한 번만 쉰다
        private static MemoryEntry PendingRest(List<MemoryEntry> memory)
        {
            foreach (MemoryEntry entry in Recent(memory))
            {
                if (entry.ActionId == NpcActions.RecallTalk)
                {
                    return entry.Recalled ? null : entry;
                }
            }
            return null;
        }

        // "주인에게 암행어사 이야기를 들려줬다" → "너는 2일 전 주인에게 암행어사 이야기를 들려줬던 일이 떠올랐다."
        private static string RecallSentence(MemoryEntry memory)
        {
            string summary = memory.Summary.Trim().TrimEnd('.');
            string when = WhenAgo(memory.DaysAgo);
            if (summary.EndsWith("다", StringComparison.Ordinal))
            {
                return "너는 " + when + " " + summary.Substring(0, summary.Length - 1) + "던 일이 떠올랐다.";
            }
            return "너는 " + when + " 있었던 일이 떠올랐다: " + summary + ".";
        }

        // 7일 안의 기억, 최근 것부터
        private static List<MemoryEntry> Recent(List<MemoryEntry> memory)
        {
            var recent = new List<MemoryEntry>();
            if (memory != null)
            {
                foreach (MemoryEntry entry in memory)
                {
                    if (entry != null && entry.DaysAgo >= 0 && entry.DaysAgo <= RecallWindowDays)
                    {
                        recent.Add(entry);
                    }
                }
            }
            // 같은 날이면 넣은 순서의 뒤쪽(나중 것)이 앞에 오게 한다
            var ordered = new List<MemoryEntry>();
            for (int days = 0; days <= RecallWindowDays; days++)
            {
                for (int i = recent.Count - 1; i >= 0; i--)
                {
                    if (recent[i].DaysAgo == days)
                    {
                        ordered.Add(recent[i]);
                    }
                }
            }
            return ordered;
        }

        // ---------- 사실 ----------

        private static void AddClarity(NpcContextInput input, ReactionRequest request)
        {
            if (!string.IsNullOrEmpty(input.Clarity))
            {
                request.Facts.Add(new Fact(NpcFactKeys.Clarity, ClarityLabel(Upper(input.Clarity))));
            }
        }

        private static void AddLastCare(int? days, ReactionRequest request)
        {
            if (days.HasValue)
            {
                request.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, days.Value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private static void AddAwakening(NpcContextInput input, ReactionRequest request)
        {
            if (!string.IsNullOrEmpty(input.AwakeningStage))
            {
                request.Facts.Add(new Fact(NpcFactKeys.Awakening, AwakeningLabel(Upper(input.AwakeningStage))));
            }
        }

        // 영어 값만 주면 작은 모델이 뜻을 놓친다. 프롬프트에는 한국어 값으로 넣는다 (규칙 기반 생성기는 둘 다 읽는다)
        private static string ClarityLabel(string clarity)
        {
            switch (clarity)
            {
                case "CLEAR": return "선명";
                case "FADED": return "흐려짐";
                case "RESEALED": return "재봉인";
                default: return clarity;
            }
        }

        private static string AwakeningLabel(string stage)
        {
            switch (stage)
            {
                case "AWAKENED": return "각성";
                case "AWAKENED_PLUS": return "각성+";
                case "FULLY_AWAKENED": return "완전 각성";
                default: return stage;
            }
        }

        // ---------- 공용 ----------

        private static void Candidates(ReactionRequest request, params string[] actionIds)
        {
            foreach (string actionId in actionIds)
            {
                request.Candidates.Add(new ActionCandidate(actionId, NpcTargets.Player));
            }
        }

        private static int? DaysSince(DateTime? at, DateTime today)
        {
            if (!at.HasValue)
            {
                return null;
            }
            return Math.Max(0, (today.Date - at.Value.Date).Days);
        }

        // 며칠 전: 오늘·어제만 낱말로, 2일 전부터는 숫자로 쓴다("2일 전"). 모델은 상황 문장의 날 수를 그대로 옮기는데, "이틀·사흘"처럼 한글로 주면
        // 대사에도 한글 날 수가 나와 미리 만든 대사의 날 수 칸({날수})으로 만들 수 없다 — 숫자로 주면 "2일 전"이 칸이 되어 다른 날 수에도 쓴다 (2026-11-18)
        private static string WhenAgo(int days)
        {
            switch (days)
            {
                case 0: return "오늘";
                case 1: return "어제";
                default: return days + "일 전";
            }
        }

        // 문장 앞에 붙는 때: "오늘", "어제", "2일 전에"
        private static string WhenAt(int days)
        {
            return days <= 1 ? WhenAgo(days) : WhenAgo(days) + "에";
        }

        private static string Upper(string value)
        {
            return value == null ? null : value.Trim().ToUpperInvariant();
        }

        // realStory 를 실화 하나씩으로 나눈다. 서버 시드는 실화 3개를 줄바꿈으로 잇는다 (마침표가 없다).
        // 줄바꿈이 없는 옛 글은 마침표로 나눈다. 끝의 마침표는 뗀다 (규칙 기반 생성기가 붙인다)
        private static List<string> Sentences(string text)
        {
            var sentences = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return sentences;
            }
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                bool end = text[i] == '\n'
                        || (text[i] == '.' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])));
                if (end)
                {
                    AddSentence(sentences, text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            AddSentence(sentences, text.Substring(start));
            return sentences;
        }

        private static void AddSentence(List<string> sentences, string sentence)
        {
            string trimmed = sentence.Trim().TrimEnd('.');
            if (trimmed.Length > 0)
            {
                sentences.Add(trimmed);
            }
        }
    }
}
