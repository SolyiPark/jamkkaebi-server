#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 실기기 시험용. 빈 장면에 빈 게임 오브젝트를 만들어 이 컴포넌트를 붙이고 빌드하면, 폰에서 프롬프트 시험의 장면을
    /// 요청 관리자로 하나씩 돌려 대사·출처(SLM/기본 대사)·첫 응답 시간·전체 시간·메모리를 화면에 보여 주고 CSV 로 저장한다.
    ///
    /// <para>로그는 "[NPC시험]" 으로 시작한다. 안드로이드는 adb logcat, iOS 는 Xcode 콘솔에서 본다.
    /// CSV 는 Application.persistentDataPath 에 생기고, 장면 하나가 끝날 때마다 한 줄씩 이어 쓴다 (중간에 실패하거나 앱이 꺼져도 그때까지의 줄은 남는다). 경로는 화면과 로그에 나온다.</para>
    ///
    /// <para>prepareSystemPrompt 를 켜면 장면마다 요청 전에 그 도깨비의 시스템 프롬프트를 미리 읽힌다(게임에서 탭해 확대할 때처럼).
    /// 미리 읽기 시간은 따로 재고, 요청 시간에는 사용자 메시지를 읽고 대사를 쓰는 시간만 남는다.
    /// 이야기 듣기 장면은 정해진 대사를 쓰므로 SLM 대사 수에서 뺀다.</para>
    /// </summary>
    public sealed class NpcDeviceTestRunner : MonoBehaviour
    {
        public enum BackendKind
        {
            LlmUnity,   // 기기 안 모델 (LLMUnity). JAMKKAEBI_LLMUNITY 심볼이 있어야 한다
            LmStudio,   // 같은 Wi-Fi 의 PC 에서 도는 LM Studio (비교용)
            RuleOnly    // 모델 없이 기본 대사만 (흐름 확인용)
        }

        [Header("백엔드")]
        public BackendKind backend = BackendKind.LlmUnity;

        [Tooltip("LLMUnity: 모델 관리자에 등록한 GGUF 파일 이름")]
        public string modelFile = "";

        [Tooltip("GPU 로 올릴 층 수. -1 이면 iOS 99, 그 밖 0")]
        public int gpuLayers = -1;

        [Tooltip("CPU 스레드 수. -1 이면 모두")]
        public int threads = -1;

        public LlmUnityGrammar grammar = LlmUnityGrammar.Gbnf;

        public string lmStudioUrl = "http://192.168.0.10:1234";
        public string lmStudioModel = "kanana-1.5-2.1b-instruct-2505";

        [Tooltip("LM Studio: JSON 스키마 형식 제약을 쓴다. Kanana 처럼 형식 제약을 켜면 LM Studio 가 오류를 내는 모델은 끈다 — 끄면 프롬프트에 형식 설명을 싣는다 (run.py --no-format 과 같다)")]
        public bool lmStudioJsonSchema = false;

        [Header("시험")]
        [Tooltip("SLM 을 기다리는 시간(초). 잴 때는 넉넉히 두고, 결과를 보고 게임 값(SlmTimeout)을 정한다")]
        public float slmTimeoutSeconds = 30f;

        [Tooltip("장면마다 반복 횟수")]
        public int repeat = 1;

        [Tooltip("요청 전에 그 도깨비의 시스템 프롬프트를 미리 읽힌다 (게임에서 탭해 확대할 때처럼). 끄면 요청 시간에 시스템 프롬프트 읽기가 섞인다")]
        public bool prepareSystemPrompt = true;

        [Tooltip("시스템 프롬프트 미리 읽기를 기다리는 최대 시간(초). 넘으면 멈추고 그대로 요청한다 (모델이 묶여도 시험이 그 장면에서 멈추지 않게). 느린 기기는 길게")]
        public float prepareTimeoutSeconds = 180f;

        [Tooltip("시스템 프롬프트를 읽은 모델의 기억(KV 슬롯)을 파일(persistentDataPath/npc_kv/)로 남겼다가, 다음에 그 도깨비를 미리 읽을 때 읽는 대신 되살린다 (LlmUnityOptions.SaveSystemState). "
            + "모델의 기억 칸은 하나라 A→B→A 로만 바뀌어도 A 를 다시 읽는데, 이것으로 메모리를 더 쓰지 않고 다시 읽기를 줄인다. 끄면 예전처럼 읽는다 (비교용). CSV 의 '기억' 열에 결과가 적힌다")]
        public bool saveSystemState = true;

        [Header("미리 만들기 (칸 있는 대사)")]
        [Tooltip("요청 전에 장면마다 대사를 미리 만들어 둔다. 서버 값(시간대·호칭·방문 횟수)에 달린 말은 칸으로 남겨 두고, 요청 때 실제 값을 넣어 모델 없이 바로 완성한다. 끝난 뒤 요청해 적중률과 시간을 잰다")]
        public bool prefetchFirst = true;

        [Tooltip("상황마다 미리 만들 대사 수")]
        public int prefetchPerScene = 2;

        [Tooltip("미리 만들기를 기다리는 최대 시간(초). 느린 기기는 길게")]
        public float prefetchWaitSeconds = 900f;

        [Tooltip("미리 만들 대사 하나를 기다리는 시간(초) = PrefetchTimeout. 기기가 대사 하나를 만드는 시간보다 길게 둔다. 짧으면 모든 시도가 잘려 하나도 보관하지 못한다 (S22 는 하나에 100초 안팎)")]
        public float prefetchTimeoutSeconds = 120f;

        [Tooltip("요청할 때 서버 값을 미리 만들 때와 다르게 준다(시간대를 바꾸고 닉네임 '하늘'을 부른다). 칸 채우기가 실제로 되는지 본다")]
        public bool requestWithOtherServerValues = true;

        [Tooltip("미리 만든 대사를 파일(persistentDataPath/npc_lines.txt)에 남기고, 시작할 때 읽어 들인다 (게임의 로딩 화면처럼). 두 번째 판부터는 파일에 있던 것은 다시 만들지 않는다")]
        public bool useLineStore = true;

        [Tooltip("대사를 띄운 뒤 그 상황의 칸 있는 대사를 쓴 만큼 뒤에서 다시 만든다 (NpcRequestManager.RefillAfterUse)")]
        public bool refillAfterUse = true;

        [Tooltip("요청·미리 읽기가 끝난 뒤 이만큼(초) 아무 일이 없어야 뒤에서 미리 만들기·다시 만들기를 새로 시작한다 (NpcRequestManager.PrefetchIdleDelay, 게임 기본 3초). "
            + "부딪혀도 플레이어가 먼저다(뒤에서 만들기는 한 조각 안에 비켜 준다) — 이 값은 부딪힘을 줄일 뿐이다. 예전 기본 10초와 비교하려면 10 으로 두고 Adaptive Idle 도 끈다 — 켜 두면 빨리 만드는 기기에서 더 짧게 줄어든다 (2026-11-25)")]
        public float prefetchIdleSeconds = 3f;

        [Tooltip("다른 도깨비의 미리 만들기·다시 만들기를 새로 시작하기 전에 쉬는 시간(초) (NpcRequestManager.PrefetchSwitchIdleDelay, 게임 기본 30초). "
            + "뒤에서 만들기는 지금 모델이 읽어 둔 도깨비의 작업을 먼저 고르고, 다른 도깨비의 작업은 이만큼 쉰 뒤에만 시작한다 — 모델이 기억하던 시스템 프롬프트를 지우지 않게 (2026-11-24 시험)")]
        public float prefetchSwitchIdleSeconds = 30f;

        [Tooltip("뒤에서 만들기 전 쉬는 시간을 기기 속도에 맞춰 줄인다 (NpcRequestManager.AdaptiveIdleDelay): 미리 만들기 한 번의 시간 × 2 가 Prefetch Idle Seconds 보다 짧으면 그만큼만 쉰다. "
            + "한 번에 10초가 넘는 폰은 예전과 같다. 끄면 늘 Prefetch Idle Seconds 를 다 쉰다 (비교용)")]
        public bool adaptiveIdle = true;

        [Tooltip("장면 사이에 쉬는 시간(초). 플레이어가 잠깐 쉬는 것처럼 둔다. 0 이면 쉬지 않고 이어 가서 다시 만들기가 돌 틈이 없다 — "
            + "다시 만들기의 적중을 재려면 Prefetch Idle Seconds + 대사 하나 만드는 시간(기기에서 10~20초)보다 길게 둔다. 쉬는 시간은 CSV 의 시간에 넣지 않는다")]
        public float sceneGapSeconds = 0f;

        public bool autoStart = true;

        [Header("데이터 (비우면 Resources/Npc/ 에서 읽는다)")]
        public TextAsset scenes;
        public TextAsset personas;
        public TextAsset actions;
        public TextAsset defaultLines;
        public TextAsset forbidden;

        private readonly List<string> lines = new List<string>();
        private readonly CancellationTokenSource stopping = new CancellationTokenSource();
        private Vector2 scroll;
        private string status = "대기";
        private bool running;
        private INpcModelBackend model;
        private NpcRequestManager manager;
        private string csvPath; // 이번 판의 CSV. 중간에 멈추거나 실패해도 여기까지의 줄은 남아 있다

        private void Start()
        {
            if (autoStart)
            {
                Run();
            }
        }

        private void OnDestroy()
        {
            stopping.Cancel();
            ReleaseBackend();
        }

        // 앞 판의 요청을 멈추고 모델을 내린다. 내린 것이 있으면 true
        private bool ReleaseBackend()
        {
            bool released = manager != null || model != null;
            if (manager != null)
            {
                // 앞 판의 다시 만들기·뒤에서 하는 저장을 멈추고 저장소를 뗀다 — 늦게 쓴 저장이 다음 판의 npc_lines.txt 를 덮어쓰지 않게 (CodeRabbit 리뷰)
                manager.CloseStore(true, TimeSpan.FromSeconds(3));
                manager.CancelAll();
                manager = null;
            }
            if (model != null)
            {
                model.Unload();
                model = null;
            }
            return released;
        }

        public async void Run()
        {
            if (running)
            {
                return;
            }
            running = true;
            lines.Clear();
            csvPath = null;
            // 시험 중에는 화면이 꺼지지 않게 한다. 느린 기기는 한 판이 길어서, 잠기면 앱이 멈춰 시간이 틀리게 잡히고 무선 adb 도 끊긴다.
            // 전원 버튼으로 직접 잠그는 것은 막지 못한다. 끝나면 기기 설정으로 되돌린다
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            try
            {
                await RunAll(stopping.Token);
            }
            catch (OperationCanceledException)
            {
                Say("중단됨" + SavedSoFar());
            }
            catch (Exception e)
            {
                status = "오류";
                Say("오류: " + e + SavedSoFar());
            }
            finally
            {
                running = false;
                Screen.sleepTimeout = SleepTimeout.SystemSetting;
            }
        }

        private async Task RunAll(CancellationToken ct)
        {
            Say("기기: " + SystemInfo.deviceModel + " / " + SystemInfo.operatingSystem);
            Say("메모리 " + SystemInfo.systemMemorySize + "MB, CPU " + SystemInfo.processorType + " × " + SystemInfo.processorCount
                + ", GPU " + SystemInfo.graphicsDeviceName);

            var personaCatalog = NpcContentLoader.FromJson<PersonaCatalog>(Text(personas, "Npc/personas"));
            var actionCatalog = NpcContentLoader.FromJson<ActionCatalog>(Text(actions, "Npc/actions"));
            var lineCatalog = NpcContentLoader.FromJson<DefaultLineCatalog>(Text(defaultLines, "Npc/default_lines"));
            var forbiddenCatalog = NpcContentLoader.FromJson<ForbiddenCatalog>(Text(forbidden, "Npc/forbidden"));
            List<NpcTestScene> sceneList = NpcTestScenes.Parse(Text(scenes, "Npc/scenes"));
            Say("데이터: 페르소나 " + personaCatalog.personas.Count + ", 행동 " + actionCatalog.actions.Count
                + ", 기본 대사 도깨비 " + lineCatalog.spirits.Count + ", 장면 " + sceneList.Count);

            // 다시 돌리기: 앞 판의 모델(3GB 넘음)을 먼저 내린다. 그대로 두면 모델이 두 벌 올라가
            // 8GB 폰에서는 앱이 꺼지고, 살아남아도 이번 판의 메모리 값이 부풀려진다.
            if (ReleaseBackend())
            {
                await Task.Yield(); // 모델을 붙인 오브젝트는 프레임 끝에 지워진다. 다음 프레임에 새로 올린다
            }

            var rule = new RuleBasedReactionGenerator(lineCatalog);
            SlmReactionGenerator slm = null;
            model = CreateBackend();
            if (model != null)
            {
                // 형식 제약을 끄면(Grammar None) 모델이 JSON 모양을 알 수 있게 프롬프트에 형식 설명을 싣는다
                // LM Studio 는 JSON 스키마를 끄면(lmStudioJsonSchema) 같은 방식으로 형식 설명을 싣는다
                bool unconstrained = backend == BackendKind.LlmUnity ? grammar == LlmUnityGrammar.None : !lmStudioJsonSchema;
                var promptOptions = new PromptOptions { DescribeFormat = unconstrained };
                // 게임처럼 페르소나를 넘겨 자기 이름·존댓말 반말 섞기도 거른다. 걸리면 곁들일 거리를 바꿔 한 번 더 만든다
                slm = new SlmReactionGenerator(model, new PromptAssembler(personaCatalog, actionCatalog, promptOptions),
                        new NpcOutputValidator(new NpcLineFilter(forbiddenCatalog), personaCatalog));
                status = "모델 올리는 중";
                long before = MemoryMb();
                Stopwatch loading = Stopwatch.StartNew();
                try
                {
                    await slm.LoadAsync(ct);
                    Say("모델 올림 " + loading.ElapsedMilliseconds + "ms, 메모리 " + before + " → " + MemoryMb() + "MB" + MemoryNote());
                }
                catch (Exception e)
                {
                    if (e is OperationCanceledException)
                    {
                        throw;
                    }
                    Say("모델 올리기 실패 → 기본 대사로만 돈다: " + e.Message);
                }
            }

            // 이 판의 관리자는 지역 변수로 잡아 쓴다. OnDestroy(화면을 닫음)가 ct 를 취소하고 필드(manager)를 null 로 비우므로,
            // await 뒤에 필드를 다시 읽으면 NullReferenceException 으로 '중단됨' 대신 '오류'가 남는다
            var run = new NpcRequestManager(slm, rule)
            {
                SlmTimeout = TimeSpan.FromSeconds(slmTimeoutSeconds), PrefetchTimeout = TimeSpan.FromSeconds(prefetchTimeoutSeconds), RefillAfterUse = refillAfterUse,
                PrefetchIdleDelay = TimeSpan.FromSeconds(Math.Max(0f, prefetchIdleSeconds)), PrefetchSwitchIdleDelay = TimeSpan.FromSeconds(Math.Max(0f, prefetchSwitchIdleSeconds)),
                AdaptiveIdleDelay = adaptiveIdle
            };
            Say("뒤에서 만들기 전 쉬는 시간 " + Math.Max(0f, prefetchIdleSeconds) + "초(다른 도깨비는 " + Math.Max(0f, prefetchSwitchIdleSeconds) + "초), 장면 사이 쉬는 시간 " + Math.Max(0f, sceneGapSeconds) + "초");
            manager = run;
            if (useLineStore)
            {
                // 게임은 로딩 화면에서 이렇게 읽는다. 버전(콘텐츠·모델)이 바뀌면 칸 있는 대사는 버리고 새로 만든다
                var lineStore = new NpcLineFileStore(Path.Combine(Application.persistentDataPath, "npc_lines.txt"));
                string version = NpcLineStoreFormat.VersionOf(personaCatalog.contentVersion, actionCatalog.contentVersion, lineCatalog.contentVersion,
                        forbiddenCatalog.contentVersion, backend.ToString(), backend == BackendKind.LmStudio ? lmStudioModel : modelFile);
                Stopwatch loadingLines = Stopwatch.StartNew();
                int loadedLines = run.LoadStore(lineStore, version);
                Say("미리 만든 대사 파일: " + loadedLines + "개 읽음, " + loadingLines.ElapsedMilliseconds + "ms (" + lineStore.Path + ")");
            }
            // CSV 는 장면 하나가 끝날 때마다 이어 쓴다. 끝에서 한 번에 저장하면 중간에 실패하거나 앱이 꺼졌을 때 지금까지의 기록이 모두 사라진다
            string path = Path.Combine(Application.persistentDataPath,
                    "npc_device_test_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
            // 미리읽기ms 는 끝난 미리 읽기만 (시간 초과면 -1), 시간 초과는 미리읽기초과ms 에 기다린 시간을 따로 적는다
            File.WriteAllText(path, "장면,반복,출처,행동,기대행동일치,대사,글자수,미리읽기ms,첫응답ms,전체ms,메모리MB,메모,미리만든,채운칸,요청서버값,미리읽기초과ms,기억\n", new UTF8Encoding(true)); // BOM: 엑셀에서 한글이 안 깨진다
            csvPath = path;
            Say("CSV: " + path + " (장면마다 이어 쓴다)");

            // 미리 만들기: 장면마다 대사를 칸 있는 대사로 만들어 둔 뒤 요청한다 (게임이 서버 값을 받기 전에 한가할 때 만들어 두는 것과 같다)
            // 기억을 꺼내는 장면(S14)은 게임처럼 돌린다: 반복마다 기억을 남기고 꺼냈음을 표시해, 같은 기억을 연달아 꺼내지 않는다 (NpcTestMemory)
            var testMemory = new NpcTestMemory(personaCatalog, DateTime.Today);
            var memoryScenes = new HashSet<string>(sceneList.Where(NpcTestMemory.Handles).Select(sc => sc.Id));
            int prefetchHits = 0;
            if (prefetchFirst && slm != null && slm.IsReady)
            {
                status = "미리 만드는 중";
                Stopwatch preparing = Stopwatch.StartNew();
                foreach (NpcTestScene scene in sceneList)
                {
                    run.PrefetchTemplate(scene.CreateRequest(), Math.Max(1, prefetchPerScene));
                    if (memoryScenes.Contains(scene.Id))
                    {
                        // 게임의 PrefetchTemplates 처럼 지난 대화 다음의 평소 안부도 미리 만든다
                        foreach (ReactionRequest follow in testMemory.PrefetchRequests(scene))
                        {
                            run.PrefetchTemplate(follow, Math.Max(1, prefetchPerScene));
                        }
                    }
                }
                while (preparing.Elapsed.TotalSeconds < prefetchWaitSeconds)
                {
                    ct.ThrowIfCancellationRequested();
                    Task working = run.PrefetchCompletion;
                    if (working == null || working.IsCompleted)
                    {
                        break;
                    }
                    await Task.Delay(500, ct);
                }
                Say("미리 만들기: " + preparing.ElapsedMilliseconds + "ms, 보관한 칸 있는 대사 " + run.Cache.TotalCount + "개 (장면 " + sceneList.Count + "개 × 상황당 최대 " + prefetchPerScene + "개)");
            }
            var slmTimes = new List<long>();
            var firstTokens = new List<long>();
            var prepareTimes = new List<long>();
            var restoredPrepares = new List<long>(); // 저장해 둔 기억을 되살린 미리 읽기의 시간
            var readPrepares = new List<long>();     // 시스템 프롬프트를 실제로 읽은 미리 읽기의 시간 (같은 도깨비가 이어져 바로 끝난 것은 뺀다)
            var reasons = new Dictionary<string, int>();
            int total = 0;
            int fixedCount = 0;
            int slmCount = 0;
            int trimmed = 0;
            int speechFixed = 0;
            int expectedOk = 0;
            int prepareTimeouts = 0;
            long peak = 0;

            for (int rep = 1; rep <= Math.Max(1, repeat); rep++)
            {
                foreach (NpcTestScene scene in sceneList)
                {
                    ct.ThrowIfCancellationRequested();
                    total++;
                    status = scene.Id + " #" + rep + " (" + total + "/" + sceneList.Count * Math.Max(1, repeat) + ")";
                    NpcContext memoryContext = memoryScenes.Contains(scene.Id) ? testMemory.Build(scene) : null;
                    ReactionRequest request = memoryContext != null ? memoryContext.Request : scene.CreateRequest();
                    // 게임처럼 반복마다 곁들일 거리를 바꾼다 (run.py 와 같게 1번째 반복 = 0번째)
                    request.Variation = rep - 1;
                    string retargeted = requestWithOtherServerValues ? Retarget(request) : "";

                    // 게임에서 탭해 확대할 때처럼 시스템 프롬프트를 먼저 읽힌다. 같은 도깨비가 이어지면 바로 끝난다
                    long prepareMs = -1;        // 끝난 미리 읽기의 시간. 시간 초과·안 함이면 -1 (통계에 넣지 않는다)
                    long prepareTimedOutMs = -1; // 시간 초과면 기다린 시간
                    // 기억(KV 슬롯) 저장·복원이 이 장면에서 무엇을 했는지 (CSV 의 '기억' 열). 공용 횟수(StateLoads·StateSaves)는 뒤에서 만들기의 것까지 세므로 쓰지 않고,
                    // 이 미리 읽기·요청이 남긴 기록(LastPrepare·LastCall 이 새로 바뀌었는지와 그 기록의 되살림·저장 여부)으로 정한다
                    LlmUnityModelBackend llm = model as LlmUnityModelBackend;
                    string kvNote = "";
                    LlmUnityCall prepareBefore = llm != null ? llm.LastPrepare : null;
                    if (prepareSystemPrompt && slm != null && slm.IsReady && !NpcActions.SkipsModel(request.Candidates))
                    {
                        Stopwatch preparing = Stopwatch.StartNew();
                        // 기한과 경주시킨다. 취소 타이머만 걸면, 취소를 듣지 않는 일(기억 저장·복원 등)에서 미리 읽기가 끝나지 않을 때 시험이 이 장면에서 멈춘다.
                        // 기한이 먼저면 미리 읽기를 취소하고 기다리지 않고 넘어간다 (백엔드는 그 일이 끝날 때까지 모델 줄을 붙잡으니, 다음 요청은 그 뒤에 선다).
                        // 미리 읽기는 실패해도 요청이 처음부터 읽을 뿐이다
                        var prepareCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        // 미리 읽기가 끝나고, 아래 기한 분기(Cancel 포함)도 끝난 뒤에만 버린다. 미리 읽기가 Cancel 도중에 끝나
                        // 그 자리에서 버리면 Cancel 이 버린 것을 건드린다
                        int ctsHolders = 2;
                        Action releaseCts = () =>
                        {
                            if (Interlocked.Decrement(ref ctsHolders) == 0)
                            {
                                prepareCts.Dispose();
                            }
                        };
                        Task prepareTask = run.PrepareAsync(scene.RelicId, prepareCts.Token);
                        prepareTask.ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                t.Exception.Handle(e => true);
                            }
                            releaseCts();
                        }, TaskContinuationOptions.ExecuteSynchronously);
                        Task deadline = Task.Delay(TimeSpan.FromSeconds(Math.Max(1f, prepareTimeoutSeconds)));
                        bool prepared;
                        try
                        {
                            // 시간 초과는 경주 결과로만 정한다 — 메인 스레드가 늦게 이어 받아도 이미 끝난 미리 읽기를 시간 초과로 치지 않는다
                            prepared = await Task.WhenAny(prepareTask, deadline) == prepareTask;
                            if (!prepared)
                            {
                                prepareCts.Cancel();
                            }
                        }
                        finally
                        {
                            releaseCts();
                        }
                        // 화면을 닫는 중이면(OnDestroy 가 ct 를 취소) 다른 것을 건드리기 전에 멈춘다
                        ct.ThrowIfCancellationRequested();
                        if (prepared)
                        {
                            prepareMs = preparing.ElapsedMilliseconds;
                            prepareTimes.Add(prepareMs);
                            if (run.LastPrepareDeferred)
                            {
                                // 같은 도깨비의 뒤에서 만들기에 맡겨 미리 읽지 않았다 — 미리읽기ms 가 0 에 가까워도 시스템 메시지가 다 읽힌 것은 아니다 (통계에서도 뺀다)
                                kvNote = "뒤에서 만들기에 맡김";
                                prepareTimes.RemoveAt(prepareTimes.Count - 1);
                            }
                            else if (llm != null)
                            {
                                bool didPrepare = llm.LastPrepare != prepareBefore; // 이 미리 읽기가 모델에서 무언가를 했다 (이미 읽힌 것이면 기록이 안 바뀐다)
                                kvNote = !didPrepare ? "이미 읽힘"
                                        : llm.LastPrepareRestored ? "미리읽기 되살림"
                                        : llm.LastPrepareSaved ? "미리읽기 읽고 저장" : "미리읽기 읽음";
                                if (kvNote == "미리읽기 되살림")
                                {
                                    restoredPrepares.Add(prepareMs);
                                }
                                else if (kvNote != "이미 읽힘")
                                {
                                    readPrepares.Add(prepareMs);
                                }
                            }
                        }
                        else
                        {
                            prepareTimedOutMs = preparing.ElapsedMilliseconds;
                            prepareTimeouts++;
                            Say(scene.Id + " 미리 읽기 시간 초과(" + prepareTimeoutSeconds + "초) → 기다리지 않고 요청" + (run.SlmStalled ? " (모델이 멈춤)" : ""));
                        }
                    }

                    LlmUnityCall callBefore = llm != null ? llm.LastCall : null;
                    Stopwatch watch = Stopwatch.StartNew();
                    NpcResponse response = await run.RequestAsync(request, ct);
                    watch.Stop();
                    // 이 요청의 생성이 끝나 LastCall 을 새로 남겼고(뒤에서 만들기는 남기지 않는다) 그 생성이 기억을 되살렸을 때만
                    if (llm != null && llm.LastCall != callBefore && llm.LastCallRestored)
                    {
                        kvNote += (kvNote.Length > 0 ? ", " : "") + "요청 전 되살림"; // 모델이 다른 도깨비를 기억하던 채로 온 요청 (미리 읽기를 안 했거나 그 사이 바뀜)
                    }

                    long memory = MemoryMb();
                    peak = Math.Max(peak, memory);
                    bool isSilent = response.Note == NpcRequestManager.SilentNote;
                    bool isFixed = response.Note == NpcRequestManager.FixedLinesNote || isSilent;
                    bool fromSlm = response.ShouldShow && !response.UsedFallback;
                    bool prefetched = response.ShouldShow && response.Note == NpcRequestManager.PrefetchedNote;
                    string source = !response.ShouldShow ? "없음" : (prefetched ? "미리" : (fromSlm ? "SLM" : (isSilent ? "대사없음" : (isFixed ? "정해진" : "기본"))));
                    string action = response.ShouldShow ? response.Reaction.ActionId : "";
                    string line = response.ShouldShow ? response.Reaction.Line : "";
                    // 기억 장면은 반복마다 요청이 달라진다(지난 대화 → 평소 안부) — 그 요청의 후보면 맞은 것으로 본다
                    bool expected = scene.Expected.Contains(action) || (memoryContext != null && request.Candidates.Any(c => c.ActionId == action));
                    if (memoryContext != null)
                    {
                        retargeted += (retargeted.Length > 0 ? ", " : "") + testMemory.Finish(scene, memoryContext, response);
                    }
                    long firstToken = fromSlm && !prefetched ? FirstTokenMs() : -1;
                    if (isFixed)
                    {
                        fixedCount++;
                    }
                    else if (prefetched)
                    {
                        // 미리 만든 대사(칸을 채워 완성): 모델을 부르지 않아 시간은 거의 0 이다. SLM 시간 통계에는 넣지 않는다
                        prefetchHits++;
                    }
                    else if (fromSlm)
                    {
                        slmCount++;
                        // Note 에는 문장을 잘라 낸 것과 어미를 고친 것이 함께 적힌다 (OutputCheck.ToResult)
                        string reactionNote = response.Reaction.Note ?? "";
                        trimmed += reactionNote.Contains("잘라") || reactionNote.Contains("끊긴") ? 1 : 0;
                        speechFixed += reactionNote.Contains(OutputCheck.SpeechFixNote) ? 1 : 0;
                        slmTimes.Add(watch.ElapsedMilliseconds);
                        if (firstToken >= 0)
                        {
                            firstTokens.Add(firstToken);
                        }
                    }
                    else
                    {
                        string reason = Reason(response.Note);
                        reasons[reason] = (reasons.ContainsKey(reason) ? reasons[reason] : 0) + 1;
                    }
                    if (expected)
                    {
                        expectedOk++;
                    }

                    Say(scene.Id + " " + source + " " + action + " " + line + "  (" + watch.ElapsedMilliseconds + "ms"
                        + (prepareMs >= 0 ? ", 미리 읽기 " + prepareMs + "ms" : "")
                        + (prepareTimedOutMs >= 0 ? ", 미리 읽기 시간 초과(" + prepareTimedOutMs + "ms 기다림)" : "")
                        + (firstToken >= 0 ? ", 첫 응답 " + firstToken + "ms" : "")
                        + (kvNote.Length > 0 ? ", " + kvNote : "") + ")"
                        + (fromSlm && response.Reaction.Note == null ? "" : "  ← " + response.Note));
                    var row = new StringBuilder();
                    row.Append(Csv(scene.Id)).Append(',').Append(rep).Append(',').Append(source).Append(',').Append(action).Append(',')
                        .Append(expected ? "O" : "X").Append(',').Append(Csv(line)).Append(',').Append(line.Length).Append(',')
                        .Append(prepareMs).Append(',').Append(firstToken).Append(',').Append(watch.ElapsedMilliseconds).Append(',')
                        .Append(memory).Append(',').Append(Csv(response.Note)).Append(',').Append(prefetched ? "O" : "X").Append(',')
                        .Append(Csv(prefetched && response.Reaction != null ? response.Reaction.Note : "")).Append(',').Append(Csv(retargeted)).Append(',')
                        .Append(prepareTimedOutMs >= 0 ? prepareTimedOutMs.ToString(CultureInfo.InvariantCulture) : "").Append(',')
                        .Append(Csv(kvNote)).Append('\n');
                    AppendCsv(path, row.ToString());

                    // 플레이어가 잠깐 쉬는 틈 — 다시 만들기는 이 사이에 돈다. 시간에는 넣지 않는다 (다음 장면의 미리 읽기부터 잰다)
                    if (sceneGapSeconds > 0f)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(sceneGapSeconds), ct);
                    }
                }
            }

            Say("=== 요약 ===");
            LlmUnityModelBackend llm0 = model as LlmUnityModelBackend;
            Say("SLM 대사 " + slmCount + "/" + (total - fixedCount) + " (이야기 " + fixedCount + "건은 정해진 대사, 문장 잘라 씀 " + trimmed + "건"
                + ", 다듬어 바로 씀 " + speechFixed + "건)"
                + ", 기대 행동 일치 " + expectedOk + "/" + total);
            if (prepareTimes.Count > 0)
            {
                Say("시스템 프롬프트 미리 읽기: 중앙값 " + Percentile(prepareTimes, 0.5) + "ms, 최대 " + prepareTimes.Max() + "ms"
                    + " (같은 도깨비가 이어지면 0 에 가깝다, 끝난 " + prepareTimes.Count + "건만)");
            }
            if (restoredPrepares.Count > 0 || readPrepares.Count > 0)
            {
                // 되살림이 읽기보다 훨씬 짧아야 한다. 되살린 뒤의 첫 응답이 읽은 뒤와 비슷하면 되살린 기억을 llama.cpp 가 앞부분으로 다시 쓰고 있다는 뜻이다
                Say("기억 되살림 " + restoredPrepares.Count + "건" + (restoredPrepares.Count > 0 ? " (중앙값 " + Percentile(restoredPrepares, 0.5) + "ms)" : "")
                    + ", 실제로 읽음 " + readPrepares.Count + "건" + (readPrepares.Count > 0 ? " (중앙값 " + Percentile(readPrepares, 0.5) + "ms)" : "")
                    + (llm0 != null ? ", 저장 " + llm0.StateSaves + "번" : "") + (saveSystemState ? "" : " — 기억 저장 꺼짐"));
            }
            if (prepareTimeouts > 0)
            {
                Say("시스템 프롬프트 미리 읽기 시간 초과 " + prepareTimeouts + "건 (" + prepareTimeoutSeconds + "초, 위 통계에서 뺌 — CSV 의 미리읽기초과ms)");
            }
            if (run.SlmStalled)
            {
                Say("모델이 멈춰 있다 (취소한 생성이 CancelHold 가 지나도 끝나지 않음) — 프롬프트 읽기 중 취소 버그가 다시 난 것일 수 있다. 로그를 남겨 둔다");
            }
            foreach (KeyValuePair<string, int> reason in reasons.OrderByDescending(r => r.Value))
            {
                Say("기본 대사로 대신: " + reason.Key + " " + reason.Value + "건");
            }
            if (slmTimes.Count > 0)
            {
                Say("SLM 전체 시간: 중앙값 " + Percentile(slmTimes, 0.5) + "ms, 90% " + Percentile(slmTimes, 0.9) + "ms, 최대 " + slmTimes.Max() + "ms");
            }
            if (firstTokens.Count > 0)
            {
                Say("첫 응답 시간: 중앙값 " + Percentile(firstTokens, 0.5) + "ms, 90% " + Percentile(firstTokens, 0.9) + "ms");
            }
            // 시작 때 미리 만들지 않아도 파일에서 읽은 대사·다시 만든 대사로 적중할 수 있다 (CodeRabbit 리뷰)
            if (prefetchFirst || useLineStore || refillAfterUse || prefetchHits > 0)
            {
                int asked = total - fixedCount;
                Say("미리 만든 대사 적중 " + prefetchHits + "/" + asked + " (적중하면 모델을 부르지 않고 칸에 서버 값을 넣어 바로 완성 — 전체 시간 0ms 안팎)");
            }
            if (useLineStore)
            {
                Say("미리 만든 대사 파일에 씀: " + (run.SaveStore() ? "보관한 칸 있는 대사 " + run.Cache.TotalCount + "개" : "실패"));
            }
            Say("메모리 최대 " + peak + "MB" + MemoryNote());

            Say("CSV 저장: " + path);
            status = "끝";
        }

        private INpcModelBackend CreateBackend()
        {
            switch (backend)
            {
                case BackendKind.LlmUnity:
#if JAMKKAEBI_LLMUNITY
                    return new LlmUnityModelBackend(new LlmUnityRuntime(gameObject), new LlmUnityOptions
                    {
                        Model = modelFile,
                        NumGpuLayers = gpuLayers >= 0 ? gpuLayers : (Application.platform == RuntimePlatform.IPhonePlayer ? 99 : 0),
                        NumThreads = threads,
                        Grammar = grammar,
                        SaveSystemState = saveSystemState,
                        DownloadProgress = p => status = "모델 내려받는 중 " + (int)(p * 100) + "%"
                    });
#else
                    Say("JAMKKAEBI_LLMUNITY 심볼이 없어 LLMUnity 를 쓸 수 없다 → 기본 대사로만 돈다 (Player Settings > Scripting Define Symbols)");
                    return null;
#endif
                case BackendKind.LmStudio:
                    return new LmStudioModelBackend(new LmStudioOptions { BaseUrl = lmStudioUrl, Model = lmStudioModel, UseJsonSchema = lmStudioJsonSchema });
                default:
                    return null;
            }
        }

        private long FirstTokenMs()
        {
            var llm = model as LlmUnityModelBackend;
            return llm != null && llm.LastCall != null ? llm.LastCall.FirstTokenMs : -1;
        }

        // "SLM 실패 InvalidOutput (TooLong: 79자 …) → 기본 대사" → "InvalidOutput TooLong"
        private static string Reason(string note)
        {
            if (string.IsNullOrEmpty(note))
            {
                return "알 수 없음";
            }
            if (note.Contains("시간 초과"))
            {
                return "시간 초과";
            }
            if (note == NpcRequestManager.StalledNote)
            {
                return "모델이 멈춤";
            }
            int cut = note.IndexOf(':');
            string head = cut > 0 ? note.Substring(0, cut) : note;
            return head.Replace("SLM 실패 ", "").Replace("(", "").Replace(" → 기본 대사", "").Trim();
        }

        private static long Percentile(List<long> values, double p)
        {
            List<long> sorted = values.OrderBy(v => v).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(p * sorted.Count))];
        }

        // 안드로이드는 앱 전체가 실제로 쓰는 메모리(VmRSS, 모델 포함)를 읽는다. 그 밖에는 Unity 가 잡은 메모리만 보인다
        private static long MemoryMb()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                foreach (string row in File.ReadAllLines("/proc/self/status"))
                {
                    if (row.StartsWith("VmRSS:", StringComparison.Ordinal))
                    {
                        string kb = new string(row.Where(char.IsDigit).ToArray());
                        return long.Parse(kb, CultureInfo.InvariantCulture) / 1024;
                    }
                }
            }
            catch (Exception)
            {
            }
