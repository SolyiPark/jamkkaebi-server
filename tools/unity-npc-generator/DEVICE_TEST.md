# 실기기 시험 (LLMUnity)

폰 안에서 작은 AI 모델이 도깨비 대사를 만들 수 있는지, 얼마나 빠른지, 메모리와 발열은 괜찮은지 재는 순서다.
시험 장면(`NpcDeviceTestRunner`)이 프롬프트 시험의 18장면을 폰에서 하나씩 돌려, 결과를 화면에 보여 주고 CSV로 저장한다.
안드로이드 기준으로 썼다. iPhone은 5단계를 본다.

## 0. 시작 전에 알아 둘 것

| 항목 | 내용 |
|---|---|
| **시험 모델** | **Kanana 1.5 2.1B Instruct** (카카오, Apache 2.0, 한국어 특화)<br>파일: `kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf` (약 1.5GB)<br>주소: `https://huggingface.co/TaeyoungLeeJack/kanana-1.5-2.1b-instruct-2505-Q4_K_M-GGUF/resolve/main/kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf` |
| 왜 Gemma 4가 아닌가 | LLMUnity 최신판(v3.0.3)의 llama.cpp(b8209)는 Gemma 4를 읽지 못한다 (b8637부터 지원). Qwen 3.5 2B는 올라가지만 한국어 말투가 무너져서 뺐다. |
| **코드 버전** | `main` 최신을 받는다. LLMUnity 모델 올리기 버그 3개(무한 대기, reasoning NullReference, 안드로이드에서 모델 파일 못 찾음)를 고친 코드가 있어야 한다. 예전 코드로는 폰에서 반드시 실패한다. |
| 기기 | 여유 저장 공간 **3GB 이상**, 첫 실행은 **Wi-Fi**에서 (모델 약 1.5GB를 받는다). |
| 시간 | Galaxy S22에서 18장면에 **30분 넘게** 걸렸다. 충전기를 꽂고, 화면이 꺼지지 않게 둔다. |
| Unity 버전 | 2022.3.16f1 이상 (LLMUnity 3.0.3 요구). 6000.3.23f1에서 확인했다. |
| iOS | **Mac과 Xcode가 있어야 한다.** |

## 1. 코드 받기

1. 서버 저장소의 `main` 브랜치를 받는다. 시험에 쓰는 것은 아래 두 곳이다.
   - `tools/unity-npc-generator/` — Unity에 넣을 C# 코드와 데이터, 이 문서
   - `tools/npc-prompt-test/data/scenes.json` — 시험 장면 18개
2. **이 저장소가 원본이다.** Unity 프로젝트에 복사한 파일을 직접 고치지 않는다. 고칠 것이 있으면 원본을 고치고 다시 복사한다 (두 곳이 달라지면 PC 시험 결과와 비교할 수 없다).

## 2. Unity 프로젝트 준비 (한 번만)

게임 프로젝트를 건드리지 않게 **시험용 새 프로젝트**에서 한다.

1. **빌드 모듈 설치**: Unity Hub → 설치된 에디터 → 모듈 추가 → **Android Build Support** (OpenJDK, Android SDK & NDK Tools 포함)
2. **새 프로젝트**: Unity Hub → New project → 3D 템플릿 (예: `NpcTest`)
3. **LLMUnity 설치**
   - PC에 **Git**이 설치돼 있어야 한다 (Package Manager가 git 주소로 받을 때 쓴다).
   - Window → Package Manager → `+` → Add package from git URL → 아래 주소. 우리 코드는 3.0.3의 API로 짰으므로 **버전을 고정**한다.
     ```
     https://github.com/undreamai/LLMUnity.git#v3.0.3
     ```
   - 설치 직후 LLMUnity가 엔진 라이브러리를 따로 내려받는다. 그동안 LLM 컴포넌트의 칸이 회색으로 막혀 있다 ("Setup Library" 진행 막대). 끝날 때까지 기다린다.
