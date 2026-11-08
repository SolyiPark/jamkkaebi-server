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

            // 첫 실행이면 여기서 모델을 내려받는다 (Download on Build). 이미 있으면 바로 끝난다
            Action<float> progress = options.DownloadProgress;
            bool ready = await LLM.WaitUntilModelSetup(p =>
            {
                if (progress != null)
                {
                    progress(p);
                }
            });
            if (!ready)
            {
                throw new InvalidOperationException("모델 파일을 준비하지 못했다 (내려받기 실패 · 저장 공간 부족 · 모델 관리자에 등록 안 됨)");
            }

            var holder = new GameObject("NpcLLM");
            holder.transform.SetParent(host.transform, false);
            holder.SetActive(false); // 설정을 마치기 전에 LLM 이 시작하지 않게 꺼 둔다 (LLMUnity README 의 방법)

            LLM llm = holder.AddComponent<LLM>();
            llm.model = options.Model;
            llm.numGPULayers = options.NumGpuLayers;
            llm.numThreads = options.NumThreads;
            llm.contextSize = options.ContextSize;
            llm.reasoning = !options.DisableThinking;
            llm.dontDestroyOnLoad = false;

            LLMAgent agent = holder.AddComponent<LLMAgent>();
            agent.llm = llm;

            holder.SetActive(true);
            await llm.WaitUntilReady();
            if (llm.failed)
            {
                Object.Destroy(holder);
                throw new InvalidOperationException("LLM 이 모델 '" + options.Model + "' 를 올리지 못했다. "
                        + "메모리가 부족하거나, 이 LLMUnity 의 llama.cpp 가 모델 구조를 모를 수 있다 (예: gemma4). 기기 로그를 본다");
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
