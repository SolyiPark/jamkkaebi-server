# 도깨비 반응 생성기 (Unity용)

AI 반응 생성기의 클라이언트 쪽 코드다. 생성기 두 가지와 그 부품이 들어 있다.

- **규칙 기반 생성기:** 기본 대사로 반응(행동 + 대사)을 만든다. 모델 없이 바로 돌아간다.
  - 지금은 클라이언트가 AI 없이 상호작용 흐름 전체를 먼저 만들 때 쓴다.
  - 나중에는 SLM이 시간 초과·실패했을 때, 저사양 기기에서 AI를 껐을 때 대신 쓴다.
- **컨텍스트 구성기:** 게임 상태(서버 응답 + 기기의 대화 기억)를 반응 요청으로 바꾼다. 후보를 정하고, **가장 중요한 사실 하나를 상황 문장에 적고**, 관련 있는 사실·기억만 넣고, 실화를 돌려 가며 고른다.
- **요청 관리자:** 반응 요청을 받아 SLM 으로 만들어 보고, 안 되면(시간 초과·저사양 기기·모델 미준비·검사 실패 등) 기본 대사로 대신한다. 화면 종료·NPC 전환·새 요청으로 밀린 결과는 버린다. **게임 코드는 이것만 부르면 된다.**
- **SLM 생성기:** 프롬프트 조립 → 모델 호출 → 출력 검사를 하고, 검사를 통과한 반응만 돌려준다. 모델 런타임은 `INpcModelBackend` 로 감싸 끼운다 — 폰은 `LlmUnityModelBackend`, 에디터·개발은 `LmStudioModelBackend`.
- **LLMUnity 백엔드 (출시용):** 폰 안에서 모델을 돌리는 `INpcModelBackend`. LLMUnity(llama.cpp)로 모델을 올리고 내리며, 모든 LLMUnity 호출을 Unity 메인 스레드에서 한다. 실기기 시험 순서는 [DEVICE_TEST.md](DEVICE_TEST.md).
- **LM Studio 백엔드 (개발용):** PC의 LM Studio 서버에 HTTP로 붙는 `INpcModelBackend`. Unity 에디터에서 진짜 모델(Gemma 4 E2B)로 흐름 전체를 돌려 볼 때 쓴다.
- **출력 검사기:** 모델 출력의 JSON, 후보 포함 여부, 대사 길이, 줄바꿈, 금칙어를 확인한다. 통과하지 못하면 `Failed(InvalidOutput)`.
- **프롬프트 조립기:** 요청 한 건을 SLM에 보낼 프롬프트로 만든다. `tools/npc-prompt-test/run.py`로 시험하며 다듬은 프롬프트와 **글자 하나까지 같은** 프롬프트를 만든다.
- **실화 돌리기(`StoryRotation`):** 이야기 듣기 때마다 도깨비의 실화 3개를 돌려 가며 고른다. 다 들려줄 때까지 같은 이야기를 반복하지 않는다.
- **공통 금칙어 필터(`NpcLineFilter`):** 욕설과 애착 형성을 해치는 말(밀어내기, 지나친 퉁명스러움 등)을 거른다. SLM 응답 검사에 쓴다.

대화의 목적은 플레이어가 상호작용을 통해 도깨비에게 **정이 들게 하는 것**이다. 프롬프트·기본 대사·금칙어 모두 이 원칙을 따른다.

## 파일

| 파일 | 내용 |
|---|---|
| `Runtime/NpcReactionContract.cs` | 공통 계약: `INpcReactionGenerator`, 요청·결과 타입, 선택지·행동·사실 키 상수 |
| `Runtime/RuleBasedReactionGenerator.cs` | 규칙 기반 생성기 |
| `Runtime/DefaultLineCatalog.cs` | 기본 대사 데이터 타입 |
| `Runtime/PromptAssembler.cs` | 프롬프트 조립기 (`NpcPrompt`, `PromptOptions` 포함) |
| `Runtime/NpcContentCatalogs.cs` | 페르소나·행동 카탈로그 데이터 타입 |
| `Runtime/NpcContextBuilder.cs` | 컨텍스트 구성기 (`NpcContextInput`, `NpcContext` 포함) |
| `Runtime/NpcRequestManager.cs` | 요청 관리자 (`NpcResponse` 포함) |
| `Runtime/SlmReactionGenerator.cs` | SLM 생성기와 모델 런타임 자리(`INpcModelBackend`) |
| `Runtime/LlmUnityModelBackend.cs` | LLMUnity 백엔드의 핵심 (`LlmUnityOptions`, `ILlmUnityRuntime`, `ILlmUnitySession`). Unity 없이 컴파일된다 |
| `Runtime/NpcTestScenes.cs` | 시험 장면(scenes.json)을 요청으로 바꾼다. 실기기 시험이 쓴다 |
| `Unity/LlmUnityRuntime.cs` | LLMUnity 를 직접 부르는 연결부 (LLM + LLMAgent). `JAMKKAEBI_LLMUNITY` 심볼이 있어야 컴파일된다 |
| `Unity/NpcDeviceTestRunner.cs` | 실기기 시험 장면용 컴포넌트. 18장면을 돌려 화면·로그·CSV 로 결과를 남긴다 |
| `DEVICE_TEST.md` | 실기기(안드로이드·iOS) 시험 순서 |
| `Runtime/LmStudioModelBackend.cs` | 개발용 LM Studio 백엔드 (`LmStudioOptions`, `LmStudioCall` 포함) |
| `Runtime/NpcOutputValidator.cs` | 출력 검사기 (`OutputCheck`, `OutputProblem`, 작은 JSON 파서 포함) |
| `Runtime/StoryRotation.cs` | 실화 이야기를 돌려 가며 고르는 기록 |
| `Runtime/NpcLineFilter.cs` | 공통 금칙어 필터 (`ForbiddenCatalog` 포함) |
| `Runtime/NpcContentLoader.cs` | `Data/` 의 JSON을 읽는 Unity 전용 도우미 |
| `Data/default_lines.json` | 도깨비별 기본 대사와 실화 이야기 대사 (규칙 기반 생성기) |
| `Data/personas.json` · `Data/actions.json` · `Data/forbidden.json` | 페르소나(실화 조각 포함)·행동 카탈로그·공통 금칙어. `tools/npc-prompt-test/data/` 의 복사본 |
| `Verify/` | Unity 없이 검증하는 도구 |

