using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Jamkkaebi.Npc
{
    /// <summary>미리 만든 대사를 남겨 두는 곳 (NpcRequestManager.LoadStore). 글 한 덩이를 읽고 쓴다.</summary>
    public interface INpcLineStore
    {
        /// <summary>남겨 둔 글. 없으면 null.</summary>
        string Read();

        void Write(string text);
    }

    /// <summary>
    /// 파일 하나에 남긴다. 게임은 Application.persistentDataPath 아래 경로를 준다 (예: npc_lines.txt).
    /// 쓰다가 앱이 꺼져도 앞 파일이 깨지지 않게 임시 파일(.tmp)에 다 쓴 뒤 바꿔 넣는다. 바꿔 넣는 사이에 꺼져 원래 파일이 없으면 임시 파일을 읽고,
    /// 다음에 쓸 때 그 임시 파일을 먼저 원래 파일로 올린 뒤 새로 쓴다 (유일한 저장본을 덮어쓰지 않는다, CodeRabbit 리뷰).
    /// </summary>
    public sealed class NpcLineFileStore : INpcLineStore
    {
        private readonly string path;

        public NpcLineFileStore(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException("path");
            }
            this.path = path;
        }

        public string Path
        {
            get { return path; }
        }

        public string Read()
        {
            string temp = path + ".tmp";
            if (File.Exists(path))
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            return File.Exists(temp) ? File.ReadAllText(temp, Encoding.UTF8) : null;
        }

        public void Write(string text)
        {
            string temp = path + ".tmp";
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            // 앞에서 바꿔 넣다 꺼져 임시 파일만 남았으면 그것이 유일한 저장본이다(Read 가 그것을 읽었다). 새로 쓰다 실패해도 잃지 않게 먼저 원래 파일로 올려 둔다
            if (!File.Exists(path) && File.Exists(temp))
            {
                File.Move(temp, path);
            }
            File.WriteAllText(temp, text ?? "", new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temp, path);
        }
    }

    /// <summary>
    /// 미리 만든 칸 있는 대사(NpcLineCache.ExportTemplates)와 최근 띄운 대사를 글 한 덩이로 바꾸고 되읽는다.
    ///
    /// <para>모양: 첫 줄 "jamkkaebi-npc-lines(탭)1(탭)버전", 그 뒤 한 줄에 하나씩 "T(탭)열쇠(탭)만든 시각(UTC ticks)(탭)행동(탭)대상(탭)메모(탭)대사"(칸 있는 대사),
    /// "R(탭)시나리오(탭)대사"(최근 띄운 대사, 오래된 것부터). 칸은 탭으로 가르고, 글 안의 탭·줄바꿈·역슬래시는 \t·\n·\\ 로 적는다.</para>
    ///
    /// <para><b>버전</b>: 게임이 정한다(VersionOf — 콘텐츠 버전·모델 이름). 읽을 때 버전이 다르면 칸 있는 대사는 버린다 — 페르소나·금칙어·모델이 바뀌면 예전 대사가 맞지 않을 수 있다.
    /// 최근 띄운 대사는 버전과 상관없이 되살린다(반복을 막는 데만 쓴다).</para>
    /// </summary>
    public static class NpcLineStoreFormat
    {
        public const string Header = "jamkkaebi-npc-lines";
        public const string FormatVersion = "1";

        /// <summary>버전 글을 만든다. 콘텐츠 버전(페르소나·행동·기본 대사·금칙어)과 모델 이름 따위를 넘긴다. 하나라도 바뀌면 다른 버전이다.</summary>
        public static string VersionOf(params string[] parts)
        {
            var list = new List<string>();
            foreach (string part in parts ?? new string[0])
            {
                list.Add(part ?? "");
            }
            return string.Join("+", list.ToArray());
        }

        public static string Write(string version, IEnumerable<NpcLineCache.StoredTemplate> templates, IDictionary<string, List<string>> recent)
        {
            var builder = new StringBuilder();
            builder.Append(Header).Append('\t').Append(FormatVersion).Append('\t').Append(Escape(version)).Append('\n');
            if (templates != null)
            {
                foreach (NpcLineCache.StoredTemplate item in templates)
                {
                    if (item == null || item.Result == null || string.IsNullOrEmpty(item.Result.Line))
                    {
                        continue;
                    }
                    builder.Append("T\t").Append(Escape(item.Key)).Append('\t').Append(item.At.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture))
                           .Append('\t').Append(Escape(item.Result.ActionId)).Append('\t').Append(Escape(item.Result.TargetId))
                           .Append('\t').Append(Escape(item.Result.Note)).Append('\t').Append(Escape(item.Result.Line)).Append('\n');
                }
            }
            if (recent != null)
            {
                foreach (KeyValuePair<string, List<string>> pair in recent)
                {
                    foreach (string line in pair.Value ?? new List<string>())
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            builder.Append("R\t").Append(Escape(pair.Key)).Append('\t').Append(Escape(line)).Append('\n');
                        }
                    }
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// 글을 되읽는다. 머리 줄이 없거나 모양이 다르면 false (아무것도 읽지 않는다). 버전이 다르면 versionMatched 가 false 이고 templates 는 비어 있다.
        /// 모양이 깨진 줄은 건너뛴다.
        /// </summary>
        public static bool TryRead(string text, string version, out List<NpcLineCache.StoredTemplate> templates, out Dictionary<string, List<string>> recent,
                                   out bool versionMatched)
        {
            templates = new List<NpcLineCache.StoredTemplate>();
            recent = new Dictionary<string, List<string>>();
            versionMatched = false;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string[] rows = text.Split('\n');
            string[] head = rows[0].TrimEnd('\r').Split('\t');
            if (head.Length < 3 || head[0] != Header || head[1] != FormatVersion)
            {
                return false;
            }
            versionMatched = Unescape(head[2]) == (version ?? "");
            for (int i = 1; i < rows.Length; i++)
            {
                string row = rows[i].TrimEnd('\r');
                if (row.Length == 0)
                {
                    continue;
                }
                string[] cells = row.Split('\t');
                long ticks;
                if (cells[0] == "T" && cells.Length == 7 && versionMatched && long.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)
                        && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
                {
                    string line = Unescape(cells[6]);
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    templates.Add(new NpcLineCache.StoredTemplate
                    {
                        Key = Unescape(cells[1]),
                        At = new DateTime(ticks, DateTimeKind.Utc),
                        Result = ReactionResult.Success("stored", Unescape(cells[3]), Unescape(cells[4]), line, ReactionSource.Slm, NullIfEmpty(Unescape(cells[5])))
                    });
                }
                else if (cells[0] == "R" && cells.Length == 3)
                {
                    string key = Unescape(cells[1]);
                    List<string> lines;
                    if (!recent.TryGetValue(key, out lines))
                    {
                        lines = new List<string>();
                        recent[key] = lines;
                    }
                    lines.Add(Unescape(cells[2]));
                }
            }
            return true;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\\') < 0)
            {
                return value ?? "";
            }
            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }
                char next = value[++i];
                switch (next)
                {
                    case 't': builder.Append('\t'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    default: builder.Append(next); break;
                }
            }
            return builder.ToString();
        }
    }
}
