# 실기기 시험 (LLMUnity)

폰 안에서 Gemma가 도깨비 대사를 만들 수 있는지, 얼마나 빠른지, 메모리와 발열은 괜찮은지 재는 순서다.
시험 장면(`NpcDeviceTestRunner`)이 프롬프트 시험의 18장면을 폰에서 하나씩 돌려, 결과를 화면에 보여 주고 CSV로 저장한다.

## 0. 시작 전에 알아 둘 것

| 항목 | 내용 |
|---|---|
| **Gemma 4를 읽을 수 있나** | LLMUnity 최신판(v3.0.3, 2026년 3월)에 들어 있는 llama.cpp(b8209)는 Gemma 4(2026년 4월 출시)보다 먼저 나왔다. **Gemma 4 E2B를 못 올릴 가능성이 높다.** 1단계에서 에디터로 먼저 확인한다. |
| 안 되면 | ① LLMUnity 새 버전이 나왔는지 확인한다. ② LLMUnity가 지원하는 작은 모델(예: Qwen 3.5 2B)로 흐름과 속도를 먼저 재고, 그 모델로 `run.py` 품질 시험을 다시 한다. ③ 런타임(LiteRT-LM 등)을 다시 검토한다. |
| 모델 크기 | Gemma 4 E2B Q4_K_M 파일이 약 3.2GB다. LLMUnity는 모바일에 1~2B 모델을 권한다. RAM 8GB 이상 기기부터 시험한다. |
| Unity 버전 | 2022.3.16f1 이상 (LLMUnity 3.0.3 요구) |
| iOS | **Mac과 Xcode가 있어야 한다.** Windows만으로는 iOS 빌드를 못 한다. |

## 1. 프로젝트 준비 (한 번만)

1. **빌드 모듈 설치**: Unity Hub → 설치된 에디터 → 모듈 추가
   - Android Build Support (OpenJDK, Android SDK & NDK Tools 포함)
   - iOS Build Support
2. **LLMUnity 설치**: Window → Package Manager → `+` → Add package from git URL → `https://github.com/undreamai/LLMUnity.git`
3. **우리 코드 넣기**
   - `Runtime/*.cs` → `Assets/Scripts/Npc/`
   - `Unity/*.cs` → `Assets/Scripts/Npc/Unity/`
   - `Data/` 의 JSON 4개와 `tools/npc-prompt-test/data/scenes.json` → `Assets/Resources/Npc/`
4. **심볼 켜기**: Project Settings → Player → Other Settings → Scripting Define Symbols 에 `JAMKKAEBI_LLMUNITY` 를 넣는다. **Android 탭과 iOS 탭(그리고 PC 탭)에 각각** 넣는다. 이게 없으면 LLMUnity 연결부가 컴파일되지 않는다.
5. **모델 등록**
   - 빌드에 넣지 않을 장면(예: `ModelSetup`)을 하나 만들고, 빈 게임 오브젝트에 LLM 컴포넌트를 붙인다.
   - LLM 컴포넌트의 모델 관리자에서 GGUF 파일을 내려받거나(Download model) 가져온다(Load model). 파일 이름을 적어 둔다.
   - **Download on Build** 를 켠다. 3GB를 앱에 넣지 않고 첫 실행 때 내려받는다. 이때 모델 URL(Hugging Face 링크)이 등록돼 있어야 한다.
6. **시험 장면 만들기**
   - 새 장면 `NpcDeviceTest` → 빈 게임 오브젝트 → `NpcDeviceTestRunner` 컴포넌트를 붙인다.
   - Backend = `LlmUnity`, Model File = 5에서 적어 둔 GGUF 파일 이름.
   - File → Build Settings 의 Scenes In Build 에는 이 장면만 넣는다.

## 2. 에디터(PC)에서 먼저

1. `NpcDeviceTest` 장면에서 Play 를 누른다.
2. 화면에 "모델 올림 …ms" 가 나오고 장면별 대사가 이어지면 성공이다.
3. **"모델 올리기 실패"** 가 나오면 콘솔 로그를 본다. `unknown model architecture: 'gemma4'` 같은 말이 있으면 0단계의 "Gemma 4를 읽을 수 있나" 문제다.
4. 비교하고 싶으면 Backend 를 `LmStudio` 로 바꿔 PC의 LM Studio 결과와 나란히 본다.

## 3. 안드로이드 폰

1. **폰 준비**
   - 설정 → 휴대전화 정보 → 소프트웨어 정보 → 빌드 번호를 7번 누르면 개발자 옵션이 생긴다.
   - 개발자 옵션 → USB 디버깅을 켠다. PC에 USB로 꽂고 폰에 뜨는 "허용"을 누른다.
2. **빌드 설정**: File → Build Settings → Android → Switch Platform. Player Settings → Other Settings 에서
   - Scripting Backend: **IL2CPP**
   - Target Architectures: **ARM64** 만
   - Internet Access: **Require** (모델 내려받기)
3. **Build And Run** 을 누른다. 앱이 폰에 깔리고 켜진다.
4. **첫 실행**: 모델을 내려받는다(약 3GB, Wi-Fi에서). 화면에 진행률이 나온다.
5. **결과 보기**
   - 폰 화면에 장면별 결과와 요약이 나온다.
   - PC에서 실시간 로그:
     ```bash
     adb logcat -s Unity
     ```
     `[NPC시험]` 으로 시작하는 줄이 시험 결과다.
   - CSV 가져오기 (경로는 화면 마지막 줄에 나온다):
     ```bash
     adb pull /sdcard/Android/data/<패키지 이름>/files/
     ```
