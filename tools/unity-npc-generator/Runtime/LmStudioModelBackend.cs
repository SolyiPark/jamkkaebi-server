using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jamkkaebi.Npc
{
    /// <summary>LM Studio 백엔드 설정. 기본값은 프롬프트 시험(run.py --backend lmstudio)과 같다.</summary>
    public sealed class LmStudioOptions
    {
        public LmStudioOptions()
        {
            BaseUrl = "http://127.0.0.1:1234";
            Model = "google/gemma-4-e2b";
            Temperature = 0.7;
            MaxTokens = 64;
            UseJsonSchema = true;
            DisableThinking = true;
            SystemInUser = false;
            WarmUp = true;
            HttpTimeout = TimeSpan.FromSeconds(120);
        }

        /// <summary>
        /// LM Studio 서버 주소. 폰에서 PC 의 LM Studio 를 쓰려면 PC 의 내부 IP (예: http://192.168.0.10:1234).
        /// 같은 PC 면 localhost 가 아니라 127.0.0.1 로 쓴다 — Windows 에서 localhost 는 IPv6(::1)부터 붙어 보고, IPv4 에서만 듣는
        /// LM Studio 에 거절당하면 약 2초 뒤에야 IPv4 로 넘어가서 요청마다 2초가 더해진다.
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>모델 이름. LM Studio 의 모델 목록(run.py --list-models)에 나오는 이름 그대로.</summary>
        public string Model { get; set; }

        public double Temperature { get; set; }

        /// <summary>한 번에 생성할 최대 토큰. 넘치면 대사 중간에서 끊기고, 출력 검사기가 끝난 문장까지만 쓴다.</summary>
        public int MaxTokens { get; set; }

        /// <summary>정하면 결과가 재현된다. 비교 시험용.</summary>
        public int? Seed { get; set; }

        /// <summary>prompt.JsonSchema 를 response_format 으로 넘겨 행동·대상을 후보 안으로 묶는다.</summary>
        public bool UseJsonSchema { get; set; }

        /// <summary>
        /// 생각(추론) 모드를 끈다 (reasoning_effort: none). 켜 두면 Gemma 4 가 출력 한도를 생각에 다 쓰고 대사를 비워 돌려준다.
        /// </summary>
        public bool DisableThinking { get; set; }

        /// <summary>
        /// 시스템 메시지를 따로 보내지 않고 사용자 메시지 앞에 붙여 보낸다. LM Studio 의 채팅 틀이 시스템 메시지를 빼먹는 모델에 켠다
        /// (EXAONE 4.0 1.2B: 입력 토큰이 사용자 메시지만큼뿐이었다. run.py 는 --system-in-user auto 로 알아서 정한다).
        /// 글은 시스템 메시지가 앞에 오는 그대로라 미리 읽기(PrepareAsync)도 그대로 듣는다.
        /// </summary>
        public bool SystemInUser { get; set; }

        /// <summary>LoadAsync 에서 짧게 한 번 불러 모델을 메모리에 올려 둔다. 안 하면 첫 요청이 모델 로드 때문에 시간 초과된다.</summary>
        public bool WarmUp { get; set; }

        /// <summary>HTTP 한 번을 기다리는 최대 시간. 게임의 시간 초과는 요청 관리자의 SlmTimeout 이 따로 정한다.</summary>
        public TimeSpan HttpTimeout { get; set; }
    }

    /// <summary>마지막 생성 호출의 측정값. 응답 시간 평가에 쓴다.</summary>
    public sealed class LmStudioCall
    {
        public LmStudioCall(long elapsedMs, int promptTokens, int completionTokens, int reasoningTokens)
        {
            ElapsedMs = elapsedMs;
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            ReasoningTokens = reasoningTokens;
        }

        public long ElapsedMs { get; private set; }
        public int PromptTokens { get; private set; }
        public int CompletionTokens { get; private set; }

        /// <summary>대사 대신 생각에 쓴 토큰. 0 이어야 한다.</summary>
        public int ReasoningTokens { get; private set; }
    }

    /// <summary>
    /// <b>개발용</b> 모델 백엔드. PC 에서 돌아가는 LM Studio 서버(OpenAI 호환 API)에 HTTP 로 붙는다.
    /// Unity 에디터에서 진짜 모델(Gemma 4 E2B)로 상호작용 흐름 전체를 돌려 볼 때 쓴다. 같은 Wi-Fi 의 폰에서 PC 의 LM Studio 를
    /// 쓸 수도 있다. 출시 빌드에는 기기 안에서 도는 런타임(LiteRT-LM, LLMUnity 등)의 백엔드를 쓴다.
    ///
    /// <list type="bullet">
    /// <item>LoadAsync: 서버에 모델이 있는지 확인하고, 짧게 한 번 불러 모델을 메모리에 올린다. 실패하면 예외를 던지고 IsLoaded 는 false 다.</item>
    /// <item>CompleteAsync: 한 번에 하나씩 보낸다(기기의 모델처럼). 모델이 낸 글을 고치지 않고 돌려준다.</item>
    /// <item>PrepareAsync: 시스템 메시지로 1토큰만 생성해 LM Studio(llama.cpp)가 그 앞부분을 기억하게 한다. 같은 줄에 선다.</item>
    /// <item>Unload: 진행 중인 요청을 취소하고 더 받지 않는다. LM Studio 의 모델은 내리지 않는다 (LM Studio 가 관리).</item>
    /// </list>
    /// </summary>
    public sealed class LmStudioModelBackend : INpcModelBackend, IDisposable
    {
        private readonly LmStudioOptions options;
        private readonly HttpClient http;
        private readonly SemaphoreSlim oneAtATime = new SemaphoreSlim(1, 1);
        private readonly object gate = new object();
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private volatile bool loaded;
        private volatile LmStudioCall lastCall;
        private volatile LmStudioCall lastPrepare;

        public LmStudioModelBackend(LmStudioOptions options) : this(options, new HttpClientHandler())
        {
        }

        /// <param name="handler">시험에서 가짜 서버를 끼울 때 쓴다.</param>
        public LmStudioModelBackend(LmStudioOptions options, HttpMessageHandler handler)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            this.options = options;
            http = new HttpClient(handler) { Timeout = options.HttpTimeout };
        }

        public bool IsLoaded
        {
            get { return loaded; }
        }

        /// <summary>마지막 생성 호출의 측정값. 아직 없으면 null.</summary>
        public LmStudioCall LastCall
        {
            get { return lastCall; }
        }

        /// <summary>마지막 미리 읽기 호출의 측정값. 아직 없으면 null.</summary>
        public LmStudioCall LastPrepare
        {
            get { return lastPrepare; }
        }

        public async Task LoadAsync(CancellationToken ct)
        {
            if (loaded)
            {
                return;
            }
            string body = await Send(HttpMethod.Get, "/v1/models", null, ct).ConfigureAwait(false);
            List<string> models = ModelIds(body);
            if (!models.Contains(options.Model))
            {
                throw new InvalidOperationException("LM Studio 에 모델 '" + options.Model + "' 가 없다. 있는 모델: "
                        + (models.Count > 0 ? string.Join(", ", models.ToArray()) : "(없음)"));
            }
            if (options.WarmUp)
            {
                await Chat("짧게 답한다.", "안녕", null, 8, false, ct).ConfigureAwait(false);
            }
            lock (gate)
            {
                if (lifetime.IsCancellationRequested)
                {
                    lifetime = new CancellationTokenSource();
                }
                loaded = true;
            }
        }

        public void Unload()
        {
            lock (gate)
            {
                loaded = false;
                lifetime.Cancel();
            }
        }

        public Task<string> CompleteAsync(NpcPrompt prompt, CancellationToken ct)
        {
            if (prompt == null)
            {
                throw new ArgumentNullException("prompt");
            }
            string schema = options.UseJsonSchema ? prompt.JsonSchema : null;
            return Run(prompt.System, prompt.User, schema, options.MaxTokens, false, ct);
        }

        public Task PrepareAsync(string system, CancellationToken ct)
        {
            if (system == null)
            {
                throw new ArgumentNullException("system");
            }
            return Run(system, NpcPrompt.PrepareUser, null, 1, true, ct);
        }

        private async Task<string> Run(string system, string user, string schema, int maxTokens, bool prepare, CancellationToken ct)
        {
            CancellationToken alive;
            lock (gate)
            {
                if (!loaded)
                {
                    throw new InvalidOperationException("모델이 올라오지 않았다. LoadAsync 를 먼저 부른다.");
                }
                alive = lifetime.Token;
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, alive))
            {
                await oneAtATime.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    return await Chat(system, user, schema, maxTokens, prepare, linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    oneAtATime.Release();
                }
            }
        }

        public void Dispose()
        {
            Unload();
            http.Dispose();
        }

        /// <summary>LM Studio 로 보낼 요청 본문. run.py 의 chat_lmstudio 와 같은 모양이다.</summary>
        internal string ChatBody(string system, string user, string schema, int maxTokens)
        {
            var body = new StringBuilder();
            body.Append("{\"model\":").Append(JsonString(options.Model));
            if (options.SystemInUser)
            {
                string merged = string.IsNullOrEmpty(system) ? (user ?? "") : system + "\n\n" + (user ?? "");
                body.Append(",\"messages\":[{\"role\":\"user\",\"content\":").Append(JsonString(merged)).Append("}]");
            }
            else
            {
                body.Append(",\"messages\":[{\"role\":\"system\",\"content\":").Append(JsonString(system ?? ""));
                body.Append("},{\"role\":\"user\",\"content\":").Append(JsonString(user ?? "")).Append("}]");
            }
            body.Append(",\"stream\":false");
            body.Append(",\"temperature\":").Append(options.Temperature.ToString("R", CultureInfo.InvariantCulture));
            body.Append(",\"max_tokens\":").Append(maxTokens.ToString(CultureInfo.InvariantCulture));
            if (options.Seed.HasValue)
            {
                body.Append(",\"seed\":").Append(options.Seed.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (options.DisableThinking)
            {
                body.Append(",\"reasoning_effort\":\"none\"");
            }
            if (!string.IsNullOrEmpty(schema))
            {
                body.Append(",\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"reaction\",\"strict\":true,\"schema\":")
                    .Append(schema).Append("}}");
            }
            body.Append("}");
            return body.ToString();
        }

        // prepare: 미리 읽기 — 측정값을 LastPrepare 에 남기고, 빈 답도 괜찮다
        private async Task<string> Chat(string system, string user, string schema, int maxTokens, bool prepare, CancellationToken ct)
        {
            Stopwatch watch = Stopwatch.StartNew();
            string body = await Send(HttpMethod.Post, "/v1/chat/completions", ChatBody(system, user, schema, maxTokens), ct)
                    .ConfigureAwait(false);
            watch.Stop();

            Dictionary<string, object> json;
            try
            {
                json = MiniJson.ParseObject(body);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException("LM Studio 응답을 읽지 못했다: " + e.Message);
            }
            string content = Path(json, "choices", 0, "message", "content") as string;
            Dictionary<string, object> usage = Path(json, "usage") as Dictionary<string, object>;
            int reasoning = Number(Path(usage, "completion_tokens_details", "reasoning_tokens"));
            var measured = new LmStudioCall(watch.ElapsedMilliseconds, Number(Path(usage, "prompt_tokens")),
                    Number(Path(usage, "completion_tokens")), reasoning);
            if (prepare)
            {
                lastPrepare = measured;
                return content ?? "";
            }
            lastCall = measured;

            if (string.IsNullOrEmpty(content))
            {
                throw new InvalidOperationException(reasoning > 0
                        ? "대사 없이 생각에 " + reasoning + "토큰을 쓰고 끝났다. DisableThinking 과 모델의 생각 모드 설정을 확인한다"
                        : "LM Studio 가 빈 응답을 돌려주었다");
            }
            return content;
        }

        private async Task<string> Send(HttpMethod method, string path, string body, CancellationToken ct)
        {
            string url = options.BaseUrl.TrimEnd('/') + path;
            using (var request = new HttpRequestMessage(method, url))
            {
                if (body != null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }
                HttpResponseMessage response;
                try
                {
                    response = await http.SendAsync(request, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    // HttpClient 는 자기 시간 초과도 취소 예외로 알린다
                    throw new TimeoutException("LM Studio 가 " + (int)options.HttpTimeout.TotalSeconds + "초 안에 답하지 않았다");
                }
                catch (HttpRequestException e)
                {
                    throw new InvalidOperationException("LM Studio 서버(" + options.BaseUrl + ")에 연결하지 못했다. "
                            + "Developer 탭에서 서버를 켰는지 확인한다 (" + e.GetBaseException().Message + ")", e);
                }
                using (response)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("LM Studio HTTP " + (int)response.StatusCode + ": "
                                + (text.Length > 300 ? text.Substring(0, 300) + "…" : text));
                    }
                    return text;
                }
            }
        }

        private static List<string> ModelIds(string body)
        {
            var ids = new List<string>();
            Dictionary<string, object> json;
            try
            {
                json = MiniJson.ParseObject(body);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException("LM Studio 모델 목록을 읽지 못했다: " + e.Message);
            }
            var data = Path(json, "data") as List<object>;
            if (data != null)
            {
                foreach (object item in data)
                {
                    string id = Path(item, "id") as string;
                    if (id != null)
                    {
                        ids.Add(id);
                    }
                }
            }
            return ids;
        }

        // JSON 안을 키(string)와 순번(int)으로 따라 들어간다. 없으면 null
        private static object Path(object node, params object[] steps)
        {
            foreach (object step in steps)
            {
                if (node == null)
                {
                    return null;
                }
                var key = step as string;
                if (key != null)
                {
                    var map = node as Dictionary<string, object>;
                    object next;
                    node = map != null && map.TryGetValue(key, out next) ? next : null;
                }
                else
                {
                    var list = node as List<object>;
                    int index = (int)step;
                    node = list != null && index < list.Count ? list[index] : null;
                }
            }
            return node;
        }

        private static int Number(object value)
        {
            return value is double ? (int)(double)value : 0;
        }

        private static string JsonString(string value)
        {
            var text = new StringBuilder(value.Length + 2);
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }
                        break;
                }
            }
            text.Append('"');
            return text.ToString();
        }
    }
}