4. **우리 코드 넣기** — `tools/` 아래에서 Unity 프로젝트의 `Assets/` 아래로 복사한다.

   | 가져올 것 | 넣을 곳 |
   |---|---|
   | `unity-npc-generator/Runtime/*.cs` (15개) | `Assets/Scripts/Npc/` |
   | `unity-npc-generator/Unity/*.cs` (2개) | `Assets/Scripts/Npc/Unity/` |
   | `unity-npc-generator/Data/*.json` (4개) + `npc-prompt-test/data/scenes.json` | `Assets/Resources/Npc/` |

   Windows (PowerShell, 첫 줄 두 경로는 자기 것으로 바꾼다):
   ```powershell
   $proj = "C:\UnityProjects\NpcTest"; $src = "C:\jamkkaebi-server\tools"; New-Item -ItemType Directory -Force "$proj\Assets\Scripts\Npc\Unity", "$proj\Assets\Resources\Npc" | Out-Null; Copy-Item "$src\unity-npc-generator\Runtime\*.cs" "$proj\Assets\Scripts\Npc\"; Copy-Item "$src\unity-npc-generator\Unity\*.cs" "$proj\Assets\Scripts\Npc\Unity\"; Copy-Item "$src\unity-npc-generator\Data\*.json" "$proj\Assets\Resources\Npc\"; Copy-Item "$src\npc-prompt-test\data\scenes.json" "$proj\Assets\Resources\Npc\"
   ```
   Mac (터미널):
   ```bash
   proj=~/UnityProj/NpcTest; src=~/jamkkaebi-server/tools; mkdir -p "$proj/Assets/Scripts/Npc/Unity" "$proj/Assets/Resources/Npc" && cp "$src"/unity-npc-generator/Runtime/*.cs "$proj/Assets/Scripts/Npc/" && cp "$src"/unity-npc-generator/Unity/*.cs "$proj/Assets/Scripts/Npc/Unity/" && cp "$src"/unity-npc-generator/Data/*.json "$src"/npc-prompt-test/data/scenes.json "$proj/Assets/Resources/Npc/"
   ```
   - **`Verify/` 폴더는 넣지 않는다.** Unity 밖에서 쓰는 검사 프로그램이라 넣으면 컴파일 오류가 난다.
   - **`Resources` 폴더 이름은 그대로** 둔다. 코드가 `Resources.Load("Npc/…")`로 읽는다.
5. **심볼 켜기**: Edit → Project Settings → Player → Other Settings → Script Compilation → Scripting Define Symbols 에 `JAMKKAEBI_LLMUNITY` 를 넣고 Apply.
   - **대문자 그대로** 쓴다. 대소문자가 다르면 LLMUnity 연결부가 통째로 빠진 채 컴파일된다 (오류도 안 난다).
   - 플랫폼 탭마다 따로 저장된다. **PC 탭**(에디터 시험)과 **Android 탭**(폰 빌드)에 각각 넣는다.
6. **컴파일 확인**
   - Window → General → Console 에서 **빨간 줄 0개**면 성공이다.
   - 심볼이 정말 켜졌는지: `Assets/Scripts/Npc/Unity/LlmUnityRuntime.cs` 9번째 줄 `{` 아래에 `이건_일부러_넣은_오류;` 를 넣고 저장 → **빨간 줄이 생기면 켜진 것**이다. 확인 뒤 그 줄을 지운다.
7. **모델 등록**
   - 빌드에 넣지 않을 장면(예: `ModelSetup`)을 하나 만들고, 빈 게임 오브젝트에 LLM 컴포넌트를 붙인다.
   - 모델 관리자에서 **Download model** → 0단계의 **주소**를 넣는다. 에디터 시험에 쓰려고 PC에도 받는다. 등록된 파일 이름(`kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf`)을 확인한다.
   - Kanana 줄의 **Build** 를 켠다. **다른 모델이 등록돼 있으면 Build 를 끄거나 지운다** (켜 두면 폰에서 그 모델까지 받는다).
   - **Download on Start** 를 켠다 (예전 문서의 "Download on Build"가 3.0.3에서 이 이름이다). 모델을 앱에 넣지 않고 폰에서 첫 실행 때 받는다.
   - ⚠ **Download on Start 를 끄지 않는다.** 끄면 모델이 APK 안에 들어가는데, 첫 실행 때 LLMUnity가 1.5GB 파일을 통째로 메모리에 올려 꺼내다가 **앱이 꺼진다** (S22에서 확인).
