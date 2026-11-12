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
        /// <summary>글자 쌍(이웃한 두 글자) 겹침이 이만큼 넘으면 같은 대사다. "고마워, 덕분에 더 선명해졌어." 와 "관리해 줘서 고마워, 덕분에 더 선명해졌어." 는 0.81.</summary>
        public const double SimilarPairs = 0.8;

        /// <summary>짧은 대사의 이만큼이 긴 대사 안에 그대로 있으면 같은 대사다 ("안녕하시오, 계시오?" 와 "안녕하시오, 계시오? 주인님께서…").</summary>
        public const double ContainedShare = 0.8;

        /// <summary>그 겹침이 적어도 이 글자 수는 되어야 한다 ("고마워" 처럼 짧은 낱말 하나로는 같다고 보지 않는다).</summary>
        public const int ContainedMin = 5;

        /// <summary>앞 OpeningChars 글자가 이만큼 겹치면 같은 대사다 — "주인님, 관리해 줘서 고마워요!"로 늘 시작하는 것을 막는다.</summary>
        public const double SimilarOpening = 0.8;

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
        private static readonly string[] FirstOnlyWords = { "또왔", "다시왔", "자주왔", "오랜만", "단골" };

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
                    rx = new Regex("[\\s,]*(?<![가-힣])(?:" + words + ")" + AddressParticles + "(?=[\\s,.!?…~]|$)", RegexOptions.CultureInvariant);
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
                new[] { "자셨", "잤" }, new[] { "서셨", "섰" }, new[] { "내셨", "냈" }, new[] { "드셨", "먹었" }, new[] { "으셨", "었" },
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
                "([가-힣]*[아어여해])\\s?달라고\\s?(?:부탁|요청|말)(?:드립니다|드려요|드릴게요|드릴게|드려|해요|했어요|했어|했다|했네|합니다|할게요|할게|하다|해)?", RegexOptions.CultureInvariant);
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

        private static string DropHonorific(string line)
        {
            foreach (string[] pair in HonorificWords)
            {
                if (pair[0].EndsWith("신"))
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
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            line = StripAddress(GuestToFriend(line), name);
            if (line.Length == 0)
            {
                return line;
            }
            // 말투 어미를 고치다 새로 생긴 이름("향산이예요"→"향산이야")을 한 번 더 지운다
            return DropRepeatedLead(GuestToFriend(StripAddress(ApplySpeech(line, label), name)));
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

        public static string StripAddress(string line, string name)
        {
            Regex address = AddressFor(name);
            if (string.IsNullOrEmpty(line) || !address.IsMatch(line))
            {
                return line;
            }
            line = address.Replace(line, "");
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
            if (shared.Length >= ContainedMin && shared.Length >= ContainedShare * Math.Min(ca.Length, cb.Length))
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
