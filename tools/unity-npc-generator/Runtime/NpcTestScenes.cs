using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jamkkaebi.Npc
{
    /// <summary>프롬프트 시험 장면 하나 (tools/npc-prompt-test/data/scenes.json). 실기기 시험에서 요청으로 바꿔 돌린다.</summary>
    public sealed class NpcTestScene
    {
        public string Id { get; internal set; }

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
}
