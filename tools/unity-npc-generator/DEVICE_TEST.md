# 실기기 시험 (LLMUnity)

폰 안에서 **Kanana 1.5 2.1B**가 도깨비 대사를 만들 수 있는지, 얼마나 빠른지, 메모리와 발열은 괜찮은지 재는 순서다.
시험 장면(`NpcDeviceTestRunner`)이 프롬프트 시험의 18장면을 폰에서 하나씩 돌려, 결과를 화면에 보여 주고 CSV로 저장한다.
안드로이드 기준으로 썼다. 

## 0. 시작 전에 알아 둘 것

| 항목 | 내용 |
|---|---|
| 모델 | **Kanana 1.5 2.1B Instruct** (카카오, Apache 2.0). 구조가 Llama 라 LLMUnity 3.0.3 의 llama.cpp(b8209)로 올라간다. Gemma 4 E2B 는 이 llama.cpp 가 구조를 몰라 뺐다. |
| 모델 파일 | `kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf` (약 1.5GB). PC 시험(LM Studio)에 쓴 것과 같은 파일이다.<br>주소: `https://huggingface.co/TaeyoungLeeJack/kanana-1.5-2.1b-instruct-2505-Q4_K_M-GGUF/resolve/main/kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf` (공식 GGUF 가 없어 변환본이다. 받기 전에 저장소 페이지에서 파일 이름을 확인한다) |
| 프롬프트 | `PromptAssembler` 가 `run.py` 와 같은 프롬프트를 만든다(2026-11-11 판, `tools/npc-prompt-test/PROMPT_CHANGES.md`). 출력 검사 전에 어미를 말투에 맞게 고치는 단계가 켜져 있다(`NpcOutputValidator.NormalizeSpeech`). |
| 안 되면 | ① 콘솔 로그(`unknown model architecture` 등)를 저장해 알린다. ② 같은 구조인 Mi:dm 2.0 Mini 로 흐름을 먼저 본다. ③ 런타임(LiteRT-LM 등)을 다시 검토한다. |
| 기기 | RAM 6GB 이상 기기부터(8GB 권장). 여유 저장 공간 **2GB 이상**, 첫 실행은 **Wi-Fi**에서 (모델 1.5GB를 받는다). |
| Unity 버전 | 2022.3.16f1 이상 (LLMUnity 3.0.3 요구) |

## 1. 코드 받기

1. 서버 저장소의 `feat/ai-npc` 브랜치를 받는다. 시험에 쓰는 것은 아래 두 곳이다.
   - `tools/unity-npc-generator/` — Unity에 넣을 C# 코드와 데이터, 이 문서
   - `tools/npc-prompt-test/data/scenes.json` — 시험 장면 18개
2. **이 저장소가 원본이다.** Unity 프로젝트에 복사한 파일을 직접 고치지 않는다. 고칠 것이 있으면 원본을 고치고 다시 복사한다 (두 곳이 달라지면 PC 시험 결과와 비교할 수 없다).

## 2. Unity 프로젝트 준비 (한 번만)

게임 프로젝트를 건드리지 않게 **시험용 새 프로젝트**에서 한다.

1. **빌드 모듈 설치**: Unity Hub → 설치된 에디터 → 모듈 추가 → **Android Build Support** (OpenJDK, Android SDK & NDK Tools 포함)
2. **새 프로젝트**: Unity Hub → New project → 3D 템플릿
3. **LLMUnity 설치**
   - PC에 **Git**이 설치돼 있어야 한다 (Package Manager가 git 주소로 받을 때 쓴다).
   - Window → Package Manager → `+` → Add package from git URL → 아래 주소를 넣는다. 우리 코드는 3.0.3의 API로 짰으므로 **버전을 고정**한다 (태그 이름은 GitHub의 LLMUnity Releases에서 확인).
     ```
     https://github.com/undreamai/LLMUnity.git#v3.0.3
     ```
