using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jamkkaebi.Npc
{
    /// <summary>프롬프트 시험 장면 하나 (tools/npc-prompt-test/data/scenes.json). 실기기 시험에서 요청으로 바꿔 돌린다.</summary>
    public sealed class NpcTestScene
    {
        public string Id { get; internal set; }

        /// <summary>어느 도깨비인지. 시스템 프롬프트를 미리 읽힐 때 쓴다.</summary>
        public int RelicId { get; internal set; }

        /// <summary>게임이 만들 요청과 같은 모양. 돌릴 때마다 새로 만든다 (요청 관리자가 RequestId 를 고쳐 쓴다).</summary>
        public Func<ReactionRequest> CreateRequest { get; internal set; }

        public List<string> Expected { get; internal set; }

        /// <summary>그 장면에서 무엇을 봐야 하는지.</summary>
        public string Note { get; internal set; }
    }

    /// <summary>
    /// scenes.json 을 읽어 요청으로 바꾼다. Unity 의 JsonUtility 는 값의 형(숫자·글자·참거짓)이 섞인 facts 를 못 읽어서
    /// 출력 검사기의 작은 JSON 파서를 쓴다. Verify 의 장면 변환과 같은 규칙이다 (Verify 가 둘을 비교한다).
    /// </summary>
    public static class NpcTestScenes
    {
        public static List<NpcTestScene> Parse(string scenesJson)
        {
            Dictionary<string, object> root = MiniJson.ParseObject(scenesJson);
            var result = new List<NpcTestScene>();
            foreach (object item in List(root, "scenes"))
            {
                var scene = (Dictionary<string, object>)item;
                result.Add(new NpcTestScene
                {
                    Id = Text(scene, "id"),
                    RelicId = (int)(double)scene["relicId"],
                    CreateRequest = () => ToRequest(scene),
                    Expected = Texts(scene, "expected"),
                    Note = Text(scene, "note")
                });
            }
            return result;
        }

        private static ReactionRequest ToRequest(Dictionary<string, object> scene)
        {
            string id = Text(scene, "id");
            var request = new ReactionRequest
            {
                RequestId = id + "-req",
                InteractionId = id,
                RelicId = (int)(double)scene["relicId"],
                Mode = Text(scene, "mode") == "owner" ? InteractionMode.Owner : InteractionMode.Visitor,
                Input = Text(scene, "input"),
                Situation = Text(scene, "event"),
                Nickname = Text(scene, "nickname"),
                Intimacy = IntimacyOf(scene),
                ContentVersion = "test"
            };
            foreach (object c in List(scene, "candidates"))
            {
                var candidate = (Dictionary<string, object>)c;
                request.Candidates.Add(new ActionCandidate(Text(candidate, "action"), Texts(candidate, "targets").ToArray()));
            }
            foreach (object f in List(scene, "facts"))
            {
                var fact = (Dictionary<string, object>)f;
                string key = Text(fact, "key");
                // 이야기 자료는 값에 문장을 넣는다 (시험 데이터는 문장을 text 에 두었다)
                string value = key == NpcFactKeys.VerifiedStory ? Text(fact, "text") : Value(fact.ContainsKey("value") ? fact["value"] : null);
                request.Facts.Add(new Fact(key, value));
            }
            object storyIndex;
            if (scene.TryGetValue("storyIndex", out storyIndex))
            {
                request.Facts.Add(new Fact(NpcFactKeys.StoryIndex, Value(storyIndex)));
            }
            foreach (object m in List(scene, "memory"))
            {
                var memory = (Dictionary<string, object>)m;
                request.Memory.Add(new MemoryEntry((int)(double)memory["daysAgo"], Text(memory, "action"),
                        memory.ContainsKey("summary") ? Text(memory, "summary") : null));
            }
            return request;
        }

        // run.py 의 fact_value 와 같게: 참거짓은 소문자, 정수는 소수점 없이
        private static string Value(object value)
        {
            if (value == null)
            {
                return "";
            }
            if (value is bool)
            {
                return (bool)value ? "true" : "false";
            }
            if (value is double)
            {
                double d = (double)value;
                return d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        /// <summary>장면의 친밀도. "intimacy"(0~4)가 있으면 그것, 없으면 예전 칸 "familiar"(true → 닉네임 단계(3), false → 0단계).</summary>
        public static int IntimacyOf(Dictionary<string, object> scene)
        {
            object value;
            if (scene.TryGetValue("intimacy", out value) && value != null && !(value is bool))
            {
                return Math.Max(0, Math.Min(NpcAddress.LevelUpVisits.Count, Convert.ToInt32(value, CultureInfo.InvariantCulture)));
            }
            return scene.TryGetValue("familiar", out value) && value is bool && (bool)value ? NpcAddress.NicknameLevel : 0;
        }

        private static List<object> List(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value is List<object> ? (List<object>)value : new List<object>();
        }

        private static List<string> Texts(Dictionary<string, object> map, string key)
        {
            var texts = new List<string>();
            foreach (object value in List(map, key))
            {
                texts.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            return texts;
        }
    }

    /// <summary>
    /// 기억을 꺼내는 장면(대화 기억이 있는 주인 안부, S14)을 실기기 시험에서 <b>게임처럼</b> 돌린다. scenes.json 의 장면은 같은 기억을 늘 꺼내는
    /// 요청이라, 반복하면 게임에서는 일어나지 않는 "같은 기억을 연달아 꺼내기"가 된다. 이 클래스는 장면마다 기억을 들고 있다가 반복마다
    /// 컨텍스트 구성기(NpcContextBuilder)로 요청을 만들고, 띄운 뒤 Finish 로 남길 기억과 꺼냈음 표시(MemoryEntry.Recalled)를 반영한다.
    /// 장면의 기억이 하나면 반복 1은 지난 대화, 반복 2부터는 평소 안부가 된다(꺼낼 기억이 남지 않음).
    /// 요청의 ID·닉네임·친밀도·콘텐츠 버전·곁들일 거리 번호는 장면 것을 그대로 써서, 반복 1의 요청은 장면의 요청과 같다(Verify 가 비교한다).
    /// </summary>
    public sealed class NpcTestMemory
    {
        private readonly NpcContextBuilder builder;
        private readonly DateTime today;
        private readonly Dictionary<string, List<MemoryEntry>> memories = new Dictionary<string, List<MemoryEntry>>();

        public NpcTestMemory(PersonaCatalog personas, DateTime today)
        {
            builder = new NpcContextBuilder(personas);
            this.today = today.Date;
        }

        /// <summary>대화 기억이 있는 주인 안부 장면인지 — 이 클래스로 돌릴 장면</summary>
        public static bool Handles(NpcTestScene scene)
        {
            ReactionRequest request = scene.CreateRequest();
            return request.Mode == InteractionMode.Owner && request.Input == NpcInputs.AskStatus && request.Memory != null && request.Memory.Count > 0;
        }

        /// <summary>이 장면의 지금 기억으로 다음 안부 요청을 만든다. 띄운 뒤 <see cref="Finish"/> 를 꼭 부른다.</summary>
        public NpcContext Build(NpcTestScene scene)
        {
            ReactionRequest sample = scene.CreateRequest();
            List<MemoryEntry> memory;
            if (!memories.TryGetValue(scene.Id, out memory))
            {
                memory = new List<MemoryEntry>(sample.Memory);
                memories[scene.Id] = memory;
            }
            NpcContext context = builder.Build(StateOf(sample, memory));
            CopyScene(sample, context.Request);
            return context;
        }

        /// <summary>
        /// 게임이 미리 만들어 둘 안부 요청들 (NpcContextBuilder.PrefetchTemplates 의 안부 — 지난 대화와 그다음 평소 안부).
        /// 장면 요청과 함께 PrefetchTemplate 에 넘긴다.
        /// </summary>
        public List<ReactionRequest> PrefetchRequests(NpcTestScene scene)
        {
            ReactionRequest sample = scene.CreateRequest();
            var result = new List<ReactionRequest>();
            foreach (ReactionRequest request in builder.PrefetchTemplates(StateOf(sample, new List<MemoryEntry>(sample.Memory))))
            {
                if (request.Input == NpcInputs.AskStatus)
                {
                    CopyScene(sample, request);
                    result.Add(request);
                }
            }
            return result;
        }

        /// <summary>
        /// 게임처럼 마무리한다: 띄웠으면 남길 기억(오늘, DaysAgo 0)을 뒤에 붙이고, 꺼냈음 표시는 Finish 가 이 장면의 기억에 바로 남긴다.
        /// 무엇을 했는지 글로 돌려준다 (CSV 에 적는다).
        /// </summary>
        public string Finish(NpcTestScene scene, NpcContext context, NpcResponse response)
        {
            MemoryEntry marked = context.MemoryToMark;
            MemoryEntry saved = context.Finish(response);
            List<MemoryEntry> memory;
            if (saved != null && memories.TryGetValue(scene.Id, out memory))
            {
                memory.Add(new MemoryEntry(0, saved.ActionId, saved.Summary));
            }
            string kind = context.KeyFact == "recall" ? "기억: 지난 대화 꺼냄" : "기억: 평소 안부";
            if (marked != null && marked.Recalled)
            {
                kind += marked.ActionId == NpcActions.RecallTalk ? " (꺼낸 뒤 한 번 쉼 표시)" : " (꺼냈음 표시)";
            }
            return kind;
        }

        private NpcContextInput StateOf(ReactionRequest sample, List<MemoryEntry> memory)
        {
            // 선명하고 최근 관리가 없는 주인 — 그래야 안부가 지난 대화(③) 또는 평소(④)가 된다
            return new NpcContextInput
            {
                RelicId = sample.RelicId, Mode = InteractionMode.Owner, Input = NpcInputs.AskStatus, Today = today,
                Clarity = "CLEAR", AwakeningStage = "AWAKENED", LastPurifiedAt = null, Nickname = sample.Nickname, Memory = memory
            };
        }

        private static void CopyScene(ReactionRequest sample, ReactionRequest request)
        {
            request.RequestId = sample.RequestId;
            request.InteractionId = sample.InteractionId;
            request.ContentVersion = sample.ContentVersion;
            request.Nickname = sample.Nickname;
            request.Intimacy = sample.Intimacy;
            request.Variation = sample.Variation;   // 구성기가 매긴 곁들일 거리 번호 대신 장면 것 (시험은 반복마다 따로 정한다)
        }
    }
}
