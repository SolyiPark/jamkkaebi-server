// 실기기 시험(Unity/NpcDeviceTestRunner.cs)을 Unity 없이 PC 에서 돌린다. 모델은 LM Studio 로 부른다.
//
//   verify.bat --device [--url http://127.0.0.1:1234] [--model kanana-1.5-2.1b-instruct-2505] [--repeat 4] [--gap 0] [--idle 3] [--switch-idle 30] [--no-adaptive-idle]
//                       [--seed 0] [--fresh] [--per-scene 2] [--no-prefetch] [--no-store] [--no-refill] [--no-prepare] [--same-server-values]
//
// 흐름은 실기기 시험과 같다: 파일(npc_lines.txt)을 읽고 → 장면마다 칸 있는 대사를 미리 만들고 → 장면을 반복해 요청한다.
// 요청마다 그 도깨비의 시스템 프롬프트를 먼저 읽히고(미리 읽기), 서버 값(시간대·호칭)을 바꿔 요청하고, 장면 사이에 --gap 초 쉰다.
// CSV 는 실기기 시험과 같은 열로 Verify/bin/device/ 에 쓴다 (첫응답ms·메모리MB 는 LM Studio 에서 잴 수 없어 -1).
//
// 실기기와 다른 점: LLMUnity 가 아니라 LM Studio(llama.cpp 서버)를 부른다. 미리 읽기는 같은 도깨비가 이어져도 1토큰 생성을 한 번 한다(LLMUnity 는 건너뜀).
// seed 는 --seed 를 주면 매번 그 값(고치기 전 LLMUnity 의 seed 0 과 같은 상황),
// 주지 않으면 LM Studio 가 매번 무작위로 정한다(고친 뒤 LLMUnity 와 같은 상황). PC 가 폰보다 빠르므로 시간은 같은 PC 의 결과끼리 비교한다.
// 실기기 시험 화면의 흐름을 고치면 이 파일도 같이 고친다.
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
using Jamkkaebi.Npc;

public static class DeviceRun
{
    private sealed class Settings
    {
        public string Url = "http://127.0.0.1:1234";
        public string Model = "kanana-1.5-2.1b-instruct-2505";
        public int Repeat = 4;
        public double GapSeconds = 0;
        public double IdleSeconds = 3;
        public double SwitchIdleSeconds = 30;
        public bool AdaptiveIdle = true;
        public int? Seed;
        public bool Fresh;
        public int PerScene = 2;
        public bool Prefetch = true;
        public bool Store = true;
        public bool Refill = true;
        public bool Prepare = true;
        public bool OtherServerValues = true;
        public double SlmTimeoutSeconds = 30;
        public double PrefetchTimeoutSeconds = 120;
        public double PrefetchWaitSeconds = 900;
        public double PrepareTimeoutSeconds = 180;
    }

    private static readonly Regex SituationTime = new Regex("(?<=주인이 )(새벽|아침|오전|낮|저녁|밤)(?=에 )");
    private static string logPath;