4. **우리 코드 넣기** — `tools/` 아래에서 Unity 프로젝트의 `Assets/` 아래로 복사한다.

   | 가져올 것 | 넣을 곳 |
   |---|---|
   | `unity-npc-generator/Runtime/*.cs` (16개, `NpcLineChecks.cs` 포함) | `Assets/Scripts/Npc/` |
   | `unity-npc-generator/Unity/*.cs` (2개) | `Assets/Scripts/Npc/Unity/` |
   | `unity-npc-generator/Data/*.json` (4개) + `npc-prompt-test/data/scenes.json` | `Assets/Resources/Npc/` |

   PowerShell로 한 번에 (첫 줄 두 경로는 자기 것으로 바꾼다):
   ```powershell
   $proj = "C:\UnityProjects\NpcTest"; $src = "C:\jamkkaebi\tools"; New-Item -ItemType Directory -Force "$proj\Assets\Scripts\Npc\Unity", "$proj\Assets\Resources\Npc" | Out-Null; Copy-Item "$src\unity-npc-generator\Runtime\*.cs" "$proj\Assets\Scripts\Npc\"; Copy-Item "$src\unity-npc-generator\Unity\*.cs" "$proj\Assets\Scripts\Npc\Unity\"; Copy-Item "$src\unity-npc-generator\Data\*.json" "$proj\Assets\Resources\Npc\"; Copy-Item "$src\npc-prompt-test\data\scenes.json" "$proj\Assets\Resources\Npc\"
   ```
   - **`Verify/` 폴더는 넣지 않는다.** Unity 밖에서 쓰는 검사 프로그램이라 넣으면 컴파일 오류가 난다.
   - **`Resources` 폴더 이름은 그대로** 둔다. 코드가 `Resources.Load("Npc/…")`로 읽는다. `Scripts` 이름은 바꿔도 된다.
5. **심볼 켜기**: Edit → Project Settings → Player → Other Settings → Script Compilation → Scripting Define Symbols 에 `JAMKKAEBI_LLMUNITY` 를 넣고 Apply.
   - **대문자 그대로** 쓴다. 대소문자가 다르면 LLMUnity 연결부가 통째로 빠진 채 컴파일된다 (오류도 안 난다).
   - 플랫폼 탭마다 따로 저장된다. **PC(모니터) 탭**(에디터 시험)과 **Android 탭**(폰 빌드)에 각각 넣는다. 에디터는 File → Build Settings에서 지금 고른 플랫폼의 탭 값으로 컴파일한다.
6. **컴파일 확인**
   - Unity는 파일이 바뀌면 알아서 컴파일한다. 직접 돌리려면 Assets → Refresh (Ctrl + R). 오른쪽 아래 도는 표시가 사라지면 끝이다.
   - Window → General → Console (Ctrl + Shift + C)에서 **빨간 줄 0개**면 성공이다.
   - 심볼이 정말 켜졌는지: `Assets/Scripts/Npc/Unity/LlmUnityRuntime.cs` 9번째 줄 `{` 아래에 `이건_일부러_넣은_오류;` 를 넣고 Ctrl + R → **빨간 줄이 생기면 켜진 것**이다. 확인 뒤 그 줄을 지우고 다시 0개인지 본다.
7. **모델 등록**
   - 빌드에 넣지 않을 장면(예: `ModelSetup`)을 하나 만들고, 빈 게임 오브젝트에 LLM 컴포넌트를 붙인다.
   - LLM 컴포넌트의 모델 관리자에서 **Download model** → 0단계의 모델 주소를 넣는다. 에디터 시험에 쓰려고 PC에도 1.5GB를 받는다(LM Studio 폴더의 같은 파일을 Load model 로 골라도 된다). 등록된 파일 이름(`kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf`)을 확인한다.
   - **Download on Build** 를 켠다. 1.5GB를 앱에 넣지 않고 폰에서 첫 실행 때 받는다. 주소로 등록해야 이 기능이 쓸 주소를 안다.
