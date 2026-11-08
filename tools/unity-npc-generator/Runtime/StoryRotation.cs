using System.Collections.Generic;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 실화 하나를 "들려줬다"고 기록한 표. 반응을 띄우지 못했으면 StoryRotation.Return 으로 되돌린다.
    /// 보통은 직접 다루지 않고 NpcContext.Finish 가 처리한다.
    /// </summary>
    public sealed class StoryTicket
    {
        internal StoryTicket(int relicId, int index, long seq, long previousSeq, int previousPosition)
        {
            RelicId = relicId;
            Index = index;
            Seq = seq;
            PreviousSeq = previousSeq;
            PreviousPosition = previousPosition;
        }

        public int RelicId { get; private set; }

        /// <summary>고른 실화 번호 (0부터).</summary>
        public int Index { get; private set; }

        internal long Seq { get; private set; }

        // 이번에 고르기 전의 기록 (없었으면 -1). 되돌릴 때 그 자리로 돌려놓는다
        internal long PreviousSeq { get; private set; }
        internal int PreviousPosition { get; private set; }
    }

    /// <summary>
    /// 도깨비마다 실화 이야기를 돌려 가며 고른다. 다 들려줄 때까지 같은 이야기를 다시 고르지 않고,
    /// 다 들려준 뒤에는 가장 오래전에 들려준 것부터 고른다 (그래서 바로 앞 이야기를 연달아 고르는 일이 없다).
    ///
    /// <para>쓰는 법: 컨텍스트 구성기가 이야기 듣기 요청을 만들 때 Take 로 번호를 받아
    /// verified_story(그 실화 문장)와 story_index(번호)를 사실에 넣는다. SLM 이 실패해 규칙 기반 생성기로 넘어가도
    /// 같은 번호의 이야기 대사가 나온다. 어느 실화를 들려줄지는 게임이 정한다 — 작은 모델에게 여럿 중 안 한 것을
    /// 고르게 하면 자주 틀린다.</para>
    ///
    /// <para>되돌리기: 화면을 닫거나 다른 도깨비로 바꿔 반응이 버려지면 Return(ticket) 으로 그 실화를
    /// "안 들려줌"(고르기 전 자리)으로 되돌린다. 그사이 다른 요청이 고른 실화는 건드리지 않고,
    /// 같은 실화를 그 뒤에 다시 들려줬으면 되돌리지 않는다.</para>
    ///
    /// <para>앱을 껐다 켜도 이어 가려면 History 를 저장했다가 Restore 로 되돌린다.</para>
    /// </summary>
    public sealed class StoryRotation
    {
        private readonly object gate = new object();

        // 도깨비별로 들려준 기록. 앞이 오래된 것, 뒤가 최근 것. 번호마다 한 번만 들어 있다
        private readonly Dictionary<int, List<Entry>> told = new Dictionary<int, List<Entry>>();
        private long nextSeq;

        /// <summary>
        /// 다음에 들려줄 이야기 번호를 고르고, 들려준 것으로 기록한다. storyCount 가 0 이하면 -1.
        /// 되돌릴 일이 없는 곳(규칙 기반 생성기 등)에서 쓴다. 되돌릴 수 있어야 하면 Take.
        /// </summary>
        public int Next(int relicId, int storyCount)
        {
            StoryTicket ticket = Take(relicId, storyCount);
            return ticket != null ? ticket.Index : -1;
        }

        /// <summary>
        /// Next 와 같게 고르고 기록하되, 되돌릴 때 쓸 표를 돌려준다. storyCount 가 0 이하면 null.
        /// 아직 안 들려준 것이 있으면 그중 앞 번호, 다 들려줬으면 가장 오래전에 들려준 것.
        /// </summary>
        public StoryTicket Take(int relicId, int storyCount)
        {
            if (storyCount <= 0)
            {
                return null;
            }
            lock (gate)
            {
                List<Entry> history = HistoryOf(relicId);
                int pick = -1;
                for (int i = 0; i < storyCount && pick < 0; i++)
                {
                    if (Find(history, i) < 0)
                    {
                        pick = i;
                    }
                }
                if (pick < 0)
                {
                    foreach (Entry entry in history)
                    {
                        if (entry.Index < storyCount)
                        {
                            pick = entry.Index;
                            break;
                        }
                    }
                }
                if (pick < 0)
                {
                    pick = 0;
                }

                int position = Find(history, pick);
                long previousSeq = -1;
                if (position >= 0)
                {
                    previousSeq = history[position].Seq;
                    history.RemoveAt(position);
                }
                long seq = ++nextSeq;
                history.Add(new Entry(pick, seq));
                return new StoryTicket(relicId, pick, seq, previousSeq, position);
            }
        }

        /// <summary>
        /// 들려주지 못한 실화를 고르기 전 상태로 되돌린다. 되돌렸으면 true.
        /// 이미 되돌렸거나, 같은 실화를 그 뒤에 다시 들려줬으면 false (아무것도 바꾸지 않는다).
        /// </summary>
        public bool Return(StoryTicket ticket)
        {
            if (ticket == null)
            {
                return false;
            }
            lock (gate)
            {
                List<Entry> history = HistoryOf(ticket.RelicId);
                int position = -1;
                for (int i = 0; i < history.Count; i++)
                {
                    if (history[i].Seq == ticket.Seq)
                    {
                        position = i;
                        break;
                    }
                }
                if (position < 0)
                {
                    return false;
                }
                history.RemoveAt(position);
                if (ticket.PreviousSeq >= 0)
                {
                    // 전에 들려준 적이 있던 실화면 원래 자리(오래된 순서)로 돌려놓는다
                    history.Insert(System.Math.Min(ticket.PreviousPosition, history.Count), new Entry(ticket.Index, ticket.PreviousSeq));
                }
                return true;
            }
        }

        /// <summary>들려준 순서(오래된 것부터). 저장용 복사본이다.</summary>
        public List<int> History(int relicId)
        {
            lock (gate)
            {
                var indices = new List<int>();
                foreach (Entry entry in HistoryOf(relicId))
                {
                    indices.Add(entry.Index);
                }
                return indices;
            }
        }

        /// <summary>저장해 둔 History 로 되돌린다. 음수와 겹치는 번호는 버린다.</summary>
        public void Restore(int relicId, IEnumerable<int> history)
        {
            lock (gate)
            {
                var restored = new List<Entry>();
                if (history != null)
                {
                    foreach (int i in history)
                    {
                        if (i >= 0 && Find(restored, i) < 0)
                        {
                            restored.Add(new Entry(i, ++nextSeq));
                        }
                    }
                }
                told[relicId] = restored;
            }
        }

        private List<Entry> HistoryOf(int relicId)
        {
            List<Entry> history;
            if (!told.TryGetValue(relicId, out history))
            {
                history = new List<Entry>();
                told[relicId] = history;
            }
            return history;
        }

        private static int Find(List<Entry> history, int index)
        {
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Index == index)
                {
                    return i;
                }
            }
            return -1;
        }

        private struct Entry
        {
            public readonly int Index;
            public readonly long Seq;

            public Entry(int index, long seq)
            {
                Index = index;
                Seq = seq;
            }
        }
    }
}
