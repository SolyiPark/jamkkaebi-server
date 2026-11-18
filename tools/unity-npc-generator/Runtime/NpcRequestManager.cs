using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>요청 관리자가 돌려주는 것. 띄울 반응이 있는지와, 왜 그렇게 됐는지.</summary>
    public sealed class NpcResponse
    {
        private NpcResponse()
        {
        }

        /// <summary>대사를 띄울지. false 면 아무것도 띄우지 않는다 (화면 종료·NPC 전환·새 요청으로 대체·후보 없음).</summary>
        public bool ShouldShow { get; private set; }

        /// <summary>띄울 반응. ShouldShow 일 때만 있다. Source 로 SLM 이 만든 것인지 기본 대사인지 안다.</summary>
        public ReactionResult Reaction { get; private set; }

        /// <summary>SLM 을 불렀을 때 그 결과 (실패 이유 기록용). 부르지 않았으면 null.</summary>
        public ReactionResult SlmAttempt { get; private set; }

        /// <summary>어떻게 처리됐는지 사람이 읽는 설명. 로그와 평가용이다. 예: "SLM 시간 초과(3000ms) → 기본 대사".</summary>
        public string Note { get; private set; }

        /// <summary>SLM 대신 기본 대사(규칙 기반)로 응답했는지.</summary>
        public bool UsedFallback
        {
            get { return ShouldShow && Reaction.Source == ReactionSource.Rule; }
        }

        internal static NpcResponse Show(ReactionResult reaction, ReactionResult slmAttempt, string note)
        {
            return new NpcResponse { ShouldShow = true, Reaction = reaction, SlmAttempt = slmAttempt, Note = note };
        }

        internal static NpcResponse Discard(ReactionResult slmAttempt, string note)
        {
            return new NpcResponse { ShouldShow = false, SlmAttempt = slmAttempt, Note = note };
        }
    }

    /// <summary>
    /// 요청 관리자. 반응 요청 한 건을 받아 SLM 으로 만들어 보고, 안 되면 규칙 기반 생성기의 기본 대사로 대신한다.
    ///
    /// <para><b>정해진 대사를 쓰는 경우</b>: 이야기 듣기는 SLM 을 부르지 않고 검수한 이야기 대사를 차례로 쓴다 (Note 가 FixedLinesNote).</para>
    ///
    /// <para><b>대사 없이 몸짓만 하는 경우</b>: 끌려갈 때(struggle_*)는 SLM 을 부르지 않고, 행동만 고른 반응을 돌려준다 (Note 가 SilentNote).
    /// <c>Reaction.Line</c> 이 빈 글이면 말풍선 없이 그 행동의 애니메이션만 재생한다.</para>
    ///
    /// <para><b>기본 대사로 대신하는 경우</b>: SLM 을 끈 기기(저사양), 모델을 아직 안 받았거나 안 올라옴, 시간 초과,
    /// 출력 검사 실패, 페르소나 없는 도깨비, 런타임 오류. 앞의 둘은 SLM 을 부르지도 않는다.</para>
    ///
    /// <para><b>시스템 프롬프트 미리 읽기</b>: 도깨비를 탭해 확대할 때 PrepareAsync 를 부르면, 그 도깨비의 시스템 메시지를
    /// 모델에 미리 읽혀 둔다. 다른 도깨비의 요청·준비가 오거나 Cancel·CancelAll 하면 멈춘다.</para>
    ///
    /// <para><b>아무것도 띄우지 않는 경우</b>: 부르는 쪽이 취소함(화면 종료), Cancel(relicId)(NPC 전환), CancelAll,
    /// 같은 도깨비에게 새 요청이 와서 옛 요청이 밀림, 후보가 없음. 늦게 끝난 옛 요청의 결과는 버린다.</para>
    ///
    /// <para><b>같은 대사 막기</b>: 시나리오(같은 도깨비의 같은 선택지)마다 이번 실행에서 띄운 대사 RecentLineCount(10)개를 기억해 요청의 RecentLines 에 넣는다.
    /// 출력 검사기는 그와 같은 대사를 거르고(SLM 생성기가 다시 만든다), 기본 대사도 그와 다른 것을 고른다.</para>
    ///
    /// <para>도깨비(relicId)마다 진행 중인 요청을 하나만 둔다. 손님 관람처럼 여러 도깨비가 동시에 말풍선을 띄우는 것은 된다.
    /// RequestId 는 시도마다 여기서 새로 붙인다. Unity 메인 스레드에서 await 하면 결과도 메인 스레드로 돌아온다.</para>
    /// </summary>
    public sealed class NpcRequestManager
    {
        /// <summary>정해진 대사(이야기)를 띄웠을 때의 Note. SLM 을 부르지 않았다.</summary>
        public const string FixedLinesNote = "이야기는 정해진 대사";

        /// <summary>대사 없이 몸짓만 한 경우(끌려갈 때)의 Note. SLM 을 부르지 않았고 Reaction.Line 은 빈 글이다 — 말풍선을 띄우지 않는다.</summary>
        public const string SilentNote = "끌려갈 때는 대사 없음";

        /// <summary>미리 만들어 둔 대사를 꺼내 쓴 경우의 Note. 그 요청에서는 SLM 을 부르지 않았다 (응답이 바로 뜬다).</summary>
        public const string PrefetchedNote = "미리 만든 대사";

        private readonly INpcReactionGenerator slm;
        private readonly INpcReactionGenerator rule;
        private readonly object gate = new object();
        private readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();

        // 시나리오(도깨비 + 선택지)마다 최근에 띄운 대사 (오래된 것부터). 같은 도깨비의 같은 상황에서 같은 말이 거듭되는 것을 막으려는 것이라
        // 관리하기 대사는 관리하기 대사끼리만 비교한다
        private readonly Dictionary<string, List<string>> shown = new Dictionary<string, List<string>>();

        // 미리 만들기: 보관함, 할 일 줄, 일꾼, 진행 중인 요청 수
        private readonly Queue<PrefetchJob> prefetchJobs = new Queue<PrefetchJob>();
        private CancellationTokenSource prefetchCts;
        private Task prefetchWorker = Task.FromResult(true);
        private bool prefetchRunning;
        private int activeRequests;

        // 미리 만든 대사를 남겨 두는 곳(LoadStore)과 그 버전. 쓰기는 한 번에 하나만, 바뀔 때마다 뒤에서 쓴다(SaveSoon)
        private INpcLineStore store;
        private string storeVersion;
        private readonly object storeWriteGate = new object();
        private int savePending;

        // 쓴 만큼 다시 만드는 선택지 (NpcContextBuilder.PrefetchTemplates 가 미리 만드는 것과 같다)
        private static readonly string[] RefillInputs = { NpcInputs.Tap, NpcInputs.AskStatus, NpcInputs.Care, NpcInputs.Visit };

        // 진행 중인 시스템 프롬프트 미리 읽기. 모델은 하나라 한 번에 하나만 둔다
        private Session preparing;
        private int preparingRelicId;

        /// <param name="slm">SLM 생성기. 모델을 쓰지 않는 빌드면 null.</param>
        /// <param name="rule">규칙 기반 생성기. 반드시 있어야 한다.</param>
        public NpcRequestManager(INpcReactionGenerator slm, INpcReactionGenerator rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException("rule");
            }
            this.slm = slm;
            this.rule = rule;
            SlmEnabled = slm != null;
            SlmTimeout = TimeSpan.FromSeconds(3);
            RecentLineCount = 10;
            Cache = new NpcLineCache();
            UsePrefetched = true;
            PrefetchTimeout = TimeSpan.FromSeconds(30);
            RefillTarget = request => request.Input == NpcInputs.Tap ? 2 : 1;
            // 꺼낼 때 지금 규칙의 출력 검사를 한 번 더 한다 (파일에 남겨 둔 대사는 그 뒤에 바뀐 규칙을 모르고 만들어졌을 수 있다)
            SlmReactionGenerator generator = slm as SlmReactionGenerator;
            if (generator != null)
            {
                Cache.Recheck = generator.Recheck;
            }
        }

        /// <summary>
        /// 미리 만든 대사를 꺼내 쓰거나(또는 보관함이 비어 그 자리에서 만들거나) 띄운 뒤, 그 상황의 칸 있는 대사를 <b>쓴 만큼</b> 뒤에서 다시 만들어 채우는지.
        /// 다음 번 같은 상황(정화·탭 인사·안부·손님 관람)은 이번에 뒤에서 만든 대사를 바로 꺼내 쓴다. 기본 false — 게임은 켠다(README "미리 만든 대사 파일").
        /// 채울 개수는 RefillTarget. 기억을 꺼내는 대화(지난 대화)는 기억이 바뀌어 다시 쓰지 않으므로 채우지 않는다.
        /// </summary>
        public bool RefillAfterUse { get; set; }

        /// <summary>
        /// 쓴 뒤 그 상황에 채워 둘 칸 있는 대사 수. 기본: 탭 인사 2(자주 해서 다시 만들기가 끝나기 전에 또 올 수 있다), 나머지 1(쓴 만큼 하나).
        /// 정화는 상태(선명·흐려짐·재봉인)마다 열쇠가 따로라, 쓴 상태의 대사 하나만 다시 만든다.
        /// </summary>
        public Func<ReactionRequest, int> RefillTarget { get; set; }

        /// <summary>LoadStore 로 붙인 저장소. 없으면 null.</summary>
        public INpcLineStore Store
        {
            get
            {
                lock (gate)
                {
                    return store;
                }
            }
        }

        /// <summary>
        /// 미리 만든 대사 파일을 붙이고 읽어 들인다 — 게임을 처음 켤 때(로딩 화면) 부른다. 읽은 칸 있는 대사 수를 돌려준다 (파일이 없거나 깨졌거나 버전이 다르면 0).
        /// 그 뒤로는 대사를 만들거나 꺼내 쓸 때마다 뒤에서 파일을 고쳐 쓴다. 버전(NpcLineStoreFormat.VersionOf)이 다르면 칸 있는 대사는 버리고(최근 띄운 대사는 되살린다)
        /// 다음 저장 때 새 버전으로 덮어쓴다. 읽은 뒤 PrefetchTemplate 을 부르면 파일에 있던 것은 다시 만들지 않고 모자란 것만 만든다.
        /// </summary>
        public int LoadStore(INpcLineStore lineStore, string version)
        {
            if (lineStore == null)
            {
                throw new ArgumentNullException("lineStore");
            }
            // 다 읽고 넣은 뒤에 저장소를 붙인다 — 먼저 붙이면 읽는 사이에 뒤에서 도는 저장(SaveSoon)이 빈 보관함으로 파일을 덮어쓸 수 있다
            string text;
            try
            {
                text = lineStore.Read();
            }
            catch (Exception)
            {
                text = null; // 못 읽는 파일 — 처음부터 만든다. 다음 저장이 덮어쓴다
            }
            List<NpcLineCache.StoredTemplate> templates;
            Dictionary<string, List<string>> recent;
            bool versionMatched;
            if (!NpcLineStoreFormat.TryRead(text, version ?? "", out templates, out recent, out versionMatched))
            {
                AttachStore(lineStore, version);
                return 0;
            }
            lock (gate)
            {
                foreach (KeyValuePair<string, List<string>> pair in recent)
                {
                    List<string> lines;
                    if (!shown.TryGetValue(pair.Key, out lines))
                    {
                        lines = new List<string>();
                        shown[pair.Key] = lines;
                    }
                    foreach (string line in pair.Value)
                    {
                        lines.Remove(line);
                        lines.Add(line);
                    }
                    while (lines.Count > Math.Max(0, RecentLineCount))
                    {
                        lines.RemoveAt(0);
                    }
                }
            }
            int imported = Cache.ImportTemplates(templates);
            AttachStore(lineStore, version);
            return imported;
        }

        private void AttachStore(INpcLineStore lineStore, string version)
        {
            lock (gate)
            {
                store = lineStore;
                storeVersion = version ?? "";
            }
        }

        /// <summary>지금 보관함의 칸 있는 대사와 최근 띄운 대사를 저장소에 쓴다. 저장소가 없거나 쓰지 못하면 false. 보통은 알아서 뒤에서 쓰고, 앱을 내릴 때 한 번 더 부른다.</summary>
        public bool SaveStore()
        {
            // 지금 내용을 찍는 것과 쓰는 것을 한 잠금 안에서 한다. 찍은 뒤 잠금을 잡으면 겹친 저장 둘 중 먼저 찍은(옛) 내용이 나중에 쓰여 새 내용을 덮을 수 있다
            lock (storeWriteGate)
            {
                INpcLineStore target;
                string version;
                var recent = new Dictionary<string, List<string>>();
                lock (gate)
                {
                    target = store;
                    version = storeVersion;
                    foreach (KeyValuePair<string, List<string>> pair in shown)
                    {
                        recent[pair.Key] = new List<string>(pair.Value);
                    }
                }
                if (target == null)
                {
                    return false;
                }
                string text = NpcLineStoreFormat.Write(version, Cache.ExportTemplates(), recent);
                try
                {
                    target.Write(text);
                    return true;
                }
                catch (Exception)
                {
                    return false; // 쓰지 못해도 요청 처리에는 영향이 없다. 다음 변경 때 다시 쓴다
                }
            }
        }

        // 바뀐 것을 뒤에서 쓴다. 이미 쓰려고 기다리는 것이 있으면 그것이 이번 변경까지 쓴다
        private void SaveSoon()
        {
            if (Store == null || Interlocked.Exchange(ref savePending, 1) == 1)
            {
                return;
            }
            Task.Run(() =>
            {
                Interlocked.Exchange(ref savePending, 0);
                SaveStore();
            });
        }

        // 쓴 만큼 다시 만든다 (RefillAfterUse). 이 요청을 견본으로 칸 있는 대사를 RefillTarget 개까지 채운다 — 요청의 서버 값은 칸으로 남는다
        private void Refill(ReactionRequest request)
        {
            if (!RefillAfterUse || !UsePrefetched || !SlmEnabled || slm == null || request == null || Array.IndexOf(RefillInputs, request.Input) < 0
                    || (request.Memory != null && request.Memory.Count > 0) || request.Candidates == null || NpcActions.SkipsModel(request.Candidates))
            {
                return;
            }
            int target = RefillTarget != null ? RefillTarget(request) : 1;
            if (target <= 0)
            {
                return;
            }
            var copy = new ReactionRequest
            {
                InteractionId = request.InteractionId,
                RelicId = request.RelicId,
                Mode = request.Mode,
                Input = request.Input,
                Situation = request.Situation,
                Candidates = request.Candidates,
                Facts = request.Facts != null ? new List<Fact>(request.Facts) : new List<Fact>(),
                Memory = new List<MemoryEntry>(),
                ContentVersion = request.ContentVersion,
                Variation = request.Variation + 1,   // 방금 쓴 곁들일 거리 다음 것으로
                Nickname = request.Nickname,
                Intimacy = request.Intimacy,
                RecentLines = request.RecentLines != null ? new List<string>(request.RecentLines) : new List<string>()
            };
            EnqueuePrefetch(copy, target, true);
        }

        /// <summary>미리 만들어 둔 대사 보관함 (Prefetch 가 채우고 요청이 꺼내 쓴다).</summary>
        public NpcLineCache Cache { get; private set; }

        /// <summary>미리 만들어 둔 대사가 있으면 SLM 을 부르지 않고 바로 꺼내 쓰는지. 기본 true.</summary>
        public bool UsePrefetched { get; set; }

        /// <summary>미리 만들 때 한 번 만드는 데 기다리는 최대 시간. 기본 30초 — 사용자가 기다리지 않는 시간이라 요청 때(SlmTimeout)보다 넉넉하다.</summary>
        public TimeSpan PrefetchTimeout { get; set; }

        /// <summary>
        /// 지금 진행 중인 미리 만들기가 끝날 때까지 기다리는 작업 (할 일이 없으면 이미 끝난 작업). 시험과 화면 종료에서 쓴다.
        /// </summary>
        public Task PrefetchCompletion
        {
            get
            {
                lock (gate)
                {
                    return prefetchWorker;
                }
            }
        }

        private sealed class PrefetchJob
        {
            public bool IsTemplate;   // 칸 있는 대사로 만든다 (PrefetchTemplate)
            public ReactionRequest Template;
            public int Count;
            public int NextVariation;
            public int Failures;
        }

        /// <summary>
        /// 시나리오(같은 도깨비의 같은 선택지)마다 기억해 두고 다시 쓰지 않을 최근 대사 수. 기본 10 — 최근 10개 중 하나와 90% 이상 겹치면
        /// 출력 검사가 거르고 SLM 생성기가 새로 만든다. 0 이면 기억하지 않는다.
        /// </summary>
        public int RecentLineCount { get; set; }

        /// <summary>
        /// SLM 을 쓸지. 실기기 측정으로 정한 사양 기준에 못 미치는 기기면 false 로 두어 처음부터 기본 대사만 쓴다.
        /// </summary>
        public bool SlmEnabled { get; set; }

        /// <summary>
        /// SLM 을 기다리는 시간. 넘으면 기본 대사로 대신한다. 기본 3초이고, 실기기의 첫 응답·전체 생성 시간을 재서 정한다.
        /// 모델 런타임이 취소에 늦게 반응해도 이 시간이 지나면 기다리지 않는다.
        /// </summary>
        public TimeSpan SlmTimeout { get; set; }

        /// <summary>
        /// 반응을 만든다. 예외를 던지지 않고(요청이 null 일 때만 ArgumentNullException) 늘 NpcResponse 를 돌려준다.
        /// ct 는 부르는 쪽의 취소(화면 종료 등)다 — 취소되면 기본 대사로 대신하지 않고 버린다.
        /// </summary>
        public Task<NpcResponse> RequestAsync(ReactionRequest request, CancellationToken ct = default(CancellationToken))
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            return Handle(request, ct);
        }

        private async Task<NpcResponse> Handle(ReactionRequest request, CancellationToken ct)
        {
            if (request.Candidates == null || request.Candidates.Count == 0)
            {
                return NpcResponse.Discard(null, "후보가 없어 띄울 반응이 없다");
            }
            if (string.IsNullOrEmpty(request.InteractionId))
            {
                request.InteractionId = NewId();
            }

            // 진행 중인 요청이 있는 동안에는 미리 만들기를 멈춘다 (모델은 하나라 요청이 먼저다). 만들던 것은 취소하고 요청이 끝나면 이어 만든다
            Interlocked.Increment(ref activeRequests);
            CancelCurrentPrefetch();
            Session session = Begin(request.RelicId, ct);
            try
            {
                if (session.Token.IsCancellationRequested)
                {
                    return NpcResponse.Discard(null, session.CancelNote());
                }
                AttachRecent(request);

                // 0) 미리 만들어 둔 대사가 있으면 바로 꺼내 쓴다 (같은 상황에서 만든 것, 최근 대사와 90% 이상 겹치지 않는 것)
                if (UsePrefetched && !NpcActions.SkipsModel(request.Candidates))
                {
                    ReactionResult ready = Cache.TryTake(request, request.RecentLines);
                    if (ready == null)
                    {
                        // 칸 있는 대사: 서버 값을 모르는 채로 미리 만들어 둔 것에 이 요청의 시간대·호칭·방문 횟수를 넣어 바로 완성한다
                        SlmReactionGenerator generator = slm as SlmReactionGenerator;
                        ready = Cache.TryTakeTemplate(request, request.RecentLines, generator != null ? generator.MaxLineLength(request.Input) : int.MaxValue);
                    }
                    if (ready != null)
                    {
                        request.RequestId = NewId();
                        ReactionResult served = ReactionResult.Success(request.RequestId, ready.ActionId, ready.TargetId, ready.Line, ReactionSource.Slm,
                                ready.Note);
                        Remember(request, served.Line);
                        Refill(request);
                        return NpcResponse.Show(served, null, PrefetchedNote);
                    }
                }

                // 1) SLM
                ReactionResult slmAttempt = null;
                string fallbackNote;
                if (NpcActions.OnlyFixedLines(request.Candidates))
                {
                    fallbackNote = FixedLinesNote;
                }
                else if (NpcActions.OnlySilent(request.Candidates))
                {
                    fallbackNote = SilentNote;
                }
                else if (!SlmEnabled || slm == null)
                {
                    fallbackNote = "SLM 꺼짐(기기 사양 또는 모델 없는 빌드) → 기본 대사";
                }
                else if (!slm.IsReady)
                {
                    fallbackNote = "모델이 아직 준비되지 않음 → 기본 대사";
                }
                else
                {
                    slmAttempt = await RunSlm(request, session).ConfigureAwait(false);
                    if (session.Token.IsCancellationRequested)
                    {
                        return NpcResponse.Discard(slmAttempt, session.CancelNote());
                    }
                    if (!session.TimedOut && slmAttempt.IsSuccess && slmAttempt.RequestId == request.RequestId)
                    {
                        Remember(request, slmAttempt.Line);
                        Refill(request);
                        return NpcResponse.Show(slmAttempt, slmAttempt, slmAttempt.Note != null ? "SLM (" + slmAttempt.Note + ")" : "SLM");
                    }
                    fallbackNote = session.TimedOut
                            ? "SLM 시간 초과(" + (int)SlmTimeout.TotalMilliseconds + "ms) → 기본 대사"
                            : "SLM 실패 " + slmAttempt.FailReason
                              + (slmAttempt.FailDetail != null ? " (" + slmAttempt.FailDetail + ")" : "") + " → 기본 대사";
                }

                // 2) 기본 대사
                request.RequestId = NewId();
                ReactionResult fallback;
                try
                {
                    fallback = await rule.GenerateAsync(request, session.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    fallback = ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Rule, e.Message);
                }
                if (session.Token.IsCancellationRequested)
                {
                    return NpcResponse.Discard(slmAttempt, session.CancelNote());
                }
                if (fallback.IsSuccess && fallback.RequestId == request.RequestId)
                {
                    Remember(request, fallback.Line);
                    Refill(request);
                    return NpcResponse.Show(fallback, slmAttempt, fallbackNote);
                }
                return NpcResponse.Discard(slmAttempt, "기본 대사도 실패 " + fallback.FailReason + " (" + fallbackNote + ")");
            }
            finally
            {
                End(request.RelicId, session);
                Interlocked.Decrement(ref activeRequests);
            }
        }

        // ---------- 미리 만들기 ----------

        /// <summary>
        /// 이 상황의 대사를 count 개 미리 만들어 보관함(Cache)에 둔다 — 백그라운드로, 기다리지 않는다. 요청이 오면 바로 꺼내 쓰므로 기기가 느려도 응답이 바로 뜬다.
        /// template 은 요청과 같은 모양이어야 한다 (NpcContextBuilder.PrefetchRequests 가 만든다: 탭 인사·안부·관리 감사·손님 관람).
        /// 한가할 때(진행 중인 요청이 없을 때)만 만들고, 요청이 오면 멈췄다가 끝난 뒤 이어 만든다. 만든 대사는 서로, 그리고 이미 띄운 최근 대사와
        /// 90% 이상 겹치지 않아야 보관한다(NpcLineChecks.Similar). SLM 을 쓰지 않거나 이야기·끌려갈 때처럼 모델을 안 쓰는 요청이면 아무것도 하지 않는다.
        /// </summary>
        public void Prefetch(ReactionRequest template, int count)
        {
            EnqueuePrefetch(template, count, false);
        }

        /// <summary>
        /// 서버 값(시간대·닉네임·방문 횟수)을 <b>기다리지 않고</b> 대사를 미리 만든다. template 의 값(시간대 사실·호칭·방문 횟수)은 견본일 뿐이고, 만든 대사에서 그 값에 달린 말을
        /// 칸(<c>{시간대}</c>·<c>{호칭}</c>·<c>{횟수}</c>, NpcLineSlots)으로 남겨 보관한다. 서버 값이 온 뒤의 요청(RequestAsync)은 같은 상황이면 이 대사를 꺼내 칸에 실제 값을
        /// 넣어 모델 없이 바로 완성한다 — 시간대가 바뀌거나 닉네임·방문 횟수가 달라도 같은 대사를 쓸 수 있다. 칸으로 만들 수 없는 대사(며칠 전·"아침 일찍")는 보관하지 않고 다시 만든다.
        /// 견본을 만드는 것은 NpcContextBuilder.PrefetchTemplates 가 한다.
        /// </summary>
        public void PrefetchTemplate(ReactionRequest template, int count)
        {
            EnqueuePrefetch(template, count, true);
        }

        private void EnqueuePrefetch(ReactionRequest template, int count, bool isTemplate)
        {
            if (template == null || count <= 0 || !SlmEnabled || slm == null || template.Candidates == null || template.Candidates.Count == 0
                    || NpcActions.SkipsModel(template.Candidates))
            {
                return;
            }
            lock (gate)
            {
                // 같은 상황이 이미 줄에 있으면 더하지 않는다
                string key = isTemplate ? NpcLineCache.TemplateKeyOf(template) : NpcLineCache.KeyOf(template);
                foreach (PrefetchJob queued in prefetchJobs)
                {
                    if (queued.IsTemplate == isTemplate && (isTemplate ? NpcLineCache.TemplateKeyOf(queued.Template) : NpcLineCache.KeyOf(queued.Template)) == key)
                    {
                        queued.Count = Math.Max(queued.Count, count);
                        return;
                    }
                }
                prefetchJobs.Enqueue(new PrefetchJob { IsTemplate = isTemplate, Template = template, Count = Math.Min(count, Math.Max(1, Cache.MaxPerKey)), NextVariation = template.Variation });
                if (!prefetchRunning)
                {
                    prefetchRunning = true;
                    prefetchWorker = RunPrefetchWorker();
                }
            }
        }

        /// <summary>줄에 있는 미리 만들기를 모두 버리고 진행 중인 것을 취소한다 (화면 종료·NPC 전환). 보관함의 대사는 그대로 둔다.</summary>
        public void CancelPrefetch()
        {
            lock (gate)
            {
                prefetchJobs.Clear();
            }
            CancelCurrentPrefetch();
        }

        private void CancelCurrentPrefetch()
        {
            CancellationTokenSource running;
            lock (gate)
            {
                running = prefetchCts;
            }
            if (running != null)
            {
                try
                {
                    running.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 이미 끝난 생성
                }
            }
        }

        private async Task RunPrefetchWorker()
        {
            try
            {
                while (true)
                {
                    PrefetchJob job;
                    lock (gate)
                    {
                        if (prefetchJobs.Count == 0)
                        {
                            prefetchRunning = false;
                            return;
                        }
                        job = prefetchJobs.Peek();
                    }
                    // 요청이 진행 중이거나 모델이 아직 안 올라왔으면 기다린다
                    while (Volatile.Read(ref activeRequests) > 0 || slm == null || !slm.IsReady)
                    {
                        if (!SlmEnabled || slm == null)
                        {
                            CancelPrefetch();
                            lock (gate)
                            {
                                prefetchRunning = false;
                            }
                            return;
                        }
                        lock (gate)
                        {
                            if (prefetchJobs.Count == 0)
                            {
                                prefetchRunning = false;
                                return;
                            }
                        }
                        await Task.Delay(25).ConfigureAwait(false);
                    }
                    bool finished = await RunPrefetchJob(job).ConfigureAwait(false);
                    if (finished)
                    {
                        lock (gate)
                        {
                            if (prefetchJobs.Count > 0 && prefetchJobs.Peek() == job)
                            {
                                prefetchJobs.Dequeue();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 미리 만들기는 덤이다. 예외가 나도 요청 처리에는 영향이 없고, 다음 Prefetch 가 새로 시작한다
                lock (gate)
                {
                    prefetchRunning = false;
                }
            }
        }

        // CancelPrefetch 가 줄에서 뺀 작업인지 (화면 종료·NPC 전환). 빠진 작업은 이어 만들지 않는다
        private bool IsQueued(PrefetchJob job)
        {
            lock (gate)
            {
                foreach (PrefetchJob queued in prefetchJobs)
                {
                    if (queued == job)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // 한 상황의 대사를 목표 수만큼 만든다. 요청 때문에 취소되면 false(나중에 이어 만든다), 다 만들었거나 포기했으면 true
        private async Task<bool> RunPrefetchJob(PrefetchJob job)
        {
            while ((job.IsTemplate ? Cache.TemplateCount(job.Template) : Cache.Count(job.Template)) < job.Count && job.Failures < 3)
            {
                if (!IsQueued(job))
                {
                    return true; // CancelPrefetch 로 줄에서 빠졌다 — 더 만들지 않는다
                }
                if (Volatile.Read(ref activeRequests) > 0)
                {
                    return false;
                }
                ReactionRequest request = CopyForPrefetch(job);
                using (var cts = new CancellationTokenSource())
                {
                    lock (gate)
                    {
                        prefetchCts = cts;
                    }
                    cts.CancelAfter(PrefetchTimeout);
                    if (Volatile.Read(ref activeRequests) > 0)
                    {
                        cts.Cancel();
                    }
                    ReactionResult result;
                    try
                    {
                        result = await slm.GenerateAsync(request, cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        result = ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm);
                    }
                    lock (gate)
                    {
                        prefetchCts = null;
                    }
                    if (result.FailReason == FailReason.Cancelled)
                    {
                        // CancelPrefetch 로 줄에서 빠졌으면 끝, 요청이 왔으면(다시 이어 만든다) 나중에, 아니면 시간이 넘은 것이다
                        if (!IsQueued(job))
                        {
                            return true;
                        }
                        if (Volatile.Read(ref activeRequests) > 0)
                        {
                            return false;
                        }
                        job.Failures++;
                        continue;
                    }
                    if (!result.IsSuccess || !(job.IsTemplate ? Cache.AddTemplate(job.Template, result) : Cache.Add(job.Template, result)))
                    {
                        // 검사에 걸렸거나 보관한 대사·최근 대사와 90% 이상 겹침 → 다음 곁들일 거리·첫 낱말로 다시
                        job.Failures++;
                    }
                    else if (job.IsTemplate)
                    {
                        SaveSoon();
                    }
                }
            }
            return true;
        }

        // 미리 만들 요청: 템플릿을 베끼고 곁들일 거리 번호를 바꾼다. 이미 보관한 대사와 그 시나리오의 최근 대사를 RecentLines 로 넣어
        // 출력 검사가 90% 이상 겹치는 대사를 거르게 한다 (프롬프트에는 싣지 않는다)
        private ReactionRequest CopyForPrefetch(PrefetchJob job)
        {
            ReactionRequest t = job.Template;
            var copy = new ReactionRequest
            {
                RequestId = NewId(),
                InteractionId = t.InteractionId,
                RelicId = t.RelicId,
                Mode = t.Mode,
                Input = t.Input,
                Situation = t.Situation,
                Candidates = t.Candidates,
                Facts = t.Facts,
                Memory = t.Memory,
                ContentVersion = t.ContentVersion,
                Variation = job.NextVariation++,
                Nickname = t.Nickname,
                Intimacy = t.Intimacy,
                RecentLines = new List<string>()
            };
            if (t.RecentLines != null)
            {
                copy.RecentLines.AddRange(t.RecentLines);
            }
            copy.RecentLines.AddRange(RecentLinesOf(t.RelicId, t.Input));
            copy.RecentLines.AddRange(job.IsTemplate ? Cache.TemplateLinesFor(t) : Cache.LinesFor(t));
            return copy;
        }

        private static string ScenarioKey(int relicId, string input)
        {
            return relicId + ":" + (input ?? "");
        }

        // 이 시나리오(도깨비 + 선택지)의 최근 대사를 요청에 넣는다. 게임이 미리 넣어 둔 것은 그대로 두고 뒤에 붙인다
        private void AttachRecent(ReactionRequest request)
        {
            List<string> lines;
            lock (gate)
            {
                lines = shown.TryGetValue(ScenarioKey(request.RelicId, request.Input), out lines) ? new List<string>(lines) : new List<string>();
            }
            if (request.RecentLines == null)
            {
                request.RecentLines = new List<string>();
            }
            foreach (string line in lines)
            {
                if (!request.RecentLines.Contains(line))
                {
                    request.RecentLines.Add(line);
                }
            }
        }

        private void Remember(ReactionRequest request, string line)
        {
            if (RecentLineCount <= 0 || string.IsNullOrEmpty(line))
            {
                return;
            }
            lock (gate)
            {
                string key = ScenarioKey(request.RelicId, request.Input);
                List<string> lines;
                if (!shown.TryGetValue(key, out lines))
                {
                    lines = new List<string>();
                    shown[key] = lines;
                }
                lines.Remove(line);
                lines.Add(line);
                while (lines.Count > RecentLineCount)
                {
                    lines.RemoveAt(0);
                }
            }
            SaveSoon();
        }

        /// <summary>그 도깨비가 모든 선택지에서 띄운 최근 대사. 게임이 저장해 두었다가 다음 실행에 RecentLines 로 넘길 때 쓴다 (선택지마다 이어 붙인다).</summary>
        public List<string> RecentLinesOf(int relicId)
        {
            var all = new List<string>();
            lock (gate)
            {
                foreach (KeyValuePair<string, List<string>> entry in shown)
                {
                    if (entry.Key.StartsWith(relicId + ":", StringComparison.Ordinal))
                    {
                        all.AddRange(entry.Value);
                    }
                }
            }
            return all;
        }

        /// <summary>그 도깨비가 그 선택지(시나리오)에서 띄운 최근 대사 (오래된 것부터).</summary>
        public List<string> RecentLinesOf(int relicId, string input)
        {
            lock (gate)
            {
                List<string> lines;
                return shown.TryGetValue(ScenarioKey(relicId, input), out lines) ? new List<string>(lines) : new List<string>();
            }
        }

        /// <summary>
        /// 도깨비를 탭해 확대할 때(선택지를 띄우기 전) 부른다. 그 도깨비의 시스템 메시지를 모델에 미리 읽혀 두어, 이어지는
        /// 요청은 짧은 사용자 메시지만 읽게 한다. 기다리지 않아도 된다 — 예외를 던지지 않고, 실패해도 다음 요청이 처음부터 읽을 뿐이다.
        /// SLM 을 쓰지 않거나 모델이 준비되지 않았으면 아무것도 하지 않는다. 다른 도깨비의 요청·준비가 오거나,
        /// Cancel(그 도깨비)·CancelAll 하거나, ct 가 취소되면 멈춘다. 같은 도깨비의 요청은 이 준비가 끝나기를 기다렸다가 이어 읽는다.
        /// </summary>
        public async Task PrepareAsync(int relicId, CancellationToken ct = default(CancellationToken))
        {
            if (!SlmEnabled || slm == null || !slm.IsReady)
            {
                return;
            }
            var mine = new Session(ct);
            Session previous;
            lock (gate)
            {
                previous = preparing;
                preparing = mine;
                preparingRelicId = relicId;
            }
            if (previous != null)
            {
                previous.Cancel("다른 도깨비를 준비");
            }
            try
            {
                await slm.PrepareAsync(relicId, mine.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 미리 읽기는 덤이다. 취소·실패해도 다음 요청이 시스템 메시지부터 읽는다
            }
            finally
            {
                lock (gate)
                {
                    if (preparing == mine)
                    {
                        preparing = null;
                    }
                }
                mine.Dispose();
            }
        }

        /// <summary>그 도깨비의 진행 중인 요청(과 시스템 프롬프트 미리 읽기)을 취소한다 (NPC 전환). 결과는 버린다.</summary>
        public void Cancel(int relicId)
        {
            Session session;
            Session prepare;
            lock (gate)
            {
                sessions.TryGetValue(relicId, out session);
                prepare = preparing != null && preparingRelicId == relicId ? preparing : null;
            }
            if (session != null)
            {
                session.Cancel("다른 NPC 로 전환해 취소");
            }
            if (prepare != null)
            {
                prepare.Cancel("다른 NPC 로 전환해 취소");
            }
        }

        /// <summary>모든 진행 중인 요청과 미리 읽기를 취소한다 (화면 종료, 정화 미니게임 진입). 결과는 버린다.</summary>
        public void CancelAll()
        {
            List<Session> running;
            lock (gate)
            {
                running = new List<Session>(sessions.Values);
                if (preparing != null)
                {
                    running.Add(preparing);
                }
            }
            foreach (Session session in running)
            {
                session.Cancel("모든 요청 취소(화면 종료)");
            }
        }

        // 시간 초과면 session.TimedOut 을 세운다 (C# 5 의 async 메서드는 out 매개변수를 못 쓴다)
        private async Task<ReactionResult> RunSlm(ReactionRequest request, Session session)
        {
            request.RequestId = NewId();
            using (var slmCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
            {
                Task<ReactionResult> generating;
                try
                {
                    generating = slm.GenerateAsync(request, slmCts.Token);
                }
                catch (Exception e)
                {
                    return ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm, e.Message);
                }
                Task waiting = Task.Delay(SlmTimeout, delayCts.Token);
                Task first = await Task.WhenAny(generating, waiting).ConfigureAwait(false);
                if (first == generating)
                {
                    delayCts.Cancel();
                    try
                    {
                        return generating.Result;
                    }
                    catch (Exception e)
                    {
                        Exception inner = e is AggregateException ? ((AggregateException)e).GetBaseException() : e;
                        return ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm, inner.Message);
                    }
                }
                // 시간 초과 또는 취소. 모델에 취소를 알리고, 끝나기를 기다리지 않는다 (늦게 온 결과는 버려진다)
                session.TimedOut = !session.Token.IsCancellationRequested;
                slmCts.Cancel();
                Observe(generating);
                return ReactionResult.Failed(request.RequestId, FailReason.Cancelled, ReactionSource.Slm,
                        session.TimedOut ? "시간 초과" : "취소");
            }
        }

        // 기다리지 않고 버린 작업이 나중에 예외로 끝나도 처리되지 않은 예외로 남지 않게 한다
        private static void Observe(Task task)
        {
            task.ContinueWith(t => t.Exception.Handle(e => true),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        // 취소는 잠금 밖에서 한다. 취소하는 순간 그 요청의 남은 처리가 같은 스레드에서 이어 돌며 End 로 목록을 고치기 때문이다
        // 다른 도깨비를 미리 읽는 중이면 멈춘다 (모델은 하나라, 그대로 두면 이 요청이 그 뒤에 줄을 선다)
        private Session Begin(int relicId, CancellationToken ct)
        {
            var session = new Session(ct);
            Session previous;
            Session otherPrepare;
            lock (gate)
            {
                sessions.TryGetValue(relicId, out previous);
                sessions[relicId] = session;
                otherPrepare = preparing != null && preparingRelicId != relicId ? preparing : null;
            }
            if (previous != null)
            {
                previous.Cancel("같은 도깨비의 새 요청으로 대체");
            }
            if (otherPrepare != null)
            {
                otherPrepare.Cancel("다른 도깨비의 요청");
            }
            return session;
        }

        private void End(int relicId, Session session)
        {
            lock (gate)
            {
                Session current;
                if (sessions.TryGetValue(relicId, out current) && current == session)
                {
                    sessions.Remove(relicId);
                }
            }
            session.Dispose();
        }

        private static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        // 요청 한 건의 취소 범위: 부르는 쪽의 ct + 관리자가 거는 취소(NPC 전환·대체·전체 취소)
        private sealed class Session : IDisposable
        {
            private readonly CancellationToken caller;
            private readonly CancellationTokenSource own = new CancellationTokenSource();
            private readonly CancellationTokenSource linked;
            private string reason;

            public Session(CancellationToken caller)
            {
                this.caller = caller;
                linked = CancellationTokenSource.CreateLinkedTokenSource(caller, own.Token);
            }

            /// <summary>SLM 을 기다리다 시간 초과로 그만뒀는지.</summary>
            public bool TimedOut;

            public CancellationToken Token
            {
                get { return linked.Token; }
            }

            public void Cancel(string why)
            {
                if (reason == null)
                {
                    reason = why;
                }
                try
                {
                    own.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 이미 끝난 요청
                }
            }

            public string CancelNote()
            {
                return caller.IsCancellationRequested ? "부르는 쪽이 취소(화면 종료 등)" : (reason ?? "취소");
            }

            public void Dispose()
            {
                linked.Dispose();
                own.Dispose();
            }
        }
    }
}
