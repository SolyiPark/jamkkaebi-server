using System;
using System.Collections.Generic;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 도깨비 페르소나 (personas.json). 프롬프트 조립기가 쓴다.
    /// JsonUtility 로 읽을 수 있게 public 필드와 List 만 쓴다. 필드 이름이 곧 JSON 키다.
    /// </summary>
    [Serializable]
    public sealed class PersonaCatalog
    {
        public string contentVersion;
        public List<Persona> personas = new List<Persona>();

        /// <summary>도깨비의 페르소나. 아직 없는 도깨비면 null.</summary>
        public Persona Find(int relicId)
        {
            if (personas != null)
            {
                foreach (Persona persona in personas)
                {
                    if (persona != null && persona.relicId == relicId)
                    {
                        return persona;
                    }
                }
            }
            return null;
        }
    }

    [Serializable]
    public sealed class Persona
    {
        public int relicId;
        public string name;
        public string origin;
        public string traits;
        public string values;
        public string likes;
        public string dislikes;
        public string toOwner;
        public string toVisitor;

        // 상황별 말투. 선택지에 맞는 것만 프롬프트에 싣는다
        public string emotionStyle;
        public string onCare;
        public string onStory;
        public string onRefuse;
        public string onDrag;

        public string speechStyle;
        public List<string> examples = new List<string>();

        /// <summary>
        /// 실화 이야기 조각. 서버 realStory 를 이야기 한 번에 하나씩 쓰도록 나눈 것이고, 사실을 바꾸거나 보태지 않았다.
        /// 기본 대사(default_lines.json)의 stories 와 같은 순서다.
        /// </summary>
        public List<StoryPiece> stories = new List<StoryPiece>();

        /// <summary>어미 검사용. 프롬프트에는 싣지 않는다.</summary>
        public EndingHint endingHint;
    }

    [Serializable]
    public sealed class StoryPiece
    {
        /// <summary>대화 기억 요약에 쓰는 짧은 이름. 예: "암행어사".</summary>
        public string topic;

        /// <summary>이야기할 사실 한 문장. verified_story 사실의 값으로 넣는다.</summary>
        public string fact;

        /// <summary>사실을 확인한 곳 (공식 기록·백과사전·언론 주소, 서버 realStory). 프롬프트에는 싣지 않는다.</summary>
        public List<string> sources = new List<string>();

        /// <summary>
        /// 들려준 뒤 남길 대화 기억 요약. 다음 이야기 때 프롬프트에 실려, 모델이 지난 이야기를 다시 풀지 않고 이어 말하게 한다.
        /// </summary>
        public string MemorySummary()
        {
            return "주인에게 " + topic + " 이야기를 들려줬다";
        }
    }

    [Serializable]
    public sealed class EndingHint
    {
        public string label;
        public string pattern;
        public string notPattern;
    }

    /// <summary>행동 카탈로그 (actions.json).</summary>
    [Serializable]
    public sealed class ActionCatalog
    {
        public string contentVersion;
        public List<string> animations = new List<string>();
        public List<ActionDefinition> actions = new List<ActionDefinition>();

        public ActionDefinition Find(string id)
        {
            if (actions != null)
            {
                foreach (ActionDefinition action in actions)
                {
                    if (action != null && action.id == id)
                    {
                        return action;
                    }
                }
            }
            return null;
        }
    }

    [Serializable]
    public sealed class ActionDefinition
    {
        public string id;

        /// <summary>행동의 뜻. 프롬프트의 후보 목록에 그대로 싣는다.</summary>
        public string meaning;

        /// <summary>Idle / Walking / Positive / Struggle</summary>
        public string animation;

        /// <summary>이 행동을 쓰는 선택지(NpcInputs).</summary>
        public List<string> inputs = new List<string>();

        /// <summary>후보 필터가 이 행동을 넣는 조건. 사람이 읽는 설명이다.</summary>
        public string condition;
    }
}
