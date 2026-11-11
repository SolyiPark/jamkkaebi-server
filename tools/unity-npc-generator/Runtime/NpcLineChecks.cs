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
        public const double SimilarPairs = 0.6;

        /// <summary>짧은 대사의 이만큼이 긴 대사 안에 그대로 있으면 같은 대사다 ("안녕하시오, 계시오?" 와 "안녕하시오, 계시오? 주인님께서…").</summary>
        public const double ContainedShare = 0.6;

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
            new[] { "고맙습니다", "고마워" }, new[] { "감사합니다", "고마워" }, new[] { "감사해요", "고마워" }, new[] { "주셔서", "줘서" },
            new[] { "이에요", "이야" }, new[] { "에요", "야" }, new[] { "예요", "야" },
            new[] { "주세요", "줘" }, new[] { "하세요", "해" }, new[] { "보세요", "봐" }, new[] { "오세요", "와" }, new[] { "가세요", "가" },
            new[] { "죠", "지" }
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
        private static readonly Regex BanmalVocative = new Regex("(주인|손님)아(?=[\\s.!?…~,]|$)", RegexOptions.CultureInvariant);
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
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }
            switch (label)
            {
                case "반말":
                    line = ReplaceWords(line, BanmalWords);
                    line = BanmalVocative.Replace(line, m => m.Groups[1].Value == "주인" ? "주인님" : m.Groups[1].Value);
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

        /// <summary>감탄사만 있는 문장·마디인지 ("어흠.", "허허…", "앗,"). 비었어도 true.</summary>
        public static bool IsInterjection(string text)
        {
            string core = Core(text);
            return core.Length == 0 || Interjection.IsMatch(core);
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
