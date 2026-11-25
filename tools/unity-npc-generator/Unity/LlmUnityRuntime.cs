#if UNITY_5_3_OR_NEWER && JAMKKAEBI_LLMUNITY
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// LLMUnity(LLM + LLMAgent)로 모델을 올리는 ILlmUnityRuntime. LlmUnityModelBackend 에 넘겨 쓴다.
    ///
    /// <para>LLMUnity 패키지가 있어야 컴파일된다. Player Settings 의 Scripting Define Symbols 에 JAMKKAEBI_LLMUNITY 를 넣는다.
    /// LLMUnity v3.0.3 문서의 API 로 썼다 (LLM.model · numGPULayers · numThreads · contextSize · reasoning · failed ·
    /// WaitUntilReady · WaitUntilModelSetup, LLMAgent.llm · systemPrompt · grammar · temperature · numPredict · seed ·
    /// ClearHistory · Chat · CancelRequests). 버전이 바뀌어 컴파일이 안 되면 이 파일만 고친다.</para>
    ///
    /// <para>모델 파일은 에디터에서 LLM 컴포넌트의 모델 관리자로 한 번 등록해 둔다. 3GB 안팎이라 빌드에 넣지 말고
    /// "Download on Build"(첫 실행 때 내려받기)를 켠다.</para>
    /// </summary>
    public sealed class LlmUnityRuntime : ILlmUnityRuntime
    {
        private readonly GameObject host;

        /// <param name="host">LLM 을 붙일 게임 오브젝트의 부모. 장면이 바뀌어도 남겨야 하면 DontDestroyOnLoad 된 것을 준다.</param>
        public LlmUnityRuntime(GameObject host)
        {
            if (host == null)
            {
                throw new ArgumentNullException("host");
            }
            this.host = host;
        }

        public async Task<ILlmUnitySession> StartAsync(LlmUnityOptions options)
        {
            if (string.IsNullOrEmpty(options.Model))
            {
                throw new InvalidOperationException("LlmUnityOptions.Model 에 GGUF 파일 이름을 적는다");
            }

            // 진행률 콜백은 내려받기(LLMManager.Setup)가 시작되기 전에 직접 등록하고, 끝나면 finally 에서 뗀다.
            // LLM.WaitUntilModelSetup(콜백) 으로 등록하면 대기도 함께 시작되는데, LLM.Awake 까지 가지 못하고 실패하면
            // 그 대기가 끝나지 않고 콜백도 정적 목록에 남는다
            Action<float> progress = options.DownloadProgress;
            if (progress != null)
            {
                LLMManager.downloadProgressCallbacks.Add(progress);
            }

            GameObject holder = null;
            LLMAgent agent = null;
            bool ready;
            try
            {
                // 기기에서는 모델 목록(LLMManager.json)을 읽고 모델 파일을 꺼내거나 내려받는 일(LLMManager.Setup)을
                // LLM 을 만들기 전에 먼저 끝낸다. LLMUnity 는 이 일을 LLM.Awake 에서 하는데, 그보다 앞서 llm.model 을 넣으면
                // 아직 목록도 파일도 없어서 GGUF 를 "/파일이름" 경로로 읽으려다 FileNotFoundException 이 난다.
                // Setup 은 한 번만 실행되고 결과를 기억하므로, 뒤에서 Awake 가 다시 불러도 바로 끝난다.
                // 에디터에서는 LLMUnity 도 Setup 을 건너뛰고 모델 관리자의 파일을 바로 쓴다.
#if UNITY_EDITOR
                ready = true;
#else
                ready = await LLMManager.Setup();
#endif
                if (ready)
                {
                    holder = new GameObject("NpcLLM");
                    holder.transform.SetParent(host.transform, false);
                    holder.SetActive(false); // 설정을 마치기 전에 LLM 이 시작하지 않게 꺼 둔다 (LLMUnity README 의 방법)

                    LLM llm = holder.AddComponent<LLM>();
                    llm.model = options.Model;
                    llm.numGPULayers = options.NumGpuLayers;
                    llm.numThreads = options.NumThreads;
                    llm.contextSize = options.ContextSize;
                    llm.dontDestroyOnLoad = false;

                    agent = holder.AddComponent<LLMAgent>();
                    agent.llm = llm;

                    // 모델 준비 완료 표시(LLM.modelSetupComplete)는 LLM.Awake 안에서만 켜진다. 그래서 켠 뒤에만 기다린다
                    holder.SetActive(true);
                    ready = await LLM.WaitUntilModelSetup();
                    if (ready)
                    {
                        // 실패하면 failed 를 돌려주지 않고 LLMUnityException 을 던진다 (LLMUnity 3.0.3)
                        await llm.WaitUntilReady();
                        // reasoning 은 모델이 올라온 뒤에 넣는다. LLMUnity 3.0.3 의 setter 는 값을 저장하지 않고
                        // 바로 llmService.EnableReasoning 을 부르는데, 시작 전에는 llmService 가 null 이라 NullReferenceException 이 난다
                        llm.reasoning = !options.DisableThinking;
                    }
                }
            }
            catch (Exception e)
            {
                // 예외 종류와 상관없이 모델 이름과 기기 로그 안내를 붙이고, 원래 예외는 InnerException 으로 남긴다
                if (holder != null)
                {
                    Object.Destroy(holder);
                }
                throw new InvalidOperationException("LLM 이 모델 '" + options.Model + "' 를 올리지 못했다. "
                        + "메모리가 부족하거나, 이 LLMUnity 의 llama.cpp 가 모델 구조를 모를 수 있다 (예: gemma4). 기기 로그를 본다", e);
            }
            finally
            {
                if (progress != null)
                {
                    LLMManager.downloadProgressCallbacks.Remove(progress);
                }
            }

            if (!ready)
            {
                if (holder != null)
                {
                    Object.Destroy(holder);
                }
                throw new InvalidOperationException("모델 파일 '" + options.Model + "' 을 준비하지 못했다 "
                        + "(내려받기 실패 · 저장 공간 부족 · 모델 관리자에 등록 안 됨)");
            }
            return new Session(holder, agent);
        }

        private sealed class Session : ILlmUnitySession, ILlmUnityStateStore
        {
            // DestroyAsync 가 오브젝트가 지워지기를 기다리는 최대 시간. 보통 다음 프레임에 지워진다 (모델 서비스를 지우는 데 1초 안팎)
            private const float DestroyWaitSeconds = 10f;

            private readonly GameObject holder;
            private readonly LLMAgent agent;

            public Session(GameObject holder, LLMAgent agent)
            {
                this.holder = holder;
                this.agent = agent;
            }

            public async Task<string> ChatOnceAsync(string system, string user, string grammar, int maxTokens, LlmUnityOptions options,
                                                    Action onFirstToken)
            {
                agent.systemPrompt = system;
                agent.grammar = string.IsNullOrEmpty(grammar) ? "" : grammar; // 빈 문자열 = 형식 제약 없음
                agent.temperature = options.Temperature;
                agent.numPredict = maxTokens;
                agent.seed = options.SeedForCall(); // 생성마다 새 seed — 0 은 '무작위'가 아니라 늘 같은 seed 라 같은 프롬프트에 같은 대사가 나온다
                // 대화 기록을 남기지 않는다. 지난 요청의 대사가 다음 프롬프트에 섞이면 안 된다 (기억은 프롬프트의 요약으로 준다)
                await agent.ClearHistory();
                bool first = true;
                Action<string> partial = text =>
                {
                    // 생성 중 글이 들어올 때마다 불린다. 첫 번째만 첫 응답 시간으로 잰다
                    if (first && !string.IsNullOrEmpty(text) && onFirstToken != null)
                    {
                        first = false;
                        onFirstToken();
                    }
                };
                return await agent.Chat(user, partial, null, false);
            }

            // ---------- 시스템 메시지 기억(KV 캐시) 저장·복원 ----------
            // 모델의 기억 칸(llama.cpp 슬롯)은 하나라 마지막에 읽은 도깨비의 시스템 메시지만 남는다 — 도깨비가 A→B→A 로만 바뀌어도 A 를 처음부터 다시 읽는다
            // (2026-11-24 기기 시험: 바뀔 때마다 15~20초). 슬롯을 늘리면 슬롯마다 문맥 2048토큰 ≈ 256MB 가 더 들어(토큰당 KV 약 128KB: 32층 × KV 헤드 8 × 128 × K·V × 2바이트)
            // 앱이 이미 1.7~2.1GB 를 쓰는 폰에 부담이라, 시스템 메시지를 읽은 슬롯을 파일로 남겼다가 다음에 그 도깨비를 미리 읽을 때 되살린다 (메모리는 그대로, 파일은 프롬프트 토큰 수 × 128KB).
            // LLMUnity 3.0.3 의 바깥 LLMAgent 에는 Save·Load 가 없지만(SaveHistory·LoadHistory 는 대화 기록만), 안쪽 LlamaLib 에이전트(agent.llmAgent)의
            // SaveSlot(경로)·LoadSlot(경로) 가 그 에이전트의 슬롯으로 llama.cpp 의 슬롯 저장·복원을 부른다 (LlamaLib 은 경로를 그대로 쓰고, 실패하면 예외를 던진다).
            // 파일은 persistentDataPath/npc_kv/ 에 둔다. 이름에 앱 버전을 붙여(내장 llama.cpp 가 바뀌었을 수 있다) 옛 기억은 쓰지 않고, 오래 안 쓴 파일부터 지워 MaxStateFiles 개만 둔다.
            // 네이티브 호출은 끝날 때까지 막으므로 LLMUnity 의 ChatAsync 처럼 작업 스레드에서 부른다 (화면이 멈추지 않게)

            /// <summary>남겨 둘 기억 파일 수. 도깨비 다섯 + 프롬프트가 바뀐 직후의 옛 파일 몇 개. 넘으면 오래 안 쓴 것부터 지운다.</summary>
            private const int MaxStateFiles = 8;

            private static string StateFolder
            {
                get { return Path.Combine(Application.persistentDataPath, "npc_kv"); }
            }

            private static string StatePath(string key)
            {
                return Path.Combine(StateFolder, key + "-" + new string(Application.version.Where(char.IsLetterOrDigit).ToArray()) + ".bin");
            }

            public async Task<bool> SaveStateAsync(string key)
            {
                UndreamAI.LlamaLib.LLMAgent inner = agent != null ? agent.llmAgent : null;
                if (inner == null || string.IsNullOrEmpty(key))
                {
                    return false;
                }
                string path = StatePath(key);
                string temp = path + ".tmp";
                try
                {
                    Directory.CreateDirectory(StateFolder);
                    // 임시 파일에 쓴 뒤 옮긴다 — 쓰는 도중에 앱이 꺼지면 반쪽 파일이 남아 다음에 되살리다 실패한다
                    string written = await Task.Run(() => inner.SaveSlot(temp));
                    if (string.IsNullOrEmpty(written) || !File.Exists(temp) || new FileInfo(temp).Length == 0)
                    {
                        TryDelete(temp);
                        return false;
                    }
                    TryDelete(path);
                    File.Move(temp, path);
                    PruneStates(path);
                    return true;
                }
                catch (Exception)
                {
                    TryDelete(temp);
                    return false;
                }
            }

            public async Task<bool> LoadStateAsync(string key)
            {
                UndreamAI.LlamaLib.LLMAgent inner = agent != null ? agent.llmAgent : null;
                if (inner == null || string.IsNullOrEmpty(key))
                {
                    return false;
                }
                string path = StatePath(key);
                if (!File.Exists(path))
                {
                    return false;
                }
                string restored;
                try
                {
                    restored = await Task.Run(() => inner.LoadSlot(path));
                }
                catch (Exception)
                {
                    TryDelete(path); // 깨졌거나 모델이 달라 못 읽는 파일은 지워 다음 미리 읽기가 새로 만들게 한다
                    return false;
                }
                if (string.IsNullOrEmpty(restored))
                {
                    TryDelete(path);
                    return false;
                }
                // 오래 안 쓴 파일부터 지울 때 쓴 때로 본다. 되살리기는 이미 됐으니 여기서 실패해도(읽기 전용 저장소 등) 파일을 지우거나 실패로 돌려주지 않는다
                try
                {
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                }
                catch (Exception)
                {
                }
                return true;
            }

            // 오래 안 쓴 기억 파일부터 지워 MaxStateFiles 개만 남긴다 (프롬프트·모델·앱 버전이 바뀌면 옛 파일은 다시 쓰이지 않는다). 방금 쓴 파일은 남긴다
            private static void PruneStates(string keep)
            {
                try
                {
                    // 쓰는 도중에 앱이 꺼져 남은 임시 파일(파일 하나가 수십 MB)도 지운다. 모델 안의 일은 한 번에 하나라, 방금 쓴 것은 이미 옮겼고 지금 쓰는 것은 없다
                    foreach (FileInfo stale in new DirectoryInfo(StateFolder).GetFiles("npc-kv-*.tmp"))
                    {
                        TryDelete(stale.FullName);
                    }
                    FileInfo[] files = new DirectoryInfo(StateFolder).GetFiles("npc-kv-*.bin");
                    if (files.Length <= MaxStateFiles)
                    {
                        return;
                    }
                    foreach (FileInfo old in files.Where(f => f.FullName != Path.GetFullPath(keep))
                                 .OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxStateFiles))
                    {
                        TryDelete(old.FullName);
                    }
                }
                catch (Exception)
                {
                }
            }

            private static void TryDelete(string path)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception)
                {
                }
            }

            public void CancelRequests()
            {
                if (agent != null)
                {
                    agent.CancelRequests();
                }
            }

            public async Task DestroyAsync()
            {
                if (holder == null)
                {
                    return;
                }
                Object.Destroy(holder); // LLM 의 OnDestroy 가 모델 서비스를 멈추고 정리한다
                // Object.Destroy 는 이 프레임 끝에 지운다. 그 전에 끝내면 다음 StartAsync 가 같은 프레임에 새 LLM 을 만들어
                // 앞 모델이 메모리에 남은 채로 하나를 더 올리고, 앞 모델의 OnDestroy 가 새 모델을 올리는 동안 LLMUnity 의 잠금을 기다리며
                // 메인 스레드를 멈춘다. 지워질 때까지(Unity 의 == null) 프레임을 넘기며 기다린다.
                // 끝없이 기다리지는 않는다 — 안 지워지면 실패로 알려, 백엔드가 다시 올리지 않고 '지우지 못했다'고 알리게 한다
                float giveUpAt = Time.realtimeSinceStartup + DestroyWaitSeconds;
                while (holder != null)
                {
                    if (Time.realtimeSinceStartup > giveUpAt)
                    {
                        throw new InvalidOperationException("LLM 오브젝트가 " + DestroyWaitSeconds + "초 안에 지워지지 않았다");
                    }
                    await Task.Yield();
                }
            }
        }
    }
}
#endif