## Unity에 넣기

1. `Runtime/` 의 `.cs` 파일 열다섯 개를 `Assets/Scripts/Npc/` 같은 곳에, `Unity/` 의 두 개를 `Assets/Scripts/Npc/Unity/` 에 복사한다.
   LLMUnity 를 쓰면 Scripting Define Symbols 에 `JAMKKAEBI_LLMUNITY` 를 넣는다 (플랫폼 탭마다).
2. `Data/` 의 JSON 넷을 `Assets/Resources/Npc/` 에 복사한다 (TextAsset으로 읽힌다).
3. 클라이언트에 이미 `INpcReactionGenerator`(대사만 만드는 v1)가 있다면, 이 파일들은 `Jamkkaebi.Npc` 네임스페이스라 충돌하지 않는다. v1을 이 계약으로 바꿀지 따로 둘지 정한다.

C# 5 문법만 써서 낮은 Unity 버전에서도 컴파일된다. 외부 패키지는 필요 없다.
LM Studio 백엔드는 `System.Net.Http` 를 쓴다. 오래된 Unity에서 `HttpClient` 를 못 찾는다고 나오면 `Assets/csc.rsp` 에 `-r:System.Net.Http.dll` 한 줄을 넣는다.

## 규칙 기반 생성기 쓰기

```csharp
using Jamkkaebi.Npc;

var lines = NpcContentLoader.FromResources<DefaultLineCatalog>("Npc/default_lines");
INpcReactionGenerator generator = new RuleBasedReactionGenerator(lines);

var request = new ReactionRequest
{
    RequestId = System.Guid.NewGuid().ToString(),   // 생성 시도마다 새로
    InteractionId = interactionId,                  // 플레이어 입력 한 번 (재시도해도 같음)
    RelicId = 5,                                    // 구름결
    Mode = InteractionMode.Owner,
    Input = NpcInputs.AskStatus,
    Situation = "주인이 안부를 물었다."              // SLM용. 규칙 기반은 쓰지 않음
};
request.Candidates.Add(new ActionCandidate(NpcActions.AnswerStatus, NpcTargets.Player));
request.Facts.Add(new Fact(NpcFactKeys.Clarity, "FADED"));

ReactionResult result = await generator.GenerateAsync(request, cancellation.Token);
if (result.IsSuccess && result.RequestId == currentRequestId)
{
    PlayAnimation(result.ActionId);   // 아래 표대로
    ShowBubble(result.Line);          // 일반 텍스트로 (리치 텍스트 끔)
}
// → "조금 흐려졌어요… 살살 닦아 주실래요?"
```

**고르는 규칙**
1. 손님 관람: `visit_count_30d` 가 2 이상이면 `wander_recall`, 아니면 `wander_greet`.
2. 그 밖에는 도깨비의 `preferredActions` 순서로 후보에서 고른다. 예: 묘작이는 `thank_joyful`·`struggle_play`, 밤바다는 `thank_calm`·`struggle_protest`.
3. 해당하는 게 없으면 첫 후보. 대상은 고른 후보의 첫 대상.

**대사 규칙**
- `answer_status` 는 세 가지로 나뉜다.
  - 흐려짐·재봉인이면 관리 부탁
  - 3일 안에 관리를 받았으면 그 일을 떠올림
  - 그 밖에는 평소 상태
- `tell_story`
  - 페르소나가 있는 5종: 실화를 담은 이야기 대사(`stories`) 3개 중 요청의 `story_index` 번째를 쓴다. 번호가 없으면 스스로 돌려 가며 고른다(다 들려줄 때까지 반복 없음).
  - 페르소나가 없는 4종: 기본 대사 풀(`fallback`) 그대로. 자료 문장을 **바꾸지 않고** 싣고 " 제 이야기예요."를 붙인다.
- 같은 도깨비·같은 행동에서 바로 앞과 같은 대사는 피한다.
- 실패하면 예외 대신 실패 결과를 돌려준다: `NoCandidate`, `Cancelled`, `Error`.

## 컨텍스트 구성기 쓰기

게임 상태를 넣으면 요청 관리자에 넘길 요청이 나온다. 무엇을 AI 에게 보여 줄지는 AI 가 아니라 이 규칙이 정한다. 작은 모델은 목록에서 이번에 중요한 사실을 스스로 고르지 못했다.

```csharp
var builder = new NpcContextBuilder(personas, storyRotation);   // storyRotation: 규칙 기반 생성기와 같은 것을 넘긴다

NpcContext context = builder.Build(new NpcContextInput
{
    RelicId = spirit.relicId,
    Mode = InteractionMode.Owner,
    Input = NpcInputs.AskStatus,
    Today = exhibitionToday,                   // GET /api/exhibition 의 today ("2026-09-12", 서버 GameClock · KST 자정 기준)
    Clarity = spirit.clarity,                  // "FADED" — 관리하기면 관리 전 값
    AwakeningStage = spirit.awakeningStage,
    LastPurifiedAt = spirit.lastPurifiedAt,    // 관리하기면 이번 관리 전 값
    Memory = memoryStore.For(spirit.relicId)   // 이 도깨비와의 대화 기억 (요약, DaysAgo)
});

NpcResponse response = await manager.RequestAsync(context.Request, ct);
MemoryEntry memory = context.Finish(response);   // 꼭 한 번 부른다. 못 띄웠으면 고른 실화를 '안 들려줌'으로 되돌린다
if (response.ShouldShow)
{
    Show(response);
    if (memory != null)
    {
        memoryStore.Add(spirit.relicId, memory);   // 띄운 것만 기억한다
    }
}
// 손님 장면: Mode = Visitor, Input = NpcInputs.Visit, RecentVisitDays = friendExhibition.recentVisitDays
```

**가장 중요한 사실 고르기** — 선택지마다 위에서부터 조건을 보고 처음 맞는 것 하나만 상황 문장에 적는다 (`context.KeyFact` 에 무엇을 골랐는지 남는다).