#endif
            return UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
        }

        private static string MemoryNote()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return " (앱 전체, 모델 포함)";
#else
            return " (Unity 메모리만, 모델 제외 — iOS 는 Xcode 의 Memory 게이지로 본다)";
#endif
        }

        private static string Text(TextAsset asset, string resourcePath)
        {
            TextAsset found = asset != null ? asset : Resources.Load<TextAsset>(resourcePath);
            if (found == null)
            {
                throw new InvalidOperationException("데이터가 없다: 인스펙터에 넣거나 Resources/" + resourcePath + ".json 에 둔다");
            }
            return found.text;
        }

        // 중간에 멈추거나 실패했을 때, 그때까지 쌓인 CSV 가 어디 있는지 알려 주는 말
        private string SavedSoFar()
        {
            return csvPath != null ? "  (지금까지의 기록은 CSV 에 남아 있다: " + csvPath + ")" : "";
        }

        // 줄 하나를 파일 끝에 붙인다. 열 때마다 닫으므로 도중에 앱이 꺼져도 지금까지의 줄은 남는다.
        // 머리글에 BOM 을 이미 썼으므로 여기서는 BOM 없이 쓴다. 쓰기에 실패해도(저장 공간 부족 등) 시험은 계속한다
        private void AppendCsv(string path, string text)
        {
            try
            {
                File.AppendAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Say("CSV 쓰기 실패 (시험은 계속한다): " + e.Message);
            }
        }

        private static readonly Regex SituationTime = new Regex("(?<=주인이 )(새벽|아침|오전|낮|저녁|밤)(?=에 )");

        // 요청의 서버 값을 미리 만들 때와 다르게 바꾼다: 시간대는 다른 시간대로(탭 인사에만 있다), 닉네임은 '하늘'(친밀 3단계). 바꾼 것을 글로 돌려준다
        private static string Retarget(ReactionRequest request)
        {
            var changed = new List<string>();
            if (request.Facts != null)
            {
                foreach (Fact fact in request.Facts)
                {
                    if (fact.Key == NpcFactKeys.TimeOfDay && !string.IsNullOrEmpty(fact.Value))
                    {
                        string from = fact.Value;
                        string to = from == "저녁" ? "아침" : "저녁";
                        fact.Value = to;
                        request.Situation = SituationTime.Replace(request.Situation ?? "", to);
                        changed.Add("시간대 " + from + "→" + to);
                    }
                }
            }
            request.Nickname = "하늘";
            request.Intimacy = NpcAddress.NicknameLevel;
            changed.Add("호칭 하늘");
            return string.Join(", ", changed.ToArray());
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private void Say(string line)
        {
            lines.Add(line);
            Debug.Log("[NPC시험] " + line);
        }

        private void OnGUI()
        {
            int size = Mathf.Max(14, Screen.height / 50);
            GUI.skin.label.fontSize = size;
            GUI.skin.label.wordWrap = true;
            GUI.skin.button.fontSize = size;
            float pad = size;
            GUILayout.BeginArea(new Rect(pad, pad, Screen.width - pad * 2, Screen.height - pad * 2));
            GUILayout.BeginHorizontal();
            GUILayout.Label("상태: " + status);
            if (!running && GUILayout.Button("다시 돌리기", GUILayout.Width(size * 8)))
            {
                Run();
            }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (string line in lines)
            {
                GUILayout.Label(line);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
#endif