    // args 는 "--device" 다음부터. 끝까지 돌면 0, 중간에 멈추거나 실패하면 1
    public static int Run(string[] args, string scenesJson, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines, ForbiddenCatalog forbidden)
    {
        Settings settings;
        try
        {
            settings = Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.WriteLine("잘못된 옵션: " + e.Message);
            return 2;
        }

        string dir = Path.GetFullPath(Path.Combine("bin", "device"));
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        logPath = Path.Combine(dir, "npc_device_test_" + stamp + ".log");

        // Ctrl+C 를 누르면 지금 장면에서 멈추고, 그때까지의 CSV 와 미리 만든 대사 파일을 남긴다
        var stopping = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (sender, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            return RunAll(settings, dir, stamp, scenesJson, personas, actions, lines, forbidden, stopping.Token).GetAwaiter().GetResult() ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Say("중단됨 (Ctrl+C) — 지금까지의 기록은 CSV 에 남아 있다");
            return 1;
        }
        catch (Exception e)
        {
            Say("오류: " + e);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static Settings Parse(string[] args)
    {
        var s = new Settings();
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            switch (name)
            {
                case "--url": s.Url = Value(args, ref i); break;
                case "--model": s.Model = Value(args, ref i); break;
                case "--repeat": s.Repeat = Math.Max(1, (int)Number(args, ref i)); break;
                case "--gap": s.GapSeconds = Math.Max(0, Number(args, ref i)); break;
                case "--idle": s.IdleSeconds = Math.Max(0, Number(args, ref i)); break;
                case "--switch-idle": s.SwitchIdleSeconds = Math.Max(0, Number(args, ref i)); break;
                case "--no-adaptive-idle": s.AdaptiveIdle = false; break;
                case "--seed": s.Seed = (int)Number(args, ref i); break;
                case "--per-scene": s.PerScene = Math.Max(1, (int)Number(args, ref i)); break;
                case "--slm-timeout": s.SlmTimeoutSeconds = Math.Max(1, Number(args, ref i)); break;
                case "--prefetch-timeout": s.PrefetchTimeoutSeconds = Math.Max(1, Number(args, ref i)); break;
                case "--prefetch-wait": s.PrefetchWaitSeconds = Math.Max(0, Number(args, ref i)); break;
                case "--prepare-timeout": s.PrepareTimeoutSeconds = Math.Max(1, Number(args, ref i)); break;
                case "--fresh": s.Fresh = true; break;
                case "--no-prefetch": s.Prefetch = false; break;
                case "--no-store": s.Store = false; break;
                case "--no-refill": s.Refill = false; break;
                case "--no-prepare": s.Prepare = false; break;
                case "--same-server-values": s.OtherServerValues = false; break;
                default: throw new ArgumentException(name + " (쓸 수 있는 옵션은 Verify/DeviceRun.cs 맨 위에 있다)");
            }
        }
        return s;
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException(args[i] + " 뒤에 값이 없다");
        }
        return args[++i];
    }

