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

        CheckData(packageDir, testDataDir, lines, personas);
        CheckGenerator(lines, scenes);
        CheckStories(lines);
        CheckFilter(forbidden, lines, personas);
        CheckPrompts(personas, actions, scenes, labels, expectedPath);
        CheckOutput(forbidden, personas, actions, lines, scenes);
        CheckManager(forbidden, personas, actions, lines);
        CheckLmStudio(forbidden, personas, actions, lines);
        CheckLlmUnity(forbidden, personas, actions, lines);
        CheckTestScenes(ReadText(Path.Combine(testDataDir, "scenes.json")), scenes, personas, actions, labels);
        CheckContextBuilder(personas, actions, lines, forbidden);
        CheckStoryReturn(personas, actions, lines, forbidden);
        if (args.Length > 3 && args[3] == "--live")
        {
            CheckLive(forbidden, personas, actions, lines, scenes, labels,
                    args.Length > 4 ? args[4] : "http://localhost:1234", args.Length > 5 ? args[5] : "google/gemma-4-e2b");
        }

        Console.WriteLine(failures == 0 ? "모두 통과" : "실패 " + failures + "건");
        return failures == 0 ? 0 : 1;
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
                Check(!string.IsNullOrEmpty(result.Line), id + " 대사 있음");
                repeated |= previous != null && previous == result.Line;
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

        Check(new StoryPiece { topic = "역참 증표" }.MemorySummary() == "주인에게 역참 증표 이야기를 들려줬다", "기억 요약 모양이 장면과 같음");
    }

    // ---------- 공통 금칙어 ----------

    private static void CheckFilter(ForbiddenCatalog catalog, DefaultLineCatalog lines, PersonaCatalog personas)
    {
        Console.WriteLine("=== 공통 금칙어 ===");
        var filter = new NpcLineFilter(catalog);
        Check(catalog.groups.Count == 5, "금칙어 묶음 5개 (욕설·비하·밀어내기·퉁명·위협)");
        string[] blocked =
        {
            "……지문 남는다. 놓아.", "……손대지 마.", "앗, 놔줘~ 나 바쁘단 말이야!", "어흠! 내려놓아라.", "놓으라고!",
            "닥쳐, 귀찮아.", "저리 가.", "관심 없어.", "쳇, 됐거든.", "바보 같아.", "시끄러워, 저리 비켜.", "씨발", "개 새 끼"
        };
        foreach (string line in blocked)
        {
            Check(filter.Find(line) != null, "걸러야 함: " + line);
        }
        string[] allowed =
        {
            "새끼 참새가 날아간다!", "심심해 죽겠어~", "앗, 참새를 놓쳤어!", "여기 놓으면 볕이 잘 들겠다!", "바람 쐬러 나가자",
            "이리저리 구경하게", "흥미로운 손님이구려", "어흠, 살살 내려 주시오.", "……지문 남잖아. 살살 들어 줘.", ""
        };
        foreach (string line in allowed)
        {
            Check(filter.IsAllowed(line), "통과해야 함: " + line + " ← " + filter.Find(line));
        }
        Check(filter.FindAll("닥쳐, 귀찮아.").Count == 2, "FindAll 은 걸린 말을 모두 (닥쳐, 귀찮)");

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
        int same = 0;

        foreach (Dictionary<string, object> scene in scenes)
        {
            string id = (string)scene["id"];
            NpcPrompt prompt = assembler.Build(ToRequest(scene));
            if (prompt == null)
            {
                Check(false, id + " 프롬프트가 만들어지지 않음");
                continue;
            }
            var want = (Dictionary<string, object>)expected[id];
            bool ok = Same(id, "시스템", (string)want["system"], prompt.System)
                      & Same(id, "사용자", (string)want["user"], prompt.User)
                      & Same(id, "스키마", (string)want["schema"], prompt.JsonSchema);
            foreach (string action in prompt.ActionIds)
            {
                ok &= Check(prompt.Grammar.Contains("\"\\\"" + action + "\\\"\""), id + " 문법에 행동 " + action);
            }
            if (ok)
            {
                same++;
            }
        }
        Console.WriteLine("run.py 와 같은 프롬프트: " + same + "/" + scenes.Count);

        foreach (Dictionary<string, object> scene in scenes)
        {
            string id = (string)scene["id"];
            ReactionRequest request = ToRequest(scene);
            NpcPrompt prompt = assembler.Build(request);
            if (prompt == null)
            {
                continue;
            }
            Check(prompt.System.Contains(PromptAssembler.RuleBond), id + " 애착 규칙 있음");
            Check(!prompt.User.Contains(NpcFactKeys.StoryIndex), id + " story_index 는 프롬프트에 없음");
            bool storyWithMemory = request.Input == NpcInputs.ListenStory && request.Memory.Count > 0;
            Check(prompt.System.Contains(PromptAssembler.RuleStoryNew) == storyWithMemory, id + " 지난 이야기 규칙은 이야기+기억일 때만");
        }

        // 페르소나가 없는 도깨비나 후보가 없으면 만들지 않는다 → 규칙 기반 생성기로 대신
        Check(assembler.Build(Request(1, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet)) == null, "페르소나 없는 도깨비는 null");
        Check(assembler.Build(Request(8, InteractionMode.Owner, NpcInputs.Tap)) == null, "후보가 없으면 null");

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
        Expect(validator, "{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠}", tap, OutputProblem.NotJson, "문자열이 닫히지 않으면 NotJson");
        Expect(validator, "{\"action\":\"greet\" \"target\":\"player\"}", tap, OutputProblem.NotJson, "쉼표가 빠지면 NotJson");
        Expect(validator, "<think>{\"action\":\"greet\"", tap, OutputProblem.NotJson, "닫히지 않은 생각 블록만 있으면 NotJson");

        // 후보 포함
        Expect(validator, Out("answer_status", "player", "어흠."), tap, OutputProblem.UnknownAction, "후보에 없는 행동");
        Expect(validator, Out("안부 묻기", "player", "어흠."), tap, OutputProblem.UnknownAction, "행동 칸에 선택지 이름");
        Expect(validator, "{\"target\":\"player\",\"line\":\"어흠.\"}", tap, OutputProblem.UnknownAction, "행동이 없음");
        Expect(validator, Out("greet", "none", "어흠."), tap, OutputProblem.TargetMismatch, "후보에 없는 대상");
        Expect(validator, Out("greet", "player(주인)", "어흠."), tap, OutputProblem.TargetMismatch, "대상 칸에 'player(주인)'");
        Expect(validator, "{\"action\":\"greet\",\"line\":\"어흠.\"}", tap, OutputProblem.TargetMismatch, "대상이 없음");
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
        Expect(validator, Out("greet", "player", new string('가', 40)), tap, OutputProblem.None, "기본 40자까지 통과");
        Expect(validator, Out("greet", "player", new string('가', 41)), tap, OutputProblem.TooLong, "기본 41자는 TooLong");
        ReactionRequest story = Request(8, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory);
        Expect(validator, Out("tell_story", "player", new string('가', 75)), story, OutputProblem.None, "이야기 75자까지 통과");
        Expect(validator, Out("tell_story", "player", new string('가', 76)), story, OutputProblem.TooLong, "이야기 76자는 TooLong");
        ReactionRequest visit = Request(2, InteractionMode.Visitor, NpcInputs.Visit, NpcActions.WanderGreet);
        Expect(validator, Out("wander_greet", "player", new string('가', 25)), visit, OutputProblem.None, "손님 관람 25자까지 통과");
        Expect(validator, Out("wander_greet", "player", new string('가', 26)), visit, OutputProblem.TooLong, "손님 관람 26자는 TooLong");
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
        ReactionRequest drag = Request(6, InteractionMode.Owner, NpcInputs.Drag, NpcActions.StruggleProtest, NpcActions.StrugglePlay);
        Expect(validator, Out("struggle_protest", "player", "……지문 남을까 봐. 살살 해 줘."), drag, OutputProblem.None, "Gemma S16 대사 통과");
        Expect(validator, Out("tell_story", "player",
                "허허… 내 몸이 예전에 능산리 절터 발굴 조사 중에 저습지에서 나왔다는 이야기를 들으면 참 기분이 좋구먼. 산속 고요함 속에서 이렇게 잠시 이야기를 나누게 되니 참 반갑네."),
                Request(2, InteractionMode.Owner, NpcInputs.ListenStory, NpcActions.TellStory), OutputProblem.TooLong, "Gemma S18 96자 대사는 TooLong");

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
        Check(backend.LastPrompt != null && backend.LastPrompt.System.Contains("말발굽") && backend.LastPrompt.Grammar.Contains("greet"),
                "조립한 프롬프트(문법 포함)를 모델에 넘김");

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

    private static void CheckManager(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== 요청 관리자 ===");
        var rule = new RuleBasedReactionGenerator(lines, new Random(7));
        var backend = new FakeBackend();
        var slm = new SlmReactionGenerator(backend, new PromptAssembler(personas, actions),
                new NpcOutputValidator(new NpcLineFilter(forbidden)));
        var manager = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromMilliseconds(300) };
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
        Check(Convert.ToDouble(sent["temperature"]) == 0.7 && Convert.ToInt32(sent["max_tokens"]) == 160
                && (string)sent["reasoning_effort"] == "none", "생성 설정이 시험과 같음 (온도 0.7, 160토큰, 생각 끔)");
        var format = (Dictionary<string, object>)sent["response_format"];
        var schemaSent = ((Dictionary<string, object>)format["json_schema"])["schema"];
        Check((string)format["type"] == "json_schema"
                && Json.Serialize(schemaSent) == Json.Serialize(Json.DeserializeObject(prompt.JsonSchema)), "JSON 스키마 형식 제약을 넘김");
        Check(!sent.ContainsKey("seed"), "seed 는 정했을 때만");
        Check(backend.LastCall != null && backend.LastCall.PromptTokens == 321 && backend.LastCall.CompletionTokens == 21
                && backend.LastCall.ReasoningTokens == 0, "토큰 수를 LastCall 에 남김");

        var quiet = new LmStudioModelBackend(new LmStudioOptions { UseJsonSchema = false, DisableThinking = false, Seed = 7, WarmUp = false }, server);
        quiet.LoadAsync(CancellationToken.None).Wait();
        quiet.CompleteAsync(prompt, CancellationToken.None).Wait();
        var plain = (Dictionary<string, object>)Json.DeserializeObject(server.Bodies[server.Bodies.Count - 1]);
        Check(!plain.ContainsKey("response_format") && !plain.ContainsKey("reasoning_effort") && Convert.ToInt32(plain["seed"]) == 7,
                "설정으로 형식 제약·생각 끄기를 빼고 seed 를 넣을 수 있음");

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
        var manager = new NpcRequestManager(new SlmReactionGenerator(backend, assembler, new NpcOutputValidator(new NpcLineFilter(forbidden))),
                new RuleBasedReactionGenerator(lines, new Random(7)))
        {
            SlmTimeout = TimeSpan.FromSeconds(60)   // PC 시험용. 게임의 값은 실기기 측정으로 정한다
        };
        int slm = 0;
        var times = new List<long>();
        foreach (Dictionary<string, object> scene in scenes)
        {
            Stopwatch watch = Stopwatch.StartNew();
            NpcResponse response = Ask(manager, ToRequest(scene));
            watch.Stop();
            if (response.ShouldShow && !response.UsedFallback)
            {
                slm++;
                times.Add(watch.ElapsedMilliseconds);
            }
            LmStudioCall call = backend.LastCall;
            Console.WriteLine("{0} {1,-4} {2,-16} {3}  ({4}ms{5}){6}", scene["id"],
                    response.ShouldShow ? (response.UsedFallback ? "기본" : "SLM") : "없음",
                    response.ShouldShow ? response.Reaction.ActionId : "", response.ShouldShow ? response.Reaction.Line : "",
                    watch.ElapsedMilliseconds,
                    call != null ? ", 입력 " + call.PromptTokens + " · 출력 " + call.CompletionTokens + " · 생각 " + call.ReasoningTokens + "토큰" : "",
                    response.UsedFallback ? "  ← " + response.Note : "");
        }
        times.Sort();
        Console.WriteLine("SLM 대사 " + slm + "/" + scenes.Count + ", 기본 대사로 대신 " + (scenes.Count - slm)
                + (times.Count > 0 ? ", SLM 응답 중앙값 " + times[times.Count / 2] + "ms" : ""));
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

    // ---------- LLMUnity 백엔드 ----------

    private static void CheckLlmUnity(ForbiddenCatalog forbidden, PersonaCatalog personas, ActionCatalog actions, DefaultLineCatalog lines)
    {
        Console.WriteLine("=== LLMUnity 백엔드 (가짜 LLMUnity · 가짜 Unity 메인 스레드) ===");
        var defaults = new LlmUnityOptions();
        Check(defaults.MaxTokens == 160 && defaults.Temperature == 0.7f && defaults.DisableThinking && defaults.Grammar == LlmUnityGrammar.Gbnf
                && defaults.ContextSize == 2048 && defaults.NumGpuLayers == 0 && defaults.WarmUp, "기본 설정이 시험과 같음 (160토큰, 0.7, 생각 끔, GBNF)");

        NpcPrompt prompt = new PromptAssembler(personas, actions).Build(Request(8, InteractionMode.Owner, NpcInputs.Tap, NpcActions.Greet));
        string reply = "{\"action\":\"greet\",\"target\":\"player\",\"line\":\"어흠, 오셨구려.\"}";

        using (var unity = new UnityThread())
        {
            var runtime = new FakeLlmRuntime(reply);
            var options = new LlmUnityOptions { Model = "gemma.gguf", CancelGrace = TimeSpan.FromMilliseconds(300) };
            // 게임처럼 메인 스레드에서 만들고, 부르기는 다른 스레드(스레드 풀)에서 한다
            LlmUnityModelBackend backend = unity.Run(() => new LlmUnityModelBackend(runtime, options));

            Check(!backend.IsLoaded, "IsLoaded: 처음에는 false");
            Check(Error(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None))) == "InvalidOperationException",
                    "올리기 전에 생성하면 InvalidOperationException");

            Task.Run(() => backend.LoadAsync(CancellationToken.None)).Wait();
            FakeLlmSession first = runtime.Last;
            Check(backend.IsLoaded && runtime.Starts == 1 && runtime.LastOptions == options, "LoadAsync: 설정을 넘겨 모델을 올리고 IsLoaded true");
            Check(first.Calls.Count == 1 && first.Calls[0].User == "안녕" && first.Calls[0].MaxTokens == 8 && first.Calls[0].Grammar == null,
                    "LoadAsync: 짧게 한 번 생성해 미리 데움 (8토큰, 형식 제약 없이)");
            Task.Run(() => backend.LoadAsync(CancellationToken.None)).Wait();
            Check(runtime.Starts == 1, "LoadAsync: 이미 올라와 있으면 다시 올리지 않음");

            string raw = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)).Result;
            FakeLlmCall call = first.Calls[first.Calls.Count - 1];
            Check(raw == reply, "CompleteAsync: 모델이 낸 글을 고치지 않고 돌려줌");
            Check(call.System == prompt.System && call.User == prompt.User && call.Grammar == prompt.Grammar && call.MaxTokens == 160
                    && call.Options == options, "CompleteAsync: 시스템·사용자 메시지, GBNF 문법, 160토큰을 넘김");
            Check(backend.LastCall != null && backend.LastCall.FirstTokenMs >= 0 && backend.LastCall.FirstTokenMs <= backend.LastCall.ElapsedMs
                    && backend.LastCall.ReplyChars == reply.Length,
                    "LastCall: 첫 응답 " + (backend.LastCall != null ? backend.LastCall.FirstTokenMs : -1) + "ms ≤ 전체 "
                    + (backend.LastCall != null ? backend.LastCall.ElapsedMs : -1) + "ms");

            first.MaxActive = 0;
            Task.WaitAll(Enumerable.Range(0, 3).Select(i => Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None))).ToArray());
            Check(first.MaxActive == 1, "CompleteAsync: 동시에 불러도 모델에는 한 번에 하나씩 ← 최대 동시 " + first.MaxActive);

            // 취소: 모델에 멈추라고 하고 바로 돌아온다
            first.Delay = TimeSpan.FromSeconds(3);
            int cancels = first.Cancels;
            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(100);
            Stopwatch watch = Stopwatch.StartNew();
            string cancelled = Error(Task.Run(() => backend.CompleteAsync(prompt, cancel.Token)));
            watch.Stop();
            Check(cancelled == "OperationCanceledException" && first.Cancels == cancels + 1 && watch.ElapsedMilliseconds < 1500,
                    "취소되면 모델에 CancelRequests 하고 바로 OperationCanceledException ← " + cancelled + ", " + watch.ElapsedMilliseconds + "ms");

            // 멈추라고 해도 안 멈추는 모델: CancelGrace 뒤에는 기다리지 않고, 다음 요청을 받는다
            first.NeverReturn = true;
            var stuck = new CancellationTokenSource();
            stuck.CancelAfter(100);
            watch = Stopwatch.StartNew();
            string stuckError = Error(Task.Run(() => backend.CompleteAsync(prompt, stuck.Token)));
            watch.Stop();
            first.NeverReturn = false;
            first.Delay = TimeSpan.FromMilliseconds(20);
            Check(stuckError == "OperationCanceledException" && watch.ElapsedMilliseconds < 1500,
                    "안 멈추는 모델도 CancelGrace(300ms) 뒤 돌아옴 ← " + watch.ElapsedMilliseconds + "ms");
            Check(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)).Result == reply, "그 뒤 요청도 정상");

            // Unload: 진행 중인 요청을 취소하고 모델을 내린다
            first.Delay = TimeSpan.FromSeconds(3);
            Task<string> during = Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None));
            Thread.Sleep(150);
            backend.Unload();
            Check(Error(during) == "OperationCanceledException", "Unload: 진행 중인 요청은 취소");
            Thread.Sleep(100);
            Check(!backend.IsLoaded && first.Destroyed && first.Cancels > cancels + 1, "Unload: 모델에 멈추라고 하고 내림(Destroy), IsLoaded false");
            Check(Error(Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None))) == "InvalidOperationException",
                    "Unload 뒤 생성하면 InvalidOperationException");
            Task.Run(() => backend.LoadAsync(CancellationToken.None)).Wait();
            Check(backend.IsLoaded && runtime.Starts == 2 && runtime.Last != first, "Unload 뒤 LoadAsync 하면 새로 올림 (정화 미니게임 뒤)");

            // 올리는 도중 Unload: 올린 모델을 바로 내린다
            backend.Unload();
            runtime.StartDelay = TimeSpan.FromMilliseconds(500);
            Task loadingTask = Task.Run(() => backend.LoadAsync(CancellationToken.None));
            Thread.Sleep(150);
            backend.Unload();
            string loadError = Error(loadingTask);
            Thread.Sleep(100);
            Check(loadError == "OperationCanceledException" && !backend.IsLoaded && runtime.Last.Destroyed,
                    "올리는 도중 Unload 하면 올린 모델을 내리고 IsLoaded false ← " + loadError);
            runtime.StartDelay = TimeSpan.Zero;

            // 올리기 실패
            runtime.StartError = new InvalidOperationException("LLM 이 모델 'gemma.gguf' 를 올리지 못했다");
            Check(Message(Task.Run(() => backend.LoadAsync(CancellationToken.None))).Contains("올리지 못했다") && !backend.IsLoaded,
                    "모델을 못 올리면 이유를 담은 예외, IsLoaded false");
            runtime.StartError = null;
            runtime.WarmUpError = new InvalidOperationException("문법 오류");
            string warmError = Message(Task.Run(() => backend.LoadAsync(CancellationToken.None)));
            Thread.Sleep(100); // 내리기는 메인 스레드에 맡겨 두므로 잠깐 기다린다
            Check(warmError.Contains("문법 오류") && !backend.IsLoaded && runtime.Last.Destroyed,
                    "미리 데우기가 실패하면 올린 모델을 내리고 예외");
            runtime.WarmUpError = null;

            // 형식 제약 고르기
            Check(GrammarSent(unity, runtime, LlmUnityGrammar.JsonSchema, prompt) == prompt.JsonSchema, "Grammar = JsonSchema 면 JSON 스키마를 넘김");
            Check(GrammarSent(unity, runtime, LlmUnityGrammar.None, prompt) == null, "Grammar = None 이면 형식 제약 없이");

            // 요청 관리자까지
            Task.Run(() => backend.LoadAsync(CancellationToken.None)).Wait();
            runtime.Last.Delay = TimeSpan.FromMilliseconds(20);
            var manager = new NpcRequestManager(new SlmReactionGenerator(backend, new PromptAssembler(personas, actions),
                    new NpcOutputValidator(new NpcLineFilter(forbidden))), new RuleBasedReactionGenerator(lines, new Random(7)));
            NpcResponse response = Task.Run(() => manager.RequestAsync(Tap(8))).Result;
            Check(response.ShouldShow && response.Reaction.Source == ReactionSource.Slm && response.Reaction.Line == "어흠, 오셨구려.",
                    "요청 관리자 → SLM 생성기 → LLMUnity 백엔드로 이어짐");

            // 모든 LLMUnity 호출이 메인 스레드에서 일어났는지
            List<int> threads = runtime.AllThreads();
            Check(threads.Count > 10 && threads.All(id => id == unity.ThreadId),
                    "LLMUnity 호출 " + threads.Count + "번이 모두 Unity 메인 스레드에서 (다른 스레드에서 불러도)");
            backend.Unload();
        }
    }

    private static string GrammarSent(UnityThread unity, FakeLlmRuntime runtime, LlmUnityGrammar mode, NpcPrompt prompt)
    {
        var options = new LlmUnityOptions { Model = "gemma.gguf", Grammar = mode, WarmUp = false };
        LlmUnityModelBackend backend = unity.Run(() => new LlmUnityModelBackend(runtime, options));
        Task.Run(() => backend.LoadAsync(CancellationToken.None)).Wait();
        runtime.Last.Delay = TimeSpan.FromMilliseconds(10);
        Task.Run(() => backend.CompleteAsync(prompt, CancellationToken.None)).Wait();
        string grammar = runtime.Last.Calls[runtime.Last.Calls.Count - 1].Grammar;
        backend.Unload();
        return grammar;
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
            StartDelay = TimeSpan.Zero;
        }

        public int Starts;
        public LlmUnityOptions LastOptions;
        public TimeSpan StartDelay;
        public Exception StartError;
        public Exception WarmUpError;

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
                sessions.Add(session);
            }
            if (StartDelay > TimeSpan.Zero)
            {
                await Task.Delay(StartDelay); // 진짜 LLMUnity 처럼 메인 스레드로 돌아와 이어진다
            }
            Record();
            return session;
        }
    }

    private sealed class FakeLlmSession : ILlmUnitySession
    {
        private readonly FakeLlmRuntime runtime;
        private readonly string reply;
        private readonly Exception warmUpError;
        private TaskCompletionSource<string> pending;
        private int active;

        public FakeLlmSession(FakeLlmRuntime runtime, string reply, Exception warmUpError)
        {
            this.runtime = runtime;
            this.reply = reply;
            this.warmUpError = warmUpError;
            Delay = TimeSpan.FromMilliseconds(30);
        }

        public readonly List<FakeLlmCall> Calls = new List<FakeLlmCall>();
        public TimeSpan Delay;
        public bool NeverReturn;
        public int MaxActive;
        public int Cancels;
        public bool Destroyed;

        public Task<string> ChatOnceAsync(string system, string user, string grammar, int maxTokens, LlmUnityOptions options, Action onFirstToken)
        {
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
            var done = new TaskCompletionSource<string>();
            pending = done;
            if (NeverReturn)
            {
                return done.Task; // CancelRequests 해도 끝나지 않는 모델
            }
            // 첫 글자 알림은 끝나기 전에 꼭 한 번 온다 (타이머 해상도 때문에 따로 걸면 순서가 뒤바뀔 수 있다)
            Task.Delay(Delay).ContinueWith(_ =>
            {
                if (onFirstToken != null)
                {
                    onFirstToken();
                }
                done.TrySetResult(reply);
            });
            return done.Task.ContinueWith(t =>
            {
                Interlocked.Decrement(ref active);
                return t.Result;
            });
        }

        public void CancelRequests()
        {
            runtime.Record();
            Interlocked.Increment(ref Cancels);
            TaskCompletionSource<string> current = pending;
            if (current != null && !NeverReturn)
            {
                current.TrySetResult(""); // 멈추면 지금까지의 글(여기서는 빈 글)을 돌려준다
            }
        }

        public void Destroy()
        {
            runtime.Record();
            Destroyed = true;
        }
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
            bool ok = parsed[i].Id == id && got.RelicId == want.RelicId && got.Mode == want.Mode && got.Input == want.Input
                      && got.Situation == want.Situation
                      && Join(got.Candidates.Select(c => c.ActionId + ":" + string.Join("|", c.TargetIds.ToArray())))
                         == Join(want.Candidates.Select(c => c.ActionId + ":" + string.Join("|", c.TargetIds.ToArray())))
                      && Join(got.Facts.Select(f => f.Key + "=" + f.Value)) == Join(want.Facts.Select(f => f.Key + "=" + f.Value))
                      && Join(got.Memory.Select(m => m.DaysAgo + ":" + m.ActionId + ":" + m.Summary))
                         == Join(want.Memory.Select(m => m.DaysAgo + ":" + m.ActionId + ":" + m.Summary))
                      && parsed[i].Expected.SequenceEqual(((IEnumerable)scenes[i]["expected"]).Cast<string>());
            NpcPrompt a = assembler.Build(got);
            NpcPrompt b = assembler.Build(want);
            ok &= a.System == b.System && a.User == b.User;
            Check(ok, id + " 실기기 시험 요청이 Verify 의 요청·프롬프트와 같음");
            if (ok)
            {
                same++;
            }
            Check(parsed[i].CreateRequest() != got, id + " 돌릴 때마다 새 요청");
        }
        Console.WriteLine("같은 요청·프롬프트: " + same + "/" + scenes.Count);
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
            new KeyValuePair<string, NpcContextInput>("구름결 안부·이틀 전 관리", Owner(5, NpcInputs.AskStatus, "CLEAR", 2)),
            new KeyValuePair<string, NpcContextInput>("밤바다 안부·지난 이야기", Owner(6, NpcInputs.AskStatus, "CLEAR", 5,
                    new MemoryEntry(3, NpcActions.TellStory, "주인에게 자개 박기 이야기를 들려줬다"))),
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
        Check(Acts(tap) == "greet" && tap.Request.Situation.Contains("탭했다") && FactsOf(tap) == "awakening=각성",
                "탭: 인사 하나, 사실은 각성 단계만 (한국어 값) ← " + FactsOf(tap));

        // 안부: 우선순위 ① 흐려짐 · 재봉인
        NpcContext faded = builder.Build(Owner(5, NpcInputs.AskStatus, "FADED", 9));
        Check(Acts(faded) == "answer_status" && faded.KeyFact == "faded"
                && faded.Request.Situation == "주인이 안부를 물었다. 너는 9일 동안 관리를 받지 못해 흐려졌다."
                && FactsOf(faded) == "clarity=흐려짐, last_care_days_ago=9", "안부·흐려짐: 상황 문장에 흐려진 사실 ← " + faded.Request.Situation);
        NpcContext resealed = builder.Build(Owner(2, NpcInputs.AskStatus, "RESEALED", 16));
        Check(resealed.KeyFact == "resealed"
                && resealed.Request.Situation.EndsWith("16일 동안 관리를 받지 못해 흐려지다 못해 다시 봉인되었다. 주인이 닦아 주어야 다시 깨어날 수 있다.")
                && FactsOf(resealed).Contains("clarity=재봉인"), "안부·재봉인 ← " + resealed.Request.Situation);
        NpcContext fadedWithMemory = builder.Build(Owner(5, NpcInputs.AskStatus, "FADED", 9,
                new MemoryEntry(1, NpcActions.TellStory, "주인에게 상감 무늬 이야기를 들려줬다")));
        Check(fadedWithMemory.KeyFact == "faded" && Acts(fadedWithMemory) == "answer_status" && fadedWithMemory.Request.Memory.Count == 0,
                "흐려졌으면 지난 대화보다 관리 부탁이 먼저 (기억은 넣지 않음)");

        // ② 3일 안 관리
        NpcContext recent = builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 2));
        Check(recent.KeyFact == "recent_care" && recent.Request.Situation == "주인이 안부를 물었다. 이틀 전에 주인이 너를 닦아 관리해 주었다."
                && FactsOf(recent) == "clarity=선명, last_care_days_ago=2", "안부·이틀 전 관리 ← " + recent.Request.Situation);
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 0)).Request.Situation.EndsWith("오늘 주인이 너를 닦아 관리해 주었다."),
                "오늘 관리했으면 '오늘'");
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 1)).Request.Situation.EndsWith("어제 주인이 너를 닦아 관리해 주었다."),
                "어제 관리했으면 '어제'");
        Check(builder.Build(Owner(5, NpcInputs.AskStatus, "CLEAR", 4)).KeyFact == "none", "나흘 전 관리는 '최근'이 아님 (3일 기준)");

        // ③ 지난 대화
        var told = new MemoryEntry(3, NpcActions.TellStory, "주인에게 자개 박기 이야기를 들려줬다");
        NpcContext recall = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, new MemoryEntry(6, NpcActions.TellStory, "주인에게 해외 소장 이야기를 들려줬다"), told));
        Check(recall.KeyFact == "recall" && Acts(recall) == "recall_talk"
                && recall.Request.Situation == "주인이 안부를 물었다. 너는 사흘 전 주인에게 자개 박기 이야기를 들려줬던 일이 떠올랐다."
                && recall.Request.Facts.Count == 0 && recall.Request.Memory.Count == 1 && recall.Request.Memory[0] == told,
                "안부·지난 대화: 가장 최근 기억 하나, 상관없는 상태는 뺌 ← " + recall.Request.Situation);
        Check(recall.MemoryToSave != null && recall.MemoryToSave.ActionId == NpcActions.RecallTalk, "꺼낸 뒤 '지난 대화를 꺼냈다'를 기억에 남김");
        NpcContext again = builder.Build(Owner(6, NpcInputs.AskStatus, "CLEAR", 5, told, recall.MemoryToSave));
        Check(again.KeyFact == "none" && Acts(again) == "answer_status", "바로 전에 지난 대화를 꺼냈으면 이번에는 평소 안부");
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
            Check(i == 0 ? story.Request.Memory.Count == 0 : story.Request.Memory.Count == 1 && story.Request.Memory[0].ActionId == NpcActions.TellStory,
                    "지난번 이야기 하나를 기억으로 넣음 (첫 번째는 없음)");
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
        Check(first.KeyFact == "first_visit" && Acts(first) == "wander_greet" && first.Request.Situation.StartsWith("처음 온 손님")
                && FactsOf(first) == "visit_count_30d=1, awakening=각성+", "손님·첫 방문: 인사만, 선명도는 넣지 않음 ← " + FactsOf(first));
        NpcContext regular = builder.Build(Guest(5, NpcInputs.Visit, 4));
        Check(regular.KeyFact == "regular_visit" && Acts(regular) == "wander_greet,wander_recall"
                && regular.Request.Situation.StartsWith("최근 30일 동안 4번 온 손님") && !FactsOf(regular).Contains("clarity"),
                "손님·단골: 단골 인사 후보까지, 방문 횟수를 상황 문장에");
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
            bool good = prompt != null && !prompt.User.Contains("story_index") && !prompt.User.Contains("FADED") && !prompt.User.Contains("AWAKENED")
                        && response.ShouldShow && context.Request.Candidates.Any(c => c.ActionId == response.Reaction.ActionId);
            ok += good ? 1 : 0;
            Check(good, sample.Key + ": 프롬프트(영어 값 없음)와 기본 대사까지 이어짐");
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

        // 실제 흐름: 요청 관리자가 버린 요청
        var backend = new FakeBackend();
        backend.LoadAsync(CancellationToken.None).Wait();
        var slm = new SlmReactionGenerator(backend, new PromptAssembler(personas, actions), new NpcOutputValidator(new NpcLineFilter(forbidden)));
        var manager = new NpcRequestManager(slm, new RuleBasedReactionGenerator(lines, new Random(7)));
        string reply = Out("tell_story", "player", "어흠, 이 몸의 이야기를 들려드리겠소.");
        var flow = new NpcContextBuilder(personas);

        backend.Delayed = c => Task.Delay(1000).ContinueWith(_ => reply);
        NpcContext closing = flow.Build(Owner(2, NpcInputs.ListenStory, "CLEAR", 2));
        Task<NpcResponse> pending = manager.RequestAsync(closing.Request);
        manager.CancelAll();
        NpcResponse closed = pending.Result;
        Check(!closed.ShouldShow && closing.Finish(closed) == null && flow.Build(Owner(2, NpcInputs.ListenStory, "CLEAR", 2)).StoryIndex == closing.StoryIndex,
                "화면을 닫아 버려진 이야기는 '안 들려줌'으로 돌아가 다음에 같은 실화 ← " + closed.Note);

        var pushed = new NpcContextBuilder(personas);
        int calls = 0;
        backend.Delayed = c => Interlocked.Increment(ref calls) == 1 ? Task.Delay(1000).ContinueWith(_ => reply) : Task.FromResult(reply);
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
        backend.Delayed = null;
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

        public Task<ReactionResult> GenerateAsync(ReactionRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("구현 오류");
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
        public Exception Throw;
        public Action OnComplete;
        public Func<CancellationToken, Task<string>> Delayed;   // 정해 두면 이것으로 답한다 (느린 런타임 흉내)
        public int Calls;
        public NpcPrompt LastPrompt;
        private bool loaded;

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
            return Task.FromResult(Reply);
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
