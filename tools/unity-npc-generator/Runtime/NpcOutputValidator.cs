using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jamkkaebi.Npc
{
    /// <summary>출력 검사에서 걸린 이유. 검사하는 순서대로 적었다.</summary>
    public enum OutputProblem
    {
        None,
        NotJson,        // JSON 객체를 찾지 못했거나 읽을 수 없다
        UnknownAction,  // 행동이 후보에 없다
        TargetMismatch, // 대상이 고른 행동의 대상이 아니다
        EmptyLine,      // 대사가 없다
        Newline,        // 대사에 줄바꿈이 있다
        TooLong,        // 첫 문장부터 길이 제한을 넘는다 (또는 토큰 한도에서 끊겼는데 끝난 문장이 없다)
        Forbidden,      // 대사가 공통 금칙어에 걸린다
        Foreign,        // 영문·가나·한자가 섞였다 (영어, 행동 ID 를 그대로 적음)
        Typo,           // 오타 ("쏟었군", "할께", "ㅋㅋ", "만먹었는지")
        MissingAsk,     // 흐려진 안부에 닦아 달라는 부탁이 없다 ("조금 흐려졌어. 마지막 관리는 9일 전이었어.")
        Incomplete,     // 문장이 끝나지 않았다 ("관리해 줘서", 끝의 '히힛!'을 뗀 뒤 이어 주는 말로 끝남)
        SelfName,       // 자기 이름을 말한다 ("반갑게요, 향산이") — 페르소나를 받은 검사기만 본다
        SpeechMismatch, // 그 도깨비의 어미로 끝나지 않는 문장이 있다 (존댓말·반말 섞기) — 페르소나를 받은 검사기만 본다
        TooPlain,       // 인사 행동의 대사가 인사말뿐이다 ("안녕!", "안녕.") — 부호를 뺀 글자가 MinGreetingCore 보다 적다. 페르소나를 받은 검사기만 본다
        InnerRepeat,    // 한 대사 안에서 같은 말을 되풀이한다 ("고마워, 관리해줘서 고마워") — 페르소나를 받은 검사기만 본다
        WrongHistory,   // 기록과 어긋난다 (단골에게 "처음 온 친구", 첫 방문에 "또 왔구나", 기록 없는 주인에게 "오랜만이야")
        SituationCopy,  // 상황 문장을 그대로 옮겼다 ("주인이 너를 탭했다. 화면이 너에게 가까이 다가왔다.")
        PersonaCopy,    // 페르소나 설명문을 옮겼다 ("가장 친한 동료처럼 반긴다") — 페르소나를 받은 검사기만 본다
        WrongTime,      // 지금 시간대와 어긋난다 (낮인데 "좋은 밤", 자료에 시간대가 없는데 "오늘 밤")
        WrongAddress,   // 호칭이 규칙과 다르다 (친밀한데 '발굴자', 친밀하지 않은데 닉네임)
        WrongSubject,   // 주체가 어긋난다 (자기 것을 '내 ~' 없이 구경하듯 말함: "자개가 정말 공들여 만든 거네") — 페르소나를 받은 검사기만 본다
        Repeated        // 그 도깨비가 최근에 띄운 대사(request.RecentLines)와 같다
    }

    /// <summary>모델 출력 한 건의 검사 결과.</summary>
    public sealed class OutputCheck
    {
        internal OutputCheck(OutputProblem problem, string detail, string actionId, string targetId, string line)
                : this(problem, detail, actionId, targetId, line, line, false)
        {
        }

        internal OutputCheck(OutputProblem problem, string detail, string actionId, string targetId, string line,
                             string fullLine, bool truncated)
        {
            Problem = problem;
            Detail = detail;
            ActionId = actionId;
            TargetId = targetId;
            Line = line;
            FullLine = fullLine;
            Truncated = truncated;
        }

        public OutputProblem Problem { get; private set; }

        /// <summary>걸린 이유를 사람이 읽게 적은 것. 통과하면 null.</summary>
        public string Detail { get; private set; }

        public string ActionId { get; private set; }
        public string TargetId { get; private set; }

        /// <summary>띄울 대사. 앞뒤 공백과 따옴표를 걷어 내고, 길면 제한 안에 드는 앞 문장들만 남긴 것.</summary>
        public string Line { get; private set; }

        /// <summary>모델이 낸 대사 전체 (자르기 전). 자르지 않았으면 Line 과 같다.</summary>
        public string FullLine { get; private set; }

        /// <summary>출력 토큰 한도에서 대사 중간에 끊겼는지. 끊겼으면 끝난 문장까지만 Line 에 남는다.</summary>
        public bool Truncated { get; private set; }

        /// <summary>
        /// 검사 전에 어미를 그 도깨비 말투로 고쳤으면 고치기 전 대사 (모델이 쓴 그대로, 자르기 전). 고치지 않았으면 null.
        /// Line · FullLine 은 고친 뒤의 대사다.
        /// </summary>
        public string LineBeforeSpeechFix { get; internal set; }

        /// <summary>대사를 문장 단위로 잘라 냈는지.</summary>
        public bool Trimmed
        {
            get { return IsValid && Line != FullLine; }
        }

        public bool IsValid
        {
            get { return Problem == OutputProblem.None; }
        }

        /// <summary>통과하면 Success(잘라 냈거나 어미를 고쳤으면 Note 에 적는다), 아니면 Failed(InvalidOutput). 출처는 Slm.</summary>
        public ReactionResult ToResult(string requestId)
        {
            if (!IsValid)
            {
                return ReactionResult.Failed(requestId, FailReason.InvalidOutput, ReactionSource.Slm, Problem + ": " + Detail);
            }
            string note = !Trimmed ? null
                    : (Truncated ? "토큰 한도에서 끊긴 대사를 끝난 문장까지만 씀" : "긴 대사를 문장 단위로 잘라 씀")
                      + " (" + FullLine.Length + "자 → " + Line.Length + "자)";
            if (LineBeforeSpeechFix != null)
            {
                // 고친 뒤 코드가 앞 문장·부탁을 붙이거나 바꾸기도 해서(흐려진 안부) 고치기 전 대사만 적으면 최종 대사와 전혀 달라 보였다 (2026-11-18 기기 시험 S03). 최종 대사를 함께 적는다
                string fix = "어미를 말투에 맞게 고침 ('" + LineBeforeSpeechFix + "' → '" + Line + "')";
                note = note == null ? fix : note + "; " + fix;
            }
            return ReactionResult.Success(requestId, ActionId, TargetId, Line, ReactionSource.Slm, note);
        }
    }

    /// <summary>
    /// SLM 출력 검사. 모델이 낸 글을 읽어 아래를 차례로 확인하고, 하나라도 걸리면 화면에 띄우지 않는다.
    /// <list type="number">
    /// <item>JSON: 앞뒤 잡글·생각(&lt;think&gt;)·코드 블록이 있어도 JSON 객체 하나를 찾아 읽는다. 출력 토큰 한도에서
    /// 대사 문자열 안에서 끊겼으면 닫아서 읽고, 끝나지 않은 마지막 조각은 버린다.</item>
    /// <item>후보 포함: 행동이 후보에 있고, 대상이 <b>그 행동의</b> 대상 중 하나다.</item>
    /// <item>말투 다듬기: 페르소나 카탈로그를 받았으면, 아래 검사 전에 흔한 어미를 그 도깨비 말투로 고친다
    /// (NpcLineChecks.NormalizeSpeech — "고마워요" → 반말 "고마워", "모시겠습니다" → 하오체 "모시겠소"). NormalizeSpeech 로 끈다.</item>
    /// <item>대사: 비어 있지 않고, 줄바꿈이 없고, 길이 제한 안이다. 길면 다 버리지 않고 제한 안에 드는 앞 문장들만 남긴다
    /// (작은 모델은 두세 문장을 써서 넘기는 일이 많다). 첫 문장부터 넘치면 TooLong.</item>
    /// <item>금칙어: 띄울 대사(자른 뒤)가 공통 금칙어(NpcLineFilter)에 걸리지 않는다. 상황 설명문·몸짓 설명도 금칙어 묶음이 거른다.</item>
    /// <item>외국어: 영문·가나·한자가 없다 (NpcLineChecks.HasForeign).</item>
    /// <item>오타·끝맺음: 오타(NpcLineChecks.Typo)가 없고 문장이 이어 주는 말·조사로 끝나지 않는다(IncompleteEnding). 끝의 의성어("히힛!")는
    /// 말투 다듬기가 먼저 뗀다.</item>
    /// <item>기록: 손님 장면은 방문 횟수와, 주인 장면은 대화 기억·마지막 관리 사실이 없으면 '오랜만' 같은 말을 하지 않는다
    /// (NpcLineChecks.HistoryContradiction · NoRecordContradiction).</item>
    /// <item>상황 옮기기: 상황 문장의 한 구절을 그대로 옮기지 않는다 (NpcLineChecks.CopiedFromSituation).</item>
    /// <item>자기 이름·어미: 페르소나 카탈로그를 받았으면, 자기 이름을 말하지 않고 문장마다 그 도깨비의 어미(endingHint)로 끝난다
    /// (존댓말·반말 섞기를 막는다, NpcLineChecks.SpeechMatches).</item>
    /// <item>반복: 그 도깨비가 최근에 띄운 대사(request.RecentLines)와 같지 않다 (NpcLineChecks.Similar). 요청 관리자가 채운다.</item>
    /// </list>
    /// 프롬프트 규칙만으로는 작은 모델이 이름·어미·반복을 지키지 않아서(2026-11-11 시험) 검사로 강제한다. 걸리면 SLM 생성기가
    /// 곁들일 거리를 바꿔 다시 생성하고(MaxAttempts), 그래도 걸리면 요청 관리자가 기본 대사로 대신한다.
    /// 길이 제한과 자르는 규칙은 tools/npc-prompt-test/run.py 의 길이OK 판정(fit_sentences)과 같다 (기본 40자, 손님 관람 25자).
    /// 프롬프트에 적는 길이(30·20자)보다 조금 넉넉하다 — 작은 모델은 글자 수를 정확히 세지 못한다.
    /// </summary>
    public sealed class NpcOutputValidator
    {
        /// <summary>인사 행동의 대사는 부호를 뺀 글자가 이 수보다 적으면 인사말뿐이라 거른다. run.py 의 MIN_GREETING_CORE 와 같다.</summary>
        public const int MinGreetingCore = 4;
        private static readonly string[] PlainCheckedActions = { "greet", "wander_greet", "thank_calm", "thank_joyful" };

        /// <summary>MaxChars 에 없는 선택지의 대사 길이 제한.</summary>
        public const int DefaultMaxChars = 40;

        private const string QuoteChars = "\"“”'‘’";

        // 이것 뒤에 공백이나 글 끝이 오면 문장이 끝난다. 점 두 개 이상(...)과 '…'는 말줄임이라 끝이 아니다
        private const string SentenceEnd = ".!?~。";

        // 문장 끝 뒤에 붙는 닫는 따옴표·괄호
        private const string SentenceClose = "\"'”’)]」』";

        private readonly NpcLineFilter filter;
        private readonly PersonaCatalog personas;

        /// <summary>자기 이름·어미 검사 없이 (형식·길이·금칙어·반복만). 게임은 페르소나를 받는 쪽을 쓴다.</summary>
        public NpcOutputValidator(NpcLineFilter filter) : this(filter, null)
        {
        }

        /// <param name="personas">주면 자기 이름과 어미(존댓말·반말 섞기)도 검사한다.</param>
        public NpcOutputValidator(NpcLineFilter filter, PersonaCatalog personas)
        {
            if (filter == null)
            {
                throw new ArgumentNullException("filter");
            }
            this.filter = filter;
            this.personas = personas;
            NormalizeSpeech = true;
            MaxChars = new Dictionary<string, int>
            {
                { NpcInputs.Visit, 25 }
            };
        }

        /// <summary>선택지별 대사 길이 제한.</summary>
        public Dictionary<string, int> MaxChars { get; set; }

        /// <summary>그 선택지의 대사 길이 제한.</summary>
        /// <summary>
        /// 길이 제한에 두는 여유(글자). 실기기 시험(2026-11-12·14 Tab S9·S25 FE 48건 중 12건이 다시 만듦)에서 "26자(제한 25)"처럼 한두 글자 넘었다고 거르고 다시 만들어
        /// 한 번에 10~15초가 더 들었다. 말풍선에 한두 글자 더 들어가는 것은 문제가 아니라 제한에 이만큼 여유를 둔다. run.py 의 LENGTH_TOLERANCE 와 같다.
        /// </summary>
        public const int LengthTolerance = 5;

        public int LimitFor(string input)
        {
            int limit;
            return input != null && MaxChars != null && MaxChars.TryGetValue(input, out limit) ? limit : DefaultMaxChars;
        }

        /// <summary>
        /// 검사 전에 흔한 어미를 그 도깨비 말투로 고치는지 (NpcLineChecks.NormalizeSpeech, 기본 켬). 페르소나를 받은 검사기만 고친다.
        /// 끄면 모델이 쓴 그대로 검사한다 (run.py --no-normalize).
        /// </summary>
        public bool NormalizeSpeech { get; set; }

        public OutputCheck Check(string raw, ReactionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            string said;
            OutputCheck check = Inspect(raw, request, out said);
            check.LineBeforeSpeechFix = said;
            return check;
        }

        // said: 어미를 고쳤으면 고치기 전 대사, 아니면 null
        private OutputCheck Inspect(string raw, ReactionRequest request, out string said)
        {
            said = null;
            Dictionary<string, object> json;
            string jsonError;
            bool truncated;
            if (!TryReadObject(raw, out json, out jsonError, out truncated))
            {
                return Fail(OutputProblem.NotJson, jsonError, null, null, null);
            }

            string action = Text(json, "action").Trim();
            string target = Text(json, "target").Trim();
            // 행동·대상이 하나뿐인 요청은 모델이 그 칸을 적지 않는다(PromptAssembler 의 출력 모양). 비어 있으면 후보로 채운다 (run.py 의 evaluate 와 같다)
            if (action.Length == 0 && request.Candidates != null && request.Candidates.Count == 1)
            {
                action = request.Candidates[0].ActionId;
            }
            if (target.Length == 0 && request.Candidates != null)
            {
                List<string> onlyTargets = new List<string>();
                foreach (ActionCandidate c in request.Candidates)
                {
                    if (c != null && c.ActionId == action && c.TargetIds != null)
                    {
                        foreach (string t in c.TargetIds)
                        {
                            if (!onlyTargets.Contains(t))
                            {
                                onlyTargets.Add(t);
                            }
                        }
                    }
                }
                if (onlyTargets.Count == 1)
                {
                    target = onlyTargets[0];
                }
                else if (onlyTargets.Count == 0 && HasCandidateWithoutTargets(request, action))
                {
                    target = NpcTargets.None; // 대상이 없는 행동(인사 등)은 'none' — 다른 후보에 대상이 있어도 모델이 target 칸을 비우면 이것으로 채운다
                }
            }
            string line = CleanLine(Text(json, "line"));
            string rawLine = line; // 말투 다듬기 전 (탓하는 말을 '네가'를 지우기 전에도 본다)
            Persona persona = personas != null ? personas.Find(request.RelicId) : null;
            // 주인 장면: 대화 기억도 마지막 관리 사실도 없는데 '오랜만'이라고 하면 지어낸 말이다
            bool hasRecord = (request.Memory != null && request.Memory.Count > 0)
                    || request.FindFact(NpcFactKeys.LastCareDaysAgo) != null
                    || request.FindFact(NpcFactKeys.VisitDays) != null;
            if (NormalizeSpeech && persona != null)
            {
                string fixedLine = NpcLineChecks.NormalizeSpeech(line, persona.endingHint != null ? persona.endingHint.label : null, persona.name,
                        persona.ownThings, request.Nickname, request.FindFact(NpcFactKeys.VisitDays), request.AddressName,
                        NpcLineChecks.NoRecordApplies(request.Input == NpcInputs.Visit, hasRecord, request.Situation));
                // 상황에 맞는 한 문장으로 줄인다 (지난 대화 꺼내기는 기억의 내용이 뒤 문장에 있을 수 있어 건드리지 않는다)
                if (action != NpcActions.RecallTalk)
                {
                    fixedLine = NpcLineChecks.KeepOneSentence(fixedLine, NpcLineChecks.NeedsAsk(request));
                    if (NpcLineChecks.NeedsAsk(request) || NpcLineChecks.NoRecordApplies(request.Input == NpcInputs.Visit, hasRecord, request.Situation))
                    {
                        fixedLine = NpcLineChecks.FixTodayAlso(fixedLine);
                    }
                    if (action == NpcActions.Greet || action == NpcActions.WanderGreet || action == NpcActions.WanderRecall)
                    {
                        fixedLine = NpcLineChecks.FixGreetingTail(fixedLine);
                    }
                }
                if (action == NpcActions.ThankCalm || action == NpcActions.ThankJoyful || action == NpcActions.AnswerStatus || action == NpcActions.RecallTalk)
                {
                    fixedLine = NpcLineChecks.FixCraftSpectator(fixedLine);
                }
                if (fixedLine != line)
                {
                    said = line;
                    line = fixedLine;
                }
            }

            // 흐려진 안부: 코드가 정한 사실 문장(이미 한 말) + 모델의 부탁 문장. 한 문장 줄이기는 모델이 쓴 부분에만 했다
            string linePrefix = PromptAssembler.LinePrefix(request);
            if (linePrefix != null && line.Length > 0)
            {
                // 모델의 부분에 부탁이 없거나 사실이 둘이면 거르고 다시 만들지 않고 코드가 부탁 문장을 채운다 (2026-11-17 22:07 시험)
                if (!NpcLineChecks.IsCleanAsk(line))
                {
                    line = PromptAssembler.AskFallback(request);
                }
                line = linePrefix + " " + line;
            }
            ActionCandidate candidate = null;
            if (request.Candidates != null)
            {
                foreach (ActionCandidate c in request.Candidates)
                {
                    if (c != null && c.ActionId == action)
                    {
                        candidate = c;
                        break;
                    }
                }
            }
            if (candidate == null)
            {
                return Fail(OutputProblem.UnknownAction, "후보에 없는 행동 '" + action + "'", action, target, line);
            }
            List<string> targets = candidate.TargetIds != null && candidate.TargetIds.Count > 0
                    ? candidate.TargetIds
                    : new List<string> { NpcTargets.None };
            if (!targets.Contains(target))
            {
                return Fail(OutputProblem.TargetMismatch,
                        "'" + action + "' 의 대상이 아닌 '" + target + "' (허용: " + string.Join(", ", targets.ToArray()) + ")",
                        action, target, line);
            }

            if (line.Length == 0)
            {
                return Fail(OutputProblem.EmptyLine, "대사가 비어 있다", action, target, line);
            }
            if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
            {
                return Fail(OutputProblem.Newline, "대사에 줄바꿈이 있다", action, target, line);
            }
            int limit = LimitFor(request.Input) + LengthTolerance;
            string fitted = FitSentences(line, limit, truncated);
            if (fitted == null)
            {
                string why = truncated
                        ? "토큰 한도에서 끊겼고 " + limit + "자 안에 끝난 문장이 없다 (" + line.Length + "자까지 나옴)"
                        : "첫 문장부터 제한을 넘는다 (" + line.Length + "자, 제한 " + limit + "자)";
                return new OutputCheck(OutputProblem.TooLong, why, action, target, line, line, truncated);
            }
            string discovered = NpcLineChecks.DiscoveryEnding(fitted, action);
            if (discovered != null)
            {
                return new OutputCheck(OutputProblem.WrongSubject, "자기 이야기에 발견 어미(~구나) '" + discovered + "'", action, target, fitted, line, truncated);
            }
            string probe = NpcLineChecks.GreetingProbe(fitted, action);
            if (probe != null)
            {
                return new OutputCheck(OutputProblem.WrongHistory, "인사에 지어낸 사정·캐묻기 '" + probe + "' ← '" + fitted + "'", action, target, fitted, line, truncated);
            }
            ForbiddenMatch bad = filter.Find(fitted);
            if (bad == null && NormalizeSpeech)
            {
                // 말투 다듬기가 '네가'를 지우면 "네가 관리를 안 해줘서"의 탓하는 말이 문장으로 안 보인다 — 다듬기 전 대사로도 '지나친 퉁명스러움·무시' 묶음(cold)을 본다
                foreach (ForbiddenMatch rawHit in filter.FindAll(rawLine))
                {
                    if (rawHit.GroupId == "cold")
                    {
                        bad = rawHit;
                        break;
                    }
                }
            }
            if (bad != null)
            {
                return new OutputCheck(OutputProblem.Forbidden, "금칙어 " + bad, action, target, fitted, line, truncated);
            }
            if (NpcLineChecks.HasForeign(fitted))
            {
                return new OutputCheck(OutputProblem.Foreign, "영문·가나·한자가 섞임 '" + fitted + "'", action, target, fitted, line, truncated);
            }
            if (NpcLineChecks.NeedsAsk(request) && !NpcLineChecks.HasAsk(fitted))
            {
                return new OutputCheck(OutputProblem.MissingAsk, "닦아 달라는 부탁이 없다 '" + fitted + "'", action, target, fitted, line, truncated);
            }
            if (NpcLineChecks.NeedsAsk(request) && NpcLineChecks.MixedAskFacts(fitted))
            {
                return new OutputCheck(OutputProblem.WrongHistory, "상태와 며칠 전을 함께 말함 '" + fitted + "'", action, target, fitted, line, truncated);
            }
            string typo = NpcLineChecks.Typo(fitted);
            if (typo != null)
            {
                return new OutputCheck(OutputProblem.Typo, "오타 '" + typo + "'", action, target, fitted, line, truncated);
            }
            string dangling = NpcLineChecks.IncompleteEnding(fitted, request.AddressName);
            if (dangling != null)
            {
                return new OutputCheck(OutputProblem.Incomplete, "문장이 끝나지 않음 '" + dangling + "'", action, target, fitted, line, truncated);
            }
            string conflict = RecordConflict(fitted, request);
            if (conflict != null)
            {
                return new OutputCheck(OutputProblem.WrongHistory, "기록과 어긋남 '" + conflict + "'", action, target, fitted, line, truncated);
            }
            string copied = NpcLineChecks.CopiedFromSituation(fitted, request.Situation, request.AddressName);
            if (copied != null)
            {
                return new OutputCheck(OutputProblem.SituationCopy, "상황 문장을 옮김 '" + copied + "'", action, target, fitted, line, truncated);
            }
            string timeWord = NpcLineChecks.TimeContradiction(fitted, request.FindFact(NpcFactKeys.TimeOfDay));
            if (timeWord != null)
            {
                return new OutputCheck(OutputProblem.WrongTime, "지금 시간대와 어긋남 '" + timeWord + "' (지금 시간대: "
                        + (request.FindFact(NpcFactKeys.TimeOfDay) ?? "알 수 없음") + ")", action, target, fitted, line, truncated);
            }
            string addressWord = NpcLineChecks.AddressContradiction(fitted, request.Nickname, request.Familiar);
            if (addressWord != null)
            {
                return new OutputCheck(OutputProblem.WrongAddress, "호칭이 규칙과 다름 '" + addressWord + "' (부를 이름: " + request.AddressName + ")",
                        action, target, fitted, line, truncated);
            }
            string reversedCall = NpcLineChecks.ReversedCall(fitted, request.AddressName);
            if (reversedCall != null)
            {
                return new OutputCheck(OutputProblem.WrongSubject, "호칭 주체가 뒤바뀜 '" + reversedCall + "'", action, target, fitted, line, truncated);
            }
            string thirdPerson = NpcLineChecks.PlayerThirdPerson(fitted, request.AddressName);
            if (thirdPerson != null)
            {
                return new OutputCheck(OutputProblem.WrongSubject, "플레이어를 남 얘기하듯 부름 '" + thirdPerson + "'", action, target, fitted, line, truncated);
            }
            if (persona != null)
            {
                string personaCopy = NpcLineChecks.CopiedFromPersona(fitted, persona);
                if (personaCopy != null)
                {
                    return new OutputCheck(OutputProblem.PersonaCopy, "페르소나 설명문을 옮김 '" + personaCopy + "'", action, target, fitted, line, truncated);
                }
                if (NpcLineChecks.ContainsSelfName(fitted, persona.name))
                {
                    return new OutputCheck(OutputProblem.SelfName, "자기 이름 '" + persona.name + "'", action, target, fitted, line, truncated);
                }
                string subjectThing = NpcLineChecks.SubjectContradiction(fitted, persona.ownThings);
                if (subjectThing != null)
                {
                    return new OutputCheck(OutputProblem.WrongSubject, "주체가 어긋남 '" + subjectThing + "' (자기 것은 '내 " + subjectThing + "'처럼 말한다)",
                            action, target, fitted, line, truncated);
                }
                string wrong;
                if (!NpcLineChecks.SpeechMatches(fitted, persona.endingHint, out wrong))
                {
                    return new OutputCheck(OutputProblem.SpeechMismatch,
                            (persona.endingHint.label ?? "말투") + " 어미가 아닌 문장 '" + wrong + "'", action, target, fitted, line, truncated);
                }
                if (Array.IndexOf(PlainCheckedActions, action) >= 0 && NpcLineChecks.Core(fitted).Length < MinGreetingCore)
                {
                    return new OutputCheck(OutputProblem.TooPlain, "인사말뿐 '" + fitted + "'", action, target, fitted, line, truncated);
                }
            }
            string repeatedWord = persona != null ? NpcLineChecks.InnerRepeat(fitted) : null;
            if (repeatedWord != null)
            {
                return new OutputCheck(OutputProblem.InnerRepeat, "한 대사 안의 반복 '" + repeatedWord + "'", action, target, fitted, line, truncated);
            }
            // 흐려진 안부: 코드가 정한 앞 문장("마지막 관리가 9일 전이었어.")은 요청마다 같을 수 있어, 앞 문장을 뗀 부탁 부분끼리 견준다 (앞 문장만 같아 거르던 것을 막는다)
            string compareLine = fitted;
            IEnumerable<string> compareRecent = request.RecentLines;
            if (linePrefix != null)
            {
                List<string> prefixes = PromptAssembler.AskPrefixes(request);
                compareLine = PromptAssembler.StripAskPrefix(fitted, prefixes);
                var stripped = new List<string>();
                if (request.RecentLines != null)
                {
                    foreach (string recent in request.RecentLines)
                    {
                        stripped.Add(PromptAssembler.StripAskPrefix(recent, prefixes));
                    }
                }
                compareRecent = stripped;
            }
            KeyValuePair<string, string>? same = NpcLineChecks.FindSimilar(compareLine, compareRecent);
            if (same.HasValue)
            {
                return new OutputCheck(OutputProblem.Repeated, "최근 대사 '" + same.Value.Key + "' 와 같음 (" + same.Value.Value + ")",
                        action, target, fitted, line, truncated);
            }
            return new OutputCheck(OutputProblem.None, null, action, target, fitted, line, truncated);
        }

        private static bool HasCandidateWithoutTargets(ReactionRequest request, string action)
        {
            if (request.Candidates != null)
            {
                foreach (ActionCandidate c in request.Candidates)
                {
                    if (c != null && c.ActionId == action && (c.TargetIds == null || c.TargetIds.Count == 0))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 대사가 방문·마지막 관리·기억 같은 실제 기록과 어긋나는지 (손님: 방문 횟수·최근 방문, 주인: 기록 없는 '오랜만', 며칠 전 낱말). 어긋나는 말이 없으면 null.
        /// 출력 검사와, 미리 만들어 둔 칸 있는 대사를 꺼낼 때(NpcLineCache.TryTakeTemplate)가 같은 기준을 쓴다.
        /// </summary>
        public static string RecordConflict(string line, ReactionRequest request)
        {
            bool hasRecord = (request.Memory != null && request.Memory.Count > 0)
                    || request.FindFact(NpcFactKeys.LastCareDaysAgo) != null
                    || request.FindFact(NpcFactKeys.VisitDays) != null;
            string conflict;
            if (request.Input == NpcInputs.Visit)
            {
                int visits;
                conflict = NpcLineChecks.HistoryContradiction(line,
                        int.TryParse(request.FindFact(NpcFactKeys.VisitCount30d), out visits) ? visits : (int?)null);
                if (conflict == null)
                {
                    // 3일 전에도 온 친구에게 "오랜만이야"
                    conflict = NpcLineChecks.LongAbsenceContradiction(line, request.FindFact(NpcFactKeys.VisitDays));
                }
            }
            else
            {
                conflict = hasRecord ? null : NpcLineChecks.NoRecordContradiction(line, request.Situation);
            }
            if (conflict == null)
            {
                // "이틀 전"·"어제"·"5일 전"은 자료(마지막 관리·방문한 날·기억·상황 문장)의 실제 기록에 있는 날일 때만 말한다
                conflict = NpcLineChecks.DayRefContradiction(line, RecordedDays(request));
            }
            return conflict;
        }

        // 자료에 적힌 실제 날수: 마지막 관리, 방문한 날, 대화 기억, 상황 문장에 나온 "N일 전"
        private static HashSet<int> RecordedDays(ReactionRequest request)
        {
            var days = new HashSet<int>();
            int last;
            if (int.TryParse(request.FindFact(NpcFactKeys.LastCareDaysAgo), out last))
            {
                days.Add(last);
            }
            foreach (KeyValuePair<string, int> reference in NpcLineChecks.DayRefs(request.FindFact(NpcFactKeys.VisitDays), true))
            {
                days.Add(reference.Value);
            }
            foreach (KeyValuePair<string, int> reference in NpcLineChecks.DayRefs(request.Situation, true))
            {
                days.Add(reference.Value);
            }
            if (request.Memory != null)
            {
                foreach (MemoryEntry memory in request.Memory)
                {
                    if (memory != null)
                    {
                        days.Add(memory.DaysAgo);
                    }
                }
            }
            return days;
        }

        /// <summary>
        /// 제한 안에 드는 앞 문장들만 남긴다. 첫 문장부터 넘치면 null. 끊긴 대사(truncated)는 끝나지 않은 마지막 조각을 버린다.
        /// run.py 의 fit_sentences 와 같다.
        /// </summary>
        public static string FitSentences(string line, int limit, bool truncated)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }
            List<int> ends = SentenceEnds(line);
            if (!truncated && (ends.Count == 0 || ends[ends.Count - 1] < line.TrimEnd().Length))
            {
                ends.Add(line.Length); // 문장 부호 없이 끝난 마지막 문장
            }
            string fitted = null;
            foreach (int end in ends)
            {
                string piece = line.Substring(0, end).TrimEnd();
                if (piece.Length > limit)
                {
                    break;
                }
                if (piece.Length > 0)
                {
                    fitted = piece;
                }
            }
            return fitted ?? FitClause(line, limit);
        }

        /// <summary>쉼표로 자른 앞 마디의 최소 글자 수 (부호·띄어쓰기를 뺀 글자).</summary>
        public const int ClauseMinChars = 5;

        private const string FinalEndings = "어아야워여요죠네군지까다래자";

        /// <summary>
        /// 첫 문장이 길이 제한을 넘을 때, 제한 안에 드는 가장 긴 앞 마디(쉼표 앞)가 끝난 말이면 그것만 쓴다. 없으면 null (TooLong).
        /// 끝난 말은 끝 글자가 어미(어·아·야·워·여·요·죠·네·군·지·까·다·래·자)이고 ClauseMinChars 글자 이상인 것이다.
        /// 다시 만드는 데 1.5~2초가 더 드는 것을 아끼려는 것이다. run.py 의 fit_clause 와 같다.
        /// </summary>
        private static string FitClause(string line, int limit)
        {
            for (int i = line.Length - 1; i > 0; i--)
            {
                if (line[i] != ',' && line[i] != '，')
                {
                    continue;
                }
                string piece = line.Substring(0, i).TrimEnd();
                if (piece.Length == 0 || piece.Length > limit)
                {
                    continue;
                }
                int core = 0;
                foreach (char c in piece)
                {
                    core += char.IsLetterOrDigit(c) ? 1 : 0;
                }
                int quotes = 0;
                foreach (char c in piece)
                {
                    quotes += QuoteChars.IndexOf(c) >= 0 ? 1 : 0;
                }
                if (quotes % 2 == 1)
                {
                    continue; // 따옴표가 열린 채 끊긴 마디는 지문("인사하며 '오늘은 …")이다
                }
                if (core >= ClauseMinChars && FinalEndings.IndexOf(piece[piece.Length - 1]) >= 0)
                {
                    return piece;
                }
            }
            return null;
        }

        // 문장이 끝나는 자리(끝 글자 다음 위치)들
        private static List<int> SentenceEnds(string line)
        {
            var ends = new List<int>();
            int i = 0;
            while (i < line.Length)
            {
                if (SentenceEnd.IndexOf(line[i]) < 0)
                {
                    i++;
                    continue;
                }
                int j = i;
                bool onlyDots = true;
                while (j < line.Length && SentenceEnd.IndexOf(line[j]) >= 0)
                {
                    onlyDots &= line[j] == '.';
                    j++;
                }
                bool ellipsis = j - i >= 2 && onlyDots;
                while (j < line.Length && SentenceClose.IndexOf(line[j]) >= 0)
                {
                    j++;
                }
                if (!ellipsis && (j == line.Length || char.IsWhiteSpace(line[j])))
                {
                    ends.Add(j);
                }
                i = j;
            }
            return ends;
        }

        private static OutputCheck Fail(OutputProblem problem, string detail, string action, string target, string line)
        {
            return new OutputCheck(problem, detail, action, target, line);
        }

        // run.py 의 clean_line 과 같다: 앞뒤 공백 → 따옴표 → 공백
        private static string CleanLine(string value)
        {
            return (value ?? "").Trim().Trim(QuoteChars.ToCharArray()).Trim();
        }

        private static string Text(Dictionary<string, object> json, string key)
        {
            object value;
            if (!json.TryGetValue(key, out value) || value == null)
            {
                return "";
            }
            string text = value as string;
            return text ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 생각 블록을 지우고, 처음 '{' 부터 마지막 '}' 까지를 JSON 객체로 읽는다. 읽지 못했는데 글이 문자열 안에서 끝났으면
        /// (출력 토큰 한도에서 끊김) 문자열과 객체를 닫아 다시 읽고 truncated 를 세운다. run.py 의 parse_json 과 같다.
        /// </summary>
        private static bool TryReadObject(string raw, out Dictionary<string, object> json, out string error, out bool truncated)
        {
            json = null;
            truncated = false;
            string text = StripThink(raw ?? "");
            int start = text.IndexOf('{');
            if (start < 0)
            {
                error = "JSON 객체가 없다";
                return false;
            }
            int end = text.LastIndexOf('}');
            error = "JSON 객체가 없다";
            if (end > start)
            {
                try
                {
                    json = MiniJson.ParseObject(text.Substring(start, end - start + 1));
                    error = null;
                    return true;
                }
                catch (FormatException e)
                {
                    error = "JSON 을 읽지 못했다: " + e.Message;
                }
            }
            string tail = text.Substring(start);
            if (!EndsInString(tail))
            {
                // 대사 문자열을 닫은 뒤 토큰 한도에서 끊겨 객체의 '}' 만 빠진 경우 ("…고마워요!\"" 로 끝남, 2026-11-14 시험 S08 #4)
                try
                {
                    json = MiniJson.ParseObject(tail.TrimEnd() + "}");
                    error = null;
                    return true;
                }
                catch (FormatException)
                {
                    return false;
                }
            }
            try
            {
                json = MiniJson.ParseObject(tail.TrimEnd('\\') + "\"}");
                error = null;
                truncated = true;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // 글 끝이 JSON 문자열 안인지 (닫히지 않은 따옴표가 있는지)
        private static bool EndsInString(string text)
        {
            bool inside = false;
            bool escaped = false;
            foreach (char c in text)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = inside;
                }
                else if (c == '"')
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        // 생각 모드를 끄지 못한 모델이 <think>…</think> 를 앞에 붙이는 경우. 닫히지 않았으면 끝까지 지운다
        private static string StripThink(string text)
        {
            while (true)
            {
                int open = text.IndexOf("<think>", StringComparison.Ordinal);
                if (open < 0)
                {
                    return text;
                }
                int close = text.IndexOf("</think>", open, StringComparison.Ordinal);
                text = close < 0 ? text.Substring(0, open) : text.Remove(open, close + "</think>".Length - open);
            }
        }
    }

    /// <summary>
    /// 모델 출력만 읽는 작은 JSON 파서. Unity 의 JsonUtility 는 필드가 정해진 클래스로만 읽고 잘못된 JSON 을 알려 주지 않아서 따로 둔다.
    /// 문자열 안의 날것 줄바꿈은 받아 두고(대사 검사에서 줄바꿈으로 거른다), 그 밖에는 표준 JSON 을 따른다.
    /// </summary>
    internal sealed class MiniJson
    {
        private readonly string s;
        private int i;

        private MiniJson(string text)
        {
            s = text;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var parser = new MiniJson(text);
            parser.SkipSpace();
            Dictionary<string, object> result = parser.ReadObject();
            parser.SkipSpace();
            if (parser.i != parser.s.Length)
            {
                throw new FormatException(parser.i + "번째 글자 뒤에 남은 글이 있다");
            }
            return result;
        }

        private object ReadValue()
        {
            SkipSpace();
            if (i >= s.Length)
            {
                throw new FormatException("값이 없다");
            }
            char c = s[i];
            switch (c)
            {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return ReadString();
                case 't':
                    ReadWord("true");
                    return true;
                case 'f':
                    ReadWord("false");
                    return false;
                case 'n':
                    ReadWord("null");
                    return null;
                default:
                    return ReadNumber();
            }
        }

        private Dictionary<string, object> ReadObject()
        {
            Expect('{');
            var result = new Dictionary<string, object>();
            SkipSpace();
            if (Peek() == '}')
            {
                i++;
                return result;
            }
            while (true)
            {
                SkipSpace();
                string key = ReadString();
                SkipSpace();
                Expect(':');
                result[key] = ReadValue();
                SkipSpace();
                if (Peek() == ',')
                {
                    i++;
                    continue;
                }
                Expect('}');
                return result;
            }
        }

        private List<object> ReadArray()
        {
            Expect('[');
            var result = new List<object>();
            SkipSpace();
            if (Peek() == ']')
            {
                i++;
                return result;
            }
            while (true)
            {
                result.Add(ReadValue());
                SkipSpace();
                if (Peek() == ',')
                {
                    i++;
                    continue;
                }
                Expect(']');
                return result;
            }
        }

        private string ReadString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (i >= s.Length)
                {
                    break;
                }
                char e = s[i++];
                switch (e)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > s.Length
                                || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            throw new FormatException("잘못된 \\u 이스케이프");
                        }
                        builder.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new FormatException("잘못된 이스케이프 \\" + e);
                }
            }
            throw new FormatException("문자열이 닫히지 않았다");
        }

        private double ReadNumber()
        {
            int start = i;
            while (i < s.Length && "+-.0123456789eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }
            double value;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new FormatException(start + "번째 글자에 알 수 없는 값");
            }
            return value;
        }

        private void ReadWord(string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw new FormatException(i + "번째 글자에 알 수 없는 값");
            }
            i += word.Length;
        }

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw new FormatException(i + "번째 글자에 '" + c + "' 가 와야 한다");
            }
            i++;
        }

        private char Peek()
        {
            return i < s.Length ? s[i] : '\0';
        }

        private void SkipSpace()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }
    }
}
