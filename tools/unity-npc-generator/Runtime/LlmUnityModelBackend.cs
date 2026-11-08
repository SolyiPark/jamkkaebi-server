using System;
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
            MaxTokens = 160;
            Grammar = LlmUnityGrammar.Gbnf;
            DisableThinking = true;
            WarmUp = true;
            CancelGrace = TimeSpan.FromSeconds(2);
        }

        /// <summary>GGUF 파일 이름. 에디터의 LLM 모델 관리자에 등록한 이름 그대로 (예: "gemma-4-E2B-it-Q4_K_M.gguf").</summary>
        public string Model { get; set; }

        /// <summary>
        /// GPU 로 올릴 층 수 (0 = CPU 만). iOS 는 Metal 이라 크게(예: 99) 두고, 안드로이드는 0 으로 시작한다
        /// (LlamaLib 2.0.5 에서 안드로이드 Vulkan 이 빠졌다). 실기기에서 재 보고 정한다.
        /// </summary>
        public int NumGpuLayers { get; set; }

        /// <summary>CPU 스레드 수 (-1 = 모두). 폰은 고성능 코어 수(보통 4 안팎)가 더 빠를 수 있다.</summary>
        public int NumThreads { get; set; }

        /// <summary>컨텍스트 크기(토큰). 프롬프트가 700~1000 토큰이고 대사가 160 토큰 이내라 2048 이면 된다. 작을수록 메모리를 덜 쓴다.</summary>
        public int ContextSize { get; set; }

        public float Temperature { get; set; }

        /// <summary>한 번에 생성할 최대 토큰.</summary>
        public int MaxTokens { get; set; }

        /// <summary>정하면 결과가 재현된다. 없으면 무작위.</summary>
        public int? Seed { get; set; }

        public LlmUnityGrammar Grammar { get; set; }

        /// <summary>생각(추론) 모드를 끈다. 켜 두면 Gemma 4 가 출력 한도를 생각에 다 쓰고 대사를 비운다.</summary>
        public bool DisableThinking { get; set; }

        /// <summary>LoadAsync 에서 짧게 한 번 생성해 둔다. 첫 요청이 준비 작업(GPU 셰이더 등) 때문에 시간 초과되지 않게 한다.</summary>
        public bool WarmUp { get; set; }

        /// <summary>취소한 뒤 모델이 멈추기를 기다리는 최대 시간. 지나면 기다리지 않고 다음 요청을 받는다.</summary>
        public TimeSpan CancelGrace { get; set; }

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

        /// <summary>모델을 내리고 메모리를 돌려준다. 다시 쓰려면 새로 StartAsync 한다.</summary>
        void Destroy();
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
    /// <item>취소: 취소되면 모델에 멈추라고 하고, CancelGrace 안에 멈추지 않아도 기다리지 않는다.</item>
    /// <item>올리기·내리기: LoadAsync 가 모델을 올리고(실패하면 예외, IsLoaded 는 false) 미리 한 번 생성해 둔다.
    /// Unload 는 진행 중인 요청을 취소하고 모델을 내려 메모리를 돌려준다. 올리는 도중에 Unload 하면 올린 모델을 바로 내린다.</item>
    /// </list>
    /// </summary>
    public sealed class LlmUnityModelBackend : INpcModelBackend
    {
        private readonly ILlmUnityRuntime runtime;
        private readonly LlmUnityOptions options;
        private readonly SynchronizationContext unity;
        private readonly SemaphoreSlim oneAtATime = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim loading = new SemaphoreSlim(1, 1);
        private readonly object gate = new object();
        private ILlmUnitySession session;
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private long generation;
        private volatile LlmUnityCall lastCall;

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

        public bool IsLoaded
        {
            get
            {
                lock (gate)
                {
                    return session != null;
                }
            }
        }

        /// <summary>마지막 생성 호출의 측정값. 아직 없으면 null.</summary>
        public LlmUnityCall LastCall
        {
            get { return lastCall; }
        }

        public async Task LoadAsync(CancellationToken ct)
        {
            await loading.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long started;
                lock (gate)
                {
                    if (session != null)
                    {
                        return;
                    }
                    started = generation;
                }
                ct.ThrowIfCancellationRequested();

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
                    Post(fresh.Destroy);
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
                    // 올리는 동안 Unload 되었거나 취소됐다. 올린 모델은 바로 내린다
                    Post(fresh.Destroy);
                    throw new OperationCanceledException("모델을 올리는 도중에 Unload 또는 취소되었다");
                }
            }
            finally
            {
                loading.Release();
            }
        }

        public void Unload()
        {
            ILlmUnitySession old;
            lock (gate)
            {
                old = session;
                session = null;
                generation++;
                lifetime.Cancel();
            }
            if (old != null)
            {
                Post(() =>
                {
                    old.CancelRequests();
                    old.Destroy();
                });
            }
        }

        public async Task<string> CompleteAsync(NpcPrompt prompt, CancellationToken ct)
        {
            if (prompt == null)
            {
                throw new ArgumentNullException("prompt");
            }
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
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, alive))
            {
                CancellationToken token = linked.Token;
                await oneAtATime.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    string grammar = GrammarFor(prompt);
                    Stopwatch watch = Stopwatch.StartNew();
                    long firstToken = -1;
                    Action onFirstToken = () => Interlocked.CompareExchange(ref firstToken, watch.ElapsedMilliseconds, -1);
                    Task<string> chat = OnUnity(() => current.ChatOnceAsync(prompt.System, prompt.User, grammar, options.MaxTokens,
                            options, onFirstToken));

                    var cancelled = new TaskCompletionSource<bool>();
                    using (token.Register(() => cancelled.TrySetResult(true)))
                    {
                        Task first = await Task.WhenAny(chat, cancelled.Task).ConfigureAwait(false);
                        if (first != chat)
                        {
                            // 모델에 멈추라고 하고, 잠깐만 기다린다. 그래도 안 멈추면 버리고 다음 요청을 받는다
                            Post(current.CancelRequests);
                            await Task.WhenAny(chat, Task.Delay(options.CancelGrace)).ConfigureAwait(false);
                            Observe(chat);
                            throw new OperationCanceledException(token);
                        }
                    }

                    string reply = await chat.ConfigureAwait(false);
                    watch.Stop();
                    lastCall = new LlmUnityCall(Interlocked.Read(ref firstToken), watch.ElapsedMilliseconds, reply != null ? reply.Length : 0);
                    return reply ?? "";
                }
                finally
                {
                    oneAtATime.Release();
                }
            }
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
    }
}