6. **발열 보기**: 시험이 도는 동안
   ```bash
   adb shell dumpsys thermalservice
   ```
   Thermal Status 가 0(정상)에서 올라가는지 본다. 배터리 온도는 `adb shell dumpsys battery` 의 temperature(0.1도 단위).
7. **메모리**: 화면의 "메모리 최대 …MB (앱 전체, 모델 포함)" 를 적는다.
8. GPU 층(Gpu Layers)은 `-1`(자동 → 안드로이드는 0, CPU)로 시작한다. 지금 LlamaLib은 안드로이드 Vulkan을 뺐다.

## 4. iPhone (Mac 필요)

1. **Mac 준비**: Xcode 설치, Xcode → Settings → Accounts 에 Apple ID 로그인. 무료 계정도 7일짜리 개발 서명으로 폰에 깔 수 있다.
2. **빌드**: Unity → File → Build Settings → iOS → Switch Platform → Build. Xcode 프로젝트 폴더가 생긴다.
3. **Xcode 설정**: 생긴 `.xcodeproj` 를 연다 → Unity-iPhone 타깃 → Signing & Capabilities
   - Team 을 고른다.
   - `+ Capability` → **Increased Memory Limit** 를 추가한다. 모델이 3GB라 기본 메모리 한도를 넘을 수 있다.
4. **폰 준비**: iPhone 을 USB로 연결 → 설정 → 개인정보 보호 및 보안 → **개발자 모드** 켜기(재시동). 처음 실행 때 설정 → 일반 → VPN 및 기기 관리에서 개발자 앱을 신뢰한다.
5. **Run(▶)** 을 누른다.
6. **결과 보기**
   - 폰 화면, 그리고 Xcode 아래 콘솔의 `[NPC시험]` 줄.
   - **메모리**: Xcode 왼쪽 Debug navigator(⌘7)의 Memory 게이지. 화면의 메모리 숫자는 iOS에서는 모델을 빼고 잰 값이라 이 게이지를 적는다.
   - **발열**: 같은 곳의 Energy Impact, 그리고 폰이 뜨거워지는지 손으로도.
   - **CSV**: Xcode → Window → Devices and Simulators → 기기 → 앱 → ⚙ → Download Container.
7. GPU 층은 `-1`(자동 → iOS는 99, Metal GPU)로 시작한다.

## 5. 결과로 정할 것

| 정할 것 | 어떻게 |
|---|---|
| 시간 초과(`SlmTimeout`, 지금 3초) | 요약의 "SLM 전체 시간 90%" 값을 본다. 대사가 늦게 뜨면 어색하므로, 90% 값이 3초 안팎이면 그 값에 여유를 조금 더한다. 훨씬 길면 그 기기는 SLM을 끈다. |
| SLM 을 끌 기기(`SlmEnabled`) | 모델을 못 올렸거나, 앱이 꺼졌거나(메모리), 90% 값이 너무 긴 기기. 기기 RAM(`SystemInfo.systemMemorySize`) 같은 기준으로 묶는다. |
| 형식 제약(Grammar) | "기본 대사로 대신" 이유에 `InvalidOutput NotJson` 이 많으면 GBNF 가 기기 llama.cpp 에서 안 먹는 것이다. Grammar 를 `JsonSchema`, 그다음 `None` 으로 바꿔 다시 잰다. |
| 스레드 수 | 안드로이드가 느리면 Threads 를 4로 바꿔 비교한다(모든 코어보다 고성능 코어만 쓰는 게 빠를 때가 있다). |
| 대사 품질 | CSV 의 대사를 `tools/npc-prompt-test` 의 채점표 방식으로 본다. PC 결과와 크게 다르면 기록한다. |

시작할 때는 화면의 시간 초과(Slm Timeout Seconds)를 30초로 넉넉히 두어 실제 걸리는 시간을 잰다. 3초로 두면 다 기본 대사로 넘어가 시간을 못 잰다.

## 6. 기기별 기록 양식

| 기기 | RAM | 모델 올리기(ms) | SLM 대사 (x/18) | 전체 시간 중앙값 / 90% | 첫 응답 중앙값 | 메모리 최대 | 발열 | 비고 |
|---|---|---|---|---|---|---|---|---|
| 예: Galaxy S2x | 12GB | | | | | | | |

## 문제가 생기면

| 증상 | 원인과 할 일 |
|---|---|
| "모델 올리기 실패", 로그에 `unknown model architecture` | LLMUnity 안의 llama.cpp 가 이 모델을 모른다. 0단계의 "안 되면"으로. |
| 앱이 켜지자마자 또는 모델을 올리다 꺼짐 | 메모리 부족. iOS는 Increased Memory Limit 를 넣었는지 본다. 더 작은 양자화(Q3)나 더 작은 모델로 비교한다. |
| 모든 장면이 `NotJson` | 형식 제약 문제. Grammar 를 `JsonSchema` → `None` 으로 바꿔 본다. |
| 모든 장면이 시간 초과 | Slm Timeout Seconds 를 늘려 실제 시간을 잰다. iOS는 Gpu Layers 99, 안드로이드는 Threads 4 를 시험한다. |
| "JAMKKAEBI_LLMUNITY 심볼이 없어…" | 1단계 4번. 그 플랫폼 탭에 심볼을 넣었는지 본다. |
| "데이터가 없다" | 1단계 3번. `Assets/Resources/Npc/` 에 JSON 5개(scenes 포함)가 있는지 본다. |
