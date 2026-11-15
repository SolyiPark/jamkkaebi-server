using System;
using System.Collections.Generic;
using System.Text;

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
