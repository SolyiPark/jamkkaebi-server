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

        /// <summary>
        /// 앞부분으로 비교하는 글자 수 (띄어쓰기·문장 부호 빼고). 첫 문장으로 비교하면 "주인님~"의 '~'에서 끊겨 놓쳤다.
        /// 10 → 7 (2026-11-17): 탭 인사 12건 중 9건이 "반가워, 오늘 아침…"으로 시작해도 뒤가 달라 반복으로 잡히지 않았다 (7글자 = "반가워오늘아침").
        /// </summary>
        public const int OpeningChars = 7;

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
            new[] { "냈습니다", "냈어" },
            // '으'로 끝나는 형용사의 합쇼체도 반말로 (run.py 와 같다)
            new[] { "뵙겠습니다", "볼게" }, new[] { "뵙겠어", "볼게" }, new[] { "뵐게", "볼게" }, new[] { "뵈어", "봐" }, new[] { "뵈러", "보러" }, new[] { "뵈세요", "봐" },
            new[] { "기쁩니다", "기뻐" }, new[] { "바쁩니다", "바빠" }, new[] { "예쁩니다", "예뻐" }, new[] { "슬픕니다", "슬퍼" },
            new[] { "아픕니다", "아파" }, new[] { "나쁩니다", "나빠" }, new[] { "고픕니다", "고파" }
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

        // 문장 끝 부호와 닫는 부호만 남은 꼬리, 쉼표로 가른 마디(clause). 문장은 SentenceEnds(출력 검사와 같은 문장 끝 규칙)로 가른다
        private static readonly Regex SentenceTail = new Regex("[.!?~。…]*[\"'”’)\\]」』]*\\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex ClauseRun = new Regex("[^,]+,*\\s*", RegexOptions.CultureInvariant);
        // 앞 낱말을 지울 때 함께 지우는 문장 안 부호 ("고마워… 정말 고마워" → "정말 고마워")
        private static readonly Regex InnerMarks = new Regex("^[…~.]*\\s*", RegexOptions.CultureInvariant);

        // 되풀이한 마디·낱말 사이에 이것만 있으면 같은 말을 되풀이한 것이다 ("또 왔네, 다시 왔네", "고마워 정말 고마워"). 주어·대상·다른 풀이말이 있으면
        // 다른 말이라("나는 좋아, 너는 안 좋아") 지우지 않고 출력 검사 InnerRepeat 에 맡긴다 (CodeRabbit 리뷰)
        private static readonly HashSet<string> RestateFillers = new HashSet<string>
        {
            "또", "다시", "정말", "진짜", "참", "너무", "아주", "정말로", "진짜로", "늘", "항상", "오늘도", "와", "오", "아", "음", "우와", "어서",
            "많이", "꼭", "더", "좀", "역시", "엄청", "되게", "무척", "매우", "완전", "그래", "응", "어", "자", "참말"
        };

        /// <summary>
        /// 한 문장 안에서 두 글자 이상 같은 낱말로 같은 말을 되풀이하면 하나만 남긴다 ("또 왔네, 다시 왔네" → "다시 왔네"). 거르면 다시 만드는 데 1.5~2초가 들어 고쳐 쓴다.
        /// <list type="bullet">
        /// <item>쉼표로 가른 서로 다른 마디에 있으면 짧은 마디를 통째로 지운다(길이가 같으면 뒤 마디). 낱말만 지우면 "또, 다시 왔네"처럼 마디가 망가진다.
        /// 뒤 마디를 지우면 그 마디의 문장 끝 부호를 남긴다 ("닦아줘서 고마워, 정말 고마워!" → "닦아줘서 고마워!").</item>
        /// <item>한 마디 안에 있으면 앞의 낱말만 지운다 ("고마워 정말 고마워." → "정말 고마워.").</item>
        /// </list>
        /// 같은 말일 때만 지운다: 지울 마디(또는 두 낱말 사이)에 되풀이한 낱말 말고는 '또·다시·정말' 같은 꾸밈(RestateFillers)만 있고, 두 마디의 부정(안·못·않·없)이 같아야 한다.
        /// "나는 좋아, 너는 안 좋아"·"좋아, 정말 안 좋아"는 두 말이 달라 그대로 두고 InnerRepeat 가 거른다. 문장(SentenceEnds)이 다르면 건드리지 않고,
        /// '나는'·'여기' 같은 RepeatExempt 낱말과 한 글자 낱말은 되풀이로 보지 않는다. 마디 길이는 코드 포인트로 센다(이모지 하나 = 1). run.py 의 drop_repeated_word 와 같다.
        /// </summary>
        public static string DropRepeatedWord(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            var result = new System.Text.StringBuilder(line.Length);
            int start = 0;
            List<int> ends = SentenceEnds(line);
            ends.Add(line.Length);
            foreach (int end in ends)
            {
                if (end <= start)
                {
                    continue;
                }
                // 문장 뒤의 띄어쓰기도 그 문장에 붙여 둔다 (고친 뒤에도 문장 사이 띄어쓰기를 남긴다)
                int stop = end;
                while (stop < line.Length && char.IsWhiteSpace(line[stop]))
                {
                    stop++;
                }
                result.Append(DropRepeatedWordInSentence(line.Substring(start, stop - start)));
                start = stop;
            }
            if (start < line.Length)
            {
                result.Append(line.Substring(start));
            }
            return result.ToString();
        }

        private static string DropRepeatedWordInSentence(string sentence)
        {
            // 한 번에 되풀이 하나를 지운다. 되풀이가 여럿이면 다시 본다 (낱말 수만큼이면 넉넉하다)
            for (int guard = 0; guard < 16; guard++)
            {
                string tail = SentenceTail.Match(sentence).Value;
                string body = sentence.Substring(0, sentence.Length - tail.Length);
                string fixedBody = DropOneRepeat(body);
                if (fixedBody == null)
                {
                    return sentence;
                }
                sentence = fixedBody + tail;
            }
            return sentence;
        }

        // 같은 말의 되풀이 하나를 지운 body, 지울 것이 없으면 null
        private static string DropOneRepeat(string body)
        {
            var words = new List<Match>();
            foreach (Match word in HangulWord.Matches(body))
            {
                if (word.Length >= 2 && !RepeatExempt.Contains(word.Value))
                {
                    words.Add(word);
                }
            }
            MatchCollection clauses = ClauseRun.Matches(body);
            for (int j = 1; j < words.Count; j++)
            {
                Match first = null;
                for (int i = 0; i < j && first == null; i++)
                {
                    if (words[i].Value == words[j].Value)
                    {
                        first = words[i];
                    }
                }
                if (first == null)
                {
                    continue;
                }
                Match second = words[j];
                int firstClause = ClauseAt(clauses, first.Index);
                int secondClause = ClauseAt(clauses, second.Index);
                if (firstClause < 0 || secondClause < 0)
                {
                    continue;
                }
                if (firstClause == secondClause)
                {
                    int between = first.Index + first.Length;
                    if (!OnlyFillers(body.Substring(between, second.Index - between), null))
                    {
                        continue;
                    }
                    int end = between + InnerMarks.Match(body.Substring(between)).Length;
                    return body.Substring(0, first.Index) + body.Substring(end);
                }
                string firstText = clauses[firstClause].Value;
                string secondText = clauses[secondClause].Value;
                int drop = CodePoints(Core(firstText)) < CodePoints(Core(secondText)) ? firstClause : secondClause;
                string dropped = clauses[drop].Value;
                string kept = drop == firstClause ? secondText : firstText;
                if (!OnlyFillers(dropped, first.Value) || Negated(dropped) != Negated(kept))
                {
                    continue;
                }
                string result = body.Remove(clauses[drop].Index, clauses[drop].Length);
                if (drop == clauses.Count - 1)
                {
                    // 마지막 마디를 지우면 앞 마디의 쉼표가 문장 끝에 남는다 → 지우고 지운 마디의 끝 부호를 붙인다
                    result = result.TrimEnd().TrimEnd(',').TrimEnd();
                }
                return result;
            }
            return null;
        }

        // text 의 한글 낱말이 repeated 와 RestateFillers 뿐인지
        private static bool OnlyFillers(string text, string repeated)
        {
            foreach (Match word in HangulWord.Matches(text))
            {
                if (word.Value != repeated && !RestateFillers.Contains(word.Value))
                {
                    return false;
                }
            }
            return true;
        }

        // 부정하는 말(안·못·않·없)이 있는지
        private static bool Negated(string text)
        {
            foreach (Match word in HangulWord.Matches(text))
            {
                string w = word.Value;
                if (w == "안" || w.StartsWith("못", StringComparison.Ordinal) || w.Contains("않") || w.Contains("없"))
                {
                    return true;
                }
            }
            return false;
        }

        // 코드 포인트 수 (이모지 같은 서로게이트 쌍은 하나로 센다 — Python len 과 같다)
        private static int CodePoints(string text)
        {
            int count = 0;
            foreach (char c in text)
            {
                if (!char.IsLowSurrogate(c))
                {
                    count++;
                }
            }
            return count;
        }

        private static int ClauseAt(MatchCollection clauses, int index)
        {
            for (int i = 0; i < clauses.Count; i++)
            {
                if (index >= clauses[i].Index && index < clauses[i].Index + clauses[i].Length)
                {
                    return i;
                }
            }
            return -1;
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
            return CopiedFromSituation(line, situation, null);
        }

        /// <summary>
        /// callName 을 주면 상황 문장의 '너를'도 호칭으로 바꾼 것과 견준다 — 대사의 '너를'은 말투 다듬기가 호칭으로 바꾼 뒤라 그대로는 겹침을 놓친다.
        /// </summary>
        public static string CopiedFromSituation(string line, string situation, string callName)
        {
            string a = Core(line);
            var texts = new List<string> { situation };
            if (!string.IsNullOrEmpty(callName))
            {
                texts.Add(PlayerPronounsToName(situation, callName));
                texts.Add(OwnerToCall(situation, callName));
                texts.Add(PlayerPronounsToName(OwnerToCall(situation, callName), callName));
            }
            foreach (string text in texts)
            {
                string b = Core(text);
                if (a.Length < SituationCopyMin || b.Length < SituationCopyMin)
                {
                    continue;
                }
                string shared = LongestShared(a, b);
                if (shared.Length >= SituationCopyMin)
                {
                    return shared;
                }
            }
            return null;
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
        private static readonly Regex AskWords = new Regex("닦(?!아 ?(줘서|준))|깨워(?! ?(줘서|준))|깨우|부탁|도와|손길|돌봐|씻|문질|관리해 ?(줘|줄|주|달)|관리 ?좀", RegexOptions.CultureInvariant);

        /// <summary>대사에 부탁(닦아·관리해 줘·깨워·부탁·도와…, '마지막 관리는'은 부탁이 아니다)이 들어 있는지. run.py 의 has_ask 와 같다.</summary>
        public static bool HasAsk(string line)
        {
            return !string.IsNullOrEmpty(line) && AskWords.IsMatch(line) && AskRequestForm.IsMatch(line);
        }

        // 부탁 낱말(닦·깨워…)만 있으면 "스스로 닦아야 해"(지어낸 설정)도 부탁으로 봤다 (2026-11-17 21:45 시험 S12 #4). 부탁하는 꼴(~줘·~줄래·~달라·~좋겠·~필요…)이 함께 있어야 한다
        private static readonly Regex AskRequestForm = new Regex("줘(?!서)|줄래|주면|줄 수|주길|주렴|주라|달라|부탁|필요|바라|좋겠", RegexOptions.CultureInvariant);

        /// <summary>안부 답하기에서 선명도가 흐려짐·재봉인이면 부탁이 있어야 한다. run.py 의 needs_ask 와 같다.</summary>
        public static bool NeedsAsk(ReactionRequest request)
        {
            string clarity = request.FindFact(NpcFactKeys.Clarity);
            return request.Candidates != null && request.Candidates.Exists(c => c != null && c.ActionId == NpcActions.AnswerStatus)
                    && (clarity == "흐려짐" || clarity == "재봉인");
        }

        // 흔한 오탈자는 다시 만들지 않고 고친다 ("즐기길 바래"→"즐기길 바라", 2026-11-17 21:09 시험 S05 #1). 그 밖의 오타는 출력 검사(Typo)가 거른다. run.py 의 fix_typos 와 같다
        private static readonly string[][] TypoFixes =
        {
            new[] { "몇일", "며칠" }, new[] { "됬", "됐" }, new[] { "어떻해", "어떡해" }, new[] { "웬지", "왠지" }, new[] { "금새", "금세" }, new[] { "오랫만", "오랜만" },
            new[] { "바램", "바람" }, new[] { "왠만", "웬만" }, new[] { "어의없", "어이없" }, new[] { "설겆이", "설거지" }, new[] { "않되", "안 되" }, new[] { "반갑아", "반가워" }
        };
        private static readonly Regex Barae = new Regex("(길|기를|기|도록)\\s?바래" + WordEnd, RegexOptions.CultureInvariant);

        public static string FixTypos(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            foreach (string[] pair in TypoFixes)
            {
                line = line.Replace(pair[0], pair[1]);
            }
            return Barae.Replace(line, m => m.Groups[1].Value + " 바라");
        }

        // 첫 낱말 '개운하게·깨끗하게'(정화 대사, PromptAssembler.StartersClear)는 뒤의 움직임말을 꾸민다 ("개운하게 관리해 줘서 고마워"). 모델이 뒤를 다른 말로 이으면
        // "개운하게 덕분에 기분이 좋네"·"개운하게 돌아온 발굴자, 오랜만에 관리해줘서 고마워"처럼 어색하다 (2026-11-18 --live-store 미리 만든 정화 대사). run.py 의 fix_starter_adverb 와 같다
        // 가로 공백만 본다 — 줄바꿈("개운하게\\n덕분에…")은 그대로 두어 출력 검사의 줄바꿈 검사가 거르게 한다 (CodeRabbit 리뷰)
        private static readonly Regex StarterAdverb = new Regex("^(개운|깨끗)하게[ \\t]+([가-힣]+)([ \\t]*)", RegexOptions.CultureInvariant);
        private static readonly Regex StarterVocative = new Regex("^(개운|깨끗)하게[ \\t]+[가-힣]+[ \\t]+([가-힣]+)[ \\t]*,[ \\t]*", RegexOptions.CultureInvariant);
        // '~하게'가 꾸밀 수 있는 움직임말 (관리해·닦아·씻은…). 첫 마디(쉼표·문장 끝 앞) 안에 하나라도 있으면 자연스럽다 ("개운하게 오랜만에 나를 관리해줘서")
        private static readonly string[] AdverbFollowers =
        {
            "관리해", "관리받", "닦", "씻", "해", "돌봐", "돌보", "챙겨", "챙기", "만들", "다듬", "정리", "손질", "가꿔", "가꾸", "보살", "치워", "털어",
            "빛나", "빛날", "반짝", "맑아", "느껴", "깨어", "깨워", "일어", "되", "됐", "돼", "지내", "쉬"
        };
        private static readonly Regex FirstClauseEnd = new Regex("[,.!?…~]", RegexOptions.CultureInvariant);

        /// <summary>
        /// 첫 낱말 '개운하게·깨끗하게'가 꾸밀 움직임말 없이 이어진 대사를 고친다. 뒤가 움직임말이면 그대로 둔다 ("개운하게 관리해 줘서 고마워!").
        /// <list type="bullet">
        /// <item>뒤가 "돌아온 발굴자,"처럼 꾸밈말 + 부르는 말이면 '개운하게 돌아온'을 지운다 → "발굴자, 오랜만에 관리해줘서 고마워." (부르는 말이 호칭이 아니면 쉼표까지 지운다)</item>
        /// <item>그 밖에는 "개운해,"로 바꾼다 → "개운해, 덕분에 기분이 좋네".</item>
        /// </list>
        /// run.py 의 fix_starter_adverb 와 같다.
        /// </summary>
        public static string FixStarterAdverb(string line, string callName)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            Match m = StarterAdverb.Match(line);
            if (!m.Success)
            {
                return line;
            }
            string next = m.Groups[2].Value;
            string clause = line.Substring(m.Groups[2].Index);
            Match clauseEnd = FirstClauseEnd.Match(clause);
            if (clauseEnd.Success)
            {
                clause = clause.Substring(0, clauseEnd.Index);
            }
            foreach (Match word in HangulWord.Matches(clause))
            {
                foreach (string follower in AdverbFollowers)
                {
                    if (word.Value.StartsWith(follower, StringComparison.Ordinal))
                    {
                        return line;
                    }
                }
            }
            char last = next[next.Length - 1];
            int final = (last - 0xAC00) % 28;
            Match vocative = StarterVocative.Match(line);
            if (vocative.Success && (final == 4 || final == 8) && last != '은' && last != '는')
            {
                string called = vocative.Groups[2].Value;
                string rest = line.Substring(vocative.Length);
                if (rest.Length > 0)
                {
                    return !string.IsNullOrEmpty(callName) && called == callName ? called + ", " + rest : rest;
                }
            }
            return m.Groups[1].Value + "해, " + line.Substring(m.Groups[2].Index);
        }

        private static readonly Regex StarterComma = new Regex("^(덕분에|이제|훨씬|한결|역시|요즘|조금|점점|많이|어느새|자꾸|몸이|기분이|개운하게|깨끗하게|마지막|벌써|드디어|어서|이렇게|살살|괜찮으면|시간 되면|조심조심|혹시|이왕이면),[ \\t]*", RegexOptions.CultureInvariant); // 가로 공백만 (줄바꿈은 검사가 거른다, CodeRabbit 리뷰)

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
            return IncompleteEnding(line, null);
        }

        /// <summary>address 는 지금 상대를 부르는 말(발굴자·닉네임). "어서 와, 다은."처럼 부르는 말로 끝나면 그 끝 글자('은')를 조사로 보지 않는다.</summary>
        public static string IncompleteEnding(string line, string address)
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
            if (!string.IsNullOrEmpty(address) && (word == address || word == address + "아" || word == address + "야"))
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
                "(?<![가-힣])(새벽|아침|오전|저녁|낮|밤)(?=이야|이네|이라|이니|이지|이었|이구나|이다|에|엔|도|까지|부터|처럼|[\\s.!?…~,]|$)",
                RegexOptions.CultureInvariant);

        // 지금 시간대마다 말해도 되는 시간대 낱말 (경계는 너그럽게: 새벽에 '밤', 밤에 '저녁')
        private static readonly Dictionary<string, string[]> AllowedTimeWords = new Dictionary<string, string[]>
        {
            { "새벽", new[] { "새벽", "밤" } }, { "아침", new[] { "아침", "새벽", "오전" } }, { "오전", new[] { "오전", "아침", "낮" } },
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
        /// 호칭이 규칙과 다르면 그 말, 아니면 null. 친밀하면(3단계 이상 — 주인은 관리 12번, 손님은 30일 방문 12번 이상) 닉네임으로 부르고 '발굴자'는 쓰지 않으며,
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

        // 2026-11-18 20:22 시험 S14 #1: "오, 자개가 정말 정성스럽게 만들어졌네."가 통과했다 — '정성스럽게'·'만들어'와 입음꼴 끝맺음('~어졌네')을 몰랐다
        private static readonly Regex CraftWords = new Regex("공들|정성껏|정성스럽|정성 ?들|빚[어은]|새[긴겨]|칠한|세공|깎[은아]|만든|만들어|다듬[어은]|꾸[며민]", RegexOptions.Compiled);
        // 구경하는 끝맺음: '~거네'·'~거구나', 남이 만든 것처럼 말하는 입음꼴('만들어졌네'·'새겨져 있네'·'다듬어졌어'·'완성됐네')
        private static readonly Regex SpectatorEnding = new Regex("거네|거구나|거로구나|것이네|것이로구나|졌|져 ?있|되었|됐|되어 ?있|돼 ?있", RegexOptions.Compiled);
        // 자기 것을 말하는 풀이말은 쉼표나 다른 주어('그림은'·'내가')에서 끝난다. "자개가 반짝이고, 그림은 정성스럽게 완성됐어."의 '완성됐어'는 그림 이야기다 (CodeRabbit 리뷰)
        private static readonly Regex SubjectToken = new Regex("^[가-힣]+[이가은는]$", RegexOptions.CultureInvariant);
        // '-이'로 끝나는 부사 (주어가 아니다)
        private static readonly HashSet<string> AdverbYi = new HashSet<string> { "많이", "같이", "깊이", "높이", "깨끗이", "반듯이", "따뜻이", "일찍이", "굳이", "고이", "가까이", "틈틈이" };

        // 설명문 규칙(forbidden.json narration)이 주어로 보는 낱말과 끝 부호
        private static readonly Regex NarrationSubject = new Regex("^(?:도깨비|친구|주인|손님|사람|상대|발굴자|아이)(?:이|가|은|는|도)", RegexOptions.CultureInvariant);
        private static readonly Regex NarrationMarks = new Regex("[.!?…~,]+$", RegexOptions.CultureInvariant);

        /// <summary>
        /// 다듬기 전 대사에서 걸린 설명문(공백을 지운 꼴)이 내보낼 대사(공백을 지운 꼴)에 남아 있는지. 주어('발굴자가')는 다듬기가 지웠을 수 있어 빼고 견준다
        /// ("발굴자가그림을그리고있다." → "그림을그리고있다"). 남은 말이 네 글자보다 짧으면 보지 않는다 (낱말 금칙어는 내보낼 대사에서 이미 찾았다). run.py 의 narration_kept 와 같다.
        /// </summary>
        public static bool NarrationKept(string rawMatch, string deliveredCore)
        {
            if (string.IsNullOrEmpty(rawMatch) || string.IsNullOrEmpty(deliveredCore))
            {
                return false;
            }
            string rest = NarrationMarks.Replace(NarrationSubject.Replace(rawMatch, ""), "");
            return rest.Length >= 4 && deliveredCore.IndexOf(rest, StringComparison.Ordinal) >= 0;
        }

        // 자기 것 뒤에 이어진 말에서, 그것을 말하는 풀이말만 (문장 끝·쉼표·다른 주어 앞까지)
        private static string OwnPredicate(string rest)
        {
            int stop = rest.IndexOfAny(new[] { '.', '!', '?', '…', ',' });
            if (stop >= 0)
            {
                rest = rest.Substring(0, stop);
            }
            int at = 0;
            foreach (string token in rest.Split(' '))
            {
                if (token.Length >= 2 && SubjectToken.IsMatch(token) && !AdverbYi.Contains(token) && !CraftWords.IsMatch(token))
                {
                    return rest.Substring(0, at);
                }
                at += token.Length + 1;
            }
            return rest;
        }

        /// <summary>
        /// 주체가 어긋난 대사의 그 낱말, 아니면 null. 도깨비가 자기 것(ownThings: 자개·옻칠…)을 '내 ~' 없이 남의 솜씨를 구경하듯 말하는 것을 잡는다
        /// ("오, 자개가 정말 공들여 만든 거네 덕분에 빛나네." — 2026-11-16 시험 S14). 자기 것이 주어(이·가·은·는)이고, 같은 문장에 만든 솜씨 낱말
        /// (공들·정성껏·정성스럽게·빚어·새겨·만들어…)과 구경하는 끝맺음(~거네·~거구나, 입음꼴 ~어졌네·~됐네)이 함께 있고, 앞에 '내'·'나의'·'우리'가 없을 때만 건다
        /// ("자개가 정성스럽게 만들어졌네." — 2026-11-18 시험 S14). "몸"·"모습"은 거의 모든 문장에 나오는 낱말이라 뺀다. run.py 의 subject_contradiction 과 같다.
        /// </summary>
        // 남의 솜씨를 보고 하는 칭찬 ("대단하네", "멋지구나"). 자기 이야기(관리 감사·안부·지난 대화)에서 만든 솜씨 낱말과 함께 나오고 '내'가 없으면
        // 자기 것을 남의 솜씨처럼 말한 것이다: "정말? 그렇게 공들인 거라면 대단하네." (2026-11-18 23:45 시험 S14 #3 — 자기 것의 이름이 없어 SubjectContradiction 이 놓쳤다)
        private static readonly Regex CraftPraiseEnding = new Regex("(?:대단|훌륭|굉장|근사)(?:하네|하구나|하다|하군|해)|(?:멋지|놀랍)(?:네|구나|다|군)|멋있(?:네|구나|다|군|어)|멋져|놀라워|잘 ?만들었(?:네|구나|다)",
                RegexOptions.CultureInvariant);
        // 상대가 해 준 관리를 칭찬한 것("정성껏 닦아 줘서 고마워, 정말 멋져!")은 자기 솜씨 칭찬이 아니다 (CodeRabbit 리뷰)
        private static readonly Regex CareThanks = new Regex("(?:닦|관리|챙|돌봐|돌보|씻|깨워)[가-힣 ]{0,4}(?:줘서|주어서|줬|준|주니|받아서|받으니)", RegexOptions.CultureInvariant);
        private static readonly Regex SelfMarker = new Regex("(?<![가-힣])(?:내|나|나의|내가|나는|난|우리)(?![가-힣])", RegexOptions.CultureInvariant);
        private static readonly string[] SelfTalkActions = { NpcActions.ThankCalm, NpcActions.ThankJoyful, NpcActions.AnswerStatus, NpcActions.RecallTalk };

        /// <summary>
        /// 자기 이야기(관리 감사·안부·지난 대화)에서 자기 솜씨를 남의 솜씨 보듯 칭찬한 문장이면 그 칭찬 말, 아니면 null. 한 문장 안에 만든 솜씨 낱말(공들·정성껏…)과
        /// 구경하는 칭찬(대단하네·멋지구나…)이 함께 있고 '내·나·우리'가 없을 때. run.py 의 craft_praise 와 같다.
        /// </summary>
        public static string CraftPraise(string line, string action)
        {
            if (string.IsNullOrEmpty(line) || Array.IndexOf(SelfTalkActions, action) < 0)
            {
                return null;
            }
            foreach (string sentence in Regex.Split(line, "(?<=[.!?…~])\\s+"))
            {
                Match praise = CraftPraiseEnding.Match(sentence);
                if (praise.Success && CraftWords.IsMatch(sentence) && !SelfMarker.IsMatch(sentence) && !CareThanks.IsMatch(sentence))
                {
                    return praise.Value;
                }
            }
            return null;
        }

        public static string SubjectContradiction(string line, IList<string> ownThings)
        {
            if (string.IsNullOrEmpty(line) || ownThings == null)
            {
                return null;
            }
            foreach (string thing in ownThings)
            {
                if (string.IsNullOrEmpty(thing) || thing == "몸" || thing == "모습")
                {
                    continue;
                }
                int from = 0;
                while (true)
                {
                    int at = line.IndexOf(thing, from, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        break;
                    }
                    from = at + thing.Length;
                    if (from >= line.Length || "이가은는".IndexOf(line[from]) < 0)
                    {
                        continue;
                    }
                    string before = line.Substring(0, at).TrimEnd();
                    if (before.EndsWith("내", StringComparison.Ordinal) || before.EndsWith("나의", StringComparison.Ordinal)
                            || before.EndsWith("우리", StringComparison.Ordinal) || before.EndsWith("제", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string rest = OwnPredicate(line.Substring(from + 1));
                    if (CraftWords.IsMatch(rest) && SpectatorEnding.IsMatch(rest))
                    {
                        return thing;
                    }
                }
            }
            return null;
        }

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
            line = rx.Replace(line, m => (m.Groups[1].Value == "너의" ? "나의" : "내") + m.Groups[2].Value + m.Groups[3].Value);
            // "그 자개 공들여 만들었네"·"이 자개는…"처럼 '그·이·저'로 말하면 자개 주인을 플레이어로 가정한 말처럼 들린다(2026-11-17 22:27 시험 S14). 도깨비 자신의 것은 '내'로 말한다.
            // '몸'·'모습'은 상대의 것을 가리킬 수도 있어 고치지 않는다
            var demo = new List<string>();
            foreach (string thing in ownThings)
            {
                if (!string.IsNullOrEmpty(thing) && thing != "몸" && thing != "모습")
                {
                    demo.Add(Regex.Escape(thing));
                }
            }
            if (demo.Count > 0)
            {
                line = Cached("(?<![가-힣])(?:그|이|저)(\\s?)(" + string.Join("|", demo.ToArray()) + ")").Replace(line, m => "내" + m.Groups[1].Value + m.Groups[2].Value);
            }
            // 앞에 아무것도 없이 자기 것을 말하며 솜씨를 이야기하면("기억난다, 자개 공들여 만들었다고 자랑했던 일") 자개 주인이 남처럼 들린다 (2026-11-19). 같은 마디에 만든 솜씨 낱말이 있으면 '내'를 붙인다. 주어(자개가·자개는)로 쓴 것은 붙이지 않는다 — 남의 솜씨처럼 말한 "자개가 정성스럽게 만들어졌네"는 고치지 않고 주체 검사가 거른다. run.py 의 fix_own_possessive 와 같다
            if (demo.Count > 0)
            {
                var bareThings = new List<string>();
                foreach (string thing in demo)
                {
                    bareThings.Add(Regex.Escape(thing));
                }
                Regex bare = Cached("(?<![가-힣])(?<!내 )(?<!나의 )(?<!우리 )(?<!제 )(" + string.Join("|", bareThings.ToArray()) + ")(?![이가은는](?![가-힣]))"
                        + "(?=[^,.!?…~]*?(?:" + CraftWords + "))");
                line = bare.Replace(line, m => "내 " + m.Groups[1].Value, 1);
            }
            return line;
        }

        // 지난 대화를 "~던 일"로 끝내면 말이 끝나지 않는다 → "~던 일이 기억나." (앞의 "기억난다,"는 겹치므로 뗀다). run.py 의 fix_recall_fragment 와 같다
        private static readonly Regex RecallFragmentEnd = new Regex("던 일([.!…~]*)$", RegexOptions.CultureInvariant);
        // 낱말이 끝날 때만 지운다 — "기억나는 건 함께 웃었던 일"의 '기억나'를 지우면 "는 건 …"이 남았다 (CodeRabbit 리뷰)
        private static readonly Regex RecallLead = new Regex("^(?:기억난다|기억나|생각난다|생각나)(?=[,.!\\s]|$)[,.!]?\\s*", RegexOptions.CultureInvariant);
        private static readonly Regex RecallSubject = new Regex("(?:기억|생각)나는 (?:건|게|것은)", RegexOptions.CultureInvariant);

        public static string FixRecallFragment(string line)
        {
            if (string.IsNullOrEmpty(line) || !RecallFragmentEnd.IsMatch(line))
            {
                return line;
            }
            line = RecallLead.Replace(line, "");
            // "기억나는 건 함께 웃었던 일"처럼 이미 '기억나는 건'으로 시작했으면 "…던 일이야."로 끝맺는다 ("…던 일이 기억나"를 붙이면 '기억나'가 겹친다)
            string ending = RecallSubject.IsMatch(line) ? "던 일이야" : "던 일이 기억나";
            return RecallFragmentEnd.Replace(line, m => ending + (m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "."));
        }

        /// <summary>자기 솜씨를 말하면서 자기 것을 '내' 없이 맨 이름으로 말하면("기억난다, 자개 공들여 만들었다고 자랑했던 일") 남의 자개처럼 들린다 → '내 자개'. 조사가 없어 주체 검사도 놓쳤다 (2026-11-19 --live-store). run.py 의 fix_bare_own_thing 과 같다. '몸'·'모습'은 상대의 것일 수 있어 고치지 않는다.</summary>
        public static string FixBareOwnThing(string line, IList<string> ownThings)
        {
            if (string.IsNullOrEmpty(line) || ownThings == null || !CraftWords.IsMatch(line) || SelfMarker.IsMatch(line))
            {
                return line; // 이미 '내·나·우리'로 자기 것이라고 했으면 그대로
            }
            // 가장 앞의 자기 것 하나에만 붙인다. 주어(이·가·은·는)로 쓴 것은 붙이지 않고 주체 검사(SubjectContradiction)가 거르게 둔다. '빛·반짝임'은 속성이라 뺀다("빛이 비쳐"를 "내 빛이"로 바꾸지 않는다)
            int first = -1;
            foreach (string thing in ownThings)
            {
                if (string.IsNullOrEmpty(thing) || thing == "몸" || thing == "모습" || thing == "빛" || thing == "반짝임")
                {
                    continue;
                }
                Match m = Cached("(?<![가-힣])" + Regex.Escape(thing) + "(?=(?:을|를|도|의|과|와|랑|만|에|처럼)?(?![가-힣]))").Match(line);
                if (m.Success && (first < 0 || m.Index < first))
                {
                    first = m.Index;
                }
            }
            return first < 0 ? line : line.Substring(0, first) + "내 " + line.Substring(first);
        }

        private static readonly Regex CraftNe = new Regex("(었|았|였|했)네([.!?…~]|$)", RegexOptions.CultureInvariant);

        /// <summary>자기 솜씨를 말하는 문장이 구경하듯 '~었네'로 끝나면 '~었어'로 고친다. run.py 의 fix_craft_spectator 와 같다.</summary>
        public static string FixCraftSpectator(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            var parts = new List<string>();
            foreach (string sentence in Sentences(line))
            {
                parts.Add(CraftWords.IsMatch(sentence) ? CraftNe.Replace(sentence, m => m.Groups[1].Value + "어" + m.Groups[2].Value) : sentence);
            }
            return parts.Count > 0 ? string.Join(" ", parts.ToArray()) : line;
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
        public static string FixNoRecord(string line)
        {
            return FixNoRecord(line, 0);
        }

        /// <summary>
        /// "오랜만이야"·"오랜만에 보네"를 바꿔 넣는 말. variant 로 고른다 (0 = "반가워"). 늘 "반가워" 하나로 바꾸면 모델이 다르게 쓴 인사들이 같은 대사로 모여
        /// ("어서 왔구나, 오랜만에 보네" → "어서 와, 반가워") 최근 대사와 같다고 걸리고 다시 만들었다 (2026-11-18 22:16 시험: 말발굽 탭 인사 16번 중 12번이 '오랜만',
        /// S23 #1·#2·#4·S24 #3 반복). 출력 검사기는 최근 대사와 겹치지 않는 것을 고른다 (NpcOutputValidator, run.py 의 pick_no_record_variant).
        /// </summary>
        public static readonly string[] NoRecordGreetings = { "반가워", "기다렸어", "보고 싶었어", "기뻐" };

        /// <summary>대사에 FixNoRecord 가 고칠 '오랜만'이 있는지 (바꿔 넣는 말을 고를 필요가 있는지).</summary>
        public static bool HasLongAbsenceWord(string line)
        {
            return !string.IsNullOrEmpty(line) && Regex.IsMatch(line, "오(?:랜|래간)만", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// '오랜만'이 서술어·마디인 꼴을 고친다 (FixNoRecord·FixLongAbsence 가 함께 쓴다): "오랜만이야·오랜만이네요·오랜만이구나·오랜만인걸"과 말끝의 "(정말) 오랜만!"은
        /// replacement 로 바꾸고, "오랜만이라(서)·오랜만인데·오랜만이니까"로 시작하는 마디는 마디째 지운다 ("오랜만이라 반가워" → "반가워").
        /// 뒤에 글자가 붙은 꼴("오랜만이라고")은 지우면 토막이 남아 두고, '오랜만에'는 부르는 쪽이 따로 고친다. run.py 의 fix_absence_clause 와 같다.
        /// <para>2026-11-24 기기 시험: "오랜만이야"만 고쳐, 문장 안에 남은 다른 꼴의 '오랜만'을 출력 검사가 기록과 어긋남으로 거르고 기본 대사로 넘어갔다 (인사 실패의 첫째 원인).</para>
        /// </summary>
        public static string FixAbsenceClause(string line, string replacement)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            line = line.Replace("오랫만", "오랜만"); // 철자 고치기(FixTypos)는 이보다 뒤에 돈다
            line = Regex.Replace(line, "오(?:랜|래간)만(?:이(?:네요|구나|군요|군|잖아)|인걸)", replacement, RegexOptions.CultureInvariant);
            line = Regex.Replace(line, "오(?:랜|래간)만이(?:야|네|다|에요)", replacement, RegexOptions.CultureInvariant);
            // 마디 뒤에 이어 주는 말("그런지·그래서·그런가·그런데")이 오면 함께 지운다 — "오랜만이라 그런지 반가워!"에서 '그런지'만 남지 않게
            line = Regex.Replace(line, "오(?:랜|래간)만(?:이(?:라서|라|어서|니까|던데|라도)|인데)(?![가-힣]),? ?(?:(?:그런지|그래서|그런가|그런데)(?![가-힣]),? ?)?", "",
                    RegexOptions.CultureInvariant);
            return Regex.Replace(line, "(?:(?:정말|진짜|참|너무|되게) )?오(?:랜|래간)만(?=[!?.~…]|$)", replacement, RegexOptions.CultureInvariant);
        }

        public static string FixNoRecord(string line, int variant)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            string greeting = NoRecordGreetings[((variant % NoRecordGreetings.Length) + NoRecordGreetings.Length) % NoRecordGreetings.Length];
            line = FixAbsenceClause(line, greeting);
            // '오랜만에'는 뒤가 혼자 문장이 되는 움직임말(왔·와·찾아·오·들렀·돌아…)일 때만 지운다. "오랜만에 보네"를 지우면 목적어 없는 "보네"가 남는다(2026-11-17 21:45 시험 S23 #1 "어서 와! 보네, 도윤아").
            // "오랜만에 보네"는 지우지 않고 뜻이 같은 말로 바꾼다 (22:07 시험: 남겨 두자 기록과 어긋남으로 거르고 다시 만들어 시도가 늘었다)
            // "오랜만에 보자"도 같다 (22:16 시험 S22 #3: 못 고쳐 기록과 어긋남으로 걸렸다)
            line = Regex.Replace(line, "오(?:랜|래간)만에 ?(?:보는구나|보네|보는걸|보는군|보자(?![가-힣])|봤네|봤구나|봐(?![가-힣])|만났네|만났구나|마주쳤네)", greeting, RegexOptions.CultureInvariant);
            line = Regex.Replace(line, "오(?:랜|래간)만에 ?보니", "만나니", RegexOptions.CultureInvariant);
            line = Regex.Replace(line, "오(?:랜|래간)만에 ?봐서", "만나서", RegexOptions.CultureInvariant);
            // 그 밖의 '오랜만에'는 꾸밈말 자리라 지우지 않고 '이렇게'로 바꿔 넣는다 (AbsenceToThisWay, 2026-11-26)
            line = AbsenceToThisWay(line);
            return Regex.Replace(line, "(?<![가-힣])(?:또|다시) ?(?=왔)", "", RegexOptions.CultureInvariant);
        }

        // '오랜만에'(앞의 '정말·진짜·참·너무·되게' 포함) 바로 뒤에 '이렇게'가 있으면 지운다 (겹치지 않게)
        private static readonly Regex AbsenceBeforeThisWay = new Regex("(?:(?:정말|진짜|참|너무|되게) )?오(?:랜|래간)만에 ?(?=이렇게)", RegexOptions.CultureInvariant);
        // 뒤에 띄어 쓴 말이 오거나, 붙여 쓴 움직임말(왔·와·찾아…)이 오는 '오랜만에' → '이렇게 '. 붙여 쓴 그 밖의 말("오랜만에야 왔구나")은 바꾸면 "이렇게 야"가 되어 두고, 출력 검사가 거른다
        private static readonly Regex AbsenceAdverb = new Regex(
                "(?:(?:정말|진짜|참|너무|되게) )?오(?:랜|래간)만에(?: (?=[가-힣])|(?=(?:왔|와|오|찾아|들렀|돌아|방문|놀러|만나|만났|마주)))", RegexOptions.CultureInvariant);
        // 쉼표·끝 앞의 '오랜만에'("오랜만에, 반가워")는 꾸밀 말이 없어 예전처럼 지운다
        private static readonly Regex AbsenceAdverbAlone = new Regex("(?:(?:정말|진짜|참|너무|되게) )?오(?:랜|래간)만에(?=[ ,]|$),? ?", RegexOptions.CultureInvariant);

        /// <summary>
        /// 꾸밈말 '오랜만에'를 지우지 않고 '이렇게'로 바꿔 넣는다 ("와, 오랜만에 찾아왔네." → "와, 이렇게 찾아왔네.", "왔구나, 오랜만에 찾아줘서 고마워." → "왔구나, 이렇게 찾아줘서 고마워.").
        /// 뒤에 '이렇게'가 이미 있으면 지우고, 꾸밀 말이 없으면(쉼표·끝 앞) 지운다. '오랜만에 보네·보니·봐서'는 부르는 쪽이 먼저 바꾼다. run.py 의 absence_to_this_way 와 같다.
        /// <para>2026-11-26 PC 기기 흐름 시험: 예전에는 지우기만 해서 "와, 찾아왔네."·"드디어 왔구나, 내 친구를 만나네."처럼 꾸밈말 자리가 빈 토막이 남았고, 출력 검사는 나오면 안 되는 말만 찾아
        /// 그대로 통과했다. 같은 자리에 시간 간격을 뜻하지 않는 '이렇게'를 넣으면 문장 모양이 그대로 남는다.</para>
        /// </summary>
        public static string AbsenceToThisWay(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            line = AbsenceBeforeThisWay.Replace(line, "");
            line = AbsenceAdverb.Replace(line, "이렇게 ");
            return AbsenceAdverbAlone.Replace(line, "");
        }

        /// <summary>
        /// 기록이 없는 주인 장면이라 "오랜만"·"또 왔"을 고쳐야 하는지: 손님 장면이 아니고, 대화 기억·마지막 관리·방문 사실이 없고, 상황 문장에도 그 낱말이 없을 때.
        /// run.py 의 no_record_applies 와 같다.
        /// </summary>
        public static bool NoRecordApplies(bool isVisit, bool hasRecord, string situation)
        {
            if (isVisit || hasRecord)
            {
                return false;
            }
            string basis = Core(situation);
            foreach (string word in FirstOnlyWords)
            {
                if (basis.Contains(word))
                {
                    return false;
                }
            }
            return true;
        }

        // "덕분에 관리해 줘서 고마워"는 고마움을 두 번 말해 어색하고 금칙어(`덕분에 … 줘서`)에도 걸린다. 첫 낱말 "덕분에"를 받은 모델이 이어 쓰는 말이라(2026-11-17 시험 S08 #4)
        // "관리해 줘서 고마워"로 줄인다. run.py 의 fix_thanks_overlap 과 같다
        private static readonly Regex ThanksOverlap = new Regex("덕분에\\s*(관리해|닦아)\\s?(줘서)", RegexOptions.CultureInvariant);

        // 관리는 늘 플레이어가 한다. 도깨비가 자기를 관리한 쪽으로 말한 주어("개운하게 도깨비가 관리해줘서 고마워", "내가 닦아 줘서")는 지운다 — 주어를 빼면 받은 쪽에서 고마워하는 말이 된다 (2026-11-19). run.py 의 fix_care_giver 와 같다
        private static readonly Regex SelfCareGiver = new Regex(
                "(?<![가-힣])(?:도깨비(?:가|는|께서)|내가|나는|난)\\s?(?=(?:관리|닦|챙겨|챙기|돌봐|돌보|깨워|씻겨)[가-힣]*\\s?(?:줘서|주어서|줬|준|주니))", RegexOptions.CultureInvariant);
        // 실화 이야기는 늘 도깨비가 들려주고 플레이어는 듣는다. "자개 반짝임 얘기해줘서 고마워"처럼 플레이어가 이야기한 것으로 말하면 "얘기 들어줘서"로 바꾼다 (2026-11-19). run.py 의 fix_story_teller 와 같다
        // 고마워하는 꼴("~해 줘서·주어서·주니")만 본다 — "내가 이야기를 들려줬잖아"는 도깨비가 들려준 것을 바르게 말했다 (CodeRabbit 리뷰)
        private static readonly Regex PlayerTold = new Regex("(얘기|이야기)\\s?(?:를\\s?)?해(\\s?)(줘서|주어서|주니)", RegexOptions.CultureInvariant);
        private static readonly Regex PlayerToldTo = new Regex("들려(\\s?)(줘서|주어서|주니)", RegexOptions.CultureInvariant);
        private static readonly Regex TellerIsSelf = new Regex("(?<![가-힣])(?:내가|나는|난)(?![가-힣])", RegexOptions.CultureInvariant);

        public static string FixCareGiver(string line)
        {
            return string.IsNullOrEmpty(line) ? line : SelfCareGiver.Replace(line, "");
        }

        public static string FixStoryTeller(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            if (TellerIsSelf.IsMatch(line))
            {
                return line; // 도깨비가 들려준 것을 자기 입으로 말했다
            }
            line = PlayerTold.Replace(line, "$1 들어$2$3");
            return PlayerToldTo.Replace(line, "들어$1$2");
        }

        public static string FixThanksOverlap(string line)
        {
            return string.IsNullOrEmpty(line) ? line : ThanksOverlap.Replace(line, "$1 $2");
        }

        public static string FixLongAbsence(string line, string visitDays)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(visitDays) || line.IndexOf("오", StringComparison.Ordinal) < 0)
            {
                return line;
            }
            if (visitDays.Trim() == "오늘")
            {
                // 처음 온 친구: "오랜만이야"는 "반가워", "오랜만에 와 줘서"는 "와 줘서" (지난 만남을 지어내지 않는다). "오랜만에 보니"는 "만나니" — 지우면 "보니"만 남는다
                line = Regex.Replace(line, "오(?:랜|래간)만에 ?보니", "만나니", RegexOptions.CultureInvariant);
                line = FixAbsenceClause(line, "반가워");
                return AbsenceToThisWay(line); // "오랜만에 와 줘서" → "이렇게 와 줘서" (2026-11-26, 예전에는 지웠다)
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
            // 이미 "또 왔구나!"라고 했으면 "또 왔구나! 반가워, 또 왔네!"처럼 겹치지 않게 "반가워"로 바꾼다 (2026-11-16 시험 S25 #1)
            string again = Regex.IsMatch(line, "(?<![가-힣])또(?![가-힣])", RegexOptions.CultureInvariant) ? "반가워" : "또 왔네";
            // "또 왔구나! 오랜만에 보니 반가워"의 '오랜만에'를 지우면 "또 왔구나! 보니 반가워"처럼 '보니'만 남는다 (2026-11-18 --live-store) → "다시 보니"
            line = Regex.Replace(line, "오(?:랜|래간)만에 ?보니", "다시 보니", RegexOptions.CultureInvariant);
            line = FixAbsenceClause(line, again);
            if (Regex.IsMatch(line, "(?<![가-힣])또(?![가-힣])", RegexOptions.CultureInvariant))
            {
                // 이미 "또 왔네"라고 했으면 "또 왔네, 또 와서"처럼 겹치지 않게 '또'로 바꾸지 않는다 (2026-11-17 22:27 시험 S06 #1). 지우지 않고 '이렇게'로 (2026-11-26)
                line = AbsenceToThisWay(line);
            }
            else
            {
                line = Regex.Replace(line, "오(?:랜|래간|랜 |래간 )만에", "또", RegexOptions.CultureInvariant);
            }
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
            // 말씀(드리다): "말씀드렸지?"→"말했지?" (먼저 바꾼다)
            list.AddRange(new[]
            {
                new[] { "말씀드립니다", "말해" }, new[] { "말씀드릴게", "말할게" }, new[] { "말씀드릴까", "말할까" }, new[] { "말씀드릴래", "말할래" }, new[] { "말씀드렸", "말했" },
                new[] { "말씀드리", "말하" }, new[] { "말씀드려", "말해" }, new[] { "말씀드릴", "말할" }, new[] { "말씀하", "말하" }, new[] { "말씀해", "말해" }, new[] { "말씀", "말" }
            });
            // '인사드려요'·'부탁드려요'의 '드리다'는 '주다'가 아니라 '하다'로 바꾼다
            // "말씀"은 뺐다: "말씀드릴게"→"말씀할게"가 되어 존대가 남았다. 아래 말씀 항목이 "말했·말할게"로 먼저 바꾼다 (2026-11-17 19:43 시험 S14 #2 "말씀드렸지?")
            string[] nouns = { "부탁", "인사", "감사", "사과", "축하", "약속" };
            string[][] humble = { new[] { "드립니다", "해" }, new[] { "드릴게", "할게" }, new[] { "드릴까", "할까" }, new[] { "드릴래", "할래" }, new[] { "드려", "해" }, new[] { "드렸", "했" }, new[] { "드릴", "할" } };
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
                new[] { "드릴게", "줄게" }, new[] { "드릴까", "줄까" }, new[] { "드려", "줘" },
                // "보여드릴 수 있어서"(2026-11-16 시험 S14 #3)처럼 남은 겸양 '드리다'
                new[] { "드릴래", "줄래" }, new[] { "드렸", "줬" }, new[] { "드릴", "줄" },
                // 2026-11-17 19:43 시험(존대·반말 혼합이 여전했다): 남은 겸양·높임 낱말을 반말로. "모시고"는 '시'를 떼면 "모고"가 되므로 낱말로 먼저 바꾼다
                new[] { "여쭤", "물어" }, new[] { "여쭙", "묻" }, new[] { "여쭈", "묻" }, new[] { "모시고", "데리고" }, new[] { "모셔다", "데려다" }, new[] { "모셔", "데려" },
                new[] { "뵙고", "보고" }, new[] { "뵙게", "보게" }, new[] { "뵈어", "봐" }, new[] { "뵈었", "봤" }, new[] { "뵀", "봤" },
                new[] { "계시지", "있지" }, new[] { "계시군", "있구나" }, new[] { "계시네", "있네" }, new[] { "계시", "있" },
                new[] { "주무세요", "자" }, new[] { "주무시", "자" }, new[] { "주무셔", "자" },
                new[] { "오십시오", "와" }, new[] { "주십시오", "줘" }, new[] { "하십시오", "해" }, new[] { "보십시오", "봐" }, new[] { "가십시오", "가" }, new[] { "앉으십시오", "앉아" },
                new[] { "그러세요", "그래" }, new[] { "그러셨", "그랬" }, new[] { "그러시군", "그렇구나" }, new[] { "성함", "이름" }, new[] { "연세", "나이" }
            };
            list.AddRange(rest);
            return list.ToArray();
        }

        // '~다니·~더니·~면서·~거나·~든지·~느라'도 뗀다: "나를 부르시다니 반가워"가 높임이 남은 채 나갔다(2026-11-16 시험 S01 #1)
        private static readonly Regex HonorificSi = new Regex("(?<=[가-힣])(?<![마도다모])시(?=면|며|고|는|던|지|네|겠|니|거|길|려|도록|어서|나|잖|더라|다니|더니|면서|거나|든지|느라)", RegexOptions.CultureInvariant);
        // '닦아 주면 좋겠어'는 부탁을 돌려 말해 어색하다. 바로 '닦아 줘'로 말한다
        private static readonly Regex AskRequest = new Regex("(?<=[아어여해])( ?)주면 좋겠(?:어요|어|다)(?=[\\s.!?…~,]|$)", RegexOptions.CultureInvariant);

        // 문맥에 어색한 말을 고친다 ("닦아달라고 부탁해" → "닦아 줘", "어서 왔어" → "어서 와"). run.py 의 fix_awkward 와 같다. 못 고친 것은 금칙어 awkward 묶음이 거른다
        private static readonly Regex QuotedAsk = new Regex(
                "([가-힣]*[아어여해])\\s?달라고\\s?(?:부탁|요청|말)(?:드리고\\s?싶(?:어요|어|습니다)|하고\\s?싶(?:어요|어)|드립니다|드려요|드릴게요|드릴게|드려|해요|했어요|했어|했다|했네|합니다|할게요|할게|하다|해)?", RegexOptions.CultureInvariant);
        // "어서 왔어?"를 "어서 와"로 고치면서 물음표를 남기면 "어서 와?"가 된다 (2026-11-18 23:45 시험 S06 #4 "어서 와? 또 왔네."). 인사라 "!"로 바꾼다
        // 2026-11-26 기기 시험(ver8): 첫 낱말 '어서' 뒤의 "어서 왔니?"·"어서 왔군"·"어서 왔다니, 반가워"처럼 목록에 없던 어미는 고치지 못해 금칙어(어서왔)로 버렸다
        // (Tab S10 FE 10초 판 탭 인사 실패 5건 중 3건). 맺는 어미는 모두 "어서 와"로, 뒤 마디로 잇는 어미(~다니·~으니·~는데)는 "어서 와,"로 고친다.
        // 맺는 어미 뒤에 한글이 이어지면("왔지만") 고치지 않는다 — 예전에는 "어서 와만"이 됐다. "어서 와라"(시키는 말)도 "어서 와"로
        // '어서'는 따로 선 낱말일 때만 본다 — "보고 싶어서 왔니?"의 '싶어서'를 "보고 싶어서 와!"로 바꾸지 않는다. 띄어쓰기는 가로 공백만 본다 —
        // 줄바꿈("어서\n왔어")을 삼키면 두 줄 대사가 한 줄로 보여 줄바꿈 검사를 빠져나간다 (StarterComma 와 같은 CodeRabbit 리뷰)
        private static readonly Regex Arrive = new Regex(
                "(?<![가-힣])어서,?[ \\t]?(?:왔(?:(다니|으니까|으니|는데),?(?=[ \\t])|(?:어요|어|네요|네|구나|구먼|군|지|니|단다|다|잖아)(?![가-힣]))|와라(?![가-힣])|와(?![가-힣]))(\\?+)?",
                RegexOptions.CultureInvariant);

        // "편하게 구경해 줘"는 손님에게 구경을 부탁하는 말이라 어색하다(2026-11-16 시험 S05 #2·#3). 권하는 말로 "구경해"라고 한다. run.py 의 OFFER_ASK_RE 와 같다
        private static readonly Regex OfferAsk = new Regex("(구경해|구경하고 가|둘러봐|쉬었다 가|놀다 가)\\s?줘" + WordEnd, RegexOptions.CultureInvariant);

        private static string FixAwkward(string line)
        {
            line = QuotedAsk.Replace(line, m => m.Groups[1].Value + " 줘");
            line = OfferAsk.Replace(line, m => m.Groups[1].Value);
            return Arrive.Replace(line, m => m.Groups[1].Success ? "어서 와," : m.Groups[2].Success ? "어서 와!" : "어서 와");
        }

        /// <summary>
        /// 인사말뿐인 짧은 인사("왔구나!", "어서 와.")에 지금 상대를 부르는 말을 붙인다 ("왔구나, 하늘아!", "어서 와, 발굴자."). 붙일 수 없으면 null —
        /// 부를 이름이 없거나, 이미 그 이름을 불렀거나, 붙이면 limit 를 넘거나, 부호를 빼면 남는 말이 없을 때.
        /// 프롬프트가 "한 번 불러 준다"고 적은 대로이고, 호칭 규칙(AddressName)을 따른 이름이라 호칭 검사에 걸리지 않는다. run.py 의 call_plain 과 같다.
        /// <para>2026-11-26 기기 시험(ver8): 탭 인사 실패 57건 중 9건이 인사말뿐이었다 — 모델이 쓴 "왔구나!"·"어서 와!"는 맞는 인사인데 버리고 기본 대사로 넘어갔다.</para>
        /// </summary>
        public static string CallPlain(string line, string address, int limit)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(address) || Core(line).Contains(Core(address)))
            {
                return null;
            }
            Match m = PlainTail.Match(line.Trim());
            string body = m.Groups[1].Value.TrimEnd(' ', '\t', ',');
            if (Core(body).Length == 0)
            {
                return null;
            }
            string called = body + ", " + NpcLineSlots.Vocative(address) + (m.Groups[2].Length > 0 ? m.Groups[2].Value : "!");
            return CodePoints(called) <= limit ? called : null; // 코드 포인트로 센다 — 이모지가 든 대사도 run.py(len)와 같게 자른다
        }

        // 끝 부호: NpcLineChecks.SentenceEnds 와 같게 '。'도 끝 부호로 본다 ("왔구나。" → "왔구나, 하늘아。"이지 "왔구나。, 하늘아!"가 아니다)
        private static readonly Regex PlainTail = new Regex("^(.*?)([.!?…~。]*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private static readonly Regex PastSeupnida = new Regex("([가-힣])습니다" + WordEnd, RegexOptions.CultureInvariant);

        /// <summary>'기다렸습니다'·'좋았습니다'처럼 받침 ㅆ 뒤의 '~습니다'를 '~어'로 (기다렸어·좋았어). 글자 표(았었였왔…)에 없는 것까지 잡는다. run.py 의 past_seupnida 와 같다.</summary>
        private static string PastSeupnidaToBanmal(string line)
        {
            return PastSeupnida.Replace(line, m => (m.Groups[1].Value[0] - 0xAC00) % 28 == 20 ? m.Groups[1].Value + "어" : m.Value);
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

        // 르 불규칙(부르셔서→불러서, 부르셨→불렀, 부르신→부른)·우 불규칙(깨우셨→깨웠)·'으셔'(찾으셔→찾아)의 높임. 2026-11-16 시험: 탭 상황을 "너를 불렀다"로 바꾼 뒤
        // "부르셔서 반가워"가 S22 #4·S23 #1·#4·S24 #3 에서 어미 섞임('셔')으로 걸려 다시 만들다 기본 대사로 넘어갔다. 고쳐서 살린다. run.py 의 같은 이름과 같다
        private static readonly Regex EuSyeo = new Regex("([가-힣])으셔", RegexOptions.CultureInvariant);
        private static readonly Regex RuHonorific = new Regex("([가-힣])르(셨|셔|신|실)", RegexOptions.CultureInvariant);
        private static readonly Regex WooHonorific = new Regex("([가-힣])우(셨|셔|신|실)", RegexOptions.CultureInvariant);

        private static string RuHonorificOf(Match m)
        {
            if (m.Value == "어르신")
            {
                return m.Value;
            }
            char stem = m.Groups[1].Value[0];
            string end = m.Groups[2].Value;
            if (end == "신" || end == "실")
            {
                return stem + (end == "신" ? "른" : "를");
            }
            // 받침 없는 글자는 ㄹ 받침을 붙인다(부→불), 이미 받침이 있으면 그대로(들르셔→들러)
            char withRieul = (stem - 0xAC00) % 28 != 0 ? stem : (char)(stem + 8);
            return withRieul + (end == "셨" ? "렀" : "러");
        }

        private static string WooHonorificOf(Match m)
        {
            string end = m.Groups[2].Value;
            return m.Groups[1].Value + (end == "셨" ? "웠" : end == "셔" ? "워" : end == "신" ? "운" : "울");
        }

        private static string DropHonorific(string line)
        {
            line = Make.Replace(line, m => m.Groups[1].Value == "셨" ? "만들었" : m.Groups[1].Value == "셔" ? "만들어" : m.Groups[1].Value == "신" ? "만든" : "만들");
            line = EunSyeot.Replace(line, m => m.Groups[1].Value + (TakesA(m.Groups[1].Value[0]) ? "았" : "었"));
            line = EuSyeo.Replace(line, m => m.Groups[1].Value + (TakesA(m.Groups[1].Value[0]) ? "아" : "어"));
            line = RuHonorific.Replace(line, RuHonorificOf);
            line = WooHonorific.Replace(line, WooHonorificOf);
            foreach (string[] pair in HonorificWords)
            {
                if (line.IndexOf(pair[0], StringComparison.Ordinal) < 0)
                {
                    continue; // 없는 낱말은 정규식을 쓰지 않는다 (성능)
                }
                if (EatHonorifics.Contains(pair[0]))
                {
                    line = Cached("(?<![가-힣])" + Regex.Escape(pair[0]) + (pair[0].EndsWith("신") ? "(?=[\\s.!?…~,]|$)" : "")).Replace(line, pair[1]);
                }
                else if (pair[0].EndsWith("신"))
                {
                    // '-신'(주신·하신)은 낱말 끝일 때만 — '안녕하신가'·'주신다'의 '신'을 건드리지 않는다
                    line = Cached(Regex.Escape(pair[0]) + "(?=[\\s.!?…~,]|$)").Replace(line, pair[1]);
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
            return NormalizeSpeech(line, label, name, ownThings, nickname, visitDays, null);
        }

        // 플레이어를 부를 때는 친밀도에 따라 '발굴자'나 닉네임 둘 중 하나로만 부른다. 모델이 "친구야"·"손님"·틀린 쪽 호칭으로 부르면 거르지 않고 맞는 쪽으로 바꾼다
        // (거르면 다시 만드는 1.5~2초가 든다). 부르는 말(뒤에 쉼표·느낌표·물음표가 오는 것)만 바꾸고 "친구가 왔네"·"내 친구야"처럼 문장 안의 말은 건드리지 않는다.
        // 아·야는 닉네임으로 바꿀 때만 받침에 맞게 붙인다. run.py 의 unify_player_call 과 같다
        // '온'은 "처음 온 친구야!"처럼 부르는 말이 아니라 서술하는 말이다 ("처음 온 발굴자!"로 바뀌었다, 2026-11-16 시험 S05 #1)
        private static readonly string[] CallPrefixSkip = { "내", "우리", "오랜", "새", "좋은", "단골", "온" };

        public static string UnifyPlayerCall(string line, string callName, string nickname)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(callName))
            {
                return line;
            }
            var words = new List<string> { NpcAddress.Stranger, "친구", "손님" };
            if (!string.IsNullOrEmpty(nickname))
            {
                words.Add(nickname);
            }
            var alternatives = new List<string>();
            foreach (string word in words)
            {
                alternatives.Add(Regex.Escape(word));
            }
            var rx = new Regex("(?<![가-힣])(" + string.Join("|", alternatives.ToArray()) + ")(아|야)?(?=\\s*[,!?~…])");
            return rx.Replace(line, m =>
            {
                if (m.Groups[1].Value == callName)
                {
                    return m.Value;
                }
                string before = line.Substring(0, m.Index).TrimEnd();
                foreach (string skip in CallPrefixSkip)
                {
                    if (before.EndsWith(skip, StringComparison.Ordinal))
                    {
                        return m.Value;
                    }
                }
                if (callName == NpcAddress.Stranger || !m.Groups[2].Success)
                {
                    return callName;
                }
                char last = callName[callName.Length - 1];
                bool batchim = last >= '가' && last <= '힣' && (last - 0xAC00) % 28 != 0;
                return callName + (batchim ? "아" : "야");
            });
        }

        // 플레이어를 가리키는 '너·네가·너를·너한테·네 덕분에' 같은 2인칭은 쓰지 않고, 친밀도 0~2단계면 '발굴자', 3단계 이상이면 닉네임(callName)으로 말한다 (2026-11-17).
        // 조사는 받침에 맞춘다: 네가→발굴자가 / 도윤이, 너는→발굴자는 / 도윤은, 너를→발굴자를 / 도윤을, 너랑→발굴자랑 / 도윤이랑, 네 덕분에→발굴자 덕분에.
        // '너무'·'너머'처럼 낱말의 일부인 것과 대답 "네."("네?", 문장 첫머리 "네 알겠어")는 건드리지 않는다. 자기 것을 '네'라고 한 말은 앞서 FixOwnPossessive 가 '내'로 고쳤다.
        // callName 이 없으면 바꾸지 않는다. run.py 의 player_pronouns_to_name 과 같다
        private static readonly Regex PlayerPronoun = new Regex(
                "(?<![가-힣])(?:"
                + "(?<ge>네게)(?![가-힣])"
                + "|(?<so>당신|그대|[너네니])(?<sp>가|는|은|를|을|도|한테서|한테|에게서|에게|이랑|이|랑|과|와|처럼|보다|까지|부터|만|밖에|라서|라고|의)(?![가-힣])"
                + "|(?<neun>넌)(?![가-힣])|(?<reul>널)(?![가-힣])"
                + "|(?<bare>너(?=[\\s,.!?…~]|$)|[네니](?=\\s+[가-힣]))"
                + ")", RegexOptions.CultureInvariant);

        // 조사: {받침 없는 이름 뒤, 받침 있는 이름 뒤}
        private static string PlayerParticle(string particle, bool batchim)
        {
            switch (particle)
            {
                case "가": case "이": return batchim ? "이" : "가";
                case "는": case "은": return batchim ? "은" : "는";
                case "를": case "을": return batchim ? "을" : "를";
                case "이랑": case "랑": return batchim ? "이랑" : "랑";
                case "과": case "와": return batchim ? "과" : "와";
                case "라서": return batchim ? "이라서" : "라서";
                case "라고": return batchim ? "이라고" : "라고";
                default: return particle;
            }
        }

        // 문장 첫머리의 "네" 뒤에 이런 낱말이 오면 대답이 아니라 '너의'다
        private static readonly string[] PossessedWords = { "덕분", "손길", "생각", "마음", "말", "도움", "관심", "정성", "얼굴", "이름", "목소리" };

        private static bool StartsWithAny(string text, string[] words)
        {
            foreach (string word in words)
            {
                if (text.StartsWith(word, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // 모델이 읽는 글(상황·기억·상대 줄)의 '주인'을 호칭(발굴자·닉네임)으로 바꾼다. 2026-11-17 21:09 시험: "지금 상대: 주인"·"주인이 안부를 물었다"를 읽은 모델이 하인처럼 높임을 쓰고
        // ("주인님을 뵙게 되어 반갑습니다", 말투다듬음 58%) "주인을 마주하니"처럼 상대를 남 얘기하듯 말했다. 조사는 받침에 맞춘다(주인이→발굴자가·도윤이). run.py 의 owner_to_call 과 같다
        private static readonly Regex OwnerWord = new Regex("(?<![가-힣])주인(?:님)?(께서|께|에게|한테|이랑|이|가|은|는|을|를|의|도|과|와|랑)?(?![가-힣])", RegexOptions.CultureInvariant);

        public static string OwnerToCall(string text, string callName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(callName))
            {
                return text;
            }
            char last = callName[callName.Length - 1];
            bool batchim = last >= '가' && last <= '힣' && (last - 0xAC00) % 28 != 0;
            return OwnerWord.Replace(text, m =>
            {
                string particle = m.Groups[1].Value;
                if (particle.Length == 0)
                {
                    return callName;
                }
                switch (particle)
                {
                    case "께서": case "이": case "가": return callName + (batchim ? "이" : "가");
                    case "은": case "는": return callName + (batchim ? "은" : "는");
                    case "을": case "를": return callName + (batchim ? "을" : "를");
                    case "이랑": case "랑": return callName + (batchim ? "이랑" : "랑");
                    case "과": case "와": return callName + (batchim ? "과" : "와");
                    case "께": return callName + "에게";
                    default: return callName + particle;
                }
            });
        }

        /// <summary>
        /// 호칭에 붙은 높임 조사를 반말 조사로: "발굴자께서"→"발굴자가", "도윤님께서"→"도윤이", "…께"→"…한테". 2026-11-17 21:27 시험 64건 중 2건이 어미 섞임('께서')으로 다시 만들어 5초대가 됐다.
        /// run.py 의 fix_call_honorific 와 같다.
        /// </summary>
        public static string FixCallHonorific(string line, string callName)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(callName))
            {
                return line;
            }
            char last = callName[callName.Length - 1];
            bool batchim = last >= '가' && last <= '힣' && (last - 0xAC00) % 28 != 0;
            string escaped = Regex.Escape(callName);
            line = Regex.Replace(line, escaped + "(?:님)?께서", callName + (batchim ? "이" : "가"), RegexOptions.CultureInvariant);
            return Regex.Replace(line, escaped + "(?:님)?께(?![가-힣])", callName + "한테", RegexOptions.CultureInvariant);
        }

        // 플레이어에게 플레이어를 3인칭으로 말하지 않는다 (2026-11-17 22:27 시험 뒤: "오랜만에 도윤을 찾아줘서"). 주어·목적어·여격은 말이 이어질 때 이름으로 바꾸지 않고 지운다.
        // 줄일 수 없는 꼴(너랑·너처럼·너도…)과 부르는 말은 이름으로 바꾼다. "네 덕분에"는 "덕분에"로. run.py 의 player_pronouns_to_name 과 같다
        private static readonly string[] PlayerDropParticles = { "가", "이", "는", "은", "를", "을", "한테서", "한테", "에게서", "에게", "의" };
        private static readonly Regex FollowedByWords = new Regex("^\\s+[가-힣]", RegexOptions.CultureInvariant);

        // 꾸밈말(관형형) 뒤의 플레이어 말은 지우지 않는다. "처음 온 너를 만나니 기쁘다"에서 '너를'을 지우면 꾸밈말만 남아 "처음 온 만나니 기쁘다"가 됐다
        // (2026-11-18 20:22 시험 S05 #1). 지우지 않은 말은 이름으로 바뀌어("처음 온 발굴자를") 출력 검사 PlayerThirdPerson 이 거르고 다시 만든다.
        // 꾸밈말: 받침 ㄴ·ㄹ로 끝나는 앞 낱말("온"·"만난"·"반가운"·"보고 싶던"·"올"). 조사('나는'·'에선'…)와 줄인 말('난'·'널'·'그건'), ㄴ·ㄹ로 끝나는 부사·때 낱말은 뺀다 ('만난'의 '난'은 꾸밈말).
        // '-은·-는'은 끝 글자로는 조사('나는')와 꾸밈말('잘 아는 너를')을 가를 수 없다 → 앞말이 대명사·때 낱말·흔한 사람 낱말(TopicStems)일 때만 조사로 본다 (CodeRabbit 리뷰).
        // run.py 의 _after_modifier 와 같다
        private static readonly HashSet<char> NonModifierLast = new HashSet<char>("을를엔선만란론");
        private static readonly HashSet<string> TopicStems = new HashSet<string>
        {
            "나", "너", "저", "우리", "그", "이", "그것", "이것", "저것", "그거", "이거", "오늘", "내일", "지금", "요즘", "이번", "이제", "사실", "실",
            "여기", "거기", "저기", "처음", "다음", "정말", "진짜", "친구", "도깨비", "발굴자", "주인", "손님", "사람"
        };
        // ㄴ·ㄹ 받침으로 끝나는 부사·때 낱말과 줄인 말 ("사실 너를 기다렸어"는 예전처럼 '너를'을 지운다 — CodeRabbit 리뷰)
        private static readonly HashSet<string> NonModifierWords = new HashSet<string> { "오늘", "내일", "정말", "참말", "늘", "얼른", "한번", "이번", "우선", "일단", "잘", "그만",
                "사실", "제일", "매일", "종일", "잠깐", "물론", "조만간",
                "난", "넌", "날", "널", "건", "걸", "그건", "이건", "저건", "그걸", "이걸", "땐", "그땐", "이젠" };
        private static readonly Regex TrailingHangulWord = new Regex("([가-힣]+)\\s+$", RegexOptions.CultureInvariant);
        // "너를 위한 거야"·"도윤을 위해 준비했어"도 지우면 "위한 거야"만 남는다
        private static readonly Regex ForWhom = new Regex("^\\s+위[해한하]", RegexOptions.CultureInvariant);

        /// <summary>line 의 index 자리 바로 앞 낱말이 꾸밈말(관형형)인지 ("처음 온 너를"의 '너를' 자리 → true, "오늘 너를" → false).</summary>
        public static bool AfterModifier(string line, int index)
        {
            if (string.IsNullOrEmpty(line) || index <= 0 || index > line.Length)
            {
                return false;
            }
            Match m = TrailingHangulWord.Match(line.Substring(0, index));
            if (!m.Success)
            {
                return false;
            }
            string word = m.Groups[1].Value;
            char last = word[word.Length - 1];
            int final = (last - 0xAC00) % 28;
            if ((final != 4 && final != 8) || NonModifierLast.Contains(last) || NonModifierWords.Contains(word))
            {
                return false;
            }
            return !((last == '은' || last == '는') && TopicStems.Contains(word.Substring(0, word.Length - 1)));
        }

        public static string PlayerPronounsToName(string line, string callName)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(callName))
            {
                return line;
            }
            char last = callName[callName.Length - 1];
            bool batchim = last >= '가' && last <= '힣' && (last - 0xAC00) % 28 != 0;
            bool dropped = false;
            string result = PlayerPronoun.Replace(line, m =>
            {
                string following = line.Substring(m.Index + m.Length);
                bool canDrop = FollowedByWords.IsMatch(following) && !ForWhom.IsMatch(following) && !AfterModifier(line, m.Index);
                if (m.Groups["ge"].Success)
                {
                    if (canDrop)
                    {
                        dropped = true;
                        return "";
                    }
                    return callName + "한테";
                }
                if (m.Groups["so"].Success)
                {
                    string sp = m.Groups["sp"].Value;
                    if (Array.IndexOf(PlayerDropParticles, sp) >= 0 && canDrop)
                    {
                        dropped = true;
                        return "";
                    }
                    return callName + PlayerParticle(sp, batchim);
                }
                if (m.Groups["neun"].Success)
                {
                    if (canDrop)
                    {
                        dropped = true;
                        return "";
                    }
                    return callName + PlayerParticle("는", batchim);
                }
                if (m.Groups["reul"].Success)
                {
                    if (canDrop)
                    {
                        dropped = true;
                        return "";
                    }
                    return callName + PlayerParticle("를", batchim);
                }
                string word = m.Groups["bare"].Value;
                if (word != "너")
                {
                    string before = line.Substring(0, m.Index).TrimEnd();
                    string after = line.Substring(m.Index + m.Length).TrimStart();
                    if ((before.Length == 0 || ".!?…~".IndexOf(before[before.Length - 1]) >= 0) && !StartsWithAny(after, PossessedWords))
                    {
                        return word; // 문장 첫머리의 "네 알겠어"는 대답이다. "네 덕분에"·"네 손길이"는 '너의'라서 바꾼다
                    }
                    if (StartsWithAny(after, PossessedWords))
                    {
                        dropped = true;
                        return ""; // "네 덕분에" → "덕분에"
                    }
                }
                return callName;
            });
            return dropped ? Regex.Replace(result, "\\s{2,}", " ").Trim() : result;
        }

        /// <summary>
        /// 대사에 플레이어 이름이 주어·목적어·여격으로 들어가 남 얘기처럼 들리면 그 말을 지운다 ("오랜만에 도윤을 찾아줘서"→"오랜만에 찾아줘서"). 부르는 말("도윤아"·"도윤이,")은 두고,
        /// 이어지는 말이 있을 때만 지운다. 꾸밈말 뒤("처음 온 도윤을")는 지우지 않는다(AfterModifier). 지우면 3글자도 안 남으면 그대로 둔다. run.py 의 drop_player_reference 와 같다.
        /// </summary>
        public static string DropPlayerReference(string line, string callName)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(callName))
            {
                return line;
            }
            string c = Regex.Escape(callName);
            // 꾸밈말 뒤("처음 온 도윤을 만나니")는 지우면 꾸밈말만 남는다 → 두고 PlayerThirdPerson 이 거른다
            if (line.IndexOf(callName, StringComparison.Ordinal) < 0)
            {
                return line; // 호칭이 없으면 정규식을 쓰지 않는다 (성능)
            }
            string result = Cached("(?<![가-힣])" + c + "(?:을|를|이|가|은|는|한테서|한테|에게서|에게)\\s+(?=[가-힣])(?!위[해한하])")
                    .Replace(line, m => AfterModifier(line, m.Index) ? m.Value : "");
            result = Cached("(?<![가-힣])" + c + "\\s+(?=덕분)").Replace(result, "");
            return Core(result).Length >= 3 ? result : line;
        }

        /// <summary>
        /// 고친 뒤에도 남은, 플레이어 이름이 남 얘기처럼 쓰인 말("도윤을"·"발굴자에게"·"처음 온 발굴자가 반가워"). 없으면 null. run.py 의 player_third_person 과 같다.
        /// 주어('이·가')는 뒤에 말이 이어질 때만 본다 — "도윤이,"·"도윤이야"처럼 부르는 말은 통과한다 (꾸밈말 뒤라 지우지 않고 남긴 주어를 거른다, CodeRabbit 리뷰).
        /// </summary>
        public static string PlayerThirdPerson(string line, string callName)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(callName))
            {
                return null;
            }
            if (line.IndexOf(callName, StringComparison.Ordinal) < 0)
            {
                return null;
            }
            Match found = Cached("(?<![가-힣])" + Regex.Escape(callName)
                    + "(?:(?:을|를|은|는|한테서|한테|에게서|에게|의)(?![가-힣])|(?:이|가)(?=\\s+[가-힣]))").Match(line);
            return found.Success ? found.Value : null;
        }

        /// <summary>
        /// addressName 을 주면(ReactionRequest.AddressName) 플레이어를 부르는 말을 그 하나로 맞춘다 (UnifyPlayerCall).
        /// </summary>
        public static string NormalizeSpeech(string line, string label, string name, IList<string> ownThings, string nickname, string visitDays, string addressName)
        {
            return NormalizeSpeech(line, label, name, ownThings, nickname, visitDays, addressName, false);
        }

        /// <summary>
        /// noRecord 가 true 면(NoRecordApplies) 기록 없는 주인 장면의 "오랜만이야"·"또 왔구나"를 고친다 (FixNoRecord).
        /// </summary>
        public static string NormalizeSpeech(string line, string label, string name, IList<string> ownThings, string nickname, string visitDays, string addressName, bool noRecord)
        {
            return NormalizeSpeech(line, label, name, ownThings, nickname, visitDays, addressName, noRecord, 0);
        }

        /// <summary>noRecordVariant 는 '오랜만'을 바꿔 넣는 말(NoRecordGreetings)의 번호다.</summary>
        public static string NormalizeSpeech(string line, string label, string name, IList<string> ownThings, string nickname, string visitDays, string addressName, bool noRecord,
                                             int noRecordVariant)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            line = FixCallHonorific(line, addressName); // '도윤님께서'를 StripNim 이 '도윤한테서'로 만들기 전에
            line = StripNim(line, nickname);
            if (noRecord)
            {
                line = FixNoRecord(line, noRecordVariant);
            }
            line = FixLongAbsence(line, visitDays);
            line = StripAddress(GuestToFriend(FixBodyState(FixOwnPossessive(line, ownThings))), name);
            // 플레이어에게 플레이어를 남 얘기하듯 말하지 않는다 (너를·도윤을 → 지움). 지운 뒤 "오랜만에 찾아줘서"가 드러나므로 기록 고침을 한 번 더 한다
            line = DropPlayerReference(PlayerPronounsToName(line, addressName), addressName);
            if (noRecord)
            {
                line = FixNoRecord(line, noRecordVariant);
            }
            line = FixLongAbsence(line, visitDays);
            if (line.Length == 0)
            {
                return line;
            }
            // 말투 어미를 고치다 새로 생긴 이름("향산이예요"→"향산이야")을 한 번 더 지운다
            // "덕분에, 관리해 줘서 고마워."처럼 첫 낱말 뒤에 쉼표를 찍으면 어색하다 (첫 낱말을 정해 준 뒤 생겼다)
            line = DropPlayerReference(PlayerPronounsToName(UnifyPlayerCall(FixStatementQuestion(CollapseDoubleVerb(FixStarterAdverb(StarterComma.Replace(StripTrailingInterjection(DropRepeatedWord(DropRepeatedLead(GuestToFriend(StripAddress(FixCareGiver(FixStoryTeller(FixThanksOverlap(FixTypos(ApplySpeech(line, label))))), name))))), "$1 "), addressName))), addressName, nickname), addressName), addressName);
            // 말줄임을 합치면 '?'가 어미 바로 뒤로 온다 ("기분이 좋구나...?"→"기분이 좋구나?"). 이때 평서 어미의 '?'를 한 번 더 고친다 (CodeRabbit 리뷰)
            return FixStatementQuestion(CollapsePunctuation(line));
        }

        // 연달아 붙은 문장 부호. 호칭·자기 이름을 지운 자리("반가워. 주인님!"→"반가워.!")나 '?'를 '.'로 고친 자리("왔구나?!"→"왔구나.!")에 생긴다 (2026-11-18 시험)
        private static readonly Regex PunctuationRun = new Regex("[.,!?~…]{2,}", RegexOptions.CultureInvariant);

        /// <summary>
        /// 연달아 붙은 문장 부호를 하나만 남긴다 ("반가워.!"→"반가워.", "하늘아.~"→"하늘아."). 앞의 끝 부호를 남기고, 쉼표와 끝 부호가 붙었으면 끝 부호를 남긴다("고마워,!"→"고마워!").
        /// 말줄임('…', 점 둘 이상)으로 시작하면 '…' 하나로 바꾸되, 뒤에 문장 끝 부호(?·!·~)가 있으면 그 부호를 남긴다("그래..."→"그래…", "고마워,..."→"고마워…",
        /// "별일 없었어...?"→"별일 없었어?"). run.py 의 collapse_punctuation 과 같다.
        /// </summary>
        public static string CollapsePunctuation(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            return PunctuationRun.Replace(line, m =>
            {
                string marks = m.Value.Replace(",", "");
                if (marks.Length == 0)
                {
                    return ",";
                }
                if (marks[0] == '…' || (marks[0] == '.' && marks.Length > 1 && (marks[1] == '.' || marks[1] == '…')))
                {
                    // 말줄임 뒤의 문장 끝 부호(?·!·~)는 남긴다 ("별일 없었어...?"→"별일 없었어?"). '…'는 문장 끝이 아니라서 '…'로 바꾸면
                    // 토큰 한도에서 끊긴 대사가 끝난 문장을 잃고 TooLong 이 된다 (CodeRabbit 리뷰)
                    int strong = marks.IndexOfAny(new[] { '?', '!', '~' });
                    return strong >= 0 ? marks.Substring(strong, 1) : "…";
                }
                return marks.Substring(0, 1);
            });
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
                    line = FixAwkward(DropHonorific(ImnidaToBanmal(PastSeupnidaToBanmal(line))));
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

        // 주어(이·가·께서)가 부탁하는 말의 주어일 때는 "네가"로 바꾸지 않고 지운다 ("주인님께서 닦아 주시면"→"네가 닦아 주면"이 아니라 "닦아 주면").
        // "네가 닦아줘"는 너에게 시키는 말처럼 들려 무례하고(2026-11-16 시험 S12 #1·#3·#4), 부탁은 누구에게 하는지 이미 알아서 주어가 필요 없다.
        // 고마움("주인이 관리해 줘서 고마워")은 "네가 관리해 줘서"로 자연스러워 그대로 바꾼다 (줘서 는 부탁이 아니다). run.py 의 REQUEST_SUBJECT_RE 와 같다
        private static readonly Regex RequestSubject = new Regex(
                "(?:닦|깨|돌봐|돌보|도와|씻|문지|정돈|관리|살펴|만져)[^,.!?…~]*?(?:줘(?!서)|주면|주시면|주라|줄래|줄 수|주세요|주길|주렴|달라)",
                RegexOptions.CultureInvariant);

        // "닦아줘 줘"처럼 같은 끝말을 띄어 되풀이한 것은 하나만 남긴다 (2026-11-16 시험 S12 #1). run.py 의 collapse_double_verb 와 같다
        private static readonly Regex DoubleVerb = new Regex("(\\S+(줘|봐|해))\\s+\\2(?![가-힣])", RegexOptions.CultureInvariant);

        // 묻는 말이 아닌데 '?'로 끝낸 것을 '.'로 고친다 ("오늘 아침부터 바쁘네?"·"나를 부르네?" — 2026-11-16 22:33 시험 S01 #3·#4). 감탄·서술 끝맺음(~네·~군·~구나·~더라) 뒤의 '?'만 건드리고,
        // 묻는 말("왔어?"·"볼래?"·"있지?")은 그대로 둔다. run.py 의 fix_statement_question 과 같다
        private static readonly Regex StatementQuestion = new Regex("(?<=[가-힣])(네|군|구나|로구나|더라)\\?", RegexOptions.CultureInvariant);

        // 프롬프트의 사실 칸 이름 '몸 상태'를 옮긴 "몸 상태가 선명해졌어"는 자기 몸을 남이 재듯 말한 것이다(2026-11-16 시험 S04 #4). "몸이 선명해졌어"로 고친다.
        // run.py 의 fix_body_state 와 같다
        private static readonly Regex BodyState = new Regex("몸\\s?상태(가|는|를|도)?", RegexOptions.CultureInvariant);

        public static string FixBodyState(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            return BodyState.Replace(line, m =>
            {
                string particle = m.Groups[1].Value;
                return "몸" + (particle == "가" ? "이" : particle == "는" ? "은" : particle == "를" ? "을" : particle);
            });
        }

        public static string FixStatementQuestion(string line)
        {
            return string.IsNullOrEmpty(line) ? line : StatementQuestion.Replace(line, "$1.");
        }

        public static string CollapseDoubleVerb(string line)
        {
            return string.IsNullOrEmpty(line) ? line : DoubleVerb.Replace(line, "$1");
        }

        /// <summary>
        /// 지운 호칭 앞에 쉼표가 있었고 뒤에 말이 이어지면 쉼표를 남긴다 ("예쁘네, 주인님 덕분에" → "예쁘네, 덕분에"). 쉼표까지 지우면 "예쁘네 덕분에"처럼 마디가 붙어
        /// 어색했다(2026-11-16 시험 S09 #3·S14 #1). 호칭이 문장 끝에 있으면("고마워, 주인님!") 쉼표도 함께 지운다. run.py 의 _kept_comma 와 같다.
        /// </summary>
        private static string KeptComma(Match m, string source)
        {
            if (m.Groups[1].Value.IndexOf(',') < 0)
            {
                return "";
            }
            string following = source.Substring(m.Index + m.Length).TrimStart();
            return following.Length > 0 && following[0] >= '가' && following[0] <= '힣' ? "," : "";
        }

        private static string AddressReplacement(Match m, string source)
        {
            string pronoun;
            string particle = m.Groups[3].Value;
            if (m.Groups[2].Value.StartsWith("주인", StringComparison.Ordinal) && (particle == "이" || particle == "가" || particle == "께서"))
            {
                string after = source.Substring(m.Index + m.Length);
                int stop = after.IndexOfAny(new[] { ',', '.', '!', '?', '…', '~' });
                if (RequestSubject.IsMatch(stop >= 0 ? after.Substring(0, stop) : after))
                {
                    return KeptComma(m, source);
                }
            }
            if (m.Groups[2].Value.StartsWith("주인", StringComparison.Ordinal) && AddressPronouns.TryGetValue(particle, out pronoun))
            {
                bool subject = particle == "이" || particle == "가" || particle == "은" || particle == "는" || particle == "께서";
                if (!subject)
                {
                    string rest = source.Substring(m.Index + m.Length);
                    int end = rest.IndexOfAny(new[] { '.', '!', '?', '…', '~' });
                    if (CareOrRequest.IsMatch(end >= 0 ? rest.Substring(0, end) : rest))
                    {
                        return KeptComma(m, source);
                    }
                }
                string comma = KeptComma(m, source);
                return (comma.Length > 0 ? comma + " " : m.Groups[1].Length > 0 ? " " : "") + pronoun;
            }
            return KeptComma(m, source);
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
                // 대사에 그 낱말이 없으면 정규식을 쓰지 않는다 — 낱말마다 정규식을 만들면 다듬기 한 번에 수십 개를 해석했다 (성능 리뷰 2026-11-19)
                if (line.IndexOf(pair[0], StringComparison.Ordinal) < 0)
                {
                    continue;
                }
                string word = pair[1];
                line = Cached(Regex.Escape(pair[0]) + WordEnd).Replace(line, m => word);
            }
            return line;
        }

        // 그때그때 이어 붙인 패턴(호칭·자기 것·낱말 목록)의 정규식을 기억해 둔다. 정적 Regex 메서드의 기억은 15개뿐이라, 다듬기가 패턴을 수십 개 쓰면 매번 다시 해석했다.
        // 패턴은 낱말 목록·호칭·도깨비마다 정해져 수가 많지 않다. 그래도 넘치면(1,024개) 비우고 다시 쌓는다
        private static readonly Dictionary<string, Regex> RegexCache = new Dictionary<string, Regex>();

        internal static Regex Cached(string pattern)
        {
            lock (RegexCache)
            {
                Regex rx;
                if (!RegexCache.TryGetValue(pattern, out rx))
                {
                    if (RegexCache.Count >= 1024)
                    {
                        RegexCache.Clear();
                    }
                    rx = new Regex(pattern, RegexOptions.CultureInvariant);
                    RegexCache[pattern] = rx;
                }
                return rx;
            }
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

        /// <summary>
        /// '오늘도'는 되풀이를 뜻한다. 부탁("…16일 전이라서, 오늘도 향로를 닦아 줘")·기록 없는 인사("오늘도 찾아와 줘서")에는 '오늘은'이 맞다 (2026-11-17 21:27 시험).
        /// run.py 의 fix_today_also 와 같다.
        /// </summary>
        public static string FixTodayAlso(string line)
        {
            return string.IsNullOrEmpty(line) ? line : line.Replace("오늘도", "오늘은");
        }

        private static readonly Regex DiscoveryEndingRegex = new Regex("(?:는구나|로구나|구나|군)(?:[.!?…~]|$)", RegexOptions.CultureInvariant);

        /// <summary>
        /// 자기 이야기(감사·안부·기억)의 문장이 '~구나·~는구나·~군'(남의 일을 알아챈 어미)으로 끝나면 그 문장. 인사("왔구나")는 상대를 보고 하는 말이라 괜찮다.
        /// "덕분에 기쁨이 넘쳐서 폴짝폴짝 뛰는구나!"처럼 자기를 남 얘기하듯 말한 것을 거른다 (2026-11-17 21:45 시험 S08 #4). run.py 의 discovery_ending 과 같다.
        /// </summary>
        public static string DiscoveryEnding(string line, string actionId)
        {
            if (actionId != NpcActions.ThankCalm && actionId != NpcActions.ThankJoyful && actionId != NpcActions.AnswerStatus && actionId != NpcActions.RecallTalk)
            {
                return null;
            }
            foreach (string sentence in Sentences(line))
            {
                if (DiscoveryEndingRegex.IsMatch(sentence))
                {
                    return sentence;
                }
            }
            return null;
        }

        private static readonly string[] StateWords = { "흐려", "흐릿", "뿌예", "뿌옜", "봉인" };

        /// <summary>
        /// 흐려진 안부에서 상태("흐려졌어")와 며칠 전("9일 전")을 함께 말했는지. 사실은 하나만 말한다 (PromptAssembler.AskVariant). run.py 의 mixed_ask_facts 와 같다.
        /// </summary>
        public static bool MixedAskFacts(string line)
        {
            string core = Core(line);
            foreach (string word in StateWords)
            {
                if (core.Contains(word))
                {
                    return DayRefs(line, false).Count > 0;
                }
            }
            return false;
        }

        /// <summary>
        /// 대사를 상황에 맞는 한 문장으로 줄인다 (2026-11-17 20:39 시험: 두 문장을 쓰며 길어져 반복·시간 초과·어색한 이음이 늘었다).
        /// ask(흐려진 안부)이면 닦아 달라는 말이 든 문장을 남긴다. 그 문장이 너무 짧으면(부호를 뺀 글자 8 미만) 첫 문장과 함께 둔다. 부탁하는 문장이 없으면 그대로 둔다(부탁 없음으로 거른다).
        /// 부탁이 아니면 고마움('고마'·'감사')이 든 문장, 없으면 첫 문장을 남기되 그것이 5글자 미만이면 이웃 문장까지 둔다. run.py 의 keep_one_sentence 와 같다.
        /// </summary>
        public static string KeepOneSentence(string line, bool ask)
        {
            List<string> sentences = Sentences(line);
            if (sentences.Count <= 1)
            {
                return line;
            }
            if (ask)
            {
                for (int i = 0; i < sentences.Count; i++)
                {
                    if (HasAsk(sentences[i]))
                    {
                        // "마지막 관리가 9일 전이었어. 닦아 줄래?"처럼 며칠 전 사실이 앞 문장에 있으면 그 문장과 부탁 문장 둘을 남긴다 (사실을 잃지 않게)
                        string fact = null;
                        for (int j = 0; j < i && fact == null; j++)
                        {
                            if (DayRefs(sentences[j], false).Count > 0)
                            {
                                fact = sentences[j];
                            }
                        }
                        if (fact != null && DayRefs(sentences[i], false).Count == 0)
                        {
                            return fact + " " + sentences[i];
                        }
                        return Core(sentences[i]).Length >= 8 || i == 0 ? sentences[i] : sentences[0] + " " + sentences[i];
                    }
                }
                return line;
            }
            // 고마움이 든 문장이 있으면 그것을 남긴다 ("몸이 반짝반짝해져서 기뻐! 주인이 관리해 줘서 고마워!"에서 감사를 잃지 않게). 없으면 첫 문장
            int pick = 0;
            for (int i = 0; i < sentences.Count; i++)
            {
                if (sentences[i].Contains("고마") || sentences[i].Contains("감사"))
                {
                    pick = i;
                    break;
                }
            }
            string chosen = sentences[pick];
            if (Core(chosen).Length >= 5)
            {
                return chosen;
            }
            return pick > 0 ? sentences[0] + " " + chosen : chosen + " " + sentences[1];
        }

        // 흐려진 안부에서 모델이 쓰는 부분은 "(말머리) 닦아 줄래?" 꼴이어야 한다 (2026-11-17 22:27 시험). 아니면 코드가 부탁 문장을 채운다. run.py 의 CLEAN_ASK_RE·is_clean_ask 와 같다
        private static readonly Regex CleanAsk = new Regex(
                "^(?:(?:살살|괜찮으면|시간 되면|조심조심|이왕이면|혹시|천천히|조금|잠깐만|잠깐|한 ?번|좀|꼭|부디|가끔|오늘은)[ ,]*){0,3}"
                + "(?:(?:내 몸을|내 몸|나를|날|몸을|나) ?)?(?:닦아|깨워) ?(?:줘|줄래|줄 ?수 ?있어|줄 ?수 ?있을까|주면 ?좋겠어|주라|주렴)[.?!~]*$",
                RegexOptions.CultureInvariant);

        public static bool IsCleanAsk(string part)
        {
            return !string.IsNullOrEmpty(part) && CleanAsk.IsMatch(part.Trim());
        }

        // 인사(탭·손님 맞이)에 붙은 지어낸 사정·캐묻기 ("뭘 찾으러 왔어?"·"무슨 일인지 궁금하네"·"오늘은 좀 일찍 왔구나"). 그런 마디는 지우고 반가움 마디만 남긴다. run.py 의 fix_greeting_tail 과 같다
        // '찾으러'는 "뭘 찾으러 왔어?"(캐묻기)만 거른다. "날 찾으러 왔구나"·"나를 찾으러 왔네"는 상황 문장("발굴자가 너를 보러 찾아왔다")과 같은 반가움이라 두고 쓴다.
        // 날·나를은 따로 선 낱말일 때만 뺀다 — 앞이 글의 처음이거나 한글이 아니어야 한다("전날 찾으러"·"하나를 찾으러"는 나가 아니다). run.py 의 PROBE_RE 와 같은 식
        // (2026-11-25: 기기·PC 시험에서 인사 실패(WrongHistory '찾으러')가 판마다 한두 건 — 모두 '날/나를 찾으러')
        private static readonly Regex Probe = new Regex("무슨|뭘|뭐 |뭐하|왜|궁금|길래|일찍|늦게|(?<![^가-힣]날 )(?<!^날 )(?<![^가-힣]날)(?<!^날)(?<![^가-힣]나를 )(?<!^나를 )(?<![^가-힣]나를)(?<!^나를)찾으러|하러|급하게|보고 ?싶었|오늘은 좀|처음 온 친구야|어떤 일", RegexOptions.CultureInvariant);
        private const string ClauseEnd = ",，.!?…~";

        public static List<string> Clauses(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            int i = 0;
            while (i < line.Length)
            {
                current.Append(line[i]);
                i++;
                if (ClauseEnd.IndexOf(current[current.Length - 1]) >= 0)
                {
                    while (i < line.Length && (ClauseEnd.IndexOf(line[i]) >= 0 || line[i] == ' '))
                    {
                        if (line[i] != ' ')
                        {
                            current.Append(line[i]);
                        }
                        i++;
                    }
                    result.Add(current.ToString().Trim());
                    current.Length = 0;
                }
            }
            if (current.ToString().Trim().Length > 0)
            {
                result.Add(current.ToString().Trim());
            }
            return result;
        }

        public static string FixGreetingTail(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            List<string> parts = Clauses(line);
            var keep = new List<string>();
            foreach (string part in parts)
            {
                if (FindProbe(part) == null)
                {
                    keep.Add(part);
                }
            }
            if (keep.Count > 0 && keep.Count < parts.Count && Core(string.Join("", keep.ToArray())).Length >= 3)
            {
                string text = Regex.Replace(string.Join(" ", keep.ToArray()), "[,，]+$", "");
                line = Regex.IsMatch(text, "[.!?…~]$") ? text : text + ".";
            }
            if (Regex.IsMatch(line, "\\?+$") && FindProbe(line) == null && Regex.IsMatch(line, "반가|환영|어서|안녕|왔"))
            {
                line = Regex.Replace(line, "\\?+$", "!");
            }
            return line;
        }

        /// <summary>인사 행동의 대사에 지어낸 사정·캐묻기가 남았으면 그 말, 없으면 null. run.py 의 greeting_probe 와 같다.</summary>
        public static string GreetingProbe(string line, string action)
        {
            if (string.IsNullOrEmpty(line) || (action != NpcActions.Greet && action != NpcActions.WanderGreet && action != NpcActions.WanderRecall))
            {
                return null;
            }
            return FindProbe(line);
        }

        // 지어낸 사정·캐묻기 말 (없으면 null). 가로 띄어쓰기가 여럿 이어지면 하나로 보고 찾는다 — "나를  찾으러"도 "나를 찾으러"처럼 반가움으로 본다
        // (Probe 의 앞말 조건은 띄어쓰기 하나까지만 본다). run.py 의 find_probe 와 같다
        private static string FindProbe(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            Match found = Probe.Match(Regex.Replace(text, "[ \\t]+", " "));
            return found.Success ? found.Value : null;
        }

        /// <summary>
        /// 플레이어를 목적어로 '부르는' 말 ("발굴자를 불러서 반가워", "도윤을 부르니") — 주인이 도깨비를 부른 상황에서 도깨비가 플레이어를 부른 것처럼 뒤바뀐 말. 없으면 null.
        /// run.py 의 reversed_call 과 같다.
        /// </summary>
        public static string ReversedCall(string line, string callName)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(callName))
            {
                return null;
            }
            Match found = Cached(Regex.Escape(callName) + "(?:을|를)\\s?(?:불러|부르|불렀|부른|부를|만나러|보러|찾으러)").Match(line);
            return found.Success ? found.Value : null;
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
        /// 띄어쓰기·문장 부호를 빼고 ① 똑같거나 ② 글자 쌍이 SimilarPairs 이상 겹치거나 ③ 두 대사가 함께 가진 가장 긴 구절이 긴 쪽의 ContainedShare 이상이거나
        /// ④ 앞쪽이 SimilarOpening 이상 겹치면 같은 대사다.
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
