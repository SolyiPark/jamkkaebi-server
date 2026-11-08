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

        /// <summary>친구 전시관의 recentVisitDays (지금 열람 포함, 첫 방문이면 1).</summary>
        public int? RecentVisitDays { get; set; }

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
    /// <item>대화 기억은 이번 행동과 관련된 것만 넣는다 (지난 대화 꺼내기는 그 기억 하나, 이야기는 마지막 이야기 하나).</item>
    /// <item>이야기 듣기는 StoryRotation 으로 실화를 돌려 가며 골라 verified_story · story_index 를 넣는다.</item>
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
                ContentVersion = personas.contentVersion
            };
            var context = new NpcContext { Request = request, KeyFact = "none" };
            bool owner = input.Mode == InteractionMode.Owner;

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
                    request.Situation = "주인이 너를 탭했다. 화면이 너에게 가까이 다가왔다.";
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
                    request.Situation = "주인이 너를 손가락으로 끌어당겨 다른 자리로 옮기고 있다.";
                    Candidates(request, NpcActions.StruggleProtest, NpcActions.StrugglePlay);
                    break;
                default:
                    request.Situation = "";
                    break;
            }
            return context;
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
                string gap = days.HasValue ? days.Value + "일 동안 " : "";
                // 재봉인은 "다시 봉인되었다"만 적으면 Gemma 가 관리를 부탁하지 않았다(0/3). 흐려진 끝에 봉인됐고 닦아 줘야
                // 깨어난다는 것까지 적으면 4/4 부탁했다. 안부 행동의 뜻이 "흐려졌다고 적혀 있을 때만 부탁"이라 '흐려지다'를 넣는다
                request.Situation = situation + " 너는 " + gap + "관리를 받지 못해 "
                        + (resealed ? "흐려지다 못해 다시 봉인되었다. 주인이 닦아 주어야 다시 깨어날 수 있다." : "흐려졌다.");
                context.KeyFact = resealed ? "resealed" : "faded";
                AddClarity(input, request);
                AddLastCare(days, request);
                Candidates(request, NpcActions.AnswerStatus);
                return;
            }
            if (days.HasValue && days.Value <= RecentCareDays)
            {
                // ② 최근 관리: 주인이 해 준 일을 떠올리게 한다
                request.Situation = situation + " " + WhenAt(days.Value) + " 주인이 너를 닦아 관리해 주었다.";
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
                    ? "주인이 오랜만에 관리하기를 골랐고, 주인이 너를 관리해 주었다."
                    : "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
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
            context.MemoryToSave = new MemoryEntry(0, NpcActions.TellStory, summary);

            // 지난번에 들려준 이야기 하나 — "그 이야기는 다시 풀지 않는다" 규칙과 함께 실린다
            MemoryEntry lastStory = null;
            foreach (MemoryEntry memory in Recent(input.Memory))
            {
                if (memory.ActionId == NpcActions.TellStory && !string.IsNullOrEmpty(memory.Summary))
                {
                    lastStory = memory;
                    break;
                }
            }
            if (lastStory != null)
            {
                request.Memory.Add(lastStory);
            }
        }

        private static void Visit(NpcContextInput input, NpcContext context)
        {
            ReactionRequest request = context.Request;
            int visits = Math.Max(1, input.RecentVisitDays.HasValue ? input.RecentVisitDays.Value : 1);
            const string watching = " 손님은 너를 건드릴 수 없고 구경만 한다. 너는 걸어 다니며 짧은 말풍선을 띄운다.";
            if (visits >= RegularVisitorDays)
            {
                request.Situation = "최근 30일 동안 " + visits + "번 온 손님이 전시관을 둘러보고 있다." + watching;
                context.KeyFact = "regular_visit";
                Candidates(request, NpcActions.WanderGreet, NpcActions.WanderRecall);
            }
            else
            {
                request.Situation = "처음 온 손님이 전시관을 둘러보고 있다." + watching;
                context.KeyFact = "first_visit";
                Candidates(request, NpcActions.WanderGreet);
            }
            // 선명도는 방치 상태라 손님에게 보여 주지 않는다
            request.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, visits.ToString(CultureInfo.InvariantCulture)));
            AddAwakening(input, request);
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