8. **시험 장면 만들기**
   - 새 장면 `NpcDeviceTest` → 빈 게임 오브젝트 → `NpcDeviceTestRunner` 컴포넌트를 붙인다. 카메라도 하나 둔다.
   - Inspector 값:

     | 칸 | 값 | 설명 |
     |---|---|---|
     | Backend | `LlmUnity` | 폰 안 모델 |
     | Model File | `kanana-1.5-2.1b-instruct-2505-q4_k_m.gguf` | 7번에서 등록한 이름 그대로 (대소문자까지) |
     | Gpu Layers | `-1` | 자동 (안드로이드는 0 = CPU, iOS는 99) |
     | Threads | `4` | 고성능 코어만. 비교하려면 `-1`(모든 코어)로도 한 번 잰다 |
     | Grammar | `Gbnf` | 형식 오류가 많으면 `JsonSchema` → `None` |
     | Slm Timeout Seconds | `120` | **측정용.** 실제 걸리는 시간을 재려고 넉넉히 둔다. 게임 기본값은 3초다 |
     | Repeat | `1` | 장면마다 반복 횟수 |
     | Auto Start | 켬 | 켜지자마자 시험 시작 |
     | 데이터 칸 5개 | 비움 | 비우면 `Resources/Npc/` 에서 읽는다 |

   - 값을 바꾼 뒤 **장면을 저장**한다 (Ctrl/Cmd + S). Play 중에 바꾼 값은 Play를 끄면 되돌아간다.
   - Build Profiles(또는 Build Settings)의 Scene List 에는 이 장면만 넣는다.

## 3. 에디터(PC)에서 먼저

1. `NpcDeviceTest` 장면에서 Play 를 누른다.
2. 화면에 "모델 올림 …ms" 가 나오고 장면별 대사가 이어지면 성공이다. 끝나면 "=== 요약 ==="과 CSV 경로가 나온다. (참고: M 시리즈 Mac에서 장면당 첫 응답 약 1초)
3. **"모델 올리기 실패"** 가 나오면 콘솔 로그를 저장해 알린다. **폰으로 넘어가지 않는다.**

## 4. 안드로이드 폰

1. **폰 준비**
   - 설정 → 휴대전화 정보 → 소프트웨어 정보 → **빌드번호를 7번** 누르면 개발자 옵션이 생긴다.
   - 개발자 옵션 → **USB 디버깅**을 켠다.
2. **PC와 연결** (둘 중 하나)
   - **USB**: 데이터가 되는 케이블로 꽂고, 폰 잠금을 푼 상태에서 "USB 디버깅 허용" → **항상 허용**. 팝업이 안 뜨면 폰 알림창의 USB 설정을 "파일 전송"으로 바꾸거나, 충전 전용 케이블인지 의심한다.
   - **무선 (Android 11 이상, PC와 폰이 같은 Wi-Fi)**: 개발자 옵션 → **무선 디버깅** 켜기 → "페어링 코드로 기기 페어링" 화면의 `IP:포트`와 6자리 코드로 `adb pair IP:포트` → 무선 디버깅 화면 위쪽의 **다른** `IP:포트`로 `adb connect IP:포트`. 학교 Wi-Fi처럼 기기끼리 통신을 막는 곳에서는 안 될 수 있다.
3. **adb**: Unity가 함께 깐 것을 쓴다.
   - Windows: `<Unity 에디터 폴더>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`
   - Mac: `/Applications/Unity/Hub/Editor/<버전>/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`
   - 경로가 다르면 Unity Hub → Installs → 에디터의 ⚙ → **Show in Finder/Explorer** 로 설치 폴더를 열어 `PlaybackEngines/AndroidPlayer/SDK/platform-tools` 를 찾는다.
   - `adb devices` 로 폰이 `device` 로 보이면 된다. `unauthorized` 면 폰에서 허용을 누른다.
4. **빌드 설정**: Android 플랫폼으로 전환(Switch Platform). Player Settings → Other Settings 에서
   - Scripting Backend: **IL2CPP**
   - Target Architectures: **ARM64** 만
   - Internet Access: **Require** (모델 내려받기)
   - Package Name: 적어 둔다 (예: `com.DefaultCompany.NpcTest`). CSV를 가져올 때 쓴다.
   - Scripting Define Symbols(Android 탭)에 `JAMKKAEBI_LLMUNITY` 가 있는지 다시 본다.
   - Run Device 목록에서 **Refresh** 를 눌러 폰 이름이 보이는지 확인한다.
5. **Build And Run** 을 누른다. 처음에는 APK 저장 위치를 묻는다. 프로젝트 폴더 안에 `Builds/` 를 만들어 저장한다 (`Assets/` 안은 피한다). 첫 빌드는 10~20분 걸릴 수 있다. "출처를 알 수 없는 앱" 경고가 뜨면 설치를 허용한다.
6. **첫 실행**: 모델(약 1.5GB)을 내려받는다. 화면에 "모델 내려받는 중 n%"가 나온다. 받는 동안 화면이 꺼지지 않게 둔다. 두 번째 실행부터는 받지 않는다.
7. **결과 보기**
   - 폰 화면에 장면별 결과와 요약이 나온다.
   - 실시간 로그: `adb logcat -s Unity` (`[NPC시험]` 으로 시작하는 줄이 시험 결과다)
   - CSV 가져오기 (파일 이름 `npc_device_test_날짜_시각.csv`):
     ```bash
     adb pull /storage/emulated/0/Android/data/<패키지 이름>/files/ ./DeviceResults/
     ```