    private static double Number(string[] args, ref int i)
    {
        string name = args[i];
        double value;
        if (!double.TryParse(Value(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            throw new ArgumentException(name + " 의 값이 숫자가 아니다: " + args[i]);
        }
        return value;
    }

    // LM Studio 에 연결하지 못하면 false
    private static async Task<bool> RunAll(Settings s, string dir, string stamp, string scenesJson, PersonaCatalog personas, ActionCatalog actions,
                                     DefaultLineCatalog lineCatalog, ForbiddenCatalog forbidden, CancellationToken ct)
    {
        Say("=== 실기기 시험을 PC 에서 (LM Studio " + s.Url + ", " + s.Model + ") ===");
        Say("반복 " + s.Repeat + ", 장면 사이 쉬는 시간 " + s.GapSeconds + "초, 뒤에서 만들기 전 쉬는 시간 " + s.IdleSeconds + "초(다른 도깨비는 " + s.SwitchIdleSeconds + "초, 기기 속도에 맞춰 줄이기 " + Onoff(s.AdaptiveIdle) + "), seed "
            + (s.Seed.HasValue ? s.Seed.Value + " (매번 같음 — 고치기 전 LLMUnity 와 같은 상황)" : "없음 (LM Studio 가 매번 무작위)"));
        Say("미리 만들기 " + Onoff(s.Prefetch) + " (상황당 " + s.PerScene + "개), 대사 파일 " + Onoff(s.Store) + ", 쓴 만큼 다시 만들기 " + Onoff(s.Refill)
            + ", 미리 읽기 " + Onoff(s.Prepare) + ", 요청 때 서버 값 바꾸기 " + Onoff(s.OtherServerValues));

        List<NpcTestScene> sceneList = NpcTestScenes.Parse(scenesJson);
        Say("데이터: 페르소나 " + personas.personas.Count + ", 행동 " + actions.actions.Count + ", 기본 대사 도깨비 " + lineCatalog.spirits.Count + ", 장면 " + sceneList.Count);

        var lmStudio = new LmStudioModelBackend(new LmStudioOptions { BaseUrl = s.Url, Model = s.Model, UseJsonSchema = false, Seed = s.Seed });
        var rule = new RuleBasedReactionGenerator(lineCatalog);
        // 실기기 시험처럼 JSON 스키마를 끄고 프롬프트에 형식 설명을 싣는다 (Kanana 는 JSON 스키마를 켜면 오류)
        var slm = new SlmReactionGenerator(lmStudio, new PromptAssembler(personas, actions, new PromptOptions { DescribeFormat = true }),
                new NpcOutputValidator(new NpcLineFilter(forbidden), personas));
        Stopwatch loading = Stopwatch.StartNew();
        try
        {
            await slm.LoadAsync(ct);
            Say("모델 연결 " + loading.ElapsedMilliseconds + "ms");
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException)
            {
                throw;
            }
            Say("LM Studio 에 연결하지 못함: " + e.Message);
            Say("LM Studio 의 Developer 탭에서 서버를 켜고 모델을 올렸는지 확인한다 (주소 " + s.Url + ", 모델 이름 " + s.Model + ")");
            lmStudio.Dispose();
            return false;
        }

        var run = new NpcRequestManager(slm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(s.SlmTimeoutSeconds), PrefetchTimeout = TimeSpan.FromSeconds(s.PrefetchTimeoutSeconds), RefillAfterUse = s.Refill,
            PrefetchIdleDelay = TimeSpan.FromSeconds(s.IdleSeconds), PrefetchSwitchIdleDelay = TimeSpan.FromSeconds(s.SwitchIdleSeconds),
            AdaptiveIdleDelay = s.AdaptiveIdle
        };
        try
        {
            await RunScenes(s, dir, stamp, run, slm, sceneList, personas, actions, lineCatalog, forbidden, ct);
        }
        finally
        {
            // 다시 만들기·뒤에서 하는 저장을 멈추고 지금까지 보관한 대사를 파일에 쓴다 (실기기 시험의 ReleaseBackend 와 같다)
            run.CloseStore(true, TimeSpan.FromSeconds(10));
            run.CancelAll();
            lmStudio.Dispose();
        }
        return true;
    }

    private static async Task RunScenes(Settings s, string dir, string stamp, NpcRequestManager run, SlmReactionGenerator slm, List<NpcTestScene> sceneList,
                                        PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lineCatalog, ForbiddenCatalog forbidden, CancellationToken ct)
    {
        if (s.Store)
        {
            string storePath = Path.Combine(dir, "npc_lines.txt");
            if (s.Fresh)
            {
                // 처음 설치한 것처럼: 앞 판의 파일과, 바꿔 넣다 멈춰 남은 임시 파일을 지운다
                foreach (string file in new[] { storePath, storePath + ".tmp" })
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
            }
            var lineStore = new NpcLineFileStore(storePath);
            string version = NpcLineStoreFormat.VersionOf(personas.contentVersion, actions.contentVersion, lineCatalog.contentVersion,
                    forbidden.contentVersion, "LmStudio", s.Model);
            Stopwatch loadingLines = Stopwatch.StartNew();
            int loadedLines = run.LoadStore(lineStore, version);
            Say("미리 만든 대사 파일: " + loadedLines + "개 읽음, " + loadingLines.ElapsedMilliseconds + "ms (" + lineStore.Path + ")" + (s.Fresh ? " — --fresh 로 지우고 시작" : ""));
        }

        string path = Path.Combine(dir, "npc_device_test_" + stamp + ".csv");
        File.WriteAllText(path, "장면,반복,출처,행동,기대행동일치,대사,글자수,미리읽기ms,첫응답ms,전체ms,메모리MB,메모,미리만든,채운칸,요청서버값,미리읽기초과ms,기억\n", new UTF8Encoding(true));
        Say("CSV: " + path + " (장면마다 이어 쓴다), 로그: " + logPath);

        // 기억을 꺼내는 장면(S14)은 게임처럼 돌린다: 반복마다 기억을 남기고 꺼냈음을 표시해, 같은 기억을 연달아 꺼내지 않는다 (NpcTestMemory)
        var testMemory = new NpcTestMemory(personas, DateTime.Today);
        var memoryScenes = new HashSet<string>(sceneList.Where(NpcTestMemory.Handles).Select(sc => sc.Id));
        int prefetchHits = 0;
        if (s.Prefetch && slm.IsReady)
        {
            Stopwatch preparing = Stopwatch.StartNew();
            foreach (NpcTestScene scene in sceneList)
            {
                run.PrefetchTemplate(scene.CreateRequest(), s.PerScene);
                if (memoryScenes.Contains(scene.Id))
                {
                    // 게임의 PrefetchTemplates 처럼 지난 대화 다음의 평소 안부도 미리 만든다
                    foreach (ReactionRequest follow in testMemory.PrefetchRequests(scene))
                    {
                        run.PrefetchTemplate(follow, s.PerScene);
                    }
                }
            }
            while (preparing.Elapsed.TotalSeconds < s.PrefetchWaitSeconds)
            {
                ct.ThrowIfCancellationRequested();
                Task working = run.PrefetchCompletion;
                if (working == null || working.IsCompleted)
                {
                    break;
                }
                await Task.Delay(500, ct);
            }
            Say("미리 만들기: " + preparing.ElapsedMilliseconds + "ms, 보관한 칸 있는 대사 " + run.Cache.TotalCount + "개 (장면 " + sceneList.Count + "개 × 상황당 최대 " + s.PerScene + "개)");
        }

        var slmTimes = new List<long>();
        int prepareDeferred = 0;                // 같은 도깨비의 뒤에서 만들기에 맡겨 미리 읽지 않은 수 (NpcRequestManager.LastPrepareDeferred)
        var prepareSame = new List<long>();     // 앞 장면과 같은 도깨비 — 시스템 프롬프트가 이미 읽혀 있어 짧은 호출 한 번이어야 한다
        var prepareChanged = new List<long>();  // 도깨비가 바뀜 — 시스템 프롬프트를 새로 읽는다
        var reasons = new Dictionary<string, int>();
        var hitsPerRep = new List<int>();
        int total = 0;
        int fixedCount = 0;
        int slmCount = 0;
        int expectedOk = 0;
        int prepareTimeouts = 0;
        int repeated = 0;
        int lastRelic = -1;
        int all = sceneList.Count * s.Repeat;

        for (int rep = 1; rep <= s.Repeat; rep++)
        {
            int repHits = 0;
            foreach (NpcTestScene scene in sceneList)
            {
                ct.ThrowIfCancellationRequested();
                total++;
                NpcContext memoryContext = memoryScenes.Contains(scene.Id) ? testMemory.Build(scene) : null;
                ReactionRequest request = memoryContext != null ? memoryContext.Request : scene.CreateRequest();
                request.Variation = rep - 1;
                string retargeted = s.OtherServerValues ? Retarget(request) : "";

                long prepareMs = -1;
                long prepareTimedOutMs = -1;
                string kvNote = ""; // CSV 의 기억 열 (기기 시험과 같은 열)
                if (s.Prepare && slm.IsReady && !NpcActions.SkipsModel(request.Candidates))
                {
                    Stopwatch preparing = Stopwatch.StartNew();
                    var prepareCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    // 토큰은 미리 읽기가 끝나고, 아래 기한 분기(Cancel 포함)도 끝난 뒤에만 버린다 — 미리 읽기가 Cancel 도중에 끝나
                    // 그 자리에서 버리면 Cancel 이 버린 것을 건드린다 (실기기 시험과 같다, CodeRabbit 리뷰)
                    int ctsHolders = 2;
                    Action releaseCts = () =>
                    {
                        if (Interlocked.Decrement(ref ctsHolders) == 0)
                        {
                            prepareCts.Dispose();
                        }
                    };
                    Task prepareTask = run.PrepareAsync(scene.RelicId, prepareCts.Token);
                    // 기한과 경주시킨다. 기한이 먼저면 취소만 걸고 기다리지 않고 요청한다 — 취소를 듣지 않는 일에서도 시험이 멈추지 않게.
                    // 미리 읽기는 실패해도 요청이 처음부터 읽을 뿐이다
                    Task released = prepareTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            t.Exception.Handle(e => true);
                        }
                        releaseCts();
                    }, TaskContinuationOptions.ExecuteSynchronously);
                    bool prepared;
                    try
                    {
                        prepared = await Task.WhenAny(prepareTask, Task.Delay(TimeSpan.FromSeconds(s.PrepareTimeoutSeconds), ct)) == prepareTask;
                        if (!prepared)
                        {
                            prepareCts.Cancel();
                        }
                    }
                    finally
                    {
                        releaseCts();
                    }
                    ct.ThrowIfCancellationRequested();
                    if (prepared)
                    {
                        prepareMs = preparing.ElapsedMilliseconds;
                        if (run.LastPrepareDeferred)
                        {
                            // 같은 도깨비의 뒤에서 만들기에 맡겨 미리 읽지 않았다 — 바로 끝나지만 준비가 끝난 것은 아니라 미리 읽기 통계에서 뺀다
                            prepareDeferred++;
                            kvNote = "뒤에서 만들기에 맡김";
                        }
                        else
                        {
                            (scene.RelicId == lastRelic ? prepareSame : prepareChanged).Add(prepareMs);
                        }
                    }
                    else
                    {
                        prepareTimedOutMs = preparing.ElapsedMilliseconds;
                        prepareTimeouts++;
                        Say(scene.Id + " 미리 읽기 시간 초과(" + s.PrepareTimeoutSeconds + "초) → 그대로 요청" + (run.SlmStalled ? " (모델이 멈춤)" : ""));
                    }
                    lastRelic = scene.RelicId;
                }

                Stopwatch watch = Stopwatch.StartNew();
                NpcResponse response = await run.RequestAsync(request, ct);
                watch.Stop();

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
                if (isFixed)
                {
                    fixedCount++;
                }
                else if (prefetched)
                {
                    prefetchHits++;
                    repHits++;
                }
                else if (fromSlm)
                {
                    slmCount++;
                    slmTimes.Add(watch.ElapsedMilliseconds);
                }
                else
                {
                    string reason = Reason(response.Note);
                    reasons[reason] = (reasons.ContainsKey(reason) ? reasons[reason] : 0) + 1;
                }
                expectedOk += expected ? 1 : 0;
                repeated += (response.Note ?? "").Contains("Repeated") ? 1 : 0;

                Say(scene.Id + " #" + rep + " (" + total + "/" + all + ") " + source + " " + action + " " + line + "  (" + watch.ElapsedMilliseconds + "ms"
                    + (prepareMs >= 0 ? ", 미리 읽기 " + prepareMs + "ms" : "")
                    + (prepareTimedOutMs >= 0 ? ", 미리 읽기 시간 초과(" + prepareTimedOutMs + "ms 기다림)" : "") + ")"
                    + (fromSlm && response.Reaction.Note == null ? "" : "  ← " + response.Note));
                var row = new StringBuilder();
                row.Append(Csv(scene.Id)).Append(',').Append(rep).Append(',').Append(source).Append(',').Append(action).Append(',')
                    .Append(expected ? "O" : "X").Append(',').Append(Csv(line)).Append(',').Append(line.Length).Append(',')
                    .Append(prepareMs).Append(',').Append(-1).Append(',').Append(watch.ElapsedMilliseconds).Append(',')
                    .Append(-1).Append(',').Append(Csv(response.Note)).Append(',').Append(prefetched ? "O" : "X").Append(',')
                    .Append(Csv(prefetched && response.Reaction != null ? response.Reaction.Note : "")).Append(',').Append(Csv(retargeted)).Append(',')
                    .Append(prepareTimedOutMs >= 0 ? prepareTimedOutMs.ToString(CultureInfo.InvariantCulture) : "").Append(',')
                    .Append(Csv(kvNote)).Append('\n'); // 기억 열: LM Studio 백엔드는 기억(KV 슬롯) 저장·복원을 하지 않아 '뒤에서 만들기에 맡김'만 적는다
                File.AppendAllText(path, row.ToString(), new UTF8Encoding(false));

                if (s.GapSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(s.GapSeconds), ct);
                }
            }
            hitsPerRep.Add(repHits);
        }

        Say("=== 요약 ===");
        Say("SLM 대사 " + slmCount + "/" + (total - fixedCount) + " (이야기·끌려갈 때 " + fixedCount + "건은 정해진 대사), 기대 행동 일치 " + expectedOk + "/" + total);
        Say("미리 만든 대사 적중 " + prefetchHits + "/" + (total - fixedCount) + ", 반복별 " + string.Join("·", hitsPerRep.Select(h => h.ToString(CultureInfo.InvariantCulture)).ToArray()));
        Say("메모에 Repeated 가 적힌 장면 " + repeated + "건");
        if (prepareDeferred > 0)
        {
            Say("미리 읽기 — 같은 도깨비의 뒤에서 만들기에 맡김(미리 읽지 않고 그 생성을 이어 감): " + prepareDeferred + "건 (아래 통계에서 뺌)");
        }
        if (prepareSame.Count > 0)
        {
            Say("미리 읽기 — 앞 장면과 같은 도깨비: 중앙값 " + Percentile(prepareSame, 0.5) + "ms, 최대 " + prepareSame.Max() + "ms (" + prepareSame.Count + "건 — LM Studio 는 같은 도깨비도 1토큰을 다시 불러 0 은 아니다. 수 초면 뒤에서 만들던 대사 뒤에 줄을 선 것)");
        }
        if (prepareChanged.Count > 0)
        {
            Say("미리 읽기 — 도깨비가 바뀜: 중앙값 " + Percentile(prepareChanged, 0.5) + "ms, 최대 " + prepareChanged.Max() + "ms (" + prepareChanged.Count + "건)");
        }
        if (prepareTimeouts > 0)
        {
            Say("미리 읽기 시간 초과 " + prepareTimeouts + "건 (" + s.PrepareTimeoutSeconds + "초)");
        }
        if (run.SlmStalled)
        {
            Say("모델이 멈춰 있다 (취소한 생성이 끝나지 않음)");
        }
        foreach (KeyValuePair<string, int> reason in reasons.OrderByDescending(r => r.Value))
        {
            Say("기본 대사로 대신: " + reason.Key + " " + reason.Value + "건");
        }
        if (slmTimes.Count > 0)
        {
            Say("SLM 전체 시간: 중앙값 " + Percentile(slmTimes, 0.5) + "ms, 90% " + Percentile(slmTimes, 0.9) + "ms, 최대 " + slmTimes.Max() + "ms");
        }
        if (s.Store)
        {
            Say("미리 만든 대사 파일에 씀: " + (run.SaveStore() ? "보관한 칸 있는 대사 " + run.Cache.TotalCount + "개" : "실패"));
        }
        Say("CSV 저장: " + path);
    }

    private static string Onoff(bool on)
    {
        return on ? "켬" : "끔";
    }

    // "SLM 실패 InvalidOutput (TooLong: 79자 …) → 기본 대사" → "InvalidOutput TooLong" (실기기 시험과 같다)
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

    // 실기기 시험과 같다: 시간대는 다른 시간대로(탭 인사에만 있다), 닉네임은 '하늘'(친밀 3단계)
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

    private static void Say(string line)
    {
        string stamped = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + line;
        Console.WriteLine(stamped);
        if (logPath != null)
        {
            try
            {
                File.AppendAllText(logPath, stamped + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
        }
    }
}
