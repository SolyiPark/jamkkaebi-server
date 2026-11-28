using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>NpcRequestManager.PrepareAsync(미리 읽기)가 어떻게 끝났는지.</summary>
    public enum NpcPrepareOutcome
    {
        /// <summary>SLM 을 쓰지 않거나 모델이 준비되지 않아 아무것도 하지 않았다.</summary>
        Skipped,
        /// <summary>같은 도깨비의 뒤에서 만들기에 맡겨 미리 읽지 않았다 (그 생성이 시스템 메시지를 읽는다 — 다 읽혔다는 뜻은 아니다).</summary>
        Deferred,
        /// <summary>모델에 미리 읽기를 넣어 끝까지 마쳤다 (이미 읽혀 있어 할 일이 없었던 것도 여기다).</summary>
        Completed,
        /// <summary>다른 도깨비의 요청·준비, Cancel, ct 로 멈췄다.</summary>
        Cancelled,
        /// <summary>백엔드가 실패했다. 다음 요청이 시스템 메시지부터 읽는다.</summary>
        Failed
    }

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
    /// 모델에 미리 읽혀 둔다. 다른 도깨비의 요청·준비가 오거나 Cancel·CancelAll 하면 멈춘다. 뒤에서 만들던 대사는 멈추고,
    /// 요청·미리 읽기가 끝난 뒤 PrefetchIdleDelay 가 지나야 다시 만든다 (플레이어가 기다리는 동안 모델을 비워 둔다).</para>
    ///
    /// <para><b>아무것도 띄우지 않는 경우</b>: 부르는 쪽이 취소함(화면 종료), Cancel(relicId)(NPC 전환), CancelAll,
    /// 같은 도깨비에게 새 요청이 와서 옛 요청이 밀림, 후보가 없음. 늦게 끝난 옛 요청의 결과는 버린다.</para>
    ///
    /// <para><b>같은 대사 막기</b>: 시나리오(같은 도깨비의 같은 선택지)마다 이번 실행에서 띄운 대사 RecentLineCount(5)개를 기억해 요청의 RecentLines 에 넣는다.
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

        /// <summary>
        /// 같은 상황을 뒤에서 만드는 것을 기다렸다가 꺼낸 미리 만든 대사의 메모 (WaitForSameSituation). 바로 꺼낸 것(PrefetchedNote)과 달리 기다린 시간이 있다 —
        /// 기기·PC 시험은 이것을 따로 세고 그 시간을 보고한다 (Copilot 리뷰). 미리 만든 대사인지는 IsPrefetched 로 둘을 함께 본다.
        /// </summary>
        public const string PrefetchWaitedNote = "같은 상황을 기다려 꺼낸 미리 만든 대사";

        /// <summary>미리 만든 대사로 답한 응답인지 (바로 꺼냄 PrefetchedNote · 기다려 꺼냄 PrefetchWaitedNote).</summary>
        public static bool IsPrefetched(NpcResponse response)
        {
            return response != null && response.ShouldShow && (response.Note == PrefetchedNote || response.Note == PrefetchWaitedNote);
        }
        /// <summary>
        /// 모델이 멈춰(취소한 생성이 CancelHold 가 지나도 끝나지 않음, <see cref="INpcModelHealth"/>) 기본 대사로 대신한 경우의 Note.
        /// 아직 안 올라온 것("모델이 아직 준비되지 않음")과 구분한다 — 기기 시험에서 이 메모가 나오면 모델이 묶인 것이다.
        /// </summary>
        public const string StalledNote = "모델이 멈춤(취소한 생성이 끝나지 않음) → 기본 대사";

        // 미리 만들기가 기다리며 다시 볼 간격. 요청이 끝나기를 기다릴 때는 바로 이어 만들도록 자주, 모델이 안 올라왔으면
        // (올리기 실패면 끝내 안 올라온다) 드물게, 멈췄으면(앱을 다시 켤 때까지 안 풀릴 수 있다) 더 드물게 본다 — 25ms 마다 깨어나면 폰의 배터리를 쓴다
        private const int RequestPollMs = 25;
        private const int IdlePollMs = 250;     // 플레이어가 잠깐 쉬기를(PrefetchIdleDelay) 기다릴 때
        private const int NotReadyPollMs = 250;
        private const int StalledPollMs = 1000;

        // 마지막 요청·미리 읽기가 끝난 때(Clock 의 ms). 처음에는 아주 옛날로 두어 로딩 화면의 미리 만들기는 바로 시작한다. gate 안에서 읽고 쓴다
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private long lastPlayerActivityMs = long.MinValue / 4;

        private readonly INpcReactionGenerator slm;
        private readonly INpcReactionGenerator rule;
        private readonly object gate = new object();
        private readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();

        // 시나리오(도깨비 + 선택지)마다 최근에 띄운 대사 (오래된 것부터). 같은 도깨비의 같은 상황에서 같은 말이 거듭되는 것을 막으려는 것이라
        // 관리하기 대사는 관리하기 대사끼리만 비교한다
        private readonly Dictionary<string, List<string>> shown = new Dictionary<string, List<string>>();

        // 시나리오마다 최근에 모델에 준 첫 낱말 (오래된 것부터, 이번 실행만). 실패한 시도도 넣는다 — 그 첫 낱말로 '반복'에 걸렸으면 다음에도 같은 말이 나오기 쉽다
        private readonly Dictionary<string, List<string>> triedStarters = new Dictionary<string, List<string>>();
        private const int TriedStarterCount = 3;
        private const int StarterSearch = 12; // 첫 낱말 목록(5~6개)을 두 바퀴 본다
        private const int AskStarterSearch = 20; // 흐려진 안부: 앞 문장 넷(번호 % 4) × 첫 낱말 다섯(번호 % 5)의 짝을 모두 본다

        // 미리 만들기: 보관함, 할 일 줄, 일꾼, 진행 중인 요청 수
        private readonly List<PrefetchJob> prefetchJobs = new List<PrefetchJob>();   // 넣은 차례대로. 일꾼은 모델이 읽어 둔 도깨비의 것을 먼저 꺼낸다 (NextJob)

        // 모델이 마지막으로 시스템 메시지를 읽은(또는 읽기 시작한) 도깨비 — 마지막 미리 읽기·모델을 쓴 요청·미리 만들기의 도깨비. gate 안에서 읽고 쓴다
        private int modelRelicId;
        private bool modelRelicKnown;

        // 모델을 쓴 요청·미리 읽기가 마지막으로 끝난 때 (다른 도깨비의 미리 만들기는 이때부터 PrefetchSwitchIdleDelay 를 센다). gate 안에서 읽고 쓴다
        private long lastModelActivityMs = long.MinValue / 4;

        // 끝까지 돈 미리 만들기 한 번의 시간(ms), 최근 것 몇 개 (AdaptiveIdleDelay). gate 안에서 읽고 쓴다
        private readonly List<long> prefetchDurations = new List<long>();
        private const int PrefetchDurationCount = 5;
        private const long MinAdaptiveIdleMs = 1000;
        private CancellationTokenSource prefetchCts;
        private bool prefetchGenerating; // prefetchCts 의 생성이 시작 전 확인을 지나 모델에 들어갔는지. gate 안에서 읽고 쓴다
        private volatile bool lastPrepareDeferred; // 마지막 PrepareAsync 가 같은 도깨비의 뒤에서 만들기에 맡겨 미리 읽지 않았는지 (LastPrepareDeferred)
        private volatile NpcPrepareOutcome lastPrepareOutcome; // 마지막 PrepareAsync 가 어떻게 끝났는지 (LastPrepareOutcome)
        private int prefetchRelicId;     // 그 생성의 도깨비 (prefetchGenerating 일 때만 뜻이 있다). gate 안에서 읽고 쓴다
        // 모델에 들어간 미리 만들기 생성의 보관함 열쇠와, 그 생성이 끝나(보관함에 넣고) 알리는 신호. 같은 상황의 요청이 그것을 기다려 꺼내 쓴다 (SameSituationPrefetch). gate 안에서 읽고 쓴다
        private string prefetchKey;
        private TaskCompletionSource<bool> prefetchDone;
        private bool prefetchPreempted;   // 지금 생성(prefetchCts)을 요청·미리 읽기·CancelPrefetch 가 멈췄는지. gate 안에서 읽고 쓴다
        private Task prefetchWorker = Task.FromResult(true);
        private bool prefetchRunning;
        private int activeRequests;

        // 미리 만든 대사를 남겨 두는 곳(LoadStore)과 그 버전. 쓰기는 한 번에 하나만, 바뀔 때마다 뒤에서 쓴다(SaveSoon)
        private INpcLineStore store;
        private string storeVersion;
        private readonly object storeWriteGate = new object();
        private int savePending;
        private bool closed;   // CloseStore 뒤 — 새 미리 만들기를 받지 않는다

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
            ThinkingLine = "음...";
            RecentLineCount = 5; // 2026-11-26: 10 → 5. 15자 인사를 같은 도깨비가 거듭하면 10개와 겹치지 않을 말이 모자라 반복 실패가 가장 많았다 (ver8 기기 시험 57건 중 20건)
            Cache = new NpcLineCache();
            UsePrefetched = true;
            PrefetchTimeout = TimeSpan.FromSeconds(60);
            PrefetchIdleDelay = TimeSpan.FromSeconds(3);
            PrefetchSwitchIdleDelay = TimeSpan.FromSeconds(30);
            AdaptiveIdleDelay = true;
            RefillTarget = request => request.Input == NpcInputs.Tap || request.Input == NpcInputs.Visit ? 2 : 1;
            SaveDelay = TimeSpan.FromSeconds(1);
            // 꺼낼 때 지금 규칙의 출력 검사를 한 번 더 한다 (파일에 남겨 둔 대사는 그 뒤에 바뀐 규칙을 모르고 만들어졌을 수 있다)
            SlmReactionGenerator generator = slm as SlmReactionGenerator;
            if (generator != null)
            {
                Cache.Recheck = generator.Recheck;
            }
        }

        /// <summary>SLM 의 모델이 멈췄는지 (SlmReactionGenerator.IsStalled). 다른 생성기는 멈추지 않는 것으로 본다.</summary>
        public bool SlmStalled
        {
            get { return SlmStatus == NpcModelStatus.Stalled; }
        }

        // SLM 모델 상태를 한 번에 읽는다. SLM 생성기가 아니면 IsReady 로 Ready·NotLoaded 만 (SLM 이 없으면 NotLoaded).
        // 읽다가 예외가 나면(직접 만든 생성기 등 — 인터페이스가 막지 않는다) 준비되지 않은 것으로 본다: 요청은 기본 대사로, 미리 읽기는
        // 아무것도 하지 않고 끝난다. 요청 관리자는 예외를 던지지 않는다
        private NpcModelStatus SlmStatus
        {
            get
            {
                if (slm == null)
                {
                    return NpcModelStatus.NotLoaded;
                }
                try
                {
                    SlmReactionGenerator generator = slm as SlmReactionGenerator;
                    if (generator != null)
                    {
                        return generator.ModelStatus;
                    }
                    return slm.IsReady ? NpcModelStatus.Ready : NpcModelStatus.NotLoaded;
                }
                catch (Exception)
                {
                    return NpcModelStatus.NotLoaded;
                }
            }
        }

        /// <summary>
        /// 미리 만든 대사를 꺼내 쓰거나(또는 보관함이 비어 그 자리에서 만들거나) 띄운 뒤, 그 상황의 칸 있는 대사를 <b>쓴 만큼</b> 뒤에서 다시 만들어 채우는지.
        /// 다음 번 같은 상황(정화·탭 인사·안부·손님 관람)은 이번에 뒤에서 만든 대사를 바로 꺼내 쓴다. 기본 false — 게임은 켠다(README "미리 만든 대사 파일").
        /// 채울 개수는 RefillTarget. 기억을 꺼내는 대화(지난 대화)는 기억이 바뀌어 다시 쓰지 않으므로 채우지 않는다.
        /// </summary>
        public bool RefillAfterUse { get; set; }

        /// <summary>
        /// 쓴 뒤 그 상황에 채워 둘 칸 있는 대사 수. 기본: 탭 인사·손님 관람 2(자주 하거나 여러 친구가 잇달아 와 다시 만들기가 끝나기 전에 또 올 수 있다), 나머지 1(쓴 만큼 하나).
        /// 정화는 상태(선명·흐려짐·재봉인)마다 열쇠가 따로라, 쓴 상태의 대사 하나만 다시 만든다.
        /// 보관함이 비어 그 자리에서 만든 요청(빗나감)이면 하나 더 채운다(Cache.MaxPerKey 까지) — 그 상황은 채운 것보다 빨리 쓰인다.
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
                // 최근 대사와 칸 있는 대사를 한 잠금(gate) 안에서 함께 찍는다 — 따로 찍으면 꺼낸 대사 A 를 기억하기 전의 최근 대사와 A 대신 다시 만든 B 가 섞여
                // 저장될 수 있다 (CodeRabbit 리뷰). 다시 만들기는 Remember 뒤에 줄에 들어가므로 B 가 보이면 A 의 기억도 보인다
                List<NpcLineCache.StoredTemplate> templates;
                lock (gate)
                {
                    target = store;
                    version = storeVersion;
                    foreach (KeyValuePair<string, List<string>> pair in shown)
                    {
                        recent[pair.Key] = new List<string>(pair.Value);
                    }
                    templates = target != null ? Cache.ExportTemplates() : null;
                }
                if (target == null)
                {
                    return false;
                }
                string text = NpcLineStoreFormat.Write(version, templates, recent);
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

        /// <summary>
        /// 이 관리자를 버리기 전에 부른다 (화면 종료·판 다시 돌리기·앱 종료). 미리 만들기와 쓴 만큼 다시 만들기를 멈추고(최대 wait 만큼 기다린다),
        /// save 면 지금 내용을 한 번 쓴 뒤 저장소를 뗀다. 뗀 뒤에는 뒤에서 기다리던 저장도 쓰지 않는다 — 버린 관리자가 늦게 쓴 저장이 다음 관리자(같은 파일)의 내용을
        /// 덮어쓰지 않게 한다 (CodeRabbit 리뷰). 쓰기 잠금이 관리자마다라서 관리자끼리는 서로 막지 못한다.
        /// </summary>
        public void CloseStore(bool save, TimeSpan wait)
        {
            // 닫는 중에 끝난 요청이 다시 만들기를 줄에 넣지 못하게 먼저 닫힘을 표시한다 — 그대로 두면 모델을 내린 뒤에도 일꾼이 남아 그 일을 기다렸다 (CodeRabbit 리뷰)
            lock (gate)
            {
                closed = true;
            }
            CancelPrefetch();
            try
            {
                PrefetchCompletion.Wait(wait);
            }
            catch (AggregateException)
            {
                // 미리 만들기의 오류는 덤이다
            }
            lock (storeWriteGate)
            {
                if (save)
                {
                    SaveStore();
                }
                lock (gate)
                {
                    store = null;
                }
            }
        }

        private int MaxLineLength(ReactionRequest request)
        {
            SlmReactionGenerator generator = slm as SlmReactionGenerator;
            return generator != null ? generator.MaxLineLength(request.Input) : int.MaxValue;
        }

        // 바뀐 것을 뒤에서 쓴다. 이미 쓰려고 기다리는 것이 있으면 그것이 이번 변경까지 쓴다
        private void SaveSoon()
        {
            if (Store == null || Interlocked.Exchange(ref savePending, 1) == 1)
            {
                return;
            }
            TimeSpan delay = SaveDelay;
            Task.Run(async () =>
            {
                // 꺼내기·기억·다시 만들기가 잇달아 일어나면 파일을 여러 번 쓰던 것을 SaveDelay 동안 모아 한 번 쓴다 (성능 리뷰 2026-11-19)
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
                Interlocked.Exchange(ref savePending, 0);
                SaveStore();
            });
        }

        /// <summary>
        /// 바뀐 것을 파일에 쓰기 전에 모으는 시간. 기본 1초 — 그 사이의 변경은 한 번에 쓴다. 앱을 내릴 때는 SaveStore(또는 CloseStore)로 바로 쓴다.
        /// </summary>
        public TimeSpan SaveDelay { get; set; }

        // 쓴 만큼 다시 만든다 (RefillAfterUse). 이 요청을 견본으로 칸 있는 대사를 RefillTarget 개까지 채운다 — 요청의 서버 값은 칸으로 남는다.
        // missed: 보관함에 쓸 대사가 없어 그 자리에서 만들었다 — 그 상황은 채운 것보다 빨리 쓰인다(같은 도깨비를 잇달아 탭함 등). 하나 더 채운다(MaxPerKey 까지)
        private void Refill(ReactionRequest request, bool missed)
        {
            if (!RefillAfterUse || !UsePrefetched || !SlmEnabled || slm == null || request == null || Array.IndexOf(RefillInputs, request.Input) < 0
                    || (request.Memory != null && request.Memory.Count > 0) || request.Candidates == null || NpcActions.SkipsModel(request.Candidates))
            {
                return;
            }
            int target;
            try
            {
                target = RefillTarget != null ? RefillTarget(request) : 1;
            }
            catch (Exception)
            {
                return; // 채울 수를 정하지 못하면 다시 만들지 않는다 — 이미 띄울 대사가 있는 응답을 실패로 만들지 않는다 (CodeRabbit 리뷰)
            }
            if (target <= 0)
            {
                return;
            }
            if (missed)
            {
                target = Math.Min(target + 1, Math.Max(target, Cache.MaxPerKey));
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

        /// <summary>
        /// 미리 만들 때 한 번 만드는 데 기다리는 최대 시간. 기본 60초 — 사용자가 기다리지 않는 시간이라 요청 때(SlmTimeout)보다 넉넉하다.
        /// 기기의 뒤에서 만들기는 프롬프트를 조각으로 나눠 읽어(LlmUnityOptions.BackgroundStepChars) 한 번에 읽을 때보다 길어, 30초에서 60초로 늘렸다 (2026-11-25) —
        /// 30초면 처음 읽는 도깨비(시스템 메시지 조각까지)가 폰에서 넘기기 쉬워, 시간 초과를 실패로 세다 세 번이면 그 상황을 그만 만든다.
        /// </summary>
        public TimeSpan PrefetchTimeout { get; set; }

        /// <summary>
        /// 마지막 요청·시스템 프롬프트 미리 읽기가 끝난 뒤 이만큼 아무 일이 없어야 뒤에서 미리 만들기·다시 만들기를 <b>새로 시작</b>한다. 기본 3초.
        /// 이것은 부딪힘을 <b>줄이는</b> 장치다 — 부딪혔을 때 플레이어가 먼저인 것은 백엔드가 보장한다(뒤에서 만들기는 조각으로 나눠 읽다가 한 조각 안에 비켜 준다,
        /// LlmUnityOptions.BackgroundStepChars). 예전(11-23)에는 비켜 줄 수 없어 부딪히면 미리 읽기·요청이 그 생성의 프롬프트 읽기(폰에서 7~20초) 뒤에 줄을 섰기에
        /// 10초를 쉬었다. 이제 부딪혀도 플레이어는 많아야 한 조각(1~2초)만 기다려 3초로 줄였다 (2026-11-25) — 10초면 장면 사이가 10초일 때 쉬는 시간이 끝나자마자
        /// 다음 장면이 와서, 대사 하나에 10~20초 걸리는 폰은 다시 만들기를 시작도 못 했다. 이미 시작한 생성은 이 값과 상관없이 끝까지 만든다
        /// (미리 읽기와 모델을 쓰는 요청은 멈춘다 — 같은 도깨비의 미리 읽기는 멈추지 않는다, PrepareAsync). 0 이면 요청이 끝나자마자 이어 만든다.
        /// </summary>
        public TimeSpan PrefetchIdleDelay { get; set; }

        /// <summary>
        /// <b>다른 도깨비</b>의 미리 만들기·다시 만들기를 새로 시작하기 전에 쉬어야 하는 시간. 기본 30초 (PrefetchIdleDelay 보다 짧으면 PrefetchIdleDelay).
        /// 뒤에서 만들기는 지금 모델이 시스템 메시지를 읽어 둔 도깨비(마지막 미리 읽기·요청의 도깨비)의 작업을 먼저 고르고, 그 도깨비의 작업이 없을 때만
        /// 다른 도깨비의 작업을 이만큼 쉰 뒤에 시작한다. 다른 도깨비의 프롬프트를 읽으면 모델이 기억하던 시스템 메시지가 지워지고, 읽는 동안은 멈추지 못해
        /// 그 사이 돌아온 플레이어의 미리 읽기가 그 뒤에 줄을 선 다음 시스템 메시지를 처음부터 다시 읽는다
        /// (2026-11-24 기기 시험: 장면 사이 10초 = PrefetchIdleDelay 일 때 같은 도깨비의 미리 읽기가 0초 → 45~68초. 줄 맨 앞의 다른 도깨비 작업을 집었다).
        /// 같은 도깨비의 작업은 시스템 메시지를 다시 읽지 않아(사용자 메시지만) 멈추지 못하는 구간도 짧다.
        /// </summary>
        public TimeSpan PrefetchSwitchIdleDelay { get; set; }

        /// <summary>
        /// 쉬는 시간(PrefetchIdleDelay)을 기기 속도에 맞춰 줄일지. 기본 true. 켜면 최근 미리 만들기 한 번의 시간(중앙값) × 2 (1초 이상)가 PrefetchIdleDelay 보다 짧을 때 그것만 쉰다 —
        /// 끼어든 생성이 플레이어를 막는 시간은 생성 한 번보다 길지 않으니, 빨리 만드는 기기는 짧게 쉬고 다시 만든다. 한 번에 10초가 넘는 폰에서는 예전과 같다.
        /// 다른 도깨비로 넘어가는 작업의 PrefetchSwitchIdleDelay 는 줄이지 않는다.
        /// </summary>
        public bool AdaptiveIdleDelay { get; set; }

        /// <summary>
        /// 마지막 PrepareAsync 가 미리 읽지 않고, 뒤에서 만들던 같은 도깨비의 생성에 맡겼는지 (그 생성이 시스템 메시지를 읽는다).
        /// 그때 미리 읽기는 바로 끝나지만 시스템 메시지가 아직 다 읽히지 않았을 수 있다 — 기기 시험이 미리 읽기 0초를 '준비 끝'으로 잘못 읽지 않게 따로 적는다.
        /// </summary>
        public bool LastPrepareDeferred
        {
            get { return lastPrepareDeferred; }
        }

        /// <summary>마지막 PrepareAsync 가 어떻게 끝났는지 (PrepareAsync 가 돌려주는 값과 같다).</summary>
        public NpcPrepareOutcome LastPrepareOutcome
        {
            get { return lastPrepareOutcome; }
        }

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
            // 이 작업이 도는 동안 같은 상황의 미리 만들기·다시 만들기가 합쳐졌는지 (EnqueuePrefetch). 일꾼은 다 만들었다고 본 뒤에도 이것이 서 있으면 지우지 않고 다시 본다. gate 안에서 읽고 쓴다
            public bool Refreshed;
        }

        /// <summary>
        /// 시나리오(같은 도깨비의 같은 선택지)마다 기억해 두고 다시 쓰지 않을 최근 대사 수. 기본 5 (2026-11-26 에 10 에서 줄였다) — 최근 5개 중 하나와 90% 이상 겹치면
        /// 출력 검사가 거르고 SLM 생성기가 새로 만든다. 0 이면 기억하지 않는다.
        /// </summary>
        public int RecentLineCount { get; set; }

        /// <summary>관리 감사·선명한 안부의 첫 낱말(깨끗하게·훨씬·몸이·덕분에·정말·개운하게)을 고르는 난수 (PromptAssembler.PicksStarterAtRandom). 비우면 처음 쓸 때 만든다. 시험은 씨앗을 준다.</summary>
        public Random StarterRandom { get; set; }

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
        /// 모델을 기다리는 동안 도깨비가 띄울 말 (기본 "음..."). 미리 만든 대사가 없어 그 자리에서 만들거나 같은 상황을 뒤에서 만드는 것을 기다릴 때,
        /// RequestAsync 의 thinking 으로 한 번 알린다. 비우면 알리지 않는다 (2026-11-27: 그 자리 생성은 PC 1초 남짓, 폰 10초 안팎이라 말풍선이 비어 있었다).
        /// </summary>
        public string ThinkingLine { get; set; }

        /// <summary>
        /// 반응을 만든다. 예외를 던지지 않고(요청이 null 일 때만 ArgumentNullException) 늘 NpcResponse 를 돌려준다.
        /// ct 는 부르는 쪽의 취소(화면 종료 등)다 — 취소되면 기본 대사로 대신하지 않고 버린다.
        /// </summary>
        public Task<NpcResponse> RequestAsync(ReactionRequest request, CancellationToken ct = default(CancellationToken))
        {
            return RequestAsync(request, ct, null);
        }

        /// <summary>
        /// 반응을 만든다 (위와 같다). 모델을 기다려야 하면(미리 만든 대사가 없음) 기다리기 전에 thinking 에 ThinkingLine("음...")을 한 번 알린다 —
        /// 게임은 그동안 말풍선에 띄우고, 응답이 오면 그 대사로 바꾼다. 미리 만든 대사·정해진 대사·대사 없음·기본 대사로 바로 답하는 요청은 알리지 않는다.
        /// <para>Unity 에서는 메인 스레드에서 만든 <c>new Progress&lt;string&gt;(line =&gt; ShowBubble(line))</c> 을 넘긴다 — Progress 는 만든 스레드에서 부르므로
        /// 말풍선을 바로 바꿔도 된다. 알린 뒤 응답이 띄우지 않는 것(ShouldShow == false: 화면 종료·NPC 전환)이면 게임이 말풍선을 거둔다.</para>
        /// </summary>
        public Task<NpcResponse> RequestAsync(ReactionRequest request, CancellationToken ct, IProgress<string> thinking)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            return Handle(request, ct, thinking);
        }

        // 모델을 기다리기 전에 ThinkingLine 을 한 번 알린다. 알리는 쪽의 예외는 삼킨다 (RequestAsync 는 예외를 던지지 않는다).
        // 이미 버려진 요청(같은 NPC 의 새 요청·화면 종료로 세션이 취소됨)은 알리지 않는다 — 새 요청의 말풍선을 덮지 않게 (CodeRabbit 리뷰).
        // 알린 것이 게임에 닿기 전에 새 요청이 시작될 수도 있어(Progress 는 메인 스레드로 넘겨 부른다) 게임도 말풍선의 주인을 확인한다 (README 예시)
        private sealed class ThinkingNotice
        {
            private readonly IProgress<string> sink;
            private readonly string line;
            private int shown;

            public ThinkingNotice(IProgress<string> sink, string line)
            {
                this.sink = sink;
                this.line = line;
            }

            public void Show(CancellationToken session)
            {
                if (sink == null || string.IsNullOrEmpty(line) || session.IsCancellationRequested || Interlocked.Exchange(ref shown, 1) == 1)
                {
                    return;
                }
                try
                {
                    sink.Report(line);
                }
                catch (Exception)
                {
                    // 말풍선을 띄우지 못해도 대사는 만든다
                }
            }
        }

        private async Task<NpcResponse> Handle(ReactionRequest request, CancellationToken ct, IProgress<string> thinking)
        {
            var notice = new ThinkingNotice(thinking, ThinkingLine);
            if (request.Candidates == null || request.Candidates.Count == 0)
            {
                return NpcResponse.Discard(null, "후보가 없어 띄울 반응이 없다");
            }
            if (string.IsNullOrEmpty(request.InteractionId))
            {
                request.InteractionId = NewId();
            }

            // 진행 중인 요청이 있는 동안과 끝난 뒤 PrefetchIdleDelay 동안은 미리 만들기의 다음 생성을 시작하지 않는다 (모델은 하나라 요청이 먼저다). 만들던 것은 <b>이 요청이 모델을 쓸 때만</b>
            // 취소한다(RunSlm 앞). 미리 만든 대사로 바로 답하는 요청·정해진 대사·기본 대사는 모델을 쓰지 않으므로 만들던 것을 버리지 않는다 — 예전에는 탭할 때마다
            // 취소해, 기기(대사 하나에 수십 초)에서 자주 누르면 쓴 만큼 다시 만들기가 끝나지 못했다 (성능 리뷰 2026-11-19)
            Interlocked.Increment(ref activeRequests);
            Session session = Begin(request.RelicId, ct);
            bool usedModel = false; // 모델을 쓰려 한 요청인지 (MarkPlayerActivity)
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
                    NpcResponse ready = ServePrefetched(request);
                    if (ready != null)
                    {
                        return ready;
                    }
                }

                // 1) SLM
                ReactionResult slmAttempt = null;
                string fallbackNote;
                // 모델 상태는 SLM 을 쓸 요청에서만(정해진 대사·대사 없음·SLM 꺼짐 다음에) 한 번 읽고, 그 값으로 판단과 메모를 함께 정한다.
                // 준비 여부와 멈춤을 따로 읽으면 그 사이 멈춘 생성이 끝나 멈춘 모델이 '아직 준비되지 않음'으로 적힌다
                NpcModelStatus slmStatus = NpcModelStatus.NotLoaded;
                SameSituationOutcome sameSituation;
                // 모델을 기다리기 시작한 때. 같은 상황을 기다린 뒤 그 자리에서 만들게 되면 남은 시간만 준다 — 기다림과 생성을 합쳐 SlmTimeout 을 넘지 않게
                Stopwatch modelWait = Stopwatch.StartNew();
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
                else if ((slmStatus = SlmStatus) != NpcModelStatus.Ready) // 여기서 처음 읽는다
                {
                    fallbackNote = slmStatus == NpcModelStatus.Stalled ? StalledNote : "모델이 아직 준비되지 않음 → 기본 대사";
                }
                else if ((sameSituation = await WaitForSameSituation(request, session, notice).ConfigureAwait(false)) != null)
                {
                    // 같은 상황을 뒤에서 만들던 것을 기다렸다: 꺼내 썼거나(또는 화면이 바뀌어 버림) 기다리다 시간이 넘었다
                    if (sameSituation.Response != null)
                    {
                        return sameSituation.Response;
                    }
                    fallbackNote = sameSituation.FallbackNote;
                }
                else if (modelWait.Elapsed >= SlmTimeout)
                {
                    //기다림에 주어진 시간을 다 썼으면 새 생성을 시작하지 않는다.
                    fallbackNote = SameSituationTimeoutNote + "(" + (int)SlmTimeout.TotalMilliseconds + "ms) -> 기본 대사";
                }
                else
                {
                    usedModel = true;
                    // 이 요청이 모델을 쓴다 — 뒤에서 만들던 것은 멈추고(백엔드가 한 조각 안에 비켜 준다, LlmUnityOptions.BackgroundStepChars) 요청이 끝나면 이어 만든다.
                    // 뒤에서 만들던 것이 같은 상황이면 위에서 멈추지 않고 기다렸다 꺼내 썼다 (SameSituationPrefetch). 여기 오는 것은 다른 상황이거나, 기다린 결과를 쓸 수 없었던 요청이다
                    CancelCurrentPrefetch();
                    MarkModelRelic(request.RelicId);
                    // 기기에서는 한 번 만드는 데 10초가 넘어 다시 만들 틈이 없다 — 첫 시도부터 최근 대사와 다른 첫 낱말로 만들게 한다
                    PickFreshStarter(request, request.RecentLines);
                    // 그 자리에서 만든다 — 기다리는 동안 "음..." (같은 상황을 기다렸다가 온 요청은 이미 띄웠다).
                    // 모델을 부르지 않고 끝날 요청(페르소나 없는 도깨비 등 — 생성기가 Unsupported 로 바로 끝낸다)은 띄우지 않는다 (Copilot 리뷰)
                    if (CanGenerate(request))
                    {
                        notice.Show(session.Token);
                    }
                    // 같은 상황을 기다렸다가 쓸 대사가 없어 여기 왔으면 이미 기다린 만큼 뺀다 (예전에는 다시 SlmTimeout 을 다 줘 플레이어가 SlmTimeout 의 두 배까지 기다렸다)
                    TimeSpan waitedBefore = modelWait.Elapsed;
                    TimeSpan budget = SlmTimeout - waitedBefore;
                    slmAttempt = await RunSlm(request, session, budget > MinSlmBudget ? budget : MinSlmBudget).ConfigureAwait(false);
                    if (session.Token.IsCancellationRequested)
                    {
                        return NpcResponse.Discard(slmAttempt, session.CancelNote());
                    }
                    if (!session.TimedOut && slmAttempt.IsSuccess && slmAttempt.RequestId == request.RequestId)
                    {
                        Remember(request, slmAttempt.Line);
                        Refill(request, true);
                        return NpcResponse.Show(slmAttempt, slmAttempt, slmAttempt.Note != null ? "SLM (" + slmAttempt.Note + ")" : "SLM");
                    }
                    fallbackNote = session.TimedOut
                            ? "SLM 시간 초과(" + (int)SlmTimeout.TotalMilliseconds + "ms"
                              + (waitedBefore >= SameSituationNoteThreshold ? ", 같은 상황을 기다린 " + (int)waitedBefore.TotalMilliseconds + "ms 포함" : "") + ") → 기본 대사"
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
                    Refill(request, true);
                    return NpcResponse.Show(fallback, slmAttempt, fallbackNote);
                }
                return NpcResponse.Discard(slmAttempt, "기본 대사도 실패 " + fallback.FailReason + " (" + fallbackNote + ")");
            }
            finally
            {
                End(request.RelicId, session);
                // 끝난 때를 먼저 적고 줄인다 — 거꾸로 하면 그 틈에 미리 만들기가 '요청도 없고 쉰 지도 오래'로 보고 시작한다
                MarkPlayerActivity(usedModel);
                Interlocked.Decrement(ref activeRequests);
            }
        }

        // 보관함에서 이 요청의 대사를 꺼내 띄운다 (같은 상황에서 만든 것, 최근 대사와 90% 이상 겹치지 않는 것). 없으면 null
        private NpcResponse ServePrefetched(ReactionRequest request)
        {
            return ServePrefetched(request, PrefetchedNote);
        }

        // note: 응답 메모 (바로 꺼냄 PrefetchedNote, 같은 상황을 기다려 꺼냄 PrefetchWaitedNote)
        private NpcResponse ServePrefetched(ReactionRequest request, string note)
        {
            ReactionResult ready = Cache.TryTake(request, request.RecentLines);
            if (ready == null)
            {
                // 칸 있는 대사: 서버 값을 모르는 채로 미리 만들어 둔 것에 이 요청의 시간대·호칭·방문 횟수를 넣어 바로 완성한다
                SlmReactionGenerator generator = slm as SlmReactionGenerator;
                ready = Cache.TryTakeTemplate(request, request.RecentLines, generator != null ? generator.MaxLineLength(request.Input) : int.MaxValue);
            }
            if (ready == null)
            {
                return null;
            }
            request.RequestId = NewId();
            ReactionResult served = ReactionResult.Success(request.RequestId, ready.ActionId, ready.TargetId, ready.Line, ReactionSource.Slm, ready.Note);
            Remember(request, served.Line);
            Refill(request, false);
            SaveSoon(); // 꺼낸 대사가 파일에서 빠지게 한다 (RecentLineCount 가 0 이면 Remember 가 저장하지 않는다)
            return NpcResponse.Show(served, null, note);
        }

        private static long NowMs()
        {
            return Clock.ElapsedMilliseconds;
        }

        // usedModel: 그 요청·미리 읽기가 모델을 썼는지 (다른 도깨비로 넘어가는 미리 만들기는 모델을 마지막으로 쓴 때부터 PrefetchSwitchIdleDelay 를 센다)
        private void MarkPlayerActivity(bool usedModel)
        {
            lock (gate)
            {
                lastPlayerActivityMs = NowMs();
                if (usedModel)
                {
                    lastModelActivityMs = lastPlayerActivityMs;
                }
            }
        }

        // 플레이어를 위해 모델을 비워 두는 중이면 다시 볼 때까지의 간격(ms), 아니면 0.
        // 진행 중인 요청·시스템 프롬프트 미리 읽기가 있거나, 마지막 요청·미리 읽기가 끝난 지 그 작업의 쉬는 시간(IdleDelayFor)이 안 지났거나,
        // 다른 도깨비로 넘어가는 작업인데 모델을 마지막으로 쓴 지 PrefetchSwitchIdleDelay 가 안 지났으면 비워 둔다
        private int PlayerHoldMs(PrefetchJob job)
        {
            if (Volatile.Read(ref activeRequests) > 0)
            {
                return RequestPollMs;
            }
            lock (gate)
            {
                if (preparing != null)
                {
                    return RequestPollMs;
                }
                long now = NowMs();
                long left = lastPlayerActivityMs + Millis(IdleDelayFor(job)) - now;
                if (SwitchesSpirit(job))
                {
                    // 이야기·끌려갈 때·미리 만든 대사로 답한 요청은 모델을 쓰지 않아 이 시계를 다시 돌리지 않는다 — 예전에는 그것도 세어,
                    // 모델을 안 쓰는 장면이 10초마다 이어지면(S15~S21) 다른 도깨비의 다시 만들기가 끝내 시작하지 못했다 (2026-11-25 PC 시험: 반복 3·4 적중 2건)
                    left = Math.Max(left, lastModelActivityMs + Millis(PrefetchSwitchIdleDelay) - now);
                }
                return left > 0 ? (int)Math.Min(left, IdlePollMs) : 0;
            }
        }

        private static long Millis(TimeSpan span)
        {
            return (long)Math.Max(0.0, Math.Min(span.TotalMilliseconds, int.MaxValue));
        }

        private bool HoldingForPlayer(PrefetchJob job)
        {
            return PlayerHoldMs(job) > 0;
        }

        // gate 안에서 부른다. 그 작업이 다른 도깨비로 넘어가는 것인지 — 모델이 시스템 메시지를 읽어 둔 도깨비를 알고, 그와 다를 때
        private bool SwitchesSpirit(PrefetchJob job)
        {
            return job != null && modelRelicKnown && job.Template.RelicId != modelRelicId;
        }

        // gate 안에서 부른다. 마지막 요청·미리 읽기가 끝난 뒤 그 작업을 새로 시작하기 전에 쉬어야 하는 시간 (다른 도깨비면 PlayerHoldMs 가 PrefetchSwitchIdleDelay 를 더 본다).
        // AdaptiveIdleDelay 면 PrefetchIdleDelay 를 넘지 않는 안에서 최근 미리 만들기 한 번의 시간(중앙값)의 두 배로 줄인다:
        // 쉬는 시간은 끼어든 생성이 플레이어를 막는 것을 줄이려는 것인데, 막는 시간은 그 생성 한 번보다 길지 않다. 한 번에 1~2초인 기기(PC 의 LM Studio)는
        // 10초를 다 쉬면 장면 사이 10초 동안 다시 만들기가 한 번도 끝나지 않았다 (2026-11-25 PC 시험: 적중 27/64, 장면 사이 15초면 61~63/64).
        // 한 번에 10초가 넘는 폰은 두 배가 PrefetchIdleDelay 를 넘어 예전과 같다
        private TimeSpan IdleDelayFor(PrefetchJob job)
        {
            TimeSpan delay = PrefetchIdleDelay;
            if (AdaptiveIdleDelay && prefetchDurations.Count > 0)
            {
                List<long> sorted = new List<long>(prefetchDurations);
                sorted.Sort();
                TimeSpan measured = TimeSpan.FromMilliseconds(Math.Max(MinAdaptiveIdleMs, 2 * sorted[sorted.Count / 2]));
                if (measured < delay)
                {
                    delay = measured;
                }
            }
            return delay;
        }

        // gate 안에서 부른다. 끝까지 돈(멈추지 않은) 미리 만들기 한 번의 시간을 남긴다 (AdaptiveIdleDelay)
        private void RecordPrefetchDuration(long ms)
        {
            prefetchDurations.Add(ms);
            while (prefetchDurations.Count > PrefetchDurationCount)
            {
                prefetchDurations.RemoveAt(0);
            }
        }
        // gate 안에서 부른다. 다음에 만들 작업: 모델이 시스템 메시지를 읽어 둔 도깨비의 작업(넣은 차례대로)이 먼저, 없으면 줄 맨 앞. 할 일이 없으면 null.
        // 예전에는 늘 줄 맨 앞을 집어, 멈춰서 쌓인 다른 도깨비의 작업이 모델의 기억을 바꿔 놓았다 (PrefetchSwitchIdleDelay 참고)
        private PrefetchJob NextJob()
        {
            if (prefetchJobs.Count == 0)
            {
                return null;
            }
            if (modelRelicKnown)
            {
                foreach (PrefetchJob job in prefetchJobs)
                {
                    if (job.Template.RelicId == modelRelicId)
                    {
                        return job;
                    }
                }
            }
            return prefetchJobs[0];
        }

        /// <summary>시험용: 미리 만들기가 플레이어 대기 확인을 지나 도깨비를 적기 직전에 부른다 (Verify 가 그 틈에 요청을 끼워 넣는다). 게임에서는 쓰지 않는다.</summary>
        internal Action BeforePrefetchMarkForTest { get; set; }

        /// <summary>시험용: 일꾼이 작업을 다 만들었다고 본 뒤, 줄에서 지우기 전에 부른다 (Verify 가 그 틈에 같은 상황의 다시 만들기를 끼워 넣는다). 게임에서는 null.</summary>
        internal Action<ReactionRequest> AfterPrefetchJobForTest { get; set; }

        /// <summary>시험용: 모델이 시스템 메시지를 읽어 둔 도깨비 (모르면 null).</summary>
        internal int? ModelRelicForTest
        {
            get
            {
                lock (gate)
                {
                    return modelRelicKnown ? modelRelicId : (int?)null;
                }
            }
        }

        // 모델이 이 도깨비의 시스템 메시지를 읽는다 (미리 읽기·모델을 쓰는 요청·미리 만들기가 모델을 부르기 직전)
        private void MarkModelRelic(int relicId)
        {
            lock (gate)
            {
                modelRelicId = relicId;
                modelRelicKnown = true;
            }
        }

        // ---------- 미리 만들기 ----------

        /// <summary>
        /// 이 상황의 대사를 count 개 미리 만들어 보관함(Cache)에 둔다 — 백그라운드로, 기다리지 않는다. 요청이 오면 바로 꺼내 쓰므로 기기가 느려도 응답이 바로 뜬다.
        /// template 은 요청과 같은 모양이어야 한다 (NpcContextBuilder.PrefetchRequests 가 만든다: 탭 인사·안부·관리 감사·손님 관람).
        /// 한가할 때(진행 중인 요청·미리 읽기가 없고, 마지막 것이 끝난 지 PrefetchIdleDelay 가 지났을 때)만 새로 만들기 시작하고, 모델을 쓰는 요청이나 미리 읽기가 오면
        /// 멈췄다가 다시 한가해지면 이어 만든다. 만든 대사는 서로, 그리고 이미 띄운 최근 대사와
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
                if (closed)
                {
                    return; // CloseStore 뒤에는 미리 만들기·다시 만들기를 받지 않는다
                }
                // 같은 상황이 이미 줄에 있으면 더하지 않는다
                string key = isTemplate ? NpcLineCache.TemplateKeyOf(template) : NpcLineCache.KeyOf(template);
                foreach (PrefetchJob queued in prefetchJobs)
                {
                    if (queued.IsTemplate == isTemplate && (isTemplate ? NpcLineCache.TemplateKeyOf(queued.Template) : NpcLineCache.KeyOf(queued.Template)) == key)
                    {
                        // 합쳐도 상황마다 둘 수 있는 수(MaxPerKey)를 넘지 않는다 — 넘기면 다 찬 뒤에도 넣지 못하는 생성을 실패가 쌓일 때까지 거듭한다
                        queued.Count = Math.Max(queued.Count, Math.Min(count, Math.Max(1, Cache.MaxPerKey)));
                        // 일꾼이 이 작업을 다 만들었다고 보고 지우려는 참일 수 있다 — 표시해 두면 지우지 않고 다시 본다 (Copilot 리뷰: 꺼내 간 만큼의 다시 만들기가 사라졌다).
                        // 실패가 쌓여 그만두려던 작업이면 새로 줄에 넣은 것처럼 실패를 다시 센다 (지워진 뒤 새로 넣었을 때와 같게)
                        queued.Refreshed = true;
                        if (queued.Failures >= MaxPrefetchFailures)
                        {
                            queued.Failures = 0;
                        }
                        return;
                    }
                }
                prefetchJobs.Add(new PrefetchJob { IsTemplate = isTemplate, Template = template, Count = Math.Min(count, Math.Max(1, Cache.MaxPerKey)), NextVariation = template.Variation });
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
                if (running != null)
                {
                    prefetchPreempted = true; // 요청·미리 읽기·화면 종료가 멈춘 것 — 시간 초과·검사 실패가 아니다
                }
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
                    // 기다리는 동안에도 다시 고른다 — 그 사이 플레이어가 다른 도깨비를 확대하면 그 도깨비의 작업이 먼저다 (NextJob)
                    PrefetchJob job;
                    lock (gate)
                    {
                        job = NextJob();
                        if (job == null)
                        {
                            prefetchRunning = false;
                            return;
                        }
                    }
                    // 플레이어를 위해 모델을 비워 두는 중(요청·미리 읽기 중이거나 끝난 지 얼마 안 됨 — 다른 도깨비의 작업이면 더 오래)이거나 모델이 아직 안 올라왔으면 기다린다
                    // (SLM 이 없으면 SlmStatus 가 NotLoaded 라 아래에서 끝낸다)
                    int hold = PlayerHoldMs(job);
                    NpcModelStatus status = SlmStatus;
                    if (hold > 0 || status != NpcModelStatus.Ready)
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
                        // 간격은 위 조건에서 읽은 상태로 정한다 (몇 ms 의 차이라 따로 읽어도 결과가 틀어지지 않는다)
                        int poll = hold > 0 ? hold : (status == NpcModelStatus.Stalled ? StalledPollMs : NotReadyPollMs);
                        await Task.Delay(poll).ConfigureAwait(false);
                        continue;
                    }
                    lock (gate)
                    {
                        job.Refreshed = false; // 이번에 돌기 시작한 뒤에 합쳐진 것만 본다
                    }
                    bool finished = await RunPrefetchJob(job).ConfigureAwait(false);
                    Action<ReactionRequest> afterJob = AfterPrefetchJobForTest;
                    if (finished && afterJob != null)
                    {
                        afterJob(job.Template);
                    }
                    if (finished)
                    {
                        lock (gate)
                        {
                            // 다 만들었다고 본 뒤 지우기 전에 같은 상황이 합쳐졌으면(요청이 꺼내 가서 다시 만들기를 넣음) 지우지 않는다 — 다음 바퀴에 모자란 만큼 다시 만든다
                            if (job.Refreshed)
                            {
                                job.Refreshed = false;
                            }
                            else
                            {
                                prefetchJobs.Remove(job); // CancelPrefetch 가 이미 뺐으면 아무것도 하지 않는다
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

        // 한 상황의 대사를 목표 수만큼 만든다. 플레이어 때문에 멈추면(요청·미리 읽기, 쉰 지 얼마 안 됨) false(나중에 이어 만든다), 다 만들었거나 포기했으면 true
        private async Task<bool> RunPrefetchJob(PrefetchJob job)
        {
            // 이 요청이 쓸 수 있는 것만 센다: 칸을 채우면 길이를 넘는 대사(긴 닉네임)는 세지 않고, 최근 띄운 대사와 겹쳐 꺼낼 때 버릴 대사는 버린 뒤 센다
            while (UsableCount(job) < job.Count && job.Failures < MaxPrefetchFailures)
            {
                if (!IsQueued(job))
                {
                    return true; // CancelPrefetch 로 줄에서 빠졌다 — 더 만들지 않는다
                }
                if (HoldingForPlayer(job))
                {
                    return false;
                }
                ReactionRequest request = CopyForPrefetch(job);
                TaskCompletionSource<bool> done = null; // 이 생성이 모델에 들어갔으면 끝날 때 알린다 (같은 상황의 요청이 기다린다)
                using (var cts = new CancellationTokenSource())
                {
                    lock (gate)
                    {
                        prefetchCts = cts;
                        prefetchPreempted = false;
                    }
                    cts.CancelAfter(PrefetchTimeout);
                    if (HoldingForPlayer(job))
                    {
                        // 위에서 본 뒤 prefetchCts 를 걸기 전에 요청·미리 읽기가 왔다 (그쪽의 취소가 이 생성에 닿지 않았다)
                        lock (gate)
                        {
                            prefetchPreempted = true;
                        }
                        cts.Cancel();
                    }
                    else
                    {
                        Action beforeMark = BeforePrefetchMarkForTest;
                        if (beforeMark != null)
                        {
                            beforeMark(); // 시험용 (Verify 의 경합 시험이 여기서 요청을 끼워 넣는다). 게임에서는 null
                        }
                        lock (gate)
                        {
                            // 위의 확인을 지나 정말 모델에 들어가는 생성만 같은 도깨비의 미리 읽기가 맡길 수 있다 (PrepareAsync).
                            // 확인 전에 세우면, 미리 읽기가 맡긴 뒤에 이 생성이 위의 확인에서 스스로 멈춰 둘 다 하지 않게 된다
                            prefetchRelicId = job.Template.RelicId;
                            prefetchGenerating = !prefetchPreempted;
                            // 이 생성이 그 도깨비의 시스템 메시지를 읽는다 — 이어지는 같은 도깨비 작업은 다시 읽지 않는다.
                            // 멈췄는지(prefetchPreempted)와 같은 잠금 안에서 본다: 위의 확인 뒤에 요청 B 가 이 생성을 멈추고 B 를 적었으면,
                            // 여기서 이 도깨비를 적으면 모델에 들어가지도 않은 A 가 B 를 덮어 B 에 전환 지연이 붙는다 (CodeRabbit 리뷰).
                            // 요청은 멈추기(CancelCurrentPrefetch)를 먼저 하고 자기 도깨비를 적으므로, 이 잠금이 먼저면 B 가 나중에 덮는다
                            if (!prefetchPreempted)
                            {
                                MarkModelRelic(job.Template.RelicId); // gate 는 다시 들어갈 수 있는 잠금이다
                                done = new TaskCompletionSource<bool>();
                                prefetchDone = done;
                                prefetchKey = job.IsTemplate ? NpcLineCache.TemplateKeyOf(job.Template) : NpcLineCache.KeyOf(job.Template);
                            }
                        }
                    }
                    try
                    {
                        ReactionResult result;
                        Stopwatch generating = Stopwatch.StartNew();
                        try
                        {
                            result = await slm.GenerateAsync(request, cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            result = ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm);
                        }
                        bool preempted;
                        lock (gate)
                        {
                            prefetchCts = null;
                            prefetchGenerating = false;
                            preempted = prefetchPreempted;
                            prefetchPreempted = false;
                            if (result.FailReason != FailReason.Cancelled)
                            {
                                RecordPrefetchDuration(generating.ElapsedMilliseconds); // 끝까지 돈 생성만 (AdaptiveIdleDelay)
                            }
                        }
                        if (result.FailReason == FailReason.Cancelled)
                        {
                            // CancelPrefetch 로 줄에서 빠졌으면 끝, 요청·미리 읽기가 멈췄으면(다시 이어 만든다) 나중에, 아니면 시간이 넘은 것이다.
                            // 멈춘 것은 지금 쉬는 중인지와 상관없이 실패로 세지 않는다 — 빨리 끝난 요청(미리 만든 대사로 답함)·미리 읽기 뒤에 쉬는 시간이 0 이면
                            // 여기 올 때 이미 한가해 시간 초과로 잘못 세어, 세 번이면 그 상황을 그만 만들었다
                            if (!IsQueued(job))
                            {
                                return true;
                            }
                            if (preempted || HoldingForPlayer(job))
                            {
                                return false;
                            }
                            job.Failures++;
                            continue;
                        }
                        if (!result.IsSuccess || !(job.IsTemplate ? Cache.AddTemplate(job.Template, result, MaxLineLength(job.Template)) : Cache.Add(job.Template, result)))
                        {
                            // 검사에 걸렸거나 보관한 대사·최근 대사와 90% 이상 겹침 → 다음 곁들일 거리·첫 낱말로 다시
                            job.Failures++;
                        }
                        else if (job.IsTemplate)
                        {
                            SaveSoon();
                        }
                    }
                    finally
                    {
                        if (done != null)
                        {
                            lock (gate)
                            {
                                if (prefetchDone == done)
                                {
                                    prefetchDone = null;
                                    prefetchKey = null;
                                }
                            }
                            // 잠금 밖에서, 기다리던 요청은 다른 스레드에서 잇게 한다 (이 일꾼이 요청의 일을 떠맡지 않게)
                            TaskCompletionSource<bool> finished = done;
                            ThreadPool.QueueUserWorkItem(state => finished.TrySetResult(true)); // 기다리지 않는 작업이라 Task.Run 대신 (CS4014 경고 없이)
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>같은 상황을 뒤에서 만들던 것을 기다렸을 때 그 끝: 꺼내 쓴 응답(또는 화면이 바뀌어 버린 응답), 아니면 기다리다 시간이 넘어 쓸 기본 대사 메모.</summary>
        private sealed class SameSituationOutcome
        {
            public NpcResponse Response;
            public string FallbackNote;
        }

        /// <summary>같은 상황을 뒤에서 만들다 기다리다 시간이 넘어 기본 대사로 답했을 때 메모의 앞부분 (기기 시험이 사유를 센다).</summary>
        public const string SameSituationTimeoutNote = "같은 상황을 뒤에서 만드는 중 — 기다리다 시간 초과";

        /// <summary>
        /// 보관함이 비어 모델을 써야 하는 요청이, 같은 상황(보관함 열쇠)의 대사를 뒤에서 만들고 있으면 멈추지 않고 그것이 끝나기를 기다린다 (SameSituationPrefetch).
        /// 끝나 보관함에서 꺼냈으면 그 응답, 기다리다 SlmTimeout 이 넘었으면 기본 대사 메모를 돌려준다 — 이때 새로 만들지 않는다: 만들던 것이 먼저 시작해
        /// 새로 만들어도 그보다 늦게 끝나고, 만들던 것은 멈추지 않아 다음 요청이 꺼내 쓴다. 뒤에서 만드는 것이 없거나, 다른 상황이거나, 끝났는데 쓸 대사가 없으면
        /// (검사에 걸림·최근 대사와 겹침) null — 예전처럼 만들던 것을 멈추고 그 자리에서 만든다.
        /// </summary>
        // 이 요청을 모델로 만들 수 있는지 (SlmReactionGenerator.CanGenerate). 다른 생성기는 알 수 없어 만들 수 있다고 본다.
        // "음..."을 띄울지만 정하는 미리 보기라, 여기서 난 예외(백엔드의 IsLoaded 가 던짐 등)는 가두고 띄우지 않는다 — 생성 자체의 실패는 RunSlm 이 예전처럼
        // 기본 대사로 받는다 (RequestAsync 는 예외를 던지지 않는다, CodeRabbit 리뷰)
        private bool CanGenerate(ReactionRequest request)
        {
            SlmReactionGenerator generator = slm as SlmReactionGenerator;
            if (generator == null)
            {
                return true;
            }
            try
            {
                return generator.CanGenerate(request);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<SameSituationOutcome> WaitForSameSituation(ReactionRequest request, Session session, ThinkingNotice notice)
        {
            Task<bool> building = UsePrefetched ? SameSituationPrefetch(request) : null;
            if (building == null)
            {
                NpcResponse ready = UsePrefetched ? ServePrefetched(request) : null;
                return ready != null ? new SameSituationOutcome {Response = ready} : null;
            }
            notice.Show(session.Token); // 뒤에서 만드는 것을 기다린다 — 기다리는 동안 "음..."
            Task first;
            using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
            {
                first = await Task.WhenAny(building, Task.Delay(SlmTimeout, waitCts.Token)).ConfigureAwait(false);
                waitCts.Cancel();
            }
            if (session.Token.IsCancellationRequested)
            {
                return new SameSituationOutcome { Response = NpcResponse.Discard(null, session.CancelNote()) };
            }
            if (first != building)
            {
                return new SameSituationOutcome { FallbackNote = SameSituationTimeoutNote + "(" + (int)SlmTimeout.TotalMilliseconds + "ms) → 기본 대사" };
            }
            NpcResponse built = ServePrefetched(request, PrefetchWaitedNote);
            return built != null ? new SameSituationOutcome { Response = built } : null;
        }

        /// <summary>
        /// 이 요청과 같은 상황(보관함 열쇠)의 대사를 뒤에서 만들고 있으면, 그 생성이 끝나 보관함에 넣었을 때 끝나는 작업. 아니면 null.
        /// <para>2026-11-27 ver9 기기 시험(장면 사이 10초): 그 자리에서 만든 인사 10건 중 7건이 같은 도깨비의 대사를 뒤에서 만드는 중에 왔다. 요청은 그 생성을 멈추고
        /// 같은 대사를 처음부터 다시 만들었고(약 10초), 다시 만들기는 다음 요청에 또 멈춰 끝나지 못해 회차마다 보관함이 비었다. 같은 상황이면 멈추지 말고 남은 시간만 기다린다.</para>
        /// </summary>
        private Task<bool> SameSituationPrefetch(ReactionRequest request)
        {
            string templateKey = NpcLineCache.TemplateKeyOf(request);
            string key = NpcLineCache.KeyOf(request);
            lock (gate)
            {
                // prefetchGenerating 이 아니라 신호로 본다 — 생성이 끝나 보관함에 넣기 전(prefetchGenerating 은 이미 false)에 온 요청도 기다려 꺼내 쓴다.
                // 신호는 모델에 들어간 생성에만 세우고, 보관함에 넣은 뒤(finally)에 거둔다. 다른 요청·미리 읽기가 멈춘 생성(prefetchPreempted)은 기다리지 않는다
                if (prefetchPreempted || prefetchDone == null || prefetchKey == null)
                {
                    return null;
                }
                return prefetchKey == templateKey || prefetchKey == key ? prefetchDone.Task : null;
            }
        }

        // 한 상황에서 검사에 걸리거나 겹쳐 버린 생성이 이만큼 되면 그 작업은 그만둔다 (다음에 그 상황을 쓰면 다시 줄에 들어간다)
        private const int MaxPrefetchFailures = 3;

        // 그 작업의 상황에 지금 쓸 수 있는 보관 대사 수. 최근 띄운 대사(그 시나리오 + 견본이 가져온 것)와 겹치는 대사는 버린 뒤 센다
        private int UsableCount(PrefetchJob job)
        {
            ReactionRequest t = job.Template;
            var recent = new List<string>();
            if (t.RecentLines != null)
            {
                recent.AddRange(t.RecentLines);
            }
            recent.AddRange(RecentLinesOf(t.RelicId, t.Input));
            if (!job.IsTemplate)
            {
                return Cache.Count(t, recent);   // 30분짜리 보관함이라 파일에 남기지 않는다
            }
            int before = Cache.TemplateCount(t);
            int usable = Cache.UsableTemplateCount(t, MaxLineLength(t), recent);
            if (Cache.TemplateCount(t) < before)
            {
                SaveSoon(); // 버린 대사가 파일에 남아 다음에 켤 때 다시 읽혀 칸을 차지하지 않게
            }
            return usable;
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
                RecentLines = new List<string>(),
                Background = true   // 백엔드가 플레이어의 일을 먼저 모델에 넣고, 이 생성은 나눠 읽다가 비켜 준다
            };
            if (t.RecentLines != null)
            {
                copy.RecentLines.AddRange(t.RecentLines);
            }
            copy.RecentLines.AddRange(RecentLinesOf(t.RelicId, t.Input));
            copy.RecentLines.AddRange(job.IsTemplate ? Cache.TemplateLinesFor(t) : Cache.LinesFor(t));
            PickFreshStarter(copy, copy.RecentLines);
            return copy;
        }

        /// <summary>
        /// 요청의 곁들일 거리 번호(Variation)를, 첫 낱말이 피할 대사(최근 띄운 대사·보관한 대사)의 첫머리와 겹치지 않고 이 시나리오에서 최근에 준 첫 낱말과도
        /// 다른 번호로 옮긴다. 다 겹치면 덜 겹치는 것(최근에 준 첫 낱말만 같은 것)을, 그것도 없으면 처음 번호를 쓴다. 고른 첫 낱말은 이 시나리오의 최근 첫 낱말로 적는다.
        /// <para>작은 모델은 첫 낱말이 같으면 뒤도 거의 같게 쓴다. 게임은 같은 시나리오의 요청마다 번호를 1씩 늘리지만, 앱을 다시 켜면 그날의 같은 번호부터 다시 세고
        /// 다시 만들기(번호 + 1)와도 겹쳐, 이미 띄운 대사의 첫 낱말을 다시 받아 같은 대사를 냈다 — 2026-11-23 기기 시험에서 SLM 실패의 대부분(21·26건)이 '반복'이었다.</para>
        /// </summary>
        private void PickFreshStarter(ReactionRequest request, List<string> avoidLines)
        {
            if (request == null || PromptAssembler.Starter(request) == null)
            {
                return; // 첫 낱말을 쓰지 않는 요청 (지난 대화 꺼내기 등)
            }
            string key = ScenarioKey(request.RelicId, request.Input);
            List<string> tried;
            lock (gate)
            {
                tried = triedStarters.TryGetValue(key, out tried) ? new List<string>(tried) : new List<string>();
            }
            // 흐려진 안부 대사는 코드가 붙인 앞 문장("몸이 흐려졌어.")으로 시작한다 — 그 문장을 떼고 부탁의 말머리('살살')를 견준다 (CodeRabbit 리뷰).
            // 그렇지 않으면 그 첫 낱말이 최근 3번(triedStarters)에서 빠지는 순간, 최근 대사에 남아 있는데도 다시 고른다.
            // 출력 검사가 앞 문장까지 포함한 전체 대사로 반복을 보므로(PromptAssembler.SimilarLine), 그 번호에서 붙을 앞 문장과 같은 앞 문장 뒤에 쓴 첫 낱말만 피한다 —
            // 앞 문장은 번호를 4로 나눈 나머지, 첫 낱말은 5로 나눈 나머지로 정해져 20번이면 모든 짝을 본다 (2026-11-24)
            bool askPrefix = NpcLineChecks.NeedsAsk(request);
            int search = askPrefix ? AskStarterSearch : StarterSearch;
            bool atRandom = PromptAssembler.PicksStarterAtRandom(request);
            int start = request.Variation;
            int best = start;
            int bestScore = int.MaxValue;
            string bestStarter = null;
            var ties = new List<int>();           // 무작위로 고르는 요청: 가장 덜 겹치는 첫 낱말마다 그 첫 번호
            var tieStarters = new List<string>();
            var distinct = new List<string>();
            for (int k = 0; k < search; k++)
            {
                int variation = unchecked(start + k);
                string starter = PromptAssembler.StarterAt(request, variation);
                if (starter == null)
                {
                    break;
                }
                if (!distinct.Contains(starter))
                {
                    distinct.Add(starter);
                }
                string prefixShape = null;
                if (askPrefix)
                {
                    string ignored;
                    string prefix = PromptAssembler.LinePrefixAt(request, variation);
                    prefixShape = prefix != null ? PromptAssembler.AskPrefixShape(prefix + " ", out ignored) : null;
                }
                int score = (StartsAnyWith(avoidLines, starter, askPrefix, prefixShape) ? 2 : 0) + (tried.Contains(starter) ? 1 : 0);
                if (score < bestScore) // 같으면 앞 번호(게임이 준 번호에 가까운 것)
                {
                    best = variation;
                    bestScore = score;
                    bestStarter = starter;
                    ties.Clear();
                    tieStarters.Clear();
                }
                if (score == bestScore && !tieStarters.Contains(starter))
                {
                    ties.Add(variation);
                    tieStarters.Add(starter);
                }
            }
            if (bestStarter == null)
            {
                return;
            }
            if (atRandom && ties.Count > 1)
            {
                // 관리 감사·선명한 안부(깨끗하게·훨씬·몸이·덕분에·정말·개운하게): 차례로 돌리지 않고 똑같이 덜 겹치는 것 가운데 무작위로 (2026-11-26)
                int pick;
                lock (gate)
                {
                    pick = (StarterRandom ?? (StarterRandom = new Random())).Next(ties.Count);
                }
                best = ties[pick];
                bestStarter = tieStarters[pick];
            }
            request.Variation = best;
            lock (gate)
            {
                List<string> list;
                if (!triedStarters.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    triedStarters[key] = list;
                }
                list.Remove(bestStarter);
                list.Add(bestStarter);
                // 첫 낱말 하나는 늘 비워 둔다 (목록이 작으면 모두 '최근'이 되어 고를 것이 없다)
                // 무작위로 고르는 요청은 바로 앞에 준 것 하나만 피한다 — 최근 셋을 피하면(다른 첫 낱말처럼) 고를 것이 적어 차례로 도는 것에 가깝다
                int keep = atRandom ? 1 : Math.Min(TriedStarterCount, Math.Max(1, distinct.Count - 1));
                while (list.Count > keep)
                {
                    list.RemoveAt(0);
                }
            }
        }

        // 대사가 그 첫 낱말로 시작하는지. 앞의 말줄임·따옴표·띄어쓰기는 건너뛴다. askPrefix 면 흐려진 안부의 앞 문장을 먼저 떼고, 그 앞 문장의 꼴이
        // prefixShape(이번에 붙을 앞 문장)와 같은 대사만 본다 — 앞 문장이 다르거나 없는 대사는 전체로 보면 다른 대사다 (다른 요청은 그대로 견준다)
        private static bool StartsAnyWith(List<string> lines, string starter, bool askPrefix, string prefixShape)
        {
            if (lines == null || string.IsNullOrEmpty(starter))
            {
                return false;
            }
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                string body = line;
                if (askPrefix)
                {
                    if (PromptAssembler.AskPrefixShape(line, out body) != prefixShape || prefixShape == null)
                    {
                        continue;
                    }
                }
                string head = body.TrimStart(' ', '…', '.', '"', '\'', '“', '‘', '~', '-');
                if (head.StartsWith(starter, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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
        /// 뒤에서 만들던 미리 만들기·다시 만들기는 멈추고, 이 준비가 끝난 뒤 PrefetchIdleDelay 동안 새로 시작하지 않는다.
        /// </summary>
        public async Task<NpcPrepareOutcome> PrepareAsync(int relicId, CancellationToken ct = default(CancellationToken))
        {
            lastPrepareDeferred = false; // 아무것도 하지 않고 끝나도 앞 미리 읽기의 표시가 남지 않게
            lastPrepareOutcome = NpcPrepareOutcome.Skipped;
            if (!SlmEnabled || SlmStatus != NpcModelStatus.Ready) // 예외를 던지지 않는다 (SlmStatus 가 삼킨다)
            {
                return NpcPrepareOutcome.Skipped;
            }
            NpcPrepareOutcome outcome = NpcPrepareOutcome.Completed;
            var mine = new Session(ct);
            Session previous;
            bool sameSpiritBuilding;
            lock (gate)
            {
                previous = preparing;
                preparing = mine; // 같은 잠금 안에서 세운다 — 아래에서 본 뒤에 일꾼이 다른 도깨비의 생성을 새로 시작하지 못한다 (PlayerHoldMs)
                preparingRelicId = relicId;
                // 모델이 이 도깨비의 시스템 메시지를 읽는다 — 끝난 뒤 뒤에서 만들기는 이 도깨비의 작업을 먼저 고른다 (NextJob)
                modelRelicId = relicId;
                modelRelicKnown = true;
                sameSpiritBuilding = prefetchGenerating && !prefetchPreempted && prefetchRelicId == relicId;
            }
            lastPrepareDeferred = sameSpiritBuilding;
            if (previous != null)
            {
                previous.Cancel("다른 도깨비를 준비");
            }
            // 플레이어가 도깨비를 확대했다 — 뒤에서 만들던 것을 멈춘다 (플레이어의 일이 늘 먼저다). 백엔드는 뒤에서 만들기를 한 조각 안에 비키게 하고
            // (LlmUnityOptions.BackgroundStepChars), 이 미리 읽기를 그보다 먼저 모델에 넣는다. 이 미리 읽기가 끝나고 PrefetchIdleDelay 가 지날 때까지 새로 만들지도 않는다
            // (확대해 두고 선택지를 고르는 사이에 끼어들지 않게).
            // 단, 뒤에서 만드는 것이 <b>이 도깨비</b>의 대사면 멈추지도 미리 읽지도 않는다 (2026-11-25): 그 생성이 이 도깨비의 시스템 메시지를 이미 읽고 있어
            // 미리 읽기는 할 일이 없는데(실기기 0초), 멈추면 만들던 대사만 버린다. 우선권은 그대로다 — 이어서 모델이 필요한 요청이 오면 요청이 멈추고(Handle),
            // 백엔드는 한 조각 안에 비켜 준다. 미리 만든 대사로 답하는 요청이면 멈추지 않아 그 생성이 끝까지 간다
            if (!sameSpiritBuilding)
            {
                CancelCurrentPrefetch();
            }
            try
            {
                if (sameSpiritBuilding)
                {
                    outcome = NpcPrepareOutcome.Deferred;
                }
                else
                {
                    await slm.PrepareAsync(relicId, mine.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                // 미리 읽기는 덤이다. 취소·실패해도 다음 요청이 시스템 메시지부터 읽는다 — 다만 어떻게 끝났는지는 돌려준다(기기 시험이 실패를 '이미 읽힘'으로 적지 않게)
                outcome = e is OperationCanceledException || mine.Token.IsCancellationRequested ? NpcPrepareOutcome.Cancelled : NpcPrepareOutcome.Failed;
            }
            finally
            {
                lastPrepareOutcome = outcome;
                lock (gate)
                {
                    // 끝난 때를 먼저 적는다 — preparing 을 비운 틈에 미리 만들기가 시작하지 않게 (같은 잠금 안). 미리 읽기는 모델을 쓰는 일이다
                    // (같은 도깨비의 생성에 맡겨 건너뛰었으면 모델을 쓰지 않았다)
                    lastPlayerActivityMs = NowMs();
                    if (!sameSpiritBuilding)
                    {
                        lastModelActivityMs = lastPlayerActivityMs;
                    }
                    if (preparing == mine)
                    {
                        preparing = null;
                    }
                }
                mine.Dispose();
            }
            return outcome;
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
        // 남은 시간이 이보다 적어도 생성은 이만큼 기다린다 (0 이면 Task.Delay 가 바로 끝나 모델을 부르자마자 시간 초과로 버린다)
        private static readonly TimeSpan MinSlmBudget = TimeSpan.FromMilliseconds(1);

        // 시간 초과 메모에 '같은 상황을 기다린 시간'을 적는 기준 (그보다 짧으면 보관함을 본 시간일 뿐이다)
        private static readonly TimeSpan SameSituationNoteThreshold = TimeSpan.FromMilliseconds(50);

        // timeout: 이 생성을 기다릴 시간 (보통 SlmTimeout, 같은 상황을 기다린 뒤면 그 남은 시간)
        private async Task<ReactionResult> RunSlm(ReactionRequest request, Session session, TimeSpan timeout)
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
                Task waiting = Task.Delay(timeout, delayCts.Token);
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