8. **시험 장면 만들기**
   - 새 장면 `NpcDeviceTest` → 빈 게임 오브젝트 → `NpcDeviceTestRunner` 컴포넌트를 붙인다.
   - Inspector 값:

     | 칸 | 값 | 설명 |
     |---|---|---|
     | Backend | `LlmUnity` | 폰 안 모델 |
     | Model File | `kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf` | 7번에서 등록한 이름 그대로 |
     | Gpu Layers | `-1` | 자동 (안드로이드는 0 = CPU, iOS는 99) |
     | Threads | `-1` | 모든 코어. 느리면 4로 바꿔 비교 |
     | Grammar | `Gbnf` | 형식 오류가 많으면 `JsonSchema` → `None`. Kanana 는 LM Studio 에서 JSON 스키마 형식 제약을 켜면 오류가 났다(PC 시험은 `--no-format`). LLMUnity 의 GBNF 는 다른 경로라 여기서 처음 확인한다 |
     | Slm Timeout Seconds | `30` | 처음엔 넉넉히 둬서 실제 걸리는 시간을 잰다. 3초로 두면 다 기본 대사로 넘어가 시간을 못 잰다 |
     | Repeat | `4` | 장면마다 반복 횟수. 반복마다 곁들일 거리가 바뀐다(거리 4개). PC 시험과 같게 4 |
     | Prepare System Prompt | 켬 | 장면마다 요청 전에 그 도깨비의 시스템 프롬프트를 미리 읽힌다(게임에서 탭해 확대할 때처럼). 미리 읽기 시간은 따로 재고, 요청 시간에는 사용자 메시지 읽기와 대사 쓰기만 남는다. 끄면 예전처럼 요청 시간에 섞인다 |
     | Auto Start | 켬 | 켜지자마자 시험 시작 |
     | 데이터 칸 5개 | 비움 | 비우면 `Resources/Npc/` 에서 읽는다 |

   - File → Build Settings 의 Scenes In Build 에는 이 장면만 넣는다.

## 3. 에디터(PC)에서 먼저

1. `NpcDeviceTest` 장면에서 Play 를 누른다.
2. 화면에 "모델 올림 …ms" 가 나오고 장면별 대사가 이어지면 성공이다. 끝나면 "=== 요약 ==="과 CSV 경로가 나온다.
   - 이야기 듣기 5장면(S10 · S11 · S18 · S19 · S20)은 AI 를 쓰지 않고 정해진 이야기 대사가 나온다(출처 "정해진"). 그래서 SLM 대사는 13장면 중 몇 개인지로 센다.
   - 장면 줄의 "미리 읽기 …ms" 가 시스템 프롬프트를 읽는 시간이다. 같은 도깨비가 이어지는 장면(S03→S04, S05→S06 등)은 0 에 가까워야 한다.
3. **"모델 올리기 실패"** 가 나오면 콘솔 로그를 본다. `unknown model architecture` 같은 말이 있으면 0단계의 "안 되면"으로 간다. **폰으로 넘어가지 말고** 콘솔 로그를 저장해 알린다.
4. 비교하고 싶으면 Backend 를 `LmStudio`(Lm Studio Model = `kanana-1.5-2.1b-instruct-2505`)로 바꿔 PC의 LM Studio 결과와 나란히 본다.
5. 장면 줄 끝의 메모에 "어미를 말투에 맞게 고침 ('…')"이 붙으면 모델이 쓴 대사를 고친 것이다. 요약에 "어미 고침 n건"으로 센다.

## 4. 안드로이드 폰

1. **폰 준비**
   - 설정 → 휴대전화 정보 → 소프트웨어 정보 → 빌드 번호를 7번 누르면 개발자 옵션이 생긴다.
   - 개발자 옵션 → USB 디버깅을 켠다. PC에 USB로 꽂고 폰에 뜨는 "허용"을 누른다.
2. **adb 준비**: Unity가 함께 깐 adb를 쓴다. 위치는 보통 `<Unity 에디터 폴더>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe` 다. 그 폴더에서 아래를 실행해 폰이 `device` 로 보이는지 확인한다.
   ```bash
   adb devices
   ```
3. **빌드 설정**: File → Build Settings → Android → Switch Platform. Player Settings → Other Settings 에서
   - Scripting Backend: **IL2CPP**
   - Target Architectures: **ARM64** 만
   - Internet Access: **Require** (모델 내려받기)
   - Package Name: 적어 둔다 (예: `com.DefaultCompany.NpcTest`). CSV를 가져올 때 쓴다.
   - Scripting Define Symbols(Android 탭)에 `JAMKKAEBI_LLMUNITY` 가 있는지 다시 본다.