| 선택지 | 순서 | 조건 | 상황 문장 | 후보 · 사실 · 기억 |
|---|---|---|---|---|
| 안부 묻기 | ① | 흐려짐 · 재봉인 | "주인이 안부를 물었다. 너는 9일 동안 관리를 받지 못해 흐려졌다." | 안부 답하기 · 선명도, 마지막 관리 |
| | ② | 3일 안에 관리 받음 | "… 이틀 전에 주인이 너를 닦아 관리해 주었다." | 안부 답하기 · 선명도, 마지막 관리 |
| | ③ | 7일 안의 대화 기억 (바로 전에 꺼낸 적 없음) | "… 너는 사흘 전 주인에게 옻칠 자개 이야기를 들려줬던 일이 떠올랐다." | 지난 대화 꺼내기 · 사실 없음 · 그 기억 하나 |
| | ④ | 그 밖 | "주인이 안부를 물었다." | 안부 답하기 · 선명도, 마지막 관리 |
| 관리하기 | ① | 관리 전 7일 넘게 지남 (흐려짐·재봉인) | "주인이 오랜만에 관리하기를 골랐고, 주인이 너를 관리해 주었다." | 감사 둘 · 관리 결과(흐려짐→선명), 마지막 관리 |
| | ② | 그 밖 | "주인이 관리하기를 골랐고, 주인이 너를 관리해 주었다." | 감사 둘 · 관리 결과 |
| 이야기 듣기 | – | 실화를 돌려 가며 고름 | "주인이 이야기 듣기를 골랐다." | 이야기 · 실화 한 문장, 번호 · 지난 이야기 하나 |
| 탭 · 끌어당기기 | – | – | 탭 / 끌어당김 | 인사 / 버둥 둘 · 각성 단계 / 없음 |
| 손님 관람 | ① | 30일에 2번 이상 | "최근 30일 동안 4번 온 손님이 전시관을 둘러보고 있다. …" | 걸으며 인사 + 단골 인사 · 방문 횟수, 각성 단계 |
| | ② | 그 밖 (첫 방문) | "처음 온 손님이 전시관을 둘러보고 있다. …" | 걸으며 인사 · 방문 횟수, 각성 단계 |

- **흐려짐이 지난 대화보다 먼저다.** 흐려진 도깨비가 지난 이야기만 하면 닦아 달라는 부탁을 놓친다.
- **재봉인은 이유와 할 일까지 적는다:** "너는 16일 동안 관리를 받지 못해 흐려지다 못해 다시 봉인되었다. 주인이 닦아 주어야 다시 깨어날 수 있다." "다시 봉인되었다"만 적었을 때는 Gemma가 관리를 한 번도 부탁하지 않았고(0/3), 이 문장으로는 4번 모두 부탁했다.
- **안부마다 같은 기억을 꺼내지 않는다.** 꺼낸 뒤에는 `MemoryToSave` 로 "지난 일을 다시 이야기했다"가 남고, 가장 최근 기억이 그것이면 다음 안부는 평소대로 답한다.
- **문턱값은 규칙 기반 생성기와 같다** (최근 관리 3일, 단골 2번). AI 가 실패해 기본 대사로 넘어가도 같은 내용으로 대답한다.
- **값은 한국어로 넣는다** (FADED → 흐려짐, AWAKENED_PLUS → 각성+). 영어 값만 주면 작은 모델이 뜻을 놓친다.
- **손님은 관람만 한다.** 손님이 탭·안부·관리·이야기·끌어당기기를 하면 후보가 비고, 요청 관리자가 아무것도 띄우지 않는다. 손님 장면에는 선명도를 넣지 않는다.
- **대화 기억 저장은 게임이 한다.** 구성기는 받은 기억(요약, DaysAgo)으로 판단하고, `Finish` 가 띄운 뒤 남길 기억(들려준 실화, 꺼낸 지난 대화)을 돌려줄 뿐이다. 안부 대답 같은 다른 대사를 요약해 남기는 일은 하지 않는다.
- **`Finish(response)` 는 요청마다 한 번 부른다.** 반응을 띄웠으면(SLM 이든 기본 대사든) 기억에 남길 것을 돌려주고, 띄우지 못했으면(화면 종료 · NPC 전환 · 같은 도깨비의 새 요청으로 밀림 · 기본 대사도 실패) 이 요청에 고른 실화를 "안 들려줌"으로 되돌린다.

## 실화 이야기 돌리기

어느 실화를 들려줄지는 **게임이 정한다.** 작은 모델에게 셋 중 안 한 것을 고르게 하면 자주 틀린다. 컨텍스트 구성기가 `StoryRotation` 으로 고르고(0 → 1 → 2 → 0 …), `verified_story`(실화 문장)와 `story_index`(번호)를 넣는다. `story_index` 는 프롬프트에 실리지 않고, SLM 이 실패해도 규칙 기반 생성기가 같은 번호의 이야기 대사를 고르게 한다.

- 앱을 다시 켜도 이어 가려면 `StoryRotation.History` 를 저장했다가 `Restore` 한다. 같은 `StoryRotation` 을 구성기와 규칙 기반 생성기에 함께 넘긴다.
- 요청을 만들 때 `Take` 로 들려준 것으로 기록하고 표(`StoryTicket`)를 받는다. 반응을 띄우지 못하면 `context.Finish` 가 `Return(ticket)` 으로 **고르기 전 자리로 되돌린다** — 처음 고른 실화면 "안 들려줌"으로, 다 들려준 뒤 다시 고른 실화면 원래의 오래된 자리로. 그사이 다른 요청이 고른 실화는 건드리지 않고, 같은 실화를 그 뒤에 다시 들려줬으면 되돌리지 않는다.
- 실화 문장은 30~45자로 짧게 쓴다. 60~75자로 길게 쓰면 Gemma 가 거의 그대로 옮겨 대사가 75자를 넘었다(길이 통과 3/15 → 짧게 줄여 12/15).
- 페르소나가 없는 4종은 `NpcContextInput.RealStory`(서버 `realStory`)를 문장 단위로 나눠 같은 방식으로 돌린다.
- 실화 조각은 서버 `realStory` 를 나눈 것이라 사실을 바꾸거나 보태지 않았다. 고치려면 `personas.json` 의 `stories` 와 `default_lines.json` 의 `stories` 를 같은 순서로 함께 고친다.

## 공통 금칙어로 거르기

```csharp
var filter = new NpcLineFilter(NpcContentLoader.FromResources<ForbiddenCatalog>("Npc/forbidden"));

ForbiddenMatch bad = filter.Find(line);   // 없으면 null. 로그에 bad.ToString() → "밀어내기:놔"
```

