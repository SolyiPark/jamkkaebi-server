using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 모델 런타임(LLMUnity, LiteRT-LM 등)을 감싸는 자리. 런타임마다 하나씩 구현한다 — 이 패키지에는
    /// <see cref="LlmUnityModelBackend"/>(폰, LLMUnity)와 <see cref="LmStudioModelBackend"/>(에디터·개발용, PC 의 LM Studio)가 있다.
    /// 무거운 일은 백그라운드 스레드에서 하고, 결과만 Task 로 돌려준다.
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

        /// <summary>
        /// 시스템 메시지를 모델에 미리 읽혀 둔다 (system + NpcPrompt.PrepareUser 로 1토큰만 생성). llama.cpp 는 앞부분이
        /// 글자 하나까지 같으면 이미 읽은 것을 다시 쓰므로, 다음 요청은 사용자 메시지만 읽는다. 생성 요청과 같은 줄에 선다.
        /// 앞부분을 다시 쓰지 않는 런타임은 아무것도 하지 않아도 된다. 취소되면 OperationCanceledException 을 던진다.
        /// </summary>
        Task PrepareAsync(string system, CancellationToken ct);
    }

    /// <summary>
    /// SLM 으로 반응을 만드는 생성기. 프롬프트 조립 → 모델 호출 → 출력 검사를 하고, 검사를 통과한 것만 Success 로 돌려준다.
    ///
    /// <para>실패하면 예외 대신 Failed 를 돌려준다. 요청 관리자는 Failed 를 받으면 규칙 기반 생성기로 대신한다.</para>
    /// <list type="bullet">
    /// <item>NotReady: 모델이 아직 안 올라왔다.</item>
    /// <item>Unsupported: 페르소나가 아직 없는 도깨비이거나, 정해진 대사를 쓰는 요청(이야기 듣기)이다. 모델을 부르지 않는다.</item>
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
            MaxAttempts = 2;
        }

        /// <summary>
        /// 출력 검사에 걸렸을 때 몇 번까지 생성하는지 (처음 포함). 다시 할 때는 곁들일 거리를 다음 것으로 바꾼다(Variation + 1).
        /// 기본 2. 요청 관리자의 SlmTimeout 안에서만 다시 하고, 넘으면 기본 대사로 대신된다.
        /// </summary>
        public int MaxAttempts { get; set; }

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

        /// <summary>
        /// 그 도깨비의 시스템 메시지를 모델에 미리 읽혀 둔다. 모델이 안 올라왔거나 페르소나가 없으면 아무것도 하지 않는다.
        /// 게임은 요청 관리자의 PrepareAsync 를 부른다 (다른 도깨비로 넘어가면 멈춰 준다).
        /// </summary>
        public Task PrepareAsync(int relicId, CancellationToken ct)
        {
            string system = backend.IsLoaded ? assembler.SystemFor(relicId) : null;
            return system == null ? Task.FromResult(true) : backend.PrepareAsync(system, ct);
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
            if (NpcActions.OnlyFixedLines(request.Candidates))
            {
                return ReactionResult.Failed(id, FailReason.Unsupported, ReactionSource.Slm, "이야기는 정해진 대사를 쓴다");
            }

            // 검사(자기 이름·어미·반복 등)에 걸리면 곁들일 거리를 바꿔 다시 만든다. 걸린 이유는 결과의 Note·FailDetail 에 남긴다
            var rejected = new List<string>();
            int attempts = Math.Max(1, MaxAttempts);
            for (int attempt = 1; ; attempt++)
            {
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

                OutputCheck check;
                try
                {
                    check = validator.Check(raw, request);
                }
                catch (Exception e)
                {
                    return ReactionResult.Failed(id, FailReason.Error, ReactionSource.Slm, "출력 검사 실패: " + e.Message);
                }
                if (check.IsValid || attempt >= attempts)
                {
                    ReactionResult result = check.ToResult(id);
                    return rejected.Count == 0 ? result : result.WithEarlierRejections(rejected);
                }
                rejected.Add(check.Problem + ": " + check.Detail);
                request.Variation++;
            }
        }
    }
}