4. **Build And Run** 을 누른다. 앱이 폰에 깔리고 켜진다.
5. **첫 실행**: 모델을 내려받는다(약 1.5GB, Wi-Fi에서). 화면에 "모델 내려받는 중 n%"가 나온다. 받는 동안 화면이 꺼지지 않게 둔다. 두 번째 실행부터는 받지 않는다.
6. **결과 보기**
   - 폰 화면에 장면별 결과와 요약이 나온다.
   - PC에서 실시간 로그:
     ```bash
     adb logcat -s Unity
     ```
     `[NPC시험]` 으로 시작하는 줄이 시험 결과다.
   - CSV 가져오기 (파일 이름 `npc_device_test_날짜_시각.csv`, 경로는 화면 마지막 줄에 나온다):
     ```bash
     adb pull /sdcard/Android/data/<패키지 이름>/files/
     ```
7. **발열 보기**: 시험이 도는 동안
   ```bash
   adb shell dumpsys thermalservice
   ```
   Thermal Status 가 0(정상)에서 올라가는지 본다. 배터리 온도는 `adb shell dumpsys battery` 의 temperature(0.1도 단위).
8. **메모리**: 화면의 "메모리 최대 …MB (앱 전체, 모델 포함)" 를 적는다.
9. **설정을 바꿔 다시 재기**: 6단계 표를 보고 Threads(4), Grammar(`JsonSchema` → `None`)를 바꿔 다시 빌드해 비교한다. 바꾼 값은 기록표 비고에 적는다.
10. **(선택) LLMUnity로 모델이 안 뜰 때 흐름만 보기**: 폰이 PC의 LM Studio를 쓰게 해서 화면·요청 흐름을 먼저 확인할 수 있다. **이때 잰 속도는 PC 속도라 폰 성능 기록으로 쓰지 않는다.**
    - PC의 LM Studio에서 서버를 켜고 "Serve on Local Network"를 켠다. 폰과 PC는 같은 Wi-Fi에 둔다.
    - `NpcDeviceTestRunner` 의 Backend = `LmStudio`, Lm Studio Url = `http://<PC 내부 IP>:1234`, Lm Studio Model = `kanana-1.5-2.1b-instruct-2505`. LM Studio 에서 형식 제약 오류가 나면 Grammar 를 `None` 으로 둔다.
    - 연결이 막히면 Player Settings → Other Settings → Allow downloads over HTTP 를 Always allowed 로 바꿔 본다.


## 6. 결과로 정할 것

| 정할 것 | 어떻게 |
|---|---|
| 시간 초과(`SlmTimeout`, 지금 3초) | 요약의 "SLM 전체 시간 90%" 값을 본다(미리 읽기를 켰으면 시스템 프롬프트는 이미 읽힌 뒤의 시간이다). 대사가 늦게 뜨면 어색하므로, 90% 값이 3초 안팎이면 그 값에 여유를 조금 더한다. 훨씬 길면 그 기기는 SLM을 끈다. |
| 미리 읽기가 먹히는지 | 요약의 "시스템 프롬프트 미리 읽기" 와, 같은 도깨비가 이어진 장면의 미리 읽기 시간이 0 에 가까운지 본다. 이어진 장면도 오래 걸리면 LLMUnity 가 앞부분을 다시 쓰지 않는 것이다. Prepare System Prompt 를 끄고 같은 장면의 전체 시간과도 비교한다. |
| 어미 고침 | 요약의 "어미 고침 n건"과 CSV 메모의 고치기 전 대사를 본다. 고친 대사가 어색하면(예: "…드리오") 그 예를 알린다. 고쳐도 남은 어미는 "기본 대사로 대신: … SpeechMismatch"로 나온다. |
| 문장 자르기 | 요약의 "문장 잘라 씀 n건" 과 CSV 메모의 "긴 대사를 문장 단위로 잘라 씀 / 토큰 한도에서 끊긴 대사" 를 본다. 끊긴 대사가 많으면 `MaxTokens`(64)를 조금 늘린다. |
| SLM 을 끌 기기(`SlmEnabled`) | 모델을 못 올렸거나, 앱이 꺼졌거나(메모리), 90% 값이 너무 긴 기기. 기기 RAM(`SystemInfo.systemMemorySize`) 같은 기준으로 묶는다. |
| 형식 제약(Grammar) | "기본 대사로 대신" 이유에 `InvalidOutput NotJson` 이 많으면 GBNF 가 기기 llama.cpp 에서 안 먹는 것이다. Grammar 를 `JsonSchema`, 그다음 `None` 으로 바꿔 다시 잰다. |
| 스레드 수 | 안드로이드가 느리면 Threads 를 4로 바꿔 비교한다(모든 코어보다 고성능 코어만 쓰는 게 빠를 때가 있다). |
| 대사 품질 | CSV 의 대사를 `tools/npc-prompt-test` 의 채점표 방식으로 본다. PC 결과와 크게 다르면 기록한다. |

