using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 모델 런타임(LLMUnity, LiteRT-LM 등)을 감싸는 자리. 런타임마다 하나씩 구현한다 — 이 패키지에는 구현이 없다
    /// ("모델 런타임 연결" 작업). 무거운 일은 백그라운드 스레드에서 하고, 결과만 Task 로 돌려준다.
    /// </summary>
    public interface INpcModelBackend
    {
        /// <summary>모델이 메모리에 올라와 바로 생성할 수 있는지.</summary>
        bool IsLoaded { get; }

        Task LoadAsync(CancellationToken ct);

        void Unload();

        /// <summary>
        /// 프롬프트로 한 번 생성해 모델이 낸 글을 <b>그대로</b> 돌려준다 (검사는 생성기가 한다).
        /// 런타임이 형식 제약을 받으면 prompt.Grammar(GBNF) 나 prompt.JsonSchema 를 넘긴다.
        /// 취소되면 OperationCanceledException 을 던진다.
        /// </summary>
        Task<string> CompleteAsync(NpcPrompt prompt, CancellationToken ct);
    }

    /// <summary>
    /// SLM 으로 반응을 만드는 생성기. 프롬프트 조립 → 모델 호출 → 출력 검사를 하고, 검사를 통과한 것만 Success 로 돌려준다.
    ///
    /// <para>실패하면 예외 대신 Failed 를 돌려준다. 요청 관리자는 Failed 를 받으면 규칙 기반 생성기로 대신한다.</para>
    /// <list type="bullet">
    /// <item>NotReady: 모델이 아직 안 올라왔다.</item>
    /// <item>Unsupported: 페르소나가 아직 없는 도깨비다.</item>
    /// <item>NoCandidate: 후보가 없다.</item>
    /// <item>InvalidOutput: 출력 검사(JSON · 후보 포함 · 대사 길이 · 줄바꿈 · 금칙어)에 걸렸다. FailDetail 에 이유가 있다.</item>
    /// <item>Cancelled: 요청 관리자가 취소했다 (시간 초과 포함). 늦게 끝난 생성 결과도 버린다.</item>
    /// <item>Error: 런타임 오류. FailDetail 에 예외 메시지가 있다.</item>
    /// </list>
    /// </summary>
    public sealed class SlmReactionGenerator : INpcReactionGenerator
    {
        private readonly INpcModelBackend backend;
        private readonly PromptAssembler assembler;
        private readonly NpcOutputValidator validator;

        public SlmReactionGenerator(INpcModelBackend backend, PromptAssembler assembler, NpcOutputValidator validator)
        {
            if (backend == null)
            {
                throw new ArgumentNullException("backend");
            }
            if (assembler == null)
            {
                throw new ArgumentNullException("assembler");
            }
            if (validator == null)
            {
                throw new ArgumentNullException("validator");
            }
            this.backend = backend;
            this.assembler = assembler;
            this.validator = validator;
        }

        public bool IsReady
        {
            get { return backend.IsLoaded; }
        }

        public Task LoadAsync(CancellationToken ct)
        {
            return backend.LoadAsync(ct);
        }

        public void Unload()
        {
            backend.Unload();
        }

        public async Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return ReactionResult.Failed(null, FailReason.Error, ReactionSource.Slm, "요청이 없다");
            }
            string id = request.RequestId;
            if (ct.IsCancellationRequested)
            {
                return ReactionResult.Failed(id, FailReason.Cancelled, ReactionSource.Slm);
            }
            if (!backend.IsLoaded)
            {
                return ReactionResult.Failed(id, FailReason.NotReady, ReactionSource.Slm);
            }
            if (request.Candidates == null || request.Candidates.Count == 0)
            {
                return ReactionResult.Failed(id, FailReason.NoCandidate, ReactionSource.Slm);
            }

            NpcPrompt prompt;
            try
            {
                prompt = assembler.Build(request);
            }
            catch (Exception e)
            {
                return ReactionResult.Failed(id, FailReason.Error, ReactionSource.Slm, "프롬프트 조립 실패: " + e.Message);
            }
            if (prompt == null)
            {
                return ReactionResult.Failed(id, FailReason.Unsupported, ReactionSource.Slm, "페르소나가 없는 도깨비 " + request.RelicId);
            }

            string raw;
            try
            {
                raw = await backend.CompleteAsync(prompt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ReactionResult.Failed(id, FailReason.Cancelled, ReactionSource.Slm);
            }
            catch (Exception e)
            {
                return ReactionResult.Failed(id, FailReason.Error, ReactionSource.Slm, "모델 호출 실패: " + e.Message);
            }
            if (ct.IsCancellationRequested)
            {
                return ReactionResult.Failed(id, FailReason.Cancelled, ReactionSource.Slm);
            }

            try
            {
                return validator.Check(raw, request).ToResult(id);
            }
            catch (Exception e)
            {
                return ReactionResult.Failed(id, FailReason.Error, ReactionSource.Slm, "출력 검사 실패: " + e.Message);
            }
        }
    }
}
