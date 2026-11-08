using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jamkkaebi.Npc
{
    /// <summary>출력 검사에서 걸린 이유. 검사하는 순서대로 적었다.</summary>
    public enum OutputProblem
    {
        None,
        NotJson,        // JSON 객체를 찾지 못했거나 읽을 수 없다
        UnknownAction,  // 행동이 후보에 없다
        TargetMismatch, // 대상이 고른 행동의 대상이 아니다
        EmptyLine,      // 대사가 없다
        Newline,        // 대사에 줄바꿈이 있다
        TooLong,        // 대사가 길이 제한을 넘는다
        Forbidden       // 대사가 공통 금칙어에 걸린다
    }

    /// <summary>모델 출력 한 건의 검사 결과.</summary>
    public sealed class OutputCheck
    {
        internal OutputCheck(OutputProblem problem, string detail, string actionId, string targetId, string line)
        {
            Problem = problem;
            Detail = detail;
            ActionId = actionId;
            TargetId = targetId;
            Line = line;
        }

        public OutputProblem Problem { get; private set; }

        /// <summary>걸린 이유를 사람이 읽게 적은 것. 통과하면 null.</summary>
        public string Detail { get; private set; }

        public string ActionId { get; private set; }
        public string TargetId { get; private set; }

        /// <summary>앞뒤 공백과 따옴표를 걷어 낸 대사.</summary>
        public string Line { get; private set; }

        public bool IsValid
        {
            get { return Problem == OutputProblem.None; }
        }

        /// <summary>통과하면 Success, 아니면 Failed(InvalidOutput). 출처는 Slm.</summary>
        public ReactionResult ToResult(string requestId)
        {
            return IsValid
                    ? ReactionResult.Success(requestId, ActionId, TargetId, Line, ReactionSource.Slm)
                    : ReactionResult.Failed(requestId, FailReason.InvalidOutput, ReactionSource.Slm, Problem + ": " + Detail);
        }
    }

    /// <summary>
    /// SLM 출력 검사. 모델이 낸 글을 읽어 아래를 차례로 확인하고, 하나라도 걸리면 화면에 띄우지 않는다.
    /// <list type="number">
    /// <item>JSON: 앞뒤 잡글·생각(&lt;think&gt;)·코드 블록이 있어도 JSON 객체 하나를 찾아 읽는다.</item>
    /// <item>후보 포함: 행동이 후보에 있고, 대상이 <b>그 행동의</b> 대상 중 하나다.</item>
    /// <item>대사: 비어 있지 않고, 줄바꿈이 없고, 길이 제한 안이다.</item>
    /// <item>금칙어: 공통 금칙어(NpcLineFilter)에 걸리지 않는다.</item>
    /// </list>
    /// 길이 제한은 tools/npc-prompt-test/run.py 의 길이OK 판정과 같다 (기본 40자, 손님 관람 25자, 이야기 75자).
    /// 프롬프트에 적는 길이(30·20·60자)보다 조금 넉넉하다 — 작은 모델은 글자 수를 정확히 세지 못한다.
    /// </summary>
    public sealed class NpcOutputValidator
    {
        /// <summary>MaxChars 에 없는 선택지의 대사 길이 제한.</summary>
        public const int DefaultMaxChars = 40;

        private const string QuoteChars = "\"“”'‘’";

        private readonly NpcLineFilter filter;

        public NpcOutputValidator(NpcLineFilter filter)
        {
            if (filter == null)
            {
                throw new ArgumentNullException("filter");
            }
            this.filter = filter;
            MaxChars = new Dictionary<string, int>
            {
                { NpcInputs.ListenStory, 75 },
                { NpcInputs.Visit, 25 }
            };
        }

        /// <summary>선택지별 대사 길이 제한.</summary>
        public Dictionary<string, int> MaxChars { get; set; }

        /// <summary>그 선택지의 대사 길이 제한.</summary>
        public int LimitFor(string input)
        {
            int limit;
            return input != null && MaxChars != null && MaxChars.TryGetValue(input, out limit) ? limit : DefaultMaxChars;
        }

        public OutputCheck Check(string raw, ReactionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            Dictionary<string, object> json;
            string jsonError;
            if (!TryReadObject(raw, out json, out jsonError))
            {
                return Fail(OutputProblem.NotJson, jsonError, null, null, null);
            }

            string action = Text(json, "action").Trim();
            string target = Text(json, "target").Trim();
            string line = CleanLine(Text(json, "line"));

            ActionCandidate candidate = null;
            if (request.Candidates != null)
            {
                foreach (ActionCandidate c in request.Candidates)
                {
                    if (c != null && c.ActionId == action)
                    {
                        candidate = c;
                        break;
                    }
                }
            }
            if (candidate == null)
            {
                return Fail(OutputProblem.UnknownAction, "후보에 없는 행동 '" + action + "'", action, target, line);
            }
            List<string> targets = candidate.TargetIds != null && candidate.TargetIds.Count > 0
                    ? candidate.TargetIds
                    : new List<string> { NpcTargets.None };
            if (!targets.Contains(target))
            {
                return Fail(OutputProblem.TargetMismatch,
                        "'" + action + "' 의 대상이 아닌 '" + target + "' (허용: " + string.Join(", ", targets.ToArray()) + ")",
                        action, target, line);
            }

            if (line.Length == 0)
            {
                return Fail(OutputProblem.EmptyLine, "대사가 비어 있다", action, target, line);
            }
            if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
            {
                return Fail(OutputProblem.Newline, "대사에 줄바꿈이 있다", action, target, line);
            }
            int limit = LimitFor(request.Input);
            if (line.Length > limit)
            {
                return Fail(OutputProblem.TooLong, line.Length + "자 (제한 " + limit + "자)", action, target, line);
            }
            ForbiddenMatch bad = filter.Find(line);
            if (bad != null)
            {
                return Fail(OutputProblem.Forbidden, "금칙어 " + bad, action, target, line);
            }
            return new OutputCheck(OutputProblem.None, null, action, target, line);
        }

        private static OutputCheck Fail(OutputProblem problem, string detail, string action, string target, string line)
        {
            return new OutputCheck(problem, detail, action, target, line);
        }

        // run.py 의 clean_line 과 같다: 앞뒤 공백 → 따옴표 → 공백
        private static string CleanLine(string value)
        {
            return (value ?? "").Trim().Trim(QuoteChars.ToCharArray()).Trim();
        }

        private static string Text(Dictionary<string, object> json, string key)
        {
            object value;
            if (!json.TryGetValue(key, out value) || value == null)
            {
                return "";
            }
            string text = value as string;
            return text ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>생각 블록을 지우고, 처음 '{' 부터 마지막 '}' 까지를 JSON 객체로 읽는다.</summary>
        private static bool TryReadObject(string raw, out Dictionary<string, object> json, out string error)
        {
            json = null;
            string text = StripThink(raw ?? "");
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "JSON 객체가 없다";
                return false;
            }
            try
            {
                json = MiniJson.ParseObject(text.Substring(start, end - start + 1));
                error = null;
                return true;
            }
            catch (FormatException e)
            {
                error = "JSON 을 읽지 못했다: " + e.Message;
                return false;
            }
        }

        // 생각 모드를 끄지 못한 모델이 <think>…</think> 를 앞에 붙이는 경우. 닫히지 않았으면 끝까지 지운다
        private static string StripThink(string text)
        {
            while (true)
            {
                int open = text.IndexOf("<think>", StringComparison.Ordinal);
                if (open < 0)
                {
                    return text;
                }
                int close = text.IndexOf("</think>", open, StringComparison.Ordinal);
                text = close < 0 ? text.Substring(0, open) : text.Remove(open, close + "</think>".Length - open);
            }
        }
    }

    /// <summary>
    /// 모델 출력만 읽는 작은 JSON 파서. Unity 의 JsonUtility 는 필드가 정해진 클래스로만 읽고 잘못된 JSON 을 알려 주지 않아서 따로 둔다.
    /// 문자열 안의 날것 줄바꿈은 받아 두고(대사 검사에서 줄바꿈으로 거른다), 그 밖에는 표준 JSON 을 따른다.
    /// </summary>
    internal sealed class MiniJson
    {
        private readonly string s;
        private int i;

        private MiniJson(string text)
        {
            s = text;
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var parser = new MiniJson(text);
            parser.SkipSpace();
            Dictionary<string, object> result = parser.ReadObject();
            parser.SkipSpace();
            if (parser.i != parser.s.Length)
            {
                throw new FormatException(parser.i + "번째 글자 뒤에 남은 글이 있다");
            }
            return result;
        }

        private object ReadValue()
        {
            SkipSpace();
            if (i >= s.Length)
            {
                throw new FormatException("값이 없다");
            }
            char c = s[i];
            switch (c)
            {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return ReadString();
                case 't':
                    ReadWord("true");
                    return true;
                case 'f':
                    ReadWord("false");
                    return false;
                case 'n':
                    ReadWord("null");
                    return null;
                default:
                    return ReadNumber();
            }
        }

        private Dictionary<string, object> ReadObject()
        {
            Expect('{');
            var result = new Dictionary<string, object>();
            SkipSpace();
            if (Peek() == '}')
            {
                i++;
                return result;
            }
            while (true)
            {
                SkipSpace();
                string key = ReadString();
                SkipSpace();
                Expect(':');
                result[key] = ReadValue();
                SkipSpace();
                if (Peek() == ',')
                {
                    i++;
                    continue;
                }
                Expect('}');
                return result;
            }
        }

        private List<object> ReadArray()
        {
            Expect('[');
            var result = new List<object>();
            SkipSpace();
            if (Peek() == ']')
            {
                i++;
                return result;
            }
            while (true)
            {
                result.Add(ReadValue());
                SkipSpace();
                if (Peek() == ',')
                {
                    i++;
                    continue;
                }
                Expect(']');
                return result;
            }
        }

        private string ReadString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (i >= s.Length)
                {
                    break;
                }
                char e = s[i++];
                switch (e)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > s.Length
                                || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        {
                            throw new FormatException("잘못된 \\u 이스케이프");
                        }
                        builder.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new FormatException("잘못된 이스케이프 \\" + e);
                }
            }
            throw new FormatException("문자열이 닫히지 않았다");
        }

        private double ReadNumber()
        {
            int start = i;
            while (i < s.Length && "+-.0123456789eE".IndexOf(s[i]) >= 0)
            {
                i++;
            }
            double value;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                throw new FormatException(start + "번째 글자에 알 수 없는 값");
            }
            return value;
        }

        private void ReadWord(string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw new FormatException(i + "번째 글자에 알 수 없는 값");
            }
            i += word.Length;
        }

        private void Expect(char c)
        {
            if (Peek() != c)
            {
                throw new FormatException(i + "번째 글자에 '" + c + "' 가 와야 한다");
            }
            i++;
        }

        private char Peek()
        {
            return i < s.Length ? s[i] : '\0';
        }

        private void SkipSpace()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }
    }
}
