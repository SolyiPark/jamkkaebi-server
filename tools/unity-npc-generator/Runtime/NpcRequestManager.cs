using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>요청 관리자가 돌려주는 것. 띄울 반응이 있는지와, 왜 그렇게 됐는지.</summary>
    public sealed class NpcResponse
    {
        private NpcResponse()
        {
        }

        /// <summary>대사를 띄울지. false 면 아무것도 띄우지 않는다 (화면 종료·NPC 전환·새 요청으로 대체·후보 없음).</summary>
        public bool ShouldShow { get; private set; }

        /// <summary>띄울 반응. ShouldShow 일 때만 있다. Source 로 SLM 이 만든 것인지 기본 대사인지 안다.</summary>
        public ReactionResult Reaction { get; private set; }

        /// <summary>SLM 을 불렀을 때 그 결과 (실패 이유 기록용). 부르지 않았으면 null.</summary>
        public ReactionResult SlmAttempt { get; private set; }

        /// <summary>어떻게 처리됐는지 사람이 읽는 설명. 로그와 평가용이다. 예: "SLM 시간 초과(3000ms) → 기본 대사".</summary>
        public string Note { get; private set; }

        /// <summary>SLM 대신 기본 대사(규칙 기반)로 응답했는지.</summary>
        public bool UsedFallback
        {
            get { return ShouldShow && Reaction.Source == ReactionSource.Rule; }
        }

        internal static NpcResponse Show(ReactionResult reaction, ReactionResult slmAttempt, string note)
        {
            return new NpcResponse { ShouldShow = true, Reaction = reaction, SlmAttempt = slmAttempt, Note = note };
        }

        internal static NpcResponse Discard(ReactionResult slmAttempt, string note)
        {
            return new NpcResponse { ShouldShow = false, SlmAttempt = slmAttempt, Note = note };
        }
    }

    /// <summary>
    /// 요청 관리자. 반응 요청 한 건을 받아 SLM 으로 만들어 보고, 안 되면 규칙 기반 생성기의 기본 대사로 대신한다.
    ///
    /// <para><b>기본 대사로 대신하는 경우</b>: SLM 을 끈 기기(저사양), 모델을 아직 안 받았거나 안 올라옴, 시간 초과,
    /// 출력 검사 실패, 페르소나 없는 도깨비, 런타임 오류. 앞의 둘은 SLM 을 부르지도 않는다.</para>
    ///
    /// <para><b>아무것도 띄우지 않는 경우</b>: 부르는 쪽이 취소함(화면 종료), Cancel(relicId)(NPC 전환), CancelAll,
    /// 같은 도깨비에게 새 요청이 와서 옛 요청이 밀림, 후보가 없음. 늦게 끝난 옛 요청의 결과는 버린다.</para>
    ///
    /// <para>도깨비(relicId)마다 진행 중인 요청을 하나만 둔다. 손님 관람처럼 여러 도깨비가 동시에 말풍선을 띄우는 것은 된다.
    /// RequestId 는 시도마다 여기서 새로 붙인다. Unity 메인 스레드에서 await 하면 결과도 메인 스레드로 돌아온다.</para>
    /// </summary>
    public sealed class NpcRequestManager
    {
        private readonly INpcReactionGenerator slm;
        private readonly INpcReactionGenerator rule;
        private readonly object gate = new object();
        private readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();

        /// <param name="slm">SLM 생성기. 모델을 쓰지 않는 빌드면 null.</param>
        /// <param name="rule">규칙 기반 생성기. 반드시 있어야 한다.</param>
        public NpcRequestManager(INpcReactionGenerator slm, INpcReactionGenerator rule)
        {
            if (rule == null)
            {
                throw new ArgumentNullException("rule");
            }
            this.slm = slm;
            this.rule = rule;
            SlmEnabled = slm != null;
            SlmTimeout = TimeSpan.FromSeconds(3);
        }

        /// <summary>
        /// SLM 을 쓸지. 실기기 측정으로 정한 사양 기준에 못 미치는 기기면 false 로 두어 처음부터 기본 대사만 쓴다.
        /// </summary>
        public bool SlmEnabled { get; set; }

        /// <summary>
        /// SLM 을 기다리는 시간. 넘으면 기본 대사로 대신한다. 기본 3초이고, 실기기의 첫 응답·전체 생성 시간을 재서 정한다.
        /// 모델 런타임이 취소에 늦게 반응해도 이 시간이 지나면 기다리지 않는다.
        /// </summary>
        public TimeSpan SlmTimeout { get; set; }

        /// <summary>
        /// 반응을 만든다. 예외를 던지지 않고(요청이 null 일 때만 ArgumentNullException) 늘 NpcResponse 를 돌려준다.
        /// ct 는 부르는 쪽의 취소(화면 종료 등)다 — 취소되면 기본 대사로 대신하지 않고 버린다.
        /// </summary>
        public Task<NpcResponse> RequestAsync(ReactionRequest request, CancellationToken ct = default(CancellationToken))
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            return Handle(request, ct);
        }

        private async Task<NpcResponse> Handle(ReactionRequest request, CancellationToken ct)
        {
            if (request.Candidates == null || request.Candidates.Count == 0)
            {
                return NpcResponse.Discard(null, "후보가 없어 띄울 반응이 없다");
            }
            if (string.IsNullOrEmpty(request.InteractionId))
            {
                request.InteractionId = NewId();
            }

            Session session = Begin(request.RelicId, ct);
            try
            {
                if (session.Token.IsCancellationRequested)
                {
                    return NpcResponse.Discard(null, session.CancelNote());
                }

                // 1) SLM
                ReactionResult slmAttempt = null;
                string fallbackNote;
                if (!SlmEnabled || slm == null)
                {
                    fallbackNote = "SLM 꺼짐(기기 사양 또는 모델 없는 빌드) → 기본 대사";
                }
                else if (!slm.IsReady)
                {
                    fallbackNote = "모델이 아직 준비되지 않음 → 기본 대사";
                }
                else
                {
                    slmAttempt = await RunSlm(request, session).ConfigureAwait(false);
                    if (session.Token.IsCancellationRequested)
                    {
                        return NpcResponse.Discard(slmAttempt, session.CancelNote());
                    }
                    if (!session.TimedOut && slmAttempt.IsSuccess && slmAttempt.RequestId == request.RequestId)
                    {
                        return NpcResponse.Show(slmAttempt, slmAttempt, "SLM");
                    }
                    fallbackNote = session.TimedOut
                            ? "SLM 시간 초과(" + (int)SlmTimeout.TotalMilliseconds + "ms) → 기본 대사"
                            : "SLM 실패 " + slmAttempt.FailReason
                              + (slmAttempt.FailDetail != null ? " (" + slmAttempt.FailDetail + ")" : "") + " → 기본 대사";
                }

                // 2) 기본 대사
                request.RequestId = NewId();
                ReactionResult fallback;
                try
                {
                    fallback = await rule.GenerateAsync(request, session.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    fallback = ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Rule, e.Message);
                }
                if (session.Token.IsCancellationRequested)
                {
                    return NpcResponse.Discard(slmAttempt, session.CancelNote());
                }
                if (fallback.IsSuccess && fallback.RequestId == request.RequestId)
                {
                    return NpcResponse.Show(fallback, slmAttempt, fallbackNote);
                }
                return NpcResponse.Discard(slmAttempt, "기본 대사도 실패 " + fallback.FailReason + " (" + fallbackNote + ")");
            }
            finally
            {
                End(request.RelicId, session);
            }
        }

        /// <summary>그 도깨비의 진행 중인 요청을 취소한다 (NPC 전환). 결과는 버린다.</summary>
        public void Cancel(int relicId)
        {
            Session session;
            lock (gate)
            {
                sessions.TryGetValue(relicId, out session);
            }
            if (session != null)
            {
                session.Cancel("다른 NPC 로 전환해 취소");
            }
        }

        /// <summary>모든 진행 중인 요청을 취소한다 (화면 종료, 정화 미니게임 진입). 결과는 버린다.</summary>
        public void CancelAll()
        {
            List<Session> running;
            lock (gate)
            {
                running = new List<Session>(sessions.Values);
            }
            foreach (Session session in running)
            {
                session.Cancel("모든 요청 취소(화면 종료)");
            }
        }

        // 시간 초과면 session.TimedOut 을 세운다 (C# 5 의 async 메서드는 out 매개변수를 못 쓴다)
        private async Task<ReactionResult> RunSlm(ReactionRequest request, Session session)
        {
            request.RequestId = NewId();
            using (var slmCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
            using (var delayCts = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
            {
                Task<ReactionResult> generating;
                try
                {
                    generating = slm.GenerateAsync(request, slmCts.Token);
                }
                catch (Exception e)
                {
                    return ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm, e.Message);
                }
                Task waiting = Task.Delay(SlmTimeout, delayCts.Token);
                Task first = await Task.WhenAny(generating, waiting).ConfigureAwait(false);
                if (first == generating)
                {
                    delayCts.Cancel();
                    try
                    {
                        return generating.Result;
                    }
                    catch (Exception e)
                    {
                        Exception inner = e is AggregateException ? ((AggregateException)e).GetBaseException() : e;
                        return ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm, inner.Message);
                    }
                }
                // 시간 초과 또는 취소. 모델에 취소를 알리고, 끝나기를 기다리지 않는다 (늦게 온 결과는 버려진다)
                session.TimedOut = !session.Token.IsCancellationRequested;
                slmCts.Cancel();
                Observe(generating);
                return ReactionResult.Failed(request.RequestId, FailReason.Cancelled, ReactionSource.Slm,
                        session.TimedOut ? "시간 초과" : "취소");
            }
        }

        // 기다리지 않고 버린 작업이 나중에 예외로 끝나도 처리되지 않은 예외로 남지 않게 한다
        private static void Observe(Task task)
        {
            task.ContinueWith(t => t.Exception.Handle(e => true),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        // 취소는 잠금 밖에서 한다. 취소하는 순간 그 요청의 남은 처리가 같은 스레드에서 이어 돌며 End 로 목록을 고치기 때문이다
        private Session Begin(int relicId, CancellationToken ct)
        {
            var session = new Session(ct);
            Session previous;
            lock (gate)
            {
                sessions.TryGetValue(relicId, out previous);
                sessions[relicId] = session;
            }
            if (previous != null)
            {
                previous.Cancel("같은 도깨비의 새 요청으로 대체");
            }
            return session;
        }

        private void End(int relicId, Session session)
        {
            lock (gate)
            {
                Session current;
                if (sessions.TryGetValue(relicId, out current) && current == session)
                {
                    sessions.Remove(relicId);
                }
            }
            session.Dispose();
        }

        private static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        // 요청 한 건의 취소 범위: 부르는 쪽의 ct + 관리자가 거는 취소(NPC 전환·대체·전체 취소)
        private sealed class Session : IDisposable
        {
            private readonly CancellationToken caller;
            private readonly CancellationTokenSource own = new CancellationTokenSource();
            private readonly CancellationTokenSource linked;
            private string reason;

            public Session(CancellationToken caller)
            {
                this.caller = caller;
                linked = CancellationTokenSource.CreateLinkedTokenSource(caller, own.Token);
            }

            /// <summary>SLM 을 기다리다 시간 초과로 그만뒀는지.</summary>
            public bool TimedOut;

            public CancellationToken Token
            {
                get { return linked.Token; }
            }

            public void Cancel(string why)
            {
                if (reason == null)
                {
                    reason = why;
                }
                try
                {
                    own.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 이미 끝난 요청
                }
            }

            public string CancelNote()
            {
                return caller.IsCancellationRequested ? "부르는 쪽이 취소(화면 종료 등)" : (reason ?? "취소");
            }

            public void Dispose()
            {
                linked.Dispose();
                own.Dispose();
            }
        }
    }
}
