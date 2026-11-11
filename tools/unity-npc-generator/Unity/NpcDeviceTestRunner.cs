#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// CSV 는 Application.persistentDataPath 에 생긴다 (경로는 화면과 로그에 나온다).</para>
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
            try
            {
                await RunAll(stopping.Token);
            }
            catch (OperationCanceledException)
            {
                Say("중단됨");
            }
            catch (Exception e)
            {
                status = "오류";
                Say("오류: " + e);
            }
            finally
            {
                running = false;
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

            manager = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromSeconds(slmTimeoutSeconds) };
            var csv = new StringBuilder("장면,반복,출처,행동,기대행동일치,대사,글자수,미리읽기ms,첫응답ms,전체ms,메모리MB,메모\n");
            var slmTimes = new List<long>();
            var firstTokens = new List<long>();
            var prepareTimes = new List<long>();
            var reasons = new Dictionary<string, int>();
            int total = 0;
            int fixedCount = 0;
            int slmCount = 0;
            int trimmed = 0;
            int speechFixed = 0;
            int expectedOk = 0;
            long peak = 0;

            for (int rep = 1; rep <= Math.Max(1, repeat); rep++)
            {
                foreach (NpcTestScene scene in sceneList)
                {
                    ct.ThrowIfCancellationRequested();
                    total++;
                    status = scene.Id + " #" + rep + " (" + total + "/" + sceneList.Count * Math.Max(1, repeat) + ")";
                    ReactionRequest request = scene.CreateRequest();
                    // 게임처럼 반복마다 곁들일 거리를 바꾼다 (run.py 와 같게 1번째 반복 = 0번째)
                    request.Variation = rep - 1;

                    // 게임에서 탭해 확대할 때처럼 시스템 프롬프트를 먼저 읽힌다. 같은 도깨비가 이어지면 바로 끝난다
                    long prepareMs = -1;
                    if (prepareSystemPrompt && slm != null && slm.IsReady && !NpcActions.OnlyFixedLines(request.Candidates))
                    {
                        Stopwatch preparing = Stopwatch.StartNew();
                        await manager.PrepareAsync(scene.RelicId, ct);
                        prepareMs = preparing.ElapsedMilliseconds;
                        prepareTimes.Add(prepareMs);
                    }

                    Stopwatch watch = Stopwatch.StartNew();
                    NpcResponse response = await manager.RequestAsync(request, ct);
                    watch.Stop();

                    long memory = MemoryMb();
                    peak = Math.Max(peak, memory);
                    bool isFixed = response.Note == NpcRequestManager.FixedLinesNote;
                    bool fromSlm = response.ShouldShow && !response.UsedFallback;
                    string source = !response.ShouldShow ? "없음" : (fromSlm ? "SLM" : (isFixed ? "정해진" : "기본"));
                    string action = response.ShouldShow ? response.Reaction.ActionId : "";
                    string line = response.ShouldShow ? response.Reaction.Line : "";
                    bool expected = scene.Expected.Contains(action);
                    long firstToken = fromSlm ? FirstTokenMs() : -1;
                    if (isFixed)
                    {
                        fixedCount++;
                    }
                    else if (fromSlm)
                    {
                        slmCount++;
                        // Note 에는 문장을 잘라 낸 것과 어미를 고친 것이 함께 적힌다 (OutputCheck.ToResult)
                        string reactionNote = response.Reaction.Note ?? "";
                        trimmed += reactionNote.Contains("잘라") || reactionNote.Contains("끊긴") ? 1 : 0;
                        speechFixed += reactionNote.Contains("어미를 말투에 맞게 고침") ? 1 : 0;
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
                        + (firstToken >= 0 ? ", 첫 응답 " + firstToken + "ms" : "") + ")"
                        + (fromSlm && response.Reaction.Note == null ? "" : "  ← " + response.Note));
                    csv.Append(Csv(scene.Id)).Append(',').Append(rep).Append(',').Append(source).Append(',').Append(action).Append(',')
                        .Append(expected ? "O" : "X").Append(',').Append(Csv(line)).Append(',').Append(line.Length).Append(',')
                        .Append(prepareMs).Append(',').Append(firstToken).Append(',').Append(watch.ElapsedMilliseconds).Append(',')
                        .Append(memory).Append(',').Append(Csv(response.Note)).Append('\n');
                }
            }

            Say("=== 요약 ===");
            Say("SLM 대사 " + slmCount + "/" + (total - fixedCount) + " (이야기 " + fixedCount + "건은 정해진 대사, 문장 잘라 씀 " + trimmed + "건"
                + ", 어미 고침 " + speechFixed + "건)"
                + ", 기대 행동 일치 " + expectedOk + "/" + total);
            if (prepareTimes.Count > 0)
            {
                Say("시스템 프롬프트 미리 읽기: 중앙값 " + Percentile(prepareTimes, 0.5) + "ms, 최대 " + prepareTimes.Max() + "ms"
                    + " (같은 도깨비가 이어지면 0 에 가깝다)");
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
            Say("메모리 최대 " + peak + "MB" + MemoryNote());

            string path = Path.Combine(Application.persistentDataPath,
                    "npc_device_test_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true)); // BOM: 엑셀에서 한글이 안 깨진다
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
