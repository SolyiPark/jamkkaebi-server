using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 대사 내용 검사 (출력 검사기가 쓴다). tools/npc-prompt-test/run.py 의 같은 이름 함수와 같은 규칙이다.
    /// <list type="bullet">
    /// <item>SpeechMatches: 문장마다 그 도깨비의 어미(endingHint)로 끝나는지 — 존댓말·반말 섞기를 막는다.</item>
    /// <item>Similar: 최근 대사와 같은 대사인지 — 같은 상황마다 같은 말을 하면 지루하고 정이 들지 않는다.</item>
    /// </list>
    /// </summary>
    public static class NpcLineChecks
    {
        /// <summary>
        /// 글자 쌍(이웃한 두 글자) 겹침이 이만큼 이상이면 같은 대사다 (2026-11-13: 80% → 90%). 80% 에서는 "정성껏 관리해 줘서 고마워."와 "관리해 줘서 고마워."(82%)까지
        /// 반복으로 보고 두 번 다 거른 뒤 기본 대사로 넘어가는 일이 많았다. 90% 아래면 어순만 조금 바뀐 정도라 다른 대사로 본다.
        /// </summary>
        public const double SimilarPairs = 0.9;

        /// <summary>두 대사가 함께 가진 가장 긴 구절이 더 긴 대사의 이만큼이면 같은 대사다. 짧은 쪽이 긴 쪽에 들어 있다고 같다고 보지 않는다 — "정성껏 관리해 줘서 고마워."와 "관리해 줘서 고마워."는 다른 대사다(2026-11-13: 90% 기준).</summary>
        public const double ContainedShare = 0.9;

        /// <summary>그 겹침이 적어도 이 글자 수는 되어야 한다 ("고마워" 처럼 짧은 낱말 하나로는 같다고 보지 않는다).</summary>
        public const int ContainedMin = 5;

        /// <summary>앞 OpeningChars 글자가 이만큼 겹치면 같은 대사다 — "주인님, 관리해 줘서 고마워요!"로 늘 시작하는 것을 막는다.</summary>
        public const double SimilarOpening = 0.9;

        /// <summary>앞부분으로 비교하는 글자 수 (띄어쓰기·문장 부호 빼고). 첫 문장으로 비교하면 "주인님~"의 '~'에서 끊겨 놓쳤다.</summary>
        public const int OpeningChars = 10;

        /// <summary>앞부분을 비교하려면 두 대사 모두 적어도 이 글자 수는 되어야 한다 ("고마워." 같은 짧은 말은 보지 않는다).</summary>
        public const int OpeningMin = 6;

        // 감탄사만 있는 문장·마디("어흠.", "허허,", "히힛!")는 어미를 보지 않는다. 짧은 문장을 다 건너뛰면 해요체 도깨비의
        // "고마워! 다시 오세요."의 "고마워!"(3글자)까지 건너뛰어서, 알려진 감탄사만 건너뛴다. run.py 의 INTERJECTION_RE 와 같다
        // 홀로 쓴 대답 "네!", "예." 도 어미를 보지 않는다 (구름결 "네! 천천히 보세요."). 짧은 문장을 다 건너뛰지 않고 이 둘만 더한다
        private static readonly Regex Acknowledgment = new Regex("^(네|예)$", RegexOptions.CultureInvariant);
        private static readonly Regex Interjection = new Regex("^(어흠|에헴|흠|허허|어허|허|히힛|히히|히잉|헤헤|킁킁|앗싸|앗|꺄|꺅|오예|야호|와|오|어|음|아)+$", RegexOptions.CultureInvariant);

        private const string SentenceEnd = ".!?~。";
        private const string SentenceClose = "\"'”’)]」』";
        private static readonly Regex Punct = new Regex("[\\s.!?…~,·\"'“”‘’]", RegexOptions.CultureInvariant);
        private static readonly Regex Trailing = new Regex("[\\s.!?…~,·\"'“”‘’)\\]]+$", RegexOptions.CultureInvariant);
        private static readonly Regex Vocative = new Regex("[\\s,]*(주인님|주인|손님)$", RegexOptions.CultureInvariant);

        // ---------- 말투 다듬기 ----------

        // 낱말 끝 (문장 부호·공백·글 끝 앞)
        private const string WordEnd = "(?=[\\s.!?…~,·\"'”’)\\]]|$)";

        // 말투마다 먼저 바꾸는 낱말 (흔히 틀리는 것). 앞에서부터 차례로 바꾼다 (긴 것을 먼저 둔다). run.py 의 SPEECH_WORDS 와 같다
        private static readonly string[][] BanmalWords =
        {
            new[] { "안녕하세요", "안녕" }, new[] { "안녕하십니까", "안녕" }, // "하세요"→"해"보다 먼저
            new[] { "보내세요", "보내" }, new[] { "쉬세요", "쉬어" }, new[] { "드세요", "먹어" }, new[] { "앉으세요", "앉아" }, new[] { "즐기세요", "즐겨" },
            new[] { "둘러보세요", "둘러봐" }, new[] { "지요", "지" },
            new[] { "고맙습니다", "고마워" }, new[] { "감사합니다", "고마워" }, new[] { "감사해요", "고마워" }, new[] { "주셔서", "줘서" },
            new[] { "이에요", "이야" }, new[] { "에요", "야" }, new[] { "예요", "야" },
            new[] { "주세요", "줘" }, new[] { "하세요", "해" }, new[] { "보세요", "봐" }, new[] { "오세요", "와" }, new[] { "가세요", "가" },
            new[] { "계세요", "있어" }, new[] { "마세요", "마" }, new[] { "죠", "지" },
            // 격식 어미를 반말로 (run.py 의 SPEECH_WORDS["반말"] 과 같다)
            new[] { "반갑습니다", "반가워" }, new[] { "겠습니다", "겠어" },
            new[] { "있습니다", "있어" }, new[] { "없습니다", "없어" }, new[] { "않습니다", "않아" }, 
            new[] { "합니다", "해" }, new[] { "됩니다", "돼" }, new[] { "갑니다", "가" }, new[] { "옵니다", "와" },
            new[] { "았습니다", "았어" }, new[] { "었습니다", "었어" }, new[] { "였습니다", "였어" }, new[] { "왔습니다", "왔어" }, new[] { "갔습니다", "갔어" },
            new[] { "봤습니다", "봤어" }, new[] { "줬습니다", "줬어" }, new[] { "했습니다", "했어" }, new[] { "됐습니다", "됐어" }, new[] { "썼습니다", "썼어" },
            new[] { "냈습니다", "냈어" }
        };
        private static readonly string[][] HaoWords =
        {
            new[] { "안녕하세요", "안녕하시오" }, new[] { "안녕하십니까", "안녕하시오" }, new[] { "안녕하옵니다", "안녕하시오" },
            new[] { "안녕하옵니까", "안녕하시오" },
            new[] { "감사합니다", "고맙소" }, new[] { "감사해요", "고맙소" }, new[] { "고마워요", "고맙소" }, new[] { "반가워요", "반갑소" },
            new[] { "좋아요", "좋소" }, new[] { "있어요", "있소" }, new[] { "없어요", "없소" }, new[] { "같아요", "같소" },
            new[] { "네요", "구려" }, new[] { "군요", "구려" }, new[] { "해요", "하오" }, new[] { "옵니다", "오" }, new[] { "옵니까", "오" },
            new[] { "세요", "시오" }
        };
        private static readonly string[][] HageWords =
        {
            new[] { "처음 뵙겠습니다", "처음 보는 얼굴이구먼" }, new[] { "안녕하세요", "안녕하신가" }, new[] { "안녕하십니까", "안녕하신가" },
            new[] { "고마워요", "고맙네" }, new[] { "감사해요", "고맙네" }, new[] { "반가워요", "반갑네" }, new[] { "좋아요", "좋네" },
            new[] { "주셔서", "줘서" },
            new[] { "네요", "네" }, new[] { "군요", "구먼" }, new[] { "나요", "나" }, new[] { "세요", "시게" }
        };

        // 반말에서 '요'를 떼면 안 되는 낱말
        private static readonly HashSet<string> YoNouns = new HashSet<string> { "필요", "중요", "요요", "주요", "수요" };

        private static readonly Regex BanmalPronoun = new Regex("(^|\\s)(저는|저를|제가|저도|저랑|저한테|제)" + WordEnd, RegexOptions.CultureInvariant);

        // 반말 도깨비가 "주인아,"라고 부르면 거만하게 들린다(공통 금칙어 '낮춰 부르기'). 부르는 말만 높여 고친다
        // 주인·손님 호칭과 그 도깨비 자기 이름(어간)은 말하지 않는다. 걸러 내 기본 대사로 바꾸는 대신 검사 전에 지운다. run.py 의 strip_address 와 같다
        private const string AddressParticles = "(?:께서|께|이입니다|입니다|이에요|이예요|이라고|이라는|이랑|한테|에게|이야|이|가|은|는|을|를|의|도|랑|와|과|아|야)?";
        private static readonly Dictionary<string, Regex> AddressByName = new Dictionary<string, Regex>();

        private static readonly Regex Segment = new Regex("[^,.!?…~]+[,.!?…~]*" + "\\s*", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> RepeatExempt = new HashSet<string> { "나는", "내가", "나도", "나랑", "너는", "네가", "이건", "그건", "저기", "여기", "거기" };
        private static readonly Regex HangulWord = new Regex("[가-힣]+", RegexOptions.CultureInvariant);

        /// <summary>
        /// 한 대사 안에서 같은 말을 되풀이하지 않는다 ("고마워, 관리해줘서 고마워" → "관리해줘서 고마워"). 짧은 토막(여섯 글자 이하)이 다른 토막
        /// 안에 그대로 있으면 그 토막을 지운다. run.py 의 drop_repeated_lead 와 같다.
        /// </summary>
        public static string DropRepeatedLead(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            MatchCollection matches = Segment.Matches(line);
            if (matches.Count < 2)
            {
                return line;
            }
            var segments = new List<string>();
            var cores = new List<string>();
            foreach (Match m in matches)
            {
                segments.Add(m.Value);
                cores.Add(Core(m.Value));
            }
            var kept = new System.Text.StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                string core = cores[i];
                bool repeated = false;
                if (core.Length >= 2 && core.Length <= 6)
                {
                    for (int j = 0; j < segments.Count && !repeated; j++)
                    {
                        repeated = j != i && cores[j].Contains(core) && (cores[j].Length > core.Length || j > i);
                    }
                }
                if (!repeated)
                {
                    kept.Append(segments[i]);
                }
            }
            string result = kept.ToString().Trim();
            if (result.Length == 0)
            {
                return line;
            }
            return result.EndsWith(",") ? result.Substring(0, result.Length - 1) + "." : result;
        }

        /// <summary>한 대사 안에서 두 번 나온 낱말(세 글자 이상은 앞 두 글자가 같으면 같은 말로 본다). 없으면 null. run.py 의 inner_repeat 와 같다.</summary>
        public static string InnerRepeat(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }
            var seen = new HashSet<string>();
            foreach (Match m in HangulWord.Matches(line))
            {
                string word = m.Value;
                if (word.Length < 2 || RepeatExempt.Contains(word))
                {
                    continue;
                }
                string key = word.Length == 2 ? word : word.Substring(0, 2);
                if (!seen.Add(key))
                {
                    return word;
                }
            }
            return null;
        }

        private static readonly Regex Guest = new Regex("손님(?:(이라서|이라고|이라는|이야|이다|께서|께|이랑|이|은|을|과|아)(?![가-힣]))?", RegexOptions.CultureInvariant);

        private static string GuestParticle(string particle)
        {
            switch (particle)
            {
                case "이라서": return "라서";
                case "이라고": return "라고";
                case "이라는": return "라는";
                case "이야": return "야";
                case "이다": return "다";
                case "께서": return "가";
                case "께": return "한테";
                case "이랑": return "랑";
                case "이": return "가";
                case "은": return "는";
                case "을": return "를";
                case "과": return "와";
                case "아": return "야";
                default: return "";
            }
        }

        /// <summary>'손님'이 대사에 남으면 출력 직전에 '친구'로 바꾼다 (손님께서→친구가, 손님께→친구한테). run.py 의 guest_to_friend 와 같다.</summary>
        public static string GuestToFriend(string line)
        {
            if (string.IsNullOrEmpty(line) || line.IndexOf("손님", StringComparison.Ordinal) < 0)
            {
                return line;
            }
            return Guest.Replace(line, m => "친구" + GuestParticle(m.Groups[1].Value));
        }

        private static readonly string[] RegularOnlyWords = { "처음" };
        // 처음·기록 없음과 어긋나는 낱말 ("오랜만"·"오래간만"은 기록이 없는 주인 장면에도 걸린다)
        private static readonly string[] FirstOnlyWords =
            { "또왔", "다시왔", "자주왔", "오랜만", "오래간만", "단골", "다시만나", "또만나", "또보네", "다시보네", "또보는", "또봐서", "또뵈" };

        /// <summary>
        /// 손님 방문 횟수와 어긋나는 낱말. 단골(2번 이상)이면 '처음', 첫 방문이면 '또·다시·오랜만·단골'. 없으면 null.
        /// 7번 온 단골에게 "처음 온 친구라서 반가워"라고 했다(2026-11-12 11:45 시험). run.py 의 history_contradiction 과 같다.
        /// </summary>
        public static string HistoryContradiction(string line, int? visits)
        {
            if (!visits.HasValue || string.IsNullOrEmpty(line))
            {
                return null;
            }
            string core = Core(line);
            foreach (string word in visits.Value >= RuleBasedReactionGenerator.RegularVisitorDays ? RegularOnlyWords : FirstOnlyWords)
            {
                if (core.Contains(word))
                {
                    return word;
                }
            }
            return null;
        }

        /// <summary>
        /// 기록이 없는데 있는 척하는 낱말. 주인 장면에서 대화 기억도, 마지막 관리 사실도 없으면 '오랜만'·'또 왔'·'다시 만나' 같은 말은
        /// 지어낸 것이다 (2026-11-13 실기기: 말발굽 탭 인사 S01 에서 "오랜만이야"를 두 번). 상황 문장에 그 낱말이 있으면(예: "주인이 오랜만에
        /// 관리하기를 골랐다") 근거가 있어 걸지 않는다. 손님 장면은 방문 횟수로 보는 HistoryContradiction 이 맡는다.
        /// 없으면 null. run.py 의 no_record_contradiction 과 같다.
        /// </summary>
        public static string NoRecordContradiction(string line, string situation)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }
            string core = Core(line);
            string basis = Core(situation);
            foreach (string word in FirstOnlyWords)
            {
                if (core.Contains(word) && !basis.Contains(word))
                {
                    return word;
                }
            }
            return null;
        }

        /// <summary>
        /// 대사가 상황 문장을 그대로 옮겼으면 겹친 구절, 아니면 null. 실기기에서 "주인이 너를 탭했다. 화면이 너에게 가까이 다가왔다."처럼
        /// 상황 설명문이 대사에 섞여 나왔다. 띄어쓰기·문장 부호를 빼고 SituationCopyMin 글자 넘게 같은 구절이 있으면 옮긴 것이다
        /// (낱말 몇 개를 쓰는 것은 괜찮다). run.py 의 situation_copy 와 같다.
        /// </summary>
        public static string CopiedFromSituation(string line, string situation)
        {
            string a = Core(line);
            string b = Core(situation);
            if (a.Length < SituationCopyMin || b.Length < SituationCopyMin)
            {
                return null;
            }
            string shared = LongestShared(a, b);
            return shared.Length >= SituationCopyMin ? shared : null;
        }

        /// <summary>상황 문장과 이만큼(글자) 넘게 같은 구절이 이어지면 옮긴 것이다. 안부의 기억 요약("암행어사 이야기를")처럼 꼭 쓸 낱말은 이보다 짧다.</summary>
        public const int SituationCopyMin = 10;

        private static readonly Regex Foreign = new Regex("[A-Za-z\u3040-\u30FF\u4E00-\u9FFF]", RegexOptions.CultureInvariant);

        /// <summary>영문·일본 가나·한자가 섞였는지. 한국어 대사만 써야 하고, 행동 ID(greet 등)를 그대로 적는 일도 이것으로 거른다. run.py 의 FOREIGN_RE 와 같다.</summary>
        public static bool HasForeign(string line)
        {
            return !string.IsNullOrEmpty(line) && Foreign.IsMatch(line);
        }

        // ---------- 오타 · 끝맺음 · 끝의 의성어 (2026-11-13 시험 반영) ----------

        private static bool IsSyllable(char c)
        {
            return c >= 0xAC00 && c <= 0xD7A3;
        }

        // 가운뎃소리 번호 (ㅏ=0, ㅗ=8) / 받침 번호 (없음=0, ㄹ=8, ㅆ=20)
        private static int Medial(char c)
        {
            return ((c - 0xAC00) / 28) % 21;
        }

        private static int Final(char c)
        {
            return (c - 0xAC00) % 28;
        }

        // 앞 글자의 모음이 ㅏ·ㅗ 면 '았', 아니면 '었' (쏟→쏟았, 먹→먹었)
        private static bool TakesA(char c)
        {
            return IsSyllable(c) && (Medial(c) == 0 || Medial(c) == 8);
        }

        private static readonly Regex LoneJamo = new Regex("[ㄱ-ㅎㅏ-ㅣ]", RegexOptions.CultureInvariant);
        private static readonly string[] DoubleParticles = { "을를", "를을", "를를", "을을", "은는", "는은", "에에서", "에서에서" };

        // 자주 틀리는 철자 (바른 말: 됐·며칠·어떡해·왠지·금세·오랜만·바람·웬만·어이없). '만먹'은 '만드셨'의 높임을 잘못 고쳐 생긴 것이다
        private static readonly string[] Misspelled = { "됬", "몇일", "어떻해", "웬지", "금새", "오랫만", "바램", "왠만", "어의없", "설겆이", "않되", "만먹", "엇어", "엇다", "엇군", "엇네" };

        /// <summary>
        /// 대사의 오타. 있으면 그 글, 없으면 null. 아래를 본다 (run.py 의 typo 와 같다).
        /// ① 자음·모음만 남은 글(ㅋㅋ) ② 모음 어긋남: 'ㅏ·ㅗ' 뒤에 '었'(쏟었군→쏟았군), 그 밖 모음 뒤에 '았'(먹았 아닌 먹았?)
        /// ③ 'ㄹ께·ㄹ꺼·ㄹ껄'(할께→할게) ④ 겹조사(을를·은는) ⑤ 자주 틀리는 철자 목록.
        /// 2026-11-13 시험: "정성을 쏟으셨군요"의 높임을 뺀 자리에 "쏟었군", "만드셨는지"가 "만먹었는지"로 나왔다.
        /// </summary>
        public static string Typo(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }
            Match jamo = LoneJamo.Match(line);
            if (jamo.Success)
            {
                return "자음·모음만 남음 '" + jamo.Value + "'";
            }
            for (int i = 1; i < line.Length; i++)
            {
                char c = line[i];
                char p = line[i - 1];
                if (!IsSyllable(p))
                {
                    continue;
                }
                if (c == '었' && Final(p) != 20 && TakesA(p))
                {
                    return p + "었";
                }
                if (c == '았' && Final(p) != 20 && !TakesA(p))
                {
                    return p + "았";
                }
                if ((c == '께' || c == '꺼' || c == '껄') && Final(p) == 8)
                {
                    return p.ToString() + c;
                }
            }
            // 띄어쓰기를 지우지 않고 본다 ("줄기는 은이나"의 '는 은'은 겹조사가 아니다)
            foreach (string pair in DoubleParticles)
            {
                if (line.Contains(pair))
                {
                    return pair;
                }
            }
            foreach (string word in Misspelled)
            {
                if (line.Contains(word))
                {
                    return word;
                }
            }
            return null;
        }

        // "깨워줘서 고마워"·"닦아 줘서 고마워"는 부탁이 아니라 감사다 (2026-11-14 시험 3 S12 #2·#3: 깨워 달라는 자리에서 감사만 함)
        // 흐려지거나 재봉인된 안부에서는 닦아 달라는 부탁을 꼭 한다 ("조금 흐려졌어. 마지막 관리는 9일 전이었어."로 끝나면 부탁이 빠진 것, 2026-11-14 시험 S03 #3)
        private static readonly Regex AskWords = new Regex("닦(?!아 ?(줘서|준))|깨워(?! ?(줘서|준))|깨우|부탁|도와|손길|봐 ?줘|살펴|돌봐|씻|문질|관리해 ?(줘|줄|주|달)|관리 ?좀", RegexOptions.CultureInvariant);

        /// <summary>대사에 부탁(닦아·관리해 줘·깨워·부탁·도와…, '마지막 관리는'은 부탁이 아니다)이 들어 있는지. run.py 의 has_ask 와 같다.</summary>
        public static bool HasAsk(string line)
        {
            return !string.IsNullOrEmpty(line) && AskWords.IsMatch(line);
        }

        /// <summary>안부 답하기에서 선명도가 흐려짐·재봉인이면 부탁이 있어야 한다. run.py 의 needs_ask 와 같다.</summary>
        public static bool NeedsAsk(ReactionRequest request)
        {
            string clarity = request.FindFact(NpcFactKeys.Clarity);
            return request.Candidates != null && request.Candidates.Exists(c => c != null && c.ActionId == NpcActions.AnswerStatus)
                    && (clarity == "흐려짐" || clarity == "재봉인");
        }

        private static readonly Regex StarterComma = new Regex("^(덕분에|이제|훨씬|한결|역시|요즘|조금|점점|몸이|기분이),\\s*", RegexOptions.CultureInvariant);

        private static readonly Regex DanglingConnective = new Regex("(서|며|면서|려고|도록|다가|지만|으나|니까|면)$", RegexOptions.CultureInvariant);
        private static readonly Regex DanglingParticle = new Regex("(은|는|을|를|의)$", RegexOptions.CultureInvariant);
        private static readonly Regex QuotativeEnding = new Regex("(다고|라고|냐고|자고|다니까|라니까|자니까|냐니까)$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> CompleteWords = new HashSet<string>
        {
            "여기서", "거기서", "저기서", "어디서", "어서", "그리고", "그러니까", "그러면", "그럼", "아니면", "이러면", "그래서"
        };

        /// <summary>
        /// 마지막 문장이 끝맺지 못했으면 그 낱말, 아니면 null. 이어 주는 말(~서·~며·~니까·~면…)이나 조사(~은·~는·~을·~를·~의)로 끝난
        /// 대사다 ("꼬리까지 깨끗하게 관리해 줘서"). 끝의 '히힛!' 같은 의성어를 뗀 뒤에 보므로 그 앞이 끊긴 말이면 거른다.
        /// 인용("~다고")과 "여기서"·"어서" 같은 낱말은 끝맺은 것으로 본다. run.py 의 incomplete_ending 과 같다.
        /// </summary>
        public static string IncompleteEnding(string line)
        {
            List<string> sentences = Sentences(line);
            if (sentences.Count == 0)
            {
                return null;
            }
            string ending = Ending(sentences[sentences.Count - 1]);
            string word = ending.Substring(ending.LastIndexOf(' ') + 1);
            if (word.Length < 2 || CompleteWords.Contains(word) || QuotativeEnding.IsMatch(word))
            {
                return null;
            }
            if (DanglingConnective.IsMatch(word) || DanglingParticle.IsMatch(word))
            {
                return word;
            }
            // 문장 부호 없이 '기분이'·'내가'로 끝나면 뒤가 잘린 것이다 (끝의 "히히!"를 떼고 남은 말, 2026-11-13 시험: "…예뻐 보여서 기분이")
            bool terminated = Regex.IsMatch(sentences[sentences.Count - 1].TrimEnd(), "[.!?…~。]$");
            return !terminated && Regex.IsMatch(word, "(이|가|도|에|로|와|과|랑)$") ? word : null;
        }

        private static readonly Regex TrailingInterjection = new Regex(
                "(?:[\\s,]*(?:히힛|히히|히잉|헤헤|킁킁|어흠|허허|에헴|호호|앗싸|야호|오예)[!?.~…]*)+\\s*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// 대사 끝의 의성어·헛기침("…줘서 히힛!", "고마워! 어흠.")을 뗀다. 문장으로 끝내야지 '히힛!' 같은 말로 끝내지 않는다.
        /// 앞에 말이 없는 대사("히힛.")나 앞에 오는 의성어("히힛! 놀자")는 그대로 둔다. 뗀 뒤 끝에 문장 부호가 없으면 마침표를 붙인다.
        /// run.py 의 strip_trailing_interjection 과 같다.
        /// </summary>
        public static string StripTrailingInterjection(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            Match m = TrailingInterjection.Match(line);
            if (!m.Success)
            {
                return line;
            }
            string rest = line.Substring(0, m.Index).TrimEnd(' ', ',');
            if (!Regex.IsMatch(rest, "[가-힣]"))
            {
                return line;
            }
            if (Regex.IsMatch(rest, "[.!?…~]$"))
            {
                return rest;
            }
            // 조사로 끝나면("기분이 히히!"→"기분이") 뒤가 잘린 말이라 마침표를 붙이지 않는다 — IncompleteEnding 이 거른다
            string lastWord = rest.Substring(rest.LastIndexOf(' ') + 1);
            return lastWord.Length >= 2 && Regex.IsMatch(lastWord, "(이|가|도|에|로|와|과|랑|을|를|은|는)$") ? rest : rest + ".";
        }

        /// <summary>
        /// 방문 기록에 최근 방문(오늘 말고)이 7일 안에 있으면 "오랜만"·"오래간만"은 어긋난다. 3일 전에도 온 친구에게 "오랜만이야"라고 했다
        /// (2026-11-13 시험 S06). visitDays 는 visit_days 사실("오늘, 3일 전, 9일 전"). 없으면 null. run.py 의 long_absence_contradiction 과 같다.
        /// </summary>
        public static string LongAbsenceContradiction(string line, string visitDays)
        {
            int gap = int.MaxValue;
            foreach (KeyValuePair<string, int> reference in DayRefs(visitDays, true))
            {
                if (reference.Value > 0 && reference.Value < gap)
                {
                    gap = reference.Value;
                }
            }
            if (gap >= 7)
            {
                return null;
            }
            string core = Core(line);
            foreach (string word in new[] { "오랜만", "오래간만", "오랫만" })
            {
                if (core.Contains(word))
                {
                    return word;
                }
            }
            return null;
        }

        // ---------- 실제 기록: 며칠 전 · 시간대 · 호칭 · 페르소나 설명문 ----------

        private static readonly Regex DayRef = new Regex(
                "(?<![0-9])([0-9]+)\\s?일\\s?전|어제|그저께|그제|엊그제|이틀\\s?전|사흘\\s?전|나흘\\s?전|닷새\\s?전|엿새\\s?전|이레\\s?전|일주일\\s?전|열흘\\s?전|오늘",
                RegexOptions.CultureInvariant);

        private static int DaysOf(Match m)
        {
            if (m.Groups[1].Success)
            {
                int n;
                return int.TryParse(m.Groups[1].Value, out n) ? n : -1;
            }
            string t = Regex.Replace(m.Value, "\\s", "");
            switch (t)
            {
                case "오늘": return 0;
                case "어제": return 1;
                case "그저께": case "그제": case "엊그제": case "이틀전": return 2;
                case "사흘전": return 3;
                case "나흘전": return 4;
                case "닷새전": return 5;
                case "엿새전": return 6;
                case "이레전": case "일주일전": return 7;
                case "열흘전": return 10;
                default: return -1;
            }
        }

        /// <summary>
        /// 글에 나온 "며칠 전" 표현의 날수 (오늘=0, 어제=1, 이틀 전=2, N일 전=N). 표현 글과 함께 돌려준다.
        /// includeToday 가 false 면 '오늘'은 세지 않는다 ("오늘 밤"은 날짜를 말한 것이 아니다). run.py 의 day_refs 와 같다.
        /// </summary>
        public static List<KeyValuePair<string, int>> DayRefs(string text, bool includeToday)
        {
            var found = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(text))
            {
                return found;
            }
            foreach (Match m in DayRef.Matches(text))
            {
                int days = DaysOf(m);
                if (days >= 0 && (includeToday || days > 0))
                {
                    found.Add(new KeyValuePair<string, int>(m.Value, days));
                }
            }
            return found;
        }

        /// <summary>
        /// 대사의 "어제"·"이틀 전"·"5일 전"이 자료의 실제 기록(allowed: 마지막 관리·방문한 날·대화 기억·상황 문장의 날수)에 없으면 그 표현, 아니면 null.
        /// 기록에 없는 날을 말하면 지어낸 것이다. run.py 의 day_ref_contradiction 과 같다.
        /// </summary>
        public static string DayRefContradiction(string line, ICollection<int> allowed)
        {
            foreach (KeyValuePair<string, int> reference in DayRefs(line, false))
            {
                if (allowed == null || !allowed.Contains(reference.Value))
                {
                    return reference.Key;
                }
            }
            return null;
        }

        // 시간대 낱말: 앞이 한글이면 낱말의 일부("지난밤", "한낮")라 뺀다. 뒤는 '이야·에·도·까지·처럼'이나 문장 부호·끝일 때만 ('낮잠', '밤바다', '낮은'은 아님)
        private static readonly Regex TimeWord = new Regex(
                "(?<![가-힣])(새벽|아침|저녁|낮|밤)(?=이야|이네|이라|이니|이지|이었|이구나|이다|에|엔|도|까지|부터|처럼|[\\s.!?…~,]|$)",
                RegexOptions.CultureInvariant);

        // 지금 시간대마다 말해도 되는 시간대 낱말 (경계는 너그럽게: 새벽에 '밤', 밤에 '저녁')
        private static readonly Dictionary<string, string[]> AllowedTimeWords = new Dictionary<string, string[]>
        {
            { "새벽", new[] { "새벽", "밤" } }, { "아침", new[] { "아침", "새벽" } }, { "오전", new[] { "아침", "낮" } },
            { "낮", new[] { "낮" } }, { "저녁", new[] { "저녁" } }, { "밤", new[] { "밤", "저녁" } }
        };

        /// <summary>
        /// 지금 시간대와 어긋나는 시간대 낱말. 낮인데 "좋은 밤", 자료에 시간대가 없는데(탭 인사가 아님) "오늘 밤". 없으면 null.
        /// timeOfDay 는 request 의 time_of_day 사실 (없으면 null). 날씨는 서버가 넘기지 않으므로 금칙어(weather)가 따로 막는다.
        /// run.py 의 time_contradiction 과 같다.
        /// </summary>
        public static string TimeContradiction(string line, string timeOfDay)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }
            string[] allowed;
            if (timeOfDay == null || !AllowedTimeWords.TryGetValue(timeOfDay.Trim(), out allowed))
            {
                allowed = new string[0];
            }
            foreach (Match m in TimeWord.Matches(line))
            {
                if (Array.IndexOf(allowed, m.Groups[1].Value) < 0)
                {
                    return m.Groups[1].Value;
                }
            }
            return null;
        }

        /// <summary>
        /// 호칭이 규칙과 다르면 그 말, 아니면 null. 친밀하면(30일 방문 3번 이상) 닉네임으로 부르고 '발굴자'는 쓰지 않으며,
        /// 아니면 '발굴자'로 부르고 닉네임은 쓰지 않는다. 둘 다 꼭 들어갈 필요는 없다. run.py 의 address_contradiction 과 같다.
        /// </summary>
        public static string AddressContradiction(string line, string nickname, bool familiar)
        {
            string core = Core(line);
            if (core.Length == 0)
            {
                return null;
            }
            bool useNickname = familiar && !string.IsNullOrEmpty(nickname);
            if (useNickname && core.Contains(NpcAddress.Stranger))
            {
                return NpcAddress.Stranger;
            }
            string nick = Core(nickname);
            if (!useNickname && nick.Length > 0 && core.Contains(nick))
            {
                return nickname;
            }
            return null;
        }

        /// <summary>대사가 이만큼(글자) 넘게 페르소나 설명문과 같은 구절을 이으면 설명문을 옮긴 것이다 ("가장 친한 동료처럼 반긴다").</summary>
        public const int PersonaCopyMin = 8;

        /// <summary>
        /// 대사가 페르소나의 설명 칸(성격·상대를 대할 때·관리를 받으면·가치관·감정 표현·말투)을 옮겼으면 겹친 구절, 아니면 null.
        /// 프롬프트에 실린 설명을 작은 모델이 대사로 읊는다 (2026-11-13: "가장 친한 동료처럼 반긴다"). 예시 대사를 8글자 넘게 그대로 옮긴 것도 거른다
        /// ("좋은 향은 천천히 퍼지는 게 제일 예쁘지?").
        /// run.py 의 persona_copy 와 같다.
        /// </summary>
        public static string CopiedFromPersona(string line, Persona persona)
        {
            if (persona == null)
            {
                return null;
            }
            string core = Core(line);
            var fields = new List<string> { persona.toOwner, persona.toVisitor, persona.onCare, persona.values, persona.emotionStyle, persona.traits, persona.speechStyle };
            if (persona.examples != null)
            {
                fields.AddRange(persona.examples);
            }
            foreach (string field in fields)
            {
                string other = Core(field);
                if (core.Length < PersonaCopyMin || other.Length < PersonaCopyMin)
                {
                    continue;
                }
                string shared = LongestShared(core, other);
                if (shared.Length >= PersonaCopyMin)
                {
                    return shared;
                }
            }
            return null;
        }

        private static readonly Dictionary<string, Regex> OwnPossessiveByThings = new Dictionary<string, Regex>();

        /// <summary>
        /// 자기 것(ownThings: 자개·옻칠·몸…)을 '네'라고 부르면 '내'로 고친다 ("네 자개는 반짝여" → "내 자개는 반짝여", "너의"→"나의").
        /// 주체를 바꾸지 않는다 — 밤바다가 자기 자개를 "네 자개"라고 했다(2026-11-13). 상대의 것("네 덕분에")은 고치지 않는다.
        /// run.py 의 fix_own_possessive 와 같다.
        /// </summary>
        public static string FixOwnPossessive(string line, IList<string> ownThings)
        {
            if (string.IsNullOrEmpty(line) || ownThings == null || ownThings.Count == 0)
            {
                return line;
            }
            string key = string.Join("|", new List<string>(ownThings).ToArray());
            Regex rx;
            lock (OwnPossessiveByThings)
            {
                if (!OwnPossessiveByThings.TryGetValue(key, out rx))
                {
                    var things = new List<string>();
                    foreach (string thing in ownThings)
                    {
                        if (!string.IsNullOrEmpty(thing))
                        {
                            things.Add(Regex.Escape(thing));
                        }
                    }
                    rx = new Regex("(?<![가-힣])(네|니|너의)(\\s?)(" + string.Join("|", things.ToArray()) + ")", RegexOptions.CultureInvariant);
                    OwnPossessiveByThings[key] = rx;
                }
            }
            return rx.Replace(line, m => (m.Groups[1].Value == "너의" ? "나의" : "내") + m.Groups[2].Value + m.Groups[3].Value);
        }

        // 호칭에 '님'을 붙이지 않는다: 도깨비는 모두 반말이다. 닉네임("도윤님"→"도윤", "도윤님께"→"도윤한테")과 '발굴자'·'친구'("발굴자님", "친구님")에 붙은 '님'을 뗀다
        // (닉네임이 없어도 '발굴자님'은 뗀다). 2026-11-14: "안녕, 도윤님.", 거르던 "발굴자님"을 거르지 않고 고쳐서 호칭이 대사에 나오게 한다
        public static string StripNim(string line, string nickname)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            if (!string.IsNullOrEmpty(nickname))
            {
                line = line.Replace(nickname + "님께", nickname + "한테").Replace(nickname + "님", nickname);
            }
            return line.Replace("발굴자님께", "발굴자한테").Replace("발굴자님", "발굴자").Replace("친구님께", "친구한테").Replace("친구님", "친구");
        }

        /// <summary>
        /// 최근 방문이 7일 안에 있는데(방문 기록) "오랜만에 와 줘서"라고 한 말을 걸러 내 다시 만드는 대신 "또 와 줘서"로 고친다 — 단골 손님에게 "오랜만"이라고 해서
        /// 거의 모든 시도가 걸리고 기본 대사로 넘어갔다(2026-11-14 시험 S13: 8번 중 6번). 방문 기록이 없거나 7일 넘게 비었으면 그대로 둔다.
        /// run.py 의 fix_long_absence 와 같다.
        /// </summary>
        public static string FixLongAbsence(string line, string visitDays)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(visitDays) || line.IndexOf("오", StringComparison.Ordinal) < 0)
            {
                return line;
            }
            int gap = int.MaxValue;
            foreach (KeyValuePair<string, int> reference in DayRefs(visitDays, true))
            {
                if (reference.Value > 0 && reference.Value < gap)
                {
                    gap = reference.Value;
                }
            }
            if (gap >= 7)
            {
                return line;
            }
            line = Regex.Replace(line, "오(?:랜|래간)만이(?:야|네|다|에요)", "또 왔네", RegexOptions.CultureInvariant);
            line = Regex.Replace(line, "오(?:랜|래간|랜 |래간 )만에", "또", RegexOptions.CultureInvariant);
            return line.Replace("또 또", "또");
        }

        /// <summary>자기 이름의 어간. 끝의 '이'를 뗀다(향산이→향산, 묘작이→묘작). 이름이 두 글자면 그대로 둔다.</summary>
        public static string NameStem(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length > 2 && name.EndsWith("이") ? name.Substring(0, name.Length - 1) : name;
        }

        /// <summary>대사가 자기 이름이나 그 어간을 말하는지 (띄어쓰기·부호는 빼고 본다).</summary>
        public static bool ContainsSelfName(string line, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            string stem = Core(NameStem(name));
            return stem.Length > 0 && Core(line).Contains(stem);
        }

        private static Regex AddressFor(string name)
        {
            string key = name ?? "";
            Regex rx;
            lock (AddressByName)
            {
                if (!AddressByName.TryGetValue(key, out rx))
                {
                    string words = "주인님|주인";
                    if (key.Length > 0)
                    {
                        string stem = NameStem(key);
                        // 모델이 이름을 "향 산이"처럼 띄어 쓰기도 해서 글자 사이의 띄어쓰기를 받는다
                        var chars = new List<string>();
                        foreach (char ch in stem)
                        {
                            chars.Add(Regex.Escape(ch.ToString()));
                        }
                        words += "|" + string.Join("\\s?", chars.ToArray()) + (stem != key ? "(?:이)?" : "");
                    }
                    rx = new Regex("([\\s,]*)(?<![가-힣])(" + words + ")(" + AddressParticles + ")(?=[\\s,.!?…~]|$)", RegexOptions.CultureInvariant);
                    AddressByName[key] = rx;
                }
            }
            return rx;
        }

        // 반말 도깨비가 '닦아 주시면', '오셨어'처럼 -시- 높임을 섞으면 어색하다. 높임을 뺀다. run.py 의 HONORIFIC_WORDS 와 같다
        private static readonly string[][] HonorificWords = BuildHonorificWords();

        private static string[][] BuildHonorificWords()
        {
            var list = new List<string[]>();
            // '인사드려요'·'부탁드려요'의 '드리다'는 '주다'가 아니라 '하다'로 바꾼다
            string[] nouns = { "부탁", "인사", "감사", "사과", "축하", "말씀", "약속" };
            string[][] humble = { new[] { "드립니다", "해" }, new[] { "드릴게", "할게" }, new[] { "드릴까", "할까" }, new[] { "드릴래", "할래" }, new[] { "드려", "해" } };
            foreach (string n in nouns)
            {
                foreach (string[] h in humble)
                {
                    list.Add(new[] { n + h[0], n + h[1] });
                }
            }
            string[][] rest =
            {
                new[] { "계시면", "있으면" }, new[] { "계시는", "있는" }, new[] { "계신", "있는" }, new[] { "계셔서", "있어서" }, new[] { "계셔", "있어" }, new[] { "계셨", "있었" },
                new[] { "들이셨", "들였" }, new[] { "하셨", "했" }, new[] { "오셨", "왔" }, new[] { "가셨", "갔" }, new[] { "보셨", "봤" }, new[] { "주셨", "줬" },
                new[] { "주셔", "줘" }, new[] { "하셔", "해" }, new[] { "오셔", "와" }, new[] { "가셔", "가" }, new[] { "보셔", "봐" }, new[] { "안녕하십네다", "안녕" },
                new[] { "되셨", "됐" }, new[] { "쓰셨", "썼" }, new[] { "이셨", "였" }, new[] { "나셨", "났" }, new[] { "사셨", "샀" }, new[] { "타셨", "탔" },
                new[] { "자셨", "잤" }, new[] { "서셨", "섰" }, new[] { "내셨", "냈" }, new[] { "드셨", "먹었" },
                new[] { "가실", "갈" }, new[] { "오실", "올" }, new[] { "하실", "할" }, new[] { "보실", "볼" }, new[] { "주실", "줄" }, new[] { "되실", "될" },
                new[] { "쓰실", "쓸" }, new[] { "드실", "먹을" }, new[] { "이실", "일" },
                new[] { "들이신", "들인" }, new[] { "하신", "한" }, new[] { "오신", "온" }, new[] { "가신", "간" }, new[] { "보신", "본" }, new[] { "주신", "준" },
                new[] { "되신", "된" }, new[] { "쓰신", "쓴" }, new[] { "이신", "인" }, new[] { "드신", "먹은" }, new[] { "으신", "은" },
                new[] { "드릴게", "줄게" }, new[] { "드릴까", "줄까" }, new[] { "드려", "줘" }
            };
            list.AddRange(rest);
            return list.ToArray();
        }

        private static readonly Regex HonorificSi = new Regex("(?<=[가-힣])(?<![마도다])시(?=면|며|고|는|던|지|네|겠|니|거|길|려|도록|어서|나|잖|더라)", RegexOptions.CultureInvariant);
        // '닦아 주면 좋겠어'는 부탁을 돌려 말해 어색하다. 바로 '닦아 줘'로 말한다
        private static readonly Regex AskRequest = new Regex("(?<=[아어여해])( ?)주면 좋겠(?:어요|어|다)(?=[\\s.!?…~,]|$)", RegexOptions.CultureInvariant);

        // 문맥에 어색한 말을 고친다 ("닦아달라고 부탁해" → "닦아 줘", "어서 왔어" → "어서 와"). run.py 의 fix_awkward 와 같다. 못 고친 것은 금칙어 awkward 묶음이 거른다
        private static readonly Regex QuotedAsk = new Regex(
                "([가-힣]*[아어여해])\\s?달라고\\s?(?:부탁|요청|말)(?:드리고\\s?싶(?:어요|어|습니다)|하고\\s?싶(?:어요|어)|드립니다|드려요|드릴게요|드릴게|드려|해요|했어요|했어|했다|했네|합니다|할게요|할게|하다|해)?", RegexOptions.CultureInvariant);
        private static readonly Regex Arrive = new Regex("어서\\s?왔(?:어요|어|네요|네|구나|지)", RegexOptions.CultureInvariant);

        private static string FixAwkward(string line)
        {
            line = QuotedAsk.Replace(line, m => m.Groups[1].Value + " 줘");
            return Arrive.Replace(line, "어서 와");
        }

        private static readonly Regex Imnida = new Regex("([가-힣])입니다" + WordEnd, RegexOptions.CultureInvariant);

        /// <summary>'~입니다'를 반말로: 받침 없는 낱말 뒤는 '야'(친구입니다→친구야), 받침 있는 낱말 뒤는 '이야'(책입니다→책이야). run.py 의 imnida_to_banmal 과 같다.</summary>
        private static string ImnidaToBanmal(string line)
        {
            return Imnida.Replace(line, m => m.Groups[1].Value + ((m.Groups[1].Value[0] - 0xAC00) % 28 != 0 ? "이야" : "야"));
        }

        // '만드셨'·'만드신'은 '만들었'·'만든'이다. '드셨→먹었'이 먼저 바꾸면 "만먹었는지"가 된다 (2026-11-13 시험: 밤바다 S14)
        private static readonly Regex Make = new Regex("만드(셨|셔|신|실)", RegexOptions.CultureInvariant);

        // '으셨'은 앞 글자 모음에 따라 '았'(쏟으셨→쏟았, 받으셨→받았) 또는 '었'(먹으셨→먹었). 늘 '었'으로 바꾸면 "쏟었군"이 된다 (2026-11-13 시험: 밤바다 S14)
        private static readonly Regex EunSyeot = new Regex("([가-힣])으셨", RegexOptions.CultureInvariant);

        // '드셨·드신·드실'(먹다)은 앞이 한글이 아닐 때만 ('만드셨'·'들이드셨'을 건드리지 않는다)
        private static readonly HashSet<string> EatHonorifics = new HashSet<string> { "드셨", "드신", "드실" };

        private static string DropHonorific(string line)
        {
            line = Make.Replace(line, m => m.Groups[1].Value == "셨" ? "만들었" : m.Groups[1].Value == "셔" ? "만들어" : m.Groups[1].Value == "신" ? "만든" : "만들");
            line = EunSyeot.Replace(line, m => m.Groups[1].Value + (TakesA(m.Groups[1].Value[0]) ? "았" : "었"));
            foreach (string[] pair in HonorificWords)
            {
                if (EatHonorifics.Contains(pair[0]))
                {
                    line = Regex.Replace(line, "(?<![가-힣])" + Regex.Escape(pair[0]) + (pair[0].EndsWith("신") ? "(?=[\\s.!?…~,]|$)" : ""), pair[1], RegexOptions.CultureInvariant);
                }
                else if (pair[0].EndsWith("신"))
                {
                    // '-신'(주신·하신)은 낱말 끝일 때만 — '안녕하신가'·'주신다'의 '신'을 건드리지 않는다
                    line = Regex.Replace(line, Regex.Escape(pair[0]) + "(?=[\\s.!?…~,]|$)", pair[1], RegexOptions.CultureInvariant);
                }
                else
                {
                    line = line.Replace(pair[0], pair[1]);
                }
            }
            line = HonorificSi.Replace(line, "");
            return AskRequest.Replace(line, "$1줘");
        }
        private static readonly Regex LeadingJunk = new Regex("^[\\s,]+", RegexOptions.CultureInvariant);
        private static readonly Regex CommaBeforeEnd = new Regex(",\\s*(?=[.!?…~])", RegexOptions.CultureInvariant);
        private static readonly Regex CommaAfterEnd = new Regex("(?<=[.!?…~]),\\s*", RegexOptions.CultureInvariant);
        private static readonly Regex MultiSpace = new Regex("\\s{2,}", RegexOptions.CultureInvariant);
        private static readonly Regex WordYo = new Regex("([가-힣]+)요" + WordEnd, RegexOptions.CultureInvariant);
        private static readonly Regex WordFormal = new Regex("([가-힣]*?)([가-힣])(니다|니까)" + WordEnd, RegexOptions.CultureInvariant);


        /// <summary>
        /// 작은 모델이 흔히 틀리는 어미를 그 도깨비의 말투로 고친다 (endingHint.label: 반말 · 하오체 · 하게체). 고칠 것이 없으면 그대로다.
        /// run.py 의 normalize_speech 와 같다 (Verify 가 같은 예문으로 비교한다).
        /// 2026-11-11 20:02 시험(Kanana)에서 거른 이유 63건 중 48건이 어미였다. 말투 규칙·다짐을 바꿔 가며 다섯 번 돌려도 말발굽(하오체)은
        /// 한 번도, 향산이(하게체)는 12번 중 0~2번만 첫 시도에 어미를 지켰다. 뜻은 맞는데 끝만 틀린 대사("관리해 줘서 고마워요!",
        /// "모시겠습니다", "감사합니다")가 대부분이라, 뜻은 그대로 두고 낱말 끝만 바꾼다.
        /// <list type="bullet">
        /// <item>반말: 낱말 끝 '~요'를 뗀다(고마워요→고마워, 줄게요→줄게), 이에요→이야, 주세요→줘, 주셔서→줘서, 저는→나는, 주인아→주인님.</item>
        /// <item>하오체: 습니다→소(모시겠습니다→모시겠소), ㅂ니다→오(합니다→하오, 입니다→이오), 세요→시오, 네요→구려, 안녕하십니까→안녕하시오.</item>
        /// <item>하게체: 습니다→네(반갑습니다→반갑네), ㅂ니다→네(감사합니다→감사하네), 습니까→는가, 세요→시게, 군요→구먼, 고마워요→고맙네.</item>
        /// </list>
        /// 해요체는 고치지 않는다. 고치지 못한 것(예: 하오체의 '~어요')은 출력 검사(SpeechMismatch)가 거른다.
        /// </summary>
        public static string NormalizeSpeech(string line, string label)
        {
            return NormalizeSpeech(line, label, null);
        }

        /// <summary>name 을 주면 그 도깨비의 자기 이름(어간)도 지운다.</summary>
        public static string NormalizeSpeech(string line, string label, string name)
        {
            return NormalizeSpeech(line, label, name, null);
        }

        /// <summary>ownThings 를 주면 그 도깨비의 것을 '네'라고 부른 말도 '내'로 고친다 (FixOwnPossessive).</summary>
        public static string NormalizeSpeech(string line, string label, string name, IList<string> ownThings)
        {
            return NormalizeSpeech(line, label, name, ownThings, null);
        }

        /// <summary>
        /// nickname 을 주면 "도윤님"처럼 닉네임에 붙인 '님'을 뗀다 — 도깨비는 모두 반말이라 닉네임을 높여 부르지 않는다 ("안녕, 도윤님." 2026-11-13 시험).
        /// </summary>
        public static string NormalizeSpeech(string line, string label, string name, IList<string> ownThings, string nickname)
        {
            return NormalizeSpeech(line, label, name, ownThings, nickname, null);
        }

        /// <summary>
        /// visitDays 를 주면(visit_days 사실) 최근 방문이 7일 안에 있는데 "오랜만에"라고 한 말을 "또"로 고친다 (FixLongAbsence).
        /// </summary>
        public static string NormalizeSpeech(string line, string label, string name, IList<string> ownThings, string nickname, string visitDays)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            line = StripNim(line, nickname);
            line = FixLongAbsence(line, visitDays);
            line = StripAddress(GuestToFriend(FixOwnPossessive(line, ownThings)), name);
            if (line.Length == 0)
            {
                return line;
            }
            // 말투 어미를 고치다 새로 생긴 이름("향산이예요"→"향산이야")을 한 번 더 지운다
            // "덕분에, 관리해 줘서 고마워."처럼 첫 낱말 뒤에 쉼표를 찍으면 어색하다 (첫 낱말을 정해 준 뒤 생겼다)
            return StarterComma.Replace(StripTrailingInterjection(DropRepeatedLead(GuestToFriend(StripAddress(ApplySpeech(line, label), name)))), "$1 ");
        }

        private static string ApplySpeech(string line, string label)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            switch (label)
            {
                case "반말":
                    line = ReplaceWords(line, BanmalWords);
                    line = FixAwkward(DropHonorific(ImnidaToBanmal(line)));
                    line = BanmalPronoun.Replace(line, m => m.Groups[1].Value + BanmalPronounOf(m.Groups[2].Value));
                    return WordYo.Replace(line, m => YoNouns.Contains(m.Groups[1].Value + "요") || m.Groups[1].Value.EndsWith("세")
                            || m.Groups[1].Value.EndsWith("시") ? m.Value : m.Groups[1].Value);
                case "하오체":
                    line = ReplaceWords(line, HaoWords);
                    return WordFormal.Replace(line, m => Formal(m, "소", "오", "소", "오"));
                case "하게체":
                    line = ReplaceWords(line, HageWords);
                    return WordFormal.Replace(line, m => Formal(m, "네", "네", "는가", "는가"));
                default:
                    return line;
            }
        }

        /// <summary>대사에서 '주인님'·'주인'·'손님' 호칭을 지우고 남은 쉼표·띄어쓰기를 다듬는다 ("주인님, 고마워!" → "고마워!").</summary>
        public static string StripAddress(string line)
        {
            return StripAddress(line, null);
        }

        // '주인을'·'주인이'처럼 조사가 붙은 주인 호칭은 지우면 말이 끊긴다("주인을 보니 반가워"→"보니 반가워", "주인한테 자랑했지"→"자랑했지", 2026-11-13 시험).
        // 대명사로 바꾼다 (주인을→너를, 주인이→네가, 주인한테→너한테). 부르는 말("주인님,"·"주인아")과 도깨비 자기 이름은 그대로 지운다
        private static readonly Dictionary<string, string> AddressPronouns = new Dictionary<string, string>
        {
            { "을", "너를" }, { "를", "너를" }, { "이", "네가" }, { "가", "네가" }, { "은", "너는" }, { "는", "너는" }, { "도", "너도" },
            { "한테", "너한테" }, { "에게", "너한테" }, { "랑", "너랑" }, { "이랑", "너랑" }, { "와", "너랑" }, { "과", "너랑" },
            { "의", "네" }, { "께서", "네가" }, { "께", "너한테" }
        };

        // 닦아 주는 사람이 주인이다. "주인을 닦아 주시면"(주인이 닦는 대상)은 "너를 닦아 주면"이 되어 뜻이 뒤바뀌고, "주인님께 닦아 달라고 부탁드려요"는
        // "너한테 닦아 줘"가 되어 어색하다(2026-11-13 시험 S03 #4, S12 #3). 목적어·받는 이(을·를·한테·에게·께)가 부탁·관리 동사 앞에 있으면 지우고,
        // 주어(이·가)는 "네가 닦아 주면"처럼 자연스러워 대명사로 바꾼다
        private static readonly Regex CareOrRequest = new Regex("닦|관리|돌보|씻|문지|정돈|도와|부탁|줘|줄래|줄 수|달라|주면|주라", RegexOptions.CultureInvariant);

        private static string AddressReplacement(Match m, string source)
        {
            string pronoun;
            string particle = m.Groups[3].Value;
            if (m.Groups[2].Value.StartsWith("주인", StringComparison.Ordinal) && AddressPronouns.TryGetValue(particle, out pronoun))
            {
                bool subject = particle == "이" || particle == "가" || particle == "은" || particle == "는" || particle == "께서";
                if (!subject)
                {
                    string rest = source.Substring(m.Index + m.Length);
                    int end = rest.IndexOfAny(new[] { '.', '!', '?', '…', '~' });
                    if (CareOrRequest.IsMatch(end >= 0 ? rest.Substring(0, end) : rest))
                    {
                        return "";
                    }
                }
                return (m.Groups[1].Length > 0 ? " " : "") + pronoun;
            }
            return "";
        }

        public static string StripAddress(string line, string name)
        {
            Regex address = AddressFor(name);
            if (string.IsNullOrEmpty(line) || !address.IsMatch(line))
            {
                return line;
            }
            string source = line;
            line = address.Replace(source, m => AddressReplacement(m, source));
            line = LeadingJunk.Replace(line, "");
            line = CommaBeforeEnd.Replace(line, "");
            line = CommaAfterEnd.Replace(line, " ");
            line = MultiSpace.Replace(line, " ").Trim();
            // "주인님!"처럼 호칭만 있던 대사는 부호만 남는다. 말이 하나도 없으면 빈 대사로 본다
            return Regex.IsMatch(line, "[가-힣A-Za-z0-9]") ? line : "";
        }

        private static string ReplaceWords(string line, string[][] words)
        {
            foreach (string[] pair in words)
            {
                string word = pair[1];
                line = Regex.Replace(line, Regex.Escape(pair[0]) + WordEnd, m => word, RegexOptions.CultureInvariant);
            }
            return line;
        }

        private static string BanmalPronounOf(string word)
        {
            switch (word)
            {
                case "저는": return "나는";
                case "저를": return "나를";
                case "제가": return "내가";
                case "저도": return "나도";
                case "저랑": return "나랑";
                case "제": return "내";
                default: return "나한테";
            }
        }

        // ~습니다 / ~ㅂ니다 / ~습니까 / ~ㅂ니까 를 바꾼다. 앞 글자에 받침 ㅂ 이 있으면 떼고(합 → 하) 붙인다
        private static string Formal(Match m, string afterSeup, string afterB, string askSeup, string askB)
        {
            string head = m.Groups[1].Value;
            char last = m.Groups[2].Value[0];
            bool ask = m.Groups[3].Value == "니까";
            if (last == '습')
            {
                return head + (ask ? askSeup : afterSeup);
            }
            int code = last - 0xAC00;
            if (code >= 0 && code < 11172 && code % 28 == 17) // 받침 ㅂ
            {
                return head + (char)(last - 17) + (ask ? askB : afterB);
            }
            return m.Value;
        }

        /// <summary>띄어쓰기와 문장 부호를 뺀 글.</summary>
        public static string Core(string text)
        {
            return Punct.Replace(text ?? "", "");
        }

        /// <summary>문장으로 나눈다. 마침표·느낌표·물음표·물결 뒤에 공백이나 글 끝이 오면 문장 끝이다 ('…', '...'는 말줄임이라 끝이 아니다).</summary>
        public static List<string> Sentences(string line)
        {
            var pieces = new List<string>();
            if (string.IsNullOrEmpty(line))
            {
                return pieces;
            }
            int start = 0;
            foreach (int end in SentenceEnds(line))
            {
                AddPiece(pieces, line.Substring(start, end - start));
                start = end;
            }
            AddPiece(pieces, line.Substring(start));
            return pieces;
        }

        /// <summary>
        /// 그 도깨비의 어미를 지키는지. 한 곳이라도 어긋나면 false.
        /// <list type="bullet">
        /// <item>pattern: 문장마다 이렇게 끝나야 한다 ("놀고 싶어요~ 같이 놀자!"는 해요체·반말이 섞인 것).</item>
        /// <item>notPattern: 문장은 물론 쉼표로 나뉜 마디도 이렇게 끝나면 안 된다 ("좋은 하루 보내요, 천천히 구경하게"처럼 한 문장 안에서
        /// 섞은 것). 마디는 '~니', '~고'처럼 이어 주는 말로도 끝나서 pattern 은 문장 끝에만 쓴다.</item>
        /// </list>
        /// 감탄만 있는 짧은 문장·마디는 건너뛰고, 끝에 붙은 부르는 말("…하시오, 주인님.")은 떼고 본다. 어미 규칙이 없는 도깨비면 true.
        /// </summary>
        public static bool SpeechMatches(string line, EndingHint hint, out string wrong)
        {
            wrong = null;
            if (hint == null || string.IsNullOrEmpty(line)
                    || (string.IsNullOrEmpty(hint.pattern) && string.IsNullOrEmpty(hint.notPattern)
                        && string.IsNullOrEmpty(hint.bannedPattern)))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(hint.bannedPattern))
            {
                Match banned = Regex.Match(line, hint.bannedPattern, RegexOptions.CultureInvariant);
                if (banned.Success)
                {
                    wrong = banned.Value.Trim();
                    return false;
                }
            }
            var judged = new List<string>();
            foreach (string sentence in Sentences(line))
            {
                if (!IsInterjection(sentence))
                {
                    judged.Add(sentence);
                }
            }
            if (judged.Count == 0)
            {
                judged.Add(line);
            }
            if (!string.IsNullOrEmpty(hint.pattern))
            {
                foreach (string sentence in judged)
                {
                    if (!Regex.IsMatch(Ending(sentence), hint.pattern, RegexOptions.CultureInvariant))
                    {
                        wrong = sentence;
                        return false;
                    }
                }
            }
            if (!string.IsNullOrEmpty(hint.notPattern))
            {
                foreach (string sentence in Sentences(line))
                {
                    foreach (string piece in sentence.Split(','))
                    {
                        string clause = piece.Trim();
                        if (!IsInterjection(clause)
                                && Regex.IsMatch(Ending(clause), hint.notPattern, RegexOptions.CultureInvariant))
                        {
                            wrong = clause;
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>감탄사나 홀로 쓴 대답("네!", "예.")만 있는 문장·마디인지 ("어흠.", "허허…", "앗,"). 비었어도 true.</summary>
        public static bool IsInterjection(string text)
        {
            string core = Core(text);
            return core.Length == 0 || Interjection.IsMatch(core) || Acknowledgment.IsMatch(core);
        }

        // 어미를 볼 끝: 끝의 문장 부호와 부르는 말("주인님")을 뗀 것
        private static string Ending(string text)
        {
            return Vocative.Replace(Trailing.Replace(text, ""), "");
        }

        /// <summary>
        /// 같은 대사로 볼 만큼 겹치는지. 겹치면 무엇이 겹쳤는지 적은 글, 아니면 null.
        /// 띄어쓰기·문장 부호를 빼고 ① 똑같거나 ② 글자 쌍이 SimilarPairs 넘게 겹치거나 ③ 짧은 쪽의 ContainedShare 넘게 긴 쪽에
        /// 그대로 들어 있거나 ④ 첫 문장이 SimilarOpening 넘게 겹치면 같은 대사다.
        /// "이틀 전에 닦아 주셔서 고마워요."처럼 꼭 들어가야 하는 짧은 구절만 같은 것은 같은 대사로 보지 않는다.
        /// </summary>
        public static string Similar(string a, string b)
        {
            string ca = Core(a);
            string cb = Core(b);
            if (ca.Length == 0 || cb.Length == 0)
            {
                return null;
            }
            if (ca == cb)
            {
                return "같은 대사";
            }
            double pairs = PairOverlap(ca, cb);
            if (pairs >= SimilarPairs)
            {
                return "글자 쌍 " + Math.Round(pairs * 100) + "% 겹침";
            }
            string shared = LongestShared(ca, cb);
            if (shared.Length >= ContainedMin && shared.Length >= ContainedShare * Math.Max(ca.Length, cb.Length))
            {
                return "'" + shared + "' 가 그대로 들어 있음";
            }
            int opening = Math.Min(OpeningChars, Math.Min(ca.Length, cb.Length));
            if (opening >= OpeningMin && PairOverlap(ca.Substring(0, opening), cb.Substring(0, opening)) >= SimilarOpening)
            {
                return "시작 '" + ca.Substring(0, opening) + "' 이 같음";
            }
            return null;
        }

        /// <summary>최근 대사 중 같은 대사가 있으면 (그 대사, 이유), 없으면 null.</summary>
        public static KeyValuePair<string, string>? FindSimilar(string line, IEnumerable<string> recent)
        {
            if (recent == null)
            {
                return null;
            }
            foreach (string other in recent)
            {
                string why = Similar(line, other);
                if (why != null)
                {
                    return new KeyValuePair<string, string>(other, why);
                }
            }
            return null;
        }

        /// <summary>이웃한 두 글자 묶음의 겹침 (Dice 계수, 0~1). 띄어쓰기·문장 부호를 뺀 글에 쓴다.</summary>
        public static double PairOverlap(string a, string b)
        {
            if (a.Length < 2 || b.Length < 2)
            {
                return a == b ? 1 : 0;
            }
            var counts = new Dictionary<string, int>();
            for (int i = 0; i + 1 < b.Length; i++)
            {
                string pair = b.Substring(i, 2);
                int n;
                counts.TryGetValue(pair, out n);
                counts[pair] = n + 1;
            }
            int common = 0;
            for (int i = 0; i + 1 < a.Length; i++)
            {
                string pair = a.Substring(i, 2);
                int n;
                if (counts.TryGetValue(pair, out n) && n > 0)
                {
                    common++;
                    counts[pair] = n - 1;
                }
            }
            return 2.0 * common / (a.Length - 1 + b.Length - 1);
        }

        /// <summary>두 글이 함께 가진 가장 긴 구절.</summary>
        public static string LongestShared(string a, string b)
        {
            int bestLength = 0;
            int bestEnd = 0;
            var previous = new int[b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                var current = new int[b.Length + 1];
                for (int j = 1; j <= b.Length; j++)
                {
                    if (a[i - 1] == b[j - 1])
                    {
                        current[j] = previous[j - 1] + 1;
                        if (current[j] > bestLength)
                        {
                            bestLength = current[j];
                            bestEnd = i;
                        }
                    }
                }
                previous = current;
            }
            return a.Substring(bestEnd - bestLength, bestLength);
        }

        private static void AddPiece(List<string> pieces, string piece)
        {
            string trimmed = piece.Trim();
            if (trimmed.Length > 0)
            {
                pieces.Add(trimmed);
            }
        }

        // NpcOutputValidator 의 문장 끝과 같은 규칙
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
    }
}