SLM 대사는 출력 검사기(`NpcOutputValidator`)가 이 필터로 거르므로 따로 부를 일은 없다.

- 묶음: 욕설·비속어, 비하·조롱, 밀어내기('놔', '저리 가', '손대지 마', '귀찮' 등), 지나친 퉁명스러움·무시('관심 없', '어쩌라고', '쳇' 등), 위협.
- 대사와 낱말의 공백을 지운 뒤 찾는다('손 대지 마'도 걸린다). `patterns` 는 정규식으로, '놓아.'·'놓아라'는 걸고 '여기 놓으면'은 통과시킨다.
- 금칙어는 프롬프트에 늘어놓지 않는다. 작은 모델은 프롬프트에 나온 말을 오히려 쓴다. 프롬프트에는 "정이 들게 하는 것이 목적"이라는 원칙만 적고, 걸러 내기는 이 필터가 한다.

## 요청 관리자 쓰기

게임 코드는 생성기를 직접 부르지 않고 요청 관리자만 부른다.

```csharp
var filter = new NpcLineFilter(NpcContentLoader.FromResources<ForbiddenCatalog>("Npc/forbidden"));
var slm = new SlmReactionGenerator(
        backend,                                   // INpcModelBackend: 폰은 LlmUnityModelBackend, 에디터·개발은 LmStudioModelBackend. 모델 없는 빌드면 slm 대신 null
        new PromptAssembler(personas, actions),
        new NpcOutputValidator(filter));
var manager = new NpcRequestManager(slm, new RuleBasedReactionGenerator(lines))
{
    SlmEnabled = deviceMeetsSpec,                  // 저사양 기기면 false → 처음부터 기본 대사
    SlmTimeout = TimeSpan.FromSeconds(3)           // 실기기 측정으로 정한다
};

await slm.LoadAsync(ct);                           // 전시관에 들어갈 때 (안 올라왔어도 요청은 기본 대사로 처리된다)

NpcResponse response = await manager.RequestAsync(request, screenClosing.Token);
if (response.ShouldShow)
{
    PlayAnimation(response.Reaction.ActionId);
    ShowBubble(response.Reaction.Line);
}
Debug.Log(response.Note);                          // 예: "SLM 시간 초과(3000ms) → 기본 대사"

manager.Cancel(relicId);                           // 다른 NPC 로 전환할 때
manager.CancelAll();                               // 전시관을 나가거나 정화 미니게임에 들어갈 때 (그 뒤 slm.Unload())
```

**기본 대사로 대신하는 경우** (`ShouldShow` true, `UsedFallback` true)

| 경우 | SLM 을 부르나 | `Note` |
|---|---|---|
| SLM 을 끈 기기(저사양) · 모델 없는 빌드 | 아니오 | SLM 꺼짐 … → 기본 대사 |
| 모델을 아직 안 받았거나 안 올라옴 (`IsReady` false) | 아니오 | 모델이 아직 준비되지 않음 → 기본 대사 |
| 시간 초과 | 예 (기다리지 않고 버림) | SLM 시간 초과(3000ms) → 기본 대사 |
| 출력 검사 실패 | 예 | SLM 실패 InvalidOutput (TooLong: 96자 …) → 기본 대사 |
| 페르소나 없는 4종 | 예 (모델 호출 전에 바로 실패) | SLM 실패 Unsupported … → 기본 대사 |
| 런타임 오류, 생성기 예외 | 예 | SLM 실패 Error (…) → 기본 대사 |

**아무것도 띄우지 않는 경우** (`ShouldShow` false)

| 경우 | 어떻게 알리나 |
|---|---|
| 화면을 닫음 | `RequestAsync` 에 넘긴 ct 를 취소 |
| 다른 NPC 로 전환 | `Cancel(relicId)` |
| 전시관을 나가거나 정화 미니게임에 들어감 | `CancelAll()` |
| 같은 도깨비에게 새 요청이 옴 | 자동 (옛 요청을 버린다) |
| 후보가 없음 | 자동 (생성기를 부르지 않는다) |

- 시간 초과와 화면 닫기는 둘 다 취소지만, 요청 관리자가 자기가 건 시간 초과인지 구분한다. 시간 초과만 기본 대사로 대신한다.
- 모델 런타임이 취소에 늦게 반응해도 `SlmTimeout` 이 지나면 기다리지 않는다. 늦게 끝난 생성 결과는 버린다.
- `RequestId` 는 시도(SLM, 기본 대사)마다 요청 관리자가 새로 붙이고, 결과의 `RequestId` 가 다르면 버린다. `InteractionId` 는 비어 있을 때만 붙인다.
- 도깨비마다 진행 중인 요청은 하나다. 손님 관람처럼 여러 도깨비가 동시에 말풍선을 띄우는 것은 서로 밀어내지 않는다.
- 예외를 던지지 않는다(요청이 null 일 때만 `ArgumentNullException`). Unity 메인 스레드에서 await 하면 결과도 메인 스레드로 돌아온다.

## SLM 생성기와 출력 검사

요청 관리자 안에서 쓰인다. 직접 부를 일은 시험할 때뿐이다.

**출력 검사** (`NpcOutputValidator`, 이 순서로 보고 처음 걸린 것을 돌려준다)

| 검사 | 걸리는 예 | 결과 |
|---|---|---|
| JSON | 대사만 씀, 문자열이 안 닫힘, 쉼표 빠짐. 앞뒤 잡글·`<think>`·코드 블록은 걷어 내고 읽는다 | `NotJson` |
| 후보 포함: 행동 | 후보에 없는 행동, 행동 칸에 "안부 묻기" | `UnknownAction` |
| 후보 포함: 대상 | 후보에 없는 대상, `player(주인)`, **다른 행동의** 대상 | `TargetMismatch` |
| 대사 있음 | 빈 대사, 따옴표뿐인 대사 | `EmptyLine` |
| 줄바꿈 | `\n` · `\r` (이스케이프든 날것이든) | `Newline` |
| 대사 길이 | 기본 40자, 손님 관람 25자, 이야기 75자 초과 (`MaxChars` 로 바꿈) | `TooLong` |
| 금칙어 | 공통 금칙어(`forbidden.json`)에 걸림 | `Forbidden` |