8. **발열**: 시험이 도는 동안 `adb shell dumpsys thermalservice` 의 Thermal Status 가 0(정상)에서 올라가는지, `adb shell dumpsys battery` 의 temperature(0.1도 단위)를 본다.
9. **메모리**: 화면의 "메모리 최대 …MB (앱 전체, 모델 포함)" 를 적는다.
10. **설정을 바꿔 다시 재기**: Threads(`4` ↔ `-1`), Grammar(`JsonSchema` → `None`)를 바꿔 다시 빌드해 비교한다. 바꾼 값은 기록표 비고에 적는다.
11. **앱이 이상하게 멈추거나 꺼졌으면** 다시 시험하기 전에 설정 → 애플리케이션 → 앱 → 저장공간 → **데이터 삭제** 를 한다. 받다 만 모델 파일이 남아 있으면 계속 멈춘다.

## 5. iPhone (Mac 필요)

1. Xcode 설치, Xcode → Settings → Accounts 에 Apple ID 로그인 (무료 계정도 7일짜리 개발 서명 가능).
2. Unity → iOS 로 전환 → Build. Scripting Define Symbols 의 iOS 탭에도 `JAMKKAEBI_LLMUNITY` 를 넣는다.
3. 생긴 `.xcodeproj` → Unity-iPhone 타깃 → Signing & Capabilities → Team 선택, `+ Capability` → **Increased Memory Limit** 추가.
4. iPhone 연결 → 설정 → 개인정보 보호 및 보안 → **개발자 모드** 켜기. 첫 실행 때 설정 → 일반 → VPN 및 기기 관리에서 개발자 앱을 신뢰한다.
5. **Run(▶)**. 결과는 폰 화면과 Xcode 콘솔의 `[NPC시험]` 줄. 메모리는 Debug navigator(⌘7)의 Memory 게이지(화면 숫자는 iOS에서 모델을 빼고 잰 값이다). CSV는 Window → Devices and Simulators → 앱 → ⚙ → Download Container.

## 6. 결과로 정할 것

| 정할 것 | 어떻게 |
|---|---|
| **어디가 느린가** | 출처가 `SLM` 이고 첫응답ms 가 0 이상인 행에서만 나눠 본다: **첫응답ms** = 프롬프트를 읽는 시간, **전체ms − 첫응답ms** = 대사를 쓰는 시간. 출처가 `기본` 인 행(시간 초과·검사 실패)은 첫응답ms 가 `-1` 이라 이렇게 나눌 수 없고, 전체ms 는 기본 대사로 넘어가기까지 기다린 시간이다. S22에서는 프롬프트 읽기가 대부분이었다 (아래 참고). |
| 시간 초과(`SlmTimeout`, 게임 기본 3초) | 요약의 "SLM 전체 시간 90%" 값을 본다. 3초 안팎이면 그 값에 여유를 조금 더한다. 훨씬 길면 그 기기는 SLM을 끈다. |
| SLM 을 끌 기기(`SlmEnabled`) | 모델을 못 올렸거나, 앱이 꺼졌거나(메모리), 90% 값이 너무 긴 기기. 기기 RAM(`SystemInfo.systemMemorySize`) 같은 기준으로 묶는다. |
| 형식 제약(Grammar) | "기본 대사로 대신" 이유에 `InvalidOutput NotJson` 이 많으면 Grammar 를 `JsonSchema`, 그다음 `None` 으로 바꿔 다시 잰다. |
| 스레드 수 | Threads `4` 와 `-1` 중 빠른 쪽을 적는다. |
| 대사 품질 | CSV 의 대사를 `tools/npc-prompt-test` 의 채점표 방식으로 본다. |

## 7. 기록과 돌려줄 것

기기마다 한 줄씩 채운다. 설정을 바꿔 다시 쟀으면 줄을 하나 더 쓰고 비고에 바꾼 값을 적는다.

| 기기 | RAM | 모델 올리기(ms) | SLM 대사 (x/18) | 첫 응답 중앙값 | 전체 시간 중앙값 / 90% | 메모리 최대 | 발열 | 비고 |
|---|---|---|---|---|---|---|---|---|
| Galaxy S22 (참고) | 8GB | | 4/18 (시간 초과 10, 길이 초과 4) | 약 101초 | 약 110초 / 120초 넘음 | 2,089MB | | Kanana 1.5 2.1B, Threads 4, Timeout 120, Grammar Gbnf. 뒤로 갈수록 느려짐(발열 추정) |
| | | | | | | | | |

