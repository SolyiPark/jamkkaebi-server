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

        /// <summary>마지막 플레이(관리) 시각</summary>
        public DateTime? LastPurifiedAt { get; set; }

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
        /// 친밀도는 이 날 수로 정한다(3·7·12·20번을 채울 때마다 한 단계, NpcAddress.IntimacyOf). 3단계(12번)부터 닉네임으로 부르고, 0~2단계는 '발굴자'로 부른다.
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
        /// <item>반응을 띄웠으면 대화 기억에 남길 것을 돌려준다.</item>
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
                return null;
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

            // 친밀도: 최근 30일 방문한 날 수가 3·7·12·20번을 채울 때마다 한 단계. 3단계부터 닉네임으로 부르고, 0~2단계(또는 닉네임 없음)는 '발굴자'
            request.Nickname = CleanNickname(input.Nickname);
            int? visitCount = VisitCount(input);
            request.Intimacy = NpcAddress.IntimacyOf(visitCount);

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
                    AwakeningStage = state.AwakeningStage, LastPurifiedAt = state.LastPurifiedAt, RecentVisitDays = state.RecentVisitDays,
                    Now = state.Now, Nickname = state.Nickname, VisitDates = state.VisitDates, RealStory = state.RealStory,
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
                foreach (List<DateTime> visits in visitSamples)
                {
                    var copy = new NpcContextInput
                    {
                        RelicId = state.RelicId, Mode = state.Mode, Input = input, Today = state.Today, Clarity = state.Clarity,
                        AwakeningStage = state.AwakeningStage, LastPurifiedAt = state.LastPurifiedAt, RealStory = state.RealStory,
                        Now = state.Now.HasValue ? state.Now : state.Today.Date.AddHours(13),
                        Nickname = null,
                        VisitDates = visits,
                        Memory = state.Memory != null ? new List<MemoryEntry>(state.Memory) : new List<MemoryEntry>()
                    };
                    ReactionRequest request = Build(copy).Request;
                    if (request.Candidates.Count > 0 && !NpcActions.SkipsModel(request.Candidates))
                    {
                        requests.Add(request);
                    }
                }
            }
            return requests;
        }

        // ---------- 선택지별 ----------

        private void AskStatus(NpcContextInput input, NpcContext context)
        {
            ReactionRequest request = context.Request;
            string clarity = Upper(input.Clarity);
            int? days = DaysSince(input.LastPurifiedAt, input.Today);
            string situation = "주인이 안부를 물었다.";

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
                request.Situation = situation + " 너는 오래 관리를 받지 못해 "
                        + (resealed ? "흐려지다 못해 다시 봉인되었다. 주인에게 깨워 달라고 부탁한다."
                                    : "흐려졌다. 주인에게 닦아 달라고 부탁한다.");
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
            MemoryEntry recall = RecallTarget(input.Memory);
            if (recall != null)
            {
                // ③ 지난 대화: 게임이 꺼내기로 정한다. 그 기억 하나만 주고, 상관없는 상태(선명도)는 뺀다
                request.Situation = situation + " " + RecallSentence(recall);
                request.Memory.Add(recall);
                context.KeyFact = "recall";
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
            int? days = DaysSince(input.LastPurifiedAt, input.Today);
            bool longGap = (days.HasValue && days.Value >= LongGapDays) || before == "FADED" || before == "RESEALED";

            request.Situation = longGap
                    ? "주인이 오랜만에 너를 관리해 주었다."
                    : "주인이 너를 관리해 주었다.";
            string result = string.IsNullOrEmpty(before) ? "선명" : ClarityLabel(before) + "→선명";
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

        // 꺼낼 기억: 7일 안, 요약이 있고 지난 대화 꺼내기 자신이 아닌 것 중 가장 최근 것.
        // 바로 전에 지난 대화를 꺼냈으면(가장 최근 기억이 recall_talk) 이번에는 꺼내지 않는다 — 안부마다 같은 이야기를 하지 않게
        private static MemoryEntry RecallTarget(List<MemoryEntry> memory)
        {
            List<MemoryEntry> recent = Recent(memory);
            if (recent.Count == 0 || recent[0].ActionId == NpcActions.RecallTalk)
            {
                return null;
            }
            foreach (MemoryEntry entry in recent)
            {
                if (entry.ActionId != NpcActions.RecallTalk && !string.IsNullOrEmpty(entry.Summary))
                {
                    return entry;
                }
            }
            return null;
        }

        // "주인에게 암행어사 이야기를 들려줬다" → "너는 이틀 전 주인에게 암행어사 이야기를 들려줬던 일이 떠올랐다."
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

        private static string WhenAgo(int days)
        {
            switch (days)
            {
                case 0: return "오늘";
                case 1: return "어제";
                case 2: return "이틀 전";
                case 3: return "사흘 전";
                case 4: return "나흘 전";
                default: return days + "일 전";
            }
        }

        // 문장 앞에 붙는 때: "오늘", "어제", "이틀 전에"
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