- 대사 앞뒤의 공백과 따옴표는 걷어 내고 검사한다(`run.py` 의 대사 정리와 같다).
- 길이 제한은 `run.py` 의 길이OK 판정과 같다. 프롬프트에 적는 길이(30·20·60자)보다 조금 넉넉하다. 시험에서 Gemma 이야기 대사 15건 중 2건이 이 제한에 걸렸다.
- 걸리면 `Failed(InvalidOutput)` 이고, `FailDetail` 에 무엇이 걸렸는지 적힌다("TooLong: 96자 (제한 75자)"). 다시 생성하지 않고 바로 실패를 돌려준다 — 기다리는 시간보다 규칙 기반 대사가 낫다.

**SLM 생성기의 실패 결과**

| FailReason | 언제 | 모델을 부르나 |
|---|---|---|
| `NotReady` | `LoadAsync` 전, `Unload` 뒤 | 아니오 |
| `Unsupported` | 페르소나가 아직 없는 도깨비(1, 3, 4, 7번) | 아니오 |
| `NoCandidate` | 후보가 없음 | 아니오 |
| `Cancelled` | 요청 전에 이미 취소됐거나, 생성 도중 취소(시간 초과)됨. 늦게 온 결과도 버린다 | 도중이면 예 |
| `InvalidOutput` | 출력 검사에 걸림 | 예 |
| `Error` | 런타임 예외. `FailDetail` 에 메시지 | 예 |

## 에디터에서 실제 모델로 돌리기 (LM Studio 백엔드)

PC에서 LM Studio 서버를 켜 두고(Developer 탭, 포트 1234) 백엔드만 바꿔 끼우면, 에디터에서 진짜 Gemma로 대사가 나온다.

```csharp
INpcModelBackend backend = new LmStudioModelBackend(new LmStudioOptions
{
    BaseUrl = "http://localhost:1234",
    Model = "google/gemma-4-e2b"          // LM Studio 모델 목록의 이름 그대로
});
var slm = new SlmReactionGenerator(backend, assembler, validator);
var manager = new NpcRequestManager(slm, rule) { SlmTimeout = TimeSpan.FromSeconds(30) };  // PC가 느리면 넉넉히
await slm.LoadAsync(ct);   // 모델이 목록에 있는지 확인하고, 짧게 한 번 불러 메모리에 올린다
```

| 설정 | 기본값 | 뜻 |
|---|---|---|
| `BaseUrl` | `http://localhost:1234` | LM Studio 서버 주소 |
| `Model` | `google/gemma-4-e2b` | 모델 이름. 없으면 `LoadAsync` 가 있는 모델 이름을 알려 주며 실패한다 |
| `Temperature` · `MaxTokens` | 0.7 · 160 | 프롬프트 시험(run.py)과 같다 |
| `DisableThinking` | true | 생각 모드 끄기(`reasoning_effort: none`). 켜면 Gemma 4가 대사를 비워 돌려준다 |
| `UseJsonSchema` | true | `prompt.JsonSchema` 를 형식 제약으로 넘긴다. 형식 제약 없는 기기 런타임을 흉내 내려면 false |
| `WarmUp` | true | `LoadAsync` 에서 모델을 미리 올린다. 안 하면 첫 요청이 모델 로드 때문에 시간 초과된다 |
| `Seed` | 없음 | 정하면 결과가 재현된다 |
| `HttpTimeout` | 120초 | HTTP 한 번의 한도. 게임의 시간 초과는 요청 관리자의 `SlmTimeout` 이 정한다 |

- 한 번에 하나씩 보낸다(기기 안의 모델처럼). `Unload` 하면 진행 중인 요청을 취소한다. LM Studio의 모델 자체는 내리지 않는다.
- 연결이 안 되거나, 모델이 없거나, HTTP 오류이거나, 생각만 하고 대사가 비면 이유를 담은 예외를 던진다. 요청 관리자는 이것을 기본 대사로 대신한다.
- `LastCall` 에 마지막 호출의 응답 시간과 입력·출력·생각 토큰 수가 남는다.
- **폰에서 써 보기:** LM Studio에서 로컬 네트워크 공개(Serve on Local Network)를 켜고 `BaseUrl` 을 PC의 내부 IP로 바꾼다. 안드로이드는 HTTP(암호화 없음) 연결을 막으므로 개발 빌드에서만 허용한다.
- **출시 빌드에는 넣지 않는다.** 만드는 곳을 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 로 감싸고, 출시 빌드는 기기 안 런타임의 백엔드를 쓴다.

## 폰에서 모델 돌리기 (LLMUnity 백엔드)

```csharp
// Unity 메인 스레드에서 만든다 (LLMUnity 호출을 이 스레드로 넘기려고 기억해 둔다)
var backend = new LlmUnityModelBackend(new LlmUnityRuntime(gameObject), new LlmUnityOptions
{
    Model = "gemma-4-E2B-it-Q4_K_M.gguf",   // LLM 모델 관리자에 등록한 GGUF 이름
    NumGpuLayers = Application.platform == RuntimePlatform.IPhonePlayer ? 99 : 0,
    DownloadProgress = p => ShowDownload(p)  // 첫 실행 때 모델 내려받기 진행률
});
var slm = new SlmReactionGenerator(backend, assembler, validator);
await slm.LoadAsync(ct);   // 전시관에 들어갈 때. 실패하면 예외 → 요청 관리자가 기본 대사로 처리
// …
slm.Unload();              // 정화 미니게임에 들어갈 때 (메모리를 돌려준다)
```

| 메서드 | 하는 일 |
|---|---|
| `IsLoaded` | 모델이 올라와 있는지 |
| `LoadAsync` | (첫 실행이면 내려받고) LLM + LLMAgent 를 만들어 모델을 올린 뒤, 짧게 한 번 생성해 데운다. 실패하면 이유를 담은 예외를 던지고 IsLoaded 는 false. 올리는 도중 Unload 하면 올린 모델을 바로 내린다 |
| `Unload` | 진행 중인 생성을 멈추고 LLM 오브젝트를 지워 메모리를 돌려준다. 다시 쓰려면 LoadAsync |
| `CompleteAsync` | 대화 기록 없이 한 번 생성해 모델이 낸 글을 그대로 돌려준다. 시스템·사용자 메시지, 형식 제약(기본 GBNF), 온도 0.7, 160토큰. 한 번에 하나씩, 취소되면 모델에 멈추라고 하고 `CancelGrace`(2초) 안에 안 멈춰도 기다리지 않는다 |

