using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 기본 대사로 반응을 만드는 생성기. 모델이 없어도 바로 돌아간다.
    ///
    /// <para>쓰는 곳: 클라이언트가 AI 없이 전체 흐름을 먼저 만들 때, 그리고 SLM 생성기가 시간 초과·실패했을 때와
    /// 저사양 기기에서 대신 쓴다. SLM 생성기와 같은 인터페이스라 바꿔 끼우면 된다.</para>
    ///
    /// <para>고르는 규칙</para>
    /// <list type="number">
    /// <item>손님 관람: 최근 30일에 RegularVisitorDays 번 이상 온 손님이면 단골 알아보기, 아니면 인사.</item>
    /// <item>그 밖에는 도깨비의 preferredActions 순서로 후보에서 고르고, 없으면 첫 후보.</item>
    /// <item>대상은 고른 후보의 첫 대상.</item>
    /// </list>
    ///
    /// <para>대사 규칙</para>
    /// <list type="bullet">
    /// <item>안부 답하기: 흐려짐·재봉인이면 관리 부탁, RecentCareDays 일 안에 관리를 받았으면 그 일을 떠올림, 아니면 평소 상태.</item>
    /// <item>이야기 듣기(늘 이 생성기가 맡는다 — SLM 은 이야기를 만들지 않는다): 페르소나 도깨비는 실화를 담은 이야기 대사(stories) 중 요청의 story_index 번째를 쓰고, 번호가 없으면
    /// StoryRotation 으로 돌려 가며 고른다 — 다 들려줄 때까지 같은 이야기를 반복하지 않는다. 페르소나 없는 도깨비는
    /// 자료 문장(verified_story)을 바꾸지 않고 그대로 싣고 앞뒤에 말투를 붙인다.</item>
    /// <item>같은 도깨비·같은 행동에서 바로 앞과 같은 대사는 피한다.</item>
    /// </list>
    /// </summary>
    public sealed class RuleBasedReactionGenerator : INpcReactionGenerator
    {
        /// <summary>이 일수 안에 관리를 받았으면 안부 답하기에서 그 관리를 떠올린다.</summary>
        public const int RecentCareDays = 3;

        /// <summary>최근 30일에 이만큼 이상 온 손님은 단골로 알아본다.</summary>
        public const int RegularVisitorDays = 2;

        private const string VerifiedPrefix = "검수된 사실:";

        private static readonly string[] FadedClarities = { "FADED", "RESEALED", "흐려짐", "재봉인" };

        private readonly DefaultLineCatalog catalog;
        private readonly Random random;
        private readonly StoryRotation stories;
        private readonly object gate = new object();
        private readonly Dictionary<string, string> lastLines = new Dictionary<string, string>();

        public RuleBasedReactionGenerator(DefaultLineCatalog catalog) : this(catalog, new Random(), new StoryRotation())
        {
        }

        /// <param name="random">테스트에서 결과를 고정하려면 시드를 준 Random 을 넣는다.</param>
        public RuleBasedReactionGenerator(DefaultLineCatalog catalog, Random random)
                : this(catalog, random, new StoryRotation())
        {
        }

        /// <param name="stories">
        /// 요청에 story_index 가 없을 때 이야기를 돌려 가며 고르는 기록. 컨텍스트 구성기와 같은 것을 넘기면 들려준 기록을 함께 쓴다.
        /// </param>
        public RuleBasedReactionGenerator(DefaultLineCatalog catalog, Random random, StoryRotation stories)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException("catalog");
            }
            if (catalog.fallback == null)
            {
                throw new ArgumentException("기본 대사 카탈로그에 fallback 이 없다.", "catalog");
            }
            if (random == null)
            {
                throw new ArgumentNullException("random");
            }
            if (stories == null)
            {
                throw new ArgumentNullException("stories");
            }
            this.catalog = catalog;
            this.random = random;
            this.stories = stories;
        }

        public bool IsReady
        {
            get { return true; }
        }

        public Task LoadAsync(CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public void Unload()
        {
        }

        /// <summary>미리 읽어 둘 것이 없다.</summary>
        public Task PrepareAsync(int relicId, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            return Task.FromResult(Generate(request, ct));
        }

        /// <summary>GenerateAsync 와 같다. 기다릴 것이 없어서 바로 돌려준다.</summary>
        public ReactionResult Generate(ReactionRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return ReactionResult.Failed(null, FailReason.Error, ReactionSource.Rule);
            }
            if (ct.IsCancellationRequested)
            {
                return ReactionResult.Failed(request.RequestId, FailReason.Cancelled, ReactionSource.Rule);
            }
            if (request.Candidates == null || request.Candidates.Count == 0)
            {
                return ReactionResult.Failed(request.RequestId, FailReason.NoCandidate, ReactionSource.Rule);
            }

            // 계약상 생성기는 예외를 던지지 않는다. 데이터가 깨져 있어도 실패 결과로 돌려준다.
            try
            {
                SpiritLines spirit = catalog.Find(request.RelicId);
                ActionCandidate chosen = ChooseCandidate(request, spirit);
                string target = chosen.TargetIds != null && chosen.TargetIds.Count > 0
                        ? chosen.TargetIds[0]
                        : NpcTargets.None;
                string line = ComposeLine(request, spirit, chosen.ActionId);
                if (string.IsNullOrEmpty(line))
                {
                    return ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Rule);
                }
                return ReactionResult.Success(request.RequestId, chosen.ActionId, target, line, ReactionSource.Rule);
            }
            catch (Exception)
            {
                return ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Rule);
            }
        }

        private static ActionCandidate ChooseCandidate(ReactionRequest request, SpiritLines spirit)
        {
            if (request.Input == NpcInputs.Visit)
            {
                int visits;
                bool regular = int.TryParse(request.FindFact(NpcFactKeys.VisitCount30d), out visits)
                        && visits >= RegularVisitorDays;
                ActionCandidate visitPick = FindCandidate(request.Candidates,
                        regular ? NpcActions.WanderRecall : NpcActions.WanderGreet);
                if (visitPick != null)
                {
                    return visitPick;
                }
            }

            if (spirit.preferredActions != null)
            {
                foreach (string preferred in spirit.preferredActions)
                {
                    ActionCandidate candidate = FindCandidate(request.Candidates, preferred);
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
            }
            return request.Candidates[0];
        }

        private static ActionCandidate FindCandidate(List<ActionCandidate> candidates, string actionId)
        {
            foreach (ActionCandidate candidate in candidates)
            {
                if (candidate != null && candidate.ActionId == actionId)
                {
                    return candidate;
                }
            }
            return null;
        }

        private string ComposeLine(ReactionRequest request, SpiritLines spirit, string actionId)
        {
            if (actionId == NpcActions.TellStory)
            {
                string story = StoryLine(request, spirit);
                if (story != null)
                {
                    return story;
                }
            }

            string variant = actionId == NpcActions.AnswerStatus ? StatusVariant(request) : "";
            List<string> lines = spirit.FindLines(actionId, variant)
                    ?? spirit.FindLines(actionId, "")
                    ?? catalog.fallback.FindLines(actionId, variant)
                    ?? catalog.fallback.FindLines(actionId, "");
            if (lines == null)
            {
                return null;
            }
            return Pick(request.RelicId + ":" + actionId + ":" + variant, lines, request.RecentLines);
        }

        private string StoryLine(ReactionRequest request, SpiritLines spirit)
        {
            if (spirit.stories != null && spirit.stories.Count > 0)
            {
                // 요청이 정한 번호(SLM 과 같은 실화)를 따르고, 없으면 돌려 가며 고른다
                int index;
                if (!int.TryParse(request.FindFact(NpcFactKeys.StoryIndex), out index)
                        || index < 0 || index >= spirit.stories.Count)
                {
                    index = stories.Next(request.RelicId, spirit.stories.Count);
                }
                return spirit.stories[index];
            }
            return Story(spirit, request.FindFact(NpcFactKeys.VerifiedStory));
        }

        /// <summary>
        /// 페르소나 없는 도깨비의 이야기. 자료 문장은 고치지 않고 그대로 싣는다. 어미를 도깨비 말투로 바꾸려 하면 '공예다고' 같은 비문이 생기고,
        /// 사실이 조금이라도 바뀔 수 있다. 말투는 앞뒤에 붙인 말로 낸다.
        /// </summary>
        private static string Story(SpiritLines spirit, string fact)
        {
            if (string.IsNullOrEmpty(fact))
            {
                return null;
            }
            string body = fact.Trim();
            if (body.StartsWith(VerifiedPrefix, StringComparison.Ordinal))
            {
                body = body.Substring(VerifiedPrefix.Length).Trim();
            }
            body = body.TrimEnd('.', ' ');
            if (body.Length == 0)
            {
                return null;
            }
            return (spirit.storyPrefix ?? "") + body + "." + (spirit.storyTail ?? "");
        }

        private static string StatusVariant(ReactionRequest request)
        {
            string clarity = request.FindFact(NpcFactKeys.Clarity);
            if (clarity != null && Array.IndexOf(FadedClarities, clarity.Trim()) >= 0)
            {
                return "faded";
            }
            int days;
            if (int.TryParse(request.FindFact(NpcFactKeys.LastCareDaysAgo), out days) && days <= RecentCareDays)
            {
                return "recent_care";
            }
            return "default";
        }

        // 같은 도깨비·같은 행동에서 바로 앞과 같은 대사는 피하고, 그 도깨비가 최근에 띄운 대사(request.RecentLines)와
        // 같은 대사도 되도록 피한다. 다 겹치면 바로 앞과 다른 것, 그것도 없으면 아무거나 쓴다
        private string Pick(string key, List<string> lines, List<string> recent)
        {
            lock (gate)
            {
                string last;
                lastLines.TryGetValue(key, out last);
                List<string> pool = lines.Where(line => line != last).ToList();
                if (pool.Count == 0)
                {
                    pool = lines;
                }
                List<string> fresh = pool.Where(line => !NpcLineChecks.FindSimilar(line, recent).HasValue).ToList();
                if (fresh.Count > 0)
                {
                    pool = fresh;
                }
                string picked = pool[random.Next(pool.Count)];
                lastLines[key] = picked;
                return picked;
            }
        }
    }
}
