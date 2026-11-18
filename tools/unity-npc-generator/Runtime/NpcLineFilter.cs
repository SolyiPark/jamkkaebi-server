using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Jamkkaebi.Npc
{
    /// <summary>
    /// 공통 금칙어 (forbidden.json). 대화의 목적이 플레이어의 애착 형성이라, 욕설뿐 아니라 상대를 밀어내거나
    /// 무시하는 지나친 퉁명스러움도 막는다. JsonUtility 로 읽을 수 있게 public 필드와 List 만 쓴다.
    /// </summary>
    [Serializable]
    public sealed class ForbiddenCatalog
    {
        public string contentVersion;
        public List<ForbiddenGroup> groups = new List<ForbiddenGroup>();
    }

    [Serializable]
    public sealed class ForbiddenGroup
    {
        /// <summary>profanity · insult · push_away · cold · threat</summary>
        public string id;

        public string label;

        /// <summary>공백을 지우고 부분 문자열로 찾는 낱말.</summary>
        public List<string> words = new List<string>();

        /// <summary>공백을 지운 대사에 쓰는 정규식. 낱말로는 오탐이 나는 꼴(예: '놓아' 명령형)에 쓴다.</summary>
        public List<string> patterns = new List<string>();
    }

    /// <summary>금칙어에 걸린 내용.</summary>
    public sealed class ForbiddenMatch
    {
        public ForbiddenMatch(string groupId, string label, string text)
        {
            GroupId = groupId;
            Label = label;
            Text = text;
        }

        public string GroupId { get; private set; }
        public string Label { get; private set; }

        /// <summary>걸린 말 (공백을 지운 꼴).</summary>
        public string Text { get; private set; }

        public override string ToString()
        {
            return Label + ":" + Text;
        }
    }

    /// <summary>
    /// 대사가 공통 금칙어에 걸리는지 본다. tools/npc-prompt-test/run.py 의 공통금칙어 검사와 같은 규칙이다
    /// (대사와 낱말의 공백을 지운 뒤 부분 문자열, patterns 는 정규식).
    ///
    /// <para>쓰는 곳: SLM 생성기의 응답 검사. 모델 대사가 걸리면 FailReason.InvalidOutput 으로 버리고 규칙 기반 생성기로
    /// 대신한다. 기본 대사는 Verify 에서 미리 이 필터를 통과하는지 확인한다.</para>
    /// </summary>
    public sealed class NpcLineFilter
    {
        private readonly List<Entry> entries = new List<Entry>();

        public NpcLineFilter(ForbiddenCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException("catalog");
            }
            if (catalog.groups == null)
            {
                return;
            }
            foreach (ForbiddenGroup group in catalog.groups)
            {
                if (group == null)
                {
                    continue;
                }
                var entry = new Entry { Group = group };
                if (group.words != null)
                {
                    foreach (string word in group.words)
                    {
                        string core = Normalize(word);
                        if (core.Length > 0)
                        {
                            entry.Words.Add(core);
                        }
                    }
                }
                if (group.patterns != null)
                {
                    foreach (string pattern in group.patterns)
                    {
                        if (!string.IsNullOrEmpty(pattern))
                        {
                            entry.Patterns.Add(new Regex(pattern, RegexOptions.CultureInvariant));
                        }
                    }
                }
                entries.Add(entry);
            }
        }

        /// <summary>금칙어가 없으면 true. 빈 대사도 true (길이 검사는 따로 한다).</summary>
        public bool IsAllowed(string line)
        {
            return Find(line) == null;
        }

        /// <summary>처음 걸린 금칙어. 없으면 null.</summary>
        public ForbiddenMatch Find(string line)
        {
            List<ForbiddenMatch> all = FindAll(line, true);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>걸린 금칙어 전부. 시험·로그용.</summary>
        public List<ForbiddenMatch> FindAll(string line)
        {
            return FindAll(line, false);
        }

        /// <summary>공백(줄바꿈 포함)을 모두 지운다.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        private List<ForbiddenMatch> FindAll(string line, bool firstOnly)
        {
            var found = new List<ForbiddenMatch>();
            string core = Normalize(line);
            if (core.Length == 0)
            {
                return found;
            }
            foreach (Entry entry in entries)
            {
                foreach (string word in entry.Words)
                {
                    if (core.IndexOf(word, StringComparison.Ordinal) >= 0)
                    {
                        found.Add(new ForbiddenMatch(entry.Group.id, entry.Group.label, word));
                        if (firstOnly)
                        {
                            return found;
                        }
                    }
                }
                foreach (Regex pattern in entry.Patterns)
                {
                    Match match = pattern.Match(core);
                    if (match.Success)
                    {
                        found.Add(new ForbiddenMatch(entry.Group.id, entry.Group.label, match.Value));
                        if (firstOnly)
                        {
                            return found;
                        }
                    }
                }
            }
            return found;
        }

        private sealed class Entry
        {
            public ForbiddenGroup Group;
            public readonly List<string> Words = new List<string>();
            public readonly List<Regex> Patterns = new List<Regex>();
        }
    }
}