- LLMUnity 를 부르는 곳(`Unity/LlmUnityRuntime.cs`)은 v3.0.3 문서의 API 로 썼다. 버전이 바뀌어 컴파일이 안 되면 그 파일만 고친다.
- `LastCall` 에 첫 응답 시간(첫 글자까지)과 전체 생성 시간이 남는다.
- 설정: `NumGpuLayers`(iOS 99 · 안드로이드 0으로 시작), `NumThreads`, `ContextSize`(2048), `Grammar`(Gbnf → 안 먹으면 JsonSchema → None), `DisableThinking`(생각 모드 끔), `Seed`.
- **주의:** LLMUnity 최신판(v3.0.3)의 llama.cpp 는 Gemma 4 보다 먼저 나와 Gemma 4 를 못 올릴 수 있다. [DEVICE_TEST.md](DEVICE_TEST.md) 0단계에서 먼저 확인한다.

**`INpcModelBackend` 구현할 때** (다른 런타임. `LlmUnityModelBackend` · `LmStudioModelBackend` 가 참고 구현이다)
- `CompleteAsync` 는 모델이 낸 글을 **고치지 않고** 그대로 돌려준다. 검사는 생성기가 한다.
- 런타임이 형식 제약을 받으면 `prompt.Grammar`(GBNF) 나 `prompt.JsonSchema` 를 넘긴다.
- 무거운 일은 백그라운드 스레드에서 하고, 취소되면 `OperationCanceledException` 을 던진다.

## 프롬프트 조립기 쓰기

```csharp
var personas = NpcContentLoader.FromResources<PersonaCatalog>("Npc/personas");
var actions = NpcContentLoader.FromResources<ActionCatalog>("Npc/actions");
var assembler = new PromptAssembler(personas, actions);

NpcPrompt prompt = assembler.Build(request);
if (prompt == null)
{
    // 페르소나가 아직 없는 도깨비이거나 후보가 없다 → 규칙 기반 생성기로 대신한다
}
// prompt.System  → 시스템 메시지 (페르소나 + 규칙)
// prompt.User    → 사용자 메시지 (상황 + 알고 있는 것 + 후보 + 출력 형식)
// prompt.Grammar → llama.cpp · LLMUnity 의 grammar 에 넣는다 (GBNF)
// prompt.JsonSchema → LM Studio · llama.cpp 서버의 response_format 에 넣는다
```

**프롬프트에 들어가는 것** (시험에서 정한 원칙. 이유는 `tools/npc-prompt-test/README.md` 의 "프롬프트에서 정한 것")
- 대화의 목적(상대가 정이 들게 하는 것)을 규칙 맨 앞에 넣는다. 무뚝뚝·장난스러운 성격도 밀어내지 않고 부탁하듯 말하게 한다.
- 지금 상대(주인/손님)의 태도와 지금 선택지의 말투만 넣는다.
- 예시 대사는 말투만 참고하라고 적는다.
- 사실은 한국어 이름으로 넣는다. 대화 기억은 요약으로, 이야기 자료는 한 문장만 넣는다. `story_index` 는 넣지 않는다.
- 이야기 듣기에 대화 기억이 있으면 "이미 들려준 이야기는 다시 풀지 않고 이번 '내 내력'만 새로 들려준다"는 규칙을 더한다.
- 후보에는 ID만 적고, 대상의 뜻은 따로 적는다.
- 대사 길이: 기본 30자, 손님 관람 20자, 이야기 60자·2문장. `PromptOptions` 로 바꿀 수 있다.
- `request.Situation` 에는 컨텍스트 구성기가 이번에 **가장 중요한 사실 하나를 직접** 적는다 (예: "주인이 안부를 물었다. 이틀 전에 주인이 너를 닦아 주었다."). 작은 모델은 목록에서 중요한 사실을 스스로 골라내지 못했다.

**형식 제약 두 가지**
- `Grammar`(GBNF): 행동마다 그 행동의 대상만 허용하고, 대사에 줄바꿈을 막는다.
- `JsonSchema`: 행동과 대상을 각각 후보 안으로만 막는다.
- 형식 제약이 없는 런타임(LiteRT-LM 등)이면 둘 다 안 써도 된다. 시험에서 Gemma 4 E2B는 제약 없이도 형식을 100% 지켰다. 형식 제약이 있든 없든 출력 검사는 `SlmReactionGenerator` 안에서 늘 한다.

## 행동과 움직임

| 선택지 (`Input`) | 행동 | 움직임 |
|---|---|---|
| `tap` 탭 | `greet` | Idle |
| `ask_status` 안부 묻기 | `answer_status` / `recall_talk` | Idle |
| `care` 관리하기 (관리를 마친 뒤) | `thank_calm` / `thank_joyful` | Idle / Positive |
| `listen_story` 이야기 듣기 | `tell_story` | Idle |
| `drag` 끌어당기기 (주인만) | `struggle_protest` / `struggle_play` | Struggle |
| `visit` 손님 관람 (건드리지 않음) | `wander_greet` / `wander_recall` | Walking |

## 후보 필터가 지켜야 할 것

생성기와 조립기는 받은 후보 안에서만 고른다. 어떤 후보를 넣을지는 컨텍스트 구성기(`NpcContextBuilder`)가 아래대로 정한다.

- **안부 묻기:** 평소에는 `answer_status` 하나. 대화 기억이 있어 그 이야기를 꺼내기로 했으면 `recall_talk` 하나만 넣는다 (AI가 둘 중에서 고르지 않는다).
- **관리하기:** `thank_calm`, `thank_joyful` 둘 다 넣는다.
- **끌어당기기:** `struggle_protest`, `struggle_play` 둘 다 넣는다.
- **손님 관람:** `wander_greet` 는 늘, `wander_recall` 은 방문 기록이 있을 때만 넣는다.
- **손님 장면에는 선명도(`clarity`)를 넣지 않는다.** 친구에게 숨기는 정보다.

## 사실(Facts) 만드는 법

컨텍스트 구성기가 `NpcContextInput` 에서 아래처럼 만든다. 직접 만들 일은 없다.

