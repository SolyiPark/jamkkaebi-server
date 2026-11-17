using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Jamkkaebi.Npc
{
    /// <summary>서버가 넘겨주는 값 중 대사에 들어가는 것. 미리 만들 때는 견본, 요청 때는 실제 값이다.</summary>
    public sealed class SlotValues
    {
        /// <summary>서버 시각의 시간대(새벽·아침·오전·낮·저녁·밤). 탭 인사에만 있다.</summary>
        public string TimeWord { get; set; }

        /// <summary>플레이어를 부르는 말 — 친밀도에 따라 '발굴자' 또는 닉네임(ReactionRequest.AddressName).</summary>
        public string Address { get; set; }

        /// <summary>최근 30일 방문한 날 수. 손님 장면에만 있다.</summary>
        public int? VisitCount { get; set; }

        public static SlotValues FromRequest(ReactionRequest request)
        {
            int count;
            string visits = request.FindFact(NpcFactKeys.VisitCount30d);
            return new SlotValues
            {
                TimeWord = request.FindFact(NpcFactKeys.TimeOfDay),
                Address = request.AddressName,
                VisitCount = visits != null && int.TryParse(visits, out count) ? count : (int?)null
            };
        }
    }

    /// <summary>
    /// 미리 만든 대사 중 서버 값(시간대·호칭·방문 횟수)에 달린 말을 <b>변수 칸</b>으로 남겨 두고, 서버 값이 오자마자 칸에 넣어 대사를 완성한다.
    /// 서버 값을 기다리지 않고 미리 만들 수 있고, 시간대가 바뀌거나 닉네임·방문 횟수가 달라져도 같은 대사를 다시 쓸 수 있다.
    ///
    /// <para>칸: <c>{시간대}</c>(아침에→저녁에), <c>{호칭}</c>(발굴자→닉네임), <c>{호칭아}</c>(부르는 말 "발굴자"/"도윤아"), <c>{횟수}</c>("7번").</para>
    ///
    /// <para>칸으로 만들 수 없는 대사는 보관하지 않는다(Slotify 가 null): 며칠 전·어제처럼 기록 날짜에 달린 말, "아침 일찍"처럼 시간대 뒤에 조사가 없는 말,
    /// 호칭 뒤에 조사가 바로 붙은 말("도윤이"), "세 번"처럼 한글 숫자. 이런 대사는 서버 값이 바뀌면 틀릴 수 있어서다.</para>
    /// </summary>
    public static class NpcLineSlots
    {
        public const string TimeSlot = "{시간대}";
        public const string AddressSlot = "{호칭}";
        public const string AddressVocativeSlot = "{호칭아}";
        public const string CountSlot = "{횟수}";

        private static readonly Regex DayRef = new Regex("어제|그제|그저께|엊그제|이틀 ?전|사흘 ?전|나흘 ?전|닷새 ?전|[0-9]+ ?일 ?전|하루 ?전|지난 ?주|저번 ?주|[0-9]+ ?주", RegexOptions.CultureInvariant);
        private static readonly Regex TimeWord = new Regex("(?<![가-힣])(새벽|아침|오전|낮|저녁|밤)", RegexOptions.CultureInvariant);
        private static readonly Regex KoreanCount = new Regex("(한|두|세|네|다섯|여섯|일곱|여덟|아홉|열) ?번", RegexOptions.CultureInvariant);
        private static readonly Regex AnyCount = new Regex("([0-9]+) ?번", RegexOptions.CultureInvariant);

        // 시간대 뒤에 와도 되는 조사 (여섯 낱말 모두 받침으로 끝나 같은 조사를 쓴다). 긴 것부터
        private static readonly string[] TimeParticles = { "에는", "이라서", "이라고", "이구나", "부터", "마다", "까지", "이라", "이야", "이네", "이군", "에", "엔", "도", "이" };

        private static readonly Dictionary<string, string[]> AllowedTimeWords = new Dictionary<string, string[]>
        {
            { "새벽", new[] { "새벽", "밤" } }, { "아침", new[] { "아침", "새벽" } }, { "오전", new[] { "아침", "낮" } },
            { "낮", new[] { "낮" } }, { "저녁", new[] { "저녁" } }, { "밤", new[] { "밤", "저녁" } }
        };

        public static bool HasSlots(string line)
        {
            return !string.IsNullOrEmpty(line) && line.IndexOf('{') >= 0
                    && (line.Contains(TimeSlot) || line.Contains(AddressSlot) || line.Contains(AddressVocativeSlot) || line.Contains(CountSlot));
        }

        /// <summary>대사에 들어 있는 칸의 이름 ("시간대·호칭" 꼴). 없으면 빈 글.</summary>
        public static string SlotNames(string line)
        {
            var names = new List<string>();
            if (line != null && line.Contains(TimeSlot)) names.Add("시간대");
            if (line != null && (line.Contains(AddressSlot) || line.Contains(AddressVocativeSlot))) names.Add("호칭");
            if (line != null && line.Contains(CountSlot)) names.Add("횟수");
            return string.Join("·", names.ToArray());
        }

        /// <summary>
        /// 만든 대사에서 견본 값(sample)에 달린 말을 칸으로 바꾼다. 칸으로 만들 수 없는 대사면 null (보관하지 않는다).
        /// 서버 값에 달린 말이 없는 대사는 그대로 돌려준다 (칸 없음).
        /// </summary>
        public static string Slotify(string line, SlotValues sample)
        {
            if (string.IsNullOrEmpty(line) || sample == null)
            {
                return null;
            }
            if (DayRef.IsMatch(line) || KoreanCount.IsMatch(line))
            {
                return null;
            }
            string text = line;

            // 1) 시간대
            string[] allowed;
            bool known = sample.TimeWord != null && AllowedTimeWords.TryGetValue(sample.TimeWord.Trim(), out allowed);
            allowed = known ? AllowedTimeWords[sample.TimeWord.Trim()] : new string[0];
            var slotAt = new List<int>();
            var slotLen = new List<int>();
            foreach (Match m in TimeWord.Matches(text))
            {
                int after = m.Index + m.Length;
                int particle = ParticleLength(text, after);
                int end = after + particle;
                bool compound = particle == 0 && after < text.Length && IsHangul(text[after]);
                if (compound)
                {
                    continue; // 낮잠·밤바다·아침햇살처럼 다른 낱말의 일부
                }
                bool boundary = end >= text.Length || !IsHangul(text[end]);
                bool spaceThenWord = particle == 0 && after < text.Length && char.IsWhiteSpace(text[after]) && NextIsHangul(text, after);
                if (!boundary || spaceThenWord || Array.IndexOf(allowed, m.Groups[1].Value) < 0)
                {
                    return null; // "아침 일찍"처럼 조사가 없거나, 견본 시간대와 어긋나거나, 견본 시간대를 모른다
                }
                slotAt.Add(m.Index);
                slotLen.Add(m.Length);
            }
            for (int i = slotAt.Count - 1; i >= 0; i--)
            {
                text = text.Substring(0, slotAt[i]) + TimeSlot + text.Substring(slotAt[i] + slotLen[i]);
            }

            // 2) 호칭 (발굴자 또는 닉네임)
            if (!string.IsNullOrEmpty(sample.Address))
            {
                var address = new Regex("(?<![가-힣])" + Regex.Escape(sample.Address) + "(아|야)?", RegexOptions.CultureInvariant);
                bool unsafeUse = false;
                text = address.Replace(text, m =>
                {
                    int end = m.Index + m.Length;
                    if (end < text.Length && IsHangul(text[end]))
                    {
                        // 조사가 바로 붙은 호칭("도윤이")은 받침에 따라 조사가 달라져 칸으로 만들 수 없다. 단 '아·야'가 호칭 뒤 부르는 말이면 통과
                        unsafeUse = true;
                        return m.Value;
                    }
                    return m.Groups[1].Success ? AddressVocativeSlot : AddressSlot;
                });
                if (unsafeUse)
                {
                    return null;
                }
            }

            // 3) 방문 횟수 (숫자 + 번)
            foreach (Match m in AnyCount.Matches(text))
            {
                if (!sample.VisitCount.HasValue || m.Groups[1].Value != sample.VisitCount.Value.ToString())
                {
                    return null; // 견본 횟수가 아닌 숫자는 서버 값이 아니다 — 지어낸 말일 수 있다
                }
            }
            if (sample.VisitCount.HasValue)
            {
                text = Regex.Replace(text, "(?<![0-9])" + sample.VisitCount.Value + "(?= ?번)", CountSlot, RegexOptions.CultureInvariant);
            }
            return text;
        }

        /// <summary>
        /// 칸에 실제 서버 값을 넣어 대사를 완성한다. 대사에 있는 칸에 넣을 값이 없으면 null (그 대사는 못 쓴다).
        /// </summary>
        public static string Fill(string template, SlotValues actual)
        {
            if (string.IsNullOrEmpty(template) || !HasSlots(template))
            {
                return template;
            }
            string text = template;
            if (text.Contains(TimeSlot))
            {
                if (actual == null || string.IsNullOrEmpty(actual.TimeWord))
                {
                    return null;
                }
                text = text.Replace(TimeSlot, actual.TimeWord.Trim());
            }
            if (text.Contains(AddressSlot) || text.Contains(AddressVocativeSlot))
            {
                if (actual == null || string.IsNullOrEmpty(actual.Address))
                {
                    return null;
                }
                text = text.Replace(AddressSlot, actual.Address);
                text = text.Replace(AddressVocativeSlot, actual.Address == NpcAddress.Stranger ? actual.Address : actual.Address + VocativeSuffix(actual.Address));
            }
            if (text.Contains(CountSlot))
            {
                if (actual == null || !actual.VisitCount.HasValue)
                {
                    return null;
                }
                text = text.Replace(CountSlot, actual.VisitCount.Value.ToString());
            }
            return text;
        }

        private static string VocativeSuffix(string word)
        {
            char last = word[word.Length - 1];
            bool batchim = last >= '가' && last <= '힣' && (last - 0xAC00) % 28 != 0;
            return batchim ? "아" : "야";
        }

        private static int ParticleLength(string text, int at)
        {
            foreach (string particle in TimeParticles)
            {
                if (string.CompareOrdinal(text, at, particle, 0, particle.Length) == 0)
                {
                    int end = at + particle.Length;
                    if (end >= text.Length || !IsHangul(text[end]))
                    {
                        return particle.Length;
                    }
                }
            }
            return 0;
        }

        private static bool NextIsHangul(string text, int whitespaceAt)
        {
            int i = whitespaceAt;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            return i < text.Length && IsHangul(text[i]);
        }

        private static bool IsHangul(char c)
        {
            return c >= '가' && c <= '힣';
        }
    }
}
