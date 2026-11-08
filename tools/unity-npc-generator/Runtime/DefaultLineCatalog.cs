using System;
using System.Collections.Generic;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 규칙 기반 생성기가 쓰는 기본 대사 (default_lines.json).
    ///
    /// <para>Unity 의 JsonUtility 로 읽을 수 있게 public 필드와 List 만 쓴다 (Dictionary 는 JsonUtility 가 읽지 못한다).
    /// 필드 이름이 곧 JSON 키다.</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultLineCatalog
    {
        public string contentVersion;
        public List<SpiritLines> spirits = new List<SpiritLines>();

        /// <summary>페르소나가 아직 없는 도깨비가 쓰는 대사. 반드시 있어야 한다.</summary>
        public SpiritLines fallback;

        /// <summary>도깨비의 대사. 없으면 fallback.</summary>
        public SpiritLines Find(int relicId)
        {
            if (spirits != null)
            {
                foreach (SpiritLines spirit in spirits)
                {
                    if (spirit != null && spirit.relicId == relicId)
                    {
                        return spirit;
                    }
                }
            }
            return fallback;
        }
    }

    [Serializable]
    public sealed class SpiritLines
    {
        public int relicId;
        public string name;

        /// <summary>
        /// 후보가 여럿일 때 이 순서로 먼저 고른다. 페르소나의 성향이다
        /// (예: 묘작이는 기쁜 감사·놀이처럼 버둥, 밤바다는 담담한 감사·놓으라며 버둥).
        /// </summary>
        public List<string> preferredActions = new List<string>();

        /// <summary>
        /// 실화를 담은 이야기 대사. 페르소나(personas.json)의 stories 와 같은 순서라, 요청의 story_index 로 같은 이야기를 고른다.
        /// 다 들려줄 때까지 같은 이야기를 반복하지 않도록 돌려 가며 쓴다.
        /// </summary>
        public List<string> stories = new List<string>();

        /// <summary>stories 가 없을 때(페르소나 없는 도깨비) 이야기 듣기에서 자료 문장 앞에 붙는 말투.</summary>
        public string storyPrefix;

        /// <summary>stories 가 없을 때 자료 문장 뒤에 붙는 말투. 앞에 공백을 넣어 둔다.</summary>
        public string storyTail;

        public List<LinePool> pools = new List<LinePool>();

        /// <summary>행동과 변형에 맞는 대사 목록. 없으면 null.</summary>
        public List<string> FindLines(string actionId, string variant)
        {
            if (pools == null)
            {
                return null;
            }
            string wanted = variant ?? "";
            foreach (LinePool pool in pools)
            {
                if (pool != null && pool.actionId == actionId && (pool.variant ?? "") == wanted
                        && pool.lines != null && pool.lines.Count > 0)
                {
                    return pool.lines;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// 행동 하나의 대사 묶음. variant 는 같은 행동을 상황에 따라 나눈 것이다
    /// (answer_status: faded = 흐려짐·재봉인, recent_care = 최근에 관리 받음, default = 그 밖). 나누지 않으면 빈 문자열.
    /// </summary>
    [Serializable]
    public sealed class LinePool
    {
        public string actionId;
        public string variant;
        public List<string> lines = new List<string>();
    }
}