| 키 | 값 | 어디서 |
|---|---|---|
| `clarity` | 선명 / 흐려짐 / 재봉인 | 내 전시관 `GET /api/exhibition` 의 `spirits[].clarity` (CLEAR / FADED / RESEALED) |
| `last_care_days_ago` | 정수 | 내 전시관 `spirits[].lastPurifiedAt` 에서 오늘까지 며칠인지 |
| `visit_count_30d` | 정수 | 친구 전시관 `GET /api/friends/{code}/exhibition` 의 `recentVisitDays` (지금 열람 포함, 첫 방문이면 1) |
| `verified_story` | 실화 한 문장 | 페르소나 `stories[i].fact` (없는 4종은 `realStory` 의 한 문장). `StoryRotation` 으로 돌려 가며 준다 |
| `story_index` | 정수 `i` | 위 실화의 번호. 프롬프트에는 실리지 않고, 규칙 기반 생성기가 같은 이야기 대사를 고를 때 쓴다 |
| `awakening` | 각성 / 각성+ / 완전 각성 | 내 전시관 `spirits[].awakeningStage` (AWAKENED / AWAKENED_PLUS / FULLY_AWAKENED) |
| `care_result` | 흐려짐→선명 등 | 관리하기에서 관리 전 선명도 → 선명 |

## 데이터 고치기

- **기본 대사** `Data/default_lines.json`
  - 도깨비 하나 = `spirits[]` 의 항목 하나. 없는 도깨비는 `fallback`(정중한 해요체)을 쓴다.
  - 모든 행동에 대사가 있어야 하고, `answer_status` 는 `faded` · `recent_care` · `default` 셋이 필요하다.
  - 기준: 30자 안팎(손님 관람 20자 이내), 도깨비 말투 어미 유지, 묘작이는 "좋은 일" 금지(이야기 대사는 예외), 밤바다는 주인에게 시키지 않는다.
  - 끌려갈 때(`struggle_protest`)도 밀어내지 않고 "살살 들어 줘"처럼 부탁한다. 모든 기본 대사는 공통 금칙어를 통과해야 한다(`Verify` 가 검사).
  - `stories`: 실화 이야기 대사 3개(70자 이하). 페르소나 `stories` 와 같은 순서로, 사실은 그대로 두고 말투만 입힌다. 페르소나가 있는 도깨비는 `tell_story` 풀을 두지 않는다.
- **페르소나·행동·금칙어** `Data/personas.json` · `Data/actions.json` · `Data/forbidden.json`
  - **원본은 `tools/npc-prompt-test/data/`** 다. 원본을 고쳐 시험한 뒤 여기로 복사한다.
  - 복사를 잊으면 `Verify` 의 사본 비교가 실패해서 알 수 있다.
- **아직 페르소나가 없는 4종**(금동연가7년명여래입상 1, 금관총 금관 3, 관촉사 미륵보살 4, 유서지보 7)
  - 기본 대사는 `fallback` 을 그대로 쓰고, 프롬프트 조립기는 null 을 돌려준다(→ 규칙 기반).
  - 페르소나가 정해지면 세 파일 모두에 추가한다.

## Unity 없이 확인하기

`Verify/verify.bat` 하나로 확인한다. 설치할 것은 없다. Windows에 기본으로 들어 있는 C# 컴파일러(.NET Framework 4.x의 csc, C# 5)와 Python만 쓴다.

```bash
cd tools/unity-npc-generator/Verify
```

```bash
./verify.bat
```

PowerShell이나 cmd에서는 `.\verify.bat` 로 실행한다.

LM Studio 서버를 켜 두었다면 실제 모델로도 돌려 볼 수 있다. 아래 검사를 마친 뒤 18장면을 요청 관리자 → SLM 생성기 → LM Studio 백엔드로 보내고, 장면마다 대사·출처(SLM/기본 대사)·응답 시간·토큰 수를 보여 준다.

```bash
./verify.bat --live
```

주소나 모델을 바꾸려면 `./verify.bat --live http://localhost:1234 google/gemma-4-e2b`. 모델 출력은 매번 달라서 장면별 결과는 보여 주기만 하고, 연결과 흐름(SLM 대사가 하나 이상 나오는지)만 검사한다.

**하는 일**
1. `run.py` 로 프롬프트 시험의 18장면 프롬프트를 뽑아 기준으로 저장한다 (`bin/expected_prompts.json`).
2. `Runtime/` 의 코드를 C# 5로 컴파일한다 (`bin/verify.exe`).
3. 검증한다. 마지막 줄이 `모두 통과` 면 성공이고, 실패하면 항목과 함께 종료 코드 1을 낸다.

**검증하는 것**

