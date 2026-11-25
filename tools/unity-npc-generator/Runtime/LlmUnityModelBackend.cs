using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>LLMUnity 백엔드가 쓰는 형식 제약.</summary>
    public enum LlmUnityGrammar
    {
        Gbnf,       // prompt.Grammar — 행동과 대상의 짝까지 막는다 (기본)
        JsonSchema, // prompt.JsonSchema — GBNF 가 기기에서 안 먹으면 이것으로
        None        // 제약 없음 — 출력 검사만 믿는다
    }

    /// <summary>LLMUnity 백엔드 설정. 생성 설정의 기본값은 프롬프트 시험(run.py)과 같다.</summary>
    public sealed class LlmUnityOptions
    {
        public LlmUnityOptions()
        {
            Model = "";
            NumGpuLayers = 0;
            NumThreads = -1;
            ContextSize = 2048;
            Temperature = 0.7f;
            MaxTokens = 40;
            Grammar = LlmUnityGrammar.Gbnf;
            DisableThinking = true;
            WarmUp = true;
            CancelGrace = TimeSpan.FromSeconds(2);
            CancelHold = TimeSpan.FromMinutes(5);
            SaveSystemState = true;
            StateVersion = "1";
            BackgroundStepChars = 48;
        }

        /// <summary>
        /// 뒤에서 만드는 생성(NpcPrompt.Background)이 프롬프트를 한 번에 읽을 글자 수. 0 이면 나누지 않는다.
        /// 프롬프트를 읽는 동안에는 생성을 멈출 수 없어(LlamaLib 2.0.5 결함, Run 참고) 예전에는 뒤에서 만들기가 모델을 먼저 잡으면 플레이어의 요청·미리 읽기가
        /// 그 읽기(폰에서 7~20초)가 끝날 때까지 기다렸다 — 누가 먼저 모델을 잡느냐에 따라 미리 읽기가 0초도 되고 45~68초도 됐다 (2026-11-24 기기 시험).
        /// 그래서 뒤에서 만들기는 시스템 메시지·사용자 메시지를 이 글자 수씩 늘려 가며 1토큰 생성으로 읽히고(llama.cpp 가 앞서 읽은 부분을 다시 쓰므로 새로 읽는 것은 한 조각뿐),
        /// 조각 사이마다 플레이어의 일이 기다리거나 취소됐으면 멈추지 않고 그 자리에서 비켜 준다. 플레이어가 기다리는 시간은 많아야 한 조각(폰에서 1~2초) 또는 기억 파일 되살리기·저장 한 번(이것도 멈출 수 없는 짧은 일)이고,
        /// 그때까지 읽은 부분은 모델에 남아 이어지는 요청이 다시 읽지 않는다. 대신 뒤에서 만들기는 조각마다 끝맺음 몇 토큰을 더 읽어 조금 느려진다
        /// (그래서 요청 관리자의 PrefetchTimeout 기본을 60초로 늘렸다). 앞 프롬프트와 같은 앞부분(모든 도깨비에 같은 규칙)은 조각으로 다시 부르지 않고,
        /// 시스템 메시지 조각 뒤에는 미리 읽기와 같은 프롬프트(시스템 메시지 + '준비')를 한 번 읽혀 그 기억을 저장한다(SaveSystemState).
        /// </summary>
        public int BackgroundStepChars { get; set; }

        /// <summary>GGUF 파일 이름. 에디터의 LLM 모델 관리자에 등록한 이름 그대로 (예: "gemma-4-E2B-it-Q4_K_M.gguf").</summary>
        public string Model { get; set; }

        /// <summary>
        /// GPU 로 올릴 층 수 (0 = CPU 만). iOS 는 Metal 이라 크게(예: 99) 두고, 안드로이드는 0 으로 시작한다
        /// (LlamaLib 2.0.5 에서 안드로이드 Vulkan 이 빠졌다). 실기기에서 재 보고 정한다.
        /// </summary>
        public int NumGpuLayers { get; set; }

        /// <summary>CPU 스레드 수 (-1 = 모두). 폰은 고성능 코어 수(보통 4 안팎)가 더 빠를 수 있다.</summary>
        public int NumThreads { get; set; }

        /// <summary>컨텍스트 크기(토큰). 프롬프트가 1000 토큰 안쪽이고 대사가 64 토큰 이내라 2048 이면 된다. 작을수록 메모리를 덜 쓴다.</summary>
        public int ContextSize { get; set; }

        public float Temperature { get; set; }

        /// <summary>
        /// 한 번에 생성할 최대 토큰. JSON 뼈대 약 20토큰 + 40자 대사 약 25토큰이라 64 로 둔다. 넘치면 대사 중간에서 끊기고,
        /// 출력 검사기가 끝난 문장까지만 쓴다. 줄이면 생성 시간의 상한이 줄지만 끊기는 대사가 는다.
        /// </summary>
        public int MaxTokens { get; set; }

        /// <summary>정하면 결과가 재현된다. 없으면 생성마다 새 무작위 seed 를 준다 (<see cref="SeedForCall"/>).</summary>
        public int? Seed { get; set; }

        private static readonly Random SeedSource = new Random();

        /// <summary>
        /// 이번 생성에 줄 seed. Seed 를 정했으면 그 값, 아니면 1 이상의 무작위 값.
        /// LLMUnity 3.0.3 은 seed 0 을 "0 = random"이라 적지만, 0 을 넘긴 기기 시험 두 판(2026-11-18, 온도 0.7)의 대사가 100줄 모두 글자 하나 다르지 않았다 —
        /// llama.cpp 는 0 을 정해진 seed 로 쓰는 것으로 보인다. 같은 프롬프트에 늘 같은 대사가 나와, 같은 상황을 다시 만들면 최근 대사와 같아 '반복'으로 버려졌다 (2026-11-23).
        /// </summary>
        public int SeedForCall()
        {
            if (Seed.HasValue)
            {
                return Seed.Value;
            }
            lock (SeedSource)
            {
                return SeedSource.Next(1, int.MaxValue);
            }
        }

        public LlmUnityGrammar Grammar { get; set; }

        /// <summary>생각(추론) 모드를 끈다. 켜 두면 Gemma 4 가 출력 한도를 생각에 다 쓰고 대사를 비운다.</summary>
        public bool DisableThinking { get; set; }

        /// <summary>LoadAsync 에서 짧게 한 번 생성해 둔다. 첫 요청이 준비 작업(GPU 셰이더 등) 때문에 시간 초과되지 않게 한다.</summary>
        public bool WarmUp { get; set; }

        /// <summary>취소한 뒤 모델이 멈추기를 기다리는 최대 시간. 지나면 부른 쪽에는 바로 취소로 돌려준다 (모델은 CancelHold 동안 계속 붙잡아 둔다).</summary>
        public TimeSpan CancelGrace { get; set; }

        /// <summary>
        /// 취소했는데 CancelGrace 안에 멈추지 않은 생성이 실제로 끝날 때까지 다음 요청을 모델에 보내지 않는 최대 시간.
        /// 프롬프트를 읽는 동안의 취소는 첫 글자가 나올 때까지 미루므로(LlamaLib 2.0.5 결함, 아래 Run 참고) 그 생성은 남은 프롬프트를 다 읽어야 끝난다.
        /// 그 사이 보낸 요청은 llama.cpp 의 줄에 쌓여 취소되지 않은 채 하나씩 끝까지 계산된다 (2026-11-18 S22: 미리 만들기 시간 초과 약 40번이 73분 동안 줄을 이뤘다).
        /// 그동안 온 요청은 모델에 닿지 않고 자기 시간 초과로 끝난다(요청 관리자는 기본 대사).
        /// 지나도 안 끝나면 멈춘 모델로 보고(IsStalled, IsLoaded 는 false) 다음 요청을 모델에 보내지 않는다 — 묶인 슬롯 뒤에 보낸 요청도 끝나지 않기 때문이다.
        /// 멈춘 생성이 뒤늦게 끝나면 다시 쓴다. 끝내 안 끝나면 앱을 다시 켜야 한다.
        /// </summary>
        public TimeSpan CancelHold { get; set; }

        /// <summary>
        /// 시스템 메시지를 읽은 모델의 기억(KV 캐시)을 파일로 저장해 두고, 다음에 같은 시스템 메시지를 미리 읽을 때 읽는 대신
        /// 파일에서 되살린다. 앱을 다시 켜도 도깨비 다섯의 시스템 메시지(각 수백 토큰)를 다시 읽지 않아 첫 대사가 빨라진다.
        /// 모델의 기억 칸(슬롯)은 하나라 마지막에 읽은 도깨비만 남는데, 이것으로 A→B→A 처럼 돌아온 도깨비도 다시 읽지 않는다 (2026-11-25, 기기에서 바뀔 때마다 15~20초).
        /// 미리 읽기 없이 다른 도깨비의 생성이 와도(요청·미리 만들기) 먼저 되살린다. 저장은 미리 읽기(시스템 메시지 + 1토큰)가 끝났을 때만 한다.
        /// 세션이 ILlmUnityStateStore 를 지원하지 않거나 저장·복원이 실패하면 그냥 읽는다.
        /// </summary>
        public bool SaveSystemState { get; set; }

        /// <summary>저장한 기억의 버전. 시스템 메시지 밖의 것(템플릿·라이브러리)이 바뀌어 옛 기억을 버려야 하면 올린다.</summary>
        public string StateVersion { get; set; }

        /// <summary>첫 실행에 모델을 내려받을 때(LLMUnity 의 Download on Build) 진행률 0~1.</summary>
        public Action<float> DownloadProgress { get; set; }
    }

    /// <summary>올라온 모델 하나 (LLMUnity 의 LLM + LLMAgent). 모든 메서드는 Unity 메인 스레드에서 불린다.</summary>
    public interface ILlmUnitySession
    {
        /// <summary>
        /// 대화 기록 없이 한 번 묻고 답을 그대로 돌려준다. grammar 가 null 이면 형식 제약 없이.
        /// onFirstToken 은 첫 글자가 나왔을 때 한 번 부른다 (첫 응답 시간 측정용, null 일 수 있다).
        /// </summary>
        Task<string> ChatOnceAsync(string system, string user, string grammar, int maxTokens, LlmUnityOptions options, Action onFirstToken);

        /// <summary>진행 중인 생성을 멈춘다.</summary>
        void CancelRequests();

        /// <summary>
        /// 모델을 내리고 메모리를 돌려준다. 다시 쓰려면 새로 StartAsync 한다. 모델이 <b>실제로</b>지워진 뒤에 끝난다 —
        /// Unity 의 Object.Destroy 는 프레임 끝에 지우므로, 부르자마자 끝내면 다음 StartAsync 가 앞 모델이 남은 채로 새 모델을 올린다.
        /// </summary>
        Task DestroyAsync();
    }

    /// <summary>
    /// 선택 기능: 세션이 이것도 구현하면, 시스템 메시지를 읽어 둔 모델의 기억을 이름(key)으로 저장하고 되살린다.
    /// 실패는 예외 대신 false 로 알린다 (기억이 없거나 깨졌거나 모델이 다름).
    /// </summary>
    public interface ILlmUnityStateStore
    {
        /// <summary>지금 모델의 기억을 key 로 저장한다. 저장했으면 true.</summary>
        Task<bool> SaveStateAsync(string key);

        /// <summary>key 로 저장해 둔 기억을 모델에 되살린다. 되살렸으면 true.</summary>
        Task<bool> LoadStateAsync(string key);
    }

    /// <summary>LLMUnity 로 모델을 올리는 쪽. Unity 에서는 LlmUnityRuntime(Unity/ 폴더)이 구현한다. Unity 메인 스레드에서 불린다.</summary>
    public interface ILlmUnityRuntime
    {
        Task<ILlmUnitySession> StartAsync(LlmUnityOptions options);
    }

    /// <summary>마지막 생성 호출의 측정값.</summary>
    public sealed class LlmUnityCall
    {
        public LlmUnityCall(long firstTokenMs, long elapsedMs, int replyChars)
        {
            FirstTokenMs = firstTokenMs;
            ElapsedMs = elapsedMs;
            ReplyChars = replyChars;
        }

        /// <summary>요청을 넘긴 때부터 첫 글자가 나올 때까지 (첫 응답 시간). 모르면 -1.</summary>
        public long FirstTokenMs { get; private set; }

        /// <summary>요청을 넘긴 때부터 답이 다 올 때까지 (전체 생성 시간).</summary>
        public long ElapsedMs { get; private set; }

        public int ReplyChars { get; private set; }
    }

    /// <summary>
    /// 기기 안에서 도는 모델 백엔드 (LLMUnity, llama.cpp 기반). 안드로이드·iOS 출시 빌드용이다.
    ///
    /// <para>LLMUnity 를 직접 부르는 부분은 ILlmUnityRuntime · ILlmUnitySession 뒤로 감췄다(Unity/LlmUnityRuntime.cs).
    /// 이 클래스는 Unity 없이도 컴파일되고, 아래를 맡는다.</para>
    /// <list type="bullet">
    /// <item>메인 스레드: LLMUnity 호출은 모두 Unity 메인 스레드로 넘겨서 한다. 요청 관리자가 시간 초과로 다른 스레드에서
    /// 취소해도 안전하다. 그래서 반드시 메인 스레드에서 만든다(그때의 SynchronizationContext 를 기억한다).</item>
    /// <item>한 번에 하나씩: 기기의 모델은 하나라 생성 요청을 줄 세운다.</item>
    /// <item>취소: 부른 쪽은 CancelGrace 이상 기다리지 않는다. 모델에 멈추라고(CancelRequests) 하는 것은 첫 글자가 나온 뒤(생성 단계)에만 한다.
    /// 프롬프트를 읽는 중에 들어온 취소는 첫 글자가 나올 때까지 미룬다 — LlamaLib 2.0.5 는 그 구간에 멈추라고 하면 슬롯이 영구히 묶여
    /// 그 호출과 이후의 모든 호출이 돌아오지 않는다. 미리 읽기(1토큰)는 스스로 곧 끝나므로 멈추라고 하지 않는다.
    /// 그 생성이 실제로 끝날 때까지(최대 CancelHold, 또는 Unload) 다음 요청을 모델에 보내지 않는다 — llama.cpp 줄에 취소되지 않는 요청이 쌓이지 않게.
    /// CancelHold 가 지나도 안 끝나면 멈춘 모델로 보고 IsLoaded 를 false 로 둔다.</item>
    /// <item>올리기·내리기: LoadAsync 가 모델을 올리고(실패하면 예외, IsLoaded 는 false) 미리 한 번 생성해 둔다.
    /// Unload 는 바로 돌아온다 — 새 요청을 막고(IsLoaded false) 진행 중인 요청은 부른 쪽에 취소로 돌려준다. 모델을 실제로 지우는 것(Destroy)은
    /// 모델 안에서 돌던 일(생성·기억 저장·복원)이 끝난 뒤에 한다. LlamaLib 2.0.5 는 진행 중인 생성이 있을 때 지우면 메인 스레드에서 지금 읽던
    /// 프롬프트 묶음이 끝날 때까지 기다리고(PC 에서 9초), 지운 뒤 그 생성의 스레드가 지워진 모델을 건드려 앱이 죽기도 한다 (2026-11-21 PC 재현 phase 4).
    /// 그동안 생성은 남은 프롬프트를 마저 읽고 첫 글자에서 미뤄 둔 취소를 받아 곧 끝난다. 그 사이 LoadAsync 는 앞 모델이 지워지기를 기다린다
    /// (메모리에 모델이 둘 올라가지 않게). 올리는 도중에 Unload 하면 올린 모델을 바로 내린다.</item>
    /// <item>미리 읽기: PrepareAsync 가 시스템 메시지로 1토큰만 생성해 llama.cpp 가 그 앞부분을 기억하게 한다. 마지막으로 읽은
    /// 시스템 메시지가 같으면(같은 도깨비와 이어 말함) 아무것도 하지 않는다.</item>
    /// </list>
    /// </summary>
    public sealed class LlmUnityModelBackend : INpcModelBackend, INpcModelHealth
    {
        private readonly ILlmUnityRuntime runtime;
        private readonly LlmUnityOptions options;
        private readonly SynchronizationContext unity;
        private readonly SemaphoreSlim oneAtATime = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim loading = new SemaphoreSlim(1, 1);
        private readonly object gate = new object();
        private ILlmUnitySession session;
        private ILlmUnitySession stalled; // 취소한 생성이 CancelHold 가 지나도 안 끝난 세션. gate 안에서 읽고 쓴다
        // 모델 안에서 도는 일(생성·기억 저장·복원)과 그 세션. 끝나기 전에는 그 세션을 지우지 않는다. gate 안에서 읽고 쓴다
        private readonly List<ModelWork> working = new List<ModelWork>();
        // Unload 했지만 아직 지우지 않은 세션 (안에서 돌던 일이 끝나기를 기다림). 미뤄 둔 취소는 이 세션에도 보낸다. gate 안에서 읽고 쓴다
        private readonly List<ILlmUnitySession> retiring = new List<ILlmUnitySession>();
        // 내린 세션들이 모두 지워지면 끝나는 일. LoadAsync 가 기다린다. gate 안에서 읽고 쓴다
        private Task retired = Task.FromResult(true);
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private long generation;
        private volatile LlmUnityCall lastCall;
        private volatile LlmUnityCall lastPrepare;
        private volatile bool lastPrepareRestored;
        private volatile bool lastCallRestored;
        private volatile bool lastPrepareSaved;
        private int foregroundWaiting; // 모델 줄을 기다리는 플레이어의 일(요청·미리 읽기) 수 (Acquire). Interlocked 로만 읽고 쓴다
        private CancellationTokenSource backgroundYield; // 지금 모델을 잡은 뒤에서 만들기에 비키라고 알리는 것. gate 안에서 읽고 쓴다
        private int stateLoads;
        private int stateSaves;

        // 마지막으로 끝까지 읽힌 시스템 메시지와 그 세션. oneAtATime 안에서만 읽고 쓴다
        private ILlmUnitySession readSession;
        private string readSystem;
        // 마지막으로 끝까지 돈 호출의 시스템 메시지(뒤에서 만들기의 조각이면 그 앞부분). 모델에 그 앞부분이 남아 있다고 아는 것 — 모르면 null.
        // 뒤에서 만들기가 그것과 같은 앞부분을 조각으로 다시 부르지 않게 한다 (ReadInSteps). readSession 의 세션 것이고, oneAtATime 안에서만 읽고 쓴다
        private string knownSystem;

        /// <summary>Unity 메인 스레드에서 만든다.</summary>
        public LlmUnityModelBackend(ILlmUnityRuntime runtime, LlmUnityOptions options)
                : this(runtime, options, SynchronizationContext.Current)
        {
        }

        /// <param name="unityThread">LLMUnity 를 부를 스레드. null 이면 부른 스레드에서 바로 부른다 (시험용).</param>
        public LlmUnityModelBackend(ILlmUnityRuntime runtime, LlmUnityOptions options, SynchronizationContext unityThread)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException("runtime");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            this.runtime = runtime;
            this.options = options;
            unity = unityThread;
        }

        /// <summary>모델이 올라와 있고 멈추지 않았는지 (IsStalled 면 false — 요청 관리자는 모델을 부르지 않고 기본 대사로 답한다).</summary>
        public bool IsLoaded
        {
            get
            {
                lock (gate)
                {
                    return session != null && stalled != session;
                }
            }
        }

        /// <summary>
        /// 취소한 생성이 CancelHold 가 지나도 끝나지 않아 모델을 쓰지 않는 중인지. 그 생성이 뒤늦게 끝나면 false 로 돌아온다.
        /// 그동안 LoadAsync 는 InvalidOperationException 을 던진다 — 다시 쓰려면 Unload 한 뒤 LoadAsync 한다.
        /// </summary>
        public bool IsStalled
        {
            get
            {
                lock (gate)
                {
                    return session != null && stalled == session;
                }
            }
        }

        /// <summary>올라옴·안 올라옴·멈춤을 한 번에 읽는다 (IsLoaded 와 IsStalled 를 따로 읽으면 그 사이에 바뀔 수 있다).</summary>
        public NpcModelStatus Status
        {
            get
            {
                lock (gate)
                {
                    return session == null ? NpcModelStatus.NotLoaded : (stalled == session ? NpcModelStatus.Stalled : NpcModelStatus.Ready);
                }
            }
        }

        /// <summary>마지막 생성 호출의 측정값. 아직 없으면 null.</summary>
        public LlmUnityCall LastCall
        {
            get { return lastCall; }
        }

        /// <summary>마지막으로 시스템 메시지를 실제로 미리 읽힌 호출의 측정값 (ElapsedMs ≈ 시스템 메시지를 읽는 시간). 아직 없으면 null.</summary>
        public LlmUnityCall LastPrepare
        {
            get { return lastPrepare; }
        }

        /// <summary>마지막 미리 읽기가 저장해 둔 기억을 되살린 것이면 true (실제로 읽은 것이 아님).</summary>
        public bool LastPrepareRestored
        {
            get { return lastPrepareRestored; }
        }

        /// <summary>모델 줄을 기다리는 플레이어의 일(요청·미리 읽기) 수. 시험이 '플레이어의 일이 줄에 섰다'를 확인할 때 쓴다.</summary>
        internal int ForegroundWaiting
        {
            get { return Volatile.Read(ref foregroundWaiting); }
        }

        /// <summary>
        /// 마지막 미리 읽기(LastPrepare)가 시스템 메시지를 읽은 뒤 그 기억을 저장했으면 true. 되살렸거나 저장하지 못했으면 false.
        /// StateSaves 는 뒤에서 만들기의 저장까지 세므로, 이 미리 읽기가 저장했는지는 이것으로 본다 (기기 시험의 '기억' 열).
        /// </summary>
        public bool LastPrepareSaved
        {
            get { return lastPrepareSaved; }
        }

        /// <summary>마지막 생성이 시작 전에 저장해 둔 시스템 메시지의 기억을 되살렸으면 true (모델이 다른 도깨비를 기억하던 채로 온 생성).</summary>
        public bool LastCallRestored
        {
            get { return lastCallRestored; }
        }

        /// <summary>저장해 둔 기억을 되살린 횟수.</summary>
        public int StateLoads
        {
            get { return Volatile.Read(ref stateLoads); }
        }

        /// <summary>시스템 메시지를 읽은 뒤 기억을 저장한 횟수.</summary>
        public int StateSaves
        {
            get { return Volatile.Read(ref stateSaves); }
        }

        public async Task LoadAsync(CancellationToken ct)
        {
            await loading.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long started;
                Task previous;
                lock (gate)
                {
                    if (session != null && stalled == session)
                    {
                        // 이미 올라와 있지만 멈췄다. 그냥 돌아오면 IsLoaded 가 false 인 채로 남아 부른 쪽이 모른다.
                        // 묶인 모델을 안은 채 새로 올리면 메모리에 둘이 올라가므로 여기서 내리지도 않는다 — Unload 한 뒤 다시 부르게 한다
                        throw new InvalidOperationException("모델이 멈춰 있다 (취소한 생성이 CancelHold 가 지나도 끝나지 않음). "
                                + "Unload 한 뒤 LoadAsync 하면 그 생성이 끝나기를 기다렸다가 다시 올린다. 끝내 안 끝나면 앱을 다시 켠다");
                    }
                    if (session != null)
                    {
                        return;
                    }
                    started = generation;
                    previous = retired;
                }
                ct.ThrowIfCancellationRequested();

                // 앞서 내린 모델이 아직 지워지지 않았으면(안에서 돌던 생성이 남은 프롬프트를 마저 읽는 중) 지워질 때까지 기다린다.
                // 먼저 올리면 메모리에 모델이 둘 올라간다
                if (!previous.IsCompleted)
                {
                    var stop = new TaskCompletionSource<bool>();
                    using (ct.Register(() => stop.TrySetResult(true)))
                    {
                        Task first = await Task.WhenAny(previous, stop.Task, Task.Delay(options.CancelHold)).ConfigureAwait(false);
                        if (first == stop.Task)
                        {
                            throw new OperationCanceledException(ct);
                        }
                        if (first != previous)
                        {
                            throw new InvalidOperationException("앞서 내린 모델을 아직 지우지 못했다 (그 안에서 돌던 생성이 CancelHold 가 지나도 끝나지 않음). "
                                    + "끝나면 다시 올릴 수 있다. 끝내 안 끝나면 앱을 다시 켠다");
                        }
                    }
                    lock (gate)
                    {
                        if (generation != started)
                        {
                            throw new OperationCanceledException("모델을 올리는 도중에 Unload 되었다");
                        }
                    }
                }
                // 이미 끝났더라도 결과를 본다. 지우기가 실패했으면 앞 모델이 메모리에 남아 있을 수 있어 새로 올리지 않는다
                if (previous.IsFaulted || previous.IsCanceled)
                {
                    Exception cause = previous.Exception != null ? previous.Exception.GetBaseException() : null;
                    throw new InvalidOperationException((cause != null ? cause.Message : "앞서 내린 모델을 지우지 못했다")
                            + ". 메모리에 남아 있을 수 있어 새로 올리지 않는다. 앱을 다시 켠다", cause);
                }

                ILlmUnitySession fresh = await OnUnity(() => runtime.StartAsync(options)).ConfigureAwait(false);
                if (fresh == null)
                {
                    throw new InvalidOperationException("LLMUnity 가 모델을 올리지 못했다");
                }
                try
                {
                    if (options.WarmUp)
                    {
                        await OnUnity(() => fresh.ChatOnceAsync("짧게 답한다.", "안녕", null, 8, options, null)).ConfigureAwait(false);
                    }
                }
                catch
                {
                    RetireIdle(fresh); // 데우기가 끝났으니(실패) 돌던 일은 없다. 다음 LoadAsync 는 이것이 지워지기를 기다린다
                    throw;
                }

                bool stale;
                lock (gate)
                {
                    stale = generation != started || ct.IsCancellationRequested;
                    if (!stale)
                    {
                        session = fresh;
                        if (lifetime.IsCancellationRequested)
                        {
                            lifetime = new CancellationTokenSource();
                        }
                    }
                }
                if (stale)
                {
                    // 올리는 동안 Unload 되었거나 취소됐다. 올린 모델은 바로 내린다 (데우기는 끝났다)
                    RetireIdle(fresh);
                    throw new OperationCanceledException("모델을 올리는 도중에 Unload 또는 취소되었다");
                }
            }
            finally
            {
                loading.Release();
            }
        }

        /// <summary>
        /// 모델을 내린다. 바로 돌아온다 — 새 요청은 막히고(IsLoaded false) 진행 중인 요청은 부른 쪽에 취소로 돌아간다.
        /// 모델을 실제로 지우는 것은 그 안에서 돌던 일이 끝난 뒤다 (클래스 설명의 '올리기·내리기').
        /// </summary>
        public void Unload()
        {
            ILlmUnitySession old;
            var busy = new List<Task>();
            TaskCompletionSource<bool> destroyed = null;
            lock (gate)
            {
                old = session;
                session = null;
                stalled = null;
                generation++;
                if (old != null)
                {
                    // lifetime 취소보다 먼저 넣는다. 그 취소로 Run 이 보내는 '첫 글자 뒤의 취소'(SendCancel)가 이 세션에 닿아야 생성이 곧 끝난다
                    retiring.Add(old);
                    foreach (ModelWork work in working)
                    {
                        if (work.Owner == old)
                        {
                            busy.Add(work.Done);
                        }
                    }
                    // 같은 잠금 안에서 retired 에 더한다. 잠금을 풀었다 다시 잡으면 그 사이 LoadAsync 가 끝난 retired 를 읽고 기다리지 않는다
                    destroyed = AddRetired();
                }
                lifetime.Cancel();
            }
            if (old == null)
            {
                return;
            }
            // 모델에 멈추라고(CancelRequests) 하지 않는다 — 프롬프트를 읽는 중이면 LlamaLib 2.0.5 의 슬롯이 묶인다 (Run 의 취소 주석).
            // 진행 중인 생성은 남은 프롬프트를 마저 읽고, 첫 글자에서 미뤄 둔 취소를 받아 곧 끝난다. 그 뒤에 지운다
            DestroyWhenIdle(old, busy, destroyed);
        }

        // gate 안에서 부른다. 지우기 하나를 retired 에 더하고, 지우기가 끝나면 끝낼 것을 돌려준다
        // 앞의 지우기가 실패했으면 그 실패를 잃지 않게 이어 붙인다 (성공으로 끝난 것만 새것으로 바꾼다)
        private TaskCompletionSource<bool> AddRetired()
        {
            var destroyed = new TaskCompletionSource<bool>();
            retired = retired.Status == TaskStatus.RanToCompletion ? (Task)destroyed.Task : Task.WhenAll(retired, destroyed.Task);
            Observe(retired); // 다시 올리지 않으면 아무도 보지 않는다 — 처리되지 않은 예외로 남지 않게
            return destroyed;
        }

        // 안에서 돌던 일이 없는 세션(LoadAsync 의 데우기가 끝난 뒤)을 지운다. 다음 LoadAsync 는 이것이 지워지기를 기다린다
        private void RetireIdle(ILlmUnitySession fresh)
        {
            TaskCompletionSource<bool> destroyed;
            lock (gate)
            {
                destroyed = AddRetired();
            }
            DestroyWhenIdle(fresh, new List<Task>(), destroyed);
        }

        // 그 세션 안에서 돌던 일이 모두 끝나면 지운다. 끝나지 않으면 지우지 않는다 — 진행 중인 생성이 있을 때 지우면 앱이 죽을 수 있다.
        // destroyed 는 모델이 실제로 지워진 뒤에 끝난다 (DestroyAsync)
        private void DestroyWhenIdle(ILlmUnitySession old, List<Task> busy, TaskCompletionSource<bool> destroyed)
        {
            Action destroy = () =>
            {
                lock (gate)
                {
                    retiring.Remove(old);
                }
                // LLM 의 OnDestroy 가 모델 서비스를 멈추고 지운다 (LlamaLib 의 LLM_Delete). 돌던 일이 없으니 바로 끝난다
                Task<bool> gone = OnUnity(() => Done(old.DestroyAsync()));
                gone.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        destroyed.TrySetResult(true);
                        return;
                    }
                    // 지웠는지 모른다(실패·취소). 성공으로 치면 다음 LoadAsync 가 앞 모델이 남은 채로 하나를 더 올린다 — 실패로 남겨 LoadAsync 가 알린다
                    Exception cause = t.Exception != null ? t.Exception.GetBaseException() : null;
                    destroyed.TrySetException(new InvalidOperationException("앞서 내린 모델을 지우지 못했다"
                            + (cause != null ? " (" + cause.Message + ")" : " (지우기가 취소됨)"), cause));
                }, TaskContinuationOptions.ExecuteSynchronously);
            };
            if (busy.Count == 0)
            {
                destroy();
            }
            else
            {
                Task.WhenAll(busy).ContinueWith(_ => destroy(), TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        // Task → Task<bool>. 실패·취소는 그대로 실패로 넘긴다 (취소를 성공으로 치지 않는다)
        private static Task<bool> Done(Task task)
        {
            if (task == null)
            {
                return Task.FromResult(true);
            }
            return task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    throw t.Exception.GetBaseException();
                }
                if (t.IsCanceled)
                {
                    throw new InvalidOperationException("모델 지우기가 취소되었다");
                }
                return true;
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        // 모델 안에서 도는 일 하나 (끝나면 Done 이 끝난다)
        private sealed class ModelWork
        {
            public ModelWork(ILlmUnitySession owner, Task done)
            {
                Owner = owner;
                Done = done;
            }

            public ILlmUnitySession Owner { get; private set; }

            public Task Done { get; private set; }
        }

        // 모델에 일을 맡긴다(메인 스레드에서). 끝날 때까지 그 세션을 지우지 않는다 — Unload 는 이 일이 끝나기를 기다렸다가 지운다.
        // 그 세션을 이미 내렸거나 요청이 취소됐으면(token) 모델에 보내지 않고 OperationCanceledException. 메인 스레드가 실제로 일을 시작할 때
        // 한 번 더 본다 — 맡긴 뒤 메인 스레드가 꺼내기 전에 Unload·취소되면, 아직 시작도 안 한 생성이 시작돼 모델을 프롬프트 읽기 하나만큼
        // 붙잡는다 (취소는 첫 글자가 나온 뒤에야 보내므로). 이미 시작한 일은 끝날 때까지 그대로 센다
        private Task<T> OnModel<T>(ILlmUnitySession owner, Func<Task<T>> work, CancellationToken token = default(CancellationToken))
        {
            var done = new TaskCompletionSource<bool>();
            var entry = new ModelWork(owner, done.Task);
            lock (gate)
            {
                if (session != owner)
                {
                    throw new OperationCanceledException("모델을 내렸다");
                }
                token.ThrowIfCancellationRequested();
                working.Add(entry);
            }
            Task<T> task = OnUnity(() =>
            {
                lock (gate)
                {
                    if (session != owner)
                    {
                        throw new OperationCanceledException("모델을 내렸다 (모델에 보내기 전)");
                    }
                }
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException("요청이 취소되었다 (모델에 보내기 전)", token);
                }
                return work();
            });
            task.ContinueWith(_ =>
            {
                lock (gate)
                {
                    working.Remove(entry);
                }
                done.TrySetResult(true);
            }, TaskContinuationOptions.ExecuteSynchronously);
            return task;
        }

        public Task<string> CompleteAsync(NpcPrompt prompt, CancellationToken ct)
        {
            if (prompt == null)
            {
                throw new ArgumentNullException("prompt");
            }
            return Run(prompt.System, prompt.User, GrammarFor(prompt), options.MaxTokens, false, prompt.Background, ct);
        }

        /// <summary>
        /// 시스템 메시지를 미리 읽혀 둔다 (형식 제약 없이 1토큰). 마지막으로 끝까지 읽힌 시스템 메시지가 같으면 바로 끝난다.
        /// 생성 요청과 같은 줄에 서서, 미리 읽는 동안 온 같은 도깨비의 요청은 끝나기를 기다렸다가 이어 읽는다.
        /// </summary>
        public Task PrepareAsync(string system, CancellationToken ct)
        {
            if (system == null)
            {
                throw new ArgumentNullException("system");
            }
            return Run(system, NpcPrompt.PrepareUser, null, 1, true, false, ct);
        }

        // 모델 줄을 잡는다. 플레이어의 일(요청·미리 읽기)은 기다리는 동안 foregroundWaiting 에 센다 — 뒤에서 만들기는 그것이 있으면 줄을 잡아도 바로 놓고 비켜 준다.
        // SemaphoreSlim 은 기다린 차례를 지켜 주지 않아, 이것이 없으면 뒤에서 만들기와 플레이어의 일 중 먼저 들어온 쪽이 모델을 잡았다
        private async Task Acquire(bool background, CancellationToken token)
        {
            if (!background)
            {
                Interlocked.Increment(ref foregroundWaiting);
                // 모델을 잡고 있는 뒤에서 만들기에 비키라고 알린다 (취소는 잠금 밖에서 — 취소에 걸린 이어 하기가 이 스레드에서 돌 수 있다)
                CancellationTokenSource running;
                lock (gate)
                {
                    running = backgroundYield;
                }
                if (running != null)
                {
                    try
                    {
                        running.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // 그 사이 끝났다
                    }
                }
                try
                {
                    await oneAtATime.WaitAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref foregroundWaiting);
                }
                return;
            }
            while (true)
            {
                await oneAtATime.WaitAsync(token).ConfigureAwait(false);
                if (Volatile.Read(ref foregroundWaiting) == 0)
                {
                    return;
                }
                oneAtATime.Release(); // 플레이어의 일이 기다린다 — 먼저 들여보낸다
                await Task.Delay(BackgroundYieldMs, token).ConfigureAwait(false);
            }
        }

        private const int BackgroundYieldMs = 20;

        // 뒤에서 만들기가 플레이어의 일에 비켜 줘야 하는지 (취소됐거나 플레이어의 일이 줄을 기다린다)
        private bool ShouldYield(CancellationToken token)
        {
            return token.IsCancellationRequested || Volatile.Read(ref foregroundWaiting) > 0;
        }

        // 뒤에서 만들기: 프롬프트를 BackgroundStepChars 씩 늘려 가며 1토큰 생성으로 읽힌다 (LlmUnityOptions.BackgroundStepChars).
        // 조각 하나는 멈추라고 하지 않고 끝까지 읽힌다(읽는 중의 취소는 슬롯을 묶는다). 조각 사이에 플레이어의 일이 오면 OperationCanceledException 으로 비켜 준다.
        // systemRead 면 시스템 메시지는 이미 모델에 있어(같은 도깨비·되살림) 사용자 메시지만 나눠 읽는다.
        // known 은 모델에 남아 있다고 아는 앞 프롬프트의 시스템 메시지(모르면 null) — 그것과 같은 앞부분(모든 도깨비에 같은 규칙 등)은 조각으로 다시 부르지 않는다.
        // 시스템 메시지 조각 뒤에는 시스템 메시지 전체 + '준비'(미리 읽기와 같은 프롬프트)를 한 번 더 읽혀, 어느 조각도 새로 읽는 것이 한 조각을 넘지 않게 하고
        // 그 기억을 저장해 둔다 (다음에 이 도깨비로 넘어오면 되살린다)
        private async Task ReadInSteps(ILlmUnitySession current, string system, string user, bool systemRead, string known, CancellationToken token)
        {
            int step = options.BackgroundStepChars;
            var parts = new List<KeyValuePair<string, string>>();
            if (!systemRead)
            {
                int shared = known != null ? CommonPrefixLength(known, system) : 0;
                for (int k = (shared / step + 1) * step; k < system.Length; k += step)
                {
                    parts.Add(new KeyValuePair<string, string>(system.Substring(0, k), NpcPrompt.PrepareUser));
                }
                parts.Add(new KeyValuePair<string, string>(system, NpcPrompt.PrepareUser));
            }
            for (int j = step; j < user.Length; j += step)
            {
                parts.Add(new KeyValuePair<string, string>(system, user.Substring(0, j)));
            }
            foreach (KeyValuePair<string, string> part in parts)
            {
                if (ShouldYield(token))
                {
                    throw new OperationCanceledException("플레이어의 요청·미리 읽기에 모델을 비켜 줌");
                }
                KeyValuePair<string, string> piece = part;
                // 조각도 기억 저장도 취소를 듣지 않는 모델 안의 일이다. 취소되면(미리 만들기 시간 초과·플레이어의 일) 끝나기를 기다리지 않고 돌려주되,
                // 줄은 그 일이 끝날 때까지 붙잡는다(PendingModelWork → Run 이 ReleaseWhenDone 에 넘긴다). 모델에 멈추라고 하지는 않는다 — 읽는 중의 취소는 슬롯을 묶는다.
                // 예전에는 끝없이 기다려서, 끝나지 않는 조각이면 뒤에서 만들기가 줄을 쥔 채 돌아오지 않고 멈춤 판정(CancelHold)도 하지 않아,
                // 백엔드는 Ready 인데 뒤의 요청이 모두 시간 초과가 됐다 (CodeRabbit 리뷰). 제때 끝나면 다음 조각 경계에서 이어 간다
                Task chunk = OnModel(current, () => current.ChatOnceAsync(piece.Key, piece.Value, null, 1, options, null), token);
                if (!await FinishedBefore(chunk, token).ConfigureAwait(false))
                {
                    throw new PendingModelWork(chunk);
                }
                await chunk.ConfigureAwait(false);
                knownSystem = piece.Key;
                if (piece.Key == system)
                {
                    readSession = current;
                    readSystem = system; // 시스템 메시지는 끝까지 읽혔다 — 비켜 준 뒤 이 도깨비를 미리 읽으면 다시 읽지 않는다
                    ILlmUnityStateStore store = options.SaveSystemState ? current as ILlmUnityStateStore : null;
                    if (piece.Value == NpcPrompt.PrepareUser && store != null)
                    {
                        // 미리 읽기와 같은 상태(시스템 메시지 + '준비')라 그 기억을 저장한다 — 저장도 짧고 멈출 수 없는 일이라 조각 하나로 친다
                        Task<bool> persisting = PersistState(current, store, StateKey(system), token);
                        if (!await FinishedBefore(persisting, token).ConfigureAwait(false))
                        {
                            throw new PendingModelWork(persisting);
                        }
                    }
                }
            }
            if (ShouldYield(token))
            {
                throw new OperationCanceledException("플레이어의 요청·미리 읽기에 모델을 비켜 줌");
            }
        }

        private static int CommonPrefixLength(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && a[i] == b[i])
            {
                i++;
            }
            return i;
        }

        private async Task<string> Run(string system, string user, string grammar, int maxTokens, bool prepare, bool background, CancellationToken ct)
        {
            ILlmUnitySession current;
            CancellationToken alive;
            lock (gate)
            {
                if (session == null)
                {
                    throw new InvalidOperationException("모델이 올라오지 않았다. LoadAsync 를 먼저 부른다.");
                }
                current = session;
                alive = lifetime.Token;
                ThrowIfStalled(current);
            }

            // 뒤에서 만들기는 플레이어의 일이 줄에 서면 취소된다(yielding) — 조각 사이면 그 자리에서, 마지막 읽기·생성 중이면 첫 글자에서(미뤄 둔 취소) 비켜 준다
            using (var yielding = new CancellationTokenSource())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, alive, yielding.Token))
            {
                CancellationToken token = linked.Token;
                await Acquire(background, token).ConfigureAwait(false);
                if (background)
                {
                    lock (gate)
                    {
                        backgroundYield = yielding;
                    }
                    if (Volatile.Read(ref foregroundWaiting) > 0)
                    {
                        yielding.Cancel(); // 줄을 잡는 사이에 플레이어의 일이 왔다 (그쪽이 본 backgroundYield 는 아직 비어 있었다)
                    }
                }
                bool releaseLater = false; // 멈추지 않은 생성이 끝날 때 ReleaseWhenDone 이 놓는다
                try
                {
                    token.ThrowIfCancellationRequested();
                    lock (gate)
                    {
                        ThrowIfStalled(current); // 줄에서 기다리는 동안 앞의 생성이 멈춘 것으로 판정됐다
                    }
                    bool alreadyRead = readSession == current && readSystem == system;
                    if (prepare && alreadyRead)
                    {
                        return ""; // 이미 읽어 둔 시스템 메시지
                    }
                    // 끝까지 읽히기 전에는 모델이 무엇을 기억하는지 모른다 (끝나면 아래에서 다시 적는다. 취소·실패면 모르는 채로 둔다)
                    readSystem = null;
                    string known = readSession == current ? knownSystem : null;
                    knownSystem = null;
                    Stopwatch watch = Stopwatch.StartNew();
                    // 저장해 둔 그 시스템 메시지의 기억이 있으면 읽는 대신 되살린다. 미리 읽기뿐 아니라, 모델이 다른 도깨비를 기억하는 채로 온 생성
                    // (미리 읽기 없이 온 요청, 다른 도깨비로 넘어간 미리 만들기)도 되살린 뒤 사용자 메시지만 읽는다 (2026-11-25). 이미 읽어 둔 것이면 되살리지 않는다
                    ILlmUnityStateStore store = options.SaveSystemState && !alreadyRead ? current as ILlmUnityStateStore : null;
                    string stateKey = store != null ? StateKey(system) : null;
                    Task<bool> restoring = store != null ? RestoreState(current, store, stateKey, token) : Task.FromResult(false);
                    // 기억 복원도 모델 안의 일이라 취소를 듣지 않는다. 취소되면 기다리지 않고 돌려주되, 끝날 때까지 줄은 붙잡는다 (생성 취소와 같다)
                    if (!await FinishedBefore(restoring, token).ConfigureAwait(false))
                    {
                        releaseLater = true;
                        ReleaseWhenDone(restoring, alive, current);
                        throw new OperationCanceledException(token);
                    }
                    bool restored = restoring.Result;
                    if (restored)
                    {
                        token.ThrowIfCancellationRequested();
                        readSession = current;
                        readSystem = system;
                        knownSystem = system;
                        if (prepare)
                        {
                            watch.Stop();
                            lastPrepareRestored = true;
                            lastPrepareSaved = false;
                            lastPrepare = new LlmUnityCall(-1, watch.ElapsedMilliseconds, 0);
                            return "";
                        }
                        // 생성: 되살린 시스템 메시지 뒤로 사용자 메시지만 읽는다. 아래에서 끝까지 읽히기 전까지는 다시 모른다고 둔다
                        readSystem = null;
                    }
                    if (!prepare && !background)
                    {
                        lastCallRestored = restored;
                    }
                    if (background && options.BackgroundStepChars > 0)
                    {
                        try
                        {
                            await ReadInSteps(current, system, user, alreadyRead || restored, restored ? system : known, token).ConfigureAwait(false);
                        }
                        catch (PendingModelWork pending)
                        {
                            // 취소됐는데 조각·기억 저장이 아직 모델 안에 있다 — 부른 쪽에는 바로 돌려주고, 줄은 그 일이 끝날 때 놓는다
                            // (CancelHold 가 지나도 안 끝나면 멈춘 것으로 표시한다. 기억 복원·생성 취소와 같다)
                            releaseLater = true;
                            ReleaseWhenDone(pending.Work, alive, current);
                            throw new OperationCanceledException(token);
                        }
                        knownSystem = null; // 마지막 생성이 끝나기 전에는 모델이 무엇을 기억하는지 모른다
                    }
                    // 플레이어의 생성은 되살리기부터 잰다 — 부른 쪽이 기다린 시간이 첫 응답(FirstTokenMs)·전체(ElapsedMs)에 다 들어가게.
                    // 미리 읽기는 시스템 메시지를 읽는 시간만 잰다(LastPrepare), 뒤에서 만들기는 적지 않는다
                    if (prepare || background)
                    {
                        watch = Stopwatch.StartNew();
                    }
                    long firstToken = -1;
                    var stop = new StopAfterFirstToken();
                    Task<string> chat = null;
                    Action onFirstToken = () =>
                    {
                        Interlocked.CompareExchange(ref firstToken, watch.ElapsedMilliseconds, -1);
                        if (stop.FirstToken())
                        {
                            SendCancel(current, chat); // 프롬프트를 읽는 동안 미뤄 둔 취소
                        }
                    };
                    // 메인 스레드가 꺼내기 전에 취소되면 생성을 시작하지 않는다 (token)
                    chat = OnModel(current, () => current.ChatOnceAsync(system, user, grammar, maxTokens, options, onFirstToken), token);

                    var cancelled = new TaskCompletionSource<bool>();
                    using (token.Register(() => cancelled.TrySetResult(true)))
                    {
                        Task first = await Task.WhenAny(chat, cancelled.Task).ConfigureAwait(false);
                        if (first != chat)
                        {
                            // 모델에 멈추라고 하는 것은 첫 글자가 나온 뒤(슬롯이 생성 단계에 들어간 뒤)에만 한다. LlamaLib 2.0.5 의 취소(release_slot)는
                            // 슬롯 값을 잠금 없이 바꿔서, 프롬프트의 마지막 묶음을 읽는 동안 들어가면 슬롯이 생성 단계로 넘어가지 못하고 영구히 묶인다 —
                            // 그 호출도, 그 뒤의 모든 호출도 돌아오지 않고 다시 취소해도 풀리지 않는다 (2026-11-20 PC 재현). 그래서 읽는 중이면 미뤘다가
                            // 첫 글자가 나오면 보낸다. 버리는 계산은 남은 프롬프트 읽기와 토큰 하나뿐이다. 미리 읽기(1토큰)는 스스로 곧 끝나므로 보내지 않는다.
                            if (!prepare && stop.Cancel())
                            {
                                SendCancel(current, chat);
                            }
                            // 잠깐만 기다린다. 그래도 안 끝나면 부른 쪽에는 바로 돌려주되,
                            // 모델은 그 생성이 끝날 때까지 붙잡아 둔다 (다음 요청을 보내면 llama.cpp 줄에 쌓여 취소되지 않는다)
                            await Task.WhenAny(chat, Task.Delay(options.CancelGrace)).ConfigureAwait(false);
                            Observe(chat);
                            if (!chat.IsCompleted)
                            {
                                releaseLater = true;
                                ReleaseWhenDone(chat, alive, current);
                            }
                            throw new OperationCanceledException(token);
                        }
                    }

                    string reply = await chat.ConfigureAwait(false);
                    watch.Stop();
                    readSession = current;
                    readSystem = system;
                    knownSystem = system;
                    var measured = new LlmUnityCall(Interlocked.Read(ref firstToken), watch.ElapsedMilliseconds, reply != null ? reply.Length : 0);
                    if (prepare)
                    {
                        lastPrepareSaved = false;
                        lastPrepareRestored = false;
                        lastPrepare = measured;
                        if (store != null)
                        {
                            // 기억 저장도 취소를 듣지 않는다. 취소되면 기다리지 않고 돌려주되, 끝날 때까지 줄은 붙잡는다
                            Task<bool> persisting = PersistState(current, store, stateKey, token);
                            if (!await FinishedBefore(persisting, token).ConfigureAwait(false))
                            {
                                releaseLater = true;
                                ReleaseWhenDone(persisting, alive, current);
                                throw new OperationCanceledException(token);
                            }
                            lastPrepareSaved = persisting.Result;
                        }
                    }
                    else if (!background)
                    {
                        lastCall = measured; // 뒤에서 만들기는 적지 않는다 — 플레이어 요청의 첫 응답 시간(기기 시험)을 덮지 않게
                    }
                    return reply ?? "";
                }
                finally
                {
                    if (background)
                    {
                        lock (gate)
                        {
                            if (backgroundYield == yielding)
                            {
                                backgroundYield = null; // 이 using 이 끝나면 yielding 을 버린다 — 플레이어의 일이 버린 것을 취소하지 않게
                            }
                        }
                    }
                    if (!releaseLater)
                    {
                        oneAtATime.Release();
                    }
                }
            }
        }

        // 취소했는데 멈추지 않은 생성: 끝나거나, 모델을 내리거나(Unload), CancelHold 가 지나면 줄을 놓는다.
        // CancelHold 가 지나도 안 끝났으면 그 세션을 멈춘 것으로 표시한다. 줄을 놓아도 기다리던 호출은 모델에 닿지 않고 바로 실패한다
        // (예전에는 그대로 보내서, 묶인 슬롯 뒤에 선 호출이 또 안 끝나고 5분마다 하나씩 쌓였다)
        private void ReleaseWhenDone(Task chat, CancellationToken alive, ILlmUnitySession owner)
        {
            var unloaded = new TaskCompletionSource<bool>();
            CancellationTokenRegistration registration = alive.Register(() => unloaded.TrySetResult(true));
            Task.WhenAny(chat, unloaded.Task, Task.Delay(options.CancelHold)).ContinueWith(_ =>
            {
                registration.Dispose();
                if (!chat.IsCompleted && !alive.IsCancellationRequested)
                {
                    MarkStalled(chat, owner);
                }
                oneAtATime.Release();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void MarkStalled(Task chat, ILlmUnitySession owner)
        {
            lock (gate)
            {
                if (session != owner)
                {
                    return; // 이미 내렸다
                }
                stalled = owner;
            }
            // 뒤늦게라도 끝나면 모델은 다시 쓸 수 있다
            chat.ContinueWith(_ =>
            {
                lock (gate)
                {
                    if (stalled == owner)
                    {
                        stalled = null;
                    }
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        // gate 안에서 부른다
        private void ThrowIfStalled(ILlmUnitySession current)
        {
            if (stalled == current)
            {
                throw new InvalidOperationException("모델이 멈췄다 (취소한 생성이 CancelHold 가 지나도 끝나지 않음). 앱을 다시 켜야 할 수 있다.");
            }
        }

        // 모델에 멈추라고 한다. 그 사이 생성이 이미 끝났으면 보내지 않는다 (끝나는 중인 슬롯을 건드리지 않게, 다음 호출을 멈추지 않게).
        // Unload 했지만 아직 지우지 않은 세션에는 보낸다 — 그래야 생성이 곧 끝나 지울 수 있다. 지운(또는 지우기로 한) 세션에는 보내지 않는다.
        // 지우기는 생성이 끝난 뒤에만 하므로, 생성이 끝나지 않았으면 그 세션은 아직 살아 있다
        private void SendCancel(ILlmUnitySession current, Task chat)
        {
            Post(() =>
            {
                lock (gate)
                {
                    if (session != current && !retiring.Contains(current))
                    {
                        return;
                    }
                }
                if (chat == null || !chat.IsCompleted)
                {
                    current.CancelRequests();
                }
            });
        }

        /// <summary>
        /// 취소를 언제 모델에 보낼지 정한다. 첫 글자가 나오기 전(프롬프트를 읽는 중)의 취소는 미루고, 첫 글자가 나오면 그때 보낸다.
        /// 첫 글자 알림과 취소는 서로 다른 스레드에서 동시에 올 수 있어 상태를 Interlocked 로 바꾼다. 어느 쪽이 먼저든 취소는 한 번만 나간다.
        /// </summary>
        private sealed class StopAfterFirstToken
        {
            private const int Reading = 0;    // 프롬프트를 읽는 중, 취소 없음
            private const int Generating = 1; // 첫 글자가 나왔다, 취소 없음
            private const int Deferred = 2;   // 읽는 중에 취소가 들어와 미뤄 둠
            private const int Sent = 3;       // 취소를 보냈다
            private int state;

            /// <summary>첫 글자가 나왔다. 미뤄 둔 취소가 있으면 true — 지금 보낸다.</summary>
            public bool FirstToken()
            {
                int was = Interlocked.CompareExchange(ref state, Generating, Reading);
                return was == Deferred && Interlocked.CompareExchange(ref state, Sent, Deferred) == Deferred;
            }

            /// <summary>취소가 들어왔다. 이미 생성 단계면 true — 지금 보낸다. 읽는 중이면 미루고 false.</summary>
            public bool Cancel()
            {
                int was = Interlocked.CompareExchange(ref state, Deferred, Reading);
                return was == Generating && Interlocked.CompareExchange(ref state, Sent, Generating) == Generating;
            }
        }

        // op 가 취소보다 먼저 끝났으면 true. 취소되면 op 를 기다리지 않고 false (op 는 계속 돈다 — 부른 쪽이 줄을 붙잡는다)
        private static async Task<bool> FinishedBefore(Task op, CancellationToken token)
        {
            if (op.IsCompleted)
            {
                return true;
            }
            var stop = new TaskCompletionSource<bool>();
            using (token.Register(() => stop.TrySetResult(true)))
            {
                return await Task.WhenAny(op, stop.Task).ConfigureAwait(false) == op;
            }
        }

        // 저장해 둔 기억 되살리기. 실패(없음·깨짐·예외)는 읽기로 대신하면 되므로 false
        // 메인 스레드가 꺼내기 전에 취소되면(token) 되살리지 않는다 (false — 뒤의 생성도 같은 token 으로 시작하지 않는다)
        private async Task<bool> RestoreState(ILlmUnitySession owner, ILlmUnityStateStore store, string key, CancellationToken token)
        {
            try
            {
                bool ok = await OnModel(owner, () => store.LoadStateAsync(key), token).ConfigureAwait(false);
                if (ok)
                {
                    Interlocked.Increment(ref stateLoads);
                }
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 방금 읽은 시스템 메시지의 기억을 저장한다. 저장했으면 true. 실패해도 생성에는 영향이 없다
        // 메인 스레드가 꺼내기 전에 취소되면(token) 저장하지 않는다 (다음에 다시 읽을 뿐이다)
        private async Task<bool> PersistState(ILlmUnitySession owner, ILlmUnityStateStore store, string key, CancellationToken token)
        {
            try
            {
                if (await OnModel(owner, () => store.SaveStateAsync(key), token).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref stateSaves);
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// 저장한 기억의 이름. 시스템 메시지·모델 파일·컨텍스트 크기·StateVersion 이 모두 같을 때만 같다.
        /// string.GetHashCode 는 실행마다 달라질 수 있어 쓰지 않고, 64비트 FNV-1a 로 직접 만든다.
        /// </summary>
        public string StateKey(string system)
        {
            string text = options.Model + "|" + options.ContextSize + "|" + options.StateVersion + "|" + system;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            return "npc-kv-" + hash.ToString("x16");
        }

        private string GrammarFor(NpcPrompt prompt)
        {
            switch (options.Grammar)
            {
                case LlmUnityGrammar.Gbnf:
                    return prompt.Grammar;
                case LlmUnityGrammar.JsonSchema:
                    return prompt.JsonSchema;
                default:
                    return null;
            }
        }

        // ---------- Unity 메인 스레드로 넘기기 ----------

        private bool OnUnityThread
        {
            get { return unity == null || SynchronizationContext.Current == unity; }
        }

        private Task<T> OnUnity<T>(Func<Task<T>> work)
        {
            if (OnUnityThread)
            {
                return Start(work);
            }
            var done = new TaskCompletionSource<T>();
            unity.Post(_ => Start(work).ContinueWith(t => Relay(t, done), TaskContinuationOptions.ExecuteSynchronously), null);
            return done.Task;
        }

        private void Post(Action work)
        {
            if (OnUnityThread)
            {
                Swallow(work);
                return;
            }
            unity.Post(_ => Swallow(work), null);
        }

        private static Task<T> Start<T>(Func<Task<T>> work)
        {
            try
            {
                return work() ?? Task.FromResult(default(T));
            }
            catch (Exception e)
            {
                var failed = new TaskCompletionSource<T>();
                failed.SetException(e);
                return failed.Task;
            }
        }

        private static void Relay<T>(Task<T> source, TaskCompletionSource<T> target)
        {
            if (source.IsFaulted)
            {
                target.TrySetException(source.Exception.InnerExceptions);
            }
            else if (source.IsCanceled)
            {
                target.TrySetCanceled();
            }
            else
            {
                target.TrySetResult(source.Result);
            }
        }

        // 정리 작업(멈추기·내리기)의 예외는 삼킨다. 이미 내려간 모델에 멈추라고 하는 경우 등
        private static void Swallow(Action work)
        {
            try
            {
                work();
            }
            catch (Exception)
            {
            }
        }

        private static void Observe(Task task)
        {
            task.ContinueWith(t => t.Exception.Handle(e => true),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        // ReadInSteps 가 취소됐는데 맡긴 일(조각·기억 저장)이 아직 모델 안에 있다 — Run 이 받아 줄을 그 일이 끝날 때 놓게 한다(ReleaseWhenDone)
        private sealed class PendingModelWork : OperationCanceledException
        {
            public PendingModelWork(Task work) : base("취소됐지만 모델 안의 일이 아직 끝나지 않았다")
            {
                Work = work;
                Observe(work);
            }

            public Task Work { get; private set; }
        }
    }
}
