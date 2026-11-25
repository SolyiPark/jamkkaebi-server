// Unity 없이 Runtime/ 의 코드를 검증한다. Windows 기본 C# 컴파일러(C# 5)로 컴파일한다 — verify.bat 참고.
//
//   verify.exe <unity-npc-generator 폴더> <npc-prompt-test/data 폴더> <기준 프롬프트 json>
//
// 0) 데이터: Data/ 사본이 npc-prompt-test/data 원본과 같은지, 실화 이야기가 3개씩 짝이 맞는지
// 1) 규칙 기반 생성기: 프롬프트 시험의 장면을 그대로 돌려 기대 행동을 고르는지, 규칙·이야기 돌리기·실패 결과가 맞는지
// 2) 공통 금칙어: 걸러야 할 말은 걸리고 정상 대사는 통과하는지, 기본 대사·예시 대사가 모두 통과하는지
// 3) 프롬프트 조립기: run.py 가 만든 프롬프트(시험에 쓴 그것)와 글자 단위로 같은지
// 4) 출력 검사·SLM 생성기: 모델 출력을 JSON·후보 포함·대사 길이·줄바꿈·금칙어로 거르고, 걸리면 Failed 로 돌려주는지
// 5) 요청 관리자: SLM 이 안 되면 기본 대사로 대신하고, 화면 종료·NPC 전환·새 요청으로 밀린 결과는 버리는지
// 6) LM Studio 백엔드: 가짜 서버로 요청 본문·응답 읽기·오류·취소·한 번에 하나씩을 확인
// 7) LLMUnity 백엔드: 가짜 LLMUnity 와 가짜 Unity 메인 스레드로 메서드 4개(올리기·내리기·생성·준비 여부)를 확인
// 8) 실기기 시험 장면 변환(NpcTestScenes): Verify 의 장면 변환과 같은 요청·프롬프트를 만드는지
// 9) 컨텍스트 구성기: 선택지별 후보, 가장 중요한 사실 고르기(우선순위), 사실·기억 고르기, 실화 돌리기, 손님 장면
// 10) 실화 되돌리기: 반응을 띄우지 못한 요청의 실화를 '안 들려줌'으로 되돌리는지 (화면 종료·새 요청으로 밀림)
//
//   verify.exe ... --live [주소] [모델]  → 위 검사 뒤에 실제 LM Studio 로 18장면을 요청 관리자에 통과시켜 본다
//   verify.exe ... --live-store [주소] [모델] [반복]  → 실제 LM Studio 로 미리 만든 대사 파일·쓴 만큼 다시 만들기를 두 판(처음 설치·다시 켬) 돌려 본다
//   verify.exe ... --device [옵션]  → 검사 없이 실기기 시험(NpcDeviceTestRunner)과 같은 흐름을 LM Studio 로 돌리고 CSV 를 쓴다 (DeviceRun.cs)
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Jamkkaebi.Npc;

public static class Program
{
    private const int StoryCount = 3;
    private static int failures;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    public static int Main(string[] args)
    {
        // 파이프로 넘길 때만 UTF-8 로 쓴다. 콘솔에 바로 찍을 때 출력 인코딩을 바꾸면 .NET Framework 에서 예외가 난다
        if (Console.IsOutputRedirected)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        }
        if (args.Length < 3)
        {
            Console.WriteLine("사용법: verify.exe <unity-npc-generator 폴더> <npc-prompt-test/data 폴더> <기준 프롬프트 json>");
            return 2;
        }
        string packageDir = args[0];
        string testDataDir = args[1];
        string expectedPath = args[2];

        var lines = Read<DefaultLineCatalog>(Path.Combine(packageDir, "Data", "default_lines.json"));
        var personas = Read<PersonaCatalog>(Path.Combine(packageDir, "Data", "personas.json"));
        var actions = Read<ActionCatalog>(Path.Combine(packageDir, "Data", "actions.json"));
        var forbidden = Read<ForbiddenCatalog>(Path.Combine(packageDir, "Data", "forbidden.json"));
        var sceneFile = (Dictionary<string, object>)Json.DeserializeObject(ReadText(Path.Combine(testDataDir, "scenes.json")));
        var scenes = ((IEnumerable)sceneFile["scenes"]).Cast<Dictionary<string, object>>().ToList();
        var labels = ((Dictionary<string, object>)sceneFile["labels"]).ToDictionary(p => p.Key, p => (string)p.Value);

        // 실기기 시험을 PC 에서 (DeviceRun.cs): 검사는 건너뛰고 바로 돌린다
        if (args.Length > 3 && args[3] == "--device")
        {
            return DeviceRun.Run(args.Skip(4).ToArray(), ReadText(Path.Combine(testDataDir, "scenes.json")), personas, actions, lines, forbidden);
        }

        CheckData(packageDir, testDataDir, lines, personas);
        CheckGenerator(lines, scenes);
        CheckStories(lines);
        CheckFilter(forbidden, lines, personas);
        CheckPrompts(personas, actions, scenes, labels, expectedPath);
        CheckOutput(forbidden, personas, actions, lines, scenes);
        CheckManager(forbidden, personas, actions, lines);
        CheckEnforcement(forbidden, personas, actions, lines);
        CheckDeviceFindings(forbidden, personas, actions, lines, scenes, ReadText(Path.Combine(packageDir, "Data", "personas.json")));
        CheckServerContext(forbidden, personas, actions, lines);
        CheckQuality(forbidden, personas, actions, lines);
        CheckRound3(forbidden, personas, actions, lines);
        CheckLatency(forbidden, personas, actions, lines, scenes, labels);
        CheckRound5(forbidden, personas, actions, lines);
        CheckRound6(forbidden, personas, actions, lines);
        CheckPrefetch(forbidden, personas, actions, lines);
        CheckSpeechFix(expectedPath);
        CheckLmStudio(forbidden, personas, actions, lines);
        CheckLlmUnity(forbidden, personas, actions, lines);
        CheckTestScenes(ReadText(Path.Combine(testDataDir, "scenes.json")), scenes, personas, actions, labels);
        CheckContextBuilder(personas, actions, lines, forbidden);
        CheckStoryReturn(personas, actions, lines, forbidden);
        CheckReviewCases(forbidden, personas, actions, lines);
        if (args.Length > 3 && args[3] == "--live-store")
        {
            int liveRepeat;
            CheckLiveStore(forbidden, personas, actions, lines, scenes, labels,
                    args.Length > 4 ? args[4] : "http://127.0.0.1:1234", args.Length > 5 ? args[5] : "kanana-1.5-2.1b-instruct-2505",
                    args.Length > 6 && int.TryParse(args[6], out liveRepeat) ? Math.Max(1, liveRepeat) : 3);
        }
        else if (args.Length > 3 && args[3] == "--live")
        {
            CheckLive(forbidden, personas, actions, lines, scenes, labels,
                    args.Length > 4 ? args[4] : "http://127.0.0.1:1234", args.Length > 5 ? args[5] : "google/gemma-4-e2b");
        }

        Console.WriteLine(failures == 0 ? "모두 통과" : "실패 " + failures + "건");
        return failures == 0 ? 0 : 1;
    }

    // ---------- 지난 대화: 꺼냈음 표시(T/F)·오래된 것부터·꺼낸 뒤 한 번 쉬기 (2026-11-24) ----------
    // 예전에는 꺼낸 기억을 표시하지 않아 늘 가장 최근 기억만 꺼냈고, 같은 것을 다시 고르지 않으려고 꺼낸 뒤에는 새 이야기가 올 때까지 꺼내지 않았다
    private static void CheckRecallQueue(NpcContextBuilder builder)
    {
        NpcResponse shown = Show();
        var older = new MemoryEntry(6, NpcActions.TellStory, "주인에게 해외 소장 이야기를 들려줬다");
        var newer = new MemoryEntry(3, NpcActions.TellStory, "주인에게 옻칠 자개 이야기를 들려줬다");
        var memory = new List<MemoryEntry> { older, newer };   // 게임은 새 기억을 뒤에 붙인다

        // 1) 가장 오래된 기억부터
        NpcContext first = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, memory.ToArray()));
        Check(first.KeyFact == "recall" && first.Request.Memory.Count == 1 && first.Request.Memory[0] == older && first.MemoryToMark == older
                && first.Request.Situation.Contains("6일 전 주인에게 해외 소장"), "지난 대화: 아직 안 꺼낸 것 중 가장 오래된 기억부터 ← " + first.Request.Situation);
        Check(!older.Recalled, "띄우기 전에는 꺼냈음을 표시하지 않음");
        MemoryEntry talked = first.Finish(shown);
        Check(older.Recalled && !newer.Recalled && talked != null && talked.ActionId == NpcActions.RecallTalk && !talked.Recalled,
                "띄우면 꺼낸 기억에 꺼냈음(T) 표시, 지난 대화를 꺼냈다는 기억을 남김");
        memory.Add(new MemoryEntry(0, talked.ActionId, talked.Summary));

        // 2) 꺼낸 다음 안부는 평소대로 (선명도·관리 이야기), 띄운 뒤 쉰 것을 표시
        NpcContext rest = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, memory.ToArray()));
        Check(rest.KeyFact == "none" && Acts(rest) == "answer_status" && rest.Request.Memory.Count == 0 && FactsOf(rest).Contains("clarity=선명")
                && rest.MemoryToMark == memory[2] && rest.MemoryToSave == null, "지난 대화를 꺼낸 다음 안부는 평소 안부, 쉰 것을 표시할 기억은 그 recall_talk ← " + FactsOf(rest));
        Check(rest.Finish(shown) == null && memory[2].Recalled, "평소 안부를 띄우면 recall_talk 에 쉬었음(T) 표시, 남길 기억 없음");

        // 3) 그다음 안부는 다음 기억 (먼저 꺼낸 것은 다시 꺼내지 않음)
        NpcContext second = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, memory.ToArray()));
        Check(second.KeyFact == "recall" && second.Request.Memory[0] == newer && second.MemoryToMark == newer, "쉰 뒤에는 아직 안 꺼낸 다음 기억 ← " + second.Request.Situation);
        memory.Add(new MemoryEntry(0, second.Finish(shown).ActionId, "주인과 지난 일을 다시 이야기했다"));
        builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, memory.ToArray())).Finish(shown);
        Check(builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, memory.ToArray())).KeyFact == "none", "모두 꺼냈으면 평소 안부");

        // 4) 띄우지 못하면 표시하지 않는다 — 다음 안부에 같은 기억을 다시 꺼낸다
        var unshown = new MemoryEntry(2, NpcActions.TellStory, "주인에게 암행어사 이야기를 들려줬다");
        NpcContext dropped = builder.Build(Owner(8, NpcInputs.AskStatus, "CLEAR", 5, unshown));
        Check(dropped.Finish(Dropped()) == null && !unshown.Recalled && dropped.MemoryToMark == null
                && builder.Build(Owner(8, NpcInputs.AskStatus, "CLEAR", 5, unshown)).MemoryToMark == unshown,
                "못 띄운 지난 대화는 표시하지 않고(MemoryToMark 도 비움 — 게임이 저장하지 않게) 다음에 다시 꺼냄");
        Check(dropped.Finish(shown) == null && !unshown.Recalled, "Finish 는 한 번만 — 뒤늦게 '띄움'을 줘도 표시하지 않음");
        // 꺼낸 뒤 이야기를 들려줘(가장 최근 기억이 이야기) 다음 안부도 쉰다. 표시 없이 저장된 옛 recall_talk 가 여럿이어도 한 번만 쉰다
        var recalledStory = new MemoryEntry(5, NpcActions.TellStory, "주인에게 해외 소장 이야기를 들려줬다") { Recalled = true };
        var oldTalk = new MemoryEntry(4, NpcActions.RecallTalk, "주인과 지난 일을 다시 이야기했다");
        var lastTalk = new MemoryEntry(0, NpcActions.RecallTalk, "주인과 지난 일을 다시 이야기했다");
        var toldAfter = new MemoryEntry(0, NpcActions.TellStory, "주인에게 옻칠 자개 이야기를 들려줬다");
        NpcContext afterStory = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, recalledStory, oldTalk, lastTalk, toldAfter));
        Check(afterStory.KeyFact == "none" && afterStory.MemoryToMark == lastTalk, "꺼낸 뒤 이야기를 들려줘도 다음 안부는 평소대로 쉼 ← " + afterStory.KeyFact);
        afterStory.Finish(shown);
        NpcContext nextRecall = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, recalledStory, oldTalk, lastTalk, toldAfter));
        Check(lastTalk.Recalled && !oldTalk.Recalled && nextRecall.KeyFact == "recall" && nextRecall.MemoryToMark == toldAfter,
                "쉰 뒤에는 새로 들려준 이야기를 꺼냄 (옛 recall_talk 때문에 또 쉬지 않음)");

        // 5) 흐려짐·최근 관리 안부도 쉰 것으로 친다. 탭 인사는 기억을 건드리지 않는다
        var pendingFaded = new MemoryEntry(0, NpcActions.RecallTalk, "주인과 지난 일을 다시 이야기했다");
        NpcContext faded = builder.Build(Owner(6, NpcInputs.AskStatus, "FADED", 9, new MemoryEntry(4, NpcActions.TellStory, "주인에게 해외 소장 이야기를 들려줬다"), pendingFaded));
        faded.Finish(shown);
        Check(faded.KeyFact == "faded" && pendingFaded.Recalled, "흐려짐 안부도 꺼낸 뒤 쉰 것으로 표시");
        var pendingCare = new MemoryEntry(0, NpcActions.RecallTalk, "주인과 지난 일을 다시 이야기했다");
        NpcContext care = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 1, pendingCare));
        care.Finish(shown);
        Check(care.KeyFact == "recent_care" && pendingCare.Recalled, "최근 관리 안부도 꺼낸 뒤 쉰 것으로 표시");
        var untouched = new MemoryEntry(0, NpcActions.RecallTalk, "주인과 지난 일을 다시 이야기했다");
        NpcContext tap = builder.Build(Owner(6, NpcInputs.Tap, "CLEAR", 5, untouched));
        tap.Finish(shown);
        Check(tap.MemoryToMark == null && !untouched.Recalled, "탭 인사는 꺼냈음 표시를 바꾸지 않음");

        // 6) 같은 날 기억은 먼저 붙인 것부터
        var morning = new MemoryEntry(1, NpcActions.TellStory, "주인에게 아침 이야기를 들려줬다");
        var evening = new MemoryEntry(1, NpcActions.TellStory, "주인에게 저녁 이야기를 들려줬다");
        Check(builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, morning, evening)).MemoryToMark == morning, "같은 날 기억은 먼저 들어온 것부터");

        // 7) 미리 만들기: 지난 대화 안부와 그다음 평소 안부를 함께 만든다
        var waiting = new MemoryEntry(3, NpcActions.TellStory, "주인에게 옻칠 자개 이야기를 들려줬다");
        List<ReactionRequest> asks = builder.PrefetchTemplates(Owner(6, NpcInputs.Tap, "CLEAR", 5, waiting)).Where(r => r.Input == NpcInputs.AskStatus).ToList();
        Check(asks.Count == 2 && asks[0].Candidates[0].ActionId == NpcActions.RecallTalk && asks[1].Candidates[0].ActionId == NpcActions.AnswerStatus
                && asks[1].Memory.Count == 0 && !waiting.Recalled,
                "지난 대화를 꺼낼 차례면 그다음 평소 안부도 미리 만듦 ← " + string.Join(", ", asks.Select(r => r.Candidates[0].ActionId).ToArray()));
        Check(builder.PrefetchTemplates(Owner(6, NpcInputs.Tap, "CLEAR", 5)).Count(r => r.Input == NpcInputs.AskStatus) == 1, "기억이 없으면 안부는 하나");
    }

    // ---------- CodeRabbit 리뷰 사례 (2026-11-23 다시 점검) ----------
    // 리뷰에 적힌 입력을 그대로 넣어 본다. 이미 고친 것도 다시 나빠지지 않게 남겨 둔다
    private static void CheckReviewCases(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== CodeRabbit 리뷰 사례 ===");
        // '입니다'는 앞 낱말의 받침에 따라 '야'·'이야'
        Check(NpcLineChecks.NormalizeSpeech("나는 친구입니다.", "반말", "밤바다") == "나는 친구야.", "받침 없는 낱말 + 입니다 → 야 ← " + NpcLineChecks.NormalizeSpeech("나는 친구입니다.", "반말", "밤바다"));
        Check(NpcLineChecks.NormalizeSpeech("여기가 내 집입니다.", "반말", "밤바다") == "여기가 내 집이야.", "받침 있는 낱말 + 입니다 → 이야 ← " + NpcLineChecks.NormalizeSpeech("여기가 내 집입니다.", "반말", "밤바다"));
        // '손님'을 '친구'로 바꿀 때 붙은 조사도 바꾼다
        Check(NpcLineChecks.GuestToFriend("손님이 왔어") == "친구가 왔어" && NpcLineChecks.GuestToFriend("손님은 반가워") == "친구는 반가워"
                && NpcLineChecks.GuestToFriend("손님을 기다렸어") == "친구를 기다렸어",
                "손님이·은·을 → 친구가·는·를 ← " + NpcLineChecks.GuestToFriend("손님이 왔어") + " / " + NpcLineChecks.GuestToFriend("손님은 반가워") + " / " + NpcLineChecks.GuestToFriend("손님을 기다렸어"));
        // 처음 온 친구에게 "다음에 또 와"는 초대다 — 전에 왔다는 말만 어긋난다
        Check(NpcLineChecks.HistoryContradiction("반가워! 다음에 또 와.", 1) == null && NpcLineChecks.HistoryContradiction("또 왔네, 반가워!", 1) != null,
                "첫 방문: '또 와'(초대)는 통과, '또 왔네'는 어긋남");
        // '다시는'의 '시'는 높임이 아니다
        string again = NpcLineChecks.NormalizeSpeech("다시는 안 늦을게. 같이 놀자!", "반말", "밤바다");
        Check(again.StartsWith("다시는", StringComparison.Ordinal), "'다시는'의 '시'를 지우지 않음 ← " + again);
        // 닉네임이 조사로 끝나 보여도('다은') 부르는 말이면 끊긴 문장이 아니다
        Check(NpcLineChecks.IncompleteEnding("어서 와, 다은.", "다은") == null && NpcLineChecks.IncompleteEnding("내 꼬리를", "다은") != null,
                "끝의 부르는 말('다은')은 끊긴 조사로 보지 않고, '내 꼬리를'은 끊긴 문장");
        // '오전'도 시간대 낱말이다
        Check(NpcLineChecks.TimeContradiction("지금은 오전이야, 반가워!", "밤") != null && NpcLineChecks.TimeContradiction("지금은 오전이야, 반가워!", "오전") == null,
                "밤에 '오전'은 어긋나고 오전에는 통과");
        Check(NpcLineSlots.Slotify("오전에 와 줬구나.", new SlotValues { TimeWord = "오전" }) != null, "견본 시간대가 오전이면 '오전에'를 칸으로 보관");
        // "닦아 줄까?"는 내가 닦아 주겠다는 말이라 부탁이 아니다
        Check(!NpcLineChecks.HasAsk("몸이 흐려졌어. 살살 닦아 줄까?") && !NpcLineChecks.IsCleanAsk("살살 닦아 줄까?") && NpcLineChecks.HasAsk("몸이 흐려졌어. 살살 닦아 줄래?"),
                "'닦아 줄까?'는 부탁으로 보지 않고 '닦아 줄래?'는 부탁");
        // 보관함에 없는 열쇠를 읽기만 해서는 칸이 생기지 않는다
        var missCache = new NpcLineCache();
        ReactionRequest missing = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        missCache.TryTake(missing, null);
        missCache.TryTakeTemplate(missing, null);
        missCache.Count(missing);
        missCache.Count(missing, new[] { "왔구나!" });
        missCache.UsableTemplateCount(missing, 40);
        missCache.UsableTemplateCount(missing, 40, new[] { "왔구나!" });
        Check(missCache.BucketCount == 0, "없는 열쇠를 읽고 세기만 해서는 보관함 칸이 생기지 않음 ← " + missCache.BucketCount);

        // 칸 있는 대사의 열쇠는 방문 횟수·방문한 날을 빼서, 처음 온 친구에게 만든 말이 단골에게 갈 수 있다 — 꺼낼 때 방문 기록으로 다시 본다
        Func<int, string, ReactionRequest> visitor = (count, days) =>
        {
            ReactionRequest v = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
            v.Situation = "친구가 놀러 왔다.";
            v.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, count.ToString()));
            v.Facts.Add(new Fact(NpcFactKeys.VisitDays, days));
            return v;
        };
        ReactionRequest firstVisit = visitor(1, "오늘");
        ReactionRequest regular = visitor(7, "오늘, 3일 전, 5일 전");
        var visitCache = new NpcLineCache();
        Check(NpcLineCache.TemplateKeyOf(firstVisit) == NpcLineCache.TemplateKeyOf(regular), "처음 온 친구와 단골의 손님 관람은 칸 있는 대사 열쇠가 같음 (방문 기록은 열쇠에서 뺀다)");
        visitCache.AddTemplate(firstVisit, ReactionResult.Success("v1", NpcActions.WanderRecall, NpcTargets.Player, "처음 와 줬구나, 반가워!", ReactionSource.Slm, null));
        visitCache.AddTemplate(regular, ReactionResult.Success("v2", NpcActions.WanderRecall, NpcTargets.Player, "오랜만이야, 반가워!", ReactionSource.Slm, null));
        Check(visitCache.TryTakeTemplate(regular, null) == null && visitCache.TemplateCount(regular) == 2,
                "단골에게 '처음 와 줬구나', 3일 전에도 온 친구에게 '오랜만'은 꺼내지 않고 다른 요청을 위해 둠");
        ReactionResult toFirst = visitCache.TryTakeTemplate(firstVisit, null);
        Check(toFirst != null && toFirst.Line == "처음 와 줬구나, 반가워!", "처음 온 친구에게는 '처음 와 줬구나'를 꺼냄 ← " + (toFirst != null ? toFirst.Line : "(없음)"));
    }

    // ---------- 데이터 ----------

    private static void CheckData(string packageDir, string testDataDir, DefaultLineCatalog lines, PersonaCatalog personas)
    {
        Console.WriteLine("=== 데이터 ===");
        foreach (string name in new[] { "personas.json", "actions.json", "forbidden.json" })
        {
            string copy = ReadText(Path.Combine(packageDir, "Data", name)).Replace("\r\n", "\n");
            string original = ReadText(Path.Combine(testDataDir, name)).Replace("\r\n", "\n");
            Check(copy == original, "Data/" + name + " 이 npc-prompt-test/data 원본과 같음 (다르면 원본을 복사한다)");
        }
        foreach (SpiritLines spirit in lines.spirits)
        {
            Persona persona = personas.Find(spirit.relicId);
            Check(persona != null, spirit.name + " 페르소나 있음");
            if (persona == null)
            {
                continue;
            }
            Check(persona.stories.Count == StoryCount, spirit.name + " 페르소나 실화 " + StoryCount + "개 ← " + persona.stories.Count);
            Check(spirit.stories.Count == persona.stories.Count, spirit.name + " 이야기 대사 수가 페르소나 실화 수와 같음");
            foreach (StoryPiece piece in persona.stories)
            {
                Check(!string.IsNullOrEmpty(piece.topic) && !string.IsNullOrEmpty(piece.fact), spirit.name + " 실화에 topic·fact 있음");
                Check(piece.sources != null && piece.sources.Count > 0, spirit.name + " 실화 '" + piece.topic + "' 에 확인한 출처가 있음");
            }
            foreach (string story in spirit.stories)
            {
                Check(!string.IsNullOrEmpty(story) && story.Length <= 70, spirit.name + " 이야기 대사 70자 이하 ← " + story);
            }
            Check(spirit.FindLines(NpcActions.TellStory, "") == null, spirit.name + " 이야기는 stories 로만 (tell_story 풀 없음)");
        }
        Check(lines.fallback.stories == null || lines.fallback.stories.Count == 0, "기본(fallback)은 자료 문장으로 이야기");
    }

    // ---------- 규칙 기반 생성기 ----------

    private static void CheckGenerator(DefaultLineCatalog catalog, List<Dictionary<string, object>> scenes)
    {
        Console.WriteLine("=== 규칙 기반 생성기: 장면 ===");
        Check(catalog.spirits.Count == 5 && catalog.fallback != null, "기본 대사 읽기 (도깨비 5 + 기본)");
        var generator = new RuleBasedReactionGenerator(catalog, new Random(7));

        foreach (Dictionary<string, object> scene in scenes)
        {
            string id = (string)scene["id"];
            ReactionRequest request = ToRequest(scene);
            List<string> expected = ((IEnumerable)scene["expected"]).Cast<string>().ToList();
            string previous = null;
            bool repeated = false;
            // 끌려갈 때는 대사를 띄우지 않는다 — 행동만 고르고 대사는 빈 글이다
            bool silent = NpcActions.OnlySilent(request.Candidates);
            ReactionResult first = null;
            for (int i = 0; i < 6; i++)
            {
                ReactionResult result = generator.Generate(request, CancellationToken.None);
                if (first == null)
                {
                    first = result;
                }
                Check(result.IsSuccess, id + " 성공");
                Check(result.RequestId == request.RequestId, id + " 요청 ID 그대로");
                Check(expected.Contains(result.ActionId), id + " 기대 행동 " + string.Join("/", expected.ToArray()) + " ← " + result.ActionId);
                Check(result.TargetId == NpcTargets.Player, id + " 대상 player");
                Check(silent ? result.Line == "" : !string.IsNullOrEmpty(result.Line), id + (silent ? " 끌려갈 때는 대사 없음" : " 대사 있음"));
                repeated |= !silent && previous != null && previous == result.Line;
                previous = result.Line;
            }
            // 게임이 번호를 정한 이야기는 늘 같은 대사다
            Check(request.FindFact(NpcFactKeys.StoryIndex) != null || !repeated, id + " 바로 앞 대사 반복 없음");
            Console.WriteLine("{0} {1,-16} {2}", id, first.ActionId, first.Line);
        }

        Console.WriteLine("=== 규칙 기반 생성기: 규칙 ===");
        Check(Line(generator, Status(5, "FADED", null)).Contains("닦아"), "흐려짐(FADED)이면 관리 부탁");
        Check(Line(generator, Status(5, "RESEALED", null)).Contains("닦아"), "재봉인(RESEALED)이면 관리 부탁");
        string recent = Line(generator, Status(5, "CLEAR", "2"));
        Check(recent.Contains("지난번") || recent.Contains("요전"), "이틀 전 관리면 그 일을 떠올림 ← " + recent);
        string plain = Line(generator, Status(5, "CLEAR", "9"));
        Check(!plain.Contains("닦아") && !plain.Contains("지난번") && !plain.Contains("요전"), "오래전 관리면 평소 상태 ← " + plain);

        var visit = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet, NpcActions.WanderRecall);
        visit.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "1"));
        Check(generator.Generate(visit, CancellationToken.None).ActionId == NpcActions.WanderGreet, "1번째 손님은 인사");
        visit.Facts[0].Value = "2";
        Check(generator.Generate(visit, CancellationToken.None).ActionId == NpcActions.WanderRecall, "2번째 손님은 단골 알아보기");

        Check(Pick(generator, 9, NpcInputs.Care, NpcActions.ThankJoyful, NpcActions.ThankCalm) == NpcActions.ThankJoyful, "묘작이는 기쁜 감사");
        Check(Pick(generator, 6, NpcInputs.Care, NpcActions.ThankJoyful, NpcActions.ThankCalm) == NpcActions.ThankCalm, "밤바다는 담담한 감사");
        Check(Pick(generator, 2, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay) == NpcActions.StrugglePlay, "향산이는 놀이처럼 버둥");

        Check(Line(generator, Request(1, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet)).Length > 0, "페르소나 없는 도깨비(1번)도 인사");

        Check(generator.Generate(null, CancellationToken.None).FailReason == FailReason.Error, "요청이 없으면 Error");
        Check(generator.Generate(Request(8, InteractionMode.Owner, NpcInputs.Tap), CancellationToken.None).FailReason
                == FailReason.NoCandidate, "후보가 없으면 NoCandidate");
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check(generator.Generate(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet), cancelled.Token).FailReason
                == FailReason.Cancelled, "취소되면 Cancelled");
        Check(generator.GenerateAsync(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet), CancellationToken.None)
                .Result.IsSuccess, "GenerateAsync 도 같은 결과");
    }

    // ---------- 실화 이야기 ----------

    private static void CheckStories(DefaultLineCatalog catalog)
    {
        Console.WriteLine("=== 실화 이야기 ===");
        var generator = new RuleBasedReactionGenerator(catalog, new Random(7));
        foreach (SpiritLines spirit in catalog.spirits)
        {
            // 번호 없이 거듭 들으면 세 이야기를 한 번씩 다 들려준 뒤에야 처음 것으로 돌아온다
            var heard = new List<string>();
            for (int i = 0; i < StoryCount + 1; i++)
            {
                heard.Add(Line(generator, Request(spirit.relicId, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory)));
            }
            Check(heard.Take(StoryCount).Distinct().Count() == StoryCount, spirit.name + " 처음 세 번은 모두 다른 실화");
            Check(heard.Take(StoryCount).All(spirit.stories.Contains), spirit.name + " 이야기 대사는 stories 에서");
            Check(heard[StoryCount] == heard[0], spirit.name + " 네 번째는 가장 오래전 이야기로 돌아옴");
            Console.WriteLine("{0}: {1}", spirit.name, string.Join(" / ", heard.Take(StoryCount).ToArray()));
        }

        // 게임이 정한 번호(SLM 에 준 실화)를 따른다 — SLM 이 실패해도 같은 이야기가 나온다
        SpiritLines night = catalog.Find(6);
        var indexed = Request(6, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory);
        indexed.Facts.Add(new Fact(NpcFactKeys.VerifiedStory, "고려 나전칠기 상당수가 해외에 소장돼 있다"));
        indexed.Facts.Add(new Fact(NpcFactKeys.StoryIndex, "2"));
        Check(Line(generator, indexed) == night.stories[2], "story_index 2 면 세 번째 이야기 대사");
        indexed.Facts[1].Value = "9";
        Check(night.stories.Contains(Line(generator, indexed)), "번호가 범위 밖이면 돌려 가며 고름");

        // 페르소나 없는 도깨비는 기본 대사 풀 그대로 — 자료 문장을 바꾸지 않고 싣는다
        const string fact = "1921년 경주 시가지 공사 중 우연히 발견됐다";
        var plain = Request(3, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory);
        plain.Facts.Add(new Fact(NpcFactKeys.VerifiedStory, fact));
        string plainLine = Line(generator, plain);
        Check(plainLine == fact + "." + catalog.fallback.storyTail, "페르소나 없는 도깨비는 자료 문장 + 기본 말투 ← " + plainLine);
        Check(Line(generator, Request(3, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory))
                == catalog.fallback.FindLines(NpcActions.TellStory, "")[0], "자료도 없으면 기본 이야기 대사");

        // 돌리기 기록
        var rotation = new StoryRotation();
        Check(rotation.Next(8, 0) == -1, "이야기가 없으면 -1");
        rotation.Restore(8, new[] { 2, 0 });
        Check(rotation.Next(8, 3) == 1, "저장된 기록 [2,0] 이면 아직 안 한 1번");
        Check(rotation.Next(8, 3) == 2, "다 들려줬으면 가장 오래전 2번");
        Check(string.Join(",", rotation.History(8).Select(i => i.ToString()).ToArray()) == "0,1,2", "기록은 오래된 것부터 [0,1,2]");
        rotation.Restore(8, new[] { 0, 0, -1, 5 });
        Check(rotation.Next(8, 3) == 1, "겹치거나 음수인 기록은 버리고, 범위 밖 번호는 건너뜀");

        Check(new StoryPiece { topic = "암행어사" }.MemorySummary() == "주인에게 암행어사 이야기를 들려줬다", "기억 요약 모양이 장면과 같음");
    }

    // ---------- 공통 금칙어 ----------

    private static void CheckFilter(ForbiddenCatalog catalog, DefaultLineCatalog lines, PersonaCatalog personas)
    {
        Console.WriteLine("=== 공통 금칙어 ===");
        var filter = new NpcLineFilter(catalog);
        Check(catalog.groups.Count == 12, "금칙어 묶음 12개 (욕설·비하·밀어내기·퉁명·위협·낮춰 부르기·자기소개·지시문 노출·상황 설명문·작별 인사·날씨·어색한 말)");
        string[] blocked =
        {
            "……지문 남는다. 놓아.", "……손대지 마.", "앗, 놔줘~ 나 바쁘단 말이야!", "어흠! 내려놓아라.", "놓으라고!",
            "닥쳐, 귀찮아.", "저리 가.", "관심 없어.", "쳇, 됐거든.", "바보 같아.", "시끄러워, 저리 비켜.", "씨발", "개 새 끼",
            // 정상 문장에도 나오는 말은 patterns 로 욕으로 쓰인 꼴만 찾는다
            "시발", "씨바 진짜", "닥쳐!", "입 닥쳐", "꺼져!", "당장 꺼져", "미친놈", "너 미쳤어?", "미쳤냐?", "이 새끼야",
            "저 새끼.", "졸라 짜증나", "등신 같은", "니미 뭐야", "염병하네", "죽여버린다", "너 죽여", "다 죽인다",
            // 낮춰 부르기 (반말 도깨비가 주인을 이렇게 불러 거만하게 들렸다)
            "주인아, 이리 와 줘!", "주인아~ 놀자", "손님아.", "놀자, 주인아",
            // 주인님·주인·손님 호칭은 어떤 꼴로도 금지 (모든 도깨비가 반말이라 호칭 없이 말한다)
            "주인님, 놀자!", "손님, 천천히 보세요.", "나는 잠깨비 도깨비야", "어서 왔어", "닦아 달라고 부탁해", "가까이 다가와도 돼", "쯧, 내려줘라", "얼른 비켜라.", "최근 이 상대와 있었던 일을 말해 줘", "손 떼 줘, 긁힐라", "흥미로운 손님이구려"
        };
        foreach (string line in blocked)
        {
            Check(filter.Find(line) != null, "걸러야 함: " + line);
        }
        string[] allowed =
        {
            "새끼 참새가 날아간다!", "심심해 죽겠어~", "앗, 참새를 놓쳤어!", "여기 놓으면 볕이 잘 들겠다!", "바람 쐬러 나가자",
            "이리저리 구경하게", "어서 와", "닦아 줘", "조심해 줘, 부탁해", "바라는 건 없어.", "어흠, 살살 내려 주시오.", "…지문 남잖아. 살살 들어 줘.", "",
            // 욕과 글자가 겹치지만 정상인 문장 (실화·문화재 이야기에 나올 만한 꼴)
            "영향을 미친 유물이야", "큰 영향을 미쳤다네", "위기가 닥치는 날", "어려움이 닥쳐도 괜찮아", "불이 꺼져 있구먼",
            "등신불처럼 앉아 있구려", "이곳이 시발점이었지", "꽃씨 바구니", "할머니 미소가 따뜻해", "이 새끼 참새 좀 봐!",
            "저 새끼 고양이", "아직 새끼야!", "전염병이 돌던 때에도 버텼다네", "숨죽여 기다렸어",
            "숨을 죽여 봐", "목소리를 죽여서 말해 주오", "발소리를 죽여 다가왔지", "숨죽인다네", "기척을 죽여 숨었지"
        };
        foreach (string line in allowed)
        {
            Check(filter.IsAllowed(line), "통과해야 함: " + line + " ← " + filter.Find(line));
        }
        Check(filter.FindAll("닥쳐, 귀찮아.").Count == 2, "FindAll 은 걸린 말을 모두 (닥쳐, 귀찮)");
        // 2026-11-16 시험: 서술체("~떠올렸다.")와 맞이하는 상황의 작별 인사("잘 다녀와~")는 거른다
        Check(filter.Find("네 안부에 반가워하며, 사흘 전 자개 얘기를 떠올렸다.") != null, "서술체 '떠올렸다.' 는 걸러야 함");
        Check(filter.Find("오늘도 잘 다녀와~ 친구야, 다음엔 뭐 볼 거야?") != null && filter.Find("안녕히 가, 잘 가!") != null, "맞이하는 상황의 작별 인사는 걸러야 함");
        Check(filter.IsAllowed("또 왔네, 반가워!") && filter.IsAllowed("다시 와 줘서 고마워.") && filter.IsAllowed("몸이 가벼워졌어."), "작별·서술체 묶음이 정상 대사를 걸러 내지 않음");
        // 2026-11-16 20:58 시험 S01 #3: "찾아와서 반갑게 인사하며 '오늘은 특별히 더 반가워" 같은 지문이 끊긴 채 대사로 나갔다
        Check(filter.Find("찾아와서 반갑게 인사하며 '오늘은 특별히 더 반가워") != null && filter.Find("반갑게 인사하며, 잠깨비가 깨어나겠네!라고 말했다.") != null,
                "'인사하며 \'…' 같은 지문은 걸러야 함");
        Check(filter.IsAllowed("반가워, 인사하러 왔어!") && filter.IsAllowed("와, 아침에 날 불러줘서 고마워."), "지문 묶음이 정상 인사를 걸러 내지 않음");
        Check(NpcOutputValidator.FitSentences("찾아와서 반갑게 인사하며 '오늘은 특별히 더 반가워, 잠깨비가 깨어나겠네", 25, true) == null,
                "따옴표가 열린 채 끊긴 마디는 쉼표 앞이라도 쓰지 않음");
        // 2026-11-16 시험: 인사·관람 말풍선 길이 목표는 15자 (모델이 20자 목표에 중앙값 25자를 써서 응답이 2초를 넘었다)
        Check(new PromptOptions().LineChars[NpcInputs.Tap] == 15 && new PromptOptions().LineChars[NpcInputs.Visit] == 15 && new PromptOptions().DefaultLineChars == 16,
                "탭 인사·손님 관람은 15자, 그 밖은 16자 (출력 토큰이 곧 응답 시간)");
        // 2026-11-16 시험 S02 #2: 없는 선물 약속("따뜻한 우유 줄게?")을 지어낸 것은 거른다
        Check(filter.Find("찾아와서 반가워! 오늘 밤엔 따뜻한 우유 줄게?") != null && filter.IsAllowed("찾아와서 반가워! 오늘 밤에는 참새랑 술래잡기 할래?"), "없는 선물 약속은 걸러야 함, 놀자는 말은 통과");
        // 2026-11-17 시험(64건)에서 나온 어색한 말: 부른 까닭을 캐묻기·없는 사정 지어내기·손님에게 시키기·닦아 준 일과 부탁이 뒤섞인 말
        string[] awkwardNow =
        {
            "안녕, 오늘 아침부터 왜 부르는지 궁금하네.", "반가워! 오늘 밤은 너무 재미있었어.", "반가워! 오늘 하루는 평온할 것 같지?",
            "반가워, 궁금한 점이 많겠네.", "전시관이 너무 재밌나 봐!", "안녕, 처음 온 거야? 전시관 구경 잘 돼?", "오늘도 반갑게 인사해.",
            "점점 더 흐려지는 것 같아 닦아 줘서 다시 깨워주면 좋겠어.", "반가워! 오늘 아침에 나를 깨우러 왔군."
        };
        foreach (string line in awkwardNow)
        {
            Check(filter.Find(line) != null, "어색한 말은 걸러야 함: " + line);
        }
        // 2026-11-17 19:43 시험: 전시관을 구경하는 방문자처럼 말하거나 프롬프트·상황의 설명이 대사로 나온 것 ("할 일이 많아서"는 프롬프트의 '할 일' 줄을 옮겼다)
        foreach (string line in new[] { "와! 반가워서 뛰어나왔어.", "나도 전시관 구경하러 왔어.", "구경하니 참 좋네.", "왔구나, 반가워! 오늘도 수고 많았어.", "왔구나 반갑게 인사하네",
                                        "할 일이 많아서 그런가 봐, 몸이 자꾸 흐려져서 닦아 줄래?", "친구를 환영하네." })
        {
            Check(filter.Find(line) != null, "구경하는 말·설명문은 걸러야 함: " + line);
        }
        foreach (string line in new[] { "어서 와, 천천히 구경해!", "우리 전시관 구경하러 왔어?", "마음껏 둘러봐, 환영해!", "구경 잘 하고 가.", "왔구나, 기다렸어." })
        {
            Check(filter.IsAllowed(line), "주인 쪽 말은 통과해야 함: " + line);
        }
        // 2026-11-17 18:21 시험: 도깨비가 전시관을 구경하러 온 것처럼 말하거나(주체 뒤바뀜), 상황 설명이 대사로 나오거나, 부탁이 토막 난 말
        foreach (string line in new[] { "와, 전시관을 둘러보니 참 멋진데!", "반가워서 아침부터 달려왔어", "왔구나, 반가워서 왔어.", "안녕! 반가워서 먼저 인사해.", "왔구나 반갑게 인사하네",
                                        "다시 오니 반가워! 인사해.", "구경하러 온 친구를 반가이 맞아.", "혹시, 오랜만에 깨워줘 닦아 줘" })
        {
            Check(filter.Find(line) != null, "전시관 주체가 뒤바뀌거나 토막 난 말은 걸러야 함: " + line);
        }
        foreach (string line in new[] { "왔구나, 기다렸어!", "몸이 흐려졌어. 닦아 줄래?", "어서 와, 천천히 구경해.", "환영해, 우리 전시관은 마음껏 봐도 돼!", "자꾸 흐려져서 불안해. 깨워 줄래?" })
        {
            Check(filter.IsAllowed(line), "자연스러운 말은 통과해야 함: " + line);
        }
        foreach (string line in new[] { "왔구나, 기다렸어!", "닦아 줘서 고마워, 덕분에 개운해.", "편하게 구경해, 천천히 둘러봐!", "이틀 전에 닦아 줘서 몸이 가뿐해졌어." })
        {
            Check(filter.IsAllowed(line), "자연스러운 말은 통과해야 함: " + line);
        }

        // 기본 대사와 페르소나 예시는 모두 통과해야 한다 (모델이 실패했을 때 대신 나가는 말이다)
        int checkedLines = 0;
        foreach (SpiritLines spirit in lines.spirits.Concat(new[] { lines.fallback }))
        {
            IEnumerable<string> all = spirit.pools.SelectMany(p => p.lines).Concat(spirit.stories ?? new List<string>());
            foreach (string line in all)
            {
                checkedLines++;
                Check(filter.IsAllowed(line), spirit.name + " 기본 대사가 금칙어에 걸림: " + line + " ← " + filter.Find(line));
            }
        }
        foreach (Persona persona in personas.personas)
        {
            foreach (string example in persona.examples)
            {
                checkedLines++;
                Check(filter.IsAllowed(example), persona.name + " 예시 대사가 금칙어에 걸림: " + example);
            }
        }
        Console.WriteLine("기본·예시 대사 " + checkedLines + "줄이 금칙어 없이 통과해야 함");
    }

    // ---------- 프롬프트 조립기 ----------

    private static void CheckPrompts(PersonaCatalog personas, ActionCatalog actions,
                                     List<Dictionary<string, object>> scenes, Dictionary<string, string> labels,
                                     string expectedPath)
    {
        Console.WriteLine("=== 프롬프트 조립기: run.py 와 비교 ===");
        var expected = (Dictionary<string, object>)Json.DeserializeObject(ReadText(expectedPath));
        var options = new PromptOptions { TargetLabels = labels };
        var assembler = new PromptAssembler(personas, actions, options);
        var free = new PromptAssembler(personas, actions, new PromptOptions { TargetLabels = labels, DescribeFormat = true });
        int same = 0;
        int compared = 0;

        foreach (Dictionary<string, object> scene in scenes)
        {
            string id = (string)scene["id"];
            ReactionRequest request = ToRequest(scene);
            NpcPrompt prompt = assembler.Build(request);
            if (NpcActions.SkipsModel(request.Candidates))
            {
                Check(prompt == null && !expected.ContainsKey(id),
                        id + (NpcActions.OnlySilent(request.Candidates) ? " 끌려갈 때는 대사가 없어" : " 이야기는 정해진 대사라") + " 프롬프트를 만들지 않음 (run.py 도 건너뜀)");
                continue;
            }
            compared++;
            if (prompt == null)
            {
                Check(false, id + " 프롬프트가 만들어지지 않음");
                continue;
            }
            var want = (Dictionary<string, object>)expected[id];
            bool ok = Same(id, "시스템", (string)want["system"], prompt.System)
                      & Same(id, "사용자", (string)want["user"], prompt.User)
                      & Same(id, "사용자(형식 설명)", (string)want["userFree"], free.Build(request).User)
                      & Same(id, "스키마", (string)want["schema"], prompt.JsonSchema);
            // 흐려진 안부의 '며칠 전' 변형(Variation 1)도 run.py 와 같은 프롬프트
            if (expected.ContainsKey("_askDays") && ((Dictionary<string, object>)expected["_askDays"]).ContainsKey(id))
            {
                request.Variation = 1;
                ok &= Same(id + "(며칠 전 변형)", "사용자", (string)((Dictionary<string, object>)expected["_askDays"])[id], assembler.Build(request).User);
                request.Variation = 0;
            }
            // 문법도 모델이 적을 칸만 둔다: 행동·대상이 하나뿐이면 {"line"} 만, 대상만 하나뿐이면 {"action","line"}, 아니면 전부
            bool lineOnly = prompt.ActionIds.Count == 1 && prompt.TargetIds.Count == 1;
            if (lineOnly)
            {
                ok &= Check(prompt.Grammar.StartsWith("root ::= \"{\" ws \"\\\"line\\\"\"") && !prompt.Grammar.Contains("target") && !prompt.Grammar.Contains("action"),
                        id + " 행동·대상이 하나뿐이면 문법은 line 만");
            }
            else
            {
                foreach (string action in prompt.ActionIds)
                {
                    ok &= Check(prompt.Grammar.Contains("\"\\\"" + action + "\\\"\""), id + " 문법에 행동 " + action);
                }
                ok &= Check(prompt.TargetIds.Count == 1 ? !prompt.Grammar.Contains("target") : prompt.Grammar.Contains("target"),
                        id + " 대상이 하나뿐이면 문법에 target 칸이 없음");
            }
            if (ok)
            {
                same++;
            }
        }
        Console.WriteLine("run.py 와 같은 프롬프트: " + same + "/" + compared);

        // 시스템 메시지는 도깨비마다 고정 — 상대·선택지·상황에 따라 바뀌는 것이 없어야 llama.cpp 가 다시 읽지 않는다
        string[] changing = { "주인을 대할 때", "친구를 대할 때", "상황:", "고른 선택지", "대사:", "자 이내", "가치관", "좋아하는 것",
                              "싫어하는 것", "관리를 받으면", "끌려갈 때", "기억:" };
        foreach (Dictionary<string, object> scene in scenes)
        {
            string id = (string)scene["id"];
            ReactionRequest request = ToRequest(scene);
            NpcPrompt prompt = assembler.Build(request);
            if (prompt == null)
            {
                continue;
            }
            Check(prompt.System == assembler.SystemFor(request.RelicId), id + " 시스템 메시지가 그 도깨비의 고정 시스템 메시지와 같음");
            string found = changing.FirstOrDefault(word => prompt.System.Contains(word));
            Check(found == null, id + " 시스템 메시지에 바뀌는 내용이 없음 ← " + found);
            Check(prompt.System.Contains(PromptAssembler.RuleBond), id + " 애착 규칙 있음");
            Check(!prompt.User.Contains(NpcFactKeys.StoryIndex), id + " story_index 는 프롬프트에 없음");
            Check(!(prompt.System + prompt.User).Contains("JSON") && !prompt.User.Contains("출력 형식") && !prompt.User.Contains("영문 그대로"),
                    id + " 형식 제약이 있으면 형식 설명을 싣지 않음");
            string freeUser = free.Build(request).User;
            // 최근 대사는 프롬프트에 싣지 않는다 (작은 모델이 그대로 베꼈다)
            request.RecentLines = new List<string> { "첫째로 한 말.", "둘째로 한 말." };
            Check(!free.Build(request).User.Contains("첫째로 한 말."), id + " 최근 대사를 프롬프트에 싣지 않음");
            request.RecentLines = null;
            Check(freeUser.EndsWith(PromptAssembler.FormatLine) || freeUser.EndsWith(PromptAssembler.FormatActionLine) || freeUser.EndsWith(PromptAssembler.FormatLineOnly),
                    id + " 형식 제약이 없으면(DescribeFormat) 형식 설명을 끝에 붙임");
            // 대상이 하나뿐이면 target 칸을, 행동도 하나뿐이면 action 칸까지 빼서 출력 토큰을 줄인다 (응답 시간은 출력 토큰 수에 비례)
            bool onlyTarget = request.Candidates.SelectMany(c => c.TargetIds ?? new List<string>()).Distinct().Count() == 1;
            bool onlyAction = request.Candidates.Select(c => c.ActionId).Distinct().Count() == 1;
            Check(onlyTarget == !freeUser.Contains("target") && (onlyTarget && onlyAction) == freeUser.EndsWith(PromptAssembler.FormatLineOnly),
                    id + " 대상·행동이 하나뿐이면 그 칸을 형식에서 뺌");
            Check(prompt.User.StartsWith(request.Mode == InteractionMode.Owner ? "지금 상대: " + request.AddressName : "지금 상대: 친구"),
                    id + " 사용자 메시지는 지금 상대부터 (같은 상대면 앞부분이 이어짐)");
            if (request.Input == NpcInputs.Care)
            {
                Check(!prompt.User.Contains("좋아하는 것:") && !prompt.User.Contains("가치관:") && prompt.User.Contains("관리를 받으면:"),
                        id + " 감사 장면에는 좋아하는 것·가치관을 넘기지 않음");
            }
            // 좋아하는 것·싫어하는 것 칸은 뺐다 (탭 인사에 실었더니 그 낱말을 상황과 상관없이 대사에 끼워 넣었다). 끌려갈 때 칸도 없다(대사가 없다).
            // 말투 다짐 줄은 공통 규칙(RuleSpeech)과 겹쳐 뺐다
            Check(!prompt.User.Contains("좋아하는 것:") && !prompt.User.Contains("싫어하는 것:") && !prompt.User.Contains("끌려갈 때:")
                    && !prompt.User.Contains("말투:"), id + " 사용자 메시지에 좋아하는 것·싫어하는 것·끌려갈 때·말투 줄이 없음");
            Check(prompt.System.Contains(PromptAssembler.RuleSpeech) && prompt.System.Contains("반말"),
                    id + " 말투는 공통 규칙(반말)으로");
            if (request.Input == NpcInputs.Visit)
            {
                Check(!prompt.User.Contains("좋아하는 것:") && !prompt.User.Contains("가치관:") && !prompt.User.Contains("관리를 받으면:"),
                        id + " 손님 관람에는 상대 태도만");
            }
            if (request.Input == NpcInputs.AskStatus && request.Candidates.Any(c => c.ActionId == NpcActions.AnswerStatus))
            {
                bool uncared = request.Facts.Any(f => f.Key == NpcFactKeys.Clarity && (f.Value == "흐려짐" || f.Value == "재봉인"));
                Check(prompt.User.Contains("관리를 받으면:") == !uncared,
                        id + (uncared ? " 흐려진 안부에는 '관리를 받으면'을 넣지 않음 (닦아 줘서 고맙다고 하지 않게)"
                                      : " 선명한 안부에는 '관리를 받으면'을 넣음"));
            }
            // 곁들일 거리: Variation 번째 것을 하나 싣는다. 같은 상황이 거듭될 때 프롬프트가 달라져야 대사가 갈린다
            string flavorKey = PromptAssembler.FlavorKey(request);
            Persona speaker = personas.Find(request.RelicId);
            List<string> flavorItems = flavorKey != null ? speaker.flavors.For(flavorKey) : null;
            if (flavorItems != null && flavorItems.Count > 1)
            {
                Check(prompt.User.Contains(PromptAssembler.FlavorLine(flavorItems[0]) + "\n"), id + " 곁들일 거리 0번째를 싣음");
                // 흐려진 안부는 홀수 Variation 이 '며칠 전' 변형(곁들일 거리 없음)이라 짝수로 센다
                bool askScene = PromptAssembler.AskVariant(request) != null;
                int step = askScene ? 2 : 1;
                request.Variation = step;
                string second = assembler.Build(request).User;
                request.Variation = askScene ? flavorItems.Count * 2 : flavorItems.Count;
                string wrapped = assembler.Build(request).User;
                request.Variation = 0;
                Check(second.Contains(PromptAssembler.FlavorLine(flavorItems[step % flavorItems.Count]) + "\n") && wrapped == prompt.User,
                        id + " Variation 이 바뀌면 다음 거리, 목록 끝을 넘으면 처음으로");
            }
            else
            {
                Check(flavorKey == null && !prompt.User.Contains(PromptAssembler.FlavorLabel),
                        id + " 곁들일 거리는 지난 대화 꺼내기에만 싣지 않음 ← " + flavorKey);
            }
        }
        foreach (Persona persona in personas.personas)
        {
            string system = assembler.SystemFor(persona.relicId);
            Check(system != null && (system.Length - system.Replace("…", "").Length) <= 2,
                    persona.name + " 시스템 메시지에 말줄임표가 거의 없음 (많으면 모든 대사에 붙인다)");
            Check(system != null && system.Contains(PromptAssembler.RuleRole) && system.Contains(PromptAssembler.RuleSpeech),
                    persona.name + " 시스템 메시지에 닦이는 쪽 규칙·어미 규칙 있음");
            string timed = persona.examples.FirstOrDefault(e => e.Contains("오늘") || e.Contains("지금"));
            Check(timed == null, persona.name + " 예시에 지금 시각·날씨 말이 없음 (접속 시각을 넘기지 않는데 통째로 옮긴다) ← " + timed);
            Check(system.Contains(PromptAssembler.RuleBond) && system.Contains(PromptAssembler.RuleOrder) && !system.Contains("손님"),
                    persona.name + " 시스템 메시지에 애착·어순 규칙 있음, 부르는 말(손님)은 없음 (주인 장면에서 '손님께서'라고 했다)");
            // 모든 도깨비에 같은 소개·규칙이 맨 앞에 있다 — 다른 도깨비로 넘어가도 llama.cpp 가 그 부분은 다시 읽지 않는다 (2026-11-24)
            Check(system.StartsWith(assembler.SharedSystemHead + "\n\n", StringComparison.Ordinal)
                    && system.IndexOf(persona.origin, StringComparison.Ordinal) > assembler.SharedSystemHead.Length
                    && !assembler.SharedSystemHead.Contains(persona.origin) && !assembler.SharedSystemHead.Contains(persona.traits),
                    persona.name + " 시스템 메시지: 공통 규칙이 맨 앞, 도깨비마다 다른 정체·성격은 그 뒤");
            // 규칙 줄에는 하지 말 것을 적지 않는다 (작은 모델이 그 말에 끌려 설명문을 읊거나 헷갈렸다, 2026-11-13). 금지는 출력 검사가 맡는다
            // 규칙은 공통 앞부분에만 있다 (뒤의 성격·말투는 규칙이 아니라 이 검사에서 뺀다)
            string head = assembler.SharedSystemHead;
            string rules = head.Substring(head.IndexOf("규칙:", StringComparison.Ordinal));
            string negative = new[] { "않", "말고", "말 것", "마라", "금지", "못 " }.FirstOrDefault(w => rules.Contains(w));
            Check(negative == null, persona.name + " 규칙에 금지 표현이 없음 ← " + negative);
            string thin = new[] { "tap", "faded", "clear", "visit" }
                    .FirstOrDefault(key => persona.flavors == null || persona.flavors.For(key).Count < 2);
            Check(thin == null, persona.name + " 곁들일 거리가 상황마다 2개 이상 (같은 상황마다 같은 대사가 나오지 않게) ← " + thin);
        }

        // 페르소나가 없는 도깨비, 후보가 없는 요청, 이야기는 만들지 않는다 → 규칙 기반 생성기로 대신
        Check(assembler.Build(Request(1, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet)) == null, "페르소나 없는 도깨비는 null");
        Check(assembler.SystemFor(1) == null, "페르소나 없는 도깨비는 시스템 메시지도 null");
        Check(assembler.Build(Request(8, InteractionMode.Owner, NpcInputs.Tap)) == null, "후보가 없으면 null");
        Check(assembler.Build(Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory)) == null, "이야기는 null");

        NpcPrompt noExamples = new PromptAssembler(personas, actions, new PromptOptions { IncludeExamples = false })
                .Build(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        Check(!noExamples.System.Contains("대사 예시:") && !noExamples.System.Contains(PromptAssembler.RuleExamples),
                "예시를 끄면 예시와 예시 규칙이 빠짐");

        NpcPrompt sample = assembler.Build(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        Console.WriteLine("--- 문법(GBNF) 예: 말발굽 탭 ---");
        Console.Write(sample.Grammar);
    }

    // ---------- 출력 검사 · SLM 생성기 ----------

    private static void CheckOutput(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions,
                                    DefaultLineCatalog lines, List<Dictionary<string, object>> scenes)
    {
        Console.WriteLine("=== 출력 검사 ===");
        var validator = new NpcOutputValidator(new NpcLineFilter(forbidden));
        ReactionRequest tap = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);

        // 통과
        OutputCheck ok = validator.Check(Out("greet", "player", "어흠, 오셨구려."), tap);
        Check(ok.IsValid && ok.ActionId == "greet" && ok.TargetId == "player" && ok.Line == "어흠, 오셨구려.", "올바른 출력은 통과");
        ReactionResult okResult = ok.ToResult("r1");
        Check(okResult.IsSuccess && okResult.Source == ReactionSource.Slm && okResult.Line == "어흠, 오셨구려." && okResult.RequestId == "r1",
                "통과하면 Success(Slm)");
        Expect(validator, "<think>음…</think>\n```json\n" + Out("greet", "player", "어흠, 오셨구려.") + "\n```\n끝",
                tap, OutputProblem.None, "생각·코드 블록·앞뒤 잡글이 있어도 JSON 을 찾음");
        Check(validator.Check(Out("greet", "player", " “어흠, 오셨구려.” "), tap).Line == "어흠, 오셨구려.", "대사 앞뒤 공백·따옴표는 걷어 냄");
        Check(validator.Check("{\"line\":\"\\uc5b4\\ud760\",\"target\":\"player\",\"action\":\"greet\",\"extra\":[1,{\"a\":null}]}", tap).Line
                == "어흠", "키 순서·\\u 이스케이프·다른 필드가 있어도 읽음");

        // JSON
        Expect(validator, "어흠, 오셨구려.", tap, OutputProblem.NotJson, "JSON 이 없으면 NotJson");
        Expect(validator, "", tap, OutputProblem.NotJson, "빈 출력은 NotJson");
        Expect(validator, null, tap, OutputProblem.NotJson, "null 출력은 NotJson");
        Expect(validator, "{\"action\":\"greet\" \"target\":\"player\"}", tap, OutputProblem.NotJson, "쉼표가 빠지면 NotJson");
        Expect(validator, "{\"action\":\"greet\",\"target\":\"player\",\"line\":", tap, OutputProblem.NotJson, "문자열 밖에서 끊기면 NotJson");

        // 토큰 한도에서 대사 중간에 끊김: 닫아서 읽고, 끝난 문장까지만 쓴다
        OutputCheck cut = validator.Check("{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠, 오셨구려. 오늘 밤 순", tap);
        Check(cut.IsValid && cut.Truncated && cut.Line == "어흠, 오셨구려." && cut.FullLine == "어흠, 오셨구려. 오늘 밤 순",
                "토큰 한도에서 끊긴 대사는 끝난 문장까지만 ← " + cut.Line);
        Check((cut.ToResult("c").Note ?? "").Contains("끊긴") && cut.ToResult("c").Line == "어흠, 오셨구려.", "끊겨서 잘라 냈으면 결과 Note 에 적음");
        OutputCheck unfinished = validator.Check("{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠, 오셨", tap);
        Check(unfinished.Problem == OutputProblem.TooLong && unfinished.Truncated, "끊겼는데 끝난 문장이 없으면 TooLong ← " + unfinished.Detail);
        Expect(validator, "{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠}", tap, OutputProblem.TooLong,
                "문자열이 닫히지 않은 채 끝나면 끊긴 것으로 보고, 끝난 문장이 없으면 TooLong");
        Check(validator.Check("{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠, 오셨구려.", tap).Line == "어흠, 오셨구려.",
                "마침표 바로 뒤에서 끊겼으면 그 문장은 끝난 것");
        Expect(validator, "{\"action\":\"greet\",\"target\":\"pla", tap, OutputProblem.TargetMismatch, "대상 칸에서 끊기면 TargetMismatch");
        Expect(validator, "<think>{\"action\":\"greet\"", tap, OutputProblem.NotJson, "닫히지 않은 생각 블록만 있으면 NotJson");

        // 후보 포함
        Expect(validator, Out("answer_status", "player", "어흠."), tap, OutputProblem.UnknownAction, "후보에 없는 행동");
        Expect(validator, Out("안부 묻기", "player", "어흠."), tap, OutputProblem.UnknownAction, "행동 칸에 선택지 이름");
        Expect(validator, "{\"target\":\"player\",\"line\":\"어흠.\"}", tap, OutputProblem.None, "행동이 없어도 후보가 하나뿐이면 후보로 채움");
        Expect(validator, Out("greet", "none", "어흠."), tap, OutputProblem.TargetMismatch, "후보에 없는 대상");
        Expect(validator, Out("greet", "player(주인)", "어흠."), tap, OutputProblem.TargetMismatch, "대상 칸에 'player(주인)'");
        Expect(validator, "{\"action\":\"greet\",\"line\":\"어흠.\"}", tap, OutputProblem.None, "대상이 없어도 대상이 하나뿐이면 후보로 채움");
        var paired = new ReactionRequest { RequestId = "p", RelicId = 8, Mode = InteractionMode.Owner, Input = NpcInputs.Tap };
        paired.Candidates.Add(new ActionCandidate(NpcActions.Greet, NpcTargets.Player));
        paired.Candidates.Add(new ActionCandidate(NpcActions.AnswerStatus, "building:1"));
        Expect(validator, Out("greet", "building:1", "어흠."), paired, OutputProblem.TargetMismatch, "다른 행동의 대상은 안 됨 (짝 검사)");
        Expect(validator, Out("answer_status", "building:1", "어흠."), paired, OutputProblem.None, "행동에 맞는 대상이면 통과");
        var noTarget = new ReactionRequest { RequestId = "n", RelicId = 8, Mode = InteractionMode.Owner, Input = NpcInputs.Tap };
        noTarget.Candidates.Add(new ActionCandidate(NpcActions.Greet));
        Expect(validator, Out("greet", "none", "어흠."), noTarget, OutputProblem.None, "대상이 없는 후보는 none");

        // 대사
        Expect(validator, Out("greet", "player", ""), tap, OutputProblem.EmptyLine, "빈 대사");
        Expect(validator, Out("greet", "player", " \\\"\\\" "), tap, OutputProblem.EmptyLine, "따옴표뿐인 대사");
        Expect(validator, "{\"action\":\"greet\",\"target\":\"player\"}", tap, OutputProblem.EmptyLine, "대사가 없음");
        Expect(validator, Out("greet", "player", "어흠,\\n오셨구려."), tap, OutputProblem.Newline, "이스케이프된 줄바꿈");
        Expect(validator, Out("greet", "player", "어흠,\n오셨구려."), tap, OutputProblem.Newline, "날것 줄바꿈");
        Expect(validator, Out("greet", "player", "어흠,\\r오셨구려."), tap, OutputProblem.Newline, "\\r 도 줄바꿈");
        Expect(validator, Out("greet", "player", new string('나', 40)), tap, OutputProblem.None, "기본 40자까지 통과");
        Expect(validator, Out("greet", "player", new string('나', 40 + NpcOutputValidator.LengthTolerance)), tap, OutputProblem.None, "제한에 여유 5자를 더해 45자까지 통과");
        Expect(validator, Out("greet", "player", new string('나', 41 + NpcOutputValidator.LengthTolerance)), tap, OutputProblem.TooLong, "한 문장인데 46자면 TooLong");
        ReactionRequest visit = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        Expect(validator, Out("wander_greet", "player", new string('나', 25)), visit, OutputProblem.None, "손님 관람 25자까지 통과");
        Expect(validator, Out("wander_greet", "player", new string('나', 25 + NpcOutputValidator.LengthTolerance)), visit, OutputProblem.None, "손님 관람 26~30자도 통과 (실기기에서 26자·27자로 거르고 다시 만들었다)");
        Expect(validator, Out("wander_greet", "player", new string('나', 26 + NpcOutputValidator.LengthTolerance)), visit, OutputProblem.TooLong, "손님 관람 31자는 TooLong");
        Check(validator.LimitFor(NpcInputs.ListenStory) == NpcOutputValidator.DefaultMaxChars, "이야기는 SLM 을 쓰지 않아 따로 둔 제한이 없음");

        // 길면 다 버리지 않고 제한 안에 드는 앞 문장들만 남긴다
        const string longLine = "어흠, 오셨구려. 오늘 밤 순찰도 이상 없었으니 걱정 마시고 편히 쉬시되 문단속은 꼭 하시구려. 이 몸이 지키겠소.";
        OutputCheck trimmed = validator.Check(Out("greet", "player", longLine), tap);
        Check(longLine.Length > 40 && trimmed.IsValid && trimmed.Trimmed && !trimmed.Truncated && trimmed.Line == "어흠, 오셨구려."
                && trimmed.FullLine == longLine, "세 문장 " + longLine.Length + "자는 45자 안에 드는 첫 문장만 ← " + trimmed.Line);
        Check(trimmed.ToResult("t").Note == "긴 대사를 문장 단위로 잘라 씀 (" + longLine.Length + "자 → 9자)", "잘라 냈으면 결과 Note 에 적음 ← "
                + trimmed.ToResult("t").Note);
        Check(ok.ToResult("o").Note == null, "자르지 않았으면 Note 없음");
        Check(validator.Check(Out("greet", "player", "앗! 참새다! 히힛, 같이 쫓아갈래? 저기 저 나무 위로 날아갔어, 빨리 가 보자!"), tap).Line
                == "앗! 참새다! 히힛, 같이 쫓아갈래?", "제한 안에 드는 앞 문장은 여럿이어도 남김");
        Check(NpcOutputValidator.FitSentences("허허… 어서 오게. 오늘은 바람이 참 순하고 산 너머 구름이 꼭 향 연기 같구먼.", 40, false) == "허허… 어서 오게.",
                "'…'는 문장 끝이 아님");
        Check(NpcOutputValidator.FitSentences("허허... 어서 오게나, 오늘은 바람이 참 순하고 산 너머 구름이 꼭 향 연기 같구먼.", 40, false) == null,
                "'...'도 문장 끝이 아님 → 한 문장이 길면 null");
        Check(NpcOutputValidator.FitSentences("\"반가워!\" 하고 웃었어. 정말이야", 15, false) == "\"반가워!\" 하고 웃었어.", "닫는 따옴표 뒤에서 문장이 끝남");
        Check(NpcOutputValidator.FitSentences("햇볕 좋다~ 여기서 낮잠 잘래! 같이 자자, 응? 진짜 따뜻하다니까!", 20, false) == "햇볕 좋다~ 여기서 낮잠 잘래!",
                "물결(~) 뒤도 문장 끝");
        Check(NpcOutputValidator.FitSentences("어흠, 오셨구려", 40, false) == "어흠, 오셨구려", "문장 부호 없이 끝난 짧은 대사는 그대로");
        // 첫 문장이 길이 제한을 넘어도 쉼표 앞 마디가 끝난 말이면 그것만 쓴다 (다시 만드는 1.5~2초를 아낀다)
        Check(NpcOutputValidator.FitSentences("만나서 반가워, 오늘 아침 일찍 깨워줘서 정말 고마워.", 20, false) == "만나서 반가워", "쉼표 앞 마디가 끝난 말이면 그것만 씀");
        Check(NpcOutputValidator.FitSentences("이틀 전에 닦아 줘서 고마워, 덕분에 몸이 가벼워졌어.", 25, false) == "이틀 전에 닦아 줘서 고마워", "가장 긴 앞 마디를 씀");
        Check(NpcOutputValidator.FitSentences("반가워서 찾아왔는데, 오늘 아침 햇살이 참 밝네 정말로 밝아.", 15, false) == null, "앞 마디가 끝난 말이 아니면(~는데) 자르지 않고 TooLong");
        Check(NpcOutputValidator.FitSentences("좋아, 오늘 아침 일찍 깨워줘서 정말 정말 고마워.", 3, false) == null, "마디가 너무 짧으면 자르지 않음");
        OutputCheck cutAway = validator.Check(Out("greet", "player", "어흠, 오셨구려. 오늘 밤은 더 볼 일 없으니 이제 그만 놓아라, 이 몸이 바쁘오."), tap);
        Check(cutAway.IsValid && cutAway.Line == "어흠, 오셨구려.", "금칙어는 띄울 대사(자른 뒤)에서 찾음");
        Expect(validator, Out("greet", "player", "……손대지 마. 지문 남잖아. 그러니 살살 들어 주면 좋겠어, 알았지? 부탁이야."), tap,
                OutputProblem.Forbidden, "남긴 문장에 금칙어가 있으면 Forbidden");
        foreach (Dictionary<string, object> scene in scenes)
        {
            // 장면에 maxChars 가 없으면 run.py 의 --max-chars 기본값(40)으로 판정한다
            int want = scene.ContainsKey("maxChars") ? (int)scene["maxChars"] : 40;
            Check(validator.LimitFor((string)scene["input"]) == want, scene["id"] + " 길이 제한이 run.py 판정과 같음 (" + want + "자)");
        }

        // 금칙어
        OutputCheck bad = validator.Check(Out("greet", "player", "……지문 남는다. 놓아."), tap);
        Check(bad.Problem == OutputProblem.Forbidden && bad.Detail.Contains("놓아"), "금칙어(놓아) ← " + bad.Detail);
        Expect(validator, Out("greet", "player", "앗, 놔줘~"), tap, OutputProblem.Forbidden, "금칙어(놔)");
        ReactionResult badResult = bad.ToResult("r2");
        Check(!badResult.IsSuccess && badResult.FailReason == FailReason.InvalidOutput && badResult.Source == ReactionSource.Slm
                && badResult.FailDetail.StartsWith("Forbidden"), "걸리면 Failed(InvalidOutput) + 이유 ← " + badResult.FailDetail);

        // Gemma 4 E2B 가 실제로 낸 대사 (프롬프트 시험 결과)
        Expect(validator, Out("greet", "player",
                "허허… 내 몸이 예전에 능산리 절터 발굴 조사 중에 저습지에서 나왔다는 이야기를 들으면 참 기분이 좋구먼. 산속 고요함 속에서 이렇게 잠시 이야기를 나누게 되니 참 반갑네."),
                Request(2, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet), OutputProblem.TooLong, "Gemma 96자 대사는 첫 문장부터 넘쳐 TooLong");

        Console.WriteLine("=== SLM 생성기 ===");
        var backend = new FakeBackend();
        var assembler = new PromptAssembler(personas, actions);
        var slm = new SlmReactionGenerator(backend, assembler, validator);
        Check(!slm.IsReady, "올리기 전에는 IsReady false");
        Check(Run(slm, tap).FailReason == FailReason.NotReady && backend.Calls == 0, "올리기 전에는 NotReady, 모델을 부르지 않음");
        slm.LoadAsync(CancellationToken.None).Wait();
        Check(slm.IsReady, "LoadAsync 뒤 IsReady");

        backend.Reply = Out("greet", "player", "어흠, 오셨구려.");
        ReactionResult good = Run(slm, tap);
        Check(good.IsSuccess && good.Source == ReactionSource.Slm && good.Line == "어흠, 오셨구려." && good.RequestId == tap.RequestId,
                "통과한 출력은 Success(Slm)");
        Check(backend.LastPrompt != null && backend.LastPrompt.System.Contains("조선의 마패") && !backend.LastPrompt.System.Contains("말발굽") && backend.LastPrompt.Grammar.Contains("line") && !backend.LastPrompt.Grammar.Contains("target"),
                "조립한 프롬프트(문법 포함)를 모델에 넘김 — 시스템 메시지에는 출신만 있고 도깨비 이름은 없음(자기 이름을 말하는 뿌리)");

        backend.Reply = Out("greet", "player", "……손대지 마.");
        ReactionResult rejected = Run(slm, tap);
        Check(rejected.FailReason == FailReason.InvalidOutput && rejected.FailDetail.Contains("손대지마"), "금칙어 출력은 InvalidOutput ← " + rejected.FailDetail);
        backend.Reply = Out("struggle_play", "player", "히힛.");
        Check(Run(slm, tap).FailReason == FailReason.InvalidOutput, "후보 밖 행동은 InvalidOutput");

        // 요청 관리자가 할 일: SLM 이 실패하면 규칙 기반으로 대신한다
        ReactionResult fallback = new RuleBasedReactionGenerator(lines, new Random(7)).Generate(tap, CancellationToken.None);
        Check(fallback.IsSuccess && fallback.Source == ReactionSource.Rule, "실패하면 규칙 기반이 같은 요청으로 대신 ← " + fallback.Line);

        int calls = backend.Calls;
        Check(Run(slm, Request(1, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet)).FailReason == FailReason.Unsupported
                && backend.Calls == calls, "페르소나 없는 도깨비는 Unsupported, 모델을 부르지 않음");
        ReactionResult storyAttempt = Run(slm, Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory));
        Check(storyAttempt.FailReason == FailReason.Unsupported && (storyAttempt.FailDetail ?? "").Contains("정해진 대사") && backend.Calls == calls,
                "이야기는 Unsupported(정해진 대사), 모델을 부르지 않음");
        Check(Run(slm, Request(8, InteractionMode.Owner, NpcInputs.Tap)).FailReason == FailReason.NoCandidate, "후보가 없으면 NoCandidate");
        Check(Run(slm, null).FailReason == FailReason.Error, "요청이 없으면 Error");

        backend.Throw = new InvalidOperationException("GPU 메모리 부족");
        ReactionResult crashed = Run(slm, tap);
        Check(crashed.FailReason == FailReason.Error && crashed.FailDetail.Contains("GPU 메모리 부족"), "모델 오류는 Error + 메시지");
        backend.Throw = new OperationCanceledException();
        Check(Run(slm, tap).FailReason == FailReason.Cancelled, "모델이 취소되면 Cancelled");
        backend.Throw = null;
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        calls = backend.Calls;
        Check(slm.GenerateAsync(tap, cancelled.Token).Result.FailReason == FailReason.Cancelled && backend.Calls == calls,
                "이미 취소된 요청은 Cancelled, 모델을 부르지 않음");

        // 생성 도중 시간 초과: 모델이 늦게 답해도 결과를 버린다
        var timeout = new CancellationTokenSource();
        backend.Reply = Out("greet", "player", "어흠, 오셨구려.");
        backend.OnComplete = timeout.Cancel;
        Check(slm.GenerateAsync(tap, timeout.Token).Result.FailReason == FailReason.Cancelled, "생성 도중 취소되면 늦은 결과도 Cancelled");
        backend.OnComplete = null;

        slm.Unload();
        Check(!slm.IsReady && Run(slm, tap).FailReason == FailReason.NotReady, "Unload 뒤에는 NotReady");
    }

    // ---------- 요청 관리자 ----------

    // ---------- 같은 대사·말투 강제 ----------

    private static void CheckEnforcement(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 같은 대사·말투 강제 ===");

        // 같은 대사 판단 (2026-11-11 시험에서 나온 대사)
        string[][] same =
        {
            new[] { "고마워.", "고마워!" },
            new[] { "안녕하시오, 계시오?", "안녕하시오, 계시오? 주인님께서 각성하셨으니 경계를 늦추지 않겠습니다." },
            new[] { "주인님, 관리해 줘서 고마워요! 참새한테 자랑해야겠어요~", "주인님~ 관리해줘서 고마워요! 반짝이는 털이 더 빛나요!" },
            new[] { "좋은 하루라네. 천천히 구경하세요.", "좋은 하루라네, 천천히 구경하게." },
            new[] { "이틀 전에 닦아 주셔서 고마워요. 구름 무늬가 또렷해져 기뻐요.", "이틀 전에 닦아 주셔서 고마워요. 갠 하늘 같아요." }
        };
        foreach (string[] pair in same)
        {
            Check(NpcLineChecks.Similar(pair[0], pair[1]) != null, "같은 대사로 봄: " + pair[0] + " ↔ " + pair[1]);
        }
        string[][] different =
        {
            new[] { "안부를 물어주셔서 정말 고마워요. 오래 관리를 못 받아 흐려졌어요.", "이틀 전에 닦아 주셔서 정말 고마워요. 덕분에 선명해진 것 같아요." },
            new[] { "안녕? 주인님, 뭐 하고 놀까?", "안녕? 참새 술래잡기 할까?" },
            new[] { "구름 무늬가 또렷해져서 기뻐요.", "날개가 부딪힐까 무서워요. 살살 옮겨 주세요." },
            new[] { "고마워.", "고마워, 자개가 다시 반짝이네." },
            // 90% 기준 (2026-11-13): 앞말만 붙은 81~85% 겹침은 다른 대사
            new[] { "고마워, 덕분에 더 선명해졌어.", "관리해 줘서 고마워, 덕분에 더 선명해졌어." },
            new[] { "정성껏 관리해 줘서 고마워.", "관리해 줘서 고마워." }
        };
        foreach (string[] pair in different)
        {
            Check(NpcLineChecks.Similar(pair[0], pair[1]) == null,
                    "다른 대사로 봄: " + pair[0] + " ↔ " + pair[1] + " ← " + NpcLineChecks.Similar(pair[0], pair[1]));
        }

        // 문장마다 어미 (존댓말·반말 섞기)
        // 말발굽·향산이·구름결도 반말로 바꿨으므로, 하오체·하게체·해요체 검사는 페르소나가 아닌 이 합성 어미 규칙으로 계속 시험한다
        EndingHint hao = new EndingHint { label = "하오체", pattern = "(오|소|구려)$", notPattern = "(니다|요)$" };
        EndingHint hage = new EndingHint { label = "하게체", pattern = "(네|구먼|게|세|가|나)$", notPattern = "(요|니다|어|야|거든|잖아|냐)$",
                bannedPattern = "(^|[\\s.!?…~,])안녕(?!하)" };
        EndingHint haeyo = new EndingHint { label = "해요체", pattern = "(요|니다|[습십합됩갑봅옵]니까)$" };
        EndingHint banmal = personas.Find(9).endingHint;
        string wrong;
        Check(!NpcLineChecks.SpeechMatches("안녕! 놀고 싶어요~ 같이 놀자!", banmal, out wrong) && wrong == "놀고 싶어요~",
                "반말 도깨비가 한 문장이라도 '~요'로 끝내면 걸림 ← " + wrong);
        Check(NpcLineChecks.SpeechMatches("앗! 같이 놀자 히힛!", banmal, out wrong) && !NpcLineChecks.SpeechMatches("고맙습니다.", banmal, out wrong),
                "반말: 감탄은 건너뛰고, '~습니다'도 걸림");
        Check(NpcLineChecks.SpeechMatches("안녕하시오, 주인님.", hao, out wrong) && NpcLineChecks.SpeechMatches("어흠. 주인께서 오셨구려.", hao, out wrong)
                && !NpcLineChecks.SpeechMatches("안녕하십니까, 주인님.", hao, out wrong), "하오체: 끝의 부르는 말은 떼고 봄, '~습니다'는 걸림");
        Check(NpcLineChecks.SpeechMatches("왔는가? 천천히 보게.", hage, out wrong)
                && !NpcLineChecks.SpeechMatches("좋은 하루 보내요, 천천히 구경하게.", hage, out wrong), "하게체: '~요'를 섞으면 걸림");
        Check(NpcLineChecks.SpeechMatches("조심해 주세요. 고맙습니다.", haeyo, out wrong) && !NpcLineChecks.SpeechMatches("조심해 줘.", haeyo, out wrong),
                "해요체: '~요'·'~습니다'는 되고 반말은 걸림");
        Check(!NpcLineChecks.SpeechMatches("어디 가냐?", haeyo, out wrong), "해요체: '~냐'는 반말이라 걸림");
        Check(NpcLineChecks.SpeechMatches("네! 천천히 보세요.", haeyo, out wrong) && NpcLineChecks.SpeechMatches("예. 고마워요.", haeyo, out wrong),
                "해요체: 홀로 쓴 대답 '네!'·'예.'는 건너뜀 ← " + wrong);
        Check(!NpcLineChecks.SpeechMatches("싫으니까! 그만해 주세요.", haeyo, out wrong) && wrong == "싫으니까!",
                "해요체: 까닭을 말하는 반말 '~으니까'는 걸림 ← " + wrong);
        Check(NpcLineChecks.SpeechMatches("어디 가십니까? 다시 오셨습니까?", haeyo, out wrong), "해요체: 격식 물음 '~ㅂ니까'·'~습니까'는 통과 ← " + wrong);
        Check(!NpcLineChecks.SpeechMatches("고마워! 다시 오세요.", haeyo, out wrong) && wrong == "고마워!",
                "짧은 문장도 감탄사가 아니면 어미를 봄 (해요체의 '고마워!') ← " + wrong);
        Check(NpcLineChecks.SpeechMatches("히힛! 같이 놀자!", banmal, out wrong) && NpcLineChecks.SpeechMatches("어흠. 고맙소.", hao, out wrong)
                && NpcLineChecks.SpeechMatches("앗, 깨질까 봐 무서워요.", haeyo, out wrong), "감탄사만 있는 문장·마디는 건너뜀");
        // bannedPattern: 묘작이는 '야'·'저'·'주인님' 아닌 호칭, 향산이는 반말 인사·반말 어미
        Check(!NpcLineChecks.SpeechMatches("야, 나 좀 만져봐!", banmal, out wrong) && !NpcLineChecks.SpeechMatches("나 왔어! 저 놀고 싶어!", banmal, out wrong)
                && NpcLineChecks.SpeechMatches("나 왔어! 저기 참새 봐!", banmal, out wrong),
                "반말(묘작이): '야'·'저'는 걸리고, '나'·'저기'는 통과 ← " + wrong);
        Check(!NpcLineChecks.SpeechMatches("안녕, 구경 오셨구먼.", hage, out wrong) && !NpcLineChecks.SpeechMatches("쉬어 가게, 편히 앉아.", hage, out wrong)
                && NpcLineChecks.SpeechMatches("안녕하신가, 편히 쉬어 가게.", hage, out wrong),
                "하게체(향산이): 반말 '안녕'·'~아'는 걸리고, '안녕하신가'는 통과 ← " + wrong);

        // 페르소나를 받은 출력 검사기: 자기 이름·어미·반복
        var strict = new NpcOutputValidator(new NpcLineFilter(forbidden), personas);
        var loose = new NpcOutputValidator(new NpcLineFilter(forbidden));
        Expect(strict, Out("wander_greet", "player", "반가워향산이야, 편히 구경해."), Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet),
                OutputProblem.SelfName, "자기 이름(어간 '향산')이 낱말에 붙어 지울 수 없으면 걸림");
        OutputCheck nameStrip = strict.Check(Out("wander_greet", "player", "향산이가 인사하네, 편히 구경해."), Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet));
        Check(nameStrip.IsValid && nameStrip.Line == "인사하네, 편히 구경해.", "자기 이름은 조사까지 지우고 통과 ← " + nameStrip.Line);
        Check(NpcLineChecks.NormalizeSpeech("향산아, 오셨어? 닦아주시면 좋겠어.", "반말", "향산이") == "왔어? 닦아줘.", "이름 어간·조사와 -시- 높임을 지움");
        // 2026-11-16 시험 두 번째 기록: "부르셔서"(르 불규칙 높임)가 어미 섞임으로 걸려 기본 대사로 넘어간 것(S22 #4·S23 #1·#4·S24 #3)을 고쳐서 살린다
        Check(NpcLineChecks.NormalizeSpeech("안녕, 오늘 아침부터 부르셔서 반가워.", "반말", "말발굽") == "안녕, 오늘 아침부터 불러서 반가워.", "'부르셔서' → '불러서'");
        Check(NpcLineChecks.NormalizeSpeech("반가워, 오늘 아침에 부르셔서 기쁩니다.", "반말", "말발굽") == "반가워, 오늘 아침에 불러서 기뻐.", "'부르셔서 기쁩니다' → '불러서 기뻐'");
        Check(NpcLineChecks.NormalizeSpeech("안녕, 오늘 아침부터 부르셨구나.", "반말", "말발굽") == "안녕, 오늘 아침부터 불렀구나.", "'부르셨' → '불렀'");
        Check(NpcLineChecks.NormalizeSpeech("깨우셔서 고마워.", "반말", "말발굽") == "깨워서 고마워." && NpcLineChecks.NormalizeSpeech("찾으셔서 고마워.", "반말", "말발굽") == "찾아서 고마워."
                && NpcLineChecks.NormalizeSpeech("들르셔서 반가워.", "반말", "말발굽") == "들러서 반가워.", "'깨우셔서'·'찾으셔서'·'들르셔서' 높임도 풀어 줌");
        Check(NpcLineChecks.NormalizeSpeech("어르신이 오셨어.", "반말", "말발굽") == "어르신이 왔어.", "'어르신'은 건드리지 않음");
        // 2026-11-16 22:33 시험: 묻는 말이 아닌데 '?'로 끝낸 것("바쁘네?"·"부르네?")은 '.'로, 묻는 말("왔어?"·"볼래?"·"있지?")은 그대로
        Check(NpcLineChecks.NormalizeSpeech("만나서 반가워, 오늘 아침부터 바쁘네?", "반말", "말발굽") == "만나서 반가워, 오늘 아침부터 바쁘네."
                && NpcLineChecks.NormalizeSpeech("안녕, 오늘은 아침부터 나를 부르네?", "반말", "말발굽") == "안녕, 오늘은 아침부터 나를 부르네."
                && NpcLineChecks.NormalizeSpeech("반가워, 전시관 구경하러 왔어?", "반말", "말발굽") == "반가워, 전시관 구경하러 왔어?"
                && NpcLineChecks.NormalizeSpeech("별일 없었지? 그렇구나?", "반말", "말발굽") == "별일 없었지? 그렇구나."
                && NpcLineChecks.NormalizeSpeech("네?", "반말", "말발굽") == "네?",
                "감탄·서술 끝맺음(~네·~군·~구나·~더라) 뒤의 '?'만 '.'로");
        Check(PromptAssembler.RuleOrder.Contains("묻는 말만 ?로 끝낸다"), "규칙: 묻는 말만 ?로 끝낸다");
        // 2026-11-17 19:43 시험: 존대와 반말이 섞였다("말씀드렸지?"). 남은 겸양·높임 낱말을 반말로 바꾼다
        Check(NpcLineChecks.NormalizeSpeech("주인님, 자개 공들여 만든 거라 말씀드렸죠?", "반말", "밤바다") == "자개 공들여 만든 거라 말했지?"
                && NpcLineChecks.NormalizeSpeech("말씀드릴게", "반말", "밤바다") == "말할게"
                && NpcLineChecks.NormalizeSpeech("그 말씀 기억해", "반말", "밤바다") == "그 말 기억해"
                && NpcLineChecks.NormalizeSpeech("인사드렸어", "반말", "밤바다") == "인사했어"
                && NpcLineChecks.NormalizeSpeech("보여드렸어", "반말", "밤바다") == "보여줬어"
                && NpcLineChecks.NormalizeSpeech("여쭤볼게", "반말", "밤바다") == "물어볼게"
                && NpcLineChecks.NormalizeSpeech("모시고 갈게", "반말", "밤바다") == "데리고 갈게"
                && NpcLineChecks.NormalizeSpeech("기다렸습니다", "반말", "밤바다") == "기다렸어"
                && NpcLineChecks.NormalizeSpeech("계시죠?", "반말", "밤바다") == "있지?"
                && NpcLineChecks.NormalizeSpeech("그러셨구나", "반말", "밤바다") == "그랬구나",
                "말씀드리다·드렸·여쭈다·모시다·~ㅆ습니다·계시다·그러셨 의 존대 잔재를 반말로");
        // 2026-11-17: 플레이어를 '너·네가·너를'로 부르지 않고 친밀도 0~2단계는 '발굴자', 3단계 이상은 닉네임으로 말한다 (조사는 받침에 맞춤)
        Check(NpcLineChecks.NormalizeSpeech("네가 관리해 줘서 고마워. 너를 기다렸어, 너는 최고야!", "반말", null, null, null, null, "발굴자") == "관리해 줘서 고마워. 기다렸어, 최고야!"
                && NpcLineChecks.NormalizeSpeech("네가 관리해 줘서 고마워. 너를 기다렸어, 너는 최고야!", "반말", null, null, null, null, "도윤") == "관리해 줘서 고마워. 기다렸어, 최고야!",
                "플레이어에게 플레이어를 남 얘기하듯 말하지 않음: 네가·너를·너는은 이름으로 바꾸지 않고 지움 (22:27 시험 뒤 \"오랜만에 도윤을 찾아줘서\")");
        Check(NpcLineChecks.NormalizeSpeech("너랑 놀면 좋아. 너한테 자랑하고 싶어. 네 덕분에 개운해.", "반말", null, null, null, null, "도윤") == "도윤이랑 놀면 좋아. 자랑하고 싶어. 덕분에 개운해."
                && NpcLineChecks.NormalizeSpeech("너랑 놀면 좋아. 너한테 자랑하고 싶어. 네 덕분에 개운해.", "반말", null, null, null, null, "발굴자") == "발굴자랑 놀면 좋아. 자랑하고 싶어. 덕분에 개운해.",
                "너랑은 이름 + 받침에 맞는 조사(줄일 수 없음), 너한테·네 덕분에는 지움");
        Check(NpcLineChecks.NormalizeSpeech("너도 올 줄 알았어. 넌 정말 다정해, 널 좋아해. 너무 고마워.", "반말", null, null, null, null, "도윤") == "도윤도 올 줄 알았어. 정말 다정해, 좋아해. 너무 고마워.",
                "넌·널은 지우고 너도는 이름으로, '너무'처럼 낱말의 일부인 '너'는 건드리지 않음");
        Check(NpcLineChecks.NormalizeSpeech("네? 네 알겠어. 오늘은 네 생각이 났어, 너의 손길이 좋아.", "반말", null, null, null, null, "발굴자") == "네? 네 알겠어. 오늘은 생각이 났어, 손길이 좋아.",
                "대답 '네?'·'네 알겠어'는 그대로, '네 생각'·'너의 손길'은 상대 이름 없이 지움");
        // 2026-11-17 시험(64건): 기록 없는 탭 인사의 "오랜만이야"·"또 왔구나"는 거르지 않고 고치고(탭 인사 16건 중 4건이 이것으로 다시 만들었다), "덕분에 관리해 줘서"는 줄인다
        Check(NpcLineChecks.NormalizeSpeech("왔구나, 오랜만이야!", "반말", "말발굽", null, null, null, "발굴자", true) == "왔구나, 반가워!"
                && NpcLineChecks.NormalizeSpeech("오랜만에 왔구나, 반가워!", "반말", "말발굽", null, null, null, "발굴자", true) == "왔구나, 반가워!"
                && NpcLineChecks.NormalizeSpeech("또 왔구나! 오랜만이야.", "반말", "말발굽", null, null, null, "발굴자", true) == "왔구나! 반가워."
                && NpcLineChecks.NormalizeSpeech("왔구나, 오랜만이야!", "반말", "말발굽", null, null, null, "발굴자", false) == "왔구나, 오랜만이야!",
                "기록이 없는 주인 장면: '오랜만이야'→'반가워', '오랜만에'·'또 왔구나'→'왔구나' (기록이 있으면 그대로)");
        Check(NpcLineChecks.NoRecordApplies(false, false, "주인이 아침에 너를 보러 찾아왔다.") && !NpcLineChecks.NoRecordApplies(false, true, "주인이 아침에 너를 보러 찾아왔다.")
                && !NpcLineChecks.NoRecordApplies(true, false, "최근 30일 동안 4번 온 친구가 전시관을 둘러보고 있다.")
                && !NpcLineChecks.NoRecordApplies(false, false, "주인이 오랜만에 관리하기를 골랐고, 주인이 너를 관리해 주었다."),
                "고치는 때: 손님 장면이 아니고, 기록이 없고, 상황 문장에 그 낱말이 없을 때");
        Check(NpcLineChecks.NormalizeSpeech("덕분에 관리해 줘서 고마워.", "반말", "묘작이") == "관리해 줘서 고마워."
                && NpcLineChecks.NormalizeSpeech("덕분에 닦아 주셔서 개운해요.", "반말", "묘작이") == "닦아 줘서 개운해."
                && NpcLineChecks.NormalizeSpeech("덕분에 몸이 가벼워졌어.", "반말", "묘작이") == "덕분에 몸이 가벼워졌어.",
                "'덕분에 관리해 줘서'는 고마움이 겹쳐 '관리해 줘서'로 (그 밖의 '덕분에'는 그대로)");
        Check(!NpcLineChecks.HasAsk("스스로 닦아야 해.") && !NpcLineChecks.HasAsk("이틀 전에 닦아 줘서 고마워.") && NpcLineChecks.HasAsk("살살 닦아 줄래?") && NpcLineChecks.HasAsk("손길이 필요해.")
                && NpcLineChecks.HasAsk("닦아 주면 좋겠어."), "부탁은 낱말(닦·깨워…)만이 아니라 부탁하는 꼴(~줘·~줄래·~좋겠·~필요)이어야 함 (\"스스로 닦아야 해\"는 부탁이 아님)");
        // 리뷰 반영(CodeRabbit)
        Check(!NpcLineChecks.HasAsk("살살 닦아 줄까?") && !NpcLineChecks.IsCleanAsk("살살 닦아 줄까?") && !NpcLineChecks.IsCleanAsk("닦아 주겠어.") && NpcLineChecks.HasAsk("살살 닦아 줄래?") && NpcLineChecks.IsCleanAsk("살살 닦아 줄래?"),
                "'닦아 줄까?'는 도깨비의 제안이라 부탁이 아님 (플레이어가 닦아 달라는 '닦아 줄래?'만 부탁)");
        Check(NpcLineChecks.TimeContradiction("지금은 오전이야, 반가워!", "밤") == "오전" && NpcLineChecks.TimeContradiction("오전이야, 반가워!", "오전") == null
                && NpcLineSlots.Slotify("오전에 와 줬구나.", new SlotValues { TimeWord = "오전", Address = "발굴자" }) == "{시간대}에 와 줬구나.",
                "시간대 낱말에 '오전' 포함 (밤에 '오전이야'는 거르고, 오전에는 통과·칸으로 보관)");
        Check(NpcLineChecks.IncompleteEnding("어서 와, 다은.", "다은") == null && NpcLineChecks.IncompleteEnding("어서 와, 다은.") != null && NpcLineChecks.IncompleteEnding("내 꼬리를", "다은") != null,
                "닉네임으로 끝나는 부르는 말의 끝 글자('은')는 끊긴 조사가 아님, 진짜 끊긴 말은 계속 거름");
        var reviewValidator = new NpcOutputValidator(new NpcLineFilter(forbidden), personas);
        ReactionRequest plainStatus = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        Expect(reviewValidator, Out("answer_status", "player", "네가 관리를 안 해줘서 슬펐어."), plainStatus, OutputProblem.Forbidden, "'네가'를 지워도 플레이어를 탓하는 말은 거름 (끝에서 끝까지)");
        ReactionRequest mixedTargets = Request(8, InteractionMode.Owner, NpcInputs.Tap);
        mixedTargets.Candidates.Add(new ActionCandidate(NpcActions.Greet));
        mixedTargets.Candidates.Add(new ActionCandidate(NpcActions.AnswerStatus, NpcTargets.Player));
        OutputCheck noTarget = reviewValidator.Check("{\"action\": \"greet\", \"line\": \"어서 와, 반가워!\"}", mixedTargets);
        Check(noTarget.IsValid && noTarget.TargetId == NpcTargets.None, "대상 없는 행동(인사)을 모델이 target 없이 내도 'none' 으로 채움 ← " + noTarget.Problem + " " + noTarget.TargetId);
        ReactionRequest askSim = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        askSim.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        askSim.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, "9"));
        askSim.Variation = 5;
        askSim.RecentLines = new List<string> { "마지막 관리가 9일 전이었어. 살살 닦아 줘." };
        OutputCheck prefixOnly = reviewValidator.Check(Out("answer_status", "player", "시간 되면 닦아 줄래?"), askSim);
        Check(prefixOnly.IsValid && prefixOnly.Line == "마지막 관리가 9일 전이었어. 시간 되면 닦아 줄래?", "앞 문장(코드가 정한 사실)만 같은 최근 대사는 반복으로 거르지 않음 ← " + prefixOnly.Problem + " " + prefixOnly.Detail);
        // 부탁 부분까지 같으면(2026-11-25) 거르지 않고 코드의 부탁 문장 중 최근 대사와 겹치지 않는 것으로 바꾼다 — 기본 대사(그 상황에 둘뿐)로 넘어가지 않는다
        askSim.RecentLines = new List<string> { "마지막 관리가 9일 전이었어. 시간 되면 닦아 줄래?" };
        OutputCheck sameAsk = reviewValidator.Check(Out("answer_status", "player", "시간 되면 닦아 줄래?"), askSim);
        Check(sameAsk.IsValid && sameAsk.Line.StartsWith("마지막 관리가 9일 전이었어. ", StringComparison.Ordinal) && sameAsk.Line != askSim.RecentLines[0]
                && !PromptAssembler.FindRepeat(sameAsk.Line, askSim.RecentLines).HasValue
                && sameAsk.ToResult("r1").Note != null && sameAsk.ToResult("r1").Note.Contains(OutputCheck.AskReplacedNote),
                "흐려진 안부: 부탁 부분까지 같으면 겹치지 않는 코드의 부탁 문장으로 바꿔 쓰고 메모에 남김 ← " + sameAsk.Line + " " + sameAsk.Problem + " / " + sameAsk.ToResult("r1").Note);
        askSim.RecentLines = new List<string>();
        foreach (string a in new[] { "살살 닦아 줄래?", "시간 되면 닦아 줄래?", "조심조심 닦아 줘.", "괜찮으면 닦아 줄래?", "이왕이면 닦아 줘." })
        {
            askSim.RecentLines.Add("마지막 관리가 9일 전이었어. " + a);
        }
        Expect(reviewValidator, Out("answer_status", "player", "시간 되면 닦아 줄래?"), askSim, OutputProblem.Repeated,
                "흐려진 안부: 코드의 부탁 문장까지 모두 최근 대사면 계속 반복으로 거름");
        // 바꾼 대사도 출력 검사의 길이 제한을 지킨다 — 넘는 부탁 문장은 고르지 않는다 (2026-11-25 리뷰)
        askSim.RecentLines = new List<string> { "마지막 관리가 9일 전이었어. 시간 되면 닦아 줄래?" };
        string fitsAsk = PromptAssembler.AskFallbackAvoiding(askSim, "마지막 관리가 9일 전이었어.", askSim.RecentLines, int.MaxValue);
        string shortestAllowed = PromptAssembler.AskFallbackAvoiding(askSim, "마지막 관리가 9일 전이었어.", askSim.RecentLines, "마지막 관리가 9일 전이었어. ".Length + 8);
        Check(fitsAsk != null && PromptAssembler.AskFallbackAvoiding(askSim, "마지막 관리가 9일 전이었어.", askSim.RecentLines, 10) == null
                && (shortestAllowed == null || ("마지막 관리가 9일 전이었어. " + shortestAllowed).Length <= "마지막 관리가 9일 전이었어. ".Length + 8),
                "흐려진 안부: 바꿀 부탁 문장은 길이 제한 안에 드는 것만 고름 ← 제한 없음 '" + fitsAsk + "', 8자 '" + shortestAllowed + "'");
        // 2026-11-24: 앞 문장까지 포함한 전체 대사로 본다 — 앞 문장이 다르면 부탁 부분이 같아도 다른 대사, 꼴이 같으면(날 수만 다름) 같은 대사
        askSim.RecentLines = new List<string> { "몸이 흐려졌어. 시간 되면 닦아 줄래?", "다시 봉인될 만큼 흐려졌어. 시간 되면 닦아 줄래?" };
        OutputCheck otherLeadAsk = reviewValidator.Check(Out("answer_status", "player", "시간 되면 닦아 줄래?"), askSim);
        Check(otherLeadAsk.IsValid && otherLeadAsk.Line == "마지막 관리가 9일 전이었어. 시간 되면 닦아 줄래?",
                "흐려진 안부: 앞 문장이 다른 최근 대사와는 부탁 부분이 같아도 반복으로 거르지 않음 ← " + otherLeadAsk.Problem + " " + otherLeadAsk.Detail);
        askSim.RecentLines = new List<string> { "마지막 관리가 12일 전이었어. 시간 되면 닦아 줄래?" };
        OutputCheck otherDaysAsk = reviewValidator.Check(Out("answer_status", "player", "시간 되면 닦아 줄래?"), askSim);
        Check(otherDaysAsk.IsValid && otherDaysAsk.Line != "마지막 관리가 9일 전이었어. 시간 되면 닦아 줄래?",
                "흐려진 안부: 날 수만 다른 같은 꼴의 앞 문장 + 같은 부탁은 반복으로 보고 다른 부탁 문장으로 바꿈 ← " + otherDaysAsk.Line);
        Check(PromptAssembler.SimilarLine("몸이 흐려지다 못해 봉인됐어. 살살 깨워 줄래?", "몸이 흐려지다 못해 봉인됐어. 괜찮으면 깨워 줄래?") == null
                && NpcLineChecks.Similar("몸이 흐려지다 못해 봉인됐어. 살살 깨워 줄래?", "몸이 흐려지다 못해 봉인됐어. 괜찮으면 깨워 줄래?") != null
                && PromptAssembler.SimilarLine("몸이 흐려졌어. 살살 닦아 줄래?", "마지막 관리가 9일 전이었어. 살살 닦아 줄래?") == null
                && PromptAssembler.SimilarLine("마지막 관리가 {날수}일 전이었어. 살살 닦아 줄래?", "마지막 관리가 9일 전이었어. 살살 닦아 줄래?") != null
                && PromptAssembler.SimilarLine("어서 와, 반가워!", "어서 와, 반가워!") != null && PromptAssembler.SimilarLine("어서 와, 반가워!", "드디어 찾았네!") == null,
                "SimilarLine: 같은 앞 문장은 부탁 부분끼리(전체로 보면 '시작이 같음'으로 걸리던 것), 다른 앞 문장은 전체끼리, 흐려진 안부가 아니면 Similar 와 같음");
        // 2026-11-17 22:27 시험 뒤(2): 플레이어에게 플레이어를 남 얘기하지 않음, 자기 것을 '그 자개'라고 하지 않음
        Check(NpcLineChecks.NormalizeSpeech("오랜만에 도윤을 찾아줘서 고마워!", "반말", "밤바다", null, "도윤", null, "도윤", true) == "찾아줘서 고마워!"
                && NpcLineChecks.NormalizeSpeech("오랜만에 너를 찾아줘서 고마워!", "반말", "밤바다", null, "도윤", null, "도윤", true) == "찾아줘서 고마워!"
                && NpcLineChecks.NormalizeSpeech("발굴자가 날 관리해줘서 고마워!", "반말", "밤바다", null, null, null, "발굴자") == "날 관리해줘서 고마워!",
                "\"오랜만에 도윤을 찾아줘서\": 플레이어 이름·2인칭이 주어·목적어로 들어가면 이름으로 바꾸지 않고 지움 (지운 뒤 '오랜만에' 기록 고침도 다시)");
        Check(NpcLineChecks.DropPlayerReference("고마워, 도윤!", "도윤") == "고마워, 도윤!" && NpcLineChecks.DropPlayerReference("도윤아 안녕, 반가워.", "도윤") == "도윤아 안녕, 반가워."
                && NpcLineChecks.DropPlayerReference("도윤이 보고 싶었어.", "도윤") == "보고 싶었어." && NpcLineChecks.DropPlayerReference("도윤이야, 반가워.", "도윤") == "도윤이야, 반가워.",
                "부르는 말(도윤아·도윤이야·고마워, 도윤!)은 두고, 이어지는 말이 있는 주어·목적어만 지움");
        Check(NpcLineChecks.PlayerThirdPerson("정말 고마워, 도윤을!", "도윤") != null && NpcLineChecks.PlayerThirdPerson("도윤한테 고마워", "도윤") != null
                && NpcLineChecks.PlayerThirdPerson("도윤아, 고마워!", "도윤") == null, "고친 뒤에도 남은 남 얘기식 이름은 거름");
        var ownThings = new List<string> { "자개", "옻칠", "몸" };
        Check(NpcLineChecks.FixCraftSpectator(NpcLineChecks.FixOwnPossessive("그 자개 공들여 만들었네", ownThings)) == "내 자개 공들여 만들었어"
                && NpcLineChecks.FixOwnPossessive("이 자개는 사흘 전 자랑했어", ownThings) == "내 자개는 사흘 전 자랑했어"
                && NpcLineChecks.FixOwnPossessive("그 모습이 보기 좋아", ownThings) == "그 모습이 보기 좋아"
                // 2026-11-19: 앞에 아무것도 없는 자기 것 + 솜씨 낱말은 '내'를 붙인다. 주어로 쓴 것(남의 솜씨처럼 말한 것)은 주체 검사가 거른다
                && NpcLineChecks.FixOwnPossessive("기억난다, 자개 공들여 만들었다고 자랑했던 일", ownThings) == "기억난다, 내 자개 공들여 만들었다고 자랑했던 일"
                && NpcLineChecks.FixOwnPossessive("오, 자개가 정말 정성스럽게 만들어졌네.", ownThings) == "오, 자개가 정말 정성스럽게 만들어졌네."
                && NpcLineChecks.FixOwnPossessive("자개 반짝여서 좋아", ownThings) == "자개 반짝여서 좋아"
                && NpcLineChecks.FixOwnPossessive("몸 정성껏 닦았어", ownThings) == "몸 정성껏 닦았어" && NpcLineChecks.FixCraftSpectator("오늘은 기분이 좋네.") == "오늘은 기분이 좋네.",
                "\"그 자개 공들여 만들었네\": 자기 것은 '그·이'가 아니라 '내'로, 자기 솜씨를 구경하듯 '~었네'로 끝내지 않음 (자개 주인을 플레이어로 가정한 말)");
        // 2026-11-17 22:27 시험 뒤: 인사의 지어낸 사정, 부탁 문장 꼴, 겹친 '또', 오타
        Check(NpcLineChecks.FixGreetingTail("왔구나, 반가워, 무슨 일인지 궁금하네.") == "왔구나, 반가워."
                && NpcLineChecks.FixGreetingTail("와, 반가워. 뭘 찾으러 왔어?") == "와, 반가워."
                && NpcLineChecks.FixGreetingTail("어서 와 도윤아, 오늘은 무슨 일이 있길래 찾아왔어?") == "어서 와 도윤아."
                && NpcLineChecks.FixGreetingTail("또 와줘서 고마워, 향로랑 친구들 보고 싶었어?") == "또 와줘서 고마워."
                && NpcLineChecks.FixGreetingTail("안녕, 반가워?") == "안녕, 반가워!"
                && NpcLineChecks.FixGreetingTail("어서 와, 반가워!") == "어서 와, 반가워!",
                "인사에 붙은 지어낸 사정·캐묻기 마디는 지우고 반가움 마디만 남김");
        Check(NpcLineChecks.GreetingProbe("안녕, 오늘은 좀 일찍 왔구나.", NpcActions.Greet) != null && NpcLineChecks.GreetingProbe("와, 처음 온 친구야?", NpcActions.WanderGreet) != null
                && NpcLineChecks.GreetingProbe("어서 와, 반가워!", NpcActions.Greet) == null && NpcLineChecks.GreetingProbe("무슨 일이야?", NpcActions.ThankCalm) == null,
                "다 지워도 남는 지어낸 사정은 인사 행동에서만 거름");
        Check(NpcLineChecks.IsCleanAsk("시간 되면 닦아 줄 수 있어?") && NpcLineChecks.IsCleanAsk("살살 깨워줘.") && NpcLineChecks.IsCleanAsk("오늘은 닦아 줄래?")
                && !NpcLineChecks.IsCleanAsk("괜찮으면 도와줄래?") && !NpcLineChecks.IsCleanAsk("조심조심 들어와, 내 마음을 좀 닦아줄래?") && !NpcLineChecks.IsCleanAsk("이왕이면 닦아줄게, 몸이 많이 흐려 보여서 걱정이야.")
                && !NpcLineChecks.IsCleanAsk("조심조심 찾아와 줘서 고마워, 잠깐 깨워줄 수 있을까?"),
                "부탁 문장은 '(말머리) 닦아·깨워 줄래?' 꼴만 통과, 그 밖은 코드가 채움");
        Check(NpcLineChecks.FixTypos(NpcLineChecks.FixLongAbsence("또 왔네, 오랜만에 와서 반갑아!", "어제")) == "또 왔네, 와서 반가워!", "이미 '또'가 있으면 '오랜만에'를 '또'로 바꾸지 않고 지움 (\"또 왔네, 또 와서\" 겹침 방지)");
        Check(NpcLineChecks.ReversedCall("드디어 도윤을 만나러 왔다니 정말 반갑다!", "도윤") != null && NpcLineChecks.ReversedCall("발굴자를 보러 왔구나.", "발굴자") != null && NpcLineChecks.ReversedCall("도윤이 만나러 왔구나.", "도윤") == null,
                "도깨비가 플레이어를 '만나러 왔다'고 하면 주체가 뒤바뀐 말");
        // 2026-11-17 22:07 시험 뒤: 전시관 인상 말하기·부탁 빠짐
        var impressionFilter = new NpcLineFilter(forbidden);
        Check(impressionFilter.Find("전시관이 참 포근하네.") != null && impressionFilter.Find("어서 와, 전시관이 정말 넓군!") != null && impressionFilter.Find("어서 와, 전시관에서 편히 쉬어.") == null
                && impressionFilter.Find("혹시 안부를 물어봐줘?") != null, "전시관의 인상을 말하는 대사(\"전시관이 참 포근하네\")는 거르고, 맞이하는 말은 통과");
        Check(!NpcLineChecks.HasAsk("안부를 물어봐줘?") && !NpcLineChecks.HasAsk("한번 살펴봐 줘.") && NpcLineChecks.HasAsk("조심조심 닦아 줘."),
                "'물어봐줘'·'살펴봐 줘'는 닦아 달라는 부탁이 아님");
        // 2026-11-17 21:45 시험 뒤: 구조 고침 — 자기 이야기의 발견 어미, 낱말을 지우는 고침, 감정 표현 줄
        Check(NpcLineChecks.DiscoveryEnding("덕분에 기쁨이 넘쳐서 폴짝폴짝 뛰는구나! 고마워!", NpcActions.ThankJoyful) == "덕분에 기쁨이 넘쳐서 폴짝폴짝 뛰는구나!"
                && NpcLineChecks.DiscoveryEnding("왔구나, 반가워!", NpcActions.Greet) == null && NpcLineChecks.DiscoveryEnding("관리해 줘서 고마워!", NpcActions.ThankJoyful) == null
                && NpcLineChecks.DiscoveryEnding("너도 정성들여 만들었구나.", NpcActions.RecallTalk) != null,
                "감사·안부·기억(자기 이야기)에 '~구나'를 쓰면 거름, 인사('왔구나')는 통과");
        Check(NpcLineChecks.NormalizeSpeech("어서 왔구나! 오랜만에 보네, 도윤아.", "반말", "말발굽", null, "도윤", null, "도윤", true) == "어서 와! 반가워, 도윤아."
                && NpcLineChecks.NormalizeSpeech("오랜만에 왔구나, 반가워!", "반말", "말발굽", null, null, null, "발굴자", true) == "왔구나, 반가워!"
                && NpcLineChecks.NormalizeSpeech("오랜만에 보니 좋아.", "반말", "말발굽", null, null, null, "발굴자", true) == "만나니 좋아."
                && NpcLineChecks.NormalizeSpeech("오랜만에 만나서 반가워!", "반말", "말발굽", null, null, null, "발굴자", true) == "만나서 반가워!",
                "'오랜만에'는 움직임말 앞에서만 지우고, \"오랜만에 보네\"는 \"반가워\"로 바꿔 토막(\"보네\")을 남기지 않음 (22:07 시험: 남겨 두자 거르고 다시 만들어 시도가 늘었다)");
        Check(!new PromptAssembler(personas, actions).SystemFor(8).Contains("감정 표현:") && !new PromptAssembler(personas, actions).SystemFor(9).Contains("감정 표현:"),
                "시스템 메시지에 '감정 표현' 줄이 없음 (어흠·히힛을 말 가운데에 끼우거나 몸짓을 서술하게 하던 줄)");
        // 2026-11-17 21:27 시험: 어색한 대사 3종과 재시도(어미 섞임 '께서')를 줄이는 고침
        Check(NpcLineChecks.NormalizeSpeech("발굴자께서 와 주셔서 고마워.", "반말", "향산이", null, null, null, "발굴자") == "와 줘서 고마워."
                && NpcLineChecks.NormalizeSpeech("도윤님께서 오셨네, 도윤님께 말할게.", "반말", "향산이", null, "도윤", null, "도윤") == "왔네, 말할게.",
                "호칭에 붙은 '께서'·'께'는 반말로 고친 뒤 남 얘기가 되지 않게 지움 (다시 만들지 않는다)");
        Check(NpcLineChecks.FixTodayAlso("마지막 관리가 16일 전이라서, 오늘도 향로를 닦아 줘.") == "마지막 관리가 16일 전이라서, 오늘은 향로를 닦아 줘.",
                "부탁·기록 없는 인사의 '오늘도'는 '오늘은'으로");
        var todayAsk = Request(2, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        todayAsk.Situation = "주인이 안부를 물었다. 너는 오래 관리를 받지 못해 흐려졌다. 주인에게 닦아 달라고 부탁한다.";
        todayAsk.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        Check(new NpcOutputValidator(new NpcLineFilter(forbidden), personas).Check(Out("answer_status", "player", "오늘도 닦아 줄래?"), todayAsk).Line == "몸이 흐려졌어. 오늘은 닦아 줄래?", "흐려진 안부의 '오늘도'를 고쳐 통과");
        Check(!PromptAssembler.RuleBond.Contains("호칭") && Array.IndexOf(new[] { "덕분에", "정말", "훨씬", "몸이", "개운하게", "깨끗하게" }, "이제") < 0,
                "'이제 관리해 줘서 고마워'를 낳는 첫 낱말 '이제'는 감사 첫 낱말에서 뺐음 (정말로 대체)");
        // 2026-11-17 21:09 시험: 모델이 읽는 글의 '주인'을 호칭(발굴자·닉네임)으로 — "주인"을 읽은 모델이 하인처럼 높임을 썼다
        Check(NpcLineChecks.OwnerToCall("주인이 안부를 물었다. 주인에게 닦아 달라고 부탁한다.", "발굴자") == "발굴자가 안부를 물었다. 발굴자에게 닦아 달라고 부탁한다."
                && NpcLineChecks.OwnerToCall("주인이 안부를 물었다. 주인을 보고 주인과 주인의 일, 주인께서 주인은 주인도", "도윤") == "도윤이 안부를 물었다. 도윤을 보고 도윤과 도윤의 일, 도윤이 도윤은 도윤도"
                && NpcLineChecks.OwnerToCall("주인공과 집주인은 그대로, 주인이다", "도윤") == "주인공과 집주인은 그대로, 주인이다",
                "'주인'은 호칭으로, 조사는 받침에 맞춤 ('주인공'·'집주인'은 그대로)");
        ReactionRequest namedAsk = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        namedAsk.Nickname = "도윤";
        namedAsk.Intimacy = NpcAddress.NicknameLevel;
        namedAsk.Situation = "주인이 아침에 너를 보러 찾아왔다.";
        string namedUser = new PromptAssembler(personas, actions).Build(namedAsk).User;
        Check(namedUser.StartsWith("지금 상대: 도윤(한 번 불러 준다)\n대하는 태도: ") && !namedUser.Contains("호칭:") && namedUser.Contains("상황: 도윤이 아침에 너를 보러 찾아왔다.") && !namedUser.Contains("주인"),
                "프롬프트에 '주인'이 없음: 지금 상대·대할 때·상황이 모두 호칭 ← " + namedUser);
        Check(!new PromptAssembler(personas, actions).SystemFor(8).Contains("주인") && PromptAssembler.RuleRole.Contains("상대가 너를 닦아") && !PromptAssembler.FlavorsEnabled,
                "시스템 규칙에도 '주인'이 없고(상대), 곁들일 거리 줄은 싣지 않음");
        Check(NpcLineChecks.NormalizeSpeech("환영해, 전시관 구경 즐기길 바래.", "반말", "향산이") == "환영해, 전시관 구경 즐기길 바라."
                && NpcLineChecks.NormalizeSpeech("몇일 됬어, 금새 왔네.", "반말", "향산이") == "며칠 됐어, 금세 왔네.",
                "흔한 오탈자는 다시 만들지 않고 고침 (바래→바라·몇일→며칠·됬→됐)");
        ReactionRequest careFact = Request(9, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankJoyful);
        careFact.Situation = "주인이 너를 관리해 주었다.";
        careFact.Facts.Add(new Fact(NpcFactKeys.CareResult, "재봉인→선명"));
        Check(!new PromptAssembler(personas, actions).Build(careFact).User.Contains("재봉인"), "관리 결과 사실(재봉인→선명)을 싣지 않음 (\"재봉인이 선명해졌네\"로 옮겼다)");
        // 흐려진 안부는 사실을 하나만 말한다: 요청마다 번갈아 "상태"(Variation 짝수) 또는 "마지막 관리가 며칠 전"(홀수) 한 가지만 프롬프트에 싣는다
        var askAssembler = new PromptAssembler(personas, actions);
        ReactionRequest askFacts = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        askFacts.Situation = "주인이 안부를 물었다. 너는 오래 관리를 받지 못해 흐려졌다. 주인에게 닦아 달라고 부탁한다.";
        askFacts.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        askFacts.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, "9"));
        askFacts.Variation = 0;
        string stateUser = askAssembler.Build(askFacts).User;
        askFacts.Variation = 1;
        string daysUser = askAssembler.Build(askFacts).User;
        Check(stateUser.Contains("상황: 발굴자가 너를 보러 찾아왔다.\n이미 한 말: 몸이 흐려졌어.\n") && !stateUser.Contains("알고 있는 것") && !stateUser.Contains("마지막 관리") && !stateUser.Contains("몸 상태:")
                && stateUser.Contains("할 일: 이어서 닦아 달라고 부탁하는 말을 한다") && stateUser.Contains("대사: 한국어 반말, 12자 이내, 첫 낱말로 시작, 부탁 한 문장"),
                "상태 요청(짝수): 사실 문장은 코드가 '이미 한 말'로 말하고 모델은 부탁 한 문장만 ← " + stateUser);
        Check(daysUser.Contains("상황: 발굴자가 너를 보러 찾아왔다.\n이미 한 말: 마지막 관리가 9일 전이었어.\n") && !daysUser.Contains("안부를 물") && !daysUser.Contains("몸이 흐려") && !daysUser.Contains("몸 상태:") && daysUser.Contains("첫 낱말: ")
                && !daysUser.Contains("며칠 전"),
                "며칠 전 요청(홀수): 서버가 준 값 N(9)으로 코드가 만든 사실 문장 ← " + daysUser);
        askFacts.Variation = 2;
        string state2 = askAssembler.Build(askFacts).User;
        askFacts.Variation = 3;
        string days2 = askAssembler.Build(askFacts).User;
        Check(state2.Contains("이미 한 말: 몸이 많이 흐려졌어.") && days2.Contains("이미 한 말: 관리받은 지 9일이 지났어."), "같은 요청이 거듭되면 앞 문장의 표현도 바뀜");
        Check(PromptAssembler.LinePrefix(askFacts) == "관리받은 지 9일이 지났어." && PromptAssembler.AskTask(askFacts) == "이어서 닦아 달라고 부탁하는 말을 한다",
                "LinePrefix·AskTask 가 프롬프트와 같은 값을 돌려줌");
        // 재봉인은 깨워 달라고
        ReactionRequest resealedAsk = Request(2, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        resealedAsk.Facts.Add(new Fact(NpcFactKeys.Clarity, "재봉인"));
        Check(PromptAssembler.LinePrefix(resealedAsk) == "몸이 흐려지다 못해 봉인됐어." && PromptAssembler.AskTask(resealedAsk) == "이어서 깨워 달라고 부탁하는 말을 한다", "재봉인: 깨워 달라고 이어 부탁");
        askFacts.Variation = 1;
        // 검사기: 코드가 정한 앞 문장 + 모델의 부탁 문장
        var prefixValidator = new NpcOutputValidator(new NpcLineFilter(forbidden), personas);
        OutputCheck joined = prefixValidator.Check(Out("answer_status", "player", "오늘도 닦아 줄래?"), askFacts);
        Check(joined.IsValid && joined.Line == "마지막 관리가 9일 전이었어. 오늘은 닦아 줄래?", "모델의 부탁 문장 앞에 코드의 사실 문장을 붙임 ← " + joined.Line + " " + joined.Detail);
        // 22:07 시험 뒤: 거르고 다시 만들지 않고 코드가 부탁 문장을 채운다 (사실 문장은 이미 코드가 말했다)
        string filled = "마지막 관리가 9일 전이었어. " + PromptAssembler.AskFallback(askFacts);
        OutputCheck twoFacts = prefixValidator.Check(Out("answer_status", "player", "몸이 흐려져서 닦아 줄래?"), askFacts);
        Check(twoFacts.IsValid && twoFacts.Line == filled, "며칠 전 요청에서 모델이 상태까지 말하면 사실이 둘이라 모델의 부분을 코드의 부탁 문장으로 바꿈 ← " + twoFacts.Line + " " + twoFacts.Detail);
        OutputCheck noAsk = prefixValidator.Check(Out("answer_status", "player", "스스로 해야 해."), askFacts);
        Check(noAsk.IsValid && noAsk.Line == filled && NpcLineChecks.HasAsk(noAsk.Line), "부탁이 없으면 코드가 채움 (지어낸 설정으로 새지 않고 다시 만들지도 않음) ← " + noAsk.Line);
        OutputCheck echoed = prefixValidator.Check(Out("answer_status", "player", "혹시 안부를 물어봐줘?"), askFacts);
        Check(echoed.IsValid && echoed.Line == filled, "22:07 시험 \"혹시 안부를 물어봐줘?\"는 부탁이 아니라 코드가 채움 ← " + echoed.Line);
        Check(PromptAssembler.AskFallback(askFacts).Contains("닦아") && PromptAssembler.AskFallback(resealedAsk).Contains("깨워"), "채우는 부탁은 재봉인이면 깨워, 아니면 닦아");
        askFacts.Variation = 1;
        askFacts.Facts.RemoveAll(f => f.Key == NpcFactKeys.LastCareDaysAgo);
        Check(PromptAssembler.LinePrefix(askFacts) == "몸이 흐려졌어." && !askAssembler.Build(askFacts).User.Contains("지났"), "마지막 관리 사실이 없으면 홀수 요청도 상태");
        Check(NpcLineChecks.MixedAskFacts("마지막 관리가 9일 전이었어, 흐려져서 닦아 줄래?") && NpcLineChecks.MixedAskFacts("흐려진 몸이지만, 마지막 관리가 9일 전이라 닦아 줘.")
                && !NpcLineChecks.MixedAskFacts("마지막 관리가 9일 전이었어. 닦아 줄래?") && !NpcLineChecks.MixedAskFacts("몸이 흐려졌어. 닦아 줄래?"),
                "상태와 며칠 전을 함께 말하면 안전망이 걸러 다른 사실로 다시 만든다");
        // 2026-11-17 20:39 시험: 두 문장으로 길어져 시간·반복·길이가 나빠졌다 — 상황에 맞는 한 문장으로 줄인다. "발굴자를 불러서"처럼 플레이어를 부른 것으로 뒤바뀐 말은 거른다
        Check(NpcLineChecks.KeepOneSentence("자꾸 흐려져서 몸이 무거워. 깨끗해질 수 있게 닦아 줄래?", true) == "깨끗해질 수 있게 닦아 줄래?"
                && NpcLineChecks.KeepOneSentence("많이 흐려졌어. 마지막 관리는 9일 전이었어. 닦아줘.", true) == "마지막 관리는 9일 전이었어. 닦아줘."
                && NpcLineChecks.KeepOneSentence("이대로 흐려지다니 안타까워. 몸이 무거워.", true) == "이대로 흐려지다니 안타까워. 몸이 무거워."
                && NpcLineChecks.KeepOneSentence("왔구나, 반가워! 오늘도 수고 많았어.", false) == "왔구나, 반가워!"
                && NpcLineChecks.KeepOneSentence("고마워. 몸이 선명해졌어.", false) == "고마워. 몸이 선명해졌어."
                && NpcLineChecks.KeepOneSentence("한 문장이야.", false) == "한 문장이야."
                && NpcLineChecks.KeepOneSentence("몸이 반짝반짝해져서 기뻐! 발굴자가 관리해 줘서 고마워!", false) == "발굴자가 관리해 줘서 고마워!"
                && NpcLineChecks.KeepOneSentence("훨씬 더 깨끗해진 것 같아. 이틀 전에 관리받아서 고마워.", false) == "이틀 전에 관리받아서 고마워.",
                "두 문장 이상이면 줄이기: 부탁이 든 문장, 앞 문장에 며칠 전 사실이 있으면 그것과 함께, 너무 짧으면 첫 문장과 함께 (부탁이 없으면 그대로 두어 부탁 없음으로 거름)");
        Check(NpcLineChecks.ReversedCall("안녕, 발굴자를 불러서 반가워.", "발굴자") == "발굴자를 불러"
                && NpcLineChecks.ReversedCall("도윤을 부르니 반가워.", "도윤") == "도윤을 부르"
                && NpcLineChecks.ReversedCall("발굴자가 나를 불러서 반가워.", "발굴자") == null
                && NpcLineChecks.ReversedCall("반가워, 도윤아!", "도윤") == null,
                "플레이어를 목적어로 부르는 말(주체 뒤바뀜)만 잡음");
        // 대사 줄: 15자 이하(탭 인사·손님 관람)는 "(짧아도 된다)" 없이 첫 낱말로 시작하는 한 문장, 흐려진 안부는 끝을 부탁으로
        var ruleAssembler = new PromptAssembler(personas, actions);
        ReactionRequest tapRule = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        string tapUser = ruleAssembler.Build(tapRule).User;
        Check(tapUser.Contains("대사: 한국어 반말, 15자 이내, 첫 낱말로 시작해 반가운 마음을 말한다") && !tapUser.Contains("(짧아도 된다)"),
                "탭 인사의 대사 줄: 짧아도 된다는 말 없이 첫 낱말로 시작하는 한 문장 ← " + tapUser);
        ReactionRequest fadedRule = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        fadedRule.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        string fadedUser = ruleAssembler.Build(fadedRule).User;
        Check(fadedUser.Contains("대사: 한국어 반말, 12자 이내, 첫 낱말로 시작, 부탁 한 문장") && !fadedUser.Contains("닦아 줄래"), "흐려진 안부의 대사 줄: 이어지는 부탁 한 문장만 (끝말은 정하지 않음) ← " + fadedUser);
        ReactionRequest clearRule = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        clearRule.Facts.Add(new Fact(NpcFactKeys.Clarity, "선명"));
        Check(!ruleAssembler.Build(clearRule).User.Contains("부탁한다"), "선명한 안부에는 부탁 줄이 없음");
        Check(NpcLineChecks.NormalizeSpeech("개운해, 주인이 관리해 줘서 고마워.", "반말", null, null, null, null, "도윤") == "개운해, 관리해 줘서 고마워."
                && NpcLineChecks.NormalizeSpeech("개운해, 주인이 관리해 줘서 고마워.", "반말", null) == "개운해, 네가 관리해 줘서 고마워.",
                "'주인이'도 호칭으로 (호칭을 넘기지 않으면 예전처럼 '네가')");
        // 2026-11-17 시험(64건): 호칭을 지우다 쉼표가 사라져 마디가 붙고, 높임이 남고, 자기 몸을 '몸 상태'로 재고, 손님에게 "구경해 줘"라고 했다
        Check(NpcLineChecks.NormalizeSpeech("오늘 자개가 정말 예쁘네, 주인님 덕분에 빛이 나요.", "반말", null) == "오늘 자개가 정말 예쁘네, 덕분에 빛이 나."
                && NpcLineChecks.NormalizeSpeech("개운해, 주인님 덕분에 기분이 한결 좋아졌어.", "반말", null) == "개운해, 덕분에 기분이 한결 좋아졌어."
                && NpcLineChecks.NormalizeSpeech("개운해, 주인이 관리해 줘서 고마워.", "반말", null) == "개운해, 네가 관리해 줘서 고마워."
                && NpcLineChecks.NormalizeSpeech("점점 흐려져, 주인이 닦아 주시면 좋겠어.", "반말", null) == "점점 흐려져, 닦아 줘."
                && NpcLineChecks.NormalizeSpeech("고마워, 주인님! 안녕, 주인님, 반가워.", "반말", null) == "고마워! 안녕, 반가워.",
                "호칭을 지워도 뒤에 말이 이어지면 쉼표를 남기고, 끝에 붙은 호칭은 쉼표째 지움");
        Check(NpcLineChecks.NormalizeSpeech("와, 아침부터 나를 부르시다니 반갑습니다!", "반말", "말발굽") == "와, 아침부터 나를 부르다니 반가워!"
                && NpcLineChecks.NormalizeSpeech("그때 얼마나 공들였는지 보여드릴 수 있어서 뿌듯했어요.", "반말", "밤바다") == "그때 얼마나 공들였는지 보여줄 수 있어서 뿌듯했어.",
                "'부르시다니'·'보여드릴'의 높임도 풀어 줌");
        Check(NpcLineChecks.NormalizeSpeech("이틀 전에 관리받아서 몸 상태가 훨씬 더 선명해졌어요. 몸 상태는 좋아.", "반말", "구름결") == "이틀 전에 관리받아서 몸이 훨씬 더 선명해졌어. 몸은 좋아.",
                "'몸 상태가'는 '몸이'로 (자기 몸을 남이 재듯 말하지 않음)");
        Check(NpcLineChecks.NormalizeSpeech("편하게 구경해 줘, 오늘도 반갑게 인사해.", "반말", "향산이") == "편하게 구경해, 오늘도 반갑게 인사해.",
                "손님에게 '구경해 줘'는 권하는 말 '구경해'로");
        Check(NpcLineChecks.NormalizeSpeech("또 왔구나! 반가워, 오랜만이야!", "반말", "말발굽", null, null, "어제, 3일 전", "발굴자") == "또 왔구나! 반가워!"
                && NpcLineChecks.NormalizeSpeech("반가워, 오랜만이야!", "반말", "말발굽", null, null, "어제", "발굴자") == "반가워, 또 왔네!",
                "'또 왔구나'를 이미 말했으면 '오랜만이야'를 '또 왔네'로 겹쳐 쓰지 않음");
        Check(NpcLineChecks.NormalizeSpeech("환영해, 오늘 처음 온 친구야! 구경해 줘.", "반말", "향산이", null, null, "오늘", "발굴자") == "환영해, 오늘 처음 온 친구야! 구경해.",
                "'처음 온 친구야!'는 부르는 말이 아니라 서술이라 '발굴자'로 바꾸지 않음");
        // 2026-11-16 시험 S12 #1·#3·#4: 부탁하는 말의 주어 '주인이/주인님께서'는 "네가"로 바꾸지 않고 지운다. 같은 끝말을 띄어 되풀이한 것("닦아줘 줘")은 하나만 남긴다
        var careThings = new List<string> { "몸", "향" };
        Check(NpcLineChecks.NormalizeSpeech("요즘 향이 그리워서 주인이 닦아줘 줘, 나도 향산이가 다시 깨어나고 싶어.", "반말", "향산이", careThings, "도윤")
                == "요즘 향이 그리워서 닦아줘, 나도 다시 깨어나고 싶어.", "부탁의 주어 '주인이'를 지우고 '닦아줘 줘'는 하나로");
        Check(NpcLineChecks.NormalizeSpeech("혹시, 주인님께서 닦아 주시면 다시 반짝일 수 있을까요?", "반말", "향산이", careThings, "도윤")
                == "혹시 닦아 주면 다시 반짝일 수 있을까?", "부탁의 주어 '주인님께서'는 '네가'로 바꾸지 않고 지움 (앞 쉼표는 남김)");
        Check(NpcLineChecks.NormalizeSpeech("개운해! 주인이 관리해 줘서 고마워, 참새랑 놀고 싶어!", "반말", "묘작이", careThings, "도윤")
                == "개운해! 네가 관리해 줘서 고마워, 참새랑 놀고 싶어!", "고마움('줘서')의 주어는 그대로 '네가'");
        Check(NpcLineChecks.CollapseDoubleVerb("닦아줘 줘") == "닦아줘" && NpcLineChecks.CollapseDoubleVerb("와 줘") == "와 줘" && NpcLineChecks.CollapseDoubleVerb("고마워") == "고마워",
                "끝말 되풀이만 합치고 '와 줘'·'고마워'는 그대로");
        Check(NpcLineChecks.NormalizeSpeech("도시는 마시고 주시면", "반말", "말발굽") == "도시는 마시고 주면", "'도시'·'마시고'의 시는 지키고 높임만 뺌");
        Expect(strict, Out("greet", "player", "안녕!"), Tap(8), OutputProblem.TooPlain, "인사 행동의 대사가 인사말뿐이면 걸림");
        Expect(strict, Out("greet", "player", "주인님!"), Tap(8), OutputProblem.EmptyLine, "호칭만 있던 대사는 지우고 나면 빈 대사로 걸림");
        Expect(strict, Out("wander_greet", "player", "반가워! 다음에 또 와."), Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet), OutputProblem.None, "첫 방문에 '다음에 또 와'(초대)는 통과");
        ReactionRequest regularVisit = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
        regularVisit.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "7"));
        Expect(strict, Out("wander_recall", "player", "안녕, 처음 온 친구라서 반가워!"), regularVisit, OutputProblem.WrongHistory, "7번 온 단골에게 '처음'이라고 하면 걸림");
        Expect(strict, Out("wander_recall", "player", "어, 또 왔네. 반가워!"), regularVisit, OutputProblem.None, "7번 온 단골에게 '또 왔네'는 통과");
        ReactionRequest firstVisit = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        firstVisit.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "1"));
        Expect(strict, Out("wander_greet", "player", "어, 또 왔구나! 반가워."), firstVisit, OutputProblem.WrongHistory, "처음 온 친구에게 '또 왔구나'라고 하면 걸림");
        Expect(strict, Out("wander_greet", "player", "처음 보는 얼굴이네. 반가워!"), firstVisit, OutputProblem.None, "처음 온 친구에게 '처음 보는 얼굴'은 통과");
        Expect(strict, Out("greet", "player", "오늘 순찰 잘 다녀왔습네까?"), Tap(8), OutputProblem.SpeechMismatch, "'~습네까'처럼 존대가 섞이면 걸림");
        Expect(strict, Out("greet", "player", "이렇게 기다리셨어."), Tap(8), OutputProblem.SpeechMismatch, "'기다리셨어'처럼 풀지 못한 높임이 섞이면 걸림");
        Check(NpcLineChecks.NormalizeSpeech("내일 또 뵙겠습니다.", "반말", "말발굽") == "내일 또 볼게." && NpcLineChecks.NormalizeSpeech("주인님을 뵈러 왔어요.", "반말", "말발굽") == "너를 보러 왔어.",
                "뵙다·뵈다 높임은 반말로 고쳐 살림 (실기기 S12 #2)");
        // 실기기 S05 #2: 처음 온 친구(방문한 날이 오늘뿐)에게 "오랜만"이라고 한 말은 거르지 않고 고친다
        Check(NpcLineChecks.NormalizeSpeech("오랜만에 와 줘서 고마워!", "반말", "향산이", null, null, "오늘") == "와 줘서 고마워!"
                && NpcLineChecks.NormalizeSpeech("오랜만이야!", "반말", "향산이", null, null, "오늘") == "반가워!"
                && NpcLineChecks.NormalizeSpeech("오랜만에 왔네.", "반말", "향산이", null, null, "오늘, 3일 전") == "또 왔네.",
                "처음 온 친구의 '오랜만'은 고치고, 최근에 온 단골의 '오랜만에'는 '또'로");
        // 한 문장 안에서 두 글자 이상 같은 낱말이 되풀이되면 하나만 남긴다 (DropRepeatedWord)
        foreach (string[] pair in new[]
        {
            new[] { "또 왔네, 다시 왔네", "다시 왔네" },                              // 다른 마디: 짧은 마디를 통째로 지운다
            new[] { "또 왔네, 다시 왔네!", "다시 왔네!" },
            new[] { "향로를 닦아줘서 고마워, 정말 고마워!", "향로를 닦아줘서 고마워!" },  // 뒤 마디를 지우면 쉼표 대신 끝 부호를 남긴다
            new[] { "반가워, 반가워!", "반가워!" },                                  // 길이가 같으면 뒤 마디를 지운다
            new[] { "고마워 정말 고마워.", "정말 고마워." },                          // 한 마디 안: 앞 낱말만 지운다
            new[] { "또 왔네, 다시 왔네! 고마워 정말 고마워.", "다시 왔네! 정말 고마워." },  // 문장마다 따로 고친다
            new[] { "반가워, 또 왔네, 다시 왔네, 반가워!", "반가워, 다시 왔네!" },     // 되풀이가 여럿이면 모두 고친다 (먼저 나온 되풀이부터)
            new[] { "왔네! 오늘도 왔네.", "왔네! 오늘도 왔네." },                     // 다른 문장의 되풀이는 건드리지 않는다 (InnerRepeat 가 본다)
            new[] { "나는 좋아, 나는 행복해.", "나는 좋아, 나는 행복해." },           // RepeatExempt 낱말은 되풀이로 보지 않는다
            new[] { "또 와, 또 놀자.", "또 와, 또 놀자." },                           // 한 글자 낱말은 보지 않는다
            new[] { "고마워, 관리해줘서 고마웠어.", "고마워, 관리해줘서 고마웠어." },   // 꼴이 다른 낱말은 같은 말로 보지 않는다
            new[] { "안녕? 오늘도 순찰 잘 다녀왔어?", "안녕? 오늘도 순찰 잘 다녀왔어?" },
            new[] { "", "" },
            // 같은 말일 때만 지운다: 주어·대상·부정이 다르면 다른 말이라 그대로 두고 InnerRepeat 가 거른다 (CodeRabbit 리뷰)
            new[] { "나는 좋아, 너는 안 좋아.", "나는 좋아, 너는 안 좋아." },
            new[] { "좋아, 정말 안 좋아.", "좋아, 정말 안 좋아." },
            new[] { "나는 좋아 너는 안 좋아.", "나는 좋아 너는 안 좋아." },
            new[] { "관리해 줘서 고마워, 관리가 좋아서 고마워.", "관리해 줘서 고마워, 관리가 좋아서 고마워." },
            // 문장은 출력 검사와 같은 문장 끝(SentenceEnds)으로 가른다: '。'는 문장 끝, '…'는 말줄임이라 한 문장 (CodeRabbit 리뷰)
            new[] { "몸이 가벼워。 마음도 가벼워!", "몸이 가벼워。 마음도 가벼워!" },
            new[] { "고마워… 정말 고마워!", "정말 고마워!" },
            // 마디 길이는 코드 포인트로 센다: 두 마디가 4자로 같아 뒤 마디를 지운다 (UTF-16 으로 세면 이모지 마디가 5자라 앞 마디를 지웠다)
            new[] { "참 고마워, 😀 고마워!", "참 고마워!" },
        })
        {
            string dropped = NpcLineChecks.DropRepeatedWord(pair[0]);
            Check(dropped == pair[1], "한 문장 안의 낱말 되풀이: '" + pair[0] + "' → '" + pair[1] + "' ← '" + dropped + "'");
        }
        Check(NpcLineChecks.DropRepeatedWord(null) == null, "DropRepeatedWord(null) 은 null");
        Check(NpcLineChecks.NormalizeSpeech("또 왔네, 다시 왔네!", "반말", "말발굽") == "다시 왔네!", "말투 다듬기에서 '또 왔네, 다시 왔네!'는 '다시 왔네!'로");
        OutputCheck repeatFixed = strict.Check(Out("greet", "player", "향로를 닦아줘서 고마워, 정말 고마워!"), Tap(9));
        Check(repeatFixed.IsValid && repeatFixed.Line == "향로를 닦아줘서 고마워!",
                "같은 말 되풀이('정말 고마워')는 거르지 않고 짧은 마디를 지워 통과 ← " + repeatFixed.Line + " " + repeatFixed.Problem + " " + repeatFixed.Detail);
        Expect(strict, Out("greet", "player", "관리해 줘서 고마워, 관리가 좋아서 고마워."), Tap(9), OutputProblem.InnerRepeat,
                "다른 말끼리 '고마워'가 겹치면 지우지 않고 걸림 (CodeRabbit 리뷰)");
        Expect(strict, Out("greet", "player", "관리해 줘서 고마워, 관리가 좋아서 고마웠어."), Tap(9), OutputProblem.InnerRepeat,
                "꼴이 달라 지우지 못한 되풀이('관리해'·'관리가')는 걸림 (두 문장은 첫 문장 하나로 줄어든다)");
        Check(NpcLineChecks.InnerRepeat("고마워, 관리해줘서 고마웠어") == "고마웠어" && NpcLineChecks.InnerRepeat("나는 나는 좋아") == null
                && NpcLineChecks.InnerRepeat("안녕? 오늘도 순찰 잘 다녀왔어?") == null, "한 대사 안의 반복 찾기");
        Expect(strict, Out("greet", "player", "안녕."), Tap(8), OutputProblem.TooPlain, "부호만 달라도 인사말뿐이면 걸림");
        Expect(loose, Out("wander_greet", "player", "향산이가 인사하네."), Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet),
                OutputProblem.None, "페르소나를 안 받은 검사기는 이름·어미를 보지 않음");
        // 말투 다듬기: 흔한 어미는 검사 전에 고치고, 고치지 못한 것만 어미 검사가 거른다
        // 첫 낱말 '개운하게·깨끗하게'가 꾸밀 움직임말 없이 이어진 정화 대사를 고친다 (2026-11-18 --live-store 미리 만든 대사)
        Check(NpcLineChecks.FixStarterAdverb("개운하게 덕분에 기분이 좋네", "발굴자") == "개운해, 덕분에 기분이 좋네"
                && NpcLineChecks.FixStarterAdverb("개운하게 돌아온 발굴자, 오랜만에 관리해줘서 고마워.", "발굴자") == "발굴자, 오랜만에 관리해줘서 고마워."
                && NpcLineChecks.FixStarterAdverb("개운하게 돌아온 관리님, 오랜만에 관리해줘서 고마워.", "발굴자") == "오랜만에 관리해줘서 고마워."
                && NpcLineChecks.FixStarterAdverb("개운하게 고마워!", null) == "개운해, 고마워!",
                "'개운하게' 뒤에 꾸밀 움직임말이 없으면 '개운해,'로, '돌아온 발굴자,'처럼 꾸밈말 + 부르는 말이면 그 꾸밈을 지움");
        Check(NpcLineChecks.FixStarterAdverb("개운하게 관리해줘서 고마워!", "발굴자") == "개운하게 관리해줘서 고마워!"
                && NpcLineChecks.FixStarterAdverb("깨끗하게 오랜만에 나를 관리해줘서 고마워!", "발굴자") == "깨끗하게 오랜만에 나를 관리해줘서 고마워!"
                && NpcLineChecks.FixStarterAdverb("개운하게", null) == "개운하게" && NpcLineChecks.FixStarterAdverb("덕분에 개운하게 쉬었어.", null) == "덕분에 개운하게 쉬었어.",
                "첫 마디에 꾸밀 움직임말(관리해·닦아…)이 있거나 첫 낱말이 아니면 그대로");
        Expect(strict, Out("thank_calm", "player", "개운하게 덕분에 기분이 좋네!"), Care(9), OutputProblem.None, "어색한 '개운하게 덕분에'는 고쳐 바로 씀");
        OutputCheck fixedCheck = strict.Check(Out("greet", "player", "나 왔어, 고마워요!"), Tap(9));
        Check(fixedCheck.IsValid && fixedCheck.Line == "나 왔어, 고마워!" && fixedCheck.LineBeforeSpeechFix == "나 왔어, 고마워요!"
                && (fixedCheck.ToResult("s").Note ?? "").Contains(OutputCheck.SpeechFixNote + " ('나 왔어, 고마워요!' → '나 왔어, 고마워!')"),
                "반말 도깨비의 '고마워요'는 '고마워'로 고쳐 통과, 고치기 전 대사와 Note 를 남김 ← " + fixedCheck.Line + " / " + fixedCheck.ToResult("s").Note);
        Expect(strict, Out("greet", "player", "이거 어떠세요!"), Tap(9), OutputProblem.SpeechMismatch, "반말 도깨비가 고치지 못하는 '~요'를 쓰면 걸림");
        var unfixed = new NpcOutputValidator(new NpcLineFilter(forbidden), personas) { NormalizeSpeech = false };
        Expect(unfixed, Out("greet", "player", "나 왔어, 고마워요!"), Tap(9), OutputProblem.SpeechMismatch, "말투 다듬기를 끄면 '~요'가 그대로 걸림");
        OutputCheck haoCheck = strict.Check(Out("greet", "player", "고마워요! 어흠, 왔구나."), Tap(8));
        Check(haoCheck.IsValid && haoCheck.Line == "고마워! 어흠, 왔구나.", "말발굽(반말)의 '~요'는 떼어 고쳐 통과 ← " + haoCheck.Line);
        OutputCheck untouched = strict.Check(Out("greet", "player", "어흠, 왔구나."), Tap(8));
        Check(untouched.IsValid && untouched.LineBeforeSpeechFix == null && untouched.ToResult("u").Note == null, "반말 인사는 고치지 않고 통과");
        ReactionRequest again = Tap(8);
        again.RecentLines = new List<string> { "어흠, 왔구나, 기다렸어?" };
        Expect(strict, Out("greet", "player", "어흠, 왔구나, 기다렸어? 어서 와."), again, OutputProblem.Repeated, "최근 대사와 같으면 걸림");
        Expect(loose, Out("greet", "player", "어흠, 왔구나, 기다렸어? 어서 와."), again, OutputProblem.Repeated,
                "반복은 페르소나 없이도 봄 (최근 대사만 있으면)");

        // 기본 대사와 이야기 대사도 페르소나 어미를 지킨다 (모델이 실패했을 때, 이야기 듣기 때 검사 없이 그대로 나가는 말이다)
        foreach (SpiritLines spirit in lines.spirits)
        {
            Persona owner = personas.Find(spirit.relicId);
            if (owner == null)
            {
                continue;
            }
            foreach (string line in spirit.pools.SelectMany(p => p.lines).Concat(spirit.stories ?? new List<string>()))
            {
                Check(NpcLineChecks.SpeechMatches(line, owner.endingHint, out wrong), spirit.name + " 기본 대사 어미: " + line + " ← " + wrong);
            }
        }

        // SLM 생성기: 걸리면 곁들일 거리를 바꿔 한 번 더 만든다
        var backend = new FakeBackend();
        var generator = new SlmReactionGenerator(backend, new PromptAssembler(personas, actions), strict);
        generator.LoadAsync(CancellationToken.None).Wait();
        backend.Replies.Enqueue(Out("greet", "player", "이거 어떠세요!"));
        backend.Replies.Enqueue(Out("greet", "player", "앗, 같이 놀자!"));
        ReactionRequest tap9 = Tap(9);
        tap9.RequestId = "r9";
        ReactionResult retried = Run(generator, tap9);
        List<string> tapFlavors = personas.Find(9).flavors.tap;
        Check(retried.IsSuccess && retried.Line == "앗, 같이 놀자!" && backend.Calls == 2 && tap9.Variation == 1
                && retried.Note != null && retried.Note.Contains("SpeechMismatch")
                && backend.LastPrompt.User.Contains(PromptAssembler.StarterLabel + ": " + PromptAssembler.Starter(tap9)),
                "어미가 섞이면 다음 곁들일 거리로 다시 만들어 통과한 것을 씀 ← " + retried.Note);
        Check(generator.MaxAttempts == 2, "기본 MaxAttempts 는 2 (최근 대사와 겹치거나 문장이 끝나지 않으면 한 번 더 만든다)");
        backend.Calls = 0;
        backend.Reply = Out("greet", "player", "이거 어떠세요!");
        ReactionResult gaveUp = Run(generator, Tap(9));
        Check(!gaveUp.IsSuccess && gaveUp.FailReason == FailReason.InvalidOutput && backend.Calls == 2 && gaveUp.FailDetail.Contains("앞 시도 1번"),
                "MaxAttempts(2)번 모두 걸리면 InvalidOutput (요청 관리자가 기본 대사로 대신) ← " + gaveUp.FailDetail);
        generator.MaxAttempts = 1;
        backend.Calls = 0;
        Check(!Run(generator, Tap(9)).IsSuccess && backend.Calls == 1, "MaxAttempts 1 이면 다시 만들지 않음");
        generator.MaxAttempts = 2;

        // 요청 관리자: 띄운 대사를 기억해 같은 대사를 막는다
        var manager = new NpcRequestManager(generator, new RuleBasedReactionGenerator(lines, new Random(7)))
        {
            SlmTimeout = TimeSpan.FromSeconds(5)
        };
        backend.Reply = Out("greet", "player", "앗, 같이 놀자!");
        NpcResponse first = Ask(manager, Tap(9));
        NpcResponse second = Ask(manager, Tap(9));
        Check(first.ShouldShow && !first.UsedFallback && second.ShouldShow && second.UsedFallback
                && second.Note.Contains("Repeated") && second.Reaction.Line != "앗, 같이 놀자!",
                "같은 대사를 또 내면 다시 만들어 보고, 그래도 같으면 기본 대사 ← " + second.Note);
        List<string> remembered = manager.RecentLinesOf(9);
        Check(remembered.Count == 2 && remembered[0] == "앗, 같이 놀자!" && remembered[1] == second.Reaction.Line && manager.RecentLinesOf(8).Count == 0,
                "도깨비마다 띄운 대사(기본 대사 포함)를 기억함");
        ReactionRequest given = Tap(9);
        given.RecentLines = new List<string> { "저장해 둔 대사" };
        Ask(manager, given);
        Check(given.RecentLines[0] == "저장해 둔 대사" && given.RecentLines.Contains("앗, 같이 놀자!"), "게임이 넣어 둔 최근 대사는 두고 뒤에 붙임");
        Check(manager.RecentLineCount == 10, "시나리오마다 최근 대사 10개를 기억 (기본)");
        foreach (string fresh in new[] { "참새 잡으러 갈래?", "킁킁, 맛있는 냄새가 나!", "공 굴리기 하자!", "숨바꼭질 어때?",
                                         "히힛, 꼬리 잡기 놀이야!", "폴짝 뛰어 볼까?", "구름 위에서 뒹굴고 싶어!", "나비 따라 달리자!",
                                         "고양이 춤을 보여 줄게!", "오늘은 어디 탐험할까?", "수염이 간질거려!", "발톱 갈기 놀이 하자!" })
        {
            backend.Reply = Out("greet", "player", fresh);
            Ask(manager, Tap(9));
        }
        Check(manager.RecentLinesOf(9).Count == manager.RecentLineCount, "최근 대사는 RecentLineCount(10)개만 기억");

        // 기본 대사도 최근 대사와 다른 것을 고른다
        List<string> greetLines = lines.spirits.First(s => s.relicId == 9).pools.First(p => p.actionId == NpcActions.Greet).lines;
        bool avoided = true;
        for (int seed = 0; seed < 6; seed++)
        {
            ReactionRequest fallbackRequest = Tap(9);
            fallbackRequest.RequestId = "f" + seed;
            fallbackRequest.RecentLines = new List<string> { greetLines[0] };
            ReactionResult picked = new RuleBasedReactionGenerator(lines, new Random(seed)).GenerateAsync(fallbackRequest, CancellationToken.None).Result;
            avoided &= picked.IsSuccess && picked.Line != greetLines[0];
        }
        Check(avoided, "기본 대사는 최근에 띄운 것을 피함 (다른 것이 있으면)");
    }

    // 말투 다듬기: run.py 의 normalize_speech 와 같은 결과인지 (예문은 dump_expected_prompts.py 의 SPEECH_SAMPLES), 뜻대로 고치는지
    // ---------- 실기기 시험 반영 (2026-11-13) ----------

    private static void CheckDeviceFindings(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions,
                                            DefaultLineCatalog lines, List<Dictionary<string, object>> scenes, string personaJson)
    {
        Console.WriteLine("=== 실기기 시험 반영: 끌려갈 때 대사 없음·상황 설명문·기록·옛말투 ===");
        var filter = new NpcLineFilter(forbidden);
        var validator = new NpcOutputValidator(filter, personas);
        var assembler = new PromptAssembler(personas, actions);
        var rule = new RuleBasedReactionGenerator(lines, new Random(7));

        // 1) 끌려갈 때(struggle)는 대사를 띄우지 않는다: 모델도 부르지 않고, 행동만 고르고, 대사는 빈 글
        foreach (Persona persona in personas.personas)
        {
            ReactionRequest drag = Request(persona.relicId, InteractionMode.Owner, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay);
            ReactionResult picked = rule.Generate(drag, CancellationToken.None);
            Check(picked.IsSuccess && picked.Line == "" && NpcActions.IsSilent(picked.ActionId) && picked.TargetId == NpcTargets.Player,
                    persona.name + " 끌려갈 때: 행동만 고르고 대사는 빈 글 ← " + picked.ActionId + "/" + picked.Line);
            Check(assembler.Build(drag) == null, persona.name + " 끌려갈 때는 프롬프트를 만들지 않음");
        }
        foreach (SpiritLines spirit in lines.spirits.Concat(new[] { lines.fallback }))
        {
            Check(!spirit.pools.Any(pool => NpcActions.IsSilent(pool.actionId)), spirit.name + " 기본 대사에 끌려갈 때 대사가 없음");
        }
        ReactionResult noPersona = rule.Generate(Request(1, InteractionMode.Owner, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay),
                CancellationToken.None);
        Check(noPersona.IsSuccess && noPersona.Line == "", "페르소나 없는 도깨비도 끌려갈 때는 대사 없음");
        var mixed = new List<ActionCandidate>
        {
            new ActionCandidate(NpcActions.StruggleProtest, NpcTargets.Player), new ActionCandidate(NpcActions.Greet, NpcTargets.Player)
        };
        Check(!NpcActions.OnlySilent(new List<ActionCandidate>()) && !NpcActions.OnlySilent(null) && !NpcActions.OnlySilent(mixed)
                && NpcActions.SkipsModel(new List<ActionCandidate> { new ActionCandidate(NpcActions.TellStory, NpcTargets.Player) })
                && NpcActions.SkipsModel(new List<ActionCandidate> { new ActionCandidate(NpcActions.StrugglePlay, NpcTargets.Player) })
                && !NpcActions.SkipsModel(new List<ActionCandidate> { new ActionCandidate(NpcActions.Greet, NpcTargets.Player) }),
                "대사 없는 행동 판정 (섞이면 아님, 이야기·끌려갈 때는 모델을 건너뜀)");

        var backend = new FakeBackend { Reply = Out("struggle_play", "player", "히힛, 놀자!") };
        var slm = new SlmReactionGenerator(backend, assembler, validator);
        slm.LoadAsync(CancellationToken.None).Wait();
        ReactionResult slmTry = Run(slm, Request(9, InteractionMode.Owner, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay));
        Check(slmTry.FailReason == FailReason.Unsupported && backend.Calls == 0, "SLM 생성기는 끌려갈 때 모델을 부르지 않음 (Unsupported)");
        var manager = new NpcRequestManager(slm, rule);
        NpcResponse response = Ask(manager, Request(9, InteractionMode.Owner, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay));
        Check(response.ShouldShow && response.Note == NpcRequestManager.SilentNote && response.Reaction.Line == "" && response.UsedFallback
                && NpcActions.IsSilent(response.Reaction.ActionId) && backend.Calls == 0 && manager.RecentLinesOf(9).Count == 0,
                "요청 관리자: 끌려갈 때는 모델을 부르지 않고 대사 없는 반응 (최근 대사에도 안 남김) ← " + response.Note);

        // 2) 상황 설명문이 대사에 섞이면 거른다
        const string tapSituation = "주인이 너를 탭했다. 화면이 너에게 가까이 다가왔다.";
        ReactionRequest tap = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        tap.Situation = tapSituation;
        string[] narrated =
        {
            "주인이 너를 탭했다. 안녕!", "화면이 너에게 가까이 다가왔다니까.", "안녕! 화면이 너에게 가까이 다가왔어.",
            "주인이 안부를 물었다. 잘 지내.", "너는 오래 관리를 받지 못해 흐려졌다.", "(웃으며) 반가워!", "*끄덕* 왔구나", "친구는 너를 건드릴 수 없어.",
            "짧은 말풍선을 띄운다.", "반가운 마음을 담아 인사한다."
        };
        foreach (string line in narrated)
        {
            Check(filter.Find(line) != null || NpcLineChecks.CopiedFromSituation(line, tapSituation) != null, "상황 설명문·몸짓 설명은 거름: " + line);
        }
        Expect(validator, Out("greet", "player", "주인이 너를 탭했다. 화면이 너에게 가까이 다가왔다."), tap, OutputProblem.Forbidden, "탭 상황 문장을 그대로 옮기면 거름 (금칙어)");
        ReactionRequest copyOnly = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        copyOnly.Situation = "주인이 너를 불러 옆자리에 앉혔다. 고양이가 느긋하게 하품한다.";
        Expect(validator, Out("greet", "player", "옆자리에 앉아, 고양이가 느긋하게 하품한다니 좋아."), copyOnly, OutputProblem.SituationCopy,
                "금칙어가 아니어도 상황 문장 구절을 10자 넘게 옮기면 SituationCopy");
        Expect(validator, Out("greet", "player", "옆자리에 앉아 볼래? 고양이 졸려."), copyOnly, OutputProblem.None, "낱말 몇 개만 겹치면 통과");
        Check(NpcLineChecks.CopiedFromSituation("안녕", "시험") == null && NpcLineChecks.CopiedFromSituation(null, null) == null,
                "짧은 글·빈 글은 옮긴 것으로 보지 않음");
        foreach (string fine in new[] { "주인이 이름을 불러 줘서 좋아!", "관리해 줘서 고마워. 한결 가벼워졌어!", "네가 불러서 깜짝 놀랐어!", "구경 왔구나, 편하게 봐." })
        {
            Check(filter.IsAllowed(fine), "정상 대사는 상황 설명문 금칙어에 걸리지 않음: " + fine + " ← " + filter.Find(fine));
        }
        Check(filter.Find("나는 도깨비야") != null && filter.Find("마패에 깃든 정령이야") != null, "자기소개('나는 도깨비야', '깃든 정령')도 거름");

        // 2026-11-14 시험 3 (prompt_test_20261114_024003): 어색했던 대사들
        foreach (string awkward in new[]
        {
            "어서 안녕? 오늘 하루는 햇살처럼 따뜻하게 보내줄래?", "여기, 안녕! 오늘 아침에 내가 깨어났어.", "여기, 오늘 아침에 너를 탭해줘서 반가워!",
            "좋은 하루 보내!", "안녕, 전시관 구경 잘 돼? 반가워서 불렀어", "반가워, 전시관 구경 잘 해줘서 고마워.", "조금 전 관리가 안 돼서 몸이 흐려졌어.",
            "덕분에 역시 기뻐! 털이 반짝반짝해졌네~", "덕분에 닦아줘서 고마워!", "역나, 또 찾아줘서 고마워!", "이대로 흐려지면 안 되지 네가 닦아 깨워 줘.",
            "내가 만든 자개는 공들여 만들었어.", "발굴자라 불러줘서 반가워!", "찾아와서 반가워! 밤에 오는 것도 나랑 놀자고?",
            "잠깐 전 관리를 못 받아서 몸이 흐려졌어. 네가 닦아줘.", "한결 같아져서 고마워.", "오, 자개 공들여 만든 거 정말 멋지네."
        })
        {
            Check(filter.Find(awkward) != null, "어색한 대사는 금칙어로 거름: " + awkward);
        }
        foreach (string natural in new[] { "여기 있었구나, 반가워!", "한결같이 와 줘서 고마워.", "주인이라고 불러도 돼?", "안녕, 와 줘서 고마워.", "덕분에 몸이 개운해졌어.", "이틀 전에 닦아 줘서 몸 상태가 선명해.", "내 자개는 공들여 만들었어." })
        {
            Check(filter.IsAllowed(natural), "자연스러운 대사는 걸리지 않음: " + natural + " ← " + filter.Find(natural));
        }
        Check(!NpcLineChecks.HasAsk("오랜만에 깨워줘서 고마워") && !NpcLineChecks.HasAsk("닦아 줘서 고마워") && !NpcLineChecks.HasAsk("혹시, 오랜만에 깨워줘서 고마워.")
                && NpcLineChecks.HasAsk("네가 닦아줘.") && NpcLineChecks.HasAsk("깨워 줄래?") && NpcLineChecks.HasAsk("몸이 흐려졌어, 닦아줘."),
                "감사('깨워줘서'·'닦아 줘서')는 부탁이 아님, 부탁('닦아줘'·'깨워 줄래')은 부탁");
        ReactionRequest thankOnly = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        thankOnly.Situation = "주인이 안부를 물었다.";
        thankOnly.Facts.Add(new Fact(NpcFactKeys.Clarity, "재봉인"));
        OutputCheck thankFilled = validator.Check(Out("answer_status", "player", "오랜만에 깨워줘서 고마워, 덕분에 잠깐 기운이 나네."), thankOnly);
        Check(thankFilled.IsValid && thankFilled.Line.Contains("깨워") && NpcLineChecks.HasAsk(thankFilled.Line),
                "깨워 달라는 자리에서 감사만 하면 코드가 깨워 달라는 부탁을 채움 (시험 3 S12 #2) ← " + thankFilled.Line + " " + thankFilled.Detail);
        Expect(validator, Out("thank_calm", "player", "고마워."), Request(8, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankCalm), OutputProblem.TooPlain,
                "관리 감사가 '고마워.'뿐이면 TooPlain (시험 3 S07 #2)");

        // 영문·한자·행동 ID 가 섞이면 거른다
        Expect(validator, Out("greet", "player", "greet 하는 중이야!"), tap, OutputProblem.Foreign, "행동 ID(영문)가 섞이면 Foreign");
        Expect(validator, Out("greet", "player", "OK, 반가워!"), tap, OutputProblem.Foreign, "영어가 섞이면 Foreign");
        Expect(validator, Out("greet", "player", "반가워 歡迎!"), tap, OutputProblem.Foreign, "한자가 섞이면 Foreign");
        Check(!NpcLineChecks.HasForeign("반가워! 어흠~ 1,300년이나 됐어.") && !NpcLineChecks.HasForeign(null), "한글·숫자·부호만이면 통과");

        // 3) 주인 장면에서도 기록과 어긋나는 말은 거르지 않고 고친다 (기록이 없는데 "오랜만이야" → "반가워", "또 왔네" → "왔네").
        //    탭 인사 16건 중 4건이 이것으로 거르고 다시 만들다 기본 대사로 넘어갔다(2026-11-17 시험). 말투 다듬기를 끄면 예전처럼 거른다(아래)
        ReactionRequest noRecord = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        noRecord.Situation = tapSituation;
        Expect(validator, Out("greet", "player", "오랜만이야, 순찰 중이었어."), noRecord, OutputProblem.None, "기록 없는 주인 탭에 '오랜만이야'는 '반가워'로 고쳐 통과");
        var rawValidator = new NpcOutputValidator(filter, personas) { NormalizeSpeech = false };
        Expect(rawValidator, Out("greet", "player", "오랜만이야, 순찰 중이었어."), noRecord, OutputProblem.WrongHistory, "말투 다듬기를 끄면 기록 없는 주인 탭에 '오랜만이야'는 WrongHistory");
        Expect(validator, Out("greet", "player", "어, 또 왔네? 반가워."), noRecord, OutputProblem.None, "기록 없는 주인에게 '또 왔네'는 '왔네'로 고쳐 통과");
        Expect(rawValidator, Out("greet", "player", "어, 또 왔네? 반가워."), noRecord, OutputProblem.WrongHistory, "말투 다듬기를 끄면 '또 왔네'도 WrongHistory");
        Expect(validator, Out("greet", "player", "오래간만이네, 반가워."), noRecord, OutputProblem.TooPlain, "'오래간만이네'는 '반가워'로 고쳐져 '반가워, 반가워.'→'반가워.' 인사말뿐");
        Expect(rawValidator, Out("greet", "player", "오래간만이네, 반가워."), noRecord, OutputProblem.WrongHistory, "말투 다듬기를 끄면 '오래간만'도 WrongHistory");
        Expect(validator, Out("greet", "player", "왔구나, 순찰은 내가 했어."), noRecord, OutputProblem.None, "기록 없는 주인에게 평범한 인사는 통과");
        // 2026-11-18 22:16 시험: '오랜만'을 늘 "반가워"로 바꾸자 다른 인사들이 같은 대사로 모여 최근 대사와 같다고 걸렸다 (말발굽 탭 S23 #1·#2·#4·S24 #3).
        // 최근 대사와 겹치면 바꿔 넣는 말을 다른 것(NoRecordGreetings)으로 고른다
        ReactionRequest noRecordRecent = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        noRecordRecent.Situation = tapSituation;
        noRecordRecent.RecentLines = new List<string> { "어서 와, 반가워", "왔구나, 반가워." };
        OutputCheck variedAbsence = validator.Check(Out("greet", "player", "어서 왔구나, 오랜만에 보네"), noRecordRecent);
        Check(variedAbsence.IsValid && variedAbsence.Line == "어서 와, 기다렸어",
                "'오랜만에 보네'를 바꾼 대사가 최근 대사('어서 와, 반가워')와 같으면 다른 말('기다렸어')을 넣어 통과 ← " + variedAbsence.Line + " " + variedAbsence.Problem + " " + variedAbsence.Detail);
        OutputCheck variedAbsence2 = validator.Check(Out("greet", "player", "왔구나, 오랜만이야."), noRecordRecent);
        Check(variedAbsence2.IsValid && variedAbsence2.Line != "왔구나, 반가워.", "'오랜만이야'도 최근 대사와 겹치지 않는 말로 ← " + variedAbsence2.Line + " " + variedAbsence2.Problem);
        OutputCheck plainAbsence = validator.Check(Out("greet", "player", "어서 왔구나, 오랜만에 보네"), noRecord);
        Check(plainAbsence.IsValid && plainAbsence.Line == "어서 와, 반가워", "최근 대사와 겹치지 않으면 예전처럼 '반가워' ← " + plainAbsence.Line);
        Check(NpcLineChecks.FixNoRecord("왔구나, 오랜만에 보자! 반갑다.") == "왔구나, 반가워! 반갑다." && NpcLineChecks.FixNoRecord("오랜만이야!", 2) == "보고 싶었어!"
                && NpcLineChecks.FixNoRecord("오랜만이야!", 5) == "기다렸어!", "'오랜만에 보자'도 고치고, 바꿔 넣는 말은 번호로 고름 (22:16 시험 S22 #3)");
        // 2026-11-24 기기 시험: 문장 안에 남은 다른 꼴의 '오랜만'이 인사 실패(기록과 어긋남)의 첫째 원인이었다 — 지우거나 바꾼다
        var absenceCases = new[]
        {
            new[] { "드디어 오랜만에 하늘이 나를 찾았네.", "드디어 하늘이 나를 찾았네." },
            new[] { "오랜만이라 반가워!", "반가워!" },
            new[] { "오랜만인데 잘 지냈어?", "잘 지냈어?" },
            new[] { "와, 정말 오랜만!", "와, 반가워!" },
            new[] { "왔구나! 오랜만이네요.", "왔구나! 반가워." },
            new[] { "오랜만이구나, 기다렸어.", "반가워, 기다렸어." },
            new[] { "오랜만에 반가워!", "반가워!" },
            new[] { "오랜만에 봤네!", "반가워!" },
            new[] { "오랫만에 왔네!", "왔네!" },
            new[] { "오랜만이라고 해야겠다.", "오랜만이라고 해야겠다." }, // 지우면 "고 해야겠다" 토막이 남아 그대로 둔다 (출력 검사가 거른다)
            new[] { "오랜만에 보러 왔구나.", "오랜만에 보러 왔구나." },  // 지우면 "보러 왔구나"만 남아 그대로 둔다
            new[] { "오랜만에야 왔구나.", "오랜만에야 왔구나." }       // 뒤에 글자가 붙은 꼴: 지우면 "야 왔구나" 토막이 남아 그대로 둔다 (2026-11-25 리뷰)
        };
        foreach (string[] c in absenceCases)
        {
            string fixedAbsence = NpcLineChecks.FixNoRecord(c[0]);
            Check(fixedAbsence == c[1], "기록 없는 '오랜만' 고침: '" + c[0] + "' → '" + c[1] + "' ← " + fixedAbsence);
        }
        Check(NpcLineChecks.FixLongAbsence("오랜만이라 반가워!", "오늘, 어제") == "반가워!" && NpcLineChecks.FixLongAbsence("정말 오랜만이구나!", "오늘, 2일 전") == "정말 또 왔네!"
                && NpcLineChecks.FixLongAbsence("오랜만인데 반가워!", "오늘") == "반가워!",
                "손님 장면(최근 방문 7일 안·첫 방문)도 문장 안의 '오랜만' 마디를 고침");
        ReactionRequest withCare = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        withCare.Situation = "주인이 안부를 물었다.";
        withCare.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, "9"));
        Expect(validator, Out("answer_status", "player", "오랜만이야, 나 별일 없어."), withCare, OutputProblem.None, "마지막 관리 사실이 있으면 '오랜만'은 근거가 있음");
        ReactionRequest withMemory = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.RecallTalk);
        withMemory.Situation = "주인이 안부를 물었다.";
        withMemory.Memory.Add(new MemoryEntry(3, NpcActions.TellStory, "주인에게 암행어사 이야기를 들려줬다"));
        Expect(validator, Out("recall_talk", "player", "오랜만이네, 그때 일 기억나."), withMemory, OutputProblem.None, "대화 기억이 있으면 '오랜만'은 근거가 있음");
        ReactionRequest longGap = Request(8, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankCalm);
        longGap.Situation = "주인이 오랜만에 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
        Expect(validator, Out("thank_calm", "player", "오랜만이라 더 개운해, 고마워."), longGap, OutputProblem.None, "상황 문장에 '오랜만'이 있으면 근거가 있음");
        ReactionRequest firstVisit = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        firstVisit.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "1"));
        Expect(validator, Out("wander_greet", "player", "오랜만이야, 구경해."), firstVisit, OutputProblem.WrongHistory, "손님 첫 방문에 '오랜만'은 방문 횟수로 거름 (기존)");
        Check(NpcLineChecks.NoRecordContradiction("또 만나서 반가워", "시험") == "또만나", "'또 만나서'도 기록 없음과 어긋남");
        Check(NpcLineChecks.NoRecordContradiction("반가워, 순찰 도는 중이야", "시험") == null, "평범한 말은 어긋나지 않음");
        // 시험 장면: 기록이 없는 주인 탭 인사(S01·S02)
        foreach (Dictionary<string, object> scene in scenes)
        {
            if ((string)scene["input"] != NpcInputs.Tap)
            {
                continue;
            }
            ReactionRequest request = ToRequest(scene);
            Check(request.Memory.Count == 0 && request.FindFact(NpcFactKeys.LastCareDaysAgo) == null
                    && new NpcOutputValidator(filter, personas) { NormalizeSpeech = false }.Check(Out("greet", "player", "오랜만이야, 어흠."), request).Problem == OutputProblem.WrongHistory
                    && validator.Check(Out("greet", "player", "오랜만이야, 순찰 중이었어."), request).Problem == OutputProblem.None,
                    scene["id"] + " 탭 인사(기록 없음)에 '오랜만이야'는 (다듬기를 끄면) 거르고, 다듬기가 '반가워'로 고쳐 통과");
        }

        // 4) 옛말투 어미: 반말 도깨비는 '~구먼'·'~는가' 같은 말을 쓰지 않는다 (페르소나의 endingHint.bannedPattern)
        string[] oldFashioned =
        {
            "오셨구먼.", "참 좋구먼, 쉬어 가.", "잘 지냈는가?", "어서 가시게.", "반갑다네.", "좋다고들 하더라네.", "이 몸이 지키겠소.", "좋소, 놀아 보자.",
            "여기가 바로 그곳이올시다.", "인사드리옵니다.", "같이 놀아 봅시다.", "함께 가노라.", "안녕하오.", "참 반갑구려."
        };
        string[] modern =
        {
            "왔구나, 순찰은 내가 했어.", "그럴 수도 있지~", "좋은 소식이야!", "황소가 지나갔어.", "고소한 냄새가 나네.", "네가 와서 좋아!", "하나도 안 아파."
        };
        foreach (Persona persona in personas.personas)
        {
            Check(persona.endingHint != null && persona.endingHint.bannedPattern.Contains("구먼"), persona.name + " 어미 금지 패턴에 '~구먼'이 있음");
            foreach (string line in oldFashioned)
            {
                string wrong;
                Check(!NpcLineChecks.SpeechMatches(line, persona.endingHint, out wrong), persona.name + " 옛말투는 걸림: " + line);
            }
            foreach (string line in modern)
            {
                string wrong;
                Check(NpcLineChecks.SpeechMatches(line, persona.endingHint, out wrong), persona.name + " 정상 반말은 통과: " + line + " ← " + wrong);
            }
        }

        // 5) 페르소나: 좋아하는 것·싫어하는 것·끌려갈 때·말투 다짐 칸이 없고, 말투는 캐릭터 말씨만 (금지 목록 없음)
        foreach (string key in new[] { "\"likes\"", "\"dislikes\"", "\"onDrag\"", "\"speechReminder\"", "\"drag\"" })
        {
            Check(!personaJson.Contains(key), "personas.json 에 " + key + " 칸이 없음");
        }
        foreach (Persona persona in personas.personas)
        {
            string style = persona.speechStyle ?? "";
            Check(style.Length > 0 && style.Length <= 45, persona.name + " 말투는 캐릭터 말씨 한두 문장 ← " + style);
            string banned = new[] { "않는다", "쓰지", "없이", "금지", "섞지", "구먼", "하게", "는가", "-시-", "존댓말", "반말", "높임" }.FirstOrDefault(w => style.Contains(w));
            Check(banned == null, persona.name + " 말투에 금지 목록·어미 조각이 없음 (작은 모델이 그대로 옮김) ← " + banned);
            string system = assembler.SystemFor(persona.relicId);
            Check(system.Contains("말투: " + style) && system.Contains(PromptAssembler.RuleSpeech) && PromptAssembler.RuleSpeech.Contains("반말"),
                    persona.name + " 시스템 메시지: 말씨는 말투 줄, 반말은 공통 규칙");
            Check(!system.Contains("존댓말") && !system.Contains("높임"), persona.name + " 시스템 메시지에 존댓말·높임 금지 문구가 없음");
        }
    }

    // ---------- 서버가 넘기는 시간·닉네임·방문 기록 / 날씨 금지 / 실제 기록 / 주체 (2026-11-13) ----------

    private static void CheckServerContext(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 서버 입력(시간·닉네임·방문 기록)·날씨·실제 기록·주체 ===");
        var filter = new NpcLineFilter(forbidden);
        var validator = new NpcOutputValidator(filter, personas);
        var assembler = new PromptAssembler(personas, actions);
        var builder = new NpcContextBuilder(personas);

        // 1) 시간대: 서버 시각에서 계산해 탭 인사에만 넘긴다
        Check(NpcContextBuilder.TimeOfDay(new DateTime(2026, 11, 13, 3, 0, 0)) == "새벽" && NpcContextBuilder.TimeOfDay(new DateTime(2026, 11, 13, 5, 0, 0)) == "아침"
                && NpcContextBuilder.TimeOfDay(new DateTime(2026, 11, 13, 9, 30, 0)) == "오전" && NpcContextBuilder.TimeOfDay(new DateTime(2026, 11, 13, 12, 0, 0)) == "낮"
                && NpcContextBuilder.TimeOfDay(new DateTime(2026, 11, 13, 17, 0, 0)) == "저녁" && NpcContextBuilder.TimeOfDay(new DateTime(2026, 11, 13, 23, 59, 0)) == "밤",
                "시간대: 새벽(0~5)·아침(5~9)·오전(9~12)·낮(12~17)·저녁(17~21)·밤(21~24)");
        NpcContextInput tapInput = Owner(8, NpcInputs.Tap, "CLEAR", null);
        tapInput.Now = Today.AddHours(14);
        NpcContext tapCtx = builder.Build(tapInput);
        Check(tapCtx.Request.FindFact(NpcFactKeys.TimeOfDay) == "낮" && tapCtx.Request.Situation == "주인이 낮에 너를 보러 찾아왔다.",
                "탭 인사: 서버 시각 14시 → 시간대 '낮'을 자료로 넘기고 상황 문장에 탭한 때로 녹임 ← " + tapCtx.Request.Situation);
        tapCtx.Request.Variation = 0;
        string tapPrompt = assembler.Build(tapCtx.Request).User;
        Check(tapPrompt.Contains("발굴자가 낮에 너를 보러 찾아왔다.") && !tapPrompt.Contains("지금 시간대"),
                "프롬프트: 시간대는 상황 문장에만 (사실 칸에 또 싣지 않음) ← " + tapPrompt);
        // 시간대는 세 번에 한 번(Variation 0·3·6…)만 싣는다 — 늘 싣자 탭 인사가 "오늘 아침부터 …"로 거의 모두 같게 시작했다
        string timeless = "";
        foreach (int variation in new[] { 1, 2 })
        {
            tapCtx.Request.Variation = variation;
            timeless = assembler.Build(tapCtx.Request).User;
            Check(timeless.Contains("상황: 발굴자가 너를 보러 찾아왔다.") && !timeless.Contains("낮에 찾아와"), "Variation " + variation + ": 시간대 없는 상황 문장 ← " + timeless);
        }
        tapCtx.Request.Variation = 3;
        Check(assembler.Build(tapCtx.Request).User.Contains("상황: 발굴자가 낮에 너를 보러 찾아왔다."), "Variation 3: 시간대를 다시 실음");
        Check(tapCtx.Request.Situation == "주인이 낮에 너를 보러 찾아왔다.", "요청의 상황 문장 자체는 그대로 (프롬프트 조립 때만 뺌 — 출력 검사·캐시 열쇠는 그대로)");
        NpcContextInput noClock = Owner(8, NpcInputs.Tap, "CLEAR", null);
        Check(builder.Build(noClock).Request.FindFact(NpcFactKeys.TimeOfDay) == null && builder.Build(noClock).Request.Situation == "주인이 너를 보러 찾아왔다.",
                "서버 시각이 없으면 시간대 이야기를 하지 않음");
        NpcContextInput askInput = Owner(8, NpcInputs.AskStatus, "CLEAR", 2);
        askInput.Now = Today.AddHours(14);
        Check(builder.Build(askInput).Request.FindFact(NpcFactKeys.TimeOfDay) == null, "시간대는 탭 인사에만 넘김 (안부에는 없음)");

        // 2) 호칭: 30일에 12번 이상(친밀도 3단계) 방문하면 닉네임, 아니면 '발굴자'
        NpcContextInput few = Guest(8, NpcInputs.Visit, null);
        few.Nickname = "도윤";
        few.VisitDates = new List<DateTime> { Today, Today.AddDays(-3) };
        NpcContext fewCtx = builder.Build(few);
        Check(!fewCtx.Request.Familiar && fewCtx.Request.AddressName == "발굴자" && fewCtx.Request.Nickname == "도윤", "30일에 2번 방문이면 '발굴자'");
        NpcContextInput many = Guest(8, NpcInputs.Visit, null);
        many.Nickname = "도윤";
        many.VisitDates = new List<DateTime> { Today, Today.AddDays(-3), Today.AddDays(-9), Today.AddDays(-20) };
        NpcContext manyCtx = builder.Build(many);
        Check(!manyCtx.Request.Familiar && manyCtx.Request.AddressName == "발굴자" && manyCtx.Request.Nickname == "도윤",
                "30일에 4번 방문이면 1단계라 아직 '발굴자' ← " + manyCtx.Request.AddressName);
        NpcContextInput seven = Guest(8, NpcInputs.Visit, null);
        seven.Nickname = "도윤";
        seven.VisitDates = new List<DateTime> { Today, Today.AddDays(-3), Today.AddDays(-9), Today.AddDays(-20), Today.AddDays(-22), Today.AddDays(-25), Today.AddDays(-28) };
        NpcContext sevenCtx = builder.Build(seven);
        Check(sevenCtx.Request.Intimacy == 2 && !sevenCtx.Request.Familiar && sevenCtx.Request.AddressName == "발굴자", "30일에 7번 방문이면 2단계라 아직 '발굴자' ← " + sevenCtx.Request.AddressName);
        // 12번: 어제(-1)는 빼 둔다 — 아래 호칭 검사가 "어제 왔었지"는 기록에 없어 걸리는지 보려는 것이다
        NpcContextInput twelve = Guest(8, NpcInputs.Visit, null);
        twelve.Nickname = "도윤";
        twelve.VisitDates = new List<DateTime> { Today, Today.AddDays(-3), Today.AddDays(-4), Today.AddDays(-6), Today.AddDays(-9), Today.AddDays(-10),
                Today.AddDays(-12), Today.AddDays(-15), Today.AddDays(-20), Today.AddDays(-22), Today.AddDays(-25), Today.AddDays(-28) };
        NpcContext twelveCtx = builder.Build(twelve);
        Check(twelveCtx.Request.Intimacy == 3 && twelveCtx.Request.Familiar && twelveCtx.Request.AddressName == "도윤", "30일에 12번 방문이면 3단계, 닉네임 ← " + twelveCtx.Request.AddressName);
        Check(manyCtx.Request.FindFact(NpcFactKeys.VisitDays) == "오늘, 3일 전, 9일 전, 20일 전" && manyCtx.Request.FindFact(NpcFactKeys.VisitCount30d) == "4",
                "방문 기록은 며칠 전인지 그대로 사실로 넘김 ← " + manyCtx.Request.FindFact(NpcFactKeys.VisitDays));
        string manyPrompt = assembler.Build(manyCtx.Request).User;
        Check(manyPrompt.Contains("호칭: 발굴자") && manyPrompt.Contains("최근 방문한 날: 오늘, 3일 전, 9일 전, 20일 전") && !manyPrompt.Contains("온 횟수"),
                "프롬프트에 호칭과 방문한 날 (횟수는 상황 문장에)");
        Check(assembler.Build(twelveCtx.Request).User.Contains("호칭: 도윤"), "프롬프트에 호칭(닉네임, 3단계)");
        Check(assembler.Build(fewCtx.Request).User.Contains("호칭: 발굴자"), "프롬프트에 호칭(발굴자)");
        NpcContextInput old = Guest(8, NpcInputs.Visit, null);
        old.Nickname = "도윤";
        old.VisitDates = new List<DateTime> { Today, Today.AddDays(-1), Today.AddDays(-35), Today.AddDays(-40), Today.AddDays(1) };
        Check(!builder.Build(old).Request.Familiar && builder.Build(old).Request.FindFact(NpcFactKeys.VisitDays) == "오늘, 어제",
                "30일 밖·미래 날짜는 세지 않고, 같은 날은 하루로 셈");
        NpcContextInput sameDay = Guest(8, NpcInputs.Visit, null);
        sameDay.Nickname = "도윤";
        sameDay.VisitDates = new List<DateTime> { Today, Today.AddHours(5), Today.AddHours(9), Today.AddDays(-2) };
        Check(!builder.Build(sameDay).Request.Familiar, "같은 날 여러 번은 하루로 세어 2번 → 발굴자");
        NpcContextInput noNick = Guest(8, NpcInputs.Visit, 5);
        Check(!builder.Build(noNick).Request.Familiar && builder.Build(noNick).Request.AddressName == "발굴자", "닉네임이 없으면 방문이 많아도 '발굴자'");
        NpcContextInput ownerKnown = Owner(8, NpcInputs.Tap, "CLEAR", null);
        ownerKnown.Nickname = "  하늘\n ";
        ownerKnown.VisitDates = new List<DateTime>();
        for (int d = 0; d < 12; d++)
        {
            ownerKnown.VisitDates.Add(Today.AddDays(-d));
        }
        NpcContext ownerCtx = builder.Build(ownerKnown);
        Check(ownerCtx.Request.Familiar && ownerCtx.Request.AddressName == "하늘", "주인 장면도 방문 기록이 12번 이상이면 닉네임 (공백·줄바꿈은 다듬음) ← [" + ownerCtx.Request.AddressName + "]");
        Check(builder.Build(Owner(8, NpcInputs.Tap, "CLEAR", null)).Request.AddressName == "발굴자", "기록이 없는 주인은 '발굴자'");

        // 친밀도: 30일 방문한 날 수가 3·7·12·20번을 채울 때마다 한 단계 (0~4). 3단계(12번)부터 닉네임, 0~2단계는 '발굴자'
        int[] visitsAt = { 0, 1, 2, 3, 6, 7, 11, 12, 19, 20, 99 };
        int[] levelAt = { 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4 };
        for (int i = 0; i < visitsAt.Length; i++)
        {
            Check(NpcAddress.IntimacyOf(visitsAt[i]) == levelAt[i], "친밀도: 30일 " + visitsAt[i] + "번 방문 → " + levelAt[i] + "단계 ← " + NpcAddress.IntimacyOf(visitsAt[i]));
        }
        Check(NpcAddress.IntimacyOf(null) == 0, "친밀도: 방문 기록이 없으면 0단계");
        Check(fewCtx.Request.Intimacy == 0 && manyCtx.Request.Intimacy == 1, "컨텍스트: 2번 방문 → 0단계, 4번 방문 → 1단계 ← " + fewCtx.Request.Intimacy + "·" + manyCtx.Request.Intimacy);
        Check(sevenCtx.Request.Intimacy == 2 && twelveCtx.Request.Intimacy == 3, "컨텍스트: 7번 방문 → 2단계, 12번 방문 → 3단계 ← " + sevenCtx.Request.Intimacy + "·" + twelveCtx.Request.Intimacy);
        Check(NpcAddress.NicknameLevel == 3
                && NpcTestScenes.IntimacyOf(new Dictionary<string, object> { { "familiar", true } }) == 3
                && NpcTestScenes.IntimacyOf(new Dictionary<string, object> { { "familiar", false } }) == 0
                && NpcTestScenes.IntimacyOf(new Dictionary<string, object> { { "intimacy", 0 } }) == 0
                && NpcTestScenes.IntimacyOf(new Dictionary<string, object> { { "intimacy", 1.0 } }) == 1
                && NpcTestScenes.IntimacyOf(new Dictionary<string, object> { { "intimacy", 9 } }) == 4,
                "장면 칸: intimacy(0~4)가 우선, 없으면 familiar(true → 3단계, false → 0단계)");

        // 호칭 검사: 닉네임·발굴자는 모든 대사에 필수가 아니고, 규칙과 다른 호칭만 거른다
        ReactionRequest familiar = twelveCtx.Request;
        familiar.Candidates.Clear();
        familiar.Candidates.Add(new ActionCandidate(NpcActions.WanderRecall, NpcTargets.Player));
        Expect(validator, Out("wander_recall", "player", "도윤, 또 왔네?"), familiar, OutputProblem.None, "친밀하면 닉네임으로 불러도 됨");
        Expect(validator, Out("wander_recall", "player", "어서 와, 반가워!"), familiar, OutputProblem.None, "호칭은 꼭 넣지 않아도 됨");
        Expect(validator, Out("wander_recall", "player", "반가워, 발굴자 덕분에 든든해."), familiar, OutputProblem.WrongAddress, "친밀한데 문장 안에서 '발굴자'라고 하면 WrongAddress");
        // 부르는 말은 거르지 않고 맞는 호칭으로 바꾼다 (다시 만드는 시간을 아낀다)
        OutputCheck calledWrong = validator.Check(Out("wander_recall", "player", "발굴자, 또 왔네?"), familiar);
        Check(calledWrong.IsValid && calledWrong.Line == "도윤, 또 왔네.", "친밀한데 '발굴자,'로 부르면 닉네임으로 바꿈 ← " + calledWrong.Line + " " + calledWrong.Detail);
        OutputCheck calledFriend = validator.Check(Out("wander_recall", "player", "친구야, 또 왔네?"), familiar);
        Check(calledFriend.IsValid && calledFriend.Line == "도윤아, 또 왔네.", "'친구야,'도 닉네임으로 바꾸고 받침에 맞게 '아'를 붙임 ← " + calledFriend.Line);
        ReactionRequest stranger = fewCtx.Request;
        stranger.Candidates.Clear();
        stranger.Candidates.Add(new ActionCandidate(NpcActions.WanderRecall, NpcTargets.Player));
        Expect(validator, Out("wander_recall", "player", "발굴자, 또 왔네?"), stranger, OutputProblem.None, "친밀하지 않으면 '발굴자'로 불러도 됨");
        Expect(validator, Out("wander_recall", "player", "도윤이 또 왔네?"), stranger, OutputProblem.WrongAddress, "친밀하지 않은데 문장 안에서 닉네임을 말하면 WrongAddress");
        OutputCheck calledNick = validator.Check(Out("wander_recall", "player", "도윤, 또 왔네?"), stranger);
        Check(calledNick.IsValid && calledNick.Line == "발굴자, 또 왔네.", "친밀하지 않은데 닉네임으로 부르면 '발굴자'로 바꿈 ← " + calledNick.Line + " " + calledNick.Detail);
        Check(NpcLineChecks.UnifyPlayerCall("어서 와, 친구!", "발굴자", "도윤") == "어서 와, 발굴자!"
                && NpcLineChecks.UnifyPlayerCall("손님, 어서 와!", "도윤", "도윤") == "도윤, 어서 와!"
                && NpcLineChecks.UnifyPlayerCall("친구가 왔네, 반가워!", "발굴자", "도윤") == "친구가 왔네, 반가워!"
                && NpcLineChecks.UnifyPlayerCall("너는 내 친구야, 고마워!", "발굴자", "도윤") == "너는 내 친구야, 고마워!"
                && NpcLineChecks.UnifyPlayerCall("발굴자야, 어서 와!", "도윤", "도윤") == "도윤아, 어서 와!"
                && NpcLineChecks.UnifyPlayerCall("하늘아, 어서 와!", "하늘", "하늘") == "하늘아, 어서 와!"
                && NpcLineChecks.UnifyPlayerCall("어서 와!", "발굴자", "도윤") == "어서 와!",
                "플레이어를 부르는 말은 하나로 맞추고, 문장 안의 '친구가'·'내 친구야'와 부르지 않은 대사는 그대로");

        // 3) 실제 기록: 대사의 "이틀 전"·"어제"는 자료의 날과 같아야 한다
        ReactionRequest care = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        care.Situation = "주인이 안부를 물었다. 너는 이틀 전에 닦아 관리를 받았다. 고마움을 전한다.";
        care.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, "2"));
        Expect(validator, Out("answer_status", "player", "이틀 전에 닦아 줘서 고마워."), care, OutputProblem.None, "관리 기록(2일 전)과 같은 '이틀 전'은 통과");
        Expect(validator, Out("answer_status", "player", "그저께 닦아 줘서 고마워."), care, OutputProblem.None, "'그저께'도 2일 전");
        Expect(validator, Out("answer_status", "player", "어제 닦아 줘서 고마워."), care, OutputProblem.WrongHistory, "기록에 없는 '어제'는 WrongHistory");
        Expect(validator, Out("answer_status", "player", "사흘 전에 닦아 줘서 고마워."), care, OutputProblem.WrongHistory, "기록과 다른 '사흘 전'은 WrongHistory");
        Expect(validator, Out("answer_status", "player", "5일 전에 닦아 줘서 고마워."), care, OutputProblem.WrongHistory, "기록과 다른 '5일 전'은 WrongHistory");
        ReactionRequest noCare = Request(5, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        noCare.Situation = "주인이 너를 탭했다.";
        Expect(validator, Out("greet", "player", "이틀 전에 왔었지, 반가워."), noCare, OutputProblem.WrongHistory, "기록이 없는데 '이틀 전'은 WrongHistory");
        Expect(validator, Out("greet", "player", "오늘도 왔구나, 반가워."), noCare, OutputProblem.None, "'오늘'은 날짜 표현으로 보지 않음");
        Expect(validator, Out("wander_recall", "player", "3일 전에 왔었지, 또 왔네."), familiar, OutputProblem.None, "방문 기록(3일 전)에 있는 날은 통과");
        Expect(validator, Out("wander_recall", "player", "어제 왔었지, 또 왔네."), familiar, OutputProblem.WrongHistory, "방문 기록에 없는 '어제'는 WrongHistory");

        // 주체: 도깨비가 자기 것(밤바다의 자개)을 '내 ~' 없이 남의 솜씨를 구경하듯 말하면 걸린다 (2026-11-16 시험 S14)
        ReactionRequest memoryTalk = Request(6, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.RecallTalk);
        Expect(validator, Out("recall_talk", "player", "오, 자개가 정말 공들여 만든 거네 덕분에 빛나네."), memoryTalk, OutputProblem.WrongSubject, "자기 것을 구경하듯 말하면 WrongSubject");
        Expect(validator, Out("recall_talk", "player", "나 전에는 내 자개가 공들여 만든 거라고 자랑했어."), memoryTalk, OutputProblem.None, "'내 자개'처럼 자기 것이라고 말하면 통과");
        Check(NpcLineChecks.SubjectContradiction("자개가 정말 공들여 만든 거네.", new List<string> { "자개", "몸" }) == "자개"
                && NpcLineChecks.SubjectContradiction("몸이 정성껏 빚은 거네.", new List<string> { "자개", "몸" }) == null
                && NpcLineChecks.SubjectContradiction("자개가 반짝여서 기분이 좋아.", new List<string> { "자개" }) == null
                && NpcLineChecks.SubjectContradiction("우리 자개가 공들여 만든 거네.", new List<string> { "자개" }) == null
                && NpcLineChecks.SubjectContradiction("자개가 공들여 만든 거네.", null) == null,
                "주체 검사: 자기 것 + 솜씨 낱말 + 구경하는 끝맺음일 때만. 내·우리가 앞에 있거나 몸이거나 끝맺음이 다르면 통과");
        // 2026-11-18 20:22 시험 S14 #1: 자기 것을 남이 만든 것처럼 입음꼴로 말한 "자개가 정성스럽게 만들어졌네"가 통과했다
        var craftThings = new List<string> { "자개", "옻칠", "몸" };
        foreach (string shifted in new[] { "오, 자개가 정말 정성스럽게 만들어졌네.", "자개가 공들여 새겨져 있네!", "옻칠이 정성스럽게 칠해졌구나.", "자개는 정성 들여 다듬어졌어.",
                "자개가 정성껏 빚은 거네.", "자개가 많이 정성스럽게 만들어졌네." })
        {
            Check(NpcLineChecks.SubjectContradiction(shifted, craftThings) != null, "자기 것을 입음꼴로 구경하듯 말하면 주체 어긋남: " + shifted);
        }
        foreach (string own in new[] { "내 자개가 정성스럽게 만들어졌어.", "옻칠이 벗겨졌어.", "자개가 흐려졌어.", "자개가 정성 덕분에 빛나네.", "몸이 정성스럽게 다듬어졌네.",
                // 쉼표·다른 주어 뒤의 입음꼴은 자기 것 이야기가 아니다 (CodeRabbit 리뷰)
                "자개가 반짝이고, 그림은 정성스럽게 완성됐어.", "자개가 반짝이고 그림은 정성스럽게 완성됐어.", "자개가 빛나, 그림이 공들여 만들어졌네." })
        {
            Check(NpcLineChecks.SubjectContradiction(own, craftThings) == null, "솜씨 낱말이 없거나 '내'가 있거나 몸이면 통과: " + own);
        }
        Expect(validator, Out("recall_talk", "player", "오, 자개가 정말 정성스럽게 만들어졌네."), memoryTalk, OutputProblem.WrongSubject, "'자개가 정성스럽게 만들어졌네'는 WrongSubject (S14 #1)");
        // 2026-11-18 23:45 시험 S14 #3: 자기 것의 이름 없이 자기 솜씨를 남의 솜씨처럼 칭찬했다
        Expect(validator, Out("recall_talk", "player", "정말? 그렇게 공들인 거라면 대단하네."), memoryTalk, OutputProblem.WrongSubject, "'공들인 거라면 대단하네'는 남의 솜씨 칭찬 (S14 #3)");
        Check(NpcLineChecks.CraftPraise("정말? 그렇게 공들인 거라면 대단하네.", NpcActions.RecallTalk) == "대단하네"
                && NpcLineChecks.CraftPraise("내 자개는 공들여 만든 거라 대단하지!", NpcActions.RecallTalk) == null
                && NpcLineChecks.CraftPraise("나 공들여 만든 거 멋지지?", NpcActions.RecallTalk) == null
                && NpcLineChecks.CraftPraise("공들여 꾸민 전시관이 멋지네!", NpcActions.WanderGreet) == null
                && NpcLineChecks.CraftPraise("개운하게 관리해줘서 고마워, 정말 멋지다!", NpcActions.ThankCalm) == null
                && NpcLineChecks.CraftPraise("그렇게 공들였다니 대단해.", NpcActions.RecallTalk) == "대단해"
                && NpcLineChecks.CraftPraise("멋진 걸 공들여 만들었지.", NpcActions.RecallTalk) == null,
                "솜씨 칭찬: 자기 이야기에서 솜씨 낱말 + 칭찬이고 '내·나·우리'가 없을 때만 (손님 관람·솜씨 낱말 없는 칭찬은 통과)");
        // 2026-11-18 23:45 시험 S06 #4: "어서 왔어?"를 "어서 와"로 고치며 물음표가 남아 "어서 와? 또 왔네."
        Check(NpcLineChecks.NormalizeSpeech("어서 왔어? 오랜만이야.", "반말", "향산이", null, null, "오늘, 3일 전", "발굴자") == "어서 와! 또 왔네."
                && NpcLineChecks.NormalizeSpeech("어서 와? 또 왔네.", "반말", "향산이") == "어서 와! 또 왔네."
                && NpcLineChecks.NormalizeSpeech("어서 와서 쉬어.", "반말", "향산이") == "어서 와서 쉬어.",
                "'어서 왔어?'·'어서 와?'는 '어서 와!'로, '어서 와서'는 그대로");

        // 2026-11-18 20:22 시험 S05 #1: "처음 온 너를 만나니 기쁘다!"에서 '너를'을 지워 "처음 온 만나니 기쁘다!"가 됐다.
        // 꾸밈말 뒤·'위해/위한' 앞의 플레이어 말은 지우지 않고 이름으로 바꿔 둔다 → PlayerThirdPerson 이 거르고 다시 만든다
        foreach (string[] pair in new[]
        {
            new[] { "와, 처음 온 너를 만나니 기쁘다!", "발굴자", "와, 처음 온 발굴자를 만나니 기쁘다!" },
            new[] { "다시 만난 도윤을 보니 좋아.", "도윤", "다시 만난 도윤을 보니 좋아." },
            new[] { "보고 싶던 너를 만나 기뻐!", "도윤", "보고 싶던 도윤을 만나 기뻐!" },
            new[] { "반가운 너를 보니 좋아.", "도윤", "반가운 도윤을 보니 좋아." },
            new[] { "그건 너를 위한 거야.", "도윤", "그건 도윤을 위한 거야." },
            new[] { "도윤을 위해 준비했어.", "도윤", "도윤을 위해 준비했어." },
            // 꾸밈말이 아닌 앞 낱말(때·부사·'나는'의 줄임)은 예전처럼 지운다
            new[] { "오늘 너를 만나서 기뻐!", "도윤", "오늘 만나서 기뻐!" },
            new[] { "정말 너를 기다렸어.", "도윤", "정말 기다렸어." },
            new[] { "난 너를 기다렸어.", "도윤", "난 기다렸어." },
            new[] { "나는 너를 기다렸어.", "도윤", "나는 기다렸어." },
            new[] { "오랜만에 너를 찾아줘서 고마워!", "도윤", "오랜만에 찾아줘서 고마워!" },
            // '-는' 꾸밈말('아는')은 조사('나는')와 다르다, '사실'은 부사 (CodeRabbit 리뷰)
            new[] { "잘 아는 너를 만나서 기뻐.", "도윤", "잘 아는 도윤을 만나서 기뻐." },
            new[] { "사실 너를 기다렸어.", "도윤", "사실 기다렸어." },
            new[] { "친구는 너를 좋아해.", "도윤", "친구는 좋아해." },
            new[] { "처음 온 네가 반가워!", "발굴자", "처음 온 발굴자가 반가워!" },
        })
        {
            string fixedLine = NpcLineChecks.DropPlayerReference(NpcLineChecks.PlayerPronounsToName(pair[0], pair[1]), pair[1]);
            Check(fixedLine == pair[2], "꾸밈말 뒤 플레이어 말: '" + pair[0] + "' → '" + pair[2] + "' ← '" + fixedLine + "'");
        }
        Check(NpcLineChecks.AfterModifier("처음 온 너를", 5) && NpcLineChecks.AfterModifier("다시 만난 너를", 6) && !NpcLineChecks.AfterModifier("오늘 너를", 3)
                && !NpcLineChecks.AfterModifier("나는 너를", 3) && !NpcLineChecks.AfterModifier("너를", 0) && !NpcLineChecks.AfterModifier("고마워, 너를", 5),
                "꾸밈말 찾기: '온'·'만난'은 꾸밈말, '오늘'·'나는'·쉼표 뒤·첫머리는 아님");
        ReactionRequest firstVisit = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        OutputCheck dangling = validator.Check(Out("wander_greet", "player", "와, 처음 온 너를 만나니 기쁘다!"), firstVisit);
        Check(dangling.Problem == OutputProblem.WrongSubject && dangling.Line != "와, 처음 온 만나니 기쁘다!",
                "'처음 온 너를 만나니'는 '처음 온 만나니'로 내보내지 않고 거름 (S05 #1) ← " + dangling.Problem + " " + dangling.Line);
        Expect(validator, Out("wander_greet", "player", "와, 너를 만나니 기쁘다!"), firstVisit, OutputProblem.None, "꾸밈말이 없으면 '너를'을 지우고 통과");
        Check(NpcLineChecks.AfterModifier("잘 아는 너를", 5) && !NpcLineChecks.AfterModifier("사실 너를", 3) && !NpcLineChecks.AfterModifier("친구는 너를", 4),
                "꾸밈말 찾기: '아는'은 꾸밈말, '사실'·'친구는'은 아님");
        // 꾸밈말 뒤라 남긴 주어('발굴자가')도 거른다. 부르는 말('발굴자,'·'도윤이,'·'도윤이야')은 통과 (CodeRabbit 리뷰)
        Expect(validator, Out("wander_greet", "player", "와, 처음 온 네가 반가워!"), firstVisit, OutputProblem.WrongSubject, "'처음 온 발굴자가 반가워'는 남 얘기하듯 부름");
        Expect(validator, Out("wander_greet", "player", "발굴자, 반가워! 편히 구경해."), firstVisit, OutputProblem.None, "'발굴자,'라고 부르는 말은 통과");
        Check(NpcLineChecks.PlayerThirdPerson("처음 온 발굴자가 반가워!", "발굴자") == "발굴자가" && NpcLineChecks.PlayerThirdPerson("도윤이, 반가워!", "도윤") == null
                && NpcLineChecks.PlayerThirdPerson("도윤이야, 반가워.", "도윤") == null && NpcLineChecks.PlayerThirdPerson("발굴자가!", "발굴자") == null,
                "남은 주어 '발굴자가 ~'는 거르고, '도윤이,'·'도윤이야' 부르는 말은 통과");
        // 다듬기가 주어 '발굴자가'를 지워도 설명문은 거른다 — 다듬기 전 대사에서 걸린 설명문이 내보낼 대사에 남았으면 (CodeRabbit 리뷰)
        Expect(validator, Out("wander_greet", "player", "발굴자가 그림을 그리고 있다."), firstVisit, OutputProblem.Forbidden, "주어를 지운 '그림을 그리고 있다.'도 설명문");
        Expect(validator, Out("wander_greet", "player", "와, 반가워! 난 다 알고 있다."), firstVisit, OutputProblem.None, "주어가 나인 '난 다 알고 있다.'는 통과");
        Check(NpcLineChecks.NarrationKept("발굴자가그림을그리고있다.", "그림을그리고있다.") && !NpcLineChecks.NarrationKept("발굴자가그림을그리고있다.", "와,반가워!")
                && !NpcLineChecks.NarrationKept("화면", "화면"), "설명문이 내보낼 대사에 남았는지 (주어를 빼고 견줌, 네 글자 미만은 안 봄)");
        Check(NpcLineChecks.DayRefs("어제랑 10일 전, 이틀전, 오늘", false).Select(r => r.Value).OrderBy(v => v).SequenceEqual(new[] { 1, 2, 10 }),
                "'며칠 전' 표현 읽기 (어제·N일 전·이틀전, 오늘은 뺌)");

        // 4) 시간대: 지금 시간대와 어긋나는 말, 자료에 없는 시간대를 거른다
        ReactionRequest noon = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        noon.Situation = "주인이 너를 탭했다.";
        noon.Facts.Add(new Fact(NpcFactKeys.TimeOfDay, "낮"));
        Expect(validator, Out("greet", "player", "낮인데 순찰은 내가 했어."), noon, OutputProblem.None, "지금 시간대(낮)를 말하면 통과");
        Expect(validator, Out("greet", "player", "좋은 밤이야, 왔구나."), noon, OutputProblem.WrongTime, "낮인데 '밤'은 WrongTime");
        Expect(validator, Out("greet", "player", "오늘 아침부터 기다렸어."), noon, OutputProblem.WrongTime, "낮인데 '아침'도 WrongTime");
        Expect(validator, Out("greet", "player", "낮잠 자다 깼어, 왔구나."), noon, OutputProblem.None, "'낮잠'은 시간대 낱말이 아님");
        ReactionRequest noClockReq = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        noClockReq.Situation = "주인이 너를 탭했다.";
        Expect(validator, Out("greet", "player", "오늘 밤 순찰도 내가 할게."), noClockReq, OutputProblem.WrongTime, "자료에 시간대가 없는데 '오늘 밤'은 WrongTime");
        Expect(validator, Out("greet", "player", "밤새 순찰했어, 왔구나."), noClockReq, OutputProblem.None, "'밤새'는 시간대를 말한 것이 아님");

        // 5) 날씨는 말하지 않는다 (서버가 날씨를 넘기지 않는다)
        string[] weather = { "오늘 날씨가 참 좋네!", "비가 와서 눅눅해.", "눈이 내려서 반짝여.", "바람이 좋아서 기분 좋아.", "햇살이 따뜻해서 낮잠 자고 싶어.", "하늘이 맑아서 좋아.", "기온이 떨어졌어.", "날이 추워졌네." };
        foreach (string line in weather)
        {
            Check(filter.Find(line) != null, "날씨 이야기는 거름: " + line);
        }
        foreach (string line in new[] { "하늘처럼 맑아졌어.", "바람 쐬러 가고 싶어.", "구름 같은 날개가 가벼워졌어.", "눈이 부시게 반짝여.", "마음이 따뜻해졌어." })
        {
            Check(filter.IsAllowed(line), "날씨가 아닌 말은 통과: " + line + " ← " + filter.Find(line));
        }
        Check(PromptAssembler.RuleFacts.Contains("시간대") && PromptAssembler.RuleFacts.Contains("며칠 전") && !PromptAssembler.RuleFacts.Contains("날씨"),
                "공통 규칙: 시간대·며칠 전은 자료에 적힌 대로 (날씨는 규칙에 적지 않고 금칙어가 거름)");

        // 6) 주체: 자기 것을 '네'라고 부르지 않는다 (밤바다 "네 자개는")
        Persona night = personas.Find(6);
        Check(NpcLineChecks.FixOwnPossessive("네 자개는 정말 반짝여.", night.ownThings) == "내 자개는 정말 반짝여."
                && NpcLineChecks.FixOwnPossessive("너의 옻칠이 곱지?", night.ownThings) == "나의 옻칠이 곱지?"
                && NpcLineChecks.FixOwnPossessive("네 덕분에 자개가 반짝여.", night.ownThings) == "네 덕분에 자개가 반짝여."
                && NpcLineChecks.FixOwnPossessive("네!", night.ownThings) == "네!", "'네 자개'→'내 자개', '너의 옻칠'→'나의 옻칠', 상대의 것('네 덕분에')은 그대로");
        ReactionRequest nightTap = Request(6, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        OutputCheck swapped = validator.Check(Out("greet", "player", "네 자개는 오늘도 반짝여."), nightTap);
        Check(swapped.IsValid && swapped.Line == "내 자개는 오늘은 반짝여." && swapped.LineBeforeSpeechFix == "네 자개는 오늘도 반짝여.",
                "출력 검사가 '네 자개는'을 '내 자개는'으로 고침 ← " + swapped.Line + " / " + swapped.Detail);
        foreach (Persona persona in personas.personas)
        {
            Check(persona.ownThings != null && persona.ownThings.Count >= 3, persona.name + " 자기 것(ownThings) 목록이 있음");
        }
        Check(!PromptAssembler.RuleRole.Contains("네 ") && PromptAssembler.RuleRole.Contains("'내 ~'"), "공통 규칙에 '네 몸'·'네 것'이 없음 (모델이 '네'를 옮긴다), 자기 것은 '내 ~'");

        // 7) 페르소나 설명문을 대사로 읊지 않는다 ("가장 친한 동료처럼 반긴다")
        Persona horse = personas.Find(8);
        ReactionRequest horseTap = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        Expect(validator, Out("greet", "player", "가까운 동료를 대하듯 편한 태도로 왔어."), horseTap, OutputProblem.PersonaCopy, "상대를 대하는 태도 설명을 옮기면 PersonaCopy");
        Expect(validator, Out("greet", "player", "가장 친한 동료처럼 반긴다."), horseTap, OutputProblem.Forbidden, "'~처럼 반긴다'는 설명문 금칙어");
        Expect(validator, Out("greet", "player", "왔구나, 순찰은 내가 했어."), horseTap, OutputProblem.None, "평범한 인사는 통과");
        // 설명문 끝맺음: 동사 현재 평서형(-ㄴ다/-는다)·'-어/-고 있다'가 마침표·말줄임으로 끝나거나 부호 없이 끝나면 거른다. 형용사 '-다'·감탄('!')·'-단다'는 말이라 통과
        ReactionRequest firstGuest = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        firstGuest.Situation = "처음 온 친구가 와 있다.";
        Expect(validator, Out("wander_greet", "player", "와, 처음 온 친구에게 반가운 마음이 담겨 있다."), firstGuest, OutputProblem.Forbidden, "'~담겨 있다.'는 설명문 금칙어");
        foreach (string narrated in new[] { "왔구나 반가운 마음에 헛기침이 나온다.", "개운하게 돌아온 고마워한다", "주인을 따라 장난치듯 버둥거린다.", "네 안부에 반가움을 느끼며 '내 자개는 공들였어'라고 말한다.",
                // 남을 주어로 한 현재형 설명 (CodeRabbit 리뷰)
                "도깨비가 천천히 걷는다.", "친구가 그림을 그리고 있다.", "발굴자가 너를 바라본다",
                // 자리 조사 '에서'의 '서'는 이음 어미가 아니다
                "도깨비가 전시관에서 걷는다.", "친구가 여기서 걷는다." })
        {
            Check(filter.Find(narrated) != null, "설명문 끝맺음은 거름: " + narrated);
        }
        foreach (string spoken in new[] { "왔구나, 반가운 마음이 드네.", "킁킁, 아는 냄새다!", "어서 와! 반갑다.", "드디어 나타났다!", "또 왔구나, 반갑다", "새끼 참새가 날아간다!",
                "오, 내 자개 진짜 멋있다.", "향이 좋은 날엔 이런 전시도 참 예쁘단다.", "여기 있다!", "킁킁, 개운하다. 고마워!",
                // 받침 ㄴ + 다로 넓게 잡으면 걸리던 말 (동사 현재형이라도 도깨비의 행동을 그리는 설명이 아니다)
                "정말 신난다.", "네가 오니 힘이 난다.", "잘 어울린다.", "그럼 나도 간다.", "난 다 알고 있다.", "아직 남아 있다.", "기분이 좋아진다.",
                // 남이 주어라도 '-니·-서'로 이어진 뒤 끝 동사의 주어가 다른 말, 형용사로 맺은 말
                "친구가 오니 힘이 난다.", "친구가 와서 신난다.", "친구는 언제나 반갑다.", "발굴자는 다 알고 있다!",
                // 남이 주어라도 '-단다'는 전하는 말
                "친구가 정말 보고 싶단다." })
        {
            ForbiddenMatch hit = filter.Find(spoken);
            Check(hit == null, "말로 끝나는 '-다'는 통과: " + spoken + (hit != null ? " ← " + hit : ""));
        }
        Expect(validator, Out("wander_greet", "player", "와, 반가워! 편히 구경해."), firstGuest, OutputProblem.None, "같은 장면의 평범한 인사는 통과");
        foreach (Persona persona in personas.personas)
        {
            foreach (string field in new[] { persona.toOwner, persona.toVisitor, persona.onCare, persona.values })
            {
                Check(!string.IsNullOrEmpty(field) && !field.Contains("한다.") && !field.Contains("반긴다"),
                        persona.name + " 태도·가치관 칸은 문장('~한다.') 대신 짧은 말 ← " + field);
            }
        }
        Check(horse.toOwner.Length < 60, "태도 칸이 짧음");
    }

    // ---------- 대사 품질: 오타·끝맺음·끝의 의성어·최근 대사·시간대 (2026-11-13 시험 결과 반영) ----------

    private static void CheckQuality(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 대사 품질: 오타·끝맺음·끝의 의성어·시나리오별 최근 대사 ===");
        var filter = new NpcLineFilter(forbidden);
        var validator = new NpcOutputValidator(filter, personas);

        // 1) 높임을 빼다 생긴 오타 (밤바다 S14: "쏟으셨군요"→"쏟었군", "만드셨는지"→"만먹었는지")
        string[][] honorific =
        {
            new[] { "오늘도 내 자개에 정성을 쏟으셨군요.", "오늘도 내 자개에 정성을 쏟았군." },
            new[] { "어떻게 그렇게 정성 들여 만드셨는지 정말 궁금해요.", "어떻게 그렇게 정성 들여 만들었는지 정말 궁금해." },
            new[] { "이 자개는 누가 만드신 거야?", "이 자개는 누가 만든 거야?" },
            new[] { "방금 밥 드셨어?", "방금 밥 먹었어?" },
            new[] { "책을 읽으셨어요?", "책을 읽었어?" },
            new[] { "손을 닫으셨어?", "손을 닫았어?" },
            new[] { "그걸 받으셨구나.", "그걸 받았구나." }
        };
        foreach (string[] pair in honorific)
        {
            string fixedLine = NpcLineChecks.NormalizeSpeech(pair[0], "반말", "밤바다");
            Check(fixedLine == pair[1], "높임 빼기 오타 없음: " + pair[0] + " → " + fixedLine + " (기대 " + pair[1] + ")");
            Check(NpcLineChecks.Typo(fixedLine) == null, "고친 말에 오타 없음: " + fixedLine + " ← " + NpcLineChecks.Typo(fixedLine));
        }

        // 기본 대사·이야기·예시 대사에는 오타도 끝나지 않은 문장도 없다 (모델이 실패했을 때 대신 나가는 말이다)
        int plain = 0;
        foreach (SpiritLines spirit in lines.spirits.Concat(new[] { lines.fallback }))
        {
            foreach (string line in spirit.pools.SelectMany(pool => pool.lines).Concat(spirit.stories ?? new List<string>()))
            {
                plain++;
                Check(NpcLineChecks.Typo(line) == null && NpcLineChecks.IncompleteEnding(line) == null,
                        spirit.name + " 기본 대사에 오타·끊긴 문장이 없음: " + line + " ← " + (NpcLineChecks.Typo(line) ?? NpcLineChecks.IncompleteEnding(line)));
            }
        }
        foreach (Persona persona in personas.personas)
        {
            foreach (string line in persona.examples.Concat(persona.stories.Select(story => story.fact)))
            {
                plain++;
                Check(NpcLineChecks.Typo(line) == null && NpcLineChecks.IncompleteEnding(line) == null,
                        persona.name + " 예시·실화에 오타·끊긴 문장이 없음: " + line);
            }
        }
        Console.WriteLine("기본·예시·실화 " + plain + "줄 오타 검사");

        // 2) 오타 검사
        string[] typos = { "오늘도 정성을 쏟었군.", "받었어", "내일 갈께.", "금방 할꺼야.", "ㅋㅋ 재밌다", "몇일 전에 왔어.", "어떻해, 흐려졌어.",
                           "웬지 반가워.", "오랫만이야.", "만먹었는지 궁금해.", "공부를를 했어.", "좋았엇어." };
        foreach (string line in typos)
        {
            Check(NpcLineChecks.Typo(line) != null, "오타를 잡음: " + line);
        }
        string[] clean = { "오늘도 정성을 쏟았군.", "받았어? 닫았어.", "먹었어, 읽었어.", "내일 갈게.", "금방 할 거야.", "며칠 전에 왔어.", "어떡해, 흐려졌어.", "왠지 반가워.", "오랜만이야.", "좋았었어.",
                           "고마웠어.", "몰랐지?", "있었어, 없었어.", "했었어.", "가까워졌어.", "불 꺼 줘." };
        foreach (string line in clean)
        {
            Check(NpcLineChecks.Typo(line) == null, "맞는 말은 오타가 아님: " + line + " ← " + NpcLineChecks.Typo(line));
        }
        ReactionRequest night = Request(6, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        night.Situation = "주인이 안부를 물었다.";
        Expect(validator, Out("answer_status", "player", "오늘도 정성을 쏟었어."), night, OutputProblem.Typo, "출력 검사: 오타(쏟었어)는 Typo");
        // 높임이 섞인 모델 출력은 고친 뒤 오타 없이 통과한다 (S14 #3)
        OutputCheck honorificFix = validator.Check(Out("answer_status", "player", "오늘도 내 자개에 정성을 쏟으셨어요."), night);
        Check(honorificFix.IsValid && honorificFix.Line == "오늘은 내 자개에 정성을 쏟았어.", "S14 #3: '쏟으셨어요'는 '쏟았어'로 ← " + honorificFix.Line + " " + honorificFix.Detail);
        OutputCheck makeFix = validator.Check(Out("answer_status", "player", "어떻게 그렇게 정성 들여 만드셨는지 정말 궁금해요."), night);
        Check(makeFix.IsValid && makeFix.Line.Contains("만들었는지"), "S14 #4: '만드셨는지'는 '만들었는지'로 ← " + makeFix.Line + " " + makeFix.Detail);

        // 3) 대사는 문장으로 끝낸다: 끝의 의성어를 떼고, 끊긴 말이면 새로 만든다 (묘작이 S08 #4)
        Check(NpcLineChecks.StripTrailingInterjection("꼬리까지 깨끗해졌어 히힛!") == "꼬리까지 깨끗해졌어."
                && NpcLineChecks.StripTrailingInterjection("신나! 히힛!") == "신나!"
                && NpcLineChecks.StripTrailingInterjection("고마워, 어흠.") == "고마워."
                && NpcLineChecks.StripTrailingInterjection("히힛! 같이 놀자.") == "히힛! 같이 놀자."
                && NpcLineChecks.StripTrailingInterjection("히힛.") == "히힛."
                && NpcLineChecks.StripTrailingInterjection("꼬리까지 깨끗하게 관리해 줘서 히힛!") == "꼬리까지 깨끗하게 관리해 줘서.",
                "끝의 의성어는 뗌, 앞의 의성어와 의성어뿐인 대사는 그대로");
        ReactionRequest care = Request(9, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankJoyful);
        care.Situation = "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
        Expect(validator, Out("thank_joyful", "player", "주인님, 꼬리까지 깨끗하게 관리해 주셔서 히힛!"), care, OutputProblem.Incomplete,
                "S08 #4: '관리해 줘서 히힛!'은 의성어를 떼면 '줘서'로 끊겨 Incomplete");
        OutputCheck joyful = validator.Check(Out("thank_joyful", "player", "꼬리까지 깨끗해졌어, 고마워! 히힛!"), care);
        Check(joyful.IsValid && joyful.Line == "꼬리까지 깨끗해졌어, 고마워!", "문장으로 끝낸 뒤의 의성어는 떼고 통과 ← " + joyful.Line);
        foreach (string line in new[] { "관리해 줘서.", "너랑 놀면서.", "내 꼬리를" })
        {
            Check(NpcLineChecks.IncompleteEnding(line) != null, "끝나지 않은 문장: " + line);
        }
        foreach (string line in new[] { "고마워서 그래.", "그렇다고!", "어서!", "여기서.", "알아챈다니까.", "고마워.", "신나서 폴짝 뛰었어!", "그러니까.", "국보도 됐고." })
        {
            Check(NpcLineChecks.IncompleteEnding(line) == null, "끝난 문장: " + line + " ← " + NpcLineChecks.IncompleteEnding(line));
        }

        // 4) 시간대·'화면' 문장
        Check(filter.Find("화면이 가까워져서 재밌을 것 같아!") != null && filter.Find("화면이 너에게 가까이 다가왔어.") != null, "'화면이 가까워져서'는 거름 (묘작이 S02 #2)");
        Check(filter.Find("안녕? 오늘 아침이라 반가워.") != null && filter.Find("아침이라 반가워.") != null, "'아침이라 반가워'처럼 시간대를 반가운 이유로 말하면 거름 (S01 #1)");
        Check(filter.IsAllowed("아침이라 그런지 오늘따라 더 반가운 것 같아.") && filter.IsAllowed("아침에 찾아줘서 반가워.") && filter.IsAllowed("오늘 밤엔 참새랑 술래잡기 할까?"),
                "자연스러운 시간대 인사는 통과 (S01 #3·#4)");

        // 5) 시나리오별 최근 대사: 같은 도깨비의 같은 선택지끼리만 비교하고, 10개 중 90% 이상 겹치면 새로 만든다
        var backend = new FakeBackend();
        var assembler = new PromptAssembler(personas, actions);
        var slm = new SlmReactionGenerator(backend, assembler, validator);
        slm.LoadAsync(CancellationToken.None).Wait();
        var manager = new NpcRequestManager(slm, new RuleBasedReactionGenerator(lines, new Random(7))) { SlmTimeout = TimeSpan.FromSeconds(5) };
        Check(manager.RecentLineCount == 10 && slm.MaxAttempts == 2, "최근 대사 10개, 만들기 2번이 기본");
        backend.Replies.Enqueue(Out("thank_calm", "player", "관리해 줘서 고마워, 덕분에 더 선명해졌어."));
        NpcResponse first = Ask(manager, Care(6));
        backend.Replies.Enqueue(Out("thank_calm", "player", "관리해 줘서 고마워, 덕분에 더 선명해졌어, 정말."));   // 90% 이상 겹침 → 한 번 더
        backend.Replies.Enqueue(Out("thank_calm", "player", "옻칠이 다시 반짝이네, 고마워."));
        NpcResponse second = Ask(manager, Care(6));
        Check(first.ShouldShow && second.ShouldShow && !second.UsedFallback && second.Reaction.Line == "옻칠이 다시 반짝이네, 고마워." && backend.Calls == 3
                && second.Reaction.Note != null && second.Reaction.Note.Contains("Repeated"),
                "관리 대사가 최근 대사와 90% 이상 겹치면 새로 만들어 다른 대사를 씀 ← " + second.Note + " / " + second.Reaction.Line);
        Check(manager.RecentLinesOf(6, NpcInputs.Care).Count == 2 && manager.RecentLinesOf(6, NpcInputs.Tap).Count == 0 && manager.RecentLinesOf(6).Count == 2,
                "최근 대사는 도깨비 + 선택지(시나리오)마다 따로 기억");
        backend.Replies.Enqueue(Out("greet", "player", "관리해 줘서 고마워, 덕분에 더 선명해졌어."));
        NpcResponse otherScenario = Ask(manager, Request(6, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        Check(otherScenario.ShouldShow && !otherScenario.UsedFallback, "다른 선택지(탭)의 대사끼리는 비교하지 않음 ← " + otherScenario.Note);

        // 6) 3일 전에 온 친구에게 "오랜만이야" (향산이 S06)
        ReactionRequest regular = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
        regular.Situation = "최근 30일 동안 4번 온 친구가 전시관을 둘러보고 있다.";
        regular.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "4"));
        regular.Facts.Add(new Fact(NpcFactKeys.VisitDays, "오늘, 3일 전, 9일 전, 20일 전"));
        OutputCheck absence = validator.Check(Out("wander_recall", "player", "오랜만이야, 친구야."), regular);
        Check(absence.IsValid && absence.Line == "또 왔네, 친구야.", "3일 전에도 온 친구에게 '오랜만'은 거르지 않고 '또 왔네'로 고침 ← " + absence.Line);
        Expect(validator, Out("wander_recall", "player", "3일 전에 오고 또 왔네, 반가워."), regular, OutputProblem.None, "방문 기록(3일 전)을 그대로 말하면 통과");
        ReactionRequest returned = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
        returned.Situation = "최근 30일 동안 2번 온 친구가 전시관을 둘러보고 있다.";
        returned.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "2"));
        returned.Facts.Add(new Fact(NpcFactKeys.VisitDays, "오늘, 12일 전"));
        Expect(validator, Out("wander_recall", "player", "오랜만이야, 친구야."), returned, OutputProblem.None, "12일 만에 온 친구에게 '오랜만'은 통과");

        // 7) 예시 대사를 그대로 옮기면 거른다 (향산이 S12 #1)
        ReactionRequest status = Request(2, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        status.Situation = "주인이 안부를 물었다.";
        Expect(validator, Out("answer_status", "player", "좋은 향은 천천히 퍼지는 게 제일 예쁘지?"), status, OutputProblem.PersonaCopy, "예시 대사를 그대로 옮기면 PersonaCopy");
        Check(!personas.Find(2).toOwner.Contains("손녀"), "향산이 태도 칸에 '손녀'가 없음 (\"손녀분께\"로 옮겼다)");
    }

    private static ReactionRequest Care(int relicId)
    {
        ReactionRequest request = Request(relicId, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankCalm);
        request.Situation = "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
        return request;
    }

    // ---------- 2026-11-13 두 번째 시험 결과 반영: 90% 겹침·설명 지시문·호칭 대명사·닉네임 님·끊긴 말 ----------

    private static void CheckRound3(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 두 번째 시험 반영: 90% 겹침·설명 지시문·호칭·끊긴 말 ===");
        var filter = new NpcLineFilter(forbidden);
        var validator = new NpcOutputValidator(filter, personas);

        // 1) 90% 이상 겹쳐야 같은 대사다 (80%대는 다른 대사)
        Check(NpcLineChecks.SimilarPairs == 0.9 && NpcLineChecks.ContainedShare == 0.9 && NpcLineChecks.SimilarOpening == 0.9, "같은 대사 기준은 90%");
        Check(NpcLineChecks.Similar("정성껏 관리해 줘서 고마워.", "관리해 줘서 고마워.") == null, "82% 겹침(S07 #2)은 다른 대사 (90% 기준)");
        Check(NpcLineChecks.Similar("관리해 줘서 고마워, 덕분에 더 선명해졌어.", "관리해 줘서 고마워, 덕분에 더 선명해졌어, 정말.") != null, "90% 이상 겹치면 같은 대사");
        Check(NpcLineChecks.Similar("이틀 전 관리해 줘서 정말 고마워.", "선명하게 닦아줘서 정말 고마워. 구름 무늬가 더 또렷해진 것 같아.") == null, "일부만 겹치면 다른 대사");
        Check(NpcLineChecks.Similar("고마워, 또 와 줘서 반가워!", "고마워, 또 와 줘서 반가워") != null, "부호만 다르면 같은 대사");

        // 2) 설명 지시문·메타 낱말을 거른다 (실행 결과에서 대사로 나온 것)
        string[] meta =
        {
            "너는 순찰을 돌며 친구에게 인사해.", "너는 전시관을 다니며 반겨 줘.", "안개가 자욱해져서 선명도가 흐려졌어.", "선명도가 흐려졌어, 닦아 줘.",
            "지금 시간대는 아침이야.", "최근 방문한 날이 오늘이야.", "주인이 아침에 찾아와 너를 탭했다.", "오늘은 7번이나 온횟수가 많아."
        };
        foreach (string line in meta)
        {
            Check(filter.Find(line) != null, "설명 지시문·메타 낱말은 거름: " + line);
        }
        foreach (string line in new[] { "너는 정말 다정해.", "너는 어디서 왔어?", "친구야, 또 와 줘서 고마워.", "먼지가 쌓여서 흐려졌어, 닦아 줘.", "순찰은 내가 돌고 있었어." })
        {
            Check(filter.IsAllowed(line), "정상 대사는 통과: " + line + " ← " + filter.Find(line));
        }
        foreach (Persona persona in personas.personas)
        {
            string wrong;
            foreach (string line in new[] { "처음 왔나?", "순찰 잘 다녀왔나?", "밥은 먹었나?", "아무도 없나?" })
            {
                Check(!NpcLineChecks.SpeechMatches(line, persona.endingHint, out wrong), persona.name + " 하게체 '~나?'는 걸림: " + line);
            }
            foreach (string line in new[] { "뭐 하고 있나 봐.", "어디 가나 싶어서 와 봤어.", "나는 괜찮아." })
            {
                Check(NpcLineChecks.SpeechMatches(line, persona.endingHint, out wrong), persona.name + " '~나 봐'는 통과: " + line + " ← " + wrong);
            }
        }

        // 3) 조사가 붙은 '주인'은 지우지 않고 대명사로 (지우면 "보니 반가워!"로 끊긴다)
        string[][] pronouns =
        {
            new[] { "주인을 보니 반가워!", "너를 보니 반가워!" },
            new[] { "주인이 날 관리해 줘서 고마워!", "네가 날 관리해 줘서 고마워!" },
            new[] { "주인한테 자랑했지?", "너한테 자랑했지?" },
            new[] { "주인님, 고마워!", "고마워!" },
            new[] { "고마워, 주인님.", "고마워." },
            new[] { "주인아, 놀자!", "놀자!" },
            new[] { "주인 덕분에 빛나게 됐어.", "주인 덕분에 빛나게 됐어." }
        };
        foreach (string[] pair in pronouns)
        {
            string got = NpcLineChecks.StripAddress(pair[0], null);
            if (pair[1] == pair[0])
            {
                continue; // '주인' 다음에 조사 없이 낱말이 이어지면 호칭이 아니다
            }
            Check(got == pair[1], "주인 호칭 처리: " + pair[0] + " → " + got + " (기대 " + pair[1] + ")");
        }
        Check(NpcLineChecks.NormalizeSpeech("주인을 보니 반가워요!", "반말", "묘작이") == "너를 보니 반가워!", "S02 #4: '주인을 보니 반가워요!' → '너를 보니 반가워!' ← " + NpcLineChecks.NormalizeSpeech("주인을 보니 반가워요!", "반말", "묘작이"));

        // 4) 닉네임에 '님'을 붙이지 않는다 (반말)
        Check(NpcLineChecks.NormalizeSpeech("안녕, 도윤님. 이틀 전 관리 받아서 고마워요.", "반말", "구름결", null, "도윤") == "안녕, 도윤. 이틀 전 관리 받아서 고마워.", "'도윤님'은 '도윤'으로");
        ReactionRequest nick = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        nick.Situation = "주인이 안부를 물었다. 너는 이틀 전에 닦아 관리를 받았다. 고마움을 전한다.";
        nick.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, "2"));
        nick.Nickname = "도윤";
        nick.Intimacy = NpcAddress.NicknameLevel;
        OutputCheck nicked = validator.Check(Out("answer_status", "player", "안녕, 도윤님. 이틀 전 관리 받아서 정말 고마워요."), nick);
        Check(nicked.IsValid && nicked.Line == "이틀 전 관리 받아서 정말 고마워.", "출력 검사가 닉네임의 '님'을 떼고 고마움이 든 한 문장만 남김 ← " + nicked.Line + " " + nicked.Detail);

        // 5) 끝의 의성어를 떼고 남은 말이 조사로 끊겼으면 새로 만든다 (묘작이 S08 #4: "…예뻐 보여서 기분이 히히!")
        Check(NpcLineChecks.StripTrailingInterjection("오늘은 내가 제일 예뻐 보여서 기분이 히히!") == "오늘은 내가 제일 예뻐 보여서 기분이", "조사로 끝나면 마침표를 붙이지 않음");
        ReactionRequest joy = Request(9, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankJoyful);
        joy.Situation = "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
        Expect(validator, Out("thank_joyful", "player", "주인님, 오늘은 내가 제일 예뻐 보여서 기분이 히히!"), joy, OutputProblem.Incomplete, "'기분이 히히!'는 '기분이'로 끊겨 Incomplete");
        Expect(validator, Out("thank_joyful", "player", "오늘은 내가 제일 예뻐 보여서 기분이 좋아! 히히!"), joy, OutputProblem.None, "문장으로 끝낸 뒤의 의성어는 떼고 통과");
        Check(NpcLineChecks.IncompleteEnding("오늘은 기분이") != null && NpcLineChecks.IncompleteEnding("나도 기분이.") == null && NpcLineChecks.IncompleteEnding("누가?") == null,
                "문장 부호 없이 조사로 끝나면 끊긴 말, 부호가 있으면 끝난 말");

        // 6) 탭 상황: 찾아온 주인
        var builder = new NpcContextBuilder(personas);
        NpcContextInput tap = Owner(8, NpcInputs.Tap, "CLEAR", null);
        tap.Now = Today.AddHours(8);
        Check(builder.Build(tap).Request.Situation == "주인이 아침에 너를 보러 찾아왔다.", "탭 상황: 주인이 아침에 찾아와 탭했다 ← " + builder.Build(tap).Request.Situation);
    }

    // ---------- 2026-11-13 세 번째 시험 반영: 금지 없는 프롬프트·응답 시간(읽는 글·쓰는 토큰·재시도) ----------

    private static void CheckLatency(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines,
                                     List<Dictionary<string, object>> scenes, Dictionary<string, string> labels)
    {
        Console.WriteLine("=== 세 번째 시험 반영: 금지 없는 프롬프트·응답 시간 ===");
        var assembler = new PromptAssembler(personas, actions, new PromptOptions { TargetLabels = labels });
        var validator = new NpcOutputValidator(new NpcLineFilter(forbidden), personas);

        // 1) 규칙은 해야 할 것만 — 금지 표현 없음, 짧음
        string[] rules =
        {
            PromptAssembler.RuleBond, PromptAssembler.RuleFacts, PromptAssembler.RuleRole, PromptAssembler.RuleLine, PromptAssembler.RuleSpeech,
            PromptAssembler.RuleOrder, PromptAssembler.RuleVary, PromptAssembler.RuleExamples, PromptAssembler.RuleNoRepeat
        };
        foreach (string rule in rules)
        {
            string negative = new[] { "않", "말고", "말 것", "마라", "금지", "못 ", "넣지" }.FirstOrDefault(w => rule.Contains(w));
            Check(negative == null, "규칙에 금지 표현이 없음: " + rule + " ← " + negative);
        }
        Check(rules.Take(9).Sum(r => r.Length) <= 350, "규칙 전체가 짧음 (읽는 시간을 줄인다) ← " + rules.Take(9).Sum(r => r.Length) + "자");
        foreach (Persona persona in personas.personas)
        {
            foreach (string field in new[] { persona.traits, persona.emotionStyle, persona.speechStyle, persona.toOwner, persona.toVisitor, persona.onCare, persona.values }
                    .Concat(persona.examples))
            {
                string negative = new[] { "않", "말고", "지 말", "금지", "못 " }.FirstOrDefault(w => (field ?? "").Contains(w));
                Check(negative == null, persona.name + " 성격·말투·태도 칸에 금지 표현이 없음: " + field + " ← " + negative);
            }
        }

        // 2) 읽는 글을 줄였다: 시스템 메시지 평균 약 650자, 사용자 메시지 평균 약 260자 (전에는 970자·390자)
        int count = 0;
        int totalSystem = 0;
        int totalUser = 0;
        foreach (Dictionary<string, object> scene in scenes)
        {
            NpcPrompt prompt = assembler.Build(ToRequest(scene));
            if (prompt == null)
            {
                continue;
            }
            count++;
            totalSystem += prompt.System.Length;
            totalUser += prompt.User.Length;
            Check(prompt.User.Length <= 420, scene["id"] + " 사용자 메시지가 짧음 ← " + prompt.User.Length + "자");
            Check(!prompt.User.Contains("말풍선") && !prompt.User.Contains("건드릴 수 없") && !prompt.User.Contains("선명도") && !prompt.User.Contains("가치관"),
                    scene["id"] + " 사용자 메시지에 대사로 옮길 설명 낱말이 없음");
        }
        Check(totalSystem / count <= 700 && totalUser / count <= 300, "평균 시스템 " + totalSystem / count + "자 · 사용자 " + totalUser / count + "자");

        // 3) 쓰는 토큰을 줄였다: 모델이 적을 칸만 문법·스키마에 둔다, 출력 한도 40토큰
        ReactionRequest greet = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        NpcPrompt greetPrompt = assembler.Build(greet);
        Check(greetPrompt.JsonSchema == "{\"type\":\"object\",\"properties\":{\"line\":{\"type\":\"string\"}},\"required\":[\"line\"]}"
                && greetPrompt.User.Contains("할 일: 찾아온 상대에게 반가운 마음을 한 문장으로 말한다") && !greetPrompt.User.Contains("후보:") && !greetPrompt.User.Contains("대상"),
                "행동·대상이 하나뿐이면 스키마는 line 만, 후보 목록 대신 '할 일' 한 줄 ← " + greetPrompt.User);
        ReactionRequest care = Request(6, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankJoyful, NpcActions.ThankCalm);
        NpcPrompt carePrompt = assembler.Build(care);
        Check(carePrompt.JsonSchema.Contains("\"action\"") && !carePrompt.JsonSchema.Contains("\"target\"") && carePrompt.Grammar.Contains("thank_joyful")
                && carePrompt.Grammar.Contains("thank_calm") && !carePrompt.Grammar.Contains("target") && carePrompt.User.Contains("후보:") && !carePrompt.User.Contains("대상"),
                "대상만 하나뿐이면 action+line ← " + carePrompt.Grammar);
        var paired = new ReactionRequest { RequestId = "p", RelicId = 8, Mode = InteractionMode.Owner, Input = NpcInputs.Tap, Situation = "시험" };
        paired.Candidates.Add(new ActionCandidate(NpcActions.Greet, NpcTargets.Player));
        paired.Candidates.Add(new ActionCandidate(NpcActions.AnswerStatus, "building:1"));
        NpcPrompt fullPrompt = assembler.Build(paired);
        Check(fullPrompt.JsonSchema.Contains("\"target\"") && fullPrompt.Grammar.Contains("target") && fullPrompt.User.Contains("대상 ID 뜻"), "대상이 여럿이면 전부 (action·target·line)");

        // 모델이 칸을 비워도 검사기가 후보로 채운다
        Check(validator.Check("{\"line\":\"왔구나, 순찰은 내가 했어.\"}", greet).IsValid && validator.Check("{\"line\":\"왔구나, 순찰은 내가 했어.\"}", greet).ActionId == "greet"
                && validator.Check("{\"action\":\"thank_joyful\",\"line\":\"히힛, 고마워!\"}", care).TargetId == NpcTargets.Player, "빠진 action·target 칸은 후보로 채움");

        // 4) 응답 시간 규칙: 기다리는 시간 3초, 느린 시도는 다시 만들지 않는다
        var backend = new FakeBackend();
        var generator = new SlmReactionGenerator(backend, assembler, validator);
        generator.LoadAsync(CancellationToken.None).Wait();
        Check(new NpcRequestManager(generator, new RuleBasedReactionGenerator(lines, new Random(7))).SlmTimeout == TimeSpan.FromSeconds(3), "SLM 을 기다리는 시간은 3초 (넘으면 기본 대사)");
        Check(generator.MaxAttempts == 2 && generator.RetryOnlyWithin == TimeSpan.FromMilliseconds(1500), "다시 만들기는 최대 2번, 1.5초 안에 끝난 시도만");
        backend.Reply = Out("greet", "player", "이거 어떠세요!");
        backend.Calls = 0;
        Run(generator, Request(9, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        Check(backend.Calls == 2, "빠른 기기: 어미가 틀리면 한 번 더 만든다 (" + backend.Calls + "번)");
        backend.Calls = 0;
        generator.RetryOnlyWithin = TimeSpan.FromMilliseconds(50);
        backend.Delayed = c => Task.Delay(150).ContinueWith(_ => Out("greet", "player", "이거 어떠세요!"));
        ReactionResult slow = Run(generator, Request(9, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        Check(!slow.IsSuccess && backend.Calls == 1 && slow.FailReason == FailReason.InvalidOutput,
                "느린 기기: 한 번 만드는 데 오래 걸렸으면 검사에 걸려도 다시 만들지 않고 곧바로 기본 대사로 (" + backend.Calls + "번)");
        backend.Delayed = null;
        generator.RetryOnlyWithin = TimeSpan.FromMilliseconds(1500);

        // 5) 상황 문장은 짧다: 읽을 글을 줄이고 "순찰을 돌며" 같은 설명을 옮기지 않게
        var builder = new NpcContextBuilder(personas);
        NpcContextInput visitor = Guest(8, NpcInputs.Visit, 7);
        Check(builder.Build(visitor).Request.Situation == "최근 30일 동안 7번 온 친구가 와 있다.", "손님 상황 문장은 한 문장 — 명사 '전시관'을 읽은 모델이 '전시관이 참 포근하네'라고 인상을 말해 뺐다 ← " + builder.Build(visitor).Request.Situation);
        Check(builder.Build(Guest(8, NpcInputs.Visit, 1)).Request.Situation == "처음 온 친구가 와 있다." && !PromptAssembler.RuleBond.Contains("전시관"), "첫 방문 상황 문장도 한 문장, 규칙에도 '전시관' 명사 없음");
        Check(assembler.Build(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 2)).Request).User.Contains("몸 상태: 선명"), "사실 칸의 이름은 '선명도' 대신 '몸 상태'");
    }

    // ---------- 2026-11-13 네 번째 시험 반영: 같은 감사의 반복·'너한테 닦아 줘' ----------

    private static void CheckRound5(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 네 번째 시험 반영: 첫 낱말로 반복 줄이기·호칭 문맥 ===");
        var filter = new NpcLineFilter(forbidden);
        var assembler = new PromptAssembler(personas, actions);

        // 1) 요청마다 다른 첫 낱말: 같은 시나리오를 거듭 불러도 프롬프트가 달라진다 (관리해 줘서 고마워만 되풀이하던 것)
        foreach (Persona persona in personas.personas)
        {
            ReactionRequest care = Request(persona.relicId, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankJoyful, NpcActions.ThankCalm);
            care.Situation = "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
            var starters = new List<string>();
            var prompts = new HashSet<string>();
            for (int variation = 0; variation < 6; variation++)
            {
                care.Variation = variation;
                starters.Add(PromptAssembler.Starter(care));
                prompts.Add(assembler.Build(care).User);
            }
            Check(starters.Distinct().Count() == 6 && prompts.Count == 6, persona.name + " 관리 감사: 여섯 번 부르면 첫 낱말·프롬프트가 모두 다름 ← " + string.Join("/", starters.ToArray()));
            care.Variation = 2;
            string user = assembler.Build(care).User;
            Check(user.Contains(PromptAssembler.StarterLabel + ": " + PromptAssembler.Starter(care) + "\n") || user.Contains(PromptAssembler.StarterLabel + ": " + PromptAssembler.Starter(care) + "\n\n"),
                    persona.name + " 사용자 메시지에 '첫 낱말: …' 줄");
        }
        foreach (string word in new[] { "안녕", "왔구나", "와", "반가워", "기다렸어", "어서", "드디어", "마음껏", "몸이", "많이", "요즘", "점점", "혹시", "이대로", "어느새", "자꾸", "덕분에", "이제", "훨씬", "개운해", "깨끗해졌어", "개운하게", "깨끗하게", "환영해", "편하게", "구경하러", "천천히", "자주", "이번에도", "또", "다시", "오늘도" })
        {
            Check(filter.IsAllowed(word) && NpcLineChecks.Typo(word) == null, "첫 낱말 '" + word + "' 은 금칙어·오타가 아님");
        }
        ReactionRequest recall = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
        ReactionRequest first = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        var recallWords = new HashSet<string>();
        var firstWords = new HashSet<string>();
        for (int v = 0; v < 6; v++)
        {
            recall.Variation = v;
            first.Variation = v;
            recallWords.Add(PromptAssembler.Starter(recall));
            firstWords.Add(PromptAssembler.Starter(first));
        }
        Check(!firstWords.Contains("또") && !firstWords.Contains("자주") && !firstWords.Contains("이번에도"), "첫 방문 손님에게는 '또'·'자주'로 시작하지 않음");
        Check(recallWords.Contains("자주") && !recallWords.Contains("안녕") && recallWords.Count == 6, "단골 손님의 첫 낱말은 알아보는 말 ← " + string.Join("/", recallWords.ToArray()));
        ReactionRequest memory = Request(6, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.RecallTalk);
        Check(PromptAssembler.Starter(memory) == null && !assembler.Build(memory).User.Contains(PromptAssembler.StarterLabel), "지난 대화 꺼내기에는 첫 낱말이 없음 (기억 하나에 집중)");
        Check(PromptAssembler.FlavorHint == "(내 이야기)", "곁들일 거리는 내 이야기 (첫 낱말과 겹치지 않고, 일어난 일로 읽지 않게)");

        // 2) 주인 호칭: 목적어·받는 이가 부탁·관리 동사 앞이면 지우고, 주어는 대명사로
        string[][] address =
        {
            new[] { "흐려진 몸 때문에 마음이 아파서 주인님께 닦아 달라고 부탁드려요.", "흐려진 몸 때문에 마음이 아파서 닦아 줘." },
            new[] { "주인을 닦아 주시면 금방 활기차질 거예요, 주인님.", "닦아 주면 금방 활기차질 거야." },
            new[] { "주인한테 닦아 줄래?", "닦아 줄래?" },
            new[] { "주인을 오래 못 봐서 몸이 흐릿해졌어요.", "너를 오래 못 봐서 몸이 흐릿해졌어." },
            // 2026-11-16: 부탁하는 말의 주어는 "네가"로 바꾸지 않고 지운다 ("네가 닦아 주면"은 너에게 시키는 말처럼 들린다, 시험 S12)
            new[] { "주인이 닦아 주면 선명해질 거예요.", "닦아 주면 선명해질 거야." },
            new[] { "주인한테 자랑했지?", "너한테 자랑했지?" },
            new[] { "주인을 보니 반가워요!", "너를 보니 반가워!" }
        };
        foreach (string[] pair in address)
        {
            string got = NpcLineChecks.NormalizeSpeech(pair[0], "반말", "구름결");
            Check(got == pair[1], "호칭 처리: " + pair[0] + " → " + got + " (기대 " + pair[1] + ")");
        }

        // 3) 탓하는 말·어색한 '오랜만에'를 거른다
        foreach (string line in new[] { "네가 오래 관리를 못 해줘서 몸이 흐려졌어.", "니가 안 닦아 줘서 흐려졌잖아.", "오랜만에 관리를 못 받아서 그런지 몸이 흐려졌어.", "오랜만에 봉인되어 흐려졌어." })
        {
            Check(filter.Find(line) != null, "탓하는 말·어색한 말은 거름: " + line);
        }
        foreach (string line in new[] { "네가 닦아 주면 선명해질 거야.", "오랜만에 닦아 주니 개운해.", "관리를 못 받아서 흐려졌어, 닦아 줘." })
        {
            Check(filter.IsAllowed(line), "정상 대사는 통과: " + line + " ← " + filter.Find(line));
        }

        // 4) 기본 대사가 넉넉하다: 관리 감사는 도깨비마다 다섯 가지 이상 (모델이 거르면 대신 나가는 말이 같은 말만 되풀이하지 않게)
        foreach (SpiritLines spirit in lines.spirits)
        {
            Check(spirit.FindLines(NpcActions.ThankCalm, "").Count >= 5 && spirit.FindLines(NpcActions.ThankJoyful, "").Count >= 5, spirit.name + " 기본 감사 대사 5개 이상");
        }
    }

    // ---------- 2026-11-14 시험 반영: 어색한 시작·끊긴 대사·부탁 빠짐 ----------

    private static void CheckRound6(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 2026-11-14 시험 반영: 어색한 시작·끊긴 대사·부탁 빠짐 ===");
        var filter = new NpcLineFilter(forbidden);
        var validator = new NpcOutputValidator(filter, personas);
        var assembler = new PromptAssembler(personas, actions);

        // 1) 어색한 말
        foreach (string line in new[] { "잘 왔지? 다음엔 꼭 같이 가자!", "같이 가자, 구경 가자.", "함께 가볼래?", "아침부터 반가웠다.", "자랑하고 싶어졌다.", "닦아 줘서 선명해졌다." })
        {
            Check(filter.Find(line) != null, "어색한 말은 거름: " + line);
        }
        foreach (string line in new[] { "발굴자, 어서 와!", "발굴자야, 닦아 줘.", "와, 잡았다!", "킁킁, 왔다 왔다! 놀자!", "다음에 또 보자.", "같이 놀자!", "고마워, 선명해졌어.",
                                        "발굴자가 닦아 줘서 고마워.", "발굴자를 기다렸어.", "발굴자는 정말 다정해." })
        {
            Check(filter.IsAllowed(line), "정상 대사는 통과: " + line + " ← " + filter.Find(line));
        }
        Check(NpcLineChecks.NormalizeSpeech("덕분에, 관리해 줘서 고마워요.", "반말", "밤바다") == "덕분에 관리해 줘서 고마워.", "첫 낱말 뒤의 쉼표는 뗌 ← " + NpcLineChecks.NormalizeSpeech("덕분에, 관리해 줘서 고마워요.", "반말", "밤바다"));
        Check(NpcLineChecks.NormalizeSpeech("오늘은 관리해 줘서 고마워, 선명해진 네 모습이 참 예쁘다.", "반말", "향산이", personas.Find(2).ownThings)
                == "오늘은 관리해 줘서 고마워, 선명해진 내 모습이 참 예쁘다.", "자기 모습을 '네 모습'이라고 하지 않음 (향산이 S09 #2)");
        Check(NpcLineChecks.Typo("오늘은 관리해줘서 고마워, ㄷ선명해진 네 모습이 참 예쁘다") != null, "자음만 남은 'ㄷ선명해진'은 오타로 거름");

        // 2) 첫 낱말은 자연스러운 시작이다 ("잘"·"오늘은"은 뺐다)
        var all = new HashSet<string>();
        foreach (string input in new[] { NpcInputs.Tap, NpcInputs.Care, NpcInputs.Visit, NpcInputs.AskStatus })
        {
            foreach (int variation in Enumerable.Range(0, 6))
            {
                ReactionRequest r = input == NpcInputs.Visit ? Request(8, InteractionMode.Visitor, input, NpcActions.WanderRecall) : Request(8, InteractionMode.Owner, input, NpcActions.Greet);
                if (input == NpcInputs.Care)
                {
                    r = Request(8, InteractionMode.Owner, input, NpcActions.ThankCalm);
                }
                if (input == NpcInputs.AskStatus)
                {
                    r = Request(8, InteractionMode.Owner, input, NpcActions.AnswerStatus);
                    r.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
                }
                r.Variation = variation;
                all.Add(PromptAssembler.Starter(r));
            }
        }
        Check(!all.Contains("잘") && !all.Contains("오늘은"), "첫 낱말에 '잘'·'오늘은'이 없음 ← " + string.Join("/", all.ToArray()));
        Check(PromptAssembler.FlavorHint == "(내 이야기)", "곁들일 거리는 '내 이야기'로 (일어난 일로 읽지 않게)");

        // 3) 흐려진 안부에는 부탁이 있어야 한다 (조금 흐려졌어. 마지막 관리는 9일 전이었어.)
        ReactionRequest faded = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        faded.Situation = "주인이 안부를 물었다. 너는 오래 관리를 받지 못해 흐려졌다. 주인에게 닦아 달라고 부탁한다.";
        faded.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        faded.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, "9"));
        OutputCheck fadedFilled = validator.Check(Out("answer_status", "player", "조금 흐려졌어. 마지막 관리는 9일 전이었어."), faded);
        Check(fadedFilled.IsValid && fadedFilled.Line == "몸이 흐려졌어. " + PromptAssembler.AskFallback(faded), "흐려졌다고만 하고 부탁이 없으면 코드가 부탁을 채움 ← " + fadedFilled.Line + " " + fadedFilled.Detail);
        Expect(validator, Out("answer_status", "player", "살살 닦아 줘."), faded, OutputProblem.None, "부탁이 있으면 통과");
        Expect(validator, Out("answer_status", "player", "요즘 손길이 필요해."), faded, OutputProblem.None, "'손길'도 부탁으로 봄");
        ReactionRequest clear = Request(5, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        clear.Situation = "주인이 안부를 물었다.";
        clear.Facts.Add(new Fact(NpcFactKeys.Clarity, "선명"));
        Expect(validator, Out("answer_status", "player", "나는 잘 지내고 있어."), clear, OutputProblem.None, "선명한 안부는 부탁이 필요 없음");
        // 말줄임 뒤의 '?'는 문장 끝으로 남긴다 — '…'로 합치면 끊긴 대사가 끝난 문장을 잃고 TooLong 이 되었다.
        OutputCheck cutQuestion = validator.Check("{\"line\": \"별일 없었어...? 오늘", clear);
        Check(cutQuestion.IsValid && cutQuestion.Truncated && cutQuestion.Line == "별일 없었어?",
                "끊긴 대사의 '...?'는 '?'로 합쳐 끝난 문장을 남김 ← " + cutQuestion.Line + " " + cutQuestion.Problem + " " + cutQuestion.Detail);
        // 말줄임을 합쳐 드러난 평서 어미의 '?'는 한 번 더 고친다 (CodeRabbit 리뷰). 참 물음('없었어?')은 그대로
        string collapsedStatement = NpcLineChecks.NormalizeSpeech("기분이 좋구나...? 별일 없었어...?", "반말", "말발굽");
        Check(collapsedStatement == "기분이 좋구나. 별일 없었어?", "'좋구나...?'는 '좋구나.'로, '없었어...?'는 '없었어?'로 ← " + collapsedStatement);

        // 4) 토큰 한도에서 끊겨 '}' 만 빠진 JSON 도 읽는다 (묘작이 S08 #4: 문자열은 닫았는데 객체가 안 닫힘)
        ReactionRequest care = Request(9, InteractionMode.Owner, NpcInputs.Care, NpcActions.ThankJoyful);
        care.Situation = "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다.";
        OutputCheck repaired = validator.Check("{\"action\": \"thank_joyful\", \"line\": \"덕분에 몸이 가벼워! 관리해 줘서 고마워!\"", care);
        Check(repaired.IsValid && repaired.Line == "관리해 줘서 고마워!", "'}' 가 빠진 JSON 은 닫아 읽음 ← " + repaired.Detail);
        Expect(validator, "{\"action\": \"thank_joyful\", \"line\": ", care, OutputProblem.NotJson, "값이 없이 끊기면 여전히 NotJson");

        // 5) 문법: 문장 단위로 끝맺는다 — 부호가 오기 전에는 따옴표를 닫지 못하고, 문장은 30자 안에 끝난다.
        //    지난 대화 꺼내기·관리 감사는 문장이 둘까지, 그 밖은 한 문장(끝 부호 전에 글자 5개 이상) — 출력 검사가 버릴 둘째 문장을 만들지 않는다 (2026-11-25)
        ReactionRequest greet = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        string grammar = assembler.Build(greet).Grammar;
        Check(grammar.Contains("line ::= \"\\\"\" one \"\\\"\"") && grammar.Contains("term ::= [.!?~…]") && grammar.Contains("b ::= [^") && !grammar.Contains("char+")
                && !grammar.Contains("sentence ::="), "인사: 한 문장 문법 ← " + grammar);
        string oneRule = grammar.Split('\n').First(l => l.StartsWith("one ::="));
        int coreSlots = oneRule.Split(' ').Count(w => w == "k");
        Check(coreSlots == PromptAssembler.OneSentenceMinCore && oneRule.IndexOf(" k", StringComparison.Ordinal) < oneRule.IndexOf("term", StringComparison.Ordinal)
                && oneRule.Count(c => c == 'b') == PromptAssembler.SentenceMaxChars - PromptAssembler.OneSentenceMinCore && !oneRule.Contains("{"),
                "한 문장 규칙: 끝 부호 전에 글자(k) " + PromptAssembler.OneSentenceMinCore + "개를 먼저 쓰고, 나머지는 " + (PromptAssembler.SentenceMaxChars - PromptAssembler.OneSentenceMinCore)
                + "자까지 펼쳐 둠({m,n} 없이) ← " + oneRule);
        string coreClass = grammar.Split('\n').First(l => l.StartsWith("k ::="));
        Check(new[] { " ", ",", "·", "'", ".", "!", "?", "~", "…" }.All(p => coreClass.Contains(p)) && coreClass.StartsWith("k ::= [^"),
                "한 문장 규칙의 글자(k)는 출력 검사가 세지 않는 부호·띄어쓰기를 뺀 것 (NpcLineChecks.Core 와 같은 기준) ← " + coreClass);
        // 관리 감사·지난 대화 꺼내기는 두 문장까지 (고마움이 든 뒤 문장·기억의 내용이 담긴 뒤 문장을 출력 검사가 남긴다)
        foreach (string twoAction in new[] { NpcActions.ThankCalm, NpcActions.ThankJoyful, NpcActions.RecallTalk })
        {
            string twoGrammar = assembler.Build(Request(8, InteractionMode.Owner, twoAction == NpcActions.RecallTalk ? NpcInputs.AskStatus : NpcInputs.Care, twoAction)).Grammar;
            Check(twoGrammar.Contains("line ::= \"\\\"\" sentence ( \" \" sentence )? \"\\\"\"") && !twoGrammar.Contains("one ::="), twoAction + ": 두 문장까지 쓰는 문법");
            string sentenceRule = twoGrammar.Split('\n').First(l => l.StartsWith("sentence ::="));
            Check(sentenceRule.Count(c => c == 'b') >= PromptAssembler.SentenceMaxChars && !sentenceRule.Contains("{"),
                    twoAction + ": 문장 규칙은 글자 " + PromptAssembler.SentenceMaxChars + "개까지 펼쳐 둠 (반복 범위 {m,n} 을 못 읽는 런타임에서도 돈다)");
        }
        // 흐려진 안부(모델은 부탁 한 문장만 쓴다)·선명한 안부·손님 관람도 한 문장
        foreach (string oneAction in new[] { NpcActions.AnswerStatus, NpcActions.WanderGreet, NpcActions.WanderRecall })
        {
            string oneGrammar = assembler.Build(Request(8, oneAction == NpcActions.AnswerStatus ? InteractionMode.Owner : InteractionMode.Visitor,
                    oneAction == NpcActions.AnswerStatus ? NpcInputs.AskStatus : NpcInputs.Visit, oneAction)).Grammar;
            Check(oneGrammar.Contains("one ::=") && !oneGrammar.Contains("sentence ::="), oneAction + ": 한 문장 문법");
        }
        var plain = new PromptAssembler(personas, actions, new PromptOptions { SentenceGrammar = false });
        Check(plain.Build(greet).Grammar.Contains("char+") && !plain.Build(greet).Grammar.Contains("sentence ::="), "SentenceGrammar=false 면 예전 문법으로 되돌림");
        Check(grammar.Contains("root ::= \"{\" ws \"\\\"line\\\"\""), "행동·대상이 하나뿐이면 root 는 line 만");
    }

    // ---------- 2026-11-14 두 번째 시험 반영: 호칭 '님'·단골 오랜만·미리 만들어 두기 ----------

    private static void CheckPrefetch(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 호칭 '님'·미리 만들어 두기(백그라운드 보관함) ===");
        var filter = new NpcLineFilter(forbidden);
        var validator = new NpcOutputValidator(filter, personas);
        var assembler = new PromptAssembler(personas, actions);
        var rule = new RuleBasedReactionGenerator(lines, new Random(7));

        // 1) 호칭에 '님'을 붙이지 않는다 — 닉네임과 '발굴자'는 거르지 않고 고쳐서 대사에 나오게 한다
        Check(NpcLineChecks.NormalizeSpeech("안녕, 도윤님. 반가워요.", "반말", "말발굽", null, "도윤") == "안녕, 도윤. 반가워.", "'도윤님' → '도윤'");
        Check(NpcLineChecks.NormalizeSpeech("도윤님께 말해 줄게요.", "반말", "말발굽", null, "도윤") == "도윤한테 말해 줄게.", "'도윤님께' → '도윤한테'");
        Check(NpcLineChecks.NormalizeSpeech("발굴자님, 반가워요!", "반말", "말발굽") == "발굴자, 반가워!", "'발굴자님' → '발굴자' (닉네임이 없어도)");
        Check(NpcLineChecks.NormalizeSpeech("친구님, 어서 와요.", "반말", "말발굽") == "친구, 어서 와.", "'친구님' → '친구'");
        ReactionRequest nick = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        nick.Situation = "처음 온 친구가 전시관을 둘러보고 있다.";
        nick.Nickname = "도윤";
        nick.Intimacy = NpcAddress.NicknameLevel;
        OutputCheck nimFixed = validator.Check(Out("wander_greet", "player", "도윤님, 어서 와요!"), nick);
        Check(nimFixed.IsValid && nimFixed.Line == "도윤, 어서 와!", "출력 검사가 '도윤님'을 고쳐 통과시킴 ← " + nimFixed.Line + " " + nimFixed.Detail);
        ReactionRequest stranger = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        stranger.Situation = "처음 온 친구가 전시관을 둘러보고 있다.";
        OutputCheck strangerFixed = validator.Check(Out("wander_greet", "player", "발굴자님, 어서 와요!"), stranger);
        Check(strangerFixed.IsValid && strangerFixed.Line == "발굴자, 어서 와!", "'발굴자님'도 고쳐 통과시킴 ← " + strangerFixed.Line + " " + strangerFixed.Detail);

        // 호칭은 세 번에 한 번 "한 번 불러 준다"고 적는다 (꼭은 아니어도 가끔 나오게)
        ReactionRequest greeting = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        greeting.Nickname = "도윤";
        greeting.Intimacy = NpcAddress.NicknameLevel;
        var calls = new List<bool>();
        for (int v = 0; v < 6; v++)
        {
            greeting.Variation = v;
            string user = assembler.Build(greeting).User;
            Check(user.Contains("지금 상대: 도윤") && !user.Contains("호칭:"), "프롬프트에 호칭이 지금 상대 줄에 한 번 (Variation " + v + ")");
            calls.Add(user.Contains("지금 상대: 도윤(한 번 불러 준다)"));
        }
        Check(calls.SequenceEqual(new[] { true, false, false, true, false, false }), "세 번에 한 번만 '한 번 불러 준다' ← " + string.Join(",", calls.Select(c => c.ToString()).ToArray()));

        // 2) 단골(최근 방문이 7일 안)에게 "오랜만"은 거르지 않고 "또"로 고친다
        Check(NpcLineChecks.FixLongAbsence("오랜만에 와 줘서 고마워.", "오늘, 3일 전, 9일 전") == "또 와 줘서 고마워.", "'오랜만에' → '또' (3일 전에도 왔다)");
        Check(NpcLineChecks.FixLongAbsence("오랜만이야, 친구야.", "오늘, 어제") == "또 왔네, 친구야.", "'오랜만이야' → '또 왔네'");
        Check(NpcLineChecks.FixLongAbsence("또 왔구나! 오랜만에 보니 너무 반가워!", "오늘, 어제") == "또 왔구나! 다시 보니 너무 반가워!"
                && NpcLineChecks.FixLongAbsence("오랜만에 보니 반가워!", "오늘") == "만나니 반가워!",
                "'오랜만에 보니'는 '보니'만 남기지 않고 '다시 보니'(단골)·'만나니'(처음 온 친구)로");
        Check(NpcLineChecks.FixLongAbsence("오랜만에 또 와 줘서 고마워.", "오늘, 2일 전") == "또 와 줘서 고마워.", "'또 또'는 하나로");
        Check(NpcLineChecks.FixLongAbsence("오랜만에 와 줘서 고마워.", "오늘, 12일 전") == "오랜만에 와 줘서 고마워." && NpcLineChecks.FixLongAbsence("오랜만이야.", null) == "오랜만이야.",
                "7일 넘게 비었거나 기록이 없으면 그대로");
        ReactionRequest regular = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
        regular.Situation = "최근 30일 동안 7번 온 친구가 전시관을 둘러보고 있다.";
        regular.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "7"));
        regular.Facts.Add(new Fact(NpcFactKeys.VisitDays, "오늘, 어제, 3일 전, 5일 전"));
        OutputCheck recalled = validator.Check(Out("wander_recall", "player", "오랜만에 와 줘서 고마워, 친구야."), regular);
        Check(recalled.IsValid && recalled.Line == "또 와 줘서 고마워, 친구야.", "단골에게 '오랜만에'는 '또'로 고쳐 통과 ← " + recalled.Line + " " + recalled.Detail);
        ReactionRequest firstVisit = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        firstVisit.Situation = "처음 온 친구가 전시관을 둘러보고 있다.";
        firstVisit.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "1"));
        Expect(validator, Out("wander_greet", "player", "오랜만이야, 어서 와."), firstVisit, OutputProblem.WrongHistory, "첫 방문에 '오랜만'은 여전히 거름");

        // 3) 더 잡은 어색한 말
        foreach (string line in new[] { "내가 닦아줄게, 주인.", "내가 관리해줄게.", "내가 도와줄게." })
        {
            Check(filter.Find(line) != null, "닦이는 쪽이 '닦아줄게'라고 하면 거름: " + line);
        }
        Check(NpcLineChecks.NormalizeSpeech("요즘 몸이 흐려서 주인님께 닦아 달라고 부탁드리고 싶어요.", "반말", "구름결") == "요즘 몸이 흐려서 닦아 줘.", "'닦아 달라고 부탁드리고 싶어요' → '닦아 줘' (\"닦아 줘드리고 싶어\" 가 되지 않게) ← "
                + NpcLineChecks.NormalizeSpeech("요즘 몸이 흐려서 주인님께 닦아 달라고 부탁드리고 싶어요.", "반말", "구름결"));
        Check(filter.Find("또 보는구나, 오늘도 왔네.") == null && NpcLineChecks.NoRecordContradiction("와, 오늘 아침부터 또 보는구나!", "주인이 아침에 찾아와 너를 탭했다.") == "또보는", "기록이 없는데 '또 보는구나'는 어긋남");
        Check(PromptAssembler.FlavorKey(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet)) == null
                && PromptAssembler.FlavorKey(Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet)) == null,
                "탭·손님에는 곁들일 거리를 싣지 않음 (\"반가워서 내 말 그림 줬어\") — 다양성은 첫 낱말이");

        // 4) 미리 만들어 둔 대사: 보관함 (중복·시간·최근 대사)
        var cache = new NpcLineCache();
        DateTime clock = new DateTime(2026, 11, 14, 12, 0, 0);
        cache.Now = () => clock;
        ReactionRequest template = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        template.Situation = "주인이 낮에 찾아와 너를 탭했다.";
        Func<string, ReactionResult> made = text => ReactionResult.Success("c", "greet", "player", text, ReactionSource.Slm);
        Check(cache.Add(template, made("왔구나, 순찰은 내가 했어.")) && cache.Add(template, made("여기 왔어? 별일 없었지?")), "보관함에 넣음");
        Check(!cache.Add(template, made("왔구나 순찰은 내가 했어!")), "이미 있는 대사와 90% 이상 겹치면 넣지 않음");
        Check(cache.Add(template, made("어서 와, 약속은 지켰어.")) && !cache.Add(template, made("전혀 다른 말이야, 안녕.")) && cache.Count(template) == 3, "상황마다 최대 3개");
        var pairs = cache.LinesFor(template);
        bool distinct = true;
        for (int i = 0; i < pairs.Count; i++)
        {
            for (int j = i + 1; j < pairs.Count; j++)
            {
                distinct &= NpcLineChecks.Similar(pairs[i], pairs[j]) == null;
            }
        }
        Check(distinct, "보관한 대사끼리는 모두 90% 아래로 겹침");
        ReactionRequest other = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        other.Situation = "주인이 밤에 찾아와 너를 탭했다.";
        Check(cache.TryTake(other, null) == null && cache.Count(other) == 0, "다른 상황(시간대)의 대사는 꺼내지 않음");
        ReactionRequest sameContext = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        sameContext.Situation = "주인이 낮에 찾아와 너를 탭했다.";
        sameContext.Variation = 9;
        sameContext.Facts.Add(new Fact(NpcFactKeys.Awakening, "각성"));
        Check(NpcLineCache.KeyOf(sameContext) == NpcLineCache.KeyOf(template), "열쇠에는 곁들일 거리 번호·각성 단계가 들어가지 않음");
        ReactionResult taken = cache.TryTake(sameContext, new[] { "왔구나, 순찰은 내가 했어!" });
        Check(taken != null && taken.Line == "여기 왔어? 별일 없었지?" && cache.Count(template) == 1, "최근 대사와 90% 이상 겹치는 것은 건너뛰고 버림 ← " + (taken != null ? taken.Line : "null"));
        clock = clock.AddMinutes(31);
        Check(cache.Count(template) == 0 && cache.TotalCount == 0, "30분이 지난 대사는 버림 (시간대가 바뀔 수 있다)");

        // 5) 요청 관리자: 미리 만들어 두고 바로 꺼내 쓴다
        var backend = new FakeBackend();
        var slm = new SlmReactionGenerator(backend, assembler, validator);
        slm.LoadAsync(CancellationToken.None).Wait();
        var manager = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        Func<ReactionRequest> tapRequest = () =>
        {
            ReactionRequest r = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
            r.Situation = "주인이 낮에 찾아와 너를 탭했다.";
            return r;
        };
        backend.Replies.Enqueue(Out("greet", "player", "왔구나, 순찰은 내가 했어."));
        backend.Replies.Enqueue(Out("greet", "player", "왔구나, 순찰은 내가 했어!"));   // 겹침 → 다시 만든다
        backend.Replies.Enqueue(Out("greet", "player", "여기 왔어? 별일 없었지?"));
        backend.Replies.Enqueue(Out("greet", "player", "어서 와, 약속은 지켰어."));
        manager.Prefetch(tapRequest(), 3);
        Check(manager.PrefetchCompletion.Wait(5000), "미리 만들기가 끝남");
        Check(manager.Cache.Count(tapRequest()) == 3, "대사 3개를 미리 만들어 둠 ← " + manager.Cache.Count(tapRequest()));
        List<string> stocked = manager.Cache.LinesFor(tapRequest());
        bool stockedDistinct = true;
        for (int i = 0; i < stocked.Count; i++)
        {
            for (int j = i + 1; j < stocked.Count; j++)
            {
                stockedDistinct &= NpcLineChecks.Similar(stocked[i], stocked[j]) == null;
            }
        }
        Check(stockedDistinct, "미리 만든 대사끼리 90% 아래로 겹침 (겹치는 대사는 다시 만듦) ← " + string.Join(" / ", stocked.ToArray()));
        int callsAfterPrefetch = backend.Calls;
        NpcResponse hit = Ask(manager, tapRequest());
        Check(hit.ShouldShow && hit.Note == NpcRequestManager.PrefetchedNote && !hit.UsedFallback && hit.Reaction.Source == ReactionSource.Slm && backend.Calls == callsAfterPrefetch
                && hit.Reaction.Line == stocked[0] && hit.Reaction.ActionId == "greet" && hit.Reaction.TargetId == NpcTargets.Player,
                "요청이 오면 모델을 부르지 않고 바로 꺼내 씀 ← " + hit.Note);
        Check(manager.RecentLinesOf(8, NpcInputs.Tap).Contains(stocked[0]) && manager.Cache.Count(tapRequest()) == 2, "꺼내 쓴 대사는 최근 대사로 남고 보관함에서 빠짐");
        Ask(manager, tapRequest());
        Ask(manager, tapRequest());
        Check(manager.Cache.Count(tapRequest()) == 0 && backend.Calls == callsAfterPrefetch, "보관한 대사를 다 쓸 때까지 모델을 부르지 않음");
        backend.Reply = Out("greet", "player", "오늘도 순찰 잘했지?");
        NpcResponse miss = Ask(manager, tapRequest());
        Check(miss.ShouldShow && miss.Note != NpcRequestManager.PrefetchedNote && backend.Calls == callsAfterPrefetch + 1, "보관함이 비면 지금처럼 그 자리에서 만듦 ← " + miss.Note);
        manager.UsePrefetched = false;
        manager.Cache.Add(tapRequest(), made("안녕, 오늘도 반가워!"));
        backend.Reply = Out("greet", "player", "어서 와, 오늘은 조용했어.");
        Check(Ask(manager, tapRequest()).Note != NpcRequestManager.PrefetchedNote, "UsePrefetched=false 면 보관함을 쓰지 않음");
        manager.UsePrefetched = true;

        // 6) 요청이 오면 미리 만들기를 멈추고, 요청이 끝난 뒤 이어 만든다
        var slowBackend = new FakeBackend();
        var slowSlm = new SlmReactionGenerator(slowBackend, assembler, validator);
        slowSlm.LoadAsync(CancellationToken.None).Wait();
        var slowManager = new NpcRequestManager(slowSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        int started = 0;
        slowBackend.Delayed = async c =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                await Task.Delay(8000, c);   // 미리 만들던 첫 생성: 요청이 오면 취소된다
            }
            return Out("greet", "player", started <= 2 ? "왔구나, 순찰은 내가 했어." : "어서 와, 약속은 지켰어.");
        };
        slowManager.Prefetch(tapRequest(), 1);
        for (int i = 0; i < 100 && Volatile.Read(ref started) < 1; i++)
        {
            Thread.Sleep(10);
        }
        Check(Volatile.Read(ref started) == 1, "미리 만들기가 시작됨");
        var watch = Stopwatch.StartNew();
        ReactionRequest real = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        real.Situation = "주인이 안부를 물었다.";
        // 요청 처리(첫 호출)와 이어 만드는 미리 만들기(그 뒤 호출)가 같은 백엔드를 쓰므로, 답을 호출 순서로 정한다 (일꾼이 답을 바꾸기 전에 재개해도 흔들리지 않게)
        int afterCancel = 0;
        slowBackend.Delayed = c => Task.FromResult(Interlocked.Increment(ref afterCancel) == 1
                ? Out("answer_status", "player", "나는 잘 지내.")
                : Out("greet", "player", "어서 와, 약속은 지켰어."));
        NpcResponse during = Ask(slowManager, real);
        Check(during.ShouldShow && watch.ElapsedMilliseconds < 3000, "미리 만들던 중에 요청이 오면 그 생성을 취소하고 요청을 먼저 처리 (" + watch.ElapsedMilliseconds + "ms) ← " + during.Note);
        Check(slowManager.PrefetchCompletion.Wait(5000) && slowManager.Cache.Count(tapRequest()) == 1, "요청이 끝나면 멈췄던 미리 만들기를 이어서 끝냄");

        // 6b) CancelPrefetch 로 줄에서 빠진 작업은 취소된 뒤 더 만들지 않는다 (화면을 나가도 백그라운드 모델 일이 이어지지 않게)
        var cancelBackend = new FakeBackend();
        var cancelSlm = new SlmReactionGenerator(cancelBackend, assembler, validator);
        cancelSlm.LoadAsync(CancellationToken.None).Wait();
        var cancelManager = new NpcRequestManager(cancelSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5) };
        int cancelStarted = 0;
        cancelBackend.Delayed = async c =>
        {
            Interlocked.Increment(ref cancelStarted);
            await Task.Delay(8000, c);
            return Out("greet", "player", "어서 와, 약속은 지켰어.");
        };
        cancelManager.Prefetch(tapRequest(), 2);
        for (int i = 0; i < 100 && Volatile.Read(ref cancelStarted) < 1; i++)
        {
            Thread.Sleep(10);
        }
        cancelManager.CancelPrefetch();
        Check(cancelManager.PrefetchCompletion.Wait(3000), "CancelPrefetch 뒤 미리 만들기 일꾼이 끝남");
        Thread.Sleep(300);
        Check(Volatile.Read(ref cancelStarted) == 1, "CancelPrefetch 로 줄에서 빠진 작업은 더 만들지 않음 (생성 " + Volatile.Read(ref cancelStarted) + "번)");

        // 7) 미리 만들지 않는 경우
        int before = backend.Calls;
        manager.Prefetch(Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory), 2);
        manager.Prefetch(Request(8, InteractionMode.Owner, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay), 2);
        manager.Prefetch(null, 2);
        manager.Prefetch(tapRequest(), 0);
        manager.PrefetchCompletion.Wait(1000);
        Check(backend.Calls == before, "이야기·끌려갈 때·빈 요청·0개는 미리 만들지 않음");
        manager.SlmEnabled = false;
        manager.Prefetch(tapRequest(), 2);
        manager.PrefetchCompletion.Wait(1000);
        Check(backend.Calls == before, "SLM 을 끈 기기는 미리 만들지 않음");
        manager.SlmEnabled = true;

        // 8) 컨텍스트 구성기가 미리 만들 요청을 만든다
        var builder = new NpcContextBuilder(personas);
        NpcContextInput state = Owner(8, NpcInputs.Tap, "FADED", 9);
        state.Now = Today.AddHours(14);
        state.Nickname = "도윤";
        List<ReactionRequest> plan = builder.PrefetchRequests(state);
        Check(plan.Select(r => r.Input).SequenceEqual(new[] { NpcInputs.Tap, NpcInputs.AskStatus, NpcInputs.Care }) && plan.All(r => !NpcActions.SkipsModel(r.Candidates)),
                "주인 장면: 탭 인사·안부·관리 감사를 미리 만들 요청으로 ← " + string.Join(",", plan.Select(r => r.Input).ToArray()));
        Check(plan[0].Situation == "주인이 낮에 너를 보러 찾아왔다." && plan[1].FindFact(NpcFactKeys.Clarity) == "흐려짐" && plan[2].FindFact(NpcFactKeys.CareResult) == "흐려짐→선명",
                "지금 상태(시간대·선명도)가 요청에 들어 있음");
        Check(builder.PrefetchRequests(Guest(8, NpcInputs.Visit, 4)).Count == 1 && builder.PrefetchRequests(Guest(8, NpcInputs.Visit, 4))[0].Input == NpcInputs.Visit, "손님 장면: 손님 관람 하나");
        // 미리 만든 요청과 나중에 만든 요청의 열쇠가 같아야 꺼내 쓴다
        NpcContextInput tapNow = Owner(8, NpcInputs.Tap, "FADED", 9);
        tapNow.Now = Today.AddHours(14);
        tapNow.Nickname = "도윤";
        Check(NpcLineCache.KeyOf(plan[0]) == NpcLineCache.KeyOf(builder.Build(tapNow).Request), "빌더가 같은 상태로 만든 요청은 미리 만든 요청과 같은 열쇠");
        backend.Reply = Out("greet", "player", "반가워! 내 갓은 오늘도 반듯해.");   // 앞 시험에서 이미 띄운 대사와 겹치지 않는 말
        manager.Cache.Clear();
        manager.Prefetch(plan[0], 1);
        Check(manager.PrefetchCompletion.Wait(5000), "빌더가 만든 요청으로 미리 만듦");
        NpcResponse viaBuilder = Ask(manager, builder.Build(tapNow).Request);
        Check(viaBuilder.Note == NpcRequestManager.PrefetchedNote, "게임이 요청을 새로 만들어도 미리 만든 대사를 꺼내 씀 ← " + viaBuilder.Note);

        CheckSlots(personas, validator, assembler, rule, builder);
        CheckLineStore(validator, assembler, rule, builder);
    }

    // 미리 만든 대사 파일: 날 수 칸, 상태별 열쇠, 파일에 쓰고 읽기, 꺼낼 때 다시 검사, 쓴 만큼 다시 만들기
    private static void CheckLineStore(NpcOutputValidator validator, PromptAssembler assembler, RuleBasedReactionGenerator rule, NpcContextBuilder builder)
    {
        Console.WriteLine("=== 미리 만든 대사 파일(앱을 다시 켜도 쓰기)·쓴 만큼 다시 만들기 ===");

        // 1) 날 수 칸: 견본의 마지막 관리 날 수와 같은 숫자 + '일'만 칸으로
        var nineDays = new SlotValues { CareDays = 9 };
        Check(NpcLineSlots.Slotify("9일 만에 닦아 줘서 고마워!", nineDays) == "{날수}일 만에 닦아 줘서 고마워!"
                && NpcLineSlots.Slotify("마지막 관리가 9일 전이었어. 닦아 줄래?", nineDays) == "마지막 관리가 {날수}일 전이었어. 닦아 줄래?",
                "마지막 관리 날 수(견본 9일)는 {날수} 칸으로");
        Check(NpcLineSlots.Fill("{날수}일 만이야, 고마워!", new SlotValues { CareDays = 12 }) == "12일 만이야, 고마워!"
                && NpcLineSlots.Fill("{날수}일 만이야!", new SlotValues()) == null && NpcLineSlots.SlotNames("{날수}일") == "날수",
                "날 수 칸에 실제 날 수를 넣고, 넣을 값이 없으면 못 씀");
        Check(NpcLineSlots.Slotify("3일 전에도 왔지?", nineDays) == null && NpcLineSlots.Slotify("일주일 만이야!", nineDays) == null
                && NpcLineSlots.Slotify("사흘 만이야!", nineDays) == null && NpcLineSlots.Slotify("9일 만이야!", new SlotValues()) == null
                && NpcLineSlots.Slotify("오랜만이야, 고마워!", nineDays) == "오랜만이야, 고마워!"
                // 만남의 간격은 관리 날 수가 아니다 (CodeRabbit 리뷰)
                && NpcLineSlots.Slotify("9일 만에 다시 만나서 기뻐!", nineDays) == null && NpcLineSlots.Slotify("9일 만이야, 고마워!", nineDays) == null,
                "견본과 다른 날 수·'일주일'·한글 날 수·날 수 사실이 없는 장면의 날 수는 보관하지 않고, 날 수가 없는 말은 그대로");

        // 2) 열쇠: 정화는 상태(관리 결과)마다 하나이고 날 수는 넣지 않는다 — 이번 정화 때 만들어 두면 다음 정화(날 수 모름)가 같은 열쇠로 찾는다
        ReactionRequest faded9 = builder.Build(Owner(8, NpcInputs.Care, "FADED", 9)).Request;
        ReactionRequest faded12 = builder.Build(Owner(8, NpcInputs.Care, "FADED", 12)).Request;
        ReactionRequest resealed16 = builder.Build(Owner(8, NpcInputs.Care, "RESEALED", 16)).Request;
        ReactionRequest clear2 = builder.Build(Owner(8, NpcInputs.Care, "CLEAR", 2)).Request;
        Check(NpcLineCache.TemplateKeyOf(faded9) == NpcLineCache.TemplateKeyOf(faded12) && NpcLineCache.KeyOf(faded9) != NpcLineCache.KeyOf(faded12),
                "흐려진 뒤 정화: 9일·12일이어도 칸 있는 대사의 열쇠는 같음 (서버 값에 달린 기존 열쇠는 다름)");
        Check(NpcLineCache.TemplateKeyOf(faded9) != NpcLineCache.TemplateKeyOf(resealed16) && NpcLineCache.TemplateKeyOf(faded9) != NpcLineCache.TemplateKeyOf(clear2),
                "선명·흐려짐·재봉인은 열쇠가 다름 (무슨 말을 할지가 다르다)");
        List<ReactionRequest> careTemplates = builder.PrefetchTemplates(Owner(8, NpcInputs.Tap, "CLEAR", 2)).Where(r => r.Input == NpcInputs.Care).ToList();
        string careResults = string.Join(",", careTemplates.Select(r => r.FindFact(NpcFactKeys.CareResult)).ToArray());
        Check(careResults == "선명→선명,흐려짐→선명,재봉인→선명", "정화 견본은 다음 정화 때의 상태를 몰라 세 상태 모두 ← " + careResults);
        Check(careTemplates.Select(r => NpcLineCache.TemplateKeyOf(r)).SequenceEqual(new[] { clear2, faded12, resealed16 }.Select(r => NpcLineCache.TemplateKeyOf(r))),
                "정화 견본의 열쇠가 다음 정화(2일·12일·16일 뒤) 요청의 열쇠와 같음");

        // 3) 파일 모양: 칸 있는 대사와 최근 대사를 쓰고 되읽는다. 버전이 다르면 칸 있는 대사는 버린다
        string careAction = faded9.Candidates[0].ActionId;
        var cache = new NpcLineCache();
        Check(cache.AddTemplate(faded9, ReactionResult.Success("r", careAction, NpcTargets.Player, "9일 만에 닦아 줘서\t고마워! \\o/", ReactionSource.Slm, "메모")), "보관함에 넣음");
        var recent = new Dictionary<string, List<string>> { { "8:care", new List<string> { "개운해, 고마워!", "줄\n바꿈" } } };
        string text = NpcLineStoreFormat.Write("v1", cache.ExportTemplates(), recent);
        List<NpcLineCache.StoredTemplate> read;
        Dictionary<string, List<string>> readRecent;
        bool matched;
        Check(NpcLineStoreFormat.TryRead(text, "v1", out read, out readRecent, out matched) && matched && read.Count == 1
                && read[0].Result.Line == "{날수}일 만에 닦아 줘서\t고마워! \\o/" && read[0].Result.Note == "메모" && read[0].Result.ActionId == careAction
                && read[0].Key == NpcLineCache.TemplateKeyOf(faded9) && readRecent["8:care"].SequenceEqual(new[] { "개운해, 고마워!", "줄\n바꿈" }),
                "칸 있는 대사·최근 대사를 글로 쓰고 그대로 되읽음 (탭·줄바꿈·역슬래시 포함)");
        Check(new NpcLineCache().ImportTemplates(read) == 1 && new NpcLineCache { Now = () => DateTime.UtcNow.AddDays(31) }.ImportTemplates(read) == 0,
                "읽은 대사를 보관함에 넣음, 30일(TemplateMaxAge)이 지난 것은 넣지 않음");
        Check(NpcLineStoreFormat.TryRead(text, "v2", out read, out readRecent, out matched) && !matched && read.Count == 0 && readRecent.Count == 1,
                "버전이 다르면 칸 있는 대사는 버리고 최근 대사만 되살림");
        Check(!NpcLineStoreFormat.TryRead("엉뚱한 글", "v1", out read, out readRecent, out matched) && !NpcLineStoreFormat.TryRead(null, "v1", out read, out readRecent, out matched),
                "머리 줄이 없는 글은 읽지 않음");
        Check(NpcLineStoreFormat.VersionOf("personas-3", null, "forbidden-29") == NpcLineStoreFormat.VersionOf("personas-3", "", "forbidden-29")
                && NpcLineStoreFormat.VersionOf("a+b", "c") != NpcLineStoreFormat.VersionOf("a", "b+c") && NpcLineStoreFormat.VersionOf("a", "b") == NpcLineStoreFormat.VersionOf("a", "b"),
                "버전 글: 같은 값이면 같고, 칸의 경계가 다르면 다름 (CodeRabbit 리뷰)");

        // 4) 파일 저장소: 임시 파일에 다 쓴 뒤 바꿔 넣는다. 바꿔 넣는 사이에 꺼져 원래 파일이 없으면 임시 파일을 읽는다
        string dir = Path.Combine(Path.GetTempPath(), "jamkkaebi_verify_" + Guid.NewGuid().ToString("N"));
        var fileStore = new NpcLineFileStore(Path.Combine(dir, "npc_lines.txt"));
        Check(fileStore.Read() == null, "파일이 없으면 null");
        fileStore.Write("하나");
        fileStore.Write("둘");
        Check(fileStore.Read() == "둘" && !File.Exists(fileStore.Path + ".tmp"), "덮어쓰고 임시 파일은 남지 않음 (폴더가 없으면 만든다)");
        File.Delete(fileStore.Path);
        File.WriteAllText(fileStore.Path + ".tmp", "셋", new UTF8Encoding(false));
        Check(fileStore.Read() == "셋", "원래 파일이 없으면 임시 파일을 읽음");
        // 임시 파일이 유일한 저장본일 때 다음 쓰기는 그것을 먼저 원래 파일로 올린 뒤 쓴다 (덮어쓰다 실패해도 잃지 않게, CodeRabbit 리뷰)
        fileStore.Write("넷");
        Check(fileStore.Read() == "넷" && File.Exists(fileStore.Path) && !File.Exists(fileStore.Path + ".tmp"), "임시 파일만 남은 뒤에도 새로 쓰고 원래 파일로 바꿔 넣음");
        Directory.Delete(dir, true);

        // 5) 요청 관리자: 한 판에서 만든 대사를 남기고, 다음 판(새 관리자)이 읽어 모델 없이 바로 쓴다
        var memory = new MemoryLineStore();
        var backend = new FakeBackend();
        var slm = new SlmReactionGenerator(backend, assembler, validator);
        slm.LoadAsync(CancellationToken.None).Wait();
        var first = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromSeconds(5) };
        Check(!first.RefillAfterUse && first.LoadStore(memory, "v1") == 0, "쓴 만큼 다시 만들기는 기본으로 꺼져 있고, 처음에는 파일이 비어 0개 읽음");
        backend.Replies.Enqueue(Out(careAction, "player", "오랜만에 닦아 줘서 개운해, 고마워!"));
        first.PrefetchTemplate(faded9, 1);
        Check(first.PrefetchCompletion.Wait(5000) && first.Cache.TemplateCount(faded9) == 1, "흐려진 뒤 정화 대사를 미리 만듦 ← " + first.Cache.TemplateCount(faded9));
        Check(first.SaveStore() && memory.Text != null && memory.Text.StartsWith(NpcLineStoreFormat.Header, StringComparison.Ordinal), "저장소에 씀");
        var backend2 = new FakeBackend { Reply = Out(careAction, "player", "개운해, 정말 고마워!") };
        var slm2 = new SlmReactionGenerator(backend2, assembler, validator);
        slm2.LoadAsync(CancellationToken.None).Wait();
        var second = new NpcRequestManager(slm2, rule) { SlmTimeout = TimeSpan.FromSeconds(5) };
        Check(second.LoadStore(memory, "v1") == 1, "다음 판이 파일에서 1개 읽음");
        NpcResponse next = Ask(second, faded12);
        Check(next.Note == NpcRequestManager.PrefetchedNote && backend2.Calls == 0 && next.Reaction.Line == "오랜만에 닦아 줘서 개운해, 고마워!",
                "다음 판의 정화(12일 뒤)가 모델 없이 파일의 대사를 바로 씀 ← " + next.Note + " / " + (next.Reaction != null ? next.Reaction.Line : null));
        Check(new NpcRequestManager(slm2, rule).LoadStore(new MemoryLineStore(memory.Text), "v2") == 0, "버전(콘텐츠·모델)이 바뀌면 파일의 대사를 쓰지 않음");

        // 6) 꺼낼 때 지금 규칙으로 다시 검사: 규칙이 바뀌기 전에 남긴 대사(지금은 금칙어 '화면')는 쓰지 않고 버린다
        var stale = new NpcRequestManager(slm2, rule) { SlmTimeout = TimeSpan.FromSeconds(5) };
        stale.Cache.AddTemplate(faded9, ReactionResult.Success("old", careAction, NpcTargets.Player, "화면 너머로 닦아 줘서 고마워!", ReactionSource.Slm, null));
        int callsBefore = backend2.Calls;
        NpcResponse rechecked = Ask(stale, faded12);
        Check(rechecked.Note != NpcRequestManager.PrefetchedNote && backend2.Calls == callsBefore + 1 && stale.Cache.TemplateCount(faded12) == 0,
                "지금 규칙에 걸리는 남은 대사는 꺼내지 않고 버린 뒤 새로 만듦 ← " + rechecked.Note);
        // 흐려진 안부: 코드가 붙인 앞 문장("마지막 관리가 9일 전이었어.")도 날 수 칸으로 남고, 다시 검사해도 앞 문장이 두 번 붙지 않는다
        ReactionRequest askFaded9 = builder.Build(Owner(8, NpcInputs.AskStatus, "FADED", 9)).Request;
        ReactionRequest askFaded12 = builder.Build(Owner(8, NpcInputs.AskStatus, "FADED", 12)).Request;
        var askCache = new NpcLineCache { Recheck = slm2.Recheck };
        Check(askCache.AddTemplate(askFaded9, ReactionResult.Success("a", NpcActions.AnswerStatus, NpcTargets.Player, "마지막 관리가 9일 전이었어. 시간 되면 닦아 줄래?",
                ReactionSource.Slm, null)), "흐려진 안부(앞 문장에 9일)도 날 수 칸으로 보관");
        ReactionResult askTaken = askCache.TryTakeTemplate(askFaded12, null);
        string askLine = askTaken != null ? askTaken.Line : "(없음)";
        // 다시 검사하면 검사기가 이 요청의 앞 문장(LinePrefix: "마지막 관리가 12일 전이었어." 또는 "몸이 많이 흐려졌어." 따위)을 새로 붙인다
        List<string> askPrefixes = PromptAssembler.AskPrefixes(askFaded12);
        Check(askTaken != null && !askLine.Contains("9일") && PromptAssembler.StripAskPrefix(askLine, askPrefixes) == "시간 되면 닦아 줄래?" && askLine != "시간 되면 닦아 줄래?"
                && askPrefixes.Count(prefix => askLine.Contains(prefix)) == 1,
                "12일 뒤 안부: 9일이 남지 않고, 다시 검사해도 앞 문장이 하나만 붙음 ← " + askLine);
        Check(NpcLineSlots.Fill("마지막 관리가 {날수}일 전이었어. 시간 되면 닦아 줄래?", SlotValues.FromRequest(askFaded12)) == "마지막 관리가 12일 전이었어. 시간 되면 닦아 줄래?",
                "앞 문장의 날 수 칸은 요청의 마지막 관리 날 수(12일)로 채움");

        // 7) 쓴 만큼 다시 만들기: 정화 대사 1개를 쓰면 뒤에서 1개만, 탭 인사는 2개까지 채운다. 보관함이 비어 그 자리에서 만들었어도 다음 번 것을 채운다
        var refillBackend = new FakeBackend();
        var refillSlm = new SlmReactionGenerator(refillBackend, assembler, validator);
        refillSlm.LoadAsync(CancellationToken.None).Wait();
        var refill = new NpcRequestManager(refillSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true, PrefetchIdleDelay = TimeSpan.Zero };
        refillBackend.Replies.Enqueue(Out(careAction, "player", "오랜만에 닦아 줘서 개운해, 고마워!"));
        refill.PrefetchTemplate(faded9, 1);
        Check(refill.PrefetchCompletion.Wait(5000) && refill.Cache.TemplateCount(faded9) == 1, "정화 대사 1개를 미리 만듦");
        refillBackend.Replies.Enqueue(Out(careAction, "player", "드디어 몸이 맑아졌어, 고마워!"));
        int beforeUse = refillBackend.Calls;
        NpcResponse used = Ask(refill, faded12);
        // 모델 호출 수는 여기서 보지 않는다 — 요청이 끝나면 다시 만들기가 곧바로 모델을 부를 수 있다 (CodeRabbit 리뷰). 아래에서 다시 만들기가 끝난 뒤 1번인지 본다
        Check(used.Note == NpcRequestManager.PrefetchedNote, "정화 때는 만들어 둔 대사를 바로 씀");
        Check(refill.PrefetchCompletion.Wait(5000) && refill.Cache.TemplateCount(faded12) == 1 && refillBackend.Calls == beforeUse + 1,
                "쓴 1개만큼 뒤에서 1개를 다시 만듦 ← 보관 " + refill.Cache.TemplateCount(faded12) + "개, 모델 " + (refillBackend.Calls - beforeUse) + "번");
        NpcResponse nextCare = Ask(refill, faded9);
        Check(nextCare.Note == NpcRequestManager.PrefetchedNote && nextCare.Reaction.Line == "드디어 몸이 맑아졌어, 고마워!", "다음 정화는 뒤에서 만든 대사를 씀 ← " + nextCare.Reaction.Line);
        refill.PrefetchCompletion.Wait(5000);
        string clearAction = clear2.Candidates[0].ActionId;
        refillBackend.Replies.Enqueue(Out(clearAction, "player", "개운해, 고마워!"));
        refillBackend.Replies.Enqueue(Out(clearAction, "player", "몸이 가벼워, 고마워!"));
        refillBackend.Replies.Enqueue(Out(clearAction, "player", "무늬가 또렷해졌어, 고마워!"));
        int beforeLive = refillBackend.Calls;
        NpcResponse live = Ask(refill, clear2);
        // 빗나간 상황은 채운 것보다 빨리 쓰인다 — 채울 수(정화 1)보다 하나 더 (2026-11-23 적중 개선)
        Check(live.Note != NpcRequestManager.PrefetchedNote && refill.PrefetchCompletion.Wait(5000) && refill.Cache.TemplateCount(clear2) == 2
                && refillBackend.Calls == beforeLive + 3, "보관함이 비어 그 자리에서 만들었으면 다음 정화 것을 하나 더(2개) 만들어 둠 ← " + refill.Cache.TemplateCount(clear2)
                + "개, 모델 " + (refillBackend.Calls - beforeLive) + "번");
        Check(refill.RefillTarget(Tap(8)) == 2 && refill.RefillTarget(Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall)) == 2 && refill.RefillTarget(faded9) == 1,
                "채울 수: 탭 인사·손님 관람 2 (여러 친구가 잇달아 온다), 정화·안부 1");
        Check(refill.Store == null && !refill.SaveStore(), "저장소를 붙이지 않았으면 쓰지 않음");

        // 리뷰 반영 (2026-11-18)
        // ① 읽는 사이에 저장이 끼어도 파일을 빈 보관함으로 덮어쓰지 않는다 — 다 읽고 넣은 뒤에 저장소를 붙인다
        var racing = new NpcRequestManager(slm2, rule);
        var racingStore = new MemoryLineStore(memory.Text);
        string beforeLoad = racingStore.Text;
        racingStore.OnRead = () => Check(!racing.SaveStore(), "읽는 사이에는 저장소가 아직 붙지 않아 저장하지 않음");
        Check(racing.LoadStore(racingStore, "v1") >= 0 && racingStore.Text == beforeLoad && racing.Store == racingStore, "읽는 사이의 저장이 파일을 덮어쓰지 않고, 다 읽은 뒤 저장소가 붙음");
        // ② 미리 만든 대사를 쓰지 않으면(UsePrefetched=false) 쓴 만큼 다시 만들지도 않는다
        var noUse = new NpcRequestManager(refillSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true, UsePrefetched = false };
        refillBackend.Reply = Out(clearAction, "player", "몸이 개운해서 좋아, 고마워!");
        int beforeNoUse = refillBackend.Calls;
        Ask(noUse, clear2);
        Check(noUse.PrefetchCompletion.Wait(5000) && refillBackend.Calls == beforeNoUse + 1 && noUse.Cache.TemplateCount(clear2) == 0,
                "UsePrefetched 를 끄면 다시 만들지 않음 (모델은 요청 한 번만) ← 모델 " + (refillBackend.Calls - beforeNoUse) + "번");
        // ③ 넣지 않는 줄(지난 것)은 빈 칸을 남기지 않는다
        var aged = new NpcLineCache { Now = () => DateTime.UtcNow.AddDays(31) };
        NpcLineStoreFormat.TryRead(text, "v1", out read, out readRecent, out matched);
        Check(aged.ImportTemplates(read) == 0 && aged.BucketCount == 0, "넣지 않은 줄은 보관함 칸도 만들지 않음 ← " + aged.BucketCount);
        // ④ '보름달'은 정해진 날 수가 아니다
        Check(NpcLineSlots.Slotify("보름달처럼 반짝여, 고마워!", nineDays) == "보름달처럼 반짝여, 고마워!" && NpcLineSlots.Slotify("보름 만이야!", nineDays) == null,
                "'보름달'은 보관하고 '보름 만'은 보관하지 않음");

        // CodeRabbit 리뷰: 쓸 수 있는 대사만 세고, 꽉 찬 칸은 이 요청에 못 쓰는 것을 비워 새로 넣는다
        Func<string, ReactionRequest> tapWith = nick =>
        {
            ReactionRequest r = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
            r.Situation = "주인이 낮에 너를 보러 찾아왔다.";
            r.Facts.Add(new Fact(NpcFactKeys.TimeOfDay, "낮"));
            if (nick != null)
            {
                r.Nickname = nick;
                r.Intimacy = NpcAddress.NicknameLevel;
            }
            return r;
        };
        ReactionRequest shortNick = tapWith("도윤");
        ReactionRequest longNick = tapWith("가나다라마바사아자차카타파하가나다라마바사");
        var usableCache = new NpcLineCache { MaxPerKey = 2 };
        usableCache.AddTemplate(shortNick, ReactionResult.Success("u1", "greet", NpcTargets.Player, "도윤, 어서 와 줘서 고마워!", ReactionSource.Slm, null));
        usableCache.AddTemplate(shortNick, ReactionResult.Success("u2", "greet", NpcTargets.Player, "도윤, 낮에 와서 반가워!", ReactionSource.Slm, null));
        Check(usableCache.TemplateCount(longNick) == 2 && usableCache.UsableTemplateCount(longNick, 20) == 0 && usableCache.UsableTemplateCount(shortNick, 20) == 2,
                "긴 닉네임을 넣으면 길이를 넘는 대사는 그 요청이 쓸 수 있는 수로 세지 않음");
        Check(usableCache.AddTemplate(longNick, ReactionResult.Success("u3", "greet", NpcTargets.Player, "왔구나, 순찰 중이었어!", ReactionSource.Slm, null), 20)
                && usableCache.TemplateCount(longNick) == 2 && usableCache.UsableTemplateCount(longNick, 20) == 1 && usableCache.UsableTemplateCount(shortNick, 20) == 2,
                "꽉 찬 칸에 이 요청이 못 쓰는 가장 오래된 것을 비우고 넣음 (짧은 닉네임은 남은 것과 새것을 그대로 씀)");
        Check(!usableCache.AddTemplate(shortNick, ReactionResult.Success("u4", "greet", NpcTargets.Player, "드디어 왔구나, 기다렸어!", ReactionSource.Slm, null), 20),
                "쓸 수 있는 대사로 꽉 찼으면 넣지 않음");
        var longBackend = new FakeBackend();
        var longSlm = new SlmReactionGenerator(longBackend, assembler, validator);
        longSlm.LoadAsync(CancellationToken.None).Wait();
        var longManager = new NpcRequestManager(longSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true, PrefetchIdleDelay = TimeSpan.Zero };
        longManager.Cache.AddTemplate(shortNick, ReactionResult.Success("l1", "greet", NpcTargets.Player, "도윤, 어서 와 줘서 고마워!", ReactionSource.Slm, null));
        longManager.Cache.AddTemplate(shortNick, ReactionResult.Success("l2", "greet", NpcTargets.Player, "도윤, 낮에 와서 반가워!", ReactionSource.Slm, null));
        // 관리자의 탭 길이 한도(여유 포함)를 넘는 닉네임 — 보관한 두 대사 모두 칸을 채우면 한도를 넘는다 (CodeRabbit 리뷰: 시험이 정말 길이 때문에 빗나가게)
        int maxLen = longSlm.MaxLineLength(NpcInputs.Tap);
        longNick = tapWith(new string('가', maxLen));
        Check(longManager.Cache.TemplateCount(longNick) == 2 && longManager.Cache.UsableTemplateCount(longNick, maxLen) == 0,
                "요청 전: 보관한 대사 2개 모두 긴 닉네임을 넣으면 한도(" + maxLen + "자)를 넘어 쓸 수 있는 대사가 0개");
        longBackend.Replies.Enqueue(Out("greet", "player", "왔구나, 순찰 중이었어!"));
        longBackend.Replies.Enqueue(Out("greet", "player", "드디어 왔구나, 기다렸어!"));
        longBackend.Replies.Enqueue(Out("greet", "player", "어서 와, 갓 끈 고쳐 맸어!"));
        longBackend.Replies.Enqueue(Out("greet", "player", "안녕, 오늘도 순찰 끝냈어!"));
        int beforeLong = longBackend.Calls;
        NpcResponse longLive = Ask(longManager, longNick);
        Check(longLive.Note != NpcRequestManager.PrefetchedNote && longManager.PrefetchCompletion.Wait(5000) && longManager.Cache.UsableTemplateCount(longNick, maxLen) == 3
                && longBackend.Calls == beforeLong + 4,
                "긴 닉네임 요청이 빗나가면 그 요청이 쓸 수 있는 대사를 탭 목표(2개)보다 하나 더(3개, 못 쓰는 것을 비우고) 만듦 ← 쓸 수 있는 " + longManager.Cache.UsableTemplateCount(longNick, maxLen)
                + "개, 모델 " + (longBackend.Calls - beforeLong) + "번");

        // CodeRabbit 리뷰: 버리는 관리자는 저장소를 닫는다 — 닫은 뒤에는 다시 만들기·저장이 같은 파일에 쓰지 않는다
        var closingStore = new MemoryLineStore();
        var closing = new NpcRequestManager(refillSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true };
        closing.LoadStore(closingStore, "v1");
        closing.CloseStore(true, TimeSpan.FromSeconds(5));
        string closedText = closingStore.Text;
        refillBackend.Reply = Out(clearAction, "player", "몸이 개운하고 가벼워, 고마워!");
        int beforeClosedAsk = refillBackend.Calls;
        Ask(closing, clear2);
        closing.PrefetchCompletion.Wait(5000);
        Thread.Sleep(200); // 뒤에서 하는 저장(SaveSoon)이 돌 틈
        Check(closedText != null && closing.Store == null && !closing.SaveStore() && closingStore.Text == closedText,
                "닫을 때 한 번 쓰고 떼어, 그 뒤의 요청·다시 만들기는 같은 파일에 쓰지 않음");
        Check(refillBackend.Calls == beforeClosedAsk + 1 && closing.Cache.TemplateCount(clear2) == 0,
                "닫은 뒤의 요청은 다시 만들기를 줄에 넣지 않음 (모델은 그 요청 한 번만, CodeRabbit 리뷰) ← 모델 " + (refillBackend.Calls - beforeClosedAsk) + "번");

        // 2026-11-19: 관리한 쪽은 늘 플레이어, 실화를 들려준 쪽은 늘 도깨비
        Check(NpcLineChecks.NormalizeSpeech("개운하게 도깨비가 관리해줘서 고마워", "반말", "밤바다") == "개운하게 관리해줘서 고마워"
                && NpcLineChecks.NormalizeSpeech("내가 닦아 줘서 고마워!", "반말", "밤바다") == "닦아 줘서 고마워!"
                && NpcLineChecks.NormalizeSpeech("내 몸을 닦아줘서 고마워", "반말", "밤바다") == "내 몸을 닦아줘서 고마워"
                && NpcLineChecks.NormalizeSpeech("도깨비가 사는 곳이야", "반말", "밤바다") == "도깨비가 사는 곳이야",
                "도깨비(자기)를 관리한 쪽으로 쓴 주어는 지우고, '내 몸을'·관리와 상관없는 '도깨비가'는 그대로");
        Check(NpcLineChecks.NormalizeSpeech("자개 반짝임 얘기해줘서 고마워", "반말", "밤바다") == "자개 반짝임 얘기 들어줘서 고마워"
                && NpcLineChecks.NormalizeSpeech("암행어사 이야기해 줘서 기뻐", "반말", "밤바다") == "암행어사 이야기 들어 줘서 기뻐"
                && NpcLineChecks.NormalizeSpeech("옛날 이야기를 들려줘서 고마워", "반말", "밤바다") == "옛날 이야기를 들어줘서 고마워",
                "플레이어가 이야기한 것처럼 말한 '얘기해줘서·들려줘서'는 '들어줘서'로");
        Check(NpcLineChecks.NormalizeSpeech("내가 이야기를 들려줬잖아.", "반말", "밤바다") == "내가 이야기를 들려줬잖아."
                && NpcLineChecks.NormalizeSpeech("난 옛날 이야기를 들려줘서 좋았어.", "반말", "밤바다") == "난 옛날 이야기를 들려줘서 좋았어.",
                "도깨비가 들려준 것을 자기 입으로 말한 것은 그대로 (CodeRabbit 리뷰)");
        Check(NpcLineChecks.NormalizeSpeech("개운하게,\n덕분에 기분이 좋아!", "반말", "밤바다").Contains("\n"),
                "첫 낱말 뒤 쉼표를 지울 때도 줄바꿈은 남겨 줄바꿈 검사가 거르게 함 (CodeRabbit 리뷰)");
        Check(NpcLineChecks.FixRecallFragment("기억나는 건 함께 웃었던 일") == "기억나는 건 함께 웃었던 일이야."
                && NpcLineChecks.FixRecallFragment("기억나, 자개 자랑했던 일") == "자개 자랑했던 일이 기억나.",
                "'기억나'는 낱말이 끝날 때만 떼고, '기억나는 건'으로 시작한 말은 '…던 일이야.'로 (CodeRabbit 리뷰)");

        // 2026-11-19: 지난 대화에서 자기 것을 '내' 없이 말하고 "~던 일"로 끝낸 대사를 고친다
        var barePersona = new List<string> { "자개", "옻칠", "몸", "무늬", "반짝임", "빛", "모습" };
        Check(NpcLineChecks.FixRecallFragment(NpcLineChecks.FixBareOwnThing("기억난다, 자개 공들여 만들었다고 자랑했던 일", barePersona)) == "내 자개 공들여 만들었다고 자랑했던 일이 기억나."
                && NpcLineChecks.FixBareOwnThing("빛이 공들여 다듬은 무늬에 비쳐", barePersona) == "빛이 공들여 다듬은 내 무늬에 비쳐"
                && NpcLineChecks.FixBareOwnThing("내 자개는 공들였어. 무늬도 정성껏 새겼지.", barePersona) == "내 자개는 공들였어. 무늬도 정성껏 새겼지."
                && NpcLineChecks.FixBareOwnThing("자개 반짝이는 거 좋아했지?", barePersona) == "자개 반짝이는 거 좋아했지?",
                "솜씨를 말할 때 '내' 없는 자기 것 앞에 하나만 '내'를 붙이고(빛·반짝임은 빼고), '~던 일'은 '~던 일이 기억나.'로");

        // ---- PR #22 Minor 리뷰 반영 ----
        // {날수}는 마지막 관리를 말하는 꼴에서만 — 방문 날 수가 견본 관리 날 수와 같아도 칸으로 만들지 않는다
        Check(NpcLineSlots.Slotify("9일 전에도 왔지?", nineDays) == null && NpcLineSlots.Slotify("관리받은 지 9일이 지났어. 닦아 줄래?", nineDays) == "관리받은 지 {날수}일이 지났어. 닦아 줄래?"
                && NpcLineSlots.Slotify("9일 만에 닦아 줘서 고마워!", nineDays) == "{날수}일 만에 닦아 줘서 고마워!",
                "방문을 말한 '9일 전'은 견본 관리 날 수(9)와 같아도 칸으로 만들지 않음");
        // 다시 검사가 바꾼 최종 대사로 최근 대사와 견준다
        var recheckCache = new NpcLineCache { Recheck = (req, res) => ReactionResult.Success(res.RequestId, res.ActionId, res.TargetId, "반가워, 또 왔구나!", res.Source, res.Note) };
        recheckCache.AddTemplate(shortNick, ReactionResult.Success("c1", "greet", NpcTargets.Player, "왔구나, 순찰 중이었어!", ReactionSource.Slm, null));
        Check(recheckCache.TryTakeTemplate(shortNick, new[] { "반가워, 또 왔구나!" }) == null && recheckCache.TemplateCount(shortNick) == 0,
                "다시 검사가 돌려준 대사가 최근 대사와 같으면 쓰지 않고 버림");
        // 읽어 넣을 때 지난 것을 먼저 쓸어 내 칸을 비운다
        DateTime importClock = DateTime.UtcNow;
        var expiredCache = new NpcLineCache { MaxPerKey = 1, Now = () => importClock };
        expiredCache.AddTemplate(shortNick, ReactionResult.Success("e1", "greet", NpcTargets.Player, "왔구나, 순찰 중이었어!", ReactionSource.Slm, null));
        importClock = importClock.AddDays(31);
        var fresh = new List<NpcLineCache.StoredTemplate>
        {
            new NpcLineCache.StoredTemplate { Key = NpcLineCache.TemplateKeyOf(shortNick), At = importClock,
                Result = ReactionResult.Success("e2", "greet", NpcTargets.Player, "드디어 왔구나, 기다렸어!", ReactionSource.Slm, null) }
        };
        Check(expiredCache.ImportTemplates(fresh) == 1 && expiredCache.TemplateCount(shortNick) == 1, "지난 대사가 칸을 차지해도 새로 읽은 대사를 넣음");
        // 채울 수 계산이 실패해도 응답은 그대로, 미리 만든 대사를 꺼냈으면 최근 대사 기억이 꺼져 있어도 저장한다
        var policyStore = new MemoryLineStore();
        var policy = new NpcRequestManager(refillSlm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true, RecentLineCount = 0, RefillTarget = r => { throw new InvalidOperationException("시험"); },
            SaveDelay = TimeSpan.Zero
        };
        policy.LoadStore(policyStore, "v1");
        policy.Cache.AddTemplate(clear2, ReactionResult.Success("p1", clearAction, NpcTargets.Player, "몸이 개운해, 고마워!", ReactionSource.Slm, null));
        policy.SaveStore();
        string beforeTake = policyStore.Text;
        NpcResponse policyResponse = Ask(policy, clear2);
        Thread.Sleep(300); // 뒤에서 하는 저장(SaveSoon)이 돌 틈
        Check(policyResponse.ShouldShow && policyResponse.Note == NpcRequestManager.PrefetchedNote, "RefillTarget 이 예외를 던져도 미리 만든 대사로 응답함");
        Check(policyStore.Text != beforeTake && !policyStore.Text.Contains("몸이 개운해, 고마워!"), "최근 대사 기억(RecentLineCount=0)이 꺼져 있어도 꺼낸 대사는 파일에서 빠짐");
        policy.CloseStore(false, TimeSpan.FromSeconds(5));

        // 성능 리뷰 (2026-11-19): 미리 만든 대사로 바로 답하는 요청은 모델을 쓰지 않으므로 만들던 미리 만들기를 취소하지 않는다
        // 가짜 모델은 생성을 시작하면 신호를 주고, 시험이 놓아주거나 관리자가 취소할 때까지 붙잡는다 (시간에 기대지 않는다, CodeRabbit 리뷰).
        // 붙잡는 것은 holdNext 가 켜진 다음 호출 하나(미리 만들기)뿐이고, 그 밖의 호출(그 자리에서 만드는 요청)은 바로 답한다
        var slowBackend = new FakeBackend();
        int slowCancelled = 0;
        int holdNext = 0;
        var holdStarted = new TaskCompletionSource<bool>();
        var holdRelease = new TaskCompletionSource<bool>();
        slowBackend.Delayed = async ct =>
        {
            if (Interlocked.Exchange(ref holdNext, 0) == 1)
            {
                TaskCompletionSource<bool> started = holdStarted;
                TaskCompletionSource<bool> release = holdRelease;
                started.TrySetResult(true);
                using (ct.Register(() => release.TrySetCanceled()))
                {
                    try
                    {
                        await release.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Increment(ref slowCancelled);
                        throw;
                    }
                }
            }
            return Out(clearAction, "player", "몸이 한결 가벼워졌어, 고마워!");
        };
        var slowSlm = new SlmReactionGenerator(slowBackend, assembler, validator);
        slowSlm.LoadAsync(CancellationToken.None).Wait();
        var keepGoing = new NpcRequestManager(slowSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        keepGoing.Cache.AddTemplate(faded9, ReactionResult.Success("k1", careAction, NpcTargets.Player, "오랜만에 닦아 줘서 개운해, 고마워!", ReactionSource.Slm, null));
        holdNext = 1;
        keepGoing.PrefetchTemplate(clear2, 1);
        Check(holdStarted.Task.Wait(5000), "미리 만들기가 모델을 부르기 시작함");
        NpcResponse hitWhileBuilding = Ask(keepGoing, faded12);
        bool keptDuringHit = Volatile.Read(ref slowCancelled) == 0 && !holdRelease.Task.IsCompleted;
        holdRelease.TrySetResult(true);
        Check(hitWhileBuilding.Note == NpcRequestManager.PrefetchedNote && keptDuringHit && keepGoing.PrefetchCompletion.Wait(5000) && slowCancelled == 0
                && slowBackend.Calls == 1 && keepGoing.Cache.TemplateCount(clear2) == 1,
                "미리 만든 대사로 답하는 동안 만들던 미리 만들기를 버리지 않고 끝까지 만듦 ← 취소 " + slowCancelled + "번, 모델 " + slowBackend.Calls + "번");
        // 모델을 쓰는 요청(보관함이 빈 상황)은 만들던 것을 멈추고 먼저 처리한다
        slowCancelled = 0;
        holdStarted = new TaskCompletionSource<bool>();
        holdRelease = new TaskCompletionSource<bool>();
        holdNext = 1;
        keepGoing.PrefetchTemplate(resealed16, 1);
        Check(holdStarted.Task.Wait(5000), "두 번째 미리 만들기가 모델을 부르기 시작함");
        NpcResponse liveWhileBuilding = Ask(keepGoing, tapWith("도윤"));
        Check(liveWhileBuilding.ShouldShow && liveWhileBuilding.Note != NpcRequestManager.PrefetchedNote && slowCancelled == 1,
                "그 자리에서 모델을 쓰는 요청은 만들던 미리 만들기를 멈춤 ← 취소 " + slowCancelled + "번");
        keepGoing.PrefetchCompletion.Wait(5000);

        // 2026-11-23 기기 시험: 뒤에서 다시 만들기가 모델을 붙잡아 미리 읽기가 0ms → 60초 안팎으로 늘었다.
        // ① 요청이 끝난 뒤 PrefetchIdleDelay 동안은 다시 만들기를 새로 시작하지 않고, 지나면 만든다
        Check(new NpcRequestManager(slowSlm, rule).PrefetchIdleDelay == TimeSpan.FromSeconds(3), "다시 만들기 전에 쉬는 시간은 기본 3초 (부딪혀도 플레이어가 먼저라 10초에서 줄였다, 2026-11-25)");
        var idleBackend = new FakeBackend();
        var idleSlm = new SlmReactionGenerator(idleBackend, assembler, validator);
        idleSlm.LoadAsync(CancellationToken.None).Wait();
        var idle = new NpcRequestManager(idleSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true, PrefetchIdleDelay = TimeSpan.FromMilliseconds(2000) };
        idle.Cache.AddTemplate(faded9, ReactionResult.Success("i1", careAction, NpcTargets.Player, "오랜만에 닦아 줘서 개운해, 고마워!", ReactionSource.Slm, null));
        idleBackend.Reply = Out(careAction, "player", "드디어 몸이 맑아졌어, 고마워!");
        NpcResponse idleHit = Ask(idle, faded12);
        Stopwatch idleWatch = Stopwatch.StartNew();
        Thread.Sleep(300);
        int idleCallsEarly = idleBackend.Calls;
        bool idleLookedEarly = idleWatch.ElapsedMilliseconds < 1500;   // 시험 기계가 느려 쉬는 시간을 넘겨 봤으면 '아직'을 따지지 않는다
        Check(idleHit.Note == NpcRequestManager.PrefetchedNote && (idleCallsEarly == 0 || !idleLookedEarly), "요청이 끝나자마자는 다시 만들지 않음 (쉬는 시간 안) ← 모델 " + idleCallsEarly + "번");
        Check(idle.PrefetchCompletion.Wait(8000) && idleBackend.Calls == 1 && idle.Cache.TemplateCount(faded12) == 1,
                "쉬는 시간이 지나면 쓴 만큼 다시 만듦 ← 모델 " + idleBackend.Calls + "번, 보관 " + idle.Cache.TemplateCount(faded12) + "개");

        // ② 미리 읽기(도깨비를 확대)는 뒤에서 만들던 것을 멈추고, 끝난 뒤 쉬는 시간 동안 새로 만들지 않으며, 그 뒤 이어 만든다 (멈춘 것은 실패로 세지 않는다)
        var focusBackend = new FakeBackend();
        int focusCancelled = 0;
        int focusHold = 1;
        var focusStarted = new TaskCompletionSource<bool>();
        focusBackend.Delayed = async ct =>
        {
            if (Interlocked.Exchange(ref focusHold, 0) == 1)
            {
                focusStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref focusCancelled);
                    throw;
                }
            }
            return Out(clearAction, "player", "몸이 한결 가벼워졌어, 고마워!");
        };
        var focusSlm = new SlmReactionGenerator(focusBackend, assembler, validator);
        focusSlm.LoadAsync(CancellationToken.None).Wait();
        // 다른 도깨비(5번)를 확대 — 8번의 미리 만들기를 멈춘다. 다른 도깨비로 넘어간 뒤 쉬는 시간은 이 시험에서는 같게 둔다
        var focus = new NpcRequestManager(focusSlm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5),
            PrefetchIdleDelay = TimeSpan.FromMilliseconds(2000), PrefetchSwitchIdleDelay = TimeSpan.FromMilliseconds(2000)
        };
        focus.PrefetchTemplate(clear2, 1);
        Check(focusStarted.Task.Wait(5000), "미리 읽기 전: 뒤에서 미리 만들기가 모델을 부르기 시작함");
        Check(focus.PrepareAsync(5).Wait(3000) && Volatile.Read(ref focusCancelled) == 1 && focusBackend.Prepared.Count == 1,
                "다른 도깨비의 미리 읽기가 뒤에서 만들던 것을 멈추고 끝남 ← 취소 " + focusCancelled + "번");
        Stopwatch focusWatch = Stopwatch.StartNew();
        Thread.Sleep(300);
        int focusCallsEarly = focusBackend.Calls;
        bool focusLookedEarly = focusWatch.ElapsedMilliseconds < 1500;
        Check(!focusLookedEarly || (focusCallsEarly == 1 && !focus.PrefetchCompletion.IsCompleted),
                "미리 읽기가 끝난 뒤 쉬는 시간 동안은 새로 만들지 않음 (선택지를 고르는 사이) ← 모델 " + focusCallsEarly + "번");
        Check(focus.PrefetchCompletion.Wait(8000) && focusBackend.Calls == 2 && focus.Cache.TemplateCount(clear2) == 1,
                "쉬는 시간이 지나면 멈췄던 것을 이어 만듦 ← 모델 " + focusBackend.Calls + "번, 보관 " + focus.Cache.TemplateCount(clear2) + "개");
        Check(NpcLineChecks.Cached("가(나)다") == NpcLineChecks.Cached("가(나)다"), "같은 패턴의 정규식은 한 번 만들어 다시 씀");

        // ②-2 (2026-11-24 기기 시험: 장면 사이 10초 = 쉬는 시간일 때 같은 도깨비의 미리 읽기가 0초 → 45~68초)
        // 뒤에서 만들기는 모델이 시스템 메시지를 읽어 둔 도깨비의 작업을 먼저 고르고(줄 맨 앞이 다른 도깨비여도), 다른 도깨비의 작업은 더 오래 쉰 뒤에 시작한다
        Check(new NpcRequestManager(slowSlm, rule).PrefetchSwitchIdleDelay == TimeSpan.FromSeconds(30), "다른 도깨비로 넘어가 만들기 전에 쉬는 시간은 기본 30초");
        var spiritBackend = new FakeBackend { Reply = Out("greet", "player", "왔구나, 정말 반가워!") };
        var spiritLog = new List<KeyValuePair<long, string>>();
        Stopwatch spiritWatch = new Stopwatch();
        spiritBackend.OnComplete = () =>
        {
            lock (spiritLog)
            {
                spiritLog.Add(new KeyValuePair<long, string>(spiritWatch.ElapsedMilliseconds, spiritBackend.LastPrompt.System));
            }
        };
        var spiritSlm = new SlmReactionGenerator(spiritBackend, assembler, validator);
        spiritSlm.LoadAsync(CancellationToken.None).Wait();
        var spirits = new NpcRequestManager(spiritSlm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5),
            PrefetchIdleDelay = TimeSpan.FromMilliseconds(200), PrefetchSwitchIdleDelay = TimeSpan.FromMilliseconds(1500)
        };
        ReactionRequest otherTap = builder.Build(Owner(5, NpcInputs.Tap, "CLEAR", 2)).Request;
        ReactionRequest focusedTap = builder.Build(Owner(8, NpcInputs.Tap, "CLEAR", 2)).Request;
        string otherSystem = assembler.SystemFor(5);
        string focusedSystem = assembler.SystemFor(8);
        Check(spirits.PrepareAsync(8).Wait(3000), "도깨비 고르기 시험: 8번을 미리 읽음");
        spiritWatch.Start();
        spirits.PrefetchTemplate(otherTap, 1);    // 줄 맨 앞은 다른 도깨비(5번)
        spirits.PrefetchTemplate(focusedTap, 1);
        Check(spirits.PrefetchCompletion.Wait(10000), "도깨비 고르기 시험: 미리 만들기가 끝남");
        List<KeyValuePair<long, string>> spiritCalls;
        lock (spiritLog)
        {
            spiritCalls = new List<KeyValuePair<long, string>>(spiritLog);
        }
        int firstOther = spiritCalls.FindIndex(c => c.Value == otherSystem);
        int lastFocused = spiritCalls.FindLastIndex(c => c.Value == focusedSystem);
        Check(spiritCalls.Count >= 2 && spiritCalls[0].Value == focusedSystem && firstOther > lastFocused,
                "모델이 읽어 둔 도깨비(8번)의 작업을 줄 맨 앞의 다른 도깨비(5번)보다 먼저 만듦 ← "
                + string.Join(", ", spiritCalls.Select(c => (c.Value == focusedSystem ? "8번" : c.Value == otherSystem ? "5번" : "?") + "@" + c.Key + "ms").ToArray()));
        Check(firstOther >= 0 && spiritCalls[firstOther].Key >= 1200,
                "다른 도깨비의 작업은 PrefetchSwitchIdleDelay(1.5초)를 쉰 뒤에 시작 ← " + (firstOther >= 0 ? spiritCalls[firstOther].Key + "ms" : "안 만듦"));
        Check(spiritCalls.Count >= 1 && spiritCalls[0].Key < 1200 || spiritWatch.ElapsedMilliseconds > 8000,
                "같은 도깨비의 작업은 PrefetchIdleDelay(0.2초)만 쉬고 시작 ← " + (spiritCalls.Count > 0 ? spiritCalls[0].Key + "ms" : "안 만듦"));

        // ②-3 (2026-11-25) 같은 도깨비를 확대하면 뒤에서 만들던 그 도깨비의 대사를 멈추지 않고 미리 읽지도 않는다 (그 생성이 시스템 메시지를 읽고 있어
        // 미리 읽기는 할 일이 없다). 우선권은 그대로 — 이어서 모델이 필요한 요청이 오면 그 생성을 멈추고 모델을 먼저 쓴다
        var joinBackend = new FakeBackend();
        var joinRelease = new TaskCompletionSource<bool>();
        var joinStarted = new TaskCompletionSource<bool>();
        int joinCancelled = 0;
        int joinHold = 1;
        joinBackend.Delayed = async ct =>
        {
            if (Interlocked.Exchange(ref joinHold, 0) == 1)
            {
                joinStarted.TrySetResult(true);
                var stopped = new TaskCompletionSource<bool>();
                using (ct.Register(() => stopped.TrySetResult(true)))
                {
                    await Task.WhenAny(joinRelease.Task, stopped.Task);
                }
                if (ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref joinCancelled);
                    throw new OperationCanceledException(ct);
                }
            }
            return Out(clearAction, "player", "몸이 한결 가벼워졌어, 고마워!");
        };
        var joinSlm = new SlmReactionGenerator(joinBackend, assembler, validator) { MaxAttempts = 1 };
        joinSlm.LoadAsync(CancellationToken.None).Wait();
        var joining = new NpcRequestManager(joinSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        joining.PrefetchTemplate(clear2, 1);
        Check(joinStarted.Task.Wait(5000), "플레이어 먼저 시험: 뒤에서 8번의 대사를 만들기 시작함");
        Check(joining.PrepareAsync(clear2.RelicId).Wait(3000) && Volatile.Read(ref joinCancelled) == 0 && joinBackend.Prepared.Count == 0 && joining.LastPrepareDeferred,
                "같은 도깨비를 확대하면 그 도깨비의 미리 만들기를 멈추지 않고 미리 읽지도 않음 ← 취소 " + joinCancelled + "번, 미리 읽기 " + joinBackend.Prepared.Count + "번");
        ReactionRequest sameCare = builder.Build(Owner(8, NpcInputs.Care, "CLEAR", 2)).Request;
        NpcResponse firstResponse = Ask(joining, sameCare);
        Check(firstResponse.ShouldShow && !firstResponse.UsedFallback && firstResponse.Note != NpcRequestManager.PrefetchedNote && Volatile.Read(ref joinCancelled) == 1,
                "모델이 필요한 요청은 뒤에서 만들던 것을 기다리지 않고 멈춘 뒤 모델로 바로 만듦 ← " + firstResponse.Note + ", 취소 " + joinCancelled + "번");
        // 다른 도깨비를 확대하면 예전처럼 멈추고 미리 읽는다
        var otherBackend = new FakeBackend();
        var otherRelease = new TaskCompletionSource<bool>();
        var otherStarted = new TaskCompletionSource<bool>();
        int otherCancelled = 0;
        otherBackend.Delayed = async ct =>
        {
            otherStarted.TrySetResult(true);
            var stopped = new TaskCompletionSource<bool>();
            using (ct.Register(() => stopped.TrySetResult(true)))
            {
                await Task.WhenAny(otherRelease.Task, stopped.Task);
            }
            if (ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref otherCancelled);
                throw new OperationCanceledException(ct);
            }
            return Out(clearAction, "player", "몸이 한결 가벼워졌어, 고마워!");
        };
        var otherSlm = new SlmReactionGenerator(otherBackend, assembler, validator) { MaxAttempts = 1 };
        otherSlm.LoadAsync(CancellationToken.None).Wait();
        var others = new NpcRequestManager(otherSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        others.PrefetchTemplate(clear2, 1);
        Check(otherStarted.Task.Wait(5000) && others.PrepareAsync(5).Wait(3000) && Volatile.Read(ref otherCancelled) == 1 && otherBackend.Prepared.Count == 1 && !others.LastPrepareDeferred,
                "다른 도깨비를 확대하면 뒤에서 만들던 것을 멈추고 미리 읽음 ← 취소 " + otherCancelled + "번, 미리 읽기 " + otherBackend.Prepared.Count + "번");
        otherRelease.TrySetResult(true);
        others.CancelPrefetch();
        joinRelease.TrySetResult(true);
        joining.PrefetchCompletion.Wait(5000);

        // ②-4 (2026-11-25) 다른 도깨비의 작업은 모델을 마지막으로 쓴 때부터 PrefetchSwitchIdleDelay 를 센다 — 모델을 쓰지 않는 요청(이야기)이 이어져도 시작한다
        var storyBackend = new FakeBackend { Reply = Out("greet", "player", "왔구나, 정말 반가워!") };
        var storySlm = new SlmReactionGenerator(storyBackend, assembler, validator);
        storySlm.LoadAsync(CancellationToken.None).Wait();
        var stories = new NpcRequestManager(storySlm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5),
            PrefetchIdleDelay = TimeSpan.FromMilliseconds(300), PrefetchSwitchIdleDelay = TimeSpan.FromMilliseconds(1500), AdaptiveIdleDelay = false
        };
        Check(stories.PrepareAsync(8).Wait(3000), "이야기 시험: 8번을 미리 읽음");
        stories.PrefetchTemplate(otherTap, 1);   // 다른 도깨비(5번)
        Stopwatch storyWatch = Stopwatch.StartNew();
        while (storyBackend.Calls == 0 && storyWatch.ElapsedMilliseconds < 4000)
        {
            Ask(stories, Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory)); // 모델을 쓰지 않는 요청을 0.4초마다 (쉬는 시간 0.3초보다 길게)
            Thread.Sleep(400);
        }
        Check(storyBackend.Calls > 0 && storyWatch.ElapsedMilliseconds >= 1200,
                "모델을 쓰지 않는 요청이 이어져도 다른 도깨비의 작업은 모델을 쓴 지 1.5초 뒤에 시작 ← " + (storyBackend.Calls > 0 ? storyWatch.ElapsedMilliseconds + "ms" : "4초 동안 안 만듦"));
        stories.PrefetchCompletion.Wait(5000);

        // ②-5 (2026-11-25) AdaptiveIdleDelay: 미리 만들기 한 번이 빠른 기기는 PrefetchIdleDelay(여기서 5초)를 다 쉬지 않고 그 두 배(1초 이상)만 쉬고 다시 만든다
        Func<bool, long> refillAfter = adaptive =>
        {
            var quickBackend = new FakeBackend();
            quickBackend.Replies.Enqueue(Out(clearAction, "player", "몸이 한결 가벼워졌어, 고마워!"));
            quickBackend.Reply = Out(clearAction, "player", "덕분에 내 무늬가 또렷해졌어, 고마워!");
            var quickSlm = new SlmReactionGenerator(quickBackend, assembler, validator);
            quickSlm.LoadAsync(CancellationToken.None).Wait();
            var quick = new NpcRequestManager(quickSlm, rule)
            {
                SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5), RefillAfterUse = true,
                PrefetchIdleDelay = TimeSpan.FromSeconds(5), AdaptiveIdleDelay = adaptive
            };
            quick.PrefetchTemplate(clear2, 1);
            quick.PrefetchCompletion.Wait(5000);    // 처음(한가함)은 바로 만든다 — 이 한 번의 시간을 잰다
            Ask(quick, builder.Build(Owner(8, NpcInputs.Care, "CLEAR", 2)).Request);   // 미리 만든 대사로 답하고 쓴 만큼 다시 만들기를 건다
            Stopwatch refillWatch = Stopwatch.StartNew();
            while (quickBackend.Calls < 2 && refillWatch.ElapsedMilliseconds < 3500)
            {
                Thread.Sleep(50);
            }
            return quickBackend.Calls >= 2 ? refillWatch.ElapsedMilliseconds : -1;
        };
        long adaptiveMs = refillAfter(true);
        long fixedMs = refillAfter(false);
        Check(adaptiveMs >= 800 && adaptiveMs < 3000 && fixedMs == -1,
                "빠른 기기는 쉬는 시간을 생성 시간의 두 배(1초 이상)로 줄여 다시 만듦, 끄면 PrefetchIdleDelay(5초)를 다 쉼 ← 켬 " + adaptiveMs + "ms, 끔 " + fixedMs + "ms");

        // ③ 미리 읽기·요청이 멈춘 생성은 실패로 세지 않는다 — 쉬는 시간이 0 이고 미리 읽기가 바로 끝나면, 멈춘 생성이 돌아왔을 때 이미 한가해
        //    예전에는 시간 초과로 세어 세 번이면 그 상황을 그만 만들었다
        var preemptBackend = new FakeBackend();
        int preemptHolds = 3;
        var preemptStarted = new SemaphoreSlim(0);
        preemptBackend.Delayed = async ct =>
        {
            if (Interlocked.Decrement(ref preemptHolds) >= 0)
            {
                preemptStarted.Release();
                bool stopped = false;
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);   // 멈출 때까지 붙잡는다
                }
                catch (OperationCanceledException)
                {
                    stopped = true;
                }
                if (stopped)
                {
                    // 기기 백엔드처럼 늦게 멈춘다 (프롬프트를 다 읽고 첫 글자가 나온 뒤에야 멈춤) — 취소한 쪽(미리 읽기)은 기다리지 않고 끝나 그사이 한가해진다.
                    // 취소는 부른 스레드에서 이어 돌 수 있어, 다른 스레드로 넘긴 뒤 기다린다 (C# 5 는 catch 안에서 await 하지 못한다)
                    await Task.Run(() => Thread.Sleep(300));
                    throw new OperationCanceledException(ct);
                }
            }
            return Out(clearAction, "player", "몸이 한결 가벼워졌어, 고마워!");
        };
        var preemptSlm = new SlmReactionGenerator(preemptBackend, assembler, validator) { MaxAttempts = 1 };
        preemptSlm.LoadAsync(CancellationToken.None).Wait();
        var preemptManager = new NpcRequestManager(preemptSlm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero, PrefetchSwitchIdleDelay = TimeSpan.Zero
        };
        preemptManager.PrefetchTemplate(clear2, 1);
        bool preemptedAll = true;
        for (int i = 0; i < 3; i++)
        {
            preemptedAll &= preemptStarted.Wait(5000) && preemptManager.PrepareAsync(5).Wait(3000); // 다른 도깨비의 미리 읽기 (같은 도깨비면 멈추지 않는다)
        }
        Check(preemptedAll && preemptManager.PrefetchCompletion.Wait(5000) && preemptManager.Cache.TemplateCount(clear2) == 1 && preemptBackend.Calls == 4,
                "미리 읽기에 세 번 멈춰도 그 상황을 그만두지 않고 끝내 만듦 ← 보관 " + preemptManager.Cache.TemplateCount(clear2) + "개, 모델 " + preemptBackend.Calls + "번");

        // ④ 같은 상황을 더 많이 채워 달라고 해도 상황마다 둘 수 있는 수(MaxPerKey 3)를 넘겨 만들지 않는다 (넘기면 넣지 못할 생성을 실패가 쌓일 때까지 거듭했다)
        var capBackend = new FakeBackend();
        var capStarted = new TaskCompletionSource<bool>();
        var capRelease = new TaskCompletionSource<bool>();
        int capFirst = 1;
        string[] capLines = { "몸이 한결 가벼워졌어, 고마워!", "무늬가 또렷해졌어, 고마워!", "자개가 반짝반짝해, 고마워!", "향이 맑아졌어, 고마워!", "옻칠이 윤이 나, 고마워!", "덕분에 개운해졌어, 고마워!" };
        int capIndex = -1;
        capBackend.Delayed = async ct =>
        {
            if (Interlocked.Exchange(ref capFirst, 0) == 1)
            {
                capStarted.TrySetResult(true);
                await capRelease.Task;
            }
            return Out(clearAction, "player", capLines[Math.Min(Interlocked.Increment(ref capIndex), capLines.Length - 1)]);
        };
        var capSlm = new SlmReactionGenerator(capBackend, assembler, validator) { MaxAttempts = 1 };
        capSlm.LoadAsync(CancellationToken.None).Wait();
        var capManager = new NpcRequestManager(capSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        capManager.PrefetchTemplate(clear2, 1);
        Check(capStarted.Task.Wait(5000), "한도 시험: 첫 생성이 시작됨");
        capManager.PrefetchTemplate(clear2, 10);   // 줄에 있는 같은 상황과 합친다
        capRelease.TrySetResult(true);
        Check(capManager.PrefetchCompletion.Wait(5000) && capManager.Cache.TemplateCount(clear2) == 3 && capBackend.Calls == 3,
                "합친 작업도 MaxPerKey(3)까지만 만듦 ← 보관 " + capManager.Cache.TemplateCount(clear2) + "개, 모델 " + capBackend.Calls + "번");

        // 2026-11-23 기기 시험: SLM 실패의 대부분이 '반복'이었다 (같은 첫 낱말 → 같은 대사, seed 0 → 같은 프롬프트에 같은 대사)
        // ① 같은 시나리오에 같은 번호로 잇달아 와도(S22·S23·S24 같은 탭 인사) 첫 낱말을 바꿔 준다
        Func<NpcPrompt, string> starterOf = prompt =>
        {
            if (prompt == null)
            {
                return null;
            }
            string found = prompt.User.Split('\n').FirstOrDefault(l => l.StartsWith(PromptAssembler.StarterLabel + ": ", StringComparison.Ordinal));
            return found != null ? found.Substring(PromptAssembler.StarterLabel.Length + 2).Trim() : null;
        };
        var starterBackend = new FakeBackend();
        // 한 번만 만드는 생성기 — 검사에 걸려 다시 만들면 생성기가 번호를 1 늘려, LastPrompt 가 관리자가 고른 프롬프트가 아니게 된다 (CodeRabbit 식 검토)
        var starterSlm = new SlmReactionGenerator(starterBackend, assembler, validator) { MaxAttempts = 1 };
        starterSlm.LoadAsync(CancellationToken.None).Wait();
        var starters = new NpcRequestManager(starterSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5) };
        string[] starterReplies = { "왔구나, 순찰 중이었어!", "드디어 왔구나, 기다렸어!", "어서 와, 갓 끈 고쳐 맸어!" };
        var given = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            starterBackend.Reply = Out("greet", "player", starterReplies[i]);
            Ask(starters, Tap(8));   // 늘 Variation 0
            given.Add(starterOf(starterBackend.LastPrompt));
        }
        Check(given.All(s => s != null) && given.Distinct().Count() == 3,
                "같은 번호로 세 번 와도 첫 낱말이 셋 다 다름 ← " + string.Join(" / ", given.ToArray()));
        // ② 최근 대사가 그 첫 낱말로 시작하면 다른 첫 낱말을 준다 (앱을 다시 켜 같은 번호부터 세도 파일의 최근 대사와 겹치지 않게)
        var starters2 = new NpcRequestManager(starterSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5) };
        ReactionRequest againTap = Tap(8);
        string planned = PromptAssembler.Starter(againTap);
        againTap.RecentLines = new List<string> { planned + ", 반가워!" };
        Ask(starters2, againTap);
        string gotStarter = starterOf(starterBackend.LastPrompt);
        Check(planned != null && gotStarter != null && gotStarter != planned, "최근 대사 '" + planned + ", 반가워!'와 같은 첫 낱말은 피함 ← " + gotStarter);
        // ③ 미리 만들기도 보관한 대사·최근 대사의 첫 낱말을 피한다
        var starters3 = new NpcRequestManager(starterSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        ReactionRequest prefetchTap = Tap(8);
        string prefetchPlanned = PromptAssembler.Starter(prefetchTap);
        starters3.Cache.Add(prefetchTap, ReactionResult.Success("s1", "greet", NpcTargets.Player, prefetchPlanned + " 왔네, 기다렸어!", ReactionSource.Slm, null));
        starterBackend.Reply = Out("greet", "player", "어서 와, 갓 끈 고쳐 맸어!");
        starters3.Prefetch(prefetchTap, 2);
        starters3.PrefetchCompletion.Wait(5000);
        string prefetchStarter = starterOf(starterBackend.LastPrompt);
        Check(prefetchStarter != null && prefetchStarter != prefetchPlanned, "미리 만들기는 보관한 대사의 첫 낱말('" + prefetchPlanned + "')을 피함 ← " + prefetchStarter);
        // ⑤ 흐려진 안부는 코드가 붙인 앞 문장을 떼고 부탁의 말머리를 견준다 — 다른 날(다른 날 수)·칸('{날수}')의 최근 대사도 (CodeRabbit 리뷰).
        // 2026-11-24: 출력 검사가 앞 문장까지 포함한 전체 대사로 반복을 보므로, 이번 번호에 붙을 앞 문장과 꼴이 같은 최근 대사의 말머리만 피한다
        // (요청마다 builder 가 번호를 늘리므로 앞 문장은 보낼 그 요청에서 구한다)
        for (int kind = 0; kind < 4; kind++)
        {
            ReactionRequest fadedAsk = builder.Build(Owner(5, NpcInputs.AskStatus, "FADED", 9)).Request;
            string fadedPlanned = PromptAssembler.Starter(fadedAsk);
            string ownLead = PromptAssembler.LinePrefix(fadedAsk);
            string ignoredRest;
            string ownShape = PromptAssembler.AskPrefixShape(ownLead + " ", out ignoredRest);
            // 0: 같은 앞 문장, 1: 날 수만 다름, 2: 날 수 칸, 3: 꼴이 다른 앞 문장
            string lead = kind == 0 ? ownLead
                    : kind == 1 ? (ownLead.Contains("9일") ? ownLead.Replace("9일", "30일") : ownLead)
                    : kind == 2 ? ownLead.Replace("9일", "{날수}일")
                    : PromptAssembler.AskPrefixes(fadedAsk).First(p => PromptAssembler.AskPrefixShape(p + " ", out ignoredRest) != ownShape);
            fadedAsk.RecentLines = new List<string> { lead + " " + fadedPlanned + " 닦아 줄래?" };
            starterBackend.Reply = Out("answer_status", "player", "괜찮으면 닦아 줄래?");
            Ask(new NpcRequestManager(starterSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5) }, fadedAsk);
            string fadedGot = starterOf(starterBackend.LastPrompt);
            bool sameShape = kind < 3;
            // 같은 꼴이면: 말머리를 바꾸거나 앞 문장을 바꿔(번호를 옮겨) 전체 대사가 최근 대사와 달라진다. 다른 꼴이면 번호를 그대로 둔다
            string gotLead = starterBackend.LastPrompt != null ? starterBackend.LastPrompt.User.Split('\n').FirstOrDefault(l => l.StartsWith("이미 한 말: ", StringComparison.Ordinal)) : null;
            bool differs = fadedGot != fadedPlanned || gotLead != "이미 한 말: " + ownLead;
            Check(fadedPlanned != null && fadedGot != null && (sameShape ? differs : fadedGot == fadedPlanned && gotLead == "이미 한 말: " + ownLead),
                    "흐려진 안부: 최근 대사 '" + lead + " " + fadedPlanned + " …' — " + (sameShape ? "같은 앞 문장 + 같은 말머리를 피함" : "앞 문장이 달라 번호를 그대로 둠")
                    + " ← " + gotLead + " " + fadedGot);
        }
        Check(PromptAssembler.StripAnyAskPrefix("몸이 흐려지다 못해 봉인됐어. 조심조심 깨워 줘.") == "조심조심 깨워 줘."
                && PromptAssembler.StripAnyAskPrefix("몸이 흐려졌어? 살살") == "몸이 흐려졌어? 살살" && PromptAssembler.StripAnyAskPrefix("살살 닦아 줘.") == "살살 닦아 줘.",
                "앞 문장은 코드가 붙이는 꼴만 뗌");
        // ④ 첫 낱말이 없는 요청(지난 대화 꺼내기)은 번호를 바꾸지 않는다
        ReactionRequest recallAsk = Request(8, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.RecallTalk);
        recallAsk.Variation = 5;
        Ask(new NpcRequestManager(starterSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5) }, recallAsk);
        Check(PromptAssembler.Starter(recallAsk) == null && recallAsk.Variation == 5, "첫 낱말이 없는 요청은 곁들일 거리 번호를 그대로 둠 ← " + recallAsk.Variation);

        // 2026-11-23 적중 개선: 최근 띄운 대사와 겹쳐 꺼낼 때 버릴 대사는 '쓸 수 있는 수'에 넣지 않는다
        // (예전에는 파일에서 읽은 대사가 최근 대사와 겹쳐도 "찼다"고 보고 새로 만들지 않아, 요청 때 그 대사를 버리고 빗나갔다)
        var pruneCache = new NpcLineCache();
        ReactionRequest pruneTap = tapWith("도윤");
        pruneCache.AddTemplate(pruneTap, ReactionResult.Success("p1", "greet", NpcTargets.Player, "도윤, 어서 와 줘서 고마워!", ReactionSource.Slm, null));
        pruneCache.AddTemplate(pruneTap, ReactionResult.Success("p2", "greet", NpcTargets.Player, "왔구나, 순찰 중이었어!", ReactionSource.Slm, null));
        Check(pruneCache.UsableTemplateCount(pruneTap, 40) == 2 && pruneCache.UsableTemplateCount(pruneTap, 40, new[] { "도윤, 어서 와 줘서 고마워!" }) == 1
                && pruneCache.TemplateCount(pruneTap) == 1,
                "최근 대사와 겹치는 보관 대사는 버리고 셈 (최근 대사를 주지 않으면 예전처럼 셈)");
        var pruneKeyCache = new NpcLineCache();
        pruneKeyCache.Add(pruneTap, ReactionResult.Success("k1", "greet", NpcTargets.Player, "왔구나, 순찰 중이었어!", ReactionSource.Slm, null));
        Check(pruneKeyCache.Count(pruneTap, new[] { "왔구나, 순찰 중이었어!" }) == 0 && pruneKeyCache.Count(pruneTap) == 0, "서버 값 열쇠의 보관함도 최근 대사와 겹치는 것은 버리고 셈");
        // 미리 만들기: 보관한 대사가 모두 최근 대사와 겹치면 새로 만든다
        var pruneBackend = new FakeBackend();
        var pruneSlm = new SlmReactionGenerator(pruneBackend, assembler, validator);
        pruneSlm.LoadAsync(CancellationToken.None).Wait();
        var pruneManager = new NpcRequestManager(pruneSlm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        pruneManager.Cache.AddTemplate(pruneTap, ReactionResult.Success("m1", "greet", NpcTargets.Player, "왔구나, 순찰 중이었어!", ReactionSource.Slm, null));
        pruneBackend.Reply = Out("greet", "player", "드디어 왔구나, 기다렸어!");
        ReactionRequest pruneTemplate = tapWith("도윤");
        pruneTemplate.RecentLines = new List<string> { "왔구나, 순찰 중이었어!" };   // 앞서 띄운 대사 (앱을 다시 켜 파일에서 되살린 최근 대사와 같다)
        pruneManager.PrefetchTemplate(pruneTemplate, 1);
        ReactionResult pruneTaken = pruneManager.PrefetchCompletion.Wait(5000) ? pruneManager.Cache.TryTakeTemplate(pruneTap, pruneTemplate.RecentLines) : null;
        Check(pruneBackend.Calls == 1 && pruneTaken != null && pruneTaken.Line == "드디어 왔구나, 기다렸어!",
                "보관한 대사가 최근 대사와 겹치면 '찼다'고 보지 않고 새로 만들어, 요청이 새 대사로 적중함 ← 모델 " + pruneBackend.Calls + "번, " + (pruneTaken != null ? pruneTaken.Line : "(빗나감)"));

        // ⑤ LLMUnity seed: 정하면 그 값, 없으면 생성마다 새 무작위 값 (0 은 늘 같은 대사가 나왔다)
        Check(new LlmUnityOptions { Seed = 42 }.SeedForCall() == 42, "Seed 를 정하면 그 값을 씀 (재현용)");
        var unseeded = new LlmUnityOptions();
        var seeds = Enumerable.Range(0, 20).Select(i => unseeded.SeedForCall()).ToList();
        Check(seeds.All(s => s >= 1) && seeds.Distinct().Count() > 1, "Seed 가 없으면 생성마다 1 이상의 다른 seed ← " + string.Join(",", seeds.Take(5).Select(s => s.ToString()).ToArray()));
    }

    // 메모리 안의 저장소 (파일 대신)
    private sealed class MemoryLineStore : INpcLineStore
    {
        private readonly object gate = new object();
        private string text;

        public MemoryLineStore()
        {
        }

        public MemoryLineStore(string text)
        {
            this.text = text;
        }

        public string Text
        {
            get
            {
                lock (gate)
                {
                    return text;
                }
            }
        }

        public Action OnRead;   // 읽는 사이에 할 일 (읽기와 저장이 겹치는 것을 흉내 낸다)

        public string Read()
        {
            string value = Text;
            if (OnRead != null)
            {
                OnRead();
            }
            return value;
        }

        public void Write(string value)
        {
            lock (gate)
            {
                text = value;
            }
        }
    }

    // 서버 값(시간대·호칭·방문 횟수)에 달린 말을 칸으로 남겨 두고, 서버 값이 온 뒤 칸에 넣어 대사를 완성한다
    private static void CheckSlots(PersonaCatalog personas, NpcOutputValidator validator, PromptAssembler assembler, RuleBasedReactionGenerator rule, NpcContextBuilder builder)
    {
        Console.WriteLine("=== 칸 있는 대사(서버 값 기다리지 않고 미리 만들기) ===");
        var sample = new SlotValues { TimeWord = "낮", Address = "발굴자", VisitCount = 3 };
        Check(NpcLineSlots.Slotify("낮에 찾아와 줘서 고마워!", sample) == "{시간대}에 찾아와 줘서 고마워!", "시간대 뒤에 조사가 있으면 칸으로");
        Check(NpcLineSlots.Slotify("발굴자, 어서 와!", sample) == "{호칭}, 어서 와!", "호칭은 칸으로");
        Check(NpcLineSlots.Slotify("발굴자야, 어서 와!", sample) == "{호칭아}, 어서 와!", "부르는 말의 '야'는 호칭아 칸으로");
        Check(NpcLineSlots.Slotify("3번이나 와 줘서 고마워!", sample) == "{횟수}번이나 와 줘서 고마워!", "방문 횟수는 칸으로");
        Check(NpcLineSlots.Slotify("낮잠 자고 싶었는데 반가워!", sample) == "낮잠 자고 싶었는데 반가워!" && NpcLineSlots.Slotify("밤바다는 오늘도 반짝여.", sample) == "밤바다는 오늘도 반짝여.",
                "'낮잠'·'밤바다'처럼 다른 낱말의 일부는 시간대로 보지 않음");
        Check(NpcLineSlots.Slotify("반가워!", sample) == "반가워!", "서버 값에 달린 말이 없으면 칸 없이 그대로");
        Check(NpcLineSlots.Slotify("낮 일찍 왔구나!", sample) == null && NpcLineSlots.Slotify("아침에 왔구나!", sample) == null, "시간대 뒤에 조사가 없거나 견본 시간대와 다르면 보관하지 않음");
        Check(NpcLineSlots.Slotify("어제도 왔네!", sample) == null && NpcLineSlots.Slotify("이틀 전에 닦아 줘서 고마워.", sample) == null && NpcLineSlots.Slotify("세 번이나 왔네!", sample) == null
                && NpcLineSlots.Slotify("5번이나 왔네!", sample) == null, "며칠 전·한글 숫자·견본이 아닌 횟수는 보관하지 않음");
        Check(NpcLineSlots.Slotify("도윤이 왔네!", new SlotValues { TimeWord = "낮", Address = "도윤" }) == null
                && NpcLineSlots.Slotify("도윤, 왔네!", new SlotValues { TimeWord = "낮", Address = "도윤" }) == "{호칭}, 왔네!", "조사가 바로 붙은 호칭은 보관하지 않음");

        var evening = new SlotValues { TimeWord = "저녁", Address = "도윤", VisitCount = 12 };
        Check(NpcLineSlots.Fill("{시간대}에 찾아와 줘서 고마워, {호칭}!", evening) == "저녁에 찾아와 줘서 고마워, 도윤!", "칸에 실제 시간대·호칭을 넣음");
        Check(NpcLineSlots.Fill("{호칭아}, 어서 와!", evening) == "도윤아, 어서 와!"
                && NpcLineSlots.Fill("{호칭아}, 어서 와!", new SlotValues { Address = "지우" }) == "지우야, 어서 와!"
                && NpcLineSlots.Fill("{호칭아}, 어서 와!", new SlotValues { Address = NpcAddress.Stranger }) == "발굴자, 어서 와!", "부르는 말은 받침에 맞게 아·야, '발굴자'는 그대로");
        Check(NpcLineSlots.Fill("{횟수}번이나 왔네!", evening) == "12번이나 왔네!", "방문 횟수를 넣음");
        Check(NpcLineSlots.Fill("{시간대}에 왔네!", new SlotValues { Address = "도윤" }) == null && NpcLineSlots.Fill("반가워!", new SlotValues()) == "반가워!",
                "넣을 값이 없는 칸이 있으면 null, 칸이 없는 대사는 그대로");
        Check(NpcLineSlots.SlotNames("{시간대}에 {호칭}, {횟수}번") == "시간대·호칭·횟수", "칸 이름");

        // 열쇠: 서버 값(시간대·닉네임·방문 횟수)이 달라도 같고, 어떤 말을 할지 정하는 사실(선명도)이 다르면 다르다
        Func<string, string, ReactionRequest> tapAt = (time, nick) =>
        {
            ReactionRequest r = Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
            r.Situation = "주인이 " + time + "에 너를 보러 찾아왔다.";
            r.Facts.Add(new Fact(NpcFactKeys.TimeOfDay, time));
            if (nick != null)
            {
                r.Nickname = nick;
                r.Intimacy = NpcAddress.NicknameLevel;
            }
            return r;
        };
        Check(NpcLineCache.TemplateKeyOf(tapAt("낮", null)) == NpcLineCache.TemplateKeyOf(tapAt("저녁", "도윤")), "칸 있는 대사의 열쇠는 시간대·닉네임이 달라도 같음");
        ReactionRequest otherClarity = tapAt("낮", null);
        otherClarity.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        Check(NpcLineCache.TemplateKeyOf(tapAt("낮", null)) != NpcLineCache.TemplateKeyOf(otherClarity) && NpcLineCache.KeyOf(tapAt("낮", null)) != NpcLineCache.KeyOf(tapAt("저녁", null)),
                "선명도가 다르면 열쇠가 다르고, 기존 열쇠는 시간대가 다르면 다름");

        // 요청 관리자: 낮·발굴자로 미리 만들고, 저녁·닉네임 요청이 칸을 채워 모델 없이 바로 받는다
        var backend = new FakeBackend();
        var slm = new SlmReactionGenerator(backend, assembler, validator);
        slm.LoadAsync(CancellationToken.None).Wait();
        var manager = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromSeconds(5), PrefetchIdleDelay = TimeSpan.Zero };
        backend.Replies.Enqueue(Out("greet", "player", "낮 일찍 왔구나!"));                       // 시간대 뒤에 조사가 없어 보관하지 않고 다시 만든다
        backend.Replies.Enqueue(Out("greet", "player", "낮에 찾아와 줘서 고마워, 발굴자!"));
        backend.Replies.Enqueue(Out("greet", "player", "낮에 날 불러줘서 반가워!"));
        manager.PrefetchTemplate(tapAt("낮", null), 2);
        Check(manager.PrefetchCompletion.Wait(5000), "칸 있는 대사 미리 만들기가 끝남");
        Check(manager.Cache.TemplateCount(tapAt("낮", null)) == 2 && backend.Calls == 3,
                "칸으로 만들 수 없는 대사는 버리고 다시 만들어 2개를 보관 ← " + manager.Cache.TemplateCount(tapAt("낮", null)) + "개, 모델 " + backend.Calls + "번");
        Check(manager.Cache.TemplateCount(tapAt("저녁", "도윤")) == 2, "다른 서버 값(저녁·도윤)의 요청도 같은 보관함을 봄");
        int callsBefore = backend.Calls;
        NpcResponse filled = Ask(manager, tapAt("저녁", "도윤"));
        Check(filled.ShouldShow && filled.Note == NpcRequestManager.PrefetchedNote && !filled.UsedFallback && backend.Calls == callsBefore
                && filled.Reaction.Line == "저녁에 찾아와 줘서 고마워, 도윤!" && (filled.Reaction.Note ?? "").Contains("칸 채움: 시간대·호칭"),
                "서버 값(저녁·도윤)이 오자마자 칸에 넣어 모델 없이 대사를 완성 ← " + filled.Reaction.Line + " / " + filled.Reaction.Note);
        NpcResponse second = Ask(manager, tapAt("아침", null));
        Check(second.Note == NpcRequestManager.PrefetchedNote && second.Reaction.Line == "아침에 날 불러줘서 반가워!" && backend.Calls == callsBefore, "다른 시간대·호칭 없음도 같은 보관함에서 완성 ← " + second.Reaction.Line);
        backend.Reply = Out("greet", "player", "오늘도 순찰 잘했지?");
        NpcResponse empty = Ask(manager, tapAt("낮", null));
        Check(empty.Note != NpcRequestManager.PrefetchedNote && backend.Calls == callsBefore + 1, "보관함이 비면 지금처럼 그 자리에서 만듦");

        // 선명도가 다른 요청은 칸 있는 대사를 쓰지 않는다 (어떤 말을 할지가 다르다)
        manager.Cache.Clear();
        backend.Replies.Enqueue(Out("greet", "player", "낮에 찾아와 줘서 고마워!"));
        manager.PrefetchTemplate(tapAt("낮", null), 1);
        Check(manager.PrefetchCompletion.Wait(5000), "다시 미리 만듦");
        ReactionRequest faded = tapAt("낮", null);
        faded.Facts.Add(new Fact(NpcFactKeys.Clarity, "흐려짐"));
        backend.Reply = Out("greet", "player", "와, 반가워!");
        Check(Ask(manager, faded).Note != NpcRequestManager.PrefetchedNote, "선명도가 다른 요청은 칸 있는 대사를 쓰지 않음");

        // 리뷰 반영: 칸을 채운 뒤의 길이·방문 기록, 보관함 칸 관리
        var reviewCache = new NpcLineCache();
        reviewCache.AddTemplate(tapAt("낮", null), ReactionResult.Success("r1", "greet", NpcTargets.Player, "발굴자, 어서 와 줘서 고마워!", ReactionSource.Slm, null));
        ReactionRequest longNick = tapAt("저녁", "가나다라마바사아자차카타파하가나다라마바사");
        Check(reviewCache.TryTakeTemplate(longNick, null, 20) == null && reviewCache.TemplateCount(longNick) == 1
                && reviewCache.TryTakeTemplate(longNick, null, 100) != null, "칸을 채운 뒤 길이가 제한을 넘으면 쓰지 않고 보관함에 둠 (발굴자→긴 닉네임)");
        Func<string, ReactionRequest> regularAt = days =>
        {
            ReactionRequest r = Request(8, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderRecall);
            r.Situation = "최근 30일 동안 7번 온 친구가 와 있다.";
            r.Facts.Add(new Fact(NpcFactKeys.VisitCount30d, "7"));
            r.Facts.Add(new Fact(NpcFactKeys.VisitDays, days));
            return r;
        };
        var visitCache = new NpcLineCache();
        visitCache.AddTemplate(regularAt("오늘, 20일 전"), ReactionResult.Success("r2", "wander_recall", NpcTargets.Player, "오랜만에 왔구나, 반가워!", ReactionSource.Slm, null));
        Check(visitCache.TryTakeTemplate(regularAt("오늘, 3일 전"), null, 100) == null && visitCache.TryTakeTemplate(regularAt("오늘, 20일 전"), null, 100) != null,
                "최근 방문한 손님에게 '오랜만에' 대사를 꺼내 쓰지 않음 (방문 기록 검사)");
        var bucketCache = new NpcLineCache();
        ReactionRequest bucketA = tapAt("낮", null);
        bucketCache.Count(bucketA);
        bucketCache.LinesFor(bucketA);
        bucketCache.TryTake(bucketA, null);
        bucketCache.TryTakeTemplate(bucketA, null);
        Check(bucketCache.BucketCount == 0, "읽기(Count·LinesFor·TryTake)만으로 보관함 칸을 만들지 않음 ← " + bucketCache.BucketCount);
        DateTime clock = DateTime.UtcNow;
        bucketCache.Now = () => clock;
        bucketCache.Add(bucketA, ReactionResult.Success("r3", "greet", NpcTargets.Player, "안녕, 반가워 정말!", ReactionSource.Slm, null));
        clock = clock.AddMinutes(31);
        ReactionRequest bucketB = tapAt("낮", null);
        bucketB.Situation = "다른 상황";
        bucketCache.Add(bucketB, ReactionResult.Success("r4", "greet", NpcTargets.Player, "어서 와, 오랜만이야 정말!", ReactionSource.Slm, null));
        Check(bucketCache.BucketCount == 1, "다시 부르지 않는 지난 칸은 다른 칸에 넣을 때 쓸려 나감 ← " + bucketCache.BucketCount);

        // 빌더가 만든 견본 요청: 서버 값 없이 낮·발굴자로 만들고, 서버 값이 온 뒤 만든 요청과 열쇠가 같다
        NpcContextInput ownerState = Owner(8, NpcInputs.Tap, "FADED", 9);
        List<ReactionRequest> templates = builder.PrefetchTemplates(ownerState);
        Check(templates.Count == 5 && templates[0].FindFact(NpcFactKeys.TimeOfDay) == "낮" && templates[0].Nickname == null && templates[0].AddressName == NpcAddress.Stranger
                && templates.Count(r => r.Input == NpcInputs.Care) == 3,
                "주인: 선명도를 알면 탭 인사·안부와 정화 세 상태(선명·흐려짐·재봉인), 5개를 견본(낮·발굴자)으로 만듦 ← " + templates.Count);
        NpcContextInput realState = Owner(8, NpcInputs.Tap, "FADED", 9);
        realState.Now = Today.AddHours(20);
        realState.Nickname = "도윤";
        realState.VisitDates = new List<DateTime>();
        for (int d = 0; d < 12; d++)
        {
            realState.VisitDates.Add(Today.AddDays(-d));
        }
        Check(NpcLineCache.TemplateKeyOf(templates[0]) == NpcLineCache.TemplateKeyOf(builder.Build(realState).Request), "서버 값이 온 뒤 빌더가 만든 요청은 견본과 같은 열쇠");
        Check(builder.PrefetchTemplates(Owner(8, NpcInputs.Tap, null, null)).Count == 1, "선명도를 모르면 탭 인사만");
        List<ReactionRequest> guestTemplates = builder.PrefetchTemplates(Guest(8, NpcInputs.Visit, null));
        Check(guestTemplates.Count == 2 && guestTemplates[0].Candidates[0].ActionId == NpcActions.WanderGreet && guestTemplates[1].Candidates[0].ActionId == NpcActions.WanderRecall,
                "손님: 처음 온 친구(인사)와 단골(알아보기) 견본 둘 ← " + guestTemplates.Count);
    }

    private static void CheckSpeechFix(string expectedPath)
    {
        Console.WriteLine("=== 말투 다듬기 ===");
        var expected = (Dictionary<string, object>)Json.DeserializeObject(ReadText(expectedPath));
        object raw;
        if (!expected.TryGetValue("_speechSamples", out raw))
        {
            Check(false, "기준 json 에 말투 다듬기 예문(_speechSamples)이 없음");
            return;
        }
        int same = 0;
        int total = 0;
        foreach (Dictionary<string, object> sample in ((IEnumerable)raw).Cast<Dictionary<string, object>>())
        {
            total++;
            string label = (string)sample["label"], line = (string)sample["line"], want = (string)sample["want"];
            string call = sample.ContainsKey("call") ? (string)sample["call"] : null;
            bool noRecord = sample.ContainsKey("noRecord");
            string got = sample.ContainsKey("oneSentence") ? NpcLineChecks.KeepOneSentence(line, (bool)sample["needsAsk"]) : noRecord ? NpcLineChecks.NormalizeSpeech(line, label, null, null, null, null, "발굴자", true)
                    : call == null ? NpcLineChecks.NormalizeSpeech(line, label) : NpcLineChecks.NormalizeSpeech(line, label, null, null, null, null, call);
            if (got == want)
            {
                same++;
            }
            else
            {
                Check(false, "[" + label + "] " + line + "\n      run.py: " + want + "\n      C#    : " + got);
            }
        }
        Check(total > 0 && same == total, "run.py 와 같은 결과: " + same + "/" + total);
        Console.WriteLine("run.py 와 같은 말투 다듬기: " + same + "/" + total);

        // 규칙 함수(인사 꼬리·부탁 꼴·플레이어 이름 지움·자기 솜씨·끊긴 말·남 얘기 이름·뒤바뀐 주체·인사 사정)도 run.py 와 같은 결과인지
        object fnRaw;
        if (!expected.TryGetValue("_fnSamples", out fnRaw))
        {
            Check(false, "기준 json 에 규칙 함수 예문(_fnSamples)이 없음");
            return;
        }
        int fnSame = 0;
        int fnTotal = 0;
        var ownThings = new List<string> { "자개", "옻칠", "몸" };
        foreach (Dictionary<string, object> sample in ((IEnumerable)fnRaw).Cast<Dictionary<string, object>>())
        {
            fnTotal++;
            string fn = (string)sample["fn"], line = (string)sample["line"], arg = (string)sample["arg"], want = (string)sample["want"], got;
            switch (fn)
            {
                case "greetingTail": got = NpcLineChecks.FixGreetingTail(line); break;
                case "cleanAsk": got = NpcLineChecks.IsCleanAsk(line) ? "true" : "false"; break;
                case "dropPlayer": got = NpcLineChecks.DropPlayerReference(line, arg); break;
                case "craft": got = NpcLineChecks.FixCraftSpectator(NpcLineChecks.FixOwnPossessive(line, ownThings)); break;
                case "incomplete": got = NpcLineChecks.IncompleteEnding(line, arg.Length == 0 ? null : arg) ?? ""; break;
                case "thirdPerson": got = NpcLineChecks.PlayerThirdPerson(line, arg) ?? ""; break;
                case "reversed": got = NpcLineChecks.ReversedCall(line, arg) ?? ""; break;
                case "probe": got = NpcLineChecks.GreetingProbe(line, arg) ?? ""; break;
                case "subject": got = NpcLineChecks.SubjectContradiction(line, ownThings) ?? ""; break;
                case "narrationKept": got = NpcLineChecks.NarrationKept(line, arg) ? "true" : "false"; break;
                case "craftPraise": got = NpcLineChecks.CraftPraise(line, arg) ?? ""; break;
                case "bareOwn": got = NpcLineChecks.FixBareOwnThing(line, new List<string> { "자개", "옻칠", "몸", "무늬", "반짝임", "빛", "모습" }); break;
                case "recallFragment": got = NpcLineChecks.FixRecallFragment(line); break;
                case "pronouns": got = NpcLineChecks.DropPlayerReference(NpcLineChecks.PlayerPronounsToName(line, arg), arg); break;
                default: got = "(알 수 없는 함수 " + fn + ")"; break;
            }
            if (got == want)
            {
                fnSame++;
            }
            else
            {
                Check(false, "[" + fn + "] " + line + " (" + arg + ")\n      run.py: " + want + "\n      C#    : " + got);
            }
        }
        Check(fnTotal > 0 && fnSame == fnTotal, "규칙 함수가 run.py 와 같은 결과: " + fnSame + "/" + fnTotal);
        Console.WriteLine("run.py 와 같은 규칙 함수: " + fnSame + "/" + fnTotal);

        // 뜻대로 고치는지 (run.py 와 같아도 둘 다 틀릴 수 있다)
        string[][] cases =
        {
            new[] { "반말", "관리해 주셔서 정말 고마워요!", "관리해 줘서 정말 고마워!" },
            new[] { "반말", "주인아, 어디 가요?", "어디 가?" },
            new[] { "반말", "이틀 전 닦아주신 덕분에 선명해졌어요.", "이틀 전 닦아준 덕분에 선명해졌어." },
            new[] { "반말", "오늘은 순찰 좀 같이 가실래요?", "오늘은 순찰 좀 같이 갈래?" },
            new[] { "반말", "네가 정말 공 들이셨구나.", "네가 정말 공 들였구나." },
            new[] { "반말", "좋은 곳에 오셨구나, 인사드려요.", "좋은 곳에 왔구나, 인사해." },
            new[] { "반말", "부탁드려요, 조심해 주세요~", "부탁해, 조심해 줘~" },
            new[] { "반말", "혹시 한 번 더 닦아주시면 좋겠어요.", "혹시 한 번 더 닦아줘." },
            new[] { "반말", "안녕하세요, 순찰 나왔습니다!", "안녕, 순찰 나왔어!" },
            new[] { "반말", "주인님, 고마워! 오늘도 반가워, 주인님.", "고마워! 오늘도 반가워." },
            new[] { "반말", "응, 주인님이 왔네! 손님, 구경해.", "응, 네가 왔네! 친구, 구경해." },
            new[] { "반말", "집주인이 왔어. 주인공이야.", "집주인이 왔어. 주인공이야." },
            new[] { "반말", "손님께서 오셨네요. 손님께 인사할게요.", "친구가 왔네. 친구한테 인사할게." },
            new[] { "반말", "좋은 하루 보내세요.", "좋은 하루 보내." },
            new[] { "반말", "닦아달라고 부탁해.", "닦아 줘." },
            new[] { "반말", "한 번 더 닦아 달라고 부탁드려요.", "한 번 더 닦아 줘." },
            new[] { "반말", "어서 왔어!", "어서 와!" },
            new[] { "반말", "한 번 더 닦아 달라고 부탁했다.", "한 번 더 닦아 줘." },
            new[] { "반말", "닦아 달라고 말했어.", "닦아 줘." },
            new[] { "반말", "닦아 달라고 부탁드립니다.", "닦아 줘." },
            new[] { "반말", "나는 친구입니다.", "나는 친구야." },
            new[] { "반말", "이건 책입니다.", "이건 책이야." },
            new[] { "반말", "손님이 왔어. 손님은 좋아. 손님을 반겨. 손님과 놀자.", "친구가 왔어. 친구는 좋아. 친구를 반겨. 친구와 놀자." },
            new[] { "반말", "손님이라서 왔어. 손님이야.", "친구라서 왔어. 친구야." },
            new[] { "반말", "다시는 안 늦을게. 같이 놀자!", "다시는 안 늦을게. 같이 놀자!" },
            new[] { "반말", "마시고 가. 도시는 커.", "마시고 가. 도시는 커." },
            new[] { "반말", "안녕하신가, 주신다고 했어.", "안녕하신가, 주신다고 했어." },
            new[] { "반말", "주신 덕분에 좋아. 오신 분은 누구야?", "준 덕분에 좋아. 온 분은 누구야?" },
            new[] { "반말", "내려주셔도 돼요.", "내려줘도 돼." },
            new[] { "반말", "고마워, 관리해줘서 고마워", "관리해줘서 고마워" },
            new[] { "반말", "고마워! 관리해줘서 고마워~", "관리해줘서 고마워~" },
            new[] { "반말", "관리해줘서 고마워, 고마워!", "관리해줘서 고마워." },
            new[] { "반말", "안녕하십네다! 반가워.", "안녕! 반가워." },
            new[] { "반말", "필요해요? 중요한 거야.", "필요해? 중요한 거야." },
            new[] { "반말", "주인아저씨, 요즘 어때?", "주인아저씨, 요즘 어때?" },
            new[] { "하오체", "안녕하옵니까, 주인님.", "안녕하시오." },
            new[] { "하오체", "성실하게 모시겠습니다.", "성실하게 모시겠소." },
            new[] { "하오체", "어서 오세요. 수행원입니다.", "어서 오시오. 수행원이오." },
            new[] { "하게체", "관리해 주셔서 감사합니다.", "관리해 줘서 감사하네." },
            new[] { "하게체", "좋은 하루 보내세요, 또 오셨습니까?", "좋은 하루 보내시게, 또 오셨는가?" },
            new[] { "해요체", "고마워요, 닦아 주셔서 감사합니다.", "고마워요, 닦아 주셔서 감사합니다." }
        };
        foreach (string[] c in cases)
        {
            string got = NpcLineChecks.NormalizeSpeech(c[1], c[0]);
            Check(got == c[2], "[" + c[0] + "] " + c[1] + " → " + c[2] + (got == c[2] ? "" : " ← " + got));
        }
        Check(NpcLineChecks.NormalizeSpeech(null, "반말") == null && NpcLineChecks.NormalizeSpeech("고마워요", null) == "고마워요",
                "빈 대사·어미 규칙 없는 도깨비는 그대로");
    }

    private static void CheckManager(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 요청 관리자 ===");
        var rule = new RuleBasedReactionGenerator(lines, new Random(7));
        var backend = new FakeBackend();
        var slm = new SlmReactionGenerator(backend, new PromptAssembler(personas, actions),
                new NpcOutputValidator(new NpcLineFilter(forbidden)));
        // 같은 답을 거듭 돌려주는 시험이라 최근 대사 기억은 끈다 (같은 대사 막기는 CheckEnforcement 에서 본다)
        var manager = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromMilliseconds(300), RecentLineCount = 0 };
        string hello = Out("greet", "player", "어흠, 오셨구려.");

        // 기본 대사로 대신하는 경우
        NpcResponse r = Ask(manager, Tap(8));
        Check(r.ShouldShow && r.UsedFallback && r.SlmAttempt == null && backend.Calls == 0 && r.Note.Contains("준비"),
                "모델이 안 올라왔으면 SLM 을 부르지 않고 기본 대사 ← " + r.Note);
        slm.LoadAsync(CancellationToken.None).Wait();

        backend.Reply = hello;
        ReactionRequest request = Tap(8);
        r = Ask(manager, request);
        Check(r.ShouldShow && !r.UsedFallback && r.Reaction.Source == ReactionSource.Slm && r.Reaction.Line == "어흠, 오셨구려.",
                "SLM 이 검사를 통과하면 그 대사 ← " + r.Note);
        Check(!string.IsNullOrEmpty(request.RequestId) && r.Reaction.RequestId == request.RequestId
                && !string.IsNullOrEmpty(request.InteractionId), "RequestId 와 InteractionId 를 붙임");
        ReactionRequest keep = Tap(8);
        keep.InteractionId = "tap-1";
        Ask(manager, keep);
        Check(keep.InteractionId == "tap-1", "부르는 쪽이 정한 InteractionId 는 그대로");

        manager.SlmEnabled = false;
        int calls = backend.Calls;
        r = Ask(manager, Tap(8));
        Check(r.UsedFallback && r.SlmAttempt == null && backend.Calls == calls && r.Note.Contains("꺼짐"),
                "SLM 을 끈 기기(저사양)는 처음부터 기본 대사 ← " + r.Note);
        manager.SlmEnabled = true;
        r = Ask(new NpcRequestManager(null, rule), Tap(8));
        Check(r.UsedFallback && r.SlmAttempt == null, "모델 없는 빌드(SLM 생성기 null)도 기본 대사");

        backend.Reply = Out("greet", "player", "……손대지 마.");
        r = Ask(manager, Tap(8));
        Check(r.UsedFallback && r.SlmAttempt.FailReason == FailReason.InvalidOutput && r.Note.Contains("Forbidden"),
                "출력 검사에 걸리면 기본 대사 ← " + r.Note);
        backend.Reply = hello;
        r = Ask(manager, Tap(1));
        Check(r.UsedFallback && r.SlmAttempt.FailReason == FailReason.Unsupported
                && lines.fallback.FindLines(NpcActions.Greet, "").Contains(r.Reaction.Line),
                "페르소나 없는 도깨비는 기본(fallback) 대사 ← " + r.Reaction.Line);
        backend.Throw = new InvalidOperationException("런타임 오류");
        r = Ask(manager, Tap(8));
        Check(r.UsedFallback && r.SlmAttempt.FailReason == FailReason.Error && r.Note.Contains("런타임 오류"), "런타임 오류면 기본 대사");
        backend.Throw = null;
        r = Ask(new NpcRequestManager(new ThrowingGenerator(), rule), Tap(8));
        Check(r.UsedFallback && r.SlmAttempt.FailReason == FailReason.Error, "생성기가 예외를 던져도 기본 대사 (요청 관리자는 예외를 던지지 않음)");
        // 상태(IsReady)를 읽다 예외가 나도: 정해진 대사·대사 없음은 상태를 읽지 않고 그대로, SLM 을 쓸 요청은 기본 대사, 미리 읽기는 그냥 끝남
        var readyThrows = new ReadyThrowsGenerator();
        var readyThrowsManager = new NpcRequestManager(readyThrows, rule);
        ReactionRequest storyWhenReadyThrows = Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory);
        storyWhenReadyThrows.Facts.Add(new Fact(NpcFactKeys.StoryIndex, "1"));
        // 요청 관리자가 예외를 던지면 시험 전체가 멈추지 않고 이 검사가 틀렸다고 적도록 Ok 로 받는다
        Task<NpcResponse> askStory = readyThrowsManager.RequestAsync(storyWhenReadyThrows);
        r = Ok(askStory, 3000) ? askStory.Result : null;
        Check(r != null && r.ShouldShow && r.Note == NpcRequestManager.FixedLinesNote,
                "상태를 못 읽는 생성기라도 이야기(정해진 대사)는 그대로 ← " + (r != null ? r.Note : "예외·시간 초과"));
        Task<NpcResponse> askTap = readyThrowsManager.RequestAsync(Tap(8));
        r = Ok(askTap, 3000) ? askTap.Result : null;
        Check(r != null && r.ShouldShow && r.UsedFallback && r.Note.Contains("준비되지 않음") && readyThrows.Generated == 0,
                "상태를 못 읽는 생성기면 모델을 부르지 않고 기본 대사 (예외를 던지지 않음) ← " + (r != null ? r.Note : "예외·시간 초과"));
        Task readyThrowsPrepare = readyThrowsManager.PrepareAsync(8);
        Check(Ok(readyThrowsPrepare, 1000), "상태를 못 읽는 생성기라도 PrepareAsync 는 예외 없이 끝남");

        // 이야기는 SLM 을 부르지 않고 정해진 이야기 대사를 차례로
        calls = backend.Calls;
        ReactionRequest story = Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory);
        story.Facts.Add(new Fact(NpcFactKeys.StoryIndex, "1"));
        r = Ask(manager, story);
        Check(r.ShouldShow && r.Reaction.Source == ReactionSource.Rule && r.SlmAttempt == null && backend.Calls == calls
                && r.Note == NpcRequestManager.FixedLinesNote && r.Reaction.Line == lines.Find(8).stories[1],
                "이야기는 SLM 을 부르지 않고 story_index 번째 이야기 대사 ← " + r.Note);

        // 길어서 잘라 낸 SLM 대사는 Note 에 적힌다
        backend.Reply = Out("greet", "player", "어흠, 오셨구려. 오늘 밤 순찰도 이상 없었으니 걱정 마시고 편히 쉬시되 문단속은 꼭 하시구려. 이 몸이 지키겠소.");
        r = Ask(manager, Tap(8));
        Check(r.ShouldShow && !r.UsedFallback && r.Reaction.Line == "어흠, 오셨구려." && (r.Note ?? "").StartsWith("SLM (긴 대사를"),
                "긴 SLM 대사는 버리지 않고 앞 문장만 ← " + r.Note);
        backend.Reply = hello;

        // 시간 초과
        string late = hello;
        backend.Delayed = c => Task.Delay(2000).ContinueWith(_ => late);   // 취소를 무시하는 런타임
        Stopwatch watch = Stopwatch.StartNew();
        r = Ask(manager, Tap(8));
        watch.Stop();
        Check(r.UsedFallback && r.SlmAttempt.FailReason == FailReason.Cancelled && r.Note.Contains("시간 초과"),
                "시간 초과면 기본 대사 ← " + r.Note);
        Check(watch.ElapsedMilliseconds < 1500, "런타임이 취소를 무시해도 시간 초과 뒤 기다리지 않음 ← " + watch.ElapsedMilliseconds + "ms");
        backend.Delayed = async c =>
        {
            await Task.Delay(2000, c);
            return late;
        };
        r = Ask(manager, Tap(8));
        Check(r.UsedFallback && r.Note.Contains("시간 초과"), "취소에 반응하는 런타임도 시간 초과면 기본 대사");

        // 아무것도 띄우지 않는 경우
        var screen = new CancellationTokenSource();
        screen.CancelAfter(100);
        r = manager.RequestAsync(Tap(8), screen.Token).Result;
        Check(!r.ShouldShow && r.Note.Contains("부르는 쪽"), "SLM 을 기다리는 중 화면을 닫으면 기본 대사 없이 버림 ← " + r.Note);
        var closed = new CancellationTokenSource();
        closed.Cancel();
        calls = backend.Calls;
        r = manager.RequestAsync(Tap(8), closed.Token).Result;
        Check(!r.ShouldShow && backend.Calls == calls, "이미 취소된 요청은 모델을 부르지 않고 버림");

        int count = 0;
        backend.Delayed = c => Interlocked.Increment(ref count) == 1
                ? Task.Delay(1000).ContinueWith(_ => late)
                : Task.FromResult(late);
        Task<NpcResponse> older = manager.RequestAsync(Tap(8));
        Task<NpcResponse> newer = manager.RequestAsync(Tap(8));
        Check(!older.Result.ShouldShow && older.Result.Note.Contains("새 요청"), "같은 도깨비에게 새 요청이 오면 옛 요청은 버림 ← " + older.Result.Note);
        Check(newer.Result.ShouldShow && newer.Result.Reaction.Source == ReactionSource.Slm, "새 요청은 그대로 띄움");

        backend.Delayed = c => Task.Delay(1000).ContinueWith(_ => late);
        Task<NpcResponse> pending = manager.RequestAsync(Tap(8));
        manager.Cancel(8);
        Check(!pending.Result.ShouldShow && pending.Result.Note.Contains("전환"), "NPC 전환(Cancel)이면 버림 ← " + pending.Result.Note);
        Task<NpcResponse> first = manager.RequestAsync(Tap(8));
        Task<NpcResponse> second = manager.RequestAsync(Tap(9));
        manager.CancelAll();
        Check(!first.Result.ShouldShow && !second.Result.ShouldShow, "CancelAll(화면 종료)이면 모두 버림");
        manager.Cancel(5);
        Check(true, "진행 중인 요청이 없는 도깨비를 Cancel 해도 괜찮음");

        string both = Out("greet", "player", "반가워!");
        backend.Delayed = c => Task.Delay(50).ContinueWith(_ => both);
        Task<NpcResponse> eight = manager.RequestAsync(Tap(8));
        Task<NpcResponse> nine = manager.RequestAsync(Tap(9));
        Check(eight.Result.ShouldShow && nine.Result.ShouldShow && !eight.Result.UsedFallback && !nine.Result.UsedFallback,
                "다른 도깨비끼리는 서로 밀어내지 않음 (손님 관람 말풍선)");
        backend.Delayed = null;

        // 시스템 프롬프트 미리 읽기 (탭해 확대할 때)
        string horseSystem = new PromptAssembler(personas, actions).SystemFor(8);
        backend.Prepared.Clear();
        Check(manager.PrepareAsync(8).Wait(1000) && backend.Prepared.Count == 1 && backend.Prepared[0] == horseSystem,
                "PrepareAsync: 그 도깨비의 시스템 메시지를 모델에 미리 읽힘");
        Ask(manager, Tap(8));
        Check(backend.LastPrompt.System == horseSystem, "이어지는 요청의 시스템 메시지가 미리 읽힌 것과 글자 하나까지 같음");
        Check(manager.PrepareAsync(1).Wait(1000) && backend.Prepared.Count == 1, "페르소나 없는 도깨비는 미리 읽지 않음");
        manager.SlmEnabled = false;
        Check(manager.PrepareAsync(8).Wait(1000) && backend.Prepared.Count == 1, "SLM 을 끈 기기는 미리 읽지 않음");
        manager.SlmEnabled = true;
        Check(new NpcRequestManager(null, rule).PrepareAsync(8).Wait(1000), "모델 없는 빌드도 PrepareAsync 는 바로 끝남");

        backend.PrepareDelay = TimeSpan.FromSeconds(3);
        int cancelled = backend.PrepareCancelled;
        Task prepare = manager.PrepareAsync(8);
        Ask(manager, Tap(9));
        Check(prepare.Wait(1000) && backend.PrepareCancelled == cancelled + 1, "다른 도깨비의 요청이 오면 미리 읽기를 멈춤");
        prepare = manager.PrepareAsync(5);
        Task<NpcResponse> sameSpirit = manager.RequestAsync(Tap(5));
        Check(sameSpirit.Wait(1000) && !prepare.IsCompleted, "같은 도깨비의 요청은 미리 읽기를 멈추지 않음");
        Task other = manager.PrepareAsync(6);
        Check(prepare.Wait(1000) && backend.PrepareCancelled == cancelled + 2, "다른 도깨비를 미리 읽으면 앞의 것을 멈춤");
        manager.Cancel(6);
        Check(other.Wait(1000) && backend.PrepareCancelled == cancelled + 3, "NPC 전환(Cancel)이면 그 도깨비의 미리 읽기를 멈춤");
        prepare = manager.PrepareAsync(9);
        manager.CancelAll();
        Check(prepare.Wait(1000) && backend.PrepareCancelled == cancelled + 4, "CancelAll 이면 미리 읽기도 멈춤");
        backend.PrepareDelay = TimeSpan.Zero;
        backend.PrepareThrow = new InvalidOperationException("모델 오류");
        prepare = manager.PrepareAsync(8);
        Check(prepare.Wait(1000) && prepare.Status == TaskStatus.RanToCompletion, "미리 읽기가 실패해도 예외를 던지지 않음");
        backend.PrepareThrow = null;

        calls = backend.Calls;
        r = Ask(manager, Request(8, InteractionMode.Owner, NpcInputs.Tap));
        Check(!r.ShouldShow && backend.Calls == calls && r.Note.Contains("후보"), "후보가 없으면 아무 생성기도 부르지 않고 버림");
        bool threw = false;
        try
        {
            manager.RequestAsync(null);
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }
        Check(threw, "요청이 null 이면 바로 ArgumentNullException");
    }

    // ---------- LM Studio 백엔드 ----------

    private static void CheckLmStudio(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== LM Studio 백엔드 (가짜 서버) ===");
        string models = "{\"data\":[{\"id\":\"google/gemma-4-e2b\"},{\"id\":\"kanana-2-3b-instruct-i1\"}],\"object\":\"list\"}";
        string reply = "{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠, 오셨구려.\"}";
        var server = new FakeHttp();
        server.Respond = (path, body, ct) => Task.FromResult(path == "/v1/models" ? Http(HttpStatusCode.OK, models) : Http(HttpStatusCode.OK, Chat(reply, 0)));
        var backend = new LmStudioModelBackend(new LmStudioOptions { BaseUrl = "http://test:1234/" }, server);
        var assembler = new PromptAssembler(personas, actions);
        NpcPrompt prompt = assembler.Build(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));

        Check(!backend.IsLoaded, "처음에는 IsLoaded false");
        bool refused = false;
        try
        {
            backend.CompleteAsync(prompt, CancellationToken.None).Wait();
        }
        catch (AggregateException e)
        {
            refused = e.GetBaseException() is InvalidOperationException;
        }
        Check(refused, "올리기 전에 부르면 InvalidOperationException");

        backend.LoadAsync(CancellationToken.None).Wait();
        Check(backend.IsLoaded, "모델이 목록에 있으면 IsLoaded");
        Check(server.Paths.Count == 2 && server.Paths[0] == "/v1/models" && server.Paths[1] == "/v1/chat/completions",
                "LoadAsync 는 모델 목록 확인 + 짧은 호출로 모델을 올림 ← " + string.Join(", ", server.Paths.ToArray()));
        var warm = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[1]);
        Check(Convert.ToInt32(warm["max_tokens"]) == 8 && !warm.ContainsKey("response_format"), "올리기 호출은 짧게, 형식 제약 없이");

        string raw = backend.CompleteAsync(prompt, CancellationToken.None).Result;
        Check(raw == reply, "모델이 낸 글을 고치지 않고 돌려줌");
        var sent = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[server.Bodies.Count - 1]);
        var messages = ((IEnumerable)sent["messages"]).Cast<Dictionary<string, object>>().ToList();
        Check((string)sent["model"] == "google/gemma-4-e2b" && (bool)sent["stream"] == false, "모델 이름, stream false");
        Check(messages.Count == 2 && (string)messages[0]["role"] == "system" && (string)messages[0]["content"] == prompt.System
                && (string)messages[1]["role"] == "user" && (string)messages[1]["content"] == prompt.User,
                "시스템·사용자 메시지가 프롬프트와 글자 하나까지 같음 (따옴표·줄바꿈 이스케이프)");
        Check(Convert.ToDouble(sent["temperature"]) == 0.7 && Convert.ToInt32(sent["max_tokens"]) == 40
                && (string)sent["reasoning_effort"] == "none", "생성 설정이 시험과 같음 (온도 0.7, 40토큰, 생각 끔)");
        var format = (Dictionary<string, object>)sent["response_format"];
        var schemaSent = ((Dictionary<string, object>)format["json_schema"])["schema"];
        Check((string)format["type"] == "json_schema"
                && Json.Serialize(schemaSent) == Json.Serialize(Json.DeserializeObject(prompt.JsonSchema)), "JSON 스키마 형식 제약을 넘김");
        Check(!sent.ContainsKey("seed"), "seed 는 정했을 때만");
        Check(backend.LastCall != null && backend.LastCall.PromptTokens == 321 && backend.LastCall.CompletionTokens == 21
                && backend.LastCall.ReasoningTokens == 0, "토큰 수를 LastCall 에 남김");

        LmStudioCall completed = backend.LastCall;
        backend.PrepareAsync(prompt.System, CancellationToken.None).Wait();
        var primed = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[server.Bodies.Count - 1]);
        var primedMessages = ((IEnumerable)primed["messages"]).Cast<Dictionary<string, object>>().ToList();
        Check((string)primedMessages[0]["content"] == prompt.System && (string)primedMessages[1]["content"] == NpcPrompt.PrepareUser
                && Convert.ToInt32(primed["max_tokens"]) == 1 && !primed.ContainsKey("response_format"),
                "PrepareAsync: 같은 시스템 메시지로 1토큰만, 형식 제약 없이");
        Check(backend.LastCall == completed && backend.LastPrepare != null, "미리 읽기는 LastCall 을 덮지 않고 LastPrepare 에 남김");

        var quiet = new LmStudioModelBackend(new LmStudioOptions { UseJsonSchema = false, DisableThinking = false, Seed = 7, WarmUp = false }, server);
        quiet.LoadAsync(CancellationToken.None).Wait();
        quiet.CompleteAsync(prompt, CancellationToken.None).Wait();
        var plain = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[server.Bodies.Count - 1]);
        Check(!plain.ContainsKey("response_format") && !plain.ContainsKey("reasoning_effort") && Convert.ToInt32(plain["seed"]) == 7,
                "설정으로 형식 제약·생각 끄기를 빼고 seed 를 넣을 수 있음");

        // 채팅 틀이 시스템 메시지를 빼먹는 모델(EXAONE 4.0): 사용자 메시지 앞에 붙여 한 메시지로 보낸다
        var merged = new LmStudioModelBackend(new LmStudioOptions { SystemInUser = true, WarmUp = false }, server);
        merged.LoadAsync(CancellationToken.None).Wait();
        merged.CompleteAsync(prompt, CancellationToken.None).Wait();
        var one = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[server.Bodies.Count - 1]);
        var oneMessages = ((IEnumerable)one["messages"]).Cast<Dictionary<string, object>>().ToList();
        Check(oneMessages.Count == 1 && (string)oneMessages[0]["role"] == "user"
                && (string)oneMessages[0]["content"] == prompt.System + "\n\n" + prompt.User,
                "SystemInUser: 시스템 메시지를 사용자 메시지 앞에 붙여 사용자 메시지 하나로 보냄");
        merged.PrepareAsync(prompt.System, CancellationToken.None).Wait();
        var mergedPrime = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[server.Bodies.Count - 1]);
        string primeText = (string)((IEnumerable)mergedPrime["messages"]).Cast<Dictionary<string, object>>().Single()["content"];
        Check(primeText == prompt.System + "\n\n" + NpcPrompt.PrepareUser && (prompt.System + "\n\n" + prompt.User).StartsWith(prompt.System + "\n\n"),
                "SystemInUser: 미리 읽기도 같은 앞부분(시스템 메시지 + 빈 줄)으로 보냄");
        merged.Dispose();
        Check(new LmStudioOptions().BaseUrl == "http://127.0.0.1:1234",
                "기본 주소는 127.0.0.1 (localhost 는 Windows 에서 IPv6 를 먼저 시도해 요청마다 약 2초 늦음)");

        // 오류
        Check(LoadError(new LmStudioOptions { Model = "없는-모델" }, server).Contains("kanana-2-3b-instruct-i1"),
                "모델이 없으면 있는 모델 이름을 알려 줌");
        var down = new FakeHttp();
        down.Respond = (path, body, ct) => { throw new HttpRequestException("연결 거부"); };
        Check(LoadError(new LmStudioOptions(), down).Contains("Developer 탭"), "서버가 꺼져 있으면 켜라고 알려 줌");
        server.Respond = (path, body, ct) => Task.FromResult(path == "/v1/models" ? Http(HttpStatusCode.OK, models)
                : Http(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"\"}}],\"usage\":{\"completion_tokens_details\":{\"reasoning_tokens\":160}}}"));
        Check(CompleteError(backend, prompt).Contains("생각에 160토큰"), "대사 없이 생각만 했으면 그렇게 알려 줌");
        server.Respond = (path, body, ct) => Task.FromResult(Http(HttpStatusCode.BadRequest, "{\"error\":\"model not loaded\"}"));
        Check(CompleteError(backend, prompt).Contains("HTTP 400"), "HTTP 오류는 상태 코드와 함께");
        server.Respond = (path, body, ct) => Task.FromResult(Http(HttpStatusCode.OK, "<html>"));
        Check(CompleteError(backend, prompt).Contains("읽지 못했다"), "JSON 이 아닌 응답");

        // 취소와 한 번에 하나씩
        server.Respond = async (path, body, ct) =>
        {
            await Task.Delay(5000, ct);
            return Http(HttpStatusCode.OK, Chat(reply, 0));
        };
        var cancel = new CancellationTokenSource();
        cancel.CancelAfter(100);
        Check(CompleteError(backend, prompt, cancel.Token) == "OperationCanceledException", "취소되면 OperationCanceledException");
        Task<string> during = backend.CompleteAsync(prompt, CancellationToken.None);
        Thread.Sleep(100);
        backend.Unload();
        Check(!backend.IsLoaded && CompleteErrorOf(during) == "OperationCanceledException", "Unload 하면 진행 중인 요청도 취소");

        server.Respond = async (path, body, ct) =>
        {
            await Task.Delay(100);
            return Http(HttpStatusCode.OK, path == "/v1/models" ? models : Chat(reply, 0));
        };
        backend.LoadAsync(CancellationToken.None).Wait();
        server.MaxActive = 0;
        Task.WaitAll(backend.CompleteAsync(prompt, CancellationToken.None), backend.CompleteAsync(prompt, CancellationToken.None),
                backend.CompleteAsync(prompt, CancellationToken.None));
        Check(server.MaxActive == 1, "동시에 불러도 서버에는 한 번에 하나씩 보냄 ← 최대 동시 " + server.MaxActive);

        // 요청 관리자까지 한 줄로
        server.Respond = (path, body, ct) => Task.FromResult(Http(HttpStatusCode.OK, Chat(reply, 0)));
        var manager = new NpcRequestManager(new SlmReactionGenerator(backend, assembler, new NpcOutputValidator(new NpcLineFilter(forbidden))),
                new RuleBasedReactionGenerator(lines, new Random(7)));
        NpcResponse response = Ask(manager, Tap(8));
        Check(response.ShouldShow && response.Reaction.Source == ReactionSource.Slm && response.Reaction.Line == "어흠, 오셨구려.",
                "요청 관리자 → SLM 생성기 → LM Studio 백엔드로 이어짐");
        backend.Dispose();
    }

    // 실제 LM Studio 로 장면을 돌린다. 모델 출력은 매번 달라서 장면별 결과를 보여 주기만 하고, 연결과 흐름만 검사한다
    private static void CheckLive(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines,
                                  List<Dictionary<string, object>> scenes, Dictionary<string, string> labels, string url, string model)
    {
        Console.WriteLine("=== 실제 LM Studio (" + url + ", " + model + ") ===");
        var backend = new LmStudioModelBackend(new LmStudioOptions { BaseUrl = url, Model = model });
        Stopwatch loading = Stopwatch.StartNew();
        try
        {
            backend.LoadAsync(CancellationToken.None).Wait();
        }
        catch (AggregateException e)
        {
            Check(false, "LM Studio 에 연결하지 못함: " + e.GetBaseException().Message);
            return;
        }
        Console.WriteLine("모델 올림 " + loading.ElapsedMilliseconds + "ms");
        var assembler = new PromptAssembler(personas, actions, new PromptOptions { TargetLabels = labels });
        // 게임처럼 페르소나를 넘겨 자기 이름·어미 섞기·반복을 거른다
        var manager = new NpcRequestManager(new SlmReactionGenerator(backend, assembler,
                new NpcOutputValidator(new NpcLineFilter(forbidden), personas)),
                new RuleBasedReactionGenerator(lines, new Random(7)))
        {
            SlmTimeout = TimeSpan.FromSeconds(60)   // PC 시험용. 게임의 값은 실기기 측정으로 정한다
        };
        int slm = 0;
        int fixedLines = 0;
        var times = new List<long>();
        foreach (Dictionary<string, object> scene in scenes)
        {
            ReactionRequest request = ToRequest(scene);
            // 게임에서 탭해 확대할 때처럼 시스템 메시지를 먼저 읽힌다
            // 이야기 듣기는 정해진 대사라 모델을 부르지 않는다. 미리 읽기도 하지 않는다 (실기기 시험기와 같다)
            Stopwatch preparing = Stopwatch.StartNew();
            if (!NpcActions.OnlyFixedLines(request.Candidates))
            {
                manager.PrepareAsync(request.RelicId).Wait();
            }
            preparing.Stop();
            Stopwatch watch = Stopwatch.StartNew();
            NpcResponse response = Ask(manager, request);
            watch.Stop();
            bool isFixed = response.Note == NpcRequestManager.FixedLinesNote;
            fixedLines += isFixed ? 1 : 0;
            if (response.ShouldShow && !response.UsedFallback)
            {
                slm++;
                times.Add(watch.ElapsedMilliseconds);
            }
            LmStudioCall call = isFixed ? null : backend.LastCall;
            Console.WriteLine("{0} {1,-4} {2,-16} {3}  ({4}ms, 미리 읽기 {5}ms{6}){7}", scene["id"],
                    response.ShouldShow ? (isFixed ? "정해진" : (response.UsedFallback ? "기본" : "SLM")) : "없음",
                    response.ShouldShow ? response.Reaction.ActionId : "", response.ShouldShow ? response.Reaction.Line : "",
                    watch.ElapsedMilliseconds, preparing.ElapsedMilliseconds,
                    call != null ? ", 입력 " + call.PromptTokens + " · 출력 " + call.CompletionTokens + " · 생각 " + call.ReasoningTokens + "토큰" : "",
                    response.Note != "SLM" && !isFixed ? "  ← " + response.Note : "");
        }
        times.Sort();
        Console.WriteLine("SLM 대사 " + slm + "/" + (scenes.Count - fixedLines) + " (이야기 " + fixedLines + "건은 정해진 대사), 기본 대사로 대신 "
                + (scenes.Count - fixedLines - slm) + (times.Count > 0 ? ", SLM 응답 중앙값 " + times[times.Count / 2] + "ms" : ""));
        Check(slm > 0, "실제 모델로 SLM 대사가 하나 이상 나옴");

        Console.WriteLine("--- 컨텍스트 구성기가 만든 요청 ---");
        var builder = new NpcContextBuilder(personas);
        int builtSlm = 0, built = 0;
        foreach (KeyValuePair<string, NpcContextInput> sample in ContextSamples())
        {
            NpcContext context = builder.Build(sample.Value);
            if (context.Request.Candidates.Count == 0)
            {
                continue;
            }
            built++;
            Stopwatch watch = Stopwatch.StartNew();
            NpcResponse response = Ask(manager, context.Request);
            watch.Stop();
            if (response.ShouldShow && !response.UsedFallback)
            {
                builtSlm++;
            }
            Console.WriteLine("{0,-22} {1,-4} {2,-16} {3}  ({4}ms){5}", sample.Key,
                    response.ShouldShow ? (response.UsedFallback ? "기본" : "SLM") : "없음",
                    response.ShouldShow ? response.Reaction.ActionId : "", response.ShouldShow ? response.Reaction.Line : "",
                    watch.ElapsedMilliseconds, response.UsedFallback ? "  ← " + response.Note : "");
        }
        Console.WriteLine("구성기 요청: SLM 대사 " + builtSlm + "/" + built);
        Check(builtSlm > 0, "구성기가 만든 요청으로도 SLM 대사가 나옴");
        backend.Dispose();
    }

    // ---------- 실제 LM Studio 로 미리 만든 대사 파일·쓴 만큼 다시 만들기 (--live-store) ----------
    // 게임처럼 요청 관리자를 거친다: 판 1 은 미리 만들고 → 반복 요청(반복 사이에는 뒤에서 다시 만들기가 끝나기를 기다린다 — 게임에서 사용자가 누르는 사이)
    // → 파일에 쓴다. 판 2 는 새 관리자로 그 파일을 읽어(앱을 다시 켠 것) 다시 요청한다. 형식 제약은 끈다 (run.py --no-format, Kanana 는 JSON 스키마를 켜면 오류)
    private static void CheckLiveStore(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines,
                                       List<Dictionary<string, object>> scenes, Dictionary<string, string> labels, string url, string model, int repeat)
    {
        Console.WriteLine("=== 실제 LM Studio 로 미리 만든 대사 파일·쓴 만큼 다시 만들기 (" + url + ", " + model + ", 반복 " + repeat + ") ===");
        var lmStudio = new LmStudioModelBackend(new LmStudioOptions { BaseUrl = url, Model = model, UseJsonSchema = false });
        var backend = new CountingBackend(lmStudio);
        try
        {
            backend.LoadAsync(CancellationToken.None).Wait();
        }
        catch (AggregateException e)
        {
            Check(false, "LM Studio 에 연결하지 못함: " + e.GetBaseException().Message);
            return;
        }
        var assembler = new PromptAssembler(personas, actions, new PromptOptions { TargetLabels = labels, DescribeFormat = true });
        var slm = new SlmReactionGenerator(backend, assembler, new NpcOutputValidator(new NpcLineFilter(forbidden), personas));
        var rule = new RuleBasedReactionGenerator(lines, new Random(7));
        string storePath = Path.GetFullPath(Path.Combine("bin", "live_npc_lines.txt"));
        // 판 1 은 처음 설치한 것처럼 빈 파일에서 시작한다. 앞 실행이 바꿔 넣다 멈춰 남긴 임시 파일(.tmp)도 지운다 — 남기면 판 1 이 그것을 읽는다 (CodeRabbit 리뷰)
        foreach (string file in new[] { storePath, storePath + ".tmp" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        string version = NpcLineStoreFormat.VersionOf(personas.contentVersion, actions.contentVersion, lines.contentVersion, forbidden.contentVersion, "lmstudio", model);

        // 요청: 시험 장면(이야기·끌려갈 때처럼 모델을 안 쓰는 것은 뺀다) + 다음 정화 셋(견본 1·9·16일과 다른 3·10·18일 뒤 — 날 수 칸이 채워지는지 본다)
        var builder = new NpcContextBuilder(personas);
        int careRelic = personas.personas[0].relicId;
        var requests = new List<KeyValuePair<string, Func<ReactionRequest>>>();
        foreach (Dictionary<string, object> scene in scenes)
        {
            Dictionary<string, object> captured = scene;
            if (!NpcActions.SkipsModel(ToRequest(captured).Candidates))
            {
                requests.Add(new KeyValuePair<string, Func<ReactionRequest>>((string)captured["id"], () => ToRequest(captured)));
            }
        }
        foreach (KeyValuePair<string, int> care in new[] { new KeyValuePair<string, int>("CLEAR", 3), new KeyValuePair<string, int>("FADED", 10), new KeyValuePair<string, int>("RESEALED", 18) })
        {
            KeyValuePair<string, int> captured = care;
            requests.Add(new KeyValuePair<string, Func<ReactionRequest>>("정화-" + captured.Key + "-" + captured.Value + "일",
                    () => builder.Build(Owner(careRelic, NpcInputs.Care, captured.Key, captured.Value)).Request));
        }
        List<ReactionRequest> careTemplates = builder.PrefetchTemplates(Owner(careRelic, NpcInputs.Tap, "CLEAR", 2)).Where(r => r.Input == NpcInputs.Care).ToList();

        // --- 판 1: 처음 설치 — 미리 만들고, 반복해 요청한다. 요청마다 앞 요청이 쓴 만큼 다시 만들기가 끝나기를 기다린다 (게임에서 사용자가 다음에 누를 때까지 비는 시간)
        Console.WriteLine("--- 판 1: 처음 설치 (파일 없음) ---");
        var first = new NpcRequestManager(slm, rule)
        {
            SlmTimeout = TimeSpan.FromSeconds(60), PrefetchTimeout = TimeSpan.FromSeconds(60), RefillAfterUse = true,   // PC 시험용 시간. 게임의 값은 실기기로 정한다
            PrefetchIdleDelay = TimeSpan.Zero   // 요청마다 다시 만들기가 끝나기를 기다리므로 쉬는 시간은 두지 않는다
        };
        Console.WriteLine("파일에서 읽은 칸 있는 대사 " + first.LoadStore(new NpcLineFileStore(storePath), version) + "개 (" + storePath + ")");
        int callsBefore = backend.Calls;
        Stopwatch preparing = Stopwatch.StartNew();
        foreach (KeyValuePair<string, Func<ReactionRequest>> item in requests.Where(r => !r.Key.StartsWith("정화-", StringComparison.Ordinal)))
        {
            first.PrefetchTemplate(item.Value(), 1);
        }
        foreach (ReactionRequest template in careTemplates)
        {
            first.PrefetchTemplate(template, 1);
        }
        WaitPrefetch(first);
        Console.WriteLine("미리 만들기 " + preparing.ElapsedMilliseconds + "ms, 모델 " + (backend.Calls - callsBefore) + "번, 보관한 칸 있는 대사 " + first.Cache.TotalCount + "개");
        int firstHits = 0;
        for (int rep = 1; rep <= repeat; rep++)
        {
            // 다시 만들기(요청 사이에 기다린 동안)와 요청 때의 모델 호출을 따로, 반복 전체에서 센다 (CodeRabbit 리뷰)
            int refillCalls = 0;
            int requestCalls = 0;
            long refillMs = 0;
            int hits = RunLivePass(first, requests, backend, rep - 1, ref refillCalls, ref requestCalls, ref refillMs);
            int lastRefill = backend.Calls;
            Stopwatch tail = Stopwatch.StartNew();
            WaitPrefetch(first); // 마지막 요청이 쓴 만큼
            refillCalls += backend.Calls - lastRefill;
            refillMs += tail.ElapsedMilliseconds;
            firstHits += hits;
            Console.WriteLine("판 1 반복 " + rep + ": 미리 만든 대사 적중 " + hits + "/" + requests.Count + ", 요청 때 모델 " + requestCalls + "번, 쓴 만큼 다시 만들기 모델 "
                    + refillCalls + "번(" + refillMs + "ms), 보관 " + first.Cache.TotalCount + "개");
        }
        first.CloseStore(true, TimeSpan.FromSeconds(30));
        Console.WriteLine("파일에 씀 (칸 있는 대사 " + first.Cache.TotalCount + "개)");

        // --- 판 2: 앱을 다시 켬 — 게임처럼 판 1 의 파일을 읽고, 모자란 것만 미리 만들고, 쓴 만큼 다시 만든다. 적중 가운데 파일에서 읽은 대사로 적중한 수를
        // 따로 센다(Cache.TakenFromStore) — 새로 만든 대사의 적중과 섞지 않는다 (CodeRabbit 리뷰). 다시 만들기를 끄면 같은 열쇠를 쓰는 장면(말발굽 탭 인사 넷)이
        // 파일에 남은 수(탭 2개)만큼만 적중해 게임과 다르게 낮게 나온다 (2026-11-19: 18/19 → 12/19)
        Console.WriteLine("--- 판 2: 앱을 다시 켬 (판 1 의 파일을 읽음) ---");
        var second = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromSeconds(60), PrefetchTimeout = TimeSpan.FromSeconds(60), RefillAfterUse = true, PrefetchIdleDelay = TimeSpan.Zero };
        Stopwatch loadingFile = Stopwatch.StartNew();
        int loaded = second.LoadStore(new NpcLineFileStore(storePath), version);
        Console.WriteLine("파일에서 읽은 칸 있는 대사 " + loaded + "개, " + loadingFile.ElapsedMilliseconds + "ms");
        int secondPrefetchCalls = backend.Calls;
        Stopwatch secondPreparing = Stopwatch.StartNew();
        foreach (KeyValuePair<string, Func<ReactionRequest>> item in requests.Where(r => !r.Key.StartsWith("정화-", StringComparison.Ordinal)))
        {
            second.PrefetchTemplate(item.Value(), 1);
        }
        foreach (ReactionRequest template in careTemplates)
        {
            second.PrefetchTemplate(template, 1);
        }
        WaitPrefetch(second);
        Console.WriteLine("미리 만들기(모자란 것만) " + secondPreparing.ElapsedMilliseconds + "ms, 모델 " + (backend.Calls - secondPrefetchCalls) + "번");
        int secondRefill = 0;
        int secondRequestCalls = 0;
        long secondRefillMs = 0;
        int secondHits = RunLivePass(second, requests, backend, 0, ref secondRefill, ref secondRequestCalls, ref secondRefillMs);
        int fromFile = second.Cache.TakenFromStore;
        Console.WriteLine("판 2: 미리 만든 대사 적중 " + secondHits + "/" + requests.Count + " (그중 파일에서 읽은 대사 " + fromFile + "), 요청 때 모델 " + secondRequestCalls
                + "번, 쓴 만큼 다시 만들기 모델 " + secondRefill + "번");
        second.CloseStore(false, TimeSpan.FromSeconds(30));
        Console.WriteLine("요약: 판 1 적중 " + firstHits + "/" + requests.Count * repeat + ", 판 2(다시 켬) 적중 " + secondHits + "/" + requests.Count + " (파일의 대사 " + fromFile + ")");
        Check(loaded > 0 && fromFile > 0, "앱을 다시 켠 판이 파일에서 읽은 대사로 적중함 (읽은 " + loaded + "개, 파일의 대사로 적중 " + fromFile + ")");
        lmStudio.Dispose();
    }

    // 요청을 한 바퀴 돌린다. 요청마다 앞 요청이 쓴 만큼 다시 만들기가 끝나기를 기다린다(그 모델 호출은 refillCalls 로 센다). 미리 만든 대사로 적중한 수를 돌려준다
    private static int RunLivePass(NpcRequestManager manager, List<KeyValuePair<string, Func<ReactionRequest>>> requests, CountingBackend backend, int variation,
                                   ref int refillCalls, ref int requestCalls, ref long refillMs)
    {
        int hits = 0;
        var times = new List<long>();
        foreach (KeyValuePair<string, Func<ReactionRequest>> item in requests)
        {
            ReactionRequest request = item.Value();
            request.Variation = variation;
            // 게임에서는 사용자가 다음에 누를 때까지 몇 초가 빈다 — 기다리지 않으면 같은 열쇠를 쓰는 장면(말발굽 탭 인사 S01·S22·S23·S24 는 서버 값만 다르다)이
            // 이어질 때 보관함이 비어 그 자리에서 만든다
            int beforeWait = backend.Calls;
            Stopwatch waiting = Stopwatch.StartNew();
            WaitPrefetch(manager);
            refillCalls += backend.Calls - beforeWait;
            refillMs += waiting.ElapsedMilliseconds;
            int beforeAsk = backend.Calls;
            Stopwatch watch = Stopwatch.StartNew();
            NpcResponse response = Ask(manager, request);
            watch.Stop();
            requestCalls += backend.Calls - beforeAsk;
            bool hit = response.Note == NpcRequestManager.PrefetchedNote;
            hits += hit ? 1 : 0;
            times.Add(watch.ElapsedMilliseconds);
            Console.WriteLine("  {0,-18} {1,-4} {2,6}ms  {3}{4}", item.Key, hit ? "미리" : response.UsedFallback ? "기본" : "SLM", watch.ElapsedMilliseconds,
                    response.ShouldShow ? response.Reaction.Line : "(없음)",
                    hit && response.Reaction.Note != null && response.Reaction.Note.StartsWith("칸 채움", StringComparison.Ordinal) ? "  [" + response.Reaction.Note + "]"
                            : !hit ? "  ← " + response.Note : "");
        }
        times.Sort();
        Console.WriteLine("  응답 중앙값 " + times[times.Count / 2] + "ms");
        return hits;
    }

    // 뒤에서 하는 미리 만들기가 끝날 때까지 (최대 30분)
    private static void WaitPrefetch(NpcRequestManager manager)
    {
        Stopwatch waiting = Stopwatch.StartNew();
        while (waiting.Elapsed < TimeSpan.FromMinutes(30))
        {
            Task working = manager.PrefetchCompletion;
            if (working == null || working.Wait(500))
            {
                Task again = manager.PrefetchCompletion;   // 기다리는 사이 새 일이 줄에 들어왔을 수 있다
                if (again == null || again == working || again.IsCompleted)
                {
                    return;
                }
            }
        }
    }

    // 모델 호출 수를 센다 (실제 백엔드를 감싼다)
    private sealed class CountingBackend : INpcModelBackend
    {
        private readonly INpcModelBackend inner;
        private int calls;

        public CountingBackend(INpcModelBackend inner)
        {
            this.inner = inner;
        }

        public int Calls
        {
            get { return Volatile.Read(ref calls); }
        }

        public bool IsLoaded
        {
            get { return inner.IsLoaded; }
        }

        public Task LoadAsync(CancellationToken ct)
        {
            return inner.LoadAsync(ct);
        }

        public void Unload()
        {
            inner.Unload();
        }

        public Task<string> CompleteAsync(NpcPrompt prompt, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return inner.CompleteAsync(prompt, ct);
        }

        public Task PrepareAsync(string system, CancellationToken ct)
        {
            return inner.PrepareAsync(system, ct);
        }
    }

    // ---------- LLMUnity 백엔드 ----------

    private static void CheckLlmUnity(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== LLMUnity 백엔드 (가짜 LLMUnity · 가짜 Unity 메인 스레드) ===");
        var defaults = new LlmUnityOptions();
        Check(defaults.MaxTokens == 40 && defaults.Temperature == 0.7f && defaults.DisableThinking && defaults.Grammar == LlmUnityGrammar.Gbnf
                && defaults.ContextSize == 2048 && defaults.NumGpuLayers == 0 && defaults.WarmUp && defaults.SaveSystemState, "기본 설정이 시험과 같음 (40토큰, 0.7, 생각 끔, GBNF)");

        NpcPrompt prompt = new PromptAssembler(personas, actions).Build(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        string reply = "{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠, 오셨구려.\"}";

        using (var unity = new UnityThread())
        {
            var runtime = new FakeLlmRuntime(reply);
            var options = new LlmUnityOptions { Model = "gemma.gguf", CancelGrace = TimeSpan.FromMilliseconds(300) };
            // 게임처럼 메인 스레드에서 만들고, 부르기는 다른 스레드(스레드 풀)에서 한다
            LlmUnityModelBackend backend = unity.Run(() => new LlmUnityModelBackend(runtime, options));

            Check(!backend.IsLoaded, "IsLoaded: 처음에는 false");
            Check(ErrorWithin(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)), 3000) == "InvalidOperationException",
                    "올리기 전에 생성하면 InvalidOperationException");

            Finish(Task.Run(() => backend.LoadAsync(CancellationToken.None)));
            FakeLlmSession first = runtime.Last;
            Check(backend.IsLoaded && runtime.Starts == 1 && runtime.LastOptions == options, "LoadAsync: 설정을 넘겨 모델을 올리고 IsLoaded true");
            Check(first.Calls.Count == 1 && first.Calls[0].User == "안녕" && first.Calls[0].MaxTokens == 8 && first.Calls[0].Grammar == null,
                    "LoadAsync: 짧게 한 번 생성해 미리 데움 (8토큰, 형식 제약 없이)");
            Finish(Task.Run(() => backend.LoadAsync(CancellationToken.None)));
            Check(runtime.Starts == 1, "LoadAsync: 이미 올라와 있으면 다시 올리지 않음");

            string raw = Within(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)));
            FakeLlmCall call = first.Calls[first.Calls.Count - 1];
            Check(raw == reply, "CompleteAsync: 모델이 낸 글을 고치지 않고 돌려줌");
            Check(call.System == prompt.System && call.User == prompt.User && call.Grammar == prompt.Grammar && call.MaxTokens == 40
                    && call.Options == options, "CompleteAsync: 시스템·사용자 메시지, GBNF 문법, 40토큰을 넘김");
            Check(backend.LastCall != null && backend.LastCall.FirstTokenMs >= 0 && backend.LastCall.FirstTokenMs <= backend.LastCall.ElapsedMs
                    && backend.LastCall.ReplyChars == reply.Length,
                    "LastCall: 첫 응답 " + (backend.LastCall != null ? backend.LastCall.FirstTokenMs : -1) + "ms ≤ 전체 "
                    + (backend.LastCall != null ? backend.LastCall.ElapsedMs : -1) + "ms");

            // 2026-11-25 플레이어의 일이 늘 먼저다: 뒤에서 만들기(Background)는 프롬프트를 나눠 읽다가, 플레이어의 생성이 줄에 서면 한 조각 안에 비켜 준다.
            // 예전에는 먼저 모델을 잡은 쪽이 프롬프트를 다 읽을 때까지(읽는 중에는 멈출 수 없다) 다른 쪽이 기다려, 누가 먼저 잡느냐에 따라 미리 읽기가 0초도 45~68초도 됐다
            var bgPrompt = new NpcPrompt
            {
                System = "뒤에서 만드는 다른 도깨비의 시스템 메시지 " + new string('가', 200), User = new string('나', 200), Grammar = prompt.Grammar,
                ActionIds = prompt.ActionIds, TargetIds = prompt.TargetIds, Background = true
            };
            first.Delay = TimeSpan.FromMilliseconds(150); // 조각 하나 0.15초
            int bgStart = first.Calls.Count;
            Task<string> background = Task.Run(() => backend.CompleteAsync(bgPrompt, CancellationToken.None));
            Check(WaitUntil(() => first.Calls.Count >= bgStart + 2, 3000), "플레이어 먼저 시험: 뒤에서 만들기가 프롬프트를 나눠 읽기 시작");
            Stopwatch foregroundWatch = Stopwatch.StartNew();
            string foregroundRaw = Within(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)));
            long foregroundMs = foregroundWatch.ElapsedMilliseconds;
            string backgroundError = ErrorWithin(background, 3000);
            List<FakeLlmCall> raceCalls;
            lock (first.Calls)
            {
                raceCalls = first.Calls.Skip(bgStart).ToList();
            }
            int foregroundAt = raceCalls.FindIndex(c => c.System == prompt.System && c.User == prompt.User);
            bool piecesOnly = foregroundAt > 0 && raceCalls.Take(foregroundAt).All(c => c.MaxTokens == 1 && c.Grammar == null && c.System.Length <= bgPrompt.System.Length);
            Check(foregroundRaw == reply && backgroundError == "OperationCanceledException" && piecesOnly && foregroundAt == raceCalls.Count - 1 && foregroundMs < 800
                    && first.EarlyCancels == 0,
                    "플레이어 먼저: 뒤에서 만들기는 1토큰 조각으로 나눠 읽다가 플레이어의 생성에 비켜 줌 (플레이어 " + foregroundMs + "ms, 조각 "
                    + Math.Max(0, foregroundAt) + "개 뒤, 뒤에서 만들기 " + backgroundError + ", 읽는 중 멈춤 " + first.EarlyCancels + "번)");
            // 비키지 않으면(뒤에서 만들기만 돌면) 조각을 다 읽고 마지막에 한 번 만든다 — 시스템 메시지 조각 + 사용자 메시지 조각 + 마지막 생성
            int soloStart = first.Calls.Count;
            first.Delay = TimeSpan.FromMilliseconds(30);
            string soloRaw = Within(Task.Run(() => backend.CompleteAsync(bgPrompt, CancellationToken.None)));
            List<FakeLlmCall> soloCalls;
            lock (first.Calls)
            {
                soloCalls = first.Calls.Skip(soloStart).ToList();
            }
            Check(soloRaw == reply && soloCalls.Count > 2 && soloCalls[soloCalls.Count - 1].MaxTokens == 40 && soloCalls[soloCalls.Count - 1].User == bgPrompt.User
                    && soloCalls.Take(soloCalls.Count - 1).All(c => c.MaxTokens == 1),
                    "뒤에서 만들기만 돌면 조각을 다 읽고 마지막에 형식 제약으로 한 번 만듦 ← 호출 " + soloCalls.Count + "번");
            Check(backend.LastCall != null && backend.LastCall.ReplyChars == reply.Length, "뒤에서 만들기는 LastCall(플레이어 요청의 첫 응답 시간)을 덮지 않음");
            // 모델에 남은 앞 프롬프트와 같은 앞부분(모든 도깨비에 같은 규칙 등)은 조각으로 다시 부르지 않고, 시스템 메시지 조각 뒤에는 시스템 메시지 전체 + '준비'를
            // 한 번 읽혀(어느 조각도 한 조각보다 많이 새로 읽지 않게) 그 기억을 저장한다 (2026-11-25 리뷰)
            string sharedHead = new string('공', 300);
            Finish(Task.Run(() => backend.CompleteAsync(new NpcPrompt { System = sharedHead + "가 도깨비", User = "안녕", Grammar = prompt.Grammar }, CancellationToken.None)));
            first.Store = new HashSet<string>();
            int skipStart = first.Calls.Count;
            var otherSpirit = new NpcPrompt { System = sharedHead + "나 도깨비 " + new string('나', 100), User = new string('다', 100), Grammar = prompt.Grammar, Background = true };
            Within(Task.Run(() => backend.CompleteAsync(otherSpirit, CancellationToken.None)));
            List<FakeLlmCall> skipCalls;
            lock (first.Calls)
            {
                skipCalls = first.Calls.Skip(skipStart).ToList();
            }
            int step = options.BackgroundStepChars;
            List<FakeLlmCall> systemPieces = skipCalls.Where(c => c.User == NpcPrompt.PrepareUser).ToList();
            bool noRepeatedHead = systemPieces.Count > 0 && systemPieces[0].System.Length > sharedHead.Length && systemPieces[0].System.Length <= sharedHead.Length + step;
            bool fullSystemPiece = systemPieces.Count > 0 && systemPieces[systemPieces.Count - 1].System == otherSpirit.System;
            bool piecesBounded = true;
            for (int i = 1; i < systemPieces.Count; i++)
            {
                piecesBounded &= systemPieces[i].System.Length - systemPieces[i - 1].System.Length <= step;
            }
            Check(noRepeatedHead && fullSystemPiece && piecesBounded && first.Saved.Contains(backend.StateKey(otherSpirit.System)),
                    "뒤에서 만들기: 앞 프롬프트와 같은 앞부분은 건너뛰고, 조각은 한 조각씩만 늘고, 시스템 메시지 전체 + '준비'를 읽혀 기억을 저장 ← 첫 조각 "
                    + (systemPieces.Count > 0 ? systemPieces[0].System.Length : -1) + "자 (같은 앞부분 " + sharedHead.Length + "자), 시스템 조각 " + systemPieces.Count + "개, 저장 " + first.Saved.Count + "번");
            first.Store = null;
            first.Saved.Clear();
            first.Delay = TimeSpan.FromMilliseconds(30);
            Finish(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None))); // 모델이 이 시스템 메시지를 기억하게 (아래 미리 읽기 시험의 시작 상태)

            // 미리 읽기: 마지막으로 끝까지 읽힌 시스템 메시지가 같으면 건너뛴다
            int before = first.Calls.Count;
            Finish(Task.Run(() => backend.PrepareAsync(prompt.System, CancellationToken.None)));
            Check(first.Calls.Count == before && backend.LastPrepare == null, "PrepareAsync: 방금 생성한 요청과 시스템 메시지가 같으면 다시 읽지 않음");
            Finish(Task.Run(() => backend.PrepareAsync("다른 도깨비의 시스템 메시지", CancellationToken.None)));
            FakeLlmCall primed = first.Calls[first.Calls.Count - 1];
            Check(first.Calls.Count == before + 1 && primed.System == "다른 도깨비의 시스템 메시지" && primed.User == NpcPrompt.PrepareUser
                    && primed.MaxTokens == 1 && primed.Grammar == null, "PrepareAsync: 다른 시스템 메시지는 1토큰·형식 제약 없이 읽힘");
            Check(backend.LastPrepare != null && backend.LastCall.ReplyChars == reply.Length, "미리 읽기는 LastPrepare 에 남고 LastCall 은 그대로");
            Finish(Task.Run(() => backend.PrepareAsync("다른 도깨비의 시스템 메시지", CancellationToken.None)));
            Check(first.Calls.Count == before + 1, "PrepareAsync: 같은 것을 두 번 미리 읽지 않음");
            Finish(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)));
            Finish(Task.Run(() => backend.PrepareAsync("다른 도깨비의 시스템 메시지", CancellationToken.None)));
            Check(first.Calls.Count == before + 3, "다른 도깨비의 요청이 끼면 다시 읽음");

            // 시스템 메시지 기억 저장·복원 (SaveSystemState)
            first.Store = new HashSet<string>();
            int beforeState = first.Calls.Count;
            int savesBefore = backend.StateSaves;   // 앞 시험(뒤에서 만들기의 기억 저장)이 센 것은 빼고 본다
            int loadsBefore = backend.StateLoads;
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 A", CancellationToken.None)));
            Check(first.Calls.Count == beforeState + 1 && first.Saved.Count == 1 && backend.StateSaves == savesBefore + 1 && !backend.LastPrepareRestored,
                    "기억 저장: 처음 읽은 시스템 메시지는 읽은 뒤 기억을 저장");
            // 저장이 안 됐으면(앞 검사가 틀림) 여기서 예외로 시험 전체가 멈추지 않고 이 검사도 틀렸다고 적는다
            string savedKey = first.Saved.Count > 0 ? first.Saved[0] : null;
            Check(savedKey != null && savedKey == backend.StateKey("기억할 시스템 메시지 A") && savedKey != backend.StateKey("기억할 시스템 메시지 B")
                    && savedKey.StartsWith("npc-kv-"),
                    "기억 이름: 시스템 메시지마다 다르고 같은 글이면 같음 (" + (savedKey ?? "저장 안 됨") + ")");
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 B", CancellationToken.None)));
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 A", CancellationToken.None)));
            Check(first.Calls.Count == beforeState + 2 && first.Loaded.Count == 1 && backend.StateLoads == loadsBefore + 1 && backend.LastPrepareRestored
                    && backend.LastPrepare != null && backend.LastPrepare.ReplyChars == 0,
                    "기억 복원: 저장해 둔 시스템 메시지는 읽지 않고 되살림 (모델 호출 " + (first.Calls.Count - beforeState) + "번, 되살림 " + first.Loaded.Count + "번)");
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 A", CancellationToken.None)));
            Check(first.Loaded.Count == 1, "기억 복원: 이미 읽힌 같은 시스템 메시지는 복원도 안 함");
            // 2026-11-25: 모델이 다른 도깨비를 기억하는 채로 온 생성(미리 읽기 없이 온 요청·다른 도깨비의 미리 만들기)도 먼저 되살린다 (A→B→A)
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 B", CancellationToken.None)));
            var promptA = new NpcPrompt
            {
                System = "기억할 시스템 메시지 A", User = prompt.User, Grammar = prompt.Grammar, JsonSchema = prompt.JsonSchema,
                ActionIds = prompt.ActionIds, TargetIds = prompt.TargetIds
            };
            int callsBeforeGen = first.Calls.Count;
            Finish(Task.Run(() => backend.CompleteAsync(promptA, CancellationToken.None)));
            Check(first.Loaded.Count == 3 && first.Calls.Count == callsBeforeGen + 1 && backend.LastCallRestored && first.Saved.Count == 2,
                    "기억 복원: 다른 도깨비(B)를 기억하는 채로 온 A 의 생성은 A 를 되살린 뒤 만듦, 저장은 하지 않음 ← 되살림 " + first.Loaded.Count + "번, 모델 "
                    + (first.Calls.Count - callsBeforeGen) + "번");
            Finish(Task.Run(() => backend.CompleteAsync(promptA, CancellationToken.None)));
            Check(first.Loaded.Count == 3 && !backend.LastCallRestored, "기억 복원: 바로 앞 생성과 시스템 메시지가 같으면 되살리지 않음");
            Finish(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)));
            Check(first.Saved.Count == 2, "기억 저장은 미리 읽을 때만 (생성 요청 뒤에는 저장 안 함 — 사용자 메시지가 섞이므로)");
            first.FailLoad = true;
            int beforeFail = first.Calls.Count;
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 B", CancellationToken.None)));
            first.FailLoad = false;
            Check(first.Calls.Count == beforeFail + 1 && backend.IsLoaded, "기억 복원이 실패해도(예외) 그냥 읽어서 이어감");
            options.SaveSystemState = false;
            int savedBefore = first.Saved.Count;
            Finish(Task.Run(() => backend.PrepareAsync("기억할 시스템 메시지 C", CancellationToken.None)));
            options.SaveSystemState = true;
            Check(first.Saved.Count == savedBefore, "SaveSystemState=false 면 저장하지 않음");
            first.Store = null;

            first.MaxActive = 0;
            Task.WaitAll(Enumerable.Range(0, 3).Select(i => Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None))).ToArray());
            Check(first.MaxActive == 1, "CompleteAsync: 동시에 불러도 모델에는 한 번에 하나씩 ← 최대 동시 " + first.MaxActive);

            // 취소 (2026-11-20 버그 리포트): 프롬프트를 읽는 중(첫 글자 전)에는 모델에 멈추라고 하지 않는다. LlamaLib 2.0.5 는 그 구간에
            // 멈추라고 하면 슬롯이 묶여 그 호출과 이후의 모든 호출이 돌아오지 않는다 — 가짜 모델도 그 구간의 CancelRequests 를 받으면 묶인다(Stranded)
            // 단계마다 가짜 모델의 관문(ReadGate·HoldGeneration)과 신호(ChatsStarted·FirstTokens)로 맞춘다 — 타이머로 때를 짐작하면
            // 바쁜 기계에서 취소가 엉뚱한 단계에 떨어져, 맞는 백엔드가 실패하거나 경로를 지나지 않고 통과한다
            first.Delay = TimeSpan.FromMilliseconds(20);
            var readGate = new ManualResetEventSlim(false);
            first.ReadGate = readGate;
            first.HoldGeneration = true; // 첫 글자 뒤에는 멈추라고 해야만 끝난다 — 미뤄 둔 취소가 실제로 나갔는지 본다
            int cancels = first.Cancels;
            int started = Volatile.Read(ref first.ChatsStarted);
            int tokens = Volatile.Read(ref first.FirstTokens);
            var cancel = new CancellationTokenSource();
            Task<string> reading = Task.Run(() => backend.CompleteAsync(prompt, cancel.Token));
            Check(WaitUntil(() => Volatile.Read(ref first.ChatsStarted) > started, 2000), "읽는 중 취소 시험: 생성이 모델에 들어감");
            first.ReadGate = null; // 다음 호출부터는 관문 없이
            first.HoldGeneration = false;
            Stopwatch watch = Stopwatch.StartNew();
            cancel.Cancel();
            string cancelled = ErrorWithin(reading, 3000);
            watch.Stop();
            int cancelsAtReturn = first.Cancels;
            Check(cancelled == "OperationCanceledException" && cancelsAtReturn == cancels && Volatile.Read(ref first.FirstTokens) == tokens
                    && watch.ElapsedMilliseconds < 1000,
                    "프롬프트를 읽는 중에 취소하면 모델에 멈추라고 하지 않고 CancelGrace 뒤 OperationCanceledException ← " + cancelled + ", "
                    + watch.ElapsedMilliseconds + "ms, 멈추라고 한 횟수 " + (cancelsAtReturn - cancels));
            Task<string> afterReading = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(!WaitQuietly(afterReading, 200) && first.Cancels == cancels, "읽기가 끝나기 전에는 다음 요청을 모델에 보내지 않고 멈추라고도 하지 않음");
            readGate.Set(); // 읽기가 끝나 첫 글자가 나온다 → 미뤄 둔 취소가 나가야 붙잡아 둔 생성이 끝난다
            bool returned = Ok(afterReading, 3000) && afterReading.Result == reply;
            Check(returned && first.Cancels == cancels + 1 && first.EarlyCancels == 0 && !first.Stranded && ReleaseIfHeld(first),
                    "미뤄 둔 취소는 첫 글자가 나오면 보내고, 모델이 묶이지 않아 다음 요청도 돌아옴 ← 멈추라고 한 횟수 "
                    + (first.Cancels - cancels) + ", 읽는 중 " + first.EarlyCancels);

            // 첫 글자 뒤(생성 단계)의 취소는 바로 모델에 멈추라고 한다
            first.ReadTime = TimeSpan.FromMilliseconds(10);
            first.HoldGeneration = true;
            cancels = first.Cancels;
            tokens = Volatile.Read(ref first.FirstTokens);
            var generatingCancel = new CancellationTokenSource();
            Task<string> generating = Task.Run(() => backend.CompleteAsync(prompt, generatingCancel.Token));
            Check(WaitUntil(() => Volatile.Read(ref first.FirstTokens) > tokens, 2000), "생성 단계 취소 시험: 첫 글자가 나옴");
            first.ReadTime = null;
            first.HoldGeneration = false;
            watch = Stopwatch.StartNew();
            generatingCancel.Cancel();
            string generatingError = ErrorWithin(generating, 3000);
            watch.Stop();
            Check(generatingError == "OperationCanceledException" && first.Cancels == cancels + 1 && first.EarlyCancels == 0 && watch.ElapsedMilliseconds < 1000
                    && ReleaseIfHeld(first),
                    "생성 단계에서 취소하면 바로 모델에 멈추라고 하고 돌아옴 ← " + generatingError + ", " + watch.ElapsedMilliseconds + "ms");
            Check(Ok(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)), 1000), "생성 단계 취소 뒤 다음 요청도 바로 돌아옴");

            // 미리 읽기(1토큰)는 취소돼도 멈추라고 하지 않는다 — 스스로 곧 끝난다
            var prepareGate = new ManualResetEventSlim(false);
            first.ReadGate = prepareGate;
            cancels = first.Cancels;
            started = Volatile.Read(ref first.ChatsStarted);
            var prepareCancel = new CancellationTokenSource();
            Task preparing = Task.Run(() => backend.PrepareAsync("미리 읽다 취소할 시스템 메시지", prepareCancel.Token));
            Check(WaitUntil(() => Volatile.Read(ref first.ChatsStarted) > started, 2000), "미리 읽기 취소 시험: 미리 읽기가 모델에 들어감");
            first.ReadGate = null;
            prepareCancel.Cancel();
            string prepareError = ErrorWithin(preparing, 3000);
            Task<string> afterPrepare = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(!WaitQuietly(afterPrepare, 200), "미리 읽기가 끝나기 전에는 다음 요청을 모델에 보내지 않음");
            prepareGate.Set();
            Check(Ok(afterPrepare, 3000) && prepareError == "OperationCanceledException" && first.Cancels == cancels && first.EarlyCancels == 0,
                    "미리 읽기가 취소되면 모델에 멈추라고 하지 않고(첫 글자 뒤에도), 끝나기를 기다렸다 다음 요청을 보냄 ← " + prepareError
                    + ", 멈추라고 한 횟수 " + (first.Cancels - cancels));

            // 멈추라고 해도 안 멈추는 모델 (LlamaLib 은 프롬프트를 읽는 도중에는 멈추지 않는다): 부른 쪽은 CancelGrace 뒤 돌아오지만,
            // 그 생성이 끝날 때까지 다음 요청을 모델에 보내지 않는다. 보내면 llama.cpp 줄에 쌓여 취소되지 않는다 (2026-11-18 S22).
            // 끝나지 않는 호출은 기한을 두고 기다린다 — 백엔드가 취소를 돌려주지 않게 바뀌어도 시험이 막히지 않고 그 검사가 틀렸다고 알린다
            first.NeverReturn = true;
            started = Volatile.Read(ref first.ChatsStarted);
            var stuck = new CancellationTokenSource();
            Task<string> stuckCall = Task.Run(() => backend.CompleteAsync(prompt, stuck.Token));
            Check(WaitUntil(() => Volatile.Read(ref first.ChatsStarted) > started, 2000), "안 멈추는 모델 시험: 생성이 모델에 들어감");
            first.NeverReturn = false;
            watch = Stopwatch.StartNew();
            stuck.Cancel();
            string stuckError = ErrorWithin(stuckCall, 3000);
            watch.Stop();
            Check(stuckError == "OperationCanceledException" && watch.ElapsedMilliseconds < 1500,
                    "안 멈추는 모델도 CancelGrace(300ms) 뒤 돌아옴 ← " + stuckError + ", " + watch.ElapsedMilliseconds + "ms");
            int callsWhileStuck = first.Calls.Count;
            var waiting = new CancellationTokenSource();
            waiting.CancelAfter(300); // 줄에서 기다리는 요청의 자기 시간 초과 (모델에 닿지 않으니 단계가 없다)
            string waitError = ErrorWithin(Task.Run(() => backend.CompleteAsync(prompt, waiting.Token)), 3000);
            Check(waitError == "OperationCanceledException" && first.Calls.Count == callsWhileStuck,
                    "안 멈춘 생성이 끝나기 전에는 다음 요청을 모델에 보내지 않고 자기 시간 초과로 끝남 ← " + waitError + ", 모델 호출 "
                    + (first.Calls.Count - callsWhileStuck) + "번");
            first.FinishStuck();
            Task<string> afterStuck = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(Ok(afterStuck, 3000) && afterStuck.Result == reply, "멈추지 않던 생성이 끝나면 다음 요청도 정상");

            // 끝내 안 끝나는 모델: CancelHold 가 지나면 멈춘 모델로 보고(IsLoaded false) 다음 요청을 모델에 보내지 않는다.
            // 예전에는 보냈는데, 묶인 슬롯 뒤에 선 요청은 또 끝나지 않아 5분마다 하나씩 쌓였다 (버그 리포트 7.2)
            options.CancelHold = TimeSpan.FromMilliseconds(400);
            first.NeverReturn = true;
            started = Volatile.Read(ref first.ChatsStarted);
            var hung = new CancellationTokenSource();
            Task<string> hungCall = Task.Run(() => backend.CompleteAsync(prompt, hung.Token));
            Check(WaitUntil(() => Volatile.Read(ref first.ChatsStarted) > started, 2000), "끝내 안 끝나는 모델 시험: 생성이 모델에 들어감");
            first.NeverReturn = false;
            hung.Cancel();
            Check(ErrorWithin(hungCall, 3000) == "OperationCanceledException", "끝내 안 끝나는 생성도 부른 쪽에는 취소로 돌아옴");
            int callsWhileHung = first.Calls.Count;
            Task<string> queuedBehindHung = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(WaitUntil(() => backend.IsStalled, 3000) && !backend.IsLoaded, "CancelHold 가 지나도 안 끝나면 멈춘 모델로 봄 (IsStalled, IsLoaded false)");
            Check(ErrorWithin(queuedBehindHung, 3000) == "InvalidOperationException" && first.Calls.Count == callsWhileHung,
                    "줄에서 기다리던 요청은 멈춘 모델에 보내지 않고 바로 실패 ← 모델 호출 " + (first.Calls.Count - callsWhileHung) + "번");
            Check(ErrorWithin(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)), 3000) == "InvalidOperationException"
                    && ErrorWithin(Task.Run(() => backend.PrepareAsync("멈춘 뒤 미리 읽기", CancellationToken.None)), 3000) == "InvalidOperationException"
                    && first.Calls.Count == callsWhileHung, "멈춘 뒤의 생성·미리 읽기도 모델에 보내지 않음");
            // 멈춘 동안 LoadAsync 는 조용히 돌아오지 않고 멈췄다고 알린다 (새로 올리지도 않는다 — 묶인 모델과 둘이 올라가지 않게)
            string stalledLoad = MessageWithin(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000);
            Check(stalledLoad.Contains("멈춰") && runtime.Starts == 1 && backend.IsStalled,
                    "멈춘 동안 LoadAsync 는 InvalidOperationException (새로 올리지 않음) ← " + stalledLoad);
            Check(backend.Status == NpcModelStatus.Stalled, "Status 로 멈춤을 한 번에 읽음 ← " + backend.Status);
            // 요청 관리자는 '아직 준비되지 않음'이 아니라 '모델이 멈춤'으로 기본 대사를 쓴다
            var stalledSlm = new SlmReactionGenerator(backend, new PromptAssembler(personas, actions), new NpcOutputValidator(new NpcLineFilter(forbidden)));
            var stalledManager = new NpcRequestManager(stalledSlm, new RuleBasedReactionGenerator(lines, new Random(7)));
            Task<NpcResponse> stalledAsk = Task.Run(() => stalledManager.RequestAsync(Tap(8)));
            NpcResponse stalledResponse = Ok(stalledAsk, 5000) ? stalledAsk.Result : null;
            Check(stalledResponse != null && stalledSlm.IsStalled && stalledManager.SlmStalled && stalledResponse.ShouldShow && stalledResponse.UsedFallback
                    && stalledResponse.Note == NpcRequestManager.StalledNote && first.Calls.Count == callsWhileHung,
                    "모델이 멈추면 요청 관리자는 모델을 부르지 않고 '모델이 멈춤' 메모로 기본 대사 ← " + (stalledResponse != null ? stalledResponse.Note : TimedOut));
            first.FinishStuck();
            Check(WaitUntil(() => backend.IsLoaded, 2000) && !backend.IsStalled && !stalledManager.SlmStalled
                    && Ok(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)), 3000),
                    "멈췄던 생성이 뒤늦게 끝나면 모델을 다시 씀");
            Check(WaitUntil(() => first.Active == 0, 2000), "가짜 모델: 뒤늦게 끝난 생성도 진행 중 수에서 빠짐 ← " + first.Active);
            options.CancelHold = TimeSpan.FromMinutes(5);

            // 취소된 생성 뒤에는 모델이 무엇을 기억하는지 모르니 같은 시스템 메시지도 다시 읽는다 (생성 단계에서 취소)
            first.ReadTime = TimeSpan.FromMilliseconds(10);
            first.HoldGeneration = true;
            tokens = Volatile.Read(ref first.FirstTokens);
            var abort = new CancellationTokenSource();
            Task<string> aborting = Task.Run(() => backend.CompleteAsync(prompt, abort.Token));
            Check(WaitUntil(() => Volatile.Read(ref first.FirstTokens) > tokens, 2000), "취소 뒤 다시 읽기 시험: 첫 글자가 나옴");
            first.ReadTime = null;
            first.HoldGeneration = false;
            abort.Cancel();
            string abortError = ErrorWithin(aborting, 3000);
            Check(abortError == "OperationCanceledException" && ReleaseIfHeld(first), "생성 단계에서 취소하면 붙잡아 둔 생성이 멈춤 ← " + abortError);
            first.Delay = TimeSpan.FromMilliseconds(20);
            int beforePrepare = first.Calls.Count;
            Finish(Task.Run(() => backend.PrepareAsync(prompt.System, CancellationToken.None)));
            Check(first.Calls.Count == beforePrepare + 1, "생성이 취소된 뒤에는 같은 시스템 메시지도 다시 미리 읽음");
            Check(first.EarlyCancels == 0 && !first.Stranded, "여기까지 프롬프트를 읽는 중에 모델에 멈추라고 한 적 없음 (" + first.EarlyCancels + "번)");

            // Unload: 바로 돌아오고 진행 중인 요청은 취소. 프롬프트를 읽는 중이면 모델에 멈추라고 하지 않고(슬롯이 묶이지 않게),
            // 모델은 그 생성이 끝난 뒤에 지운다 — LlamaLib 은 진행 중인 생성이 있을 때 지우면 메인 스레드가 멈추고 앱이 죽기도 한다 (2026-11-21 phase 4)
            // 가짜 모델도 진짜 Unity 처럼 지우기를 미룬다(Object.Destroy 는 프레임 끝). 그 사이 새로 올리면 메모리에 모델이 둘이다 — StartsWhileDestroying 로 잡는다
            runtime.DestroyDelay = TimeSpan.FromMilliseconds(200);
            var unloadGate = new ManualResetEventSlim(false);
            first.ReadGate = unloadGate; // 시험이 열 때까지 프롬프트를 읽는 중
            int firstStarted = Volatile.Read(ref first.ChatsStarted);
            Task<string> during = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(WaitUntil(() => Volatile.Read(ref first.ChatsStarted) > firstStarted, 2000), "Unload 시험: 생성이 모델에 들어감");
            first.ReadGate = null;
            watch = Stopwatch.StartNew();
            backend.Unload();
            long unloadMs = watch.ElapsedMilliseconds;
            Check(ErrorWithin(during, 3000) == "OperationCanceledException" && unloadMs < 100, "Unload: 바로 돌아오고 진행 중인 요청은 취소 ← Unload " + unloadMs + "ms");
            Check(!backend.IsLoaded && !first.Destroying, "Unload: 읽는 중인 생성이 끝나기 전에는 모델을 지우지 않음 (IsLoaded 는 바로 false)");
            Check(ErrorWithin(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)), 3000) == "InvalidOperationException",
                    "Unload 뒤 생성하면 InvalidOperationException");
            // 그 사이 다시 올리면 앞 모델이 실제로 지워지기를 기다렸다가 올린다 (메모리에 모델이 둘 올라가지 않게)
            Task reloading = Task.Run(() => backend.LoadAsync(CancellationToken.None));
            Check(!WaitQuietly(reloading, 200) && runtime.Starts == 1 && !first.Destroying, "앞 모델의 생성이 끝나기 전에는 지우지도, 새로 올리지도 않음 ← 올린 횟수 " + runtime.Starts);
            unloadGate.Set(); // 읽기가 끝난다 → 생성이 끝난다 → 지운다 → 다시 올린다
            Check(Ok(reloading, 4000) && first.Destroyed && first.EarlyCancels == 0
                    && !first.Stranded && !first.DestroyedWhileActive && runtime.StartsWhileDestroying == 0,
                    "생성이 끝나고 모델이 실제로 지워진 뒤 다시 올림, 읽는 중에 멈추라고 하지 않음 ← 읽는 중 " + first.EarlyCancels
                    + ", 지워지는 중에 올림 " + runtime.StartsWhileDestroying);
            Check(backend.IsLoaded && runtime.Starts == 2 && runtime.Last != first, "Unload 뒤 LoadAsync 하면 새로 올림 (정화 미니게임 뒤)");

            // 생성 단계에서 Unload: 첫 글자 뒤라 안전하니 멈추라고 하고, 멈추면 바로 지운다 (붙잡아 둔 생성은 멈추라고 해야만 끝난다)
            FakeLlmSession second = runtime.Last;
            second.ReadTime = TimeSpan.FromMilliseconds(10);
            second.HoldGeneration = true;
            int secondTokens = Volatile.Read(ref second.FirstTokens);
            Task<string> generatingUnload = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(WaitUntil(() => Volatile.Read(ref second.FirstTokens) > secondTokens, 2000), "생성 단계 Unload 시험: 첫 글자가 나옴");
            second.HoldGeneration = false;
            backend.Unload();
            Check(ErrorWithin(generatingUnload, 3000) == "OperationCanceledException", "생성 단계에서 Unload 하면 진행 중인 요청은 취소");
            Check(ReleaseIfHeld(second) && WaitUntil(() => second.Destroyed, 2000) && !backend.IsLoaded && !second.DestroyedWhileActive
                    && second.Cancels == 1 && second.EarlyCancels == 0,
                    "생성 단계에서 Unload: 멈추라고 한 뒤 생성이 멈추면 지움 ← 멈추라고 한 횟수 " + second.Cancels);
            Check(Ok(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000) && backend.IsLoaded && runtime.Starts == 3 && runtime.Last != second,
                    "생성 단계 Unload 뒤에도 다시 올림");

            // 맡긴 일을 메인 스레드가 꺼내기 전에 Unload: 아직 시작하지 않은 생성은 내린 모델에서 시작하지 않는다 (지우기를 늦추지 않게)
            FakeLlmSession dispatched = runtime.Last;
            var mainBusy = new ManualResetEventSlim(false);
            var mainBlocked = new ManualResetEventSlim(false);
            unity.Post(_ =>
            {
                mainBlocked.Set();
                mainBusy.Wait(); // 메인 스레드가 다른 일로 바쁘다
            }, null);
            Check(mainBlocked.Wait(2000), "보내기 전 Unload 시험: 메인 스레드를 붙잡음");
            int callsBeforeDispatch = dispatched.Calls.Count;
            Task<string> queuedForMain = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(WaitUntil(() => unity.Pending > 0, 2000), "보내기 전 Unload 시험: 생성이 메인 스레드 줄에 섬");
            backend.Unload();
            mainBusy.Set();
            Check(ErrorWithin(queuedForMain, 3000) == "OperationCanceledException" && WaitUntil(() => dispatched.Destroyed, 2000)
                    && dispatched.Calls.Count == callsBeforeDispatch,
                    "메인 스레드가 꺼내기 전에 Unload 되면 내린 모델에 생성을 보내지 않고 바로 지움 ← 모델 호출 "
                    + (dispatched.Calls.Count - callsBeforeDispatch) + "번");
            Check(Ok(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000) && backend.IsLoaded, "보내기 전 Unload 뒤에도 다시 올림");

            // 맡긴 일을 메인 스레드가 꺼내기 전에 요청이 취소되면: 생성을 시작하지 않는다. 시작하면 취소는 첫 글자 뒤에야 닿아
            // 그 프롬프트를 다 읽는 동안 모델 줄을 붙잡고, 부른 쪽은 CancelGrace 를 다 기다린다
            FakeLlmSession cancelledBeforeDispatch = runtime.Last;
            cancelledBeforeDispatch.ReadGate = new ManualResetEventSlim(false); // 시작했다면 읽기에서 멈춘다 (시작하면 안 된다)
            var busyAgain = new ManualResetEventSlim(false);
            var blockedAgain = new ManualResetEventSlim(false);
            unity.Post(_ =>
            {
                blockedAgain.Set();
                busyAgain.Wait();
            }, null);
            Check(blockedAgain.Wait(2000), "보내기 전 취소 시험: 메인 스레드를 붙잡음");
            int callsBeforeCancel = cancelledBeforeDispatch.Calls.Count;
            var cancelBeforeDispatch = new CancellationTokenSource();
            Task<string> queuedCancelled = Task.Run(() => backend.CompleteAsync(prompt, cancelBeforeDispatch.Token));
            Check(WaitUntil(() => unity.Pending > 0, 2000), "보내기 전 취소 시험: 생성이 메인 스레드 줄에 섬");
            cancelBeforeDispatch.Cancel();
            busyAgain.Set();
            cancelledBeforeDispatch.ReadGate = null;
            Check(ErrorWithin(queuedCancelled, 3000) == "OperationCanceledException" && cancelledBeforeDispatch.Calls.Count == callsBeforeCancel,
                    "메인 스레드가 꺼내기 전에 취소되면 모델에 생성을 보내지 않음 ← 모델 호출 " + (cancelledBeforeDispatch.Calls.Count - callsBeforeCancel) + "번");
            Check(ReleaseIfHeld(cancelledBeforeDispatch) && Ok(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)), 1000),
                    "보내기 전 취소 뒤 다음 요청은 바로 모델에 들어감 (취소된 요청이 줄을 붙잡지 않음)");

            // 끝나지 않는 생성을 안은 채 Unload: 지우지 않는다(지우면 앱이 죽을 수 있다). 다시 올리기는 CancelHold 뒤 실패하고, 생성이 뒤늦게 끝나면 지우고 올린다
            FakeLlmSession third = runtime.Last;
            int startsAtThird = runtime.Starts;
            options.CancelHold = TimeSpan.FromMilliseconds(400);
            third.NeverReturn = true;
            int thirdStarted = Volatile.Read(ref third.ChatsStarted);
            Task<string> stuckUnload = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Check(WaitUntil(() => Volatile.Read(ref third.ChatsStarted) > thirdStarted, 2000), "끝나지 않는 생성 시험: 생성이 모델에 들어감");
            third.NeverReturn = false;
            backend.Unload();
            Check(ErrorWithin(stuckUnload, 3000) == "OperationCanceledException", "끝나지 않는 생성이 있어도 Unload 하면 요청은 취소");
            string stuckLoad = MessageWithin(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 5000);
            Check(stuckLoad.Contains("아직 지우지 못했다") && !third.Destroyed && runtime.Starts == startsAtThird,
                    "생성이 끝나지 않으면 지우지 않고, 다시 올리기는 CancelHold 뒤 실패 ← " + stuckLoad);
            third.FinishStuck(); // 시험이 앞에서 틀렸어도 여기서 풀어 준다
            Check(Ok(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000) && third.Destroyed && !third.DestroyedWhileActive
                    && backend.IsLoaded && runtime.Starts == startsAtThird + 1,
                    "그 생성이 뒤늦게 끝나면 지우고 다시 올림");
            options.CancelHold = TimeSpan.FromMinutes(5);

            // 올리는 도중 Unload: 올린 모델을 바로 내리고, 그 모델이 다 지워지기 전에는 다시 올리지 않는다
            backend.Unload();
            Check(WaitUntil(() => runtime.Last.Destroyed, 2000), "올리는 도중 Unload 시험 준비: 앞 모델을 다 지움");
            // 올리기를 관문으로 붙잡아 둔다 — 지연(StartDelay)으로 두면 시험 스레드가 Unload 전에 잠깐 밀리는 사이 올리기가 끝나,
            // 이미 성공한 올리기에서 '올리는 도중 Unload'를 기대하게 된다
            var startGate = new ManualResetEventSlim(false);
            runtime.StartGate = startGate;
            int startsBefore = runtime.Starts;
            Task loadingTask = Task.Run(() => backend.LoadAsync(CancellationToken.None));
            Check(WaitUntil(() => runtime.Starts > startsBefore, 2000), "올리는 도중 Unload 시험: 올리기가 시작됨");
            runtime.StartGate = null;
            runtime.DestroyGate = new ManualResetEventSlim(false); // 그 모델을 지우는 중으로 붙잡아 둔다
            backend.Unload();
            startGate.Set(); // Unload 뒤에야 올리기가 끝난다
            string loadError = ErrorWithin(loadingTask, 3000);
            FakeLlmSession staleLoad = runtime.Last;
            Check(loadError == "OperationCanceledException" && !backend.IsLoaded && WaitUntil(() => staleLoad.Destroying, 2000) && !staleLoad.Destroyed,
                    "올리는 도중 Unload 하면 올린 모델을 내리기 시작하고 IsLoaded false ← " + loadError);
            int startsAtStale = runtime.Starts;
            Task afterStale = Task.Run(() => backend.LoadAsync(CancellationToken.None));
            Check(!WaitQuietly(afterStale, 200) && runtime.Starts == startsAtStale, "올리다 내린 모델이 다 지워지기 전에는 다시 올리지 않음");
            runtime.DestroyGate.Set();
            runtime.DestroyGate = null;
            Check(Ok(afterStale, 3000) && staleLoad.Destroyed && backend.IsLoaded && runtime.Starts == startsAtStale + 1,
                    "올리다 내린 모델이 지워진 뒤에 다시 올림");

            // 올리기 실패
            backend.Unload();
            Check(WaitUntil(() => runtime.Last.Destroyed, 2000), "올리기 실패 시험 준비: 앞 모델을 다 지움");
            runtime.StartError = new InvalidOperationException("LLM 이 모델 'gemma.gguf' 를 올리지 못했다");
            Check(MessageWithin(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000).Contains("올리지 못했다") && !backend.IsLoaded,
                    "모델을 못 올리면 이유를 담은 예외, IsLoaded false");
            runtime.StartError = null;
            // 데우기 실패: 올린 모델을 내리고, 그 모델이 다 지워지기 전에는 다시 올리지 않는다
            runtime.WarmUpError = new InvalidOperationException("문법 오류");
            runtime.DestroyGate = new ManualResetEventSlim(false);
            string warmError = MessageWithin(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000);
            FakeLlmSession failedWarm = runtime.Last;
            runtime.WarmUpError = null;
            Check(warmError.Contains("문법 오류") && !backend.IsLoaded && WaitUntil(() => failedWarm.Destroying, 2000) && !failedWarm.Destroyed,
                    "미리 데우기가 실패하면 올린 모델을 내리기 시작하고 예외");
            int startsAtWarm = runtime.Starts;
            Task warmRetry = Task.Run(() => backend.LoadAsync(CancellationToken.None));
            Check(!WaitQuietly(warmRetry, 200) && runtime.Starts == startsAtWarm, "데우기에 실패해 내린 모델이 다 지워지기 전에는 다시 올리지 않음");
            runtime.DestroyGate.Set();
            runtime.DestroyGate = null;
            Check(Ok(warmRetry, 3000) && failedWarm.Destroyed && backend.IsLoaded && runtime.Starts == startsAtWarm + 1,
                    "데우기에 실패해 내린 모델이 지워진 뒤에 다시 올림");
            Check(runtime.StartsWhileDestroying == 0,
                    "Unload·보내기 전 Unload·올리는 도중 Unload·데우기 실패 어느 경우에도 앞 모델이 지워지는 중에 새로 올리지 않음 ← "
                    + runtime.StartsWhileDestroying + "번");
            backend.Unload();
            runtime.DestroyDelay = TimeSpan.Zero;
            // 다른 백엔드로 바꾸기 전에 이 백엔드가 내린 모델이 다 지워지기를 기다린다 (다른 백엔드는 이 지우기를 기다리지 않는다)
            Check(WaitUntil(() => runtime.Last.Destroyed, 2000), "형식 제약 시험 준비: 앞 모델을 다 지움");

            // 형식 제약 고르기 (백엔드를 새로 만든다 — 각자 내린 모델이 다 지워진 뒤에 다음으로)
            Check(GrammarSent(unity, runtime, LlmUnityGrammar.JsonSchema, prompt) == prompt.JsonSchema, "Grammar = JsonSchema 면 JSON 스키마를 넘김");
            Check(GrammarSent(unity, runtime, LlmUnityGrammar.None, prompt) == null, "Grammar = None 이면 형식 제약 없이");
            Check(runtime.StartsWhileDestroying == 0, "백엔드를 바꿔도 앞 모델이 지워지는 중에 새로 올리지 않음");

            // 요청 관리자까지
            Check(Ok(Task.Run(() => backend.LoadAsync(CancellationToken.None)), 3000), "요청 관리자 시험 준비: 다시 올림");
            runtime.Last.Delay = TimeSpan.FromMilliseconds(20);
            var manager = new NpcRequestManager(new SlmReactionGenerator(backend, new PromptAssembler(personas, actions),
                    new NpcOutputValidator(new NpcLineFilter(forbidden))), new RuleBasedReactionGenerator(lines, new Random(7)));
            Task<NpcResponse> asking = Task.Run(() => manager.RequestAsync(Tap(8)));
            NpcResponse response = Ok(asking, 5000) ? asking.Result : null;
            Check(response != null && response.ShouldShow && response.Reaction.Source == ReactionSource.Slm && response.Reaction.Line == "어흠, 오셨구려.",
                    "요청 관리자 → SLM 생성기 → LLMUnity 백엔드로 이어짐");

            // 모든 LLMUnity 호출이 메인 스레드에서 일어났는지
            List<int> threads = runtime.AllThreads();
            Check(threads.Count > 10 && threads.All(id => id == unity.ThreadId),
                    "LLMUnity 호출 " + threads.Count + "번이 모두 Unity 메인 스레드에서 (다른 스레드에서 불러도)");
            backend.Unload();
            Check(WaitUntil(() => runtime.Last.Destroyed, 2000), "끝: 마지막 모델을 다 지움");

            // 지우기가 실패하면(지웠는지 모름) 성공으로 치지 않는다 — 다시 올리면 앞 모델이 남은 채로 하나를 더 올리게 된다.
            // 다른 백엔드로 본다 (이 백엔드는 실패를 기억해 더는 올리지 않는다)
            var failRuntime = new FakeLlmRuntime(reply);
            var failOptions = new LlmUnityOptions { Model = "gemma.gguf", WarmUp = false };
            LlmUnityModelBackend failing = unity.Run(() => new LlmUnityModelBackend(failRuntime, failOptions));
            Check(Ok(Task.Run(() => failing.LoadAsync(CancellationToken.None)), 3000), "지우기 실패 시험 준비: 올림");
            failRuntime.DestroyError = new InvalidOperationException("LLM 을 지우다 예외");
            failing.Unload();
            Check(WaitUntil(() => failRuntime.Last.Destroying, 2000), "지우기 실패 시험: 지우기를 시작함");
            string failedReload = MessageWithin(Task.Run(() => failing.LoadAsync(CancellationToken.None)), 3000);
            Check(failedReload.Contains("지우지 못했다") && failedReload.Contains("앱을 다시 켠다") && failRuntime.Starts == 1,
                    "지우기가 실패하면 다시 올리지 않고 어떻게 할지 알림 ← " + failedReload);
            failRuntime.DestroyError = null;
            Check(MessageWithin(Task.Run(() => failing.LoadAsync(CancellationToken.None)), 3000).Contains("지우지 못했다") && failRuntime.Starts == 1,
                    "이미 끝난 지우기 실패도 다음 LoadAsync 가 본다 (한 번 알리고 잊지 않음)");
        }
    }

    private static string GrammarSent(UnityThread unity, FakeLlmRuntime runtime, LlmUnityGrammar mode, NpcPrompt prompt)
    {
        var options = new LlmUnityOptions { Model = "gemma.gguf", Grammar = mode, WarmUp = false };
        LlmUnityModelBackend backend = unity.Run(() => new LlmUnityModelBackend(runtime, options));
        // 성공해야 하는 호출이라 Finish 로 기다린다 (막힘·예외·취소면 그 자리에서 실패로 적는다 — Task.Wait 처럼 시험 전체를 멈추지 않는다)
        if (!Finish(Task.Run(() => backend.LoadAsync(CancellationToken.None))))
        {
            return TimedOut;
        }
        FakeLlmSession session = runtime.Last;
        session.Delay = TimeSpan.FromMilliseconds(10);
        string grammar = Finish(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)))
                ? session.Calls[session.Calls.Count - 1].Grammar
                : TimedOut;
        backend.Unload();
        // 다음 백엔드는 이 백엔드의 지우기를 기다리지 않으니, 다 지워진 뒤에 돌아간다
        Check(WaitUntil(() => session.Destroyed, 2000), "형식 제약 시험(" + mode + "): 내린 모델을 다 지움");
        return grammar;
    }

    // 잠으로 때를 짐작하지 않고, 조건이 될 때까지 기다린다 (바쁜 기계에서 시험이 틀리게 지나가거나 실패하지 않게)
    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        return SpinWait.SpinUntil(condition, timeoutMs);
    }

    // 붙잡아 둔 가짜 생성(HoldGeneration·NeverReturn)이 끝났는지 본다. 안 끝났으면(취소가 모델에 닿지 않음) 풀어 주고 false —
    // 그대로 두면 백엔드가 그 생성을 CancelHold(5분) 동안 기다리며 줄을 붙잡아, 뒤의 시험이 줄줄이 시간 초과로 틀린다
    private static bool ReleaseIfHeld(FakeLlmSession session)
    {
        if (WaitUntil(() => session.Active == 0, 2000))
        {
            return true;
        }
        session.FinishStuck();
        WaitUntil(() => session.Active == 0, 2000);
        return false;
    }

    // 성공해야 하는 보통 호출을 기다린다. 5초 안에 성공으로 끝나지 않으면(막힘·예외·취소) 막히지 않고 그 자리에서 실패로 적고 다음으로 간다 —
    // 끝나기만 보면 뜻밖의 백엔드 예외를 놓치고, 뒤의 호출 수 검사만 맞아 통과할 수 있다
    private const int CallTimeoutMs = 5000;

    private static bool Finish(Task task)
    {
        if (!WaitQuietly(task, CallTimeoutMs))
        {
            Check(false, "백엔드 호출이 " + CallTimeoutMs + "ms 안에 끝나지 않음 (막힘)");
            return false;
        }
        if (task.Status != TaskStatus.RanToCompletion)
        {
            Exception cause = task.Exception != null ? task.Exception.GetBaseException() : null;
            Check(false, "백엔드 호출이 성공하지 않음 ← " + (cause != null ? cause.GetType().Name + ": " + cause.Message : "취소됨"));
            return false;
        }
        return true;
    }

    // Finish 처럼 기다리고(성공이 아니면 실패로 적음) 결과를 돌려준다. 성공이 아니면 default
    private static T Within<T>(Task<T> task)
    {
        return Finish(task) ? task.Result : default(T);
    }

    // 일부러 멈춰 둔 호출을 기다릴 때 쓴다. 기한 안에 안 끝나면 막히지 않고 "(시간 초과)" 를 돌려줘, 시험이 어느 검사에서 틀렸는지 알리고 다음으로 간다
    private const string TimedOut = "(시간 초과)";

    private static string ErrorWithin(Task task, int timeoutMs)
    {
        if (!WaitQuietly(task, timeoutMs))
        {
            return TimedOut;
        }
        return Error(task);
    }

    private static string MessageWithin(Task task, int timeoutMs)
    {
        if (!WaitQuietly(task, timeoutMs))
        {
            return TimedOut;
        }
        return Message(task);
    }

    // 기한 안에 성공으로 끝났는지. 실패·취소·시간 초과면 false (Task.Wait 처럼 예외를 던져 시험 전체를 멈추지 않는다)
    private static bool Ok(Task task, int timeoutMs)
    {
        return WaitQuietly(task, timeoutMs) && task.Status == TaskStatus.RanToCompletion;
    }

    private static bool WaitQuietly(Task task, int timeoutMs)
    {
        try
        {
            return task.Wait(timeoutMs);
        }
        catch (AggregateException)
        {
            return true; // 끝났다 (실패·취소)
        }
    }

    private static string Error(Task task)
    {
        try
        {
            task.Wait();
            return "(오류 없음)";
        }
        catch (AggregateException e)
        {
            Exception inner = e.GetBaseException();
            return inner is OperationCanceledException ? "OperationCanceledException" : inner.GetType().Name;
        }
    }

    private static string Message(Task task)
    {
        try
        {
            task.Wait();
            return "(오류 없음)";
        }
        catch (AggregateException e)
        {
            return e.GetBaseException().Message;
        }
    }

    // Unity 메인 스레드 흉내: 스레드 하나가 일감을 차례로 처리하고, 그 스레드의 SynchronizationContext 가 자기 자신이다
    private sealed class UnityThread : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private readonly Thread thread;

        public UnityThread()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "가짜 Unity 메인 스레드" };
            thread.Start();
        }

        public int ThreadId
        {
            get { return thread.ManagedThreadId; }
        }

        /// <summary>아직 꺼내지 않은 일감 수 (시험이 "메인 스레드 줄에 섰다"를 기다릴 때 쓴다).</summary>
        public int Pending
        {
            get { return queue.Count; }
        }

        public T Run<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>();
            Post(_ =>
            {
                try
                {
                    done.SetResult(work());
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            }, null);
            return done.Task.Result;
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            try
            {
                queue.Add(() => callback(state));
            }
            catch (InvalidOperationException)
            {
                // 시험이 끝난 뒤 늦게 온 일감
            }
        }

        public void Dispose()
        {
            queue.CompleteAdding();
        }

        private void Loop()
        {
            SetSynchronizationContext(this);
            foreach (Action work in queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    Console.WriteLine("  (메인 스레드 예외) " + e.Message);
                }
            }
        }
    }

    private sealed class FakeLlmCall
    {
        public string System;
        public string User;
        public string Grammar;
        public int MaxTokens;
        public LlmUnityOptions Options;
    }

    // LLMUnity 대신: 모델을 올리고(StartAsync) 세션을 돌려준다. 호출된 스레드를 모두 적어 둔다
    private sealed class FakeLlmRuntime : ILlmUnityRuntime
    {
        private readonly string reply;
        private readonly List<FakeLlmSession> sessions = new List<FakeLlmSession>();
        private readonly List<int> threads = new List<int>();

        public FakeLlmRuntime(string reply)
        {
            this.reply = reply;
        }

        public int Starts;
        public LlmUnityOptions LastOptions;
        public Exception StartError;
        public Exception WarmUpError;
        public TimeSpan DestroyDelay;   // 지우기를 이만큼 미룬다 (Unity 의 Object.Destroy 는 프레임 끝에 지운다)
        public volatile ManualResetEventSlim DestroyGate; // 있으면 열릴 때까지 지우는 중으로 붙잡는다
        public volatile ManualResetEventSlim StartGate;   // 있으면 열릴 때까지 올리는 중으로 붙잡는다 (Starts 는 먼저 늘어난다)
        public volatile Exception DestroyError;  // 있으면 지우기가 이 예외로 실패한다
        public int StartsWhileDestroying; // 앞 모델이 아직 지워지는 중에 새로 올린 횟수 (메모리에 모델이 둘)

        public FakeLlmSession Last
        {
            get
            {
                lock (sessions)
                {
                    return sessions[sessions.Count - 1];
                }
            }
        }

        public void Record()
        {
            lock (threads)
            {
                threads.Add(Thread.CurrentThread.ManagedThreadId);
            }
        }

        public List<int> AllThreads()
        {
            lock (threads)
            {
                return new List<int>(threads);
            }
        }

        public async Task<ILlmUnitySession> StartAsync(LlmUnityOptions options)
        {
            // 관문은 Starts 를 늘리기 전에 읽는다 — 시험은 Starts 가 느는 것을 보자마자 다음을 위해 관문을 비운다
            ManualResetEventSlim startGate = StartGate;
            Record();
            Interlocked.Increment(ref Starts);
            LastOptions = options;
            if (StartError != null)
            {
                throw StartError;
            }
            var session = new FakeLlmSession(this, reply, WarmUpError);
            lock (sessions)
            {
                if (sessions.Any(s => s.Destroying && !s.Destroyed))
                {
                    StartsWhileDestroying++;
                }
                sessions.Add(session);
            }
            if (startGate != null)
            {
                await Task.Run(() => startGate.Wait()); // 시험이 열 때까지 올리는 중으로 붙잡아 둔다 (진짜 LLMUnity 처럼 메인 스레드로 돌아와 이어진다)
            }
            Record();
            return session;
        }
    }

    // 진행 중인 생성 하나. 첫 글자가 나왔으면(Generating) 슬롯이 생성 단계에 들어간 것이다
    private sealed class FakeChat
    {
        public readonly TaskCompletionSource<string> Done = new TaskCompletionSource<string>();
        public volatile bool Generating;
        public volatile bool NeverReturn; // CancelRequests 해도 끝나지 않는 호출 (부를 때 정해진다)
        public volatile bool Stranded;
    }

    // LLMUnity 의 모델 하나 흉내. LlamaLib 2.0.5 처럼, 첫 글자가 나오기 전(프롬프트를 읽는 중)에 CancelRequests 를 받으면 슬롯이 묶여
    // 그 생성도 그 뒤의 생성도 끝나지 않는다 (Stranded, 2026-11-20 버그 리포트). 첫 글자 뒤의 CancelRequests 는 바로 멈춘다
    private sealed class FakeLlmSession : ILlmUnitySession, ILlmUnityStateStore
    {
        private readonly FakeLlmRuntime runtime;
        private readonly string reply;
        private readonly Exception warmUpError;
        private FakeChat pending;
        private int active;

        public FakeLlmSession(FakeLlmRuntime runtime, string reply, Exception warmUpError)
        {
            this.runtime = runtime;
            this.reply = reply;
            this.warmUpError = warmUpError;
            Delay = TimeSpan.FromMilliseconds(30);
        }

        public readonly List<FakeLlmCall> Calls = new List<FakeLlmCall>();
        public TimeSpan Delay;      // 생성 하나의 전체 시간
        public TimeSpan? ReadTime;  // 첫 글자까지(프롬프트 읽기). null 이면 Delay 끝에 첫 글자가 나오고 바로 끝난다
        // 단계 관문 (시험이 잠으로 때를 짐작하지 않게): ReadGate 가 있으면 열릴 때까지 프롬프트를 읽는 중이고, 열리면 첫 글자가 나온 뒤
        // Delay 동안 생성한다. HoldGeneration 이면 첫 글자 뒤 CancelRequests(또는 FinishStuck)로만 끝난다. 둘 다 부를 때의 값으로 정한다
        public volatile ManualResetEventSlim ReadGate;
        public volatile bool HoldGeneration;
        public volatile bool NeverReturn;
        public int MaxActive;
        public int Cancels;
        public int EarlyCancels;    // 첫 글자 전에 받은 CancelRequests 수 (진짜 LlamaLib 이면 슬롯이 묶인다)
        public volatile bool Stranded;
        public volatile bool Destroyed;

        // 시스템 메시지 기억 저장소. null 이면 저장·복원을 지원하지 않는 모델처럼 항상 false
        public HashSet<string> Store;
        public bool FailLoad;
        public readonly List<string> Saved = new List<string>();
        public readonly List<string> Loaded = new List<string>();

        public Task<bool> SaveStateAsync(string key)
        {
            runtime.Record();
            if (Store == null)
            {
                return Task.FromResult(false);
            }
            Store.Add(key);
            Saved.Add(key);
            return Task.FromResult(true);
        }

        public Task<bool> LoadStateAsync(string key)
        {
            runtime.Record();
            if (FailLoad)
            {
                throw new InvalidOperationException("기억 파일이 깨졌다");
            }
            if (Store == null || !Store.Contains(key))
            {
                return Task.FromResult(false);
            }
            Loaded.Add(key);
            return Task.FromResult(true);
        }

        public Task<string> ChatOnceAsync(string system, string user, string grammar, int maxTokens, LlmUnityOptions options, Action onFirstToken)
        {
            // 이 호출의 설정은 맨 먼저 읽는다. 시험은 ChatsStarted 신호를 보자마자 다음 호출을 위해 설정을 바꾸므로, 신호보다 먼저 읽어야 한다
            ManualResetEventSlim readGate = ReadGate;
            bool hold = HoldGeneration;
            bool neverReturn = NeverReturn;
            TimeSpan total = Delay;
            TimeSpan read = ReadTime.HasValue && ReadTime.Value < total ? ReadTime.Value : total;
            runtime.Record();
            lock (Calls)
            {
                Calls.Add(new FakeLlmCall { System = system, User = user, Grammar = grammar, MaxTokens = maxTokens, Options = options });
            }
            if (warmUpError != null && user == "안녕")
            {
                throw warmUpError;
            }
            int now = Interlocked.Increment(ref active);
            lock (Calls)
            {
                MaxActive = Math.Max(MaxActive, now);
            }
            var call = new FakeChat();
            pending = call;
            Interlocked.Increment(ref ChatsStarted);
            // 끝나면(뒤늦게 FinishStuck 으로 끝나도) 진행 중 수에서 뺀다
            Task<string> finished = call.Done.Task.ContinueWith(t =>
            {
                Interlocked.Decrement(ref active);
                return t.Result;
            });
            if (neverReturn || Stranded)
            {
                call.NeverReturn = true;
                return finished; // CancelRequests 해도 끝나지 않는 모델, 또는 묶인 슬롯 뒤에 선 요청
            }
            // 프롬프트 읽기: 관문이 있으면 시험이 열 때까지, 없으면 ReadTime 동안
            Task reading = readGate != null ? Task.Run(() => readGate.Wait()) : Task.Delay(read);
            reading.ContinueWith(_ =>
            {
                if (call.Stranded)
                {
                    return; // 묶인 슬롯은 생성 단계로 넘어가지 못한다
                }
                call.Generating = true;
                Interlocked.Increment(ref FirstTokens);
                if (onFirstToken != null)
                {
                    onFirstToken();
                }
                if (hold)
                {
                    return; // 생성 단계에 붙잡아 둔다 — CancelRequests(또는 FinishStuck)로만 끝난다
                }
                if (readGate == null && read >= total)
                {
                    // 첫 글자 알림은 끝나기 전에 꼭 한 번 온다 (타이머 해상도 때문에 따로 걸면 순서가 뒤바뀔 수 있다)
                    call.Done.TrySetResult(reply);
                }
                else
                {
                    Task.Delay(readGate != null ? total : total - read).ContinueWith(__ => call.Done.TrySetResult(reply));
                }
            });
            return finished;
        }

        /// <summary>지금 진행 중인 생성 수 (끝나지 않은 생성 포함).</summary>
        public int Active
        {
            get { return Volatile.Read(ref active); }
        }

        public void CancelRequests()
        {
            runtime.Record();
            Interlocked.Increment(ref Cancels);
            FakeChat current = pending;
            if (current == null || current.NeverReturn || current.Done.Task.IsCompleted)
            {
                return;
            }
            if (!current.Generating)
            {
                // LlamaLib 2.0.5: 프롬프트를 읽는 중의 취소는 슬롯을 영구히 묶는다
                Interlocked.Increment(ref EarlyCancels);
                current.Stranded = true;
                Stranded = true;
                return;
            }
            current.Done.TrySetResult(""); // 멈추면 지금까지의 글(여기서는 빈 글)을 돌려준다
        }

        // NeverReturn 으로 멈추지 않던 생성을 뒤늦게 끝낸다 (프롬프트를 다 읽고 나서야 멈추는 llama.cpp 처럼)
        public void FinishStuck()
        {
            FakeChat current = pending;
            if (current != null)
            {
                current.Done.TrySetResult("");
            }
        }

        // Unity 의 Object.Destroy 처럼 지우기를 미룬다 (프레임 끝). 그동안 Destroying 이고, 다 지우면 Destroyed
        public async Task DestroyAsync()
        {
            // 설정은 Destroying 을 알리기 전에 읽는다 — 시험은 Destroying 을 보자마자 관문·실패 설정을 바꿀 수 있다
            ManualResetEventSlim destroyGate = runtime.DestroyGate;
            TimeSpan destroyDelay = runtime.DestroyDelay;
            Exception destroyError = runtime.DestroyError;
            runtime.Record();
            if (Volatile.Read(ref active) > 0)
            {
                DestroyedWhileActive = true; // 진짜 LlamaLib 이면 메인 스레드가 멈추고, 지운 뒤 그 생성의 스레드가 앱을 죽일 수 있다
            }
            Destroying = true;
            if (destroyGate != null)
            {
                await Task.Run(() => destroyGate.Wait()); // 시험이 열 때까지 지우는 중으로 붙잡아 둔다
            }
            if (destroyDelay > TimeSpan.Zero)
            {
                await Task.Delay(destroyDelay);
            }
            if (destroyError != null)
            {
                throw destroyError; // 지우기 실패 (지웠는지 모름)
            }
            Destroyed = true;
        }

        public volatile bool Destroying;
        public volatile bool DestroyedWhileActive;

        // 시험이 잠으로 때를 짐작하지 않고 기다릴 수 있게: 시작한 생성 수, 첫 글자가 나온 생성 수
        public int ChatsStarted;
        public int FirstTokens;
    }

    // ---------- 실기기 시험 장면 변환 ----------

    private static void CheckTestScenes(string scenesJson, List<Dictionary<string, object>> scenes, PersonaCatalog personas,
                                        ActionCatalog actions, Dictionary<string, string> labels)
    {
        Console.WriteLine("=== 실기기 시험 장면 변환 ===");
        List<NpcTestScene> parsed = NpcTestScenes.Parse(scenesJson);
        Check(parsed.Count == scenes.Count, "장면 수 " + parsed.Count + "/" + scenes.Count);
        var assembler = new PromptAssembler(personas, actions, new PromptOptions { TargetLabels = labels });
        int same = 0;
        for (int i = 0; i < Math.Min(parsed.Count, scenes.Count); i++)
        {
            ReactionRequest got = parsed[i].CreateRequest();
            ReactionRequest want = ToRequest(scenes[i]);
            string id = (string)scenes[i]["id"];
            bool ok = parsed[i].Id == id && parsed[i].RelicId == want.RelicId && got.RelicId == want.RelicId && got.Mode == want.Mode
                      && got.Input == want.Input
                      && got.Situation == want.Situation
                      && Join(got.Candidates.Select(c => c.ActionId + ":" + string.Join("|", c.TargetIds.ToArray())))
                         == Join(want.Candidates.Select(c => c.ActionId + ":" + string.Join("|", c.TargetIds.ToArray())))
                      && Join(got.Facts.Select(f => f.Key + "=" + f.Value)) == Join(want.Facts.Select(f => f.Key + "=" + f.Value))
                      && Join(got.Memory.Select(m => m.DaysAgo + ":" + m.ActionId + ":" + m.Summary))
                         == Join(want.Memory.Select(m => m.DaysAgo + ":" + m.ActionId + ":" + m.Summary))
                      && parsed[i].Expected.SequenceEqual(((IEnumerable)scenes[i]["expected"]).Cast<string>());
            NpcPrompt a = assembler.Build(got);
            NpcPrompt b = assembler.Build(want);
            ok &= a == null ? b == null : b != null && a.System == b.System && a.User == b.User;
            Check(ok, id + " 실기기 시험 요청이 Verify 의 요청·프롬프트와 같음");
            if (ok)
            {
                same++;
            }
            Check(parsed[i].CreateRequest() != got, id + " 돌릴 때마다 새 요청");
        }
        Console.WriteLine("같은 요청·프롬프트: " + same + "/" + scenes.Count);

        // 기억 장면(S14)을 게임처럼: 반복 1은 장면과 같은 지난 대화, 반복 2부터는 평소 안부 (기억이 하나라 꺼낼 것이 남지 않음)
        List<NpcTestScene> memoryScenes = parsed.Where(NpcTestMemory.Handles).ToList();
        Check(memoryScenes.Count == 1 && memoryScenes[0].Id == "S14", "기억 장면은 S14 ← " + string.Join(",", memoryScenes.Select(sc => sc.Id).ToArray()));
        if (memoryScenes.Count > 0)
        {
            NpcTestScene memoryScene = memoryScenes[0];
            var testMemory = new NpcTestMemory(personas, Today);
            ReactionRequest sceneRequest = memoryScene.CreateRequest();
            var promptAssembler = new PromptAssembler(personas, actions, new PromptOptions { DescribeFormat = true });
            List<ReactionRequest> follow = testMemory.PrefetchRequests(memoryScene);
            Check(follow.Count == 2 && NpcLineCache.TemplateKeyOf(follow[0]) == NpcLineCache.TemplateKeyOf(sceneRequest)
                    && follow[1].Candidates[0].ActionId == NpcActions.AnswerStatus, "기억 장면의 미리 만들기: 장면과 같은 지난 대화 + 그다음 평소 안부");
            var kinds = new List<string>();
            for (int rep = 1; rep <= 4; rep++)
            {
                NpcContext context = testMemory.Build(memoryScene);
                if (rep == 1)
                {
                    NpcPrompt built = promptAssembler.Build(context.Request);
                    NpcPrompt scene = promptAssembler.Build(sceneRequest);
                    Check(NpcLineCache.TemplateKeyOf(context.Request) == NpcLineCache.TemplateKeyOf(sceneRequest) && NpcLineCache.KeyOf(context.Request) == NpcLineCache.KeyOf(sceneRequest)
                            && built.System == scene.System && built.User == scene.User, "기억 장면 반복 1은 장면의 요청·프롬프트와 같음 (미리 만든 대사를 쓴다)");
                }
                if (rep == 2)
                {
                    Check(NpcLineCache.TemplateKeyOf(context.Request) == NpcLineCache.TemplateKeyOf(follow[1]), "기억 장면 반복 2는 미리 만든 평소 안부와 같은 열쇠");
                }
                kinds.Add(context.Request.Candidates[0].ActionId + "/" + testMemory.Finish(memoryScene, context, Show()));
            }
            Check(kinds[0].StartsWith("recall_talk/기억: 지난 대화 꺼냄 (꺼냈음 표시)", StringComparison.Ordinal)
                    && kinds[1] == "answer_status/기억: 평소 안부 (꺼낸 뒤 한 번 쉼 표시)" && kinds[2] == "answer_status/기억: 평소 안부" && kinds[3] == kinds[2],
                    "기억 장면 반복: 지난 대화 → 평소(쉼) → 평소 → 평소 ← " + string.Join(" | ", kinds.ToArray()));
        }
    }

    private static string Join(IEnumerable<string> values)
    {
        return string.Join(" / ", values.ToArray());
    }

    // ---------- 컨텍스트 구성기 ----------

    private static readonly DateTime Today = new DateTime(2026, 11, 7);

    private static NpcContextInput Owner(int relicId, string input, string clarity, int? careDaysAgo, params MemoryEntry[] memory)
    {
        return new NpcContextInput
        {
            RelicId = relicId,
            Mode = InteractionMode.Owner,
            Input = input,
            Today = Today,
            Clarity = clarity,
            AwakeningStage = "AWAKENED",
            // 시각까지 있는 값 — 날짜만으로 며칠 전인지 센다
            LastPurifiedAt = careDaysAgo.HasValue ? Today.AddDays(-careDaysAgo.Value).AddHours(15) : (DateTime?)null,
            Memory = memory.ToList()
        };
    }

    private static NpcContextInput Guest(int relicId, string input, int? visits)
    {
        return new NpcContextInput
        {
            RelicId = relicId, Mode = InteractionMode.Visitor, Input = input, Today = Today,
            Clarity = "FADED", AwakeningStage = "AWAKENED_PLUS", RecentVisitDays = visits
        };
    }

    // 실제 모델로도 돌려 볼 대표 상황 (--live)
    private static List<KeyValuePair<string, NpcContextInput>> ContextSamples()
    {
        return new List<KeyValuePair<string, NpcContextInput>>
        {
            new KeyValuePair<string, NpcContextInput>("말발굽 탭", Owner(8, NpcInputs.Tap, "CLEAR", 2)),
            new KeyValuePair<string, NpcContextInput>("구름결 안부·흐려짐", Owner(5, NpcInputs.AskStatus, "FADED", 9)),
            new KeyValuePair<string, NpcContextInput>("구름결 안부·2일 전 관리", Owner(5, NpcInputs.AskStatus, "CLEAR", 2)),
            new KeyValuePair<string, NpcContextInput>("밤바다 안부·지난 이야기", Owner(6, NpcInputs.AskStatus, "CLEAR", 5,
                    new MemoryEntry(3, NpcActions.TellStory, "주인에게 옻칠 자개 이야기를 들려줬다"))),
            new KeyValuePair<string, NpcContextInput>("향산이 안부·재봉인", Owner(2, NpcInputs.AskStatus, "RESEALED", 16)),
            new KeyValuePair<string, NpcContextInput>("묘작이 관리·오랜만", Owner(9, NpcInputs.Care, "FADED", 10)),
            new KeyValuePair<string, NpcContextInput>("밤바다 관리", Owner(6, NpcInputs.Care, "CLEAR", 3)),
            new KeyValuePair<string, NpcContextInput>("말발굽 이야기", Owner(8, NpcInputs.ListenStory, "CLEAR", 2)),
            new KeyValuePair<string, NpcContextInput>("묘작이 끌어당기기", Owner(9, NpcInputs.Drag, "CLEAR", 1)),
            new KeyValuePair<string, NpcContextInput>("향산이 첫 손님", Guest(2, NpcInputs.Visit, 1)),
            new KeyValuePair<string, NpcContextInput>("구름결 단골 손님", Guest(5, NpcInputs.Visit, 4))
        };
    }

    private static void CheckContextBuilder(PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines, ForbiddenCatalog forbidden)
    {
        Console.WriteLine("=== 컨텍스트 구성기 ===");
        var builder = new NpcContextBuilder(personas);
        var rule = new RuleBasedReactionGenerator(lines, new Random(7));

        // 탭
        NpcContext tap = builder.Build(Owner(8, NpcInputs.Tap, "CLEAR", 2));
        Check(Acts(tap) == "greet" && tap.Request.Situation.Contains("찾아왔다") && FactsOf(tap) == "awakening=각성",
                "탭: 인사 하나, 사실은 각성 단계만 (한국어 값) ← " + FactsOf(tap));

        // 안부: 우선순위 ① 흐려짐 · 재봉인
        NpcContext faded = builder.Build(Owner(5, NpcInputs.AskStatus, "FADED", 9));
        Check(Acts(faded) == "answer_status" && faded.KeyFact == "faded"
                && faded.Request.Situation == "주인이 안부를 물었다. 너는 오래 관리를 받지 못해 흐려졌다. 주인에게 닦아 달라고 부탁한다."
                && FactsOf(faded) == "clarity=흐려짐, last_care_days_ago=9", "안부·흐려짐: 상황 문장에 흐려진 사실 ← " + faded.Request.Situation);
        NpcContext resealed = builder.Build(Owner(2, NpcInputs.AskStatus, "RESEALED", 16));
        Check(resealed.KeyFact == "resealed"
                && resealed.Request.Situation == "주인이 안부를 물었다. 너는 오래 관리를 받지 못해 흐려지다 못해 다시 봉인되었다. "
                                                  + "주인에게 깨워 달라고 부탁한다."
                && FactsOf(resealed).Contains("clarity=재봉인"), "안부·재봉인 ← " + resealed.Request.Situation);
        NpcContext fadedWithMemory = builder.Build(Owner(5, NpcInputs.AskStatus, "FADED", 9,
                new MemoryEntry(1, NpcActions.TellStory, "주인에게 상감 무늬 이야기를 들려줬다")));
        Check(fadedWithMemory.KeyFact == "faded" && Acts(fadedWithMemory) == "answer_status" && fadedWithMemory.Request.Memory.Count == 0,
                "흐려졌으면 지난 대화보다 관리 부탁이 먼저 (기억은 넣지 않음)");

        // ② 3일 안 관리
        NpcContext recent = builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 2));
        Check(recent.KeyFact == "recent_care" && recent.Request.Situation == "주인이 안부를 물었다. 너는 2일 전에 닦아 관리를 받았다. 고마움을 전한다."
                && FactsOf(recent) == "clarity=선명, last_care_days_ago=2", "안부·2일 전 관리 (오늘·어제가 아니면 날 수는 숫자로) ← " + recent.Request.Situation);
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 0)).Request.Situation.Contains("너는 오늘 닦아")
                && builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 1)).Request.Situation.Contains("너는 어제 닦아")
                && builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 3)).Request.Situation.Contains("너는 3일 전에 닦아"),
                "며칠 전은 오늘·어제만 낱말, 2일 전부터는 숫자 ('이틀·사흘'로 쓰지 않는다)");
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 0)).Request.Situation.EndsWith("너는 오늘 닦아 관리를 받았다. 고마움을 전한다."),
                "오늘 관리했으면 '오늘'");
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 1)).Request.Situation.EndsWith("너는 어제 닦아 관리를 받았다. 고마움을 전한다."),
                "어제 관리했으면 '어제'");
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 4)).KeyFact == "none", "나흘 전 관리는 '최근'이 아님 (3일 기준)");

        // 곁들일 거리 돌리기 (Variation)
        var rotating = new NpcContextBuilder(personas);
        int firstTap = rotating.Build(Owner(9, NpcInputs.Tap, "CLEAR", 1)).Request.Variation;
        int secondTap = rotating.Build(Owner(9, NpcInputs.Tap, "CLEAR", 1)).Request.Variation;
        int otherSpirit = rotating.Build(Owner(8, NpcInputs.Tap, "CLEAR", 1)).Request.Variation;
        int otherInput = rotating.Build(Owner(9, NpcInputs.Drag, "CLEAR", 1)).Request.Variation;
        Check(secondTap == firstTap + 1 && otherSpirit == firstTap && otherInput == firstTap,
                "같은 도깨비·선택지를 거듭 부르면 곁들일 거리가 바뀜 (도깨비·선택지마다 따로 셈) ← " + firstTap + "," + secondTap + "," + otherSpirit);
        NpcContextInput tomorrow = Owner(9, NpcInputs.Tap, "CLEAR", 1);
        tomorrow.Today = Today.AddDays(1);
        Check(new NpcContextBuilder(personas).Build(tomorrow).Request.Variation == firstTap + 1,
                "날이 바뀌면 시작점도 옮김 (앱을 새로 켠 날마다 같은 거리로 시작하지 않게)");

        // ③ 지난 대화
        var told = new MemoryEntry(3, NpcActions.TellStory, "주인에게 옻칠 자개 이야기를 들려줬다");
        NpcContext recall = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, told));
        Check(recall.KeyFact == "recall" && Acts(recall) == "recall_talk"
                && recall.Request.Situation == "주인이 안부를 물었다. 너는 3일 전 주인에게 옻칠 자개 이야기를 들려줬던 일이 떠올랐다."
                && recall.Request.Facts.Count == 0 && recall.Request.Memory.Count == 1 && recall.Request.Memory[0] == told,
                "안부·지난 대화: 기억 하나, 상관없는 상태는 뺌 ← " + recall.Request.Situation);
        Check(recall.MemoryToSave != null && recall.MemoryToSave.ActionId == NpcActions.RecallTalk, "꺼낸 뒤 '지난 대화를 꺼냈다'를 기억에 남김");
        NpcContext again = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, told, recall.MemoryToSave));
        Check(again.KeyFact == "none" && Acts(again) == "answer_status", "바로 전에 지난 대화를 꺼냈으면 이번에는 평소 안부");
        CheckRecallQueue(builder);
        Check(builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, new MemoryEntry(8, NpcActions.TellStory, "오래된 이야기"))).KeyFact == "none",
                "7일 넘은 기억은 꺼내지 않음");

        // ④ 그 밖
        NpcContext plain = builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 5));
        Check(plain.KeyFact == "none" && plain.Request.Situation == "주인이 안부를 물었다." && FactsOf(plain) == "clarity=선명, last_care_days_ago=5",
                "안부·평소: 상황 문장에 덧붙이지 않음");

        // 관리하기
        NpcContext longGap = builder.Build(Owner(9, NpcInputs.Care, "FADED", 10));
        Check(longGap.KeyFact == "long_gap_care" && longGap.Request.Situation.Contains("오랜만에") && Acts(longGap) == "thank_joyful,thank_calm"
                && FactsOf(longGap) == "care_result=흐려짐→선명, last_care_days_ago=10", "관리·오랜만: 오랜만에 + 관리 전 상태 ← " + FactsOf(longGap));
        NpcContext care = builder.Build(Owner(6, NpcInputs.Care, "CLEAR", 3));
        Check(care.KeyFact == "none" && !care.Request.Situation.Contains("오랜만") && care.Request.Situation.Contains("주인이 너를 관리해 주었다")
                && FactsOf(care) == "care_result=선명→선명", "관리·평소: 누가 관리했는지 적고, 관리 날짜는 넣지 않음");

        // 이야기 듣기: 실화 돌리기
        var storyBuilder = new NpcContextBuilder(personas);
        Persona horse = personas.Find(8);
        var picked = new List<string>();
        MemoryEntry lastSaved = null;
        for (int i = 0; i < 4; i++)
        {
            NpcContext story = storyBuilder.Build(lastSaved == null
                    ? Owner(8, NpcInputs.ListenStory, "CLEAR", 2)
                    : Owner(8, NpcInputs.ListenStory, "CLEAR", 2, new MemoryEntry(1, lastSaved.ActionId, lastSaved.Summary)));
            string fact = story.Request.FindFact(NpcFactKeys.VerifiedStory);
            string index = story.Request.FindFact(NpcFactKeys.StoryIndex);
            picked.Add(index);
            Check(fact == horse.stories[int.Parse(index)].fact && Acts(story) == "tell_story" && story.KeyFact == "story",
                    "이야기 " + (i + 1) + "번째: 실화 " + index + " 의 문장 그대로");
            Check(story.MemoryToSave.Summary == horse.stories[int.Parse(index)].MemorySummary(), "들려준 실화를 기억 요약으로 남김");
            Check(story.Request.Memory.Count == 0, "이야기는 AI 를 부르지 않으니 지난 이야기를 기억으로 넣지 않음");
            lastSaved = story.MemoryToSave;
        }
        Check(string.Join(",", picked.ToArray()) == "0,1,2,0", "실화를 0 → 1 → 2 → 0 으로 돌림 ← " + string.Join(",", picked.ToArray()));
        NpcContext plainStory = storyBuilder.Build(new NpcContextInput
        {
            RelicId = 3, Mode = InteractionMode.Owner, Input = NpcInputs.ListenStory, Today = Today,
            RealStory = "1921년 경주 시가지 공사 중 우연히 발견됐다. 무덤 이름 자체가 \"금관이 나온 무덤\"으로 붙여질 만큼 상징적인 발견이었다."
        });
        Check(plainStory.Request.FindFact(NpcFactKeys.VerifiedStory) == "1921년 경주 시가지 공사 중 우연히 발견됐다",
                "페르소나 없는 도깨비는 realStory 를 문장으로 나눠 첫 문장부터 (마침표 없이)");
        Check(storyBuilder.Build(new NpcContextInput
        {
            RelicId = 3, Mode = InteractionMode.Owner, Input = NpcInputs.ListenStory, Today = Today,
            RealStory = "1921년 경주 시가지 공사 중 우연히 발견됐다. 무덤 이름 자체가 \"금관이 나온 무덤\"으로 붙여질 만큼 상징적인 발견이었다."
        }).Request.FindFact(NpcFactKeys.VerifiedStory).StartsWith("무덤 이름"), "다음에는 둘째 문장");

        // 서버 시드 형식: 실화 3개를 줄바꿈으로 잇고 마침표가 없다 (ContentSeedData)
        const string seedStory = "나는 고구려 사람이 흙으로 빚은 거북 모양 연적이다\n"
                + "연적은 벼루에 먹을 갈 때 부을 물을 담아 두는 그릇이다\n"
                + "도자기로 만든 거북 연적 가운데 고구려 것이 가장 오래됐고, 나는 국립중앙박물관에 있다";
        var seedPicked = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            seedPicked.Add(storyBuilder.Build(new NpcContextInput
            {
                RelicId = 1, Mode = InteractionMode.Owner, Input = NpcInputs.ListenStory, Today = Today, RealStory = seedStory
            }).Request.FindFact(NpcFactKeys.VerifiedStory));
        }
        Check(seedPicked[0] == "나는 고구려 사람이 흙으로 빚은 거북 모양 연적이다"
                && seedPicked[1] == "연적은 벼루에 먹을 갈 때 부을 물을 담아 두는 그릇이다"
                && seedPicked[2].StartsWith("도자기로"),
                "줄바꿈으로 이은 서버 시드 실화는 한 줄씩 나눠 차례로 들려줌 ← " + string.Join(" / ", seedPicked.ToArray()));

        // 끌어당기기 · 손님
        Check(Acts(builder.Build(Owner(9, NpcInputs.Drag, "CLEAR", 1))) == "struggle_protest,struggle_play"
                && FactsOf(builder.Build(Owner(9, NpcInputs.Drag, "CLEAR", 1))) == "", "끌어당기기: 버둥 둘, 사실 없음");
        foreach (string input in new[] { NpcInputs.Tap, NpcInputs.AskStatus, NpcInputs.Care, NpcInputs.ListenStory, NpcInputs.Drag })
        {
            Check(builder.Build(Guest(5, input, 3)).Request.Candidates.Count == 0, "손님은 " + input + " 를 할 수 없음 (후보 없음)");
        }
        NpcContext first = builder.Build(Guest(2, NpcInputs.Visit, 1));
        Check(first.KeyFact == "first_visit" && Acts(first) == "wander_greet" && first.Request.Situation.StartsWith("처음 온 친구")
                && FactsOf(first) == "visit_count_30d=1, awakening=각성+", "손님·첫 방문: 인사만, 선명도는 넣지 않음 ← " + FactsOf(first));
        NpcContext regular = builder.Build(Guest(5, NpcInputs.Visit, 4));
        Check(regular.KeyFact == "regular_visit" && Acts(regular) == "wander_recall"
                && regular.Request.Situation.StartsWith("최근 30일 동안 4번 온 친구") && !FactsOf(regular).Contains("clarity"),
                "손님·단골: 알아보기 후보만, 방문 횟수를 상황 문장에");
        Check(Acts(builder.Build(Guest(5, NpcInputs.Visit, null))) == "wander_greet", "방문 기록이 없으면 첫 방문으로");
        var ownerVisit = Owner(5, NpcInputs.Visit, "CLEAR", 1);
        Check(builder.Build(ownerVisit).Request.Candidates.Count == 0, "주인에게 손님 관람 장면은 없음");

        // 다른 부품과 이어지는지
        var assembler = new PromptAssembler(personas, actions);
        var manager = new NpcRequestManager(null, rule);
        int ok = 0;
        foreach (KeyValuePair<string, NpcContextInput> sample in ContextSamples())
        {
            NpcContext context = builder.Build(sample.Value);
            NpcPrompt prompt = assembler.Build(context.Request);
            NpcResponse response = Ask(manager, context.Request);
            bool story = NpcActions.OnlyFixedLines(context.Request.Candidates);
            bool silent = NpcActions.OnlySilent(context.Request.Candidates);
            bool promptOk = story
                    ? prompt == null && response.Note == NpcRequestManager.FixedLinesNote
                    : silent
                        ? prompt == null && response.Note == NpcRequestManager.SilentNote && response.Reaction.Line == ""
                        : prompt != null && !prompt.User.Contains("story_index") && !prompt.User.Contains("FADED") && !prompt.User.Contains("AWAKENED");
            bool good = promptOk && response.ShouldShow && context.Request.Candidates.Any(c => c.ActionId == response.Reaction.ActionId);
            ok += good ? 1 : 0;
            Check(good, sample.Key + (story ? ": 이야기는 프롬프트 없이 정해진 대사"
                    : silent ? ": 끌려갈 때는 프롬프트 없이 대사 없는 반응" : ": 프롬프트(영어 값 없음)와 기본 대사까지 이어짐"));
        }
        Console.WriteLine("대표 상황 " + ok + "/" + ContextSamples().Count + " 이어짐");

        // 기본 대사가 같은 사실로 같은 내용을 고르는지 (문턱값이 같은지)
        Check(Line(rule, builder.Build(Owner(5, NpcInputs.AskStatus, "FADED", 9)).Request).Contains("닦아"), "흐려짐이면 기본 대사도 관리 부탁");
        string recentLine = Line(rule, builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 2)).Request);
        Check(recentLine.Contains("지난번") || recentLine.Contains("요전"), "이틀 전 관리면 기본 대사도 그 일을 떠올림 ← " + recentLine);
        Check(rule.Generate(builder.Build(Guest(5, NpcInputs.Visit, 2)).Request, CancellationToken.None).ActionId == NpcActions.WanderRecall,
                "두 번째 손님이면 기본 대사도 단골 인사");

        // 실제로 만든 프롬프트 한 벌 (사람이 읽어 보라고)
        Console.WriteLine("--- 예: 밤바다 안부·지난 이야기 (사용자 메시지) ---");
        Console.WriteLine(assembler.Build(builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, told)).Request).User);
    }

    // ---------- 실화 되돌리기 ----------

    private static void CheckStoryReturn(PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines, ForbiddenCatalog forbidden)
    {
        Console.WriteLine("=== 실화 되돌리기 ===");

        // StoryRotation: 표(ticket)로 되돌리기
        var rotation = new StoryRotation();
        Check(rotation.Take(8, 0) == null, "실화가 없으면 표가 없음");
        StoryTicket first = rotation.Take(8, 3);
        Check(first.Index == 0 && rotation.Return(first) && rotation.History(8).Count == 0 && rotation.Next(8, 3) == 0,
                "처음 고른 실화를 되돌리면 다시 '안 들려줌' → 다음에도 그 실화");

        rotation = new StoryRotation();
        rotation.Restore(8, new[] { 0, 1, 2 });
        StoryTicket oldest = rotation.Take(8, 3);
        Check(oldest.Index == 0 && Order(rotation) == "1,2,0", "다 들려줬으면 가장 오래된 0번을 다시 고름 ← " + Order(rotation));
        Check(rotation.Return(oldest) && Order(rotation) == "0,1,2", "되돌리면 원래 자리(가장 오래된 것)로 ← " + Order(rotation));

        rotation = new StoryRotation();
        StoryTicket a = rotation.Take(8, 3);
        StoryTicket b = rotation.Take(8, 3);
        Check(rotation.Return(a) && Order(rotation) == "1" && rotation.Next(8, 3) == 0,
                "그사이 고른 다른 실화(1번)는 그대로 두고 0번만 되돌림");

        rotation = new StoryRotation();
        StoryTicket t0 = rotation.Take(8, 3);
        rotation.Take(8, 3);
        rotation.Take(8, 3);
        rotation.Take(8, 3);   // 0번을 다시 들려줌
        Check(!rotation.Return(t0) && Order(rotation) == "1,2,0", "같은 실화를 그 뒤에 다시 들려줬으면 되돌리지 않음");
        Check(!rotation.Return(t0) && !rotation.Return(null), "이미 처리한 표나 빈 표는 false");

        // NpcContext.Finish
        var builder = new NpcContextBuilder(personas);
        NpcResponse shown = Show();
        NpcResponse dropped = Dropped();
        NpcContext story = builder.Build(Owner(8, NpcInputs.ListenStory, "CLEAR", 2));
        Check(story.StoryIndex == 0 && story.Finish(dropped) == null, "띄우지 못하면 기억에 남길 것 없음");
        NpcContext again = builder.Build(Owner(8, NpcInputs.ListenStory, "CLEAR", 2));
        Check(again.StoryIndex == 0, "띄우지 못한 실화는 다음에 다시 나옴 ← " + again.StoryIndex);
        MemoryEntry saved = again.Finish(shown);
        Check(saved != null && saved.ActionId == NpcActions.TellStory && saved.Summary.Contains(personas.Find(8).stories[0].topic),
                "띄웠으면 들려준 실화를 기억 요약으로 돌려줌");
        Check(again.Finish(dropped) == null && builder.Build(Owner(8, NpcInputs.ListenStory, "CLEAR", 2)).StoryIndex == 1,
                "Finish 는 한 번만 — 뒤늦게 '못 띄움'을 줘도 되돌리지 않음");
        NpcContext recall = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, new MemoryEntry(3, NpcActions.TellStory, "주인에게 옻칠 자개 이야기를 들려줬다")));
        Check(recall.StoryIndex == -1 && recall.Finish(shown) == recall.MemoryToSave, "실화가 없는 요청도 Finish 로 기억에 남길 것을 받음");

        // 실제 흐름: 요청 관리자가 버린 요청. 이야기는 정해진 대사(규칙 기반)로 가므로, 그쪽을 느리게 해서 띄우기 전에 버린다
        var backend = new FakeBackend();
        backend.LoadAsync(CancellationToken.None).Wait();
        var slm = new SlmReactionGenerator(backend, new PromptAssembler(personas, actions), new NpcOutputValidator(new NpcLineFilter(forbidden)));
        var slow = new SlowGenerator(new RuleBasedReactionGenerator(lines, new Random(7)));
        var manager = new NpcRequestManager(slm, slow);
        var flow = new NpcContextBuilder(personas);

        slow.DelayMs = n => 1000;
        NpcContext closing = flow.Build(Owner(2, NpcInputs.ListenStory, "CLEAR", 2));
        Task<NpcResponse> pending = manager.RequestAsync(closing.Request);
        manager.CancelAll();
        NpcResponse closed = pending.Result;
        Check(!closed.ShouldShow && closing.Finish(closed) == null && flow.Build(Owner(2, NpcInputs.ListenStory, "CLEAR", 2)).StoryIndex == closing.StoryIndex,
                "화면을 닫아 버려진 이야기는 '안 들려줌'으로 돌아가 다음에 같은 실화 ← " + closed.Note);

        var pushed = new NpcContextBuilder(personas);
        slow.DelayMs = n => n == 2 ? 1000 : 0;   // 첫 번째(앞 시험)는 이미 지났다. 이번 옛 요청만 늦게
        NpcContext older = pushed.Build(Owner(5, NpcInputs.ListenStory, "CLEAR", 2));
        Task<NpcResponse> olderTask = manager.RequestAsync(older.Request);
        NpcContext newer = pushed.Build(Owner(5, NpcInputs.ListenStory, "CLEAR", 2));
        Task<NpcResponse> newerTask = manager.RequestAsync(newer.Request);
        NpcResponse olderResult = olderTask.Result;
        NpcResponse newerResult = newerTask.Result;
        older.Finish(olderResult);
        newer.Finish(newerResult);
        Check(older.StoryIndex == 0 && newer.StoryIndex == 1 && !olderResult.ShouldShow && newerResult.ShouldShow,
                "같은 도깨비에게 새 요청: 옛 요청(0번)은 버려지고 새 요청(1번)은 띄움");
        int next1 = pushed.Build(Owner(5, NpcInputs.ListenStory, "CLEAR", 2)).StoryIndex;
        int next2 = pushed.Build(Owner(5, NpcInputs.ListenStory, "CLEAR", 2)).StoryIndex;
        Check(next1 == 0 && next2 == 2, "버려진 0번은 다시 나오고, 띄운 1번은 들려준 것으로 남음 ← " + next1 + ", " + next2);
        Check(backend.Calls == 0, "이 흐름에서 이야기 요청은 모델을 한 번도 부르지 않음");
    }

    // 기다렸다가 다른 생성기에 넘기는 생성기 (느린 기본 대사 흉내). DelayMs 는 몇 번째 호출(1부터)인지 받는다
    private sealed class SlowGenerator : INpcReactionGenerator
    {
        private readonly INpcReactionGenerator inner;
        private int calls;

        public SlowGenerator(INpcReactionGenerator inner)
        {
            this.inner = inner;
            DelayMs = n => 0;
        }

        public Func<int, int> DelayMs;

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

        public Task PrepareAsync(int relicId, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public async Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            int delay = DelayMs(Interlocked.Increment(ref calls));
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return ReactionResult.Failed(request.RequestId, FailReason.Cancelled, ReactionSource.Rule);
            }
            return await inner.GenerateAsync(request, ct);
        }
    }

    private static string Order(StoryRotation rotation)
    {
        return string.Join(",", rotation.History(8).Select(i => i.ToString()).ToArray());
    }

    // 요청 관리자 결과 흉내: 띄움 / 버림
    private static NpcResponse Show()
    {
        var manager = new NpcRequestManager(null, new FixedGenerator());
        return Ask(manager, Tap(8));
    }

    private static NpcResponse Dropped()
    {
        var manager = new NpcRequestManager(null, new FixedGenerator());
        var closed = new CancellationTokenSource();
        closed.Cancel();
        return manager.RequestAsync(Tap(8), closed.Token).Result;
    }

    private sealed class FixedGenerator : INpcReactionGenerator
    {
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

        public Task PrepareAsync(int relicId, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            return Task.FromResult(ReactionResult.Success(request.RequestId, NpcActions.Greet, NpcTargets.Player, "반가워요.", ReactionSource.Rule));
        }
    }

    private static string Acts(NpcContext context)
    {
        return string.Join(",", context.Request.Candidates.Select(c => c.ActionId).ToArray());
    }

    private static string FactsOf(NpcContext context)
    {
        return string.Join(", ", context.Request.Facts.Select(f => f.Key + "=" + f.Value).ToArray());
    }

    private static string Chat(string content, int reasoning)
    {
        return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Json.Serialize(content) + "}}],"
               + "\"usage\":{\"prompt_tokens\":321,\"completion_tokens\":21,\"completion_tokens_details\":{\"reasoning_tokens\":" + reasoning + "}}}";
    }

    private static HttpResponseMessage Http(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static string LoadError(LmStudioOptions options, FakeHttp server)
    {
        var backend = new LmStudioModelBackend(options, server);
        try
        {
            backend.LoadAsync(CancellationToken.None).Wait();
            return "(오류 없음)";
        }
        catch (AggregateException e)
        {
            return backend.IsLoaded ? "(IsLoaded 가 true)" : e.GetBaseException().Message;
        }
    }

    private static string CompleteError(LmStudioModelBackend backend, NpcPrompt prompt, CancellationToken ct = default(CancellationToken))
    {
        return CompleteErrorOf(backend.CompleteAsync(prompt, ct));
    }

    private static string CompleteErrorOf(Task<string> task)
    {
        try
        {
            task.Wait();
            return "(오류 없음)";
        }
        catch (AggregateException e)
        {
            Exception inner = e.GetBaseException();
            return inner is OperationCanceledException ? "OperationCanceledException" : inner.Message;
        }
    }

    // LM Studio 대신 정해 둔 답을 주는 가짜 서버
    private sealed class FakeHttp : HttpMessageHandler
    {
        public Func<string, string, CancellationToken, Task<HttpResponseMessage>> Respond;
        public readonly List<string> Paths = new List<string>();
        public readonly List<string> Bodies = new List<string>();
        public int MaxActive;
        private int active;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            lock (this)
            {
                Paths.Add(request.RequestUri.AbsolutePath);
                Bodies.Add(body);
                active++;
                MaxActive = Math.Max(MaxActive, active);
            }
            try
            {
                return await Respond(request.RequestUri.AbsolutePath, body, ct);
            }
            finally
            {
                lock (this)
                {
                    active--;
                }
            }
        }
    }

    private static ReactionRequest Tap(int relicId)
    {
        ReactionRequest request = Request(relicId, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet);
        request.RequestId = null;
        request.InteractionId = null;
        return request;
    }

    private static NpcResponse Ask(NpcRequestManager manager, ReactionRequest request)
    {
        return manager.RequestAsync(request).Result;
    }

    // GenerateAsync 에서 바로 예외를 던지는 생성기 (계약을 어긴 구현에 대비)
    private sealed class ThrowingGenerator : INpcReactionGenerator
    {
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

        public Task PrepareAsync(int relicId, CancellationToken ct)
        {
            throw new InvalidOperationException("구현 오류");
        }

        public Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("구현 오류");
        }
    }

    // IsReady 를 읽으면 예외를 던지는 생성기 (인터페이스가 막지 않는다). 다른 것은 아무것도 하지 않는다
    private sealed class ReadyThrowsGenerator : INpcReactionGenerator
    {
        public int Generated;

        public bool IsReady
        {
            get { throw new InvalidOperationException("상태를 못 읽음"); }
        }

        public Task LoadAsync(CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public void Unload()
        {
        }

        public Task PrepareAsync(int relicId, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Generated);
            return Task.FromResult(ReactionResult.Failed(request.RequestId, FailReason.Error, ReactionSource.Slm, "불리면 안 됨"));
        }
    }

    private static string Out(string action, string target, string line)
    {
        return "{\"action\":\"" + action + "\",\"target\":\"" + target + "\",\"line\":\"" + line + "\"}";
    }

    private static void Expect(NpcOutputValidator validator, string raw, ReactionRequest request, OutputProblem want, string what)
    {
        OutputCheck result = validator.Check(raw, request);
        Check(result.Problem == want, what + " (" + want + " ← " + result.Problem + (result.Detail != null ? ": " + result.Detail : "") + ")");
    }

    private static ReactionResult Run(SlmReactionGenerator generator, ReactionRequest request)
    {
        return generator.GenerateAsync(request, CancellationToken.None).Result;
    }

    // 모델 런타임 대신 정해 둔 글을 돌려주는 가짜
    private sealed class FakeBackend : INpcModelBackend
    {
        public string Reply;
        public readonly Queue<string> Replies = new Queue<string>();   // 있으면 차례로 이것으로 답한다 (다시 만들기 시험)
        public Exception Throw;
        public Action OnComplete;
        public Func<CancellationToken, Task<string>> Delayed;   // 정해 두면 이것으로 답한다 (느린 런타임 흉내)
        public int Calls;
        public NpcPrompt LastPrompt;
        public readonly List<string> Prepared = new List<string>();  // 미리 읽힌 시스템 메시지
        public TimeSpan PrepareDelay = TimeSpan.Zero;
        public Exception PrepareThrow;
        public int PrepareCancelled;
        private bool loaded;

        public async Task PrepareAsync(string system, CancellationToken ct)
        {
            lock (Prepared)
            {
                Prepared.Add(system);
            }
            if (PrepareThrow != null)
            {
                throw PrepareThrow;
            }
            if (PrepareDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(PrepareDelay, ct);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref PrepareCancelled);
                    throw;
                }
            }
        }

        public bool IsLoaded
        {
            get { return loaded; }
        }

        public Task LoadAsync(CancellationToken ct)
        {
            loaded = true;
            return Task.FromResult(true);
        }

        public void Unload()
        {
            loaded = false;
        }

        public Task<string> CompleteAsync(NpcPrompt prompt, CancellationToken ct)
        {
            Calls++;
            LastPrompt = prompt;
            if (OnComplete != null)
            {
                OnComplete();
            }
            if (Throw != null)
            {
                var failed = new TaskCompletionSource<string>();
                failed.SetException(Throw);
                return failed.Task;
            }
            if (Delayed != null)
            {
                return Delayed(ct);
            }
            return Task.FromResult(Replies.Count > 0 ? Replies.Dequeue() : Reply);
        }
    }

    private static bool Same(string id, string part, string want, string got)
    {
        if (want == got)
        {
            return true;
        }
        int at = 0;
        while (at < want.Length && at < got.Length && want[at] == got[at])
        {
            at++;
        }
        Check(false, id + " " + part + " 다름 (" + at + "번째 글자부터)\n      run.py: …" + Around(want, at) + "\n      C#    : …" + Around(got, at));
        return false;
    }

    private static string Around(string text, int at)
    {
        int start = Math.Max(0, at - 20);
        return text.Substring(start, Math.Min(60, text.Length - start)).Replace("\n", "⏎");
    }

    // ---------- 공용 ----------

    private static T Read<T>(string path)
    {
        return Json.Deserialize<T>(ReadText(path));
    }

    private static string ReadText(string path)
    {
        return File.ReadAllText(path, Encoding.UTF8);
    }

    // 프롬프트 시험의 장면 하나를 게임이 만들 요청으로 바꾼다
    private static ReactionRequest ToRequest(Dictionary<string, object> scene)
    {
        var request = new ReactionRequest
        {
            RequestId = (string)scene["id"] + "-req",
            InteractionId = (string)scene["id"],
            RelicId = (int)scene["relicId"],
            Mode = (string)scene["mode"] == "owner" ? InteractionMode.Owner : InteractionMode.Visitor,
            Input = (string)scene["input"],
            Situation = (string)scene["event"],
            Nickname = scene.ContainsKey("nickname") ? (string)scene["nickname"] : null,
            Intimacy = NpcTestScenes.IntimacyOf(scene),
            ContentVersion = "test"
        };
        foreach (Dictionary<string, object> c in (IEnumerable)scene["candidates"])
        {
            request.Candidates.Add(new ActionCandidate((string)c["action"], ((IEnumerable)c["targets"]).Cast<string>().ToArray()));
        }
        foreach (Dictionary<string, object> f in (IEnumerable)scene["facts"])
        {
            string key = (string)f["key"];
            // 이야기 자료는 계약상 값에 문장을 넣는다 (시험 데이터는 문장을 text 에 두었다)
            string value = key == NpcFactKeys.VerifiedStory ? (string)f["text"] : FactValue(f["value"]);
            request.Facts.Add(new Fact(key, value));
        }
        // 게임이 StoryRotation 으로 고른 번호. 규칙 기반 생성기는 같은 이야기 대사를 고르고, 프롬프트에는 싣지 않는다
        if (scene.ContainsKey("storyIndex"))
        {
            request.Facts.Add(new Fact(NpcFactKeys.StoryIndex, Convert.ToString(scene["storyIndex"])));
        }
        foreach (Dictionary<string, object> m in (IEnumerable)scene["memory"])
        {
            request.Memory.Add(new MemoryEntry((int)m["daysAgo"], (string)m["action"],
                    m.ContainsKey("summary") ? (string)m["summary"] : null));
        }
        return request;
    }

    // run.py 의 fact_value 와 같게 — 참거짓은 소문자로
    private static string FactValue(object value)
    {
        if (value is bool)
        {
            return (bool)value ? "true" : "false";
        }
        return Convert.ToString(value);
    }

    private static ReactionRequest Request(int relicId, InteractionMode mode, string input, params string[] actionIds)
    {
        var request = new ReactionRequest
        {
            RequestId = Guid.NewGuid().ToString(), RelicId = relicId, Mode = mode, Input = input, Situation = "시험"
        };
        foreach (string actionId in actionIds)
        {
            request.Candidates.Add(new ActionCandidate(actionId, NpcTargets.Player));
        }
        return request;
    }

    private static ReactionRequest Status(int relicId, string clarity, string lastCareDaysAgo)
    {
        ReactionRequest request = Request(relicId, InteractionMode.Owner, NpcInputs.AskStatus, NpcActions.AnswerStatus);
        request.Facts.Add(new Fact(NpcFactKeys.Clarity, clarity));
        if (lastCareDaysAgo != null)
        {
            request.Facts.Add(new Fact(NpcFactKeys.LastCareDaysAgo, lastCareDaysAgo));
        }
        return request;
    }

    private static string Line(RuleBasedReactionGenerator generator, ReactionRequest request)
    {
        ReactionResult result = generator.Generate(request, CancellationToken.None);
        return result.IsSuccess ? result.Line : "";
    }

    private static string Pick(RuleBasedReactionGenerator generator, int relicId, string input, params string[] actionIds)
    {
        return generator.Generate(Request(relicId, InteractionMode.Owner, input, actionIds), CancellationToken.None).ActionId;
    }

    private static bool Check(bool ok, string what)
    {
        if (!ok)
        {
            failures++;
            Console.WriteLine("  실패: " + what);
        }
        return ok;
    }
}