| 대상 | 내용 |
|---|---|
| 데이터 | `Data/` 의 사본 셋이 원본과 같은지, 5종 모두 실화 3개와 이야기 대사 3개가 짝이 맞는지, 이야기 대사 70자 이하 |
| 규칙 기반 생성기: 장면 | 18장면을 6번씩 돌려 기대 행동을 고르는지, 대상이 맞는지, 대사가 있는지, 바로 앞 대사를 반복하지 않는지 |
| 규칙 기반 생성기: 규칙 | 안부 세 가지 변형, 단골 기준, 도깨비 성향, 페르소나 없는 도깨비, 실패 결과(없는 요청·후보 없음·취소) |
| 실화 이야기 | 번호 없이 네 번 들으면 처음 세 번이 모두 다르고 네 번째에 처음 것으로 돌아오는지, `story_index` 를 따르는지, 페르소나 없는 도깨비는 자료 문장 그대로인지, 돌리기 기록 저장·복원 |
| 공통 금칙어 | 걸러야 할 말(옛 퉁명한 대사, 욕설, 띄어 쓴 욕설)은 걸리고 정상 대사('새끼 참새', '심심해 죽겠어', '여기 놓으면')는 통과하는지, 기본 대사·예시 대사 전부가 통과하는지 |
| 출력 검사 | JSON(잡글·생각·코드 블록 걷어 내기, 깨진 JSON), 후보 포함(행동·대상·행동과 대상의 짝), 빈 대사, 줄바꿈(이스케이프·날것), 길이 경계(40/41 · 25/26 · 75/76자), 금칙어, 장면별 길이 제한이 `run.py` 판정과 같은지, Gemma가 실제로 낸 대사 |
| 실화 되돌리기 | 표로 되돌리기(처음 고른 것 · 다시 고른 것의 원래 자리 · 그사이 고른 다른 실화는 그대로 · 그 뒤에 다시 들려줬으면 그대로 · 두 번 되돌리기), `Finish` 가 띄움/못 띄움에 따라 기억을 돌려주거나 되돌리는지, 화면 종료(CancelAll)와 같은 도깨비의 새 요청으로 버려진 이야기가 다음에 다시 나오는지 |
| 컨텍스트 구성기 | 선택지별 후보, 안부 우선순위(흐려짐·재봉인 → 3일 안 관리 → 지난 대화 → 평소)와 상황 문장, 오늘·어제·이틀 전 표현, 같은 기억 연달아 꺼내지 않기, 7일 넘은 기억 빼기, 관리 결과와 "오랜만에", 실화 0→1→2→0과 기억 요약, 페르소나 없는 도깨비의 realStory 나누기, 손님은 관람만·선명도 없음, 대표 상황 11개가 프롬프트(영어 값 없음)와 기본 대사까지 이어지는지, 기본 대사와 문턱값이 같은지 |
| 요청 관리자 | 가짜 모델로: 기본 대사로 대신하는 여섯 경우(모델 미준비·SLM 꺼짐·모델 없는 빌드·검사 실패·페르소나 없음·오류/예외), 시간 초과(취소를 무시하는 런타임도 기다리지 않음), 버리는 경우(화면 닫기·이미 취소·새 요청으로 밀림·NPC 전환·전체 취소·후보 없음), 다른 도깨비끼리는 안 밀림, RequestId·InteractionId |
| LM Studio 백엔드 | 가짜 서버로: 요청 본문(시스템·사용자 메시지가 프롬프트와 글자 하나까지 같음, 온도·토큰·생각 끔·JSON 스키마, 설정으로 빼기), 모델 목록 확인과 미리 올리기, 응답을 고치지 않고 돌려줌, 토큰 수 기록, 오류 메시지(모델 없음·서버 꺼짐·생각만 함·HTTP 오류·JSON 아님), 취소·Unload 취소, 한 번에 하나씩, 요청 관리자까지 이어짐 |
| LLMUnity 백엔드 | 가짜 LLMUnity 와 가짜 Unity 메인 스레드로: 기본 설정, 올리기(설정 전달·미리 데우기·두 번 안 올림), 생성(메시지·문법·토큰·글 그대로·첫 응답 시간), 한 번에 하나씩, 취소(멈추라고 하고 바로 돌아옴, 안 멈추는 모델도 CancelGrace 뒤 돌아옴), 내리기(진행 중 취소·Destroy·다시 올리기), 올리는 도중 내리기, 올리기·데우기 실패, 형식 제약 고르기, 요청 관리자까지, **LLMUnity 호출이 모두 메인 스레드에서** |
| 실기기 시험 장면 변환 | `NpcTestScenes` 가 18장면 모두 Verify 와 같은 요청·프롬프트를 만드는지 |
| SLM 생성기 | 가짜 모델로: 통과하면 Success(Slm), 검사에 걸리면 InvalidOutput + 이유, NotReady · Unsupported · NoCandidate · Error · Cancelled(요청 전·생성 도중), 모델을 부르지 않아야 할 때 부르지 않는지, 실패 뒤 규칙 기반 대신 |
| 프롬프트 조립기 | 18장면 모두 `run.py` 와 시스템·사용자 메시지·JSON 스키마가 글자 단위로 같은지 (다르면 몇 번째 글자부터 다른지 보여 준다), 애착 규칙이 늘 있는지, `story_index` 가 안 실리는지, 지난 이야기 규칙이 이야기+기억일 때만인지, 문법, null 경우, 예시 끄기 |

**검사를 늘리려면** `Verify/Program.cs` 에 `Check(조건, "설명")` 을 더한다. 장면을 늘리려면 `tools/npc-prompt-test/data/scenes.json` 에 장면을 넣으면 두 검사에 모두 들어간다.

**다른 방법**
- .NET SDK를 설치하면 `dotnet new nunit` 으로 테스트 프로젝트를 만들어 같은 코드를 `dotnet test` 로 돌릴 수 있다.
- Unity에 넣은 뒤에는 Unity Test Framework의 EditMode 테스트로 옮길 수 있다. `Runtime/` 코드는 UnityEngine을 쓰지 않아서 그대로 테스트된다.

**이 방법으로 확인하지 못하는 것**
- `NpcContentLoader`(JsonUtility로 읽기). Unity가 있어야 한다. Unity에 넣은 뒤 `FromResources<DefaultLineCatalog>("Npc/default_lines")` 가 도깨비 5 + fallback 을, `PersonaCatalog` 가 5종과 각 `stories` 3개를, `ForbiddenCatalog` 가 묶음 5개를 읽는지 한 번 확인한다.
- LLMUnity 연결부(`Unity/LlmUnityRuntime.cs`)와 실기기 시험 장면(`Unity/NpcDeviceTestRunner.cs`). Unity 와 LLMUnity 가 있어야 컴파일되어 여기서는 확인하지 못한다. Unity 2022.3 + LLMUnity 3.0.3 에서 `JAMKKAEBI_LLMUNITY` 를 켜고 컴파일되는 것(오류 0)은 따로 확인했다. 핵심 동작(`LlmUnityModelBackend`)은 가짜 LLMUnity 로 확인했다. 모델이 실제로 올라가는지와 기기 결과는 [DEVICE_TEST.md](DEVICE_TEST.md) 순서로 잰다.
- 기기 안에서 도는 모델의 속도·메모리·발열. PC의 LM Studio(Gemma 4 E2B)로는 `--live` 로 18장면을 끝까지 돌려 확인했다: SLM 대사 17/18, 1건(S20)은 79자라 출력 검사에서 걸러져 같은 실화의 기본 대사로 대신, 생각 토큰 0, PC 응답 중앙값 4.2초.
- `SlmTimeout` 기본값(3초)이 맞는지. 실기기에서 첫 응답·전체 생성 시간을 재서 정한다.
- `Grammar`(GBNF)가 실제 llama.cpp에서 받아들여지는지. 모양은 llama.cpp 문법 규칙에 맞췄지만, 런타임에 넣어 보지는 못했다.
