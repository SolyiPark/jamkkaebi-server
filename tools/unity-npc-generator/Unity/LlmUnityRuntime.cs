#if UNITY_5_3_OR_NEWER && JAMKKAEBI_LLMUNITY
using System;
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

            var holder = new GameObject("NpcLLM");
            holder.transform.SetParent(host.transform, false);
            holder.SetActive(false); // 설정을 마치기 전에 LLM 이 시작하지 않게 꺼 둔다 (LLMUnity README 의 방법)

            LLM llm = holder.AddComponent<LLM>();
            llm.model = options.Model;
            llm.numGPULayers = options.NumGpuLayers;
            llm.numThreads = options.NumThreads;
            llm.contextSize = options.ContextSize;
            llm.dontDestroyOnLoad = false;

            LLMAgent agent = holder.AddComponent<LLMAgent>();
            agent.llm = llm;

            // 모델 준비 완료 표시(LLM.modelSetupComplete)는 LLM.Awake 안에서만 켜진다.
            // 그래서 LLM 을 켜기 전에 기다리기 시작만 해 두고(진행률 콜백을 Awake 보다 먼저 등록), 켠 뒤에 await 한다.
            // LLM 을 켜기 전에 await 하면 아무도 표시를 켜 주지 않아 영원히 기다린다.
            Action<float> progress = options.DownloadProgress;
            Task<bool> setup = LLM.WaitUntilModelSetup(p =>
            {
                if (progress != null)
                {
                    progress(p);
                }
            });

            bool ready;
            try
            {
                holder.SetActive(true); // Awake: 첫 실행이면 모델을 내려받고(Download on Start), 이어서 모델을 올린다
                ready = await setup;
                if (ready)
                {
                    // 실패하면 failed 를 돌려주지 않고 LLMUnityException 을 던진다 (LLMUnity 3.0.3)
                    await llm.WaitUntilReady();
                    // reasoning 은 모델이 올라온 뒤에 넣는다. LLMUnity 3.0.3 의 setter 는 값을 저장하지 않고
                    // 바로 llmService.EnableReasoning 을 부르는데, 시작 전에는 llmService 가 null 이라 NullReferenceException 이 난다
                    llm.reasoning = !options.DisableThinking;
                }
            }
            catch (Exception e)
            {
                // 예외 종류와 상관없이 모델 이름과 기기 로그 안내를 붙이고, 원래 예외는 InnerException 으로 남긴다
                Object.Destroy(holder);
                throw new InvalidOperationException("LLM 이 모델 '" + options.Model + "' 를 올리지 못했다. "
                        + "메모리가 부족하거나, 이 LLMUnity 의 llama.cpp 가 모델 구조를 모를 수 있다 (예: gemma4). 기기 로그를 본다", e);
            }

            if (!ready)
            {
                Object.Destroy(holder);
                throw new InvalidOperationException("모델 파일 '" + options.Model + "' 을 준비하지 못했다 "
                        + "(내려받기 실패 · 저장 공간 부족 · 모델 관리자에 등록 안 됨)");
            }
            return new Session(holder, agent);
        }

        private sealed class Session : ILlmUnitySession
        {
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
                agent.seed = options.Seed.HasValue ? options.Seed.Value : 0; // 0 = 무작위
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

            public void CancelRequests()
            {
                if (agent != null)
                {
                    agent.CancelRequests();
                }
            }

            public void Destroy()
            {
                if (holder != null)
                {
                    Object.Destroy(holder); // LLM 의 OnDestroy 가 모델 서비스를 멈추고 정리한다
                }
            }
        }
    }
}
#endif