시험이 끝나면 아래를 모아서 넘긴다.
- 위 기록표 (기기 이름, Threads 값 포함)
- 기기별 CSV 파일 (`npc_device_test_…csv`)
- 화면의 "=== 요약 ===" 부분 (사진이나 로그)
- 실패했으면 그때의 로그 (`adb logcat -s Unity` 출력, 에디터면 콘솔 로그)

**참고: S22 결과에서 본 것**
- 대사를 만든 장면(출처 `SLM` 4개)은 첫 응답까지 72~111초, 그 뒤 대사를 쓰는 데 6~11초였다. **느린 원인은 프롬프트 읽기**다.
- 같은 도깨비가 연달아 나온 장면(S03→S04, S05→S06)은 뒤 장면이 43~50초로 절반쯤 빨랐다. 앞 장면에서 읽은 시스템 프롬프트를 다시 쓴 것으로 보인다.
- 이야기 장면(S10, S11, S18~S20)은 모두 120초 안에 끝나지 않았다.

## 문제가 생기면

| 증상 | 원인과 할 일 |
|---|---|
| 에디터에서 "모델 올리는 중"에서 멈춤 | 예전 코드다. 1단계대로 `main` 최신을 받아 다시 복사한다. |
| "모델 올리기 실패 … Object reference not set to an instance of an object" | 예전 코드다 (reasoning 설정 순서 버그). `main` 최신으로. |
| 폰에서 "모델 올리기 실패", `Could not find file "/….gguf"` | 예전 코드다 (안드로이드 모델 준비 순서 버그). `main` 최신으로. |
| 폰에서 "모델 올리는 중"에 앱이 꺼지고, 다시 켜면 멈춤 | Download on Start 를 끄고 빌드했다 (모델을 꺼내다 메모리 부족). 켜고 다시 빌드한 뒤, 앱 **데이터 삭제** 후 실행. |
| "모델 올리기 실패", 로그에 `unknown model architecture` | LLMUnity 안의 llama.cpp 가 이 모델을 모른다 (Gemma 4 등). 0단계의 시험 모델을 쓴다. |
| 모델 내려받기가 멈추거나 실패 | Wi-Fi와 여유 저장 공간(3GB 이상)을 본다. 모델을 **주소로** 등록했는지, Download on Start 를 켰는지 본다. |
| 모든 장면이 시간 초과 | Slm Timeout Seconds 를 120 으로 늘려 실제 시간을 잰다. 안드로이드는 Threads 4 를 시험한다. |
| 모든 장면이 `NotJson` | 형식 제약 문제. Grammar 를 `JsonSchema` → `None` 으로 바꿔 본다. |
| LLM 컴포넌트 칸이 회색으로 막혀 있음 | LLMUnity 가 엔진 라이브러리나 모델을 내려받는 중이다. 진행 막대가 끝날 때까지 기다린다. |
| Run Device 에 폰이 안 보임 | 4단계 2번. 폰 잠금을 풀고 디버깅 허용을 눌렀는지, 케이블이 데이터용인지 본다. 안 되면 무선 디버깅으로. |
| "JAMKKAEBI_LLMUNITY 심볼이 없어…" | 2단계 5번. 그 플랫폼 탭에 **대문자로** 넣었는지 본다. |
| "데이터가 없다" | 2단계 4번. `Assets/Resources/Npc/` 에 JSON 5개(scenes 포함)가 있는지 본다. |
| 컴파일 오류 `The type or namespace name 'LLMUnity' could not be found` | LLMUnity 가 설치되지 않았거나, 우리 스크립트가 다른 asmdef 안에 있어 LLMUnity 를 못 본다. asmdef 가 있으면 LLMUnity 참조를 추가한다. |
| 컴파일 오류 `'LLM' 또는 'LLMAgent' does not contain a definition for …` | LLMUnity 버전이 3.0.3 이 아니다. 3.0.3 으로 고정해 다시 설치한다. |
| 컴파일 오류가 `Program.cs`·`System.Web.Script` 쪽에서 남 | `Verify/` 를 넣었다. Unity 프로젝트에서 지운다. |
| `adb` 를 찾을 수 없음 | 4단계 3번의 Unity SDK 폴더에서 실행하거나, 그 폴더를 PATH 에 넣는다. |
