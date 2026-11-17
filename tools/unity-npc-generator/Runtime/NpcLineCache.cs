using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 미리 만들어 둔 대사 보관함. 기기에서 SLM 은 한 번 만드는 데 몇 초가 걸려서, 요청이 오기 전에 한가할 때 대사를 백그라운드로 만들어 두고
    /// 요청이 오면 바로 꺼내 쓴다 (NpcRequestManager.Prefetch). 같은 상황(도깨비·선택지·상황 문장·사실·기억·호칭)에서 만든 대사만 꺼내 쓴다.
    ///
    /// <para>중복: 보관함의 대사끼리, 그리고 이미 띄운 최근 대사와 <b>90% 이상 겹치면</b>(NpcLineChecks.Similar) 넣지도 꺼내지도 않는다.
    /// 상황마다 MaxPerKey 개까지, 오래된 것(MaxAge)은 버린다 — 시간대가 바뀌었는데 "좋은 아침"이 나가지 않게.</para>
    ///
    /// <para>스레드: 잠금으로 보호한다. 만든 쪽(백그라운드)과 꺼내 쓰는 쪽(요청 처리)이 달라도 된다.</para>
    /// </summary>
    public sealed class NpcLineCache
    {
        private sealed class Entry
        {
            public ReactionResult Result;
            public DateTime At;
        }

        private readonly object gate = new object();
        private readonly Dictionary<string, List<Entry>> entries = new Dictionary<string, List<Entry>>();

        public NpcLineCache()
        {
            MaxPerKey = 3;
            MaxAge = TimeSpan.FromMinutes(30);
            Now = () => DateTime.UtcNow;
        }

        /// <summary>상황마다 보관하는 최대 대사 수. 기본 3.</summary>
        public int MaxPerKey { get; set; }

        /// <summary>이 시간이 지난 대사는 버린다. 기본 30분 (시간대·날짜가 바뀔 수 있다). 시험에서 시계를 바꿀 때는 Now 를 바꾼다.</summary>
        public TimeSpan MaxAge { get; set; }

        /// <summary>지금 시각. 시험에서 바꿔 쓴다.</summary>
        public Func<DateTime> Now { get; set; }

        /// <summary>
        /// 같은 상황을 가리키는 열쇠. 도깨비·상대·선택지·후보 행동·상황 문장·사실(각성 단계·이야기 번호는 빼고)·기억·닉네임·친밀 여부가 같으면 같은 열쇠다.
        /// 요청 ID·곁들일 거리 번호(Variation)·최근 대사는 넣지 않는다 — 그래야 미리 만들 때와 요청 때의 열쇠가 같다.
        /// </summary>
        public static string KeyOf(ReactionRequest request)
        {
            var builder = new StringBuilder();
            builder.Append(request.RelicId).Append('|').Append(request.Mode).Append('|').Append(request.Input).Append('|').Append(request.Situation);
            if (request.Candidates != null)
            {
                foreach (ActionCandidate candidate in request.Candidates)
                {
                    if (candidate != null)
                    {
                        builder.Append("|c:").Append(candidate.ActionId).Append('=')
                               .Append(string.Join(",", (candidate.TargetIds ?? new List<string>()).ToArray()));
                    }
                }
            }
            var facts = new List<string>();
            if (request.Facts != null)
            {
                foreach (Fact fact in request.Facts)
                {
                    if (fact != null && fact.Key != NpcFactKeys.Awakening && fact.Key != NpcFactKeys.StoryIndex)
                    {
                        facts.Add(fact.Key + "=" + fact.Value);
                    }
                }
            }
            facts.Sort(StringComparer.Ordinal);
            foreach (string fact in facts)
            {
                builder.Append("|f:").Append(fact);
            }
            if (request.Memory != null)
            {
                foreach (MemoryEntry memory in request.Memory)
                {
                    if (memory != null)
                    {
                        builder.Append("|m:").Append(memory.DaysAgo).Append(':').Append(memory.ActionId).Append(':').Append(memory.Summary);
                    }
                }
            }
            builder.Append("|n:").Append(request.Nickname).Append(':').Append(request.Familiar);
            return builder.ToString();
        }

        private static readonly Regex SituationTime = new Regex("(?<=주인이 )(새벽|아침|오전|낮|저녁|밤)(?=에 )", RegexOptions.CultureInvariant);
        private static readonly Regex SituationCount = new Regex("[0-9]+(?=번 온 친구)", RegexOptions.CultureInvariant);

        /// <summary>
        /// 칸 있는 대사(템플릿)용 열쇠. KeyOf 와 같되 <b>서버가 넘겨주는 값에 달린 것을 뺀다</b> — 시간대(상황 문장의 "낮에"·사실 time_of_day), 닉네임·친밀도(호칭),
        /// 방문 횟수(상황 문장의 "7번"·사실 visit_count_30d·visit_days). 그래서 서버 값을 모르는 채로 미리 만든 대사를, 서버 값이 온 뒤의 요청이 같은 열쇠로 찾는다.
        /// 선명도·마지막 관리·기억처럼 어떤 말을 할지 정하는 사실은 그대로 넣는다.
        /// </summary>
        public static string TemplateKeyOf(ReactionRequest request)
        {
            var builder = new StringBuilder("T|");
            string situation = SituationCount.Replace(SituationTime.Replace(request.Situation ?? "", NpcLineSlots.TimeSlot), NpcLineSlots.CountSlot);
            builder.Append(request.RelicId).Append('|').Append(request.Mode).Append('|').Append(request.Input).Append('|').Append(situation);
            if (request.Candidates != null)
            {
                foreach (ActionCandidate candidate in request.Candidates)
                {
                    if (candidate != null)
                    {
                        builder.Append("|c:").Append(candidate.ActionId).Append('=')
                               .Append(string.Join(",", (candidate.TargetIds ?? new List<string>()).ToArray()));
                    }
                }
            }
            var facts = new List<string>();
            if (request.Facts != null)
            {
                foreach (Fact fact in request.Facts)
                {
                    if (fact != null && fact.Key != NpcFactKeys.Awakening && fact.Key != NpcFactKeys.StoryIndex && fact.Key != NpcFactKeys.TimeOfDay
                            && fact.Key != NpcFactKeys.VisitCount30d && fact.Key != NpcFactKeys.VisitDays)
                    {
                        facts.Add(fact.Key + "=" + fact.Value);
                    }
                }
            }
            facts.Sort(StringComparer.Ordinal);
            foreach (string fact in facts)
            {
                builder.Append("|f:").Append(fact);
            }
            if (request.Memory != null)
            {
                foreach (MemoryEntry memory in request.Memory)
                {
                    if (memory != null)
                    {
                        builder.Append("|m:").Append(memory.DaysAgo).Append(':').Append(memory.ActionId).Append(':').Append(memory.Summary);
                    }
                }
            }
            return builder.ToString();
        }

        /// <summary>그 상황으로 미리 만든 칸 있는 대사 수.</summary>
        public int TemplateCount(ReactionRequest request)
        {
            lock (gate)
            {
                return Live(TemplateKeyOf(request)).Count;
            }
        }

        /// <summary>
        /// 그 상황으로 보관된 칸 있는 대사 글. 요청 자신의 값(견본)을 칸에 넣은 글이다 — 미리 만들 때 새로 만든 대사와 겹치는지 비교하는 데 쓴다.
        /// 넣을 값이 없는 칸은 그대로 둔다.
        /// </summary>
        public List<string> TemplateLinesFor(ReactionRequest request)
        {
            SlotValues sample = SlotValues.FromRequest(request);
            lock (gate)
            {
                var lines = new List<string>();
                foreach (Entry entry in Live(TemplateKeyOf(request)))
                {
                    lines.Add(NpcLineSlots.Fill(entry.Result.Line, sample) ?? entry.Result.Line);
                }
                return lines;
            }
        }

        /// <summary>
        /// 서버 값을 모르는 채로(또는 견본 값으로) 만든 대사를 칸 있는 대사로 바꿔 넣는다. 요청 자신의 값(시간대·호칭·방문 횟수)이 견본이다.
        /// 칸으로 만들 수 없는 대사(며칠 전·"아침 일찍" 등, NpcLineSlots 참고)·꽉 찬 보관함·이미 있는 대사와 90% 이상 겹치는 대사는 넣지 않고 false.
        /// </summary>
        public bool AddTemplate(ReactionRequest request, ReactionResult result)
        {
            if (request == null || result == null || !result.IsSuccess || string.IsNullOrEmpty(result.Line))
            {
                return false;
            }
            string slotted = NpcLineSlots.Slotify(result.Line, SlotValues.FromRequest(request));
            if (slotted == null)
            {
                return false;
            }
            string key = TemplateKeyOf(request);
            lock (gate)
            {
                List<Entry> live = Live(key);
                if (live.Count >= MaxPerKey)
                {
                    return false;
                }
                foreach (Entry entry in live)
                {
                    if (NpcLineChecks.Similar(slotted, entry.Result.Line) != null)
                    {
                        return false;
                    }
                }
                live.Add(new Entry
                {
                    Result = ReactionResult.Success(result.RequestId, result.ActionId, result.TargetId, slotted, result.Source, result.Note),
                    At = Now()
                });
                return true;
            }
        }

        /// <summary>
        /// 그 상황으로 미리 만든 칸 있는 대사 하나를, 요청의 서버 값(시간대·호칭·방문 횟수)을 칸에 넣어 완성해 꺼낸다. 칸에 넣을 값이 없거나 넣은 뒤 시간대·호칭이
        /// 규칙과 어긋나면 그 대사는 두고 다음 것을 본다. recent 와 90% 이상 겹치는 것은 버린다. 없으면 null. 결과의 Note 에 채운 칸이 적힌다.
        /// </summary>
        public ReactionResult TryTakeTemplate(ReactionRequest request, IEnumerable<string> recent)
        {
            if (request == null)
            {
                return null;
            }
            SlotValues actual = SlotValues.FromRequest(request);
            string key = TemplateKeyOf(request);
            lock (gate)
            {
                List<Entry> live = Live(key);
                for (int i = 0; i < live.Count; i++)
                {
                    ReactionResult stored = live[i].Result;
                    string filled = NpcLineSlots.Fill(stored.Line, actual);
                    if (filled == null
                            || NpcLineChecks.TimeContradiction(filled, actual.TimeWord) != null
                            || NpcLineChecks.AddressContradiction(filled, request.Nickname, request.Familiar) != null)
                    {
                        continue; // 이 요청에는 못 쓰는 대사 — 다른 요청을 위해 둔다
                    }
                    if (NpcLineChecks.FindSimilar(filled, recent).HasValue)
                    {
                        live.RemoveAt(i);
                        i--;
                        continue;
                    }
                    live.RemoveAt(i);
                    string names = NpcLineSlots.SlotNames(stored.Line);
                    return ReactionResult.Success(stored.RequestId, stored.ActionId, stored.TargetId, filled, stored.Source,
                            names.Length > 0 ? "칸 채움: " + names : stored.Note);
                }
                return null;
            }
        }

        /// <summary>그 상황으로 보관된 대사 수 (오래된 것은 먼저 버린다).</summary>
        public int Count(ReactionRequest request)
        {
            lock (gate)
            {
                return Live(KeyOf(request)).Count;
            }
        }

        /// <summary>보관된 대사 전부.</summary>
        public int TotalCount
        {
            get
            {
                lock (gate)
                {
                    int total = 0;
                    foreach (string key in new List<string>(entries.Keys))
                    {
                        total += Live(key).Count;
                    }
                    return total;
                }
            }
        }

        /// <summary>그 상황으로 보관된 대사 글 (미리 만들 때 서로 겹치지 않게 비교하는 데 쓴다).</summary>
        public List<string> LinesFor(ReactionRequest request)
        {
            lock (gate)
            {
                var lines = new List<string>();
                foreach (Entry entry in Live(KeyOf(request)))
                {
                    lines.Add(entry.Result.Line);
                }
                return lines;
            }
        }

        /// <summary>
        /// 만든 대사를 넣는다. 성공한 결과이고, 보관함이 차지 않았고, 이미 있는 대사와 90% 이상 겹치지 않을 때만 넣고 true.
        /// </summary>
        public bool Add(ReactionRequest request, ReactionResult result)
        {
            if (request == null || result == null || !result.IsSuccess || string.IsNullOrEmpty(result.Line))
            {
                return false;
            }
            string key = KeyOf(request);
            lock (gate)
            {
                List<Entry> live = Live(key);
                if (live.Count >= MaxPerKey)
                {
                    return false;
                }
                foreach (Entry entry in live)
                {
                    if (NpcLineChecks.Similar(result.Line, entry.Result.Line) != null)
                    {
                        return false;
                    }
                }
                live.Add(new Entry { Result = result, At = Now() });
                return true;
            }
        }

        /// <summary>
        /// 그 상황으로 미리 만든 대사 하나를 꺼낸다 (가장 먼저 만든 것부터). recent(이미 띄운 최근 대사)와 90% 이상 겹치는 것은 건너뛰고 버린다.
        /// 없으면 null.
        /// </summary>
        public ReactionResult TryTake(ReactionRequest request, IEnumerable<string> recent)
        {
            if (request == null)
            {
                return null;
            }
            string key = KeyOf(request);
            lock (gate)
            {
                List<Entry> live = Live(key);
                for (int i = 0; i < live.Count; i++)
                {
                    ReactionResult result = live[i].Result;
                    live.RemoveAt(i);
                    i--;
                    if (!NpcLineChecks.FindSimilar(result.Line, recent).HasValue)
                    {
                        return result;
                    }
                }
                return null;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                entries.Clear();
            }
        }

        // 잠금 안에서 부른다. 시간이 지난 것을 버리고 남은 목록을 돌려준다
        private List<Entry> Live(string key)
        {
            List<Entry> list;
            if (!entries.TryGetValue(key, out list))
            {
                list = new List<Entry>();
                entries[key] = list;
            }
            DateTime limit = Now() - MaxAge;
            list.RemoveAll(entry => entry.At < limit);
            return list;
        }
    }
}