## 7. 기록과 돌려줄 것

기기마다 한 줄씩 채운다. 설정을 바꿔 다시 쟀으면 줄을 하나 더 쓰고 비고에 바꾼 값을 적는다.

| 기기 | RAM | 모델 올리기(ms) | SLM 대사 (x/52) | 미리 읽기 중앙값 / 최대 | 전체 시간 중앙값 / 90% | 첫 응답 중앙값 | 메모리 최대 | 발열 | 비고 |
|---|---|---|---|---|---|---|---|---|---|
| 예: Galaxy S2x | 12GB | | | | | | | | Kanana Q4_K_M, Threads -1, Grammar Gbnf, 미리 읽기 켬, Repeat 4 |

시험이 끝나면 아래를 모아서 넘긴다.
- 위 기록표
- 기기별 CSV 파일 (`npc_device_test_…csv`)
- 화면의 "=== 요약 ===" 부분 (사진이나 로그)
- 실패했으면 그때의 로그 (`adb logcat -s Unity` 출력, 에디터면 콘솔 로그)

## 문제가 생기면

| 증상 | 원인과 할 일 |
|---|---|
| "모델 올리기 실패", 로그에 `unknown model architecture` | LLMUnity 안의 llama.cpp 가 이 모델을 모른다. 0단계의 "안 되면"으로. |
| 앱이 켜지자마자 또는 모델을 올리다 꺼짐 | 메모리 부족. iOS는 Increased Memory Limit 를 넣었는지 본다. 더 작은 양자화(Q3)나 더 작은 모델로 비교한다. |
| 모델 내려받기가 멈추거나 실패 | Wi-Fi와 여유 저장 공간(2GB 이상)을 본다. 7번에서 모델을 **주소로** 등록했는지, Download on Build 를 켰는지 본다. |
| 모든 장면이 `NotJson` | 형식 제약 문제. Grammar 를 `JsonSchema` → `None` 으로 바꿔 본다. `None` 이면 시험 장면이 프롬프트에 출력 형식 설명을 싣는다(`PromptOptions.DescribeFormat`). |
| 모든 장면이 시간 초과 | Slm Timeout Seconds 를 늘려 실제 시간을 잰다. iOS는 Gpu Layers 99, 안드로이드는 Threads 4 를 시험한다. |
| "JAMKKAEBI_LLMUNITY 심볼이 없어…" | 2단계 5번. 그 플랫폼 탭에 **대문자로** 넣었는지 본다. |
| "데이터가 없다" | 2단계 4번. `Assets/Resources/Npc/` 에 JSON 5개(scenes 포함)가 있는지 본다. |
| 컴파일 오류 `The type or namespace name 'LLMUnity' could not be found` | LLMUnity 가 설치되지 않았거나, 우리 스크립트가 다른 Assembly Definition(asmdef) 안에 있어 LLMUnity 를 못 본다. asmdef 가 있으면 LLMUnity 참조를 추가한다. |
| 컴파일 오류 `'LLM' 또는 'LLMAgent' does not contain a definition for …` | LLMUnity 버전이 3.0.3 이 아니라 API 이름이 다르다. 3.0.3 으로 고정해 다시 설치하거나, 오류 줄(파일·줄 번호·메시지)을 알린다. 고칠 곳은 `LlmUnityRuntime.cs` 하나다. |
| 컴파일 오류가 `Program.cs`·`System.Web.Script` 쪽에서 남 | `Verify/` 를 넣었다. Unity 프로젝트에서 지운다. |
| `adb` 를 찾을 수 없음 | 4단계 2번의 Unity SDK 폴더에서 실행하거나, 그 폴더를 PATH 에 넣는다. |
