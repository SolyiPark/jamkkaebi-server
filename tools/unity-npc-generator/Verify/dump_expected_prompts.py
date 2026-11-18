"""run.py 의 single 방식 프롬프트를 장면별로 뽑아, C# 프롬프트 조립기와 비교할 기준을 만든다.
정해진 대사를 쓰는 장면(이야기 듣기)은 프롬프트가 없어 넣지 않는다. 형식 제약을 켠 기본값(형식 설명 없음)과
끈 값(--no-format, 형식 설명 있음)을 둘 다 뽑는다.

    python dump_expected_prompts.py <npc-prompt-test 폴더> <출력 json>
"""

import importlib.util
import json
import sys
from pathlib import Path
from types import SimpleNamespace

folder = Path(sys.argv[1]).resolve()
spec = importlib.util.spec_from_file_location("run", folder / "run.py")
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)

args = SimpleNamespace(line_chars=16, max_chars=40, max_sentences=0, format=True)
free = SimpleNamespace(line_chars=16, max_chars=40, max_sentences=0, format=False)
content = run.load_content()
expected = {}
for scene in content["scenes"]:
    if run.is_fixed(scene):
        continue
    persona = content["personas"][scene["relicId"]]
    system, user, schema, _ = run.build_prompts(args, "single", scene, persona, content)
    _, user_free, _, _ = run.build_prompts(free, "single", scene, persona, content)
    expected[scene["id"]] = {
        "system": system,
        "user": user,
        "userFree": user_free,
        # C# 조립기와 같은 모양으로 비교하려고 공백 없이 쓴다
        "schema": json.dumps(schema, ensure_ascii=False, separators=(",", ":")),
    }

# 흐려진 안부의 '며칠 전' 변형(variation 1): 사실을 하나만 싣는다 (2026-11-17)
expected["_askDays"] = {}
for scene in content["scenes"]:
    if run.is_fixed(scene) or not run.needs_ask(scene):
        continue
    persona = content["personas"][scene["relicId"]]
    _, user_days, _, _ = run.build_prompts(args, "single", scene, persona, content, 1)
    expected["_askDays"][scene["id"]] = user_days

prompt_count = len(expected)

# 말투 다듬기(normalize_speech)를 C# NpcLineChecks.NormalizeSpeech 와 비교할 예문. 2026-11-11 시험에서 어미 검사에 걸린 대사들이다.
# 장면 ID 와 겹치지 않게 '_' 로 시작하는 키에 둔다
SPEECH_SAMPLES = [
    ("하오체", "안녕하옵니다, 주인님이시라면 공손히 인사드릴까 하옵니다."),
    ("하오체", "안녕하십니까, 주인님께서 부르셨으니 이만 물러가겠습니다."),
    ("하오체", "안녕하옵니까, 주인님."),
    ("하오체", "성실하게 주인님을 모시겠습니다."),
    ("하오체", "안녕하십니다, 수행원입니다."),
    ("하오체", "오셨네요, 반가워요! 조심해요."),
    ("하오체", "이 몸은 주인님의 충직한 수행원이올시다."),
    ("반말", "주인님, 이리 와봐요! 나랑 놀자요!"),
    ("반말", "관리해 주셔서 정말 고마워요!"),
    ("반말", "왜 자꾸 저를 끌고 가요? 심심하지 않게 해 주세요!"),
    ("반말", "주인아, 제 털 좀 봐요. 필요해요?"),
    ("반말", "자개가 참 공들인 것 같지 않나요? 그렇죠?"),
    ("반말", "이거 뭐예요? 아니에요, 고맙습니다."),
    ("반말", "요즘 반짝이는 게 좋아. 주인아저씨는 아니야."),
    ("반말", "주인님, 이거 드세요!"),
    ("반말", "이틀 전 닦아주신 덕분에 선명해졌어요. 가실래요?"),
    ("반말", "안녕하세요, 순찰 나왔습니다! 인사드려요."),
    ("반말", "닦아주시면 좋겠어. 오셨어? 하시는 일 잘 보시고 가."),
    ("반말", "도시는 마시고 주시면 계시는 곳에서 드릴게."),
    ("하게체", "안녕하세요, 전시관을 둘러보시는군요."),
    ("하게체", "좋은 하루 보내세요, 처음 뵙겠습니다."),
    ("하게체", "고맙습니다, 좋은 향이 퍼지듯 기분이 나아지는 것 같네요."),
    ("하게체", "관리해 주셔서 감사합니다. 오늘도 힘내겠습니다."),
    ("하게체", "구경 잘 되나요? 또 오셨습니까?"),
    ("하게체", "전시관 구경은 언제나 즐거운 일이지요."),
    # 2026-11-17: 호칭을 지우다 쉼표가 사라지던 것, 남은 높임, 몸 상태, 권하는 말
    ("반말", "오늘 자개가 정말 예쁘네, 주인님 덕분에 빛이 나요."),
    ("반말", "개운해, 주인님 덕분에 기분이 한결 좋아졌어."),
    ("반말", "점점 흐려져, 주인이 닦아 주시면 좋겠어."),
    ("반말", "고마워, 주인님! 안녕, 주인님, 반가워."),
    ("반말", "와, 아침부터 나를 부르시다니 반갑습니다!"),
    ("반말", "그때 얼마나 공들였는지 보여드릴 수 있어서 뿌듯했어요."),
    ("반말", "편하게 구경해 줘, 오늘도 반갑게 인사해."),
    ("반말", "이틀 전에 관리받아서 몸 상태가 훨씬 더 선명해졌어요. 몸 상태는 좋아."),
    # 2026-11-17 19:43 시험: 존대 잔재(말씀드리다·드렸·여쭈다·모시다·~ㅆ습니다·계시다·뵙다·그러셨)
    ("반말", "주인님, 자개 공들여 만든 거라 말씀드렸죠? 말씀하셨어, 그 말씀 기억해."),
    ("반말", "인사드렸어. 보여드렸어. 여쭤볼게. 모시고 갈게. 기다렸습니다. 계시죠? 그러셨구나."),
    ("반말", "찾아뵈어서 반가워. 주무세요. 뵙고 싶었어. 해 주십시오. 오십시오."),
    ("해요체", "고마워요, 닦아 주셔서 감사합니다."),
    # 2026-11-18: 연달아 붙은 문장 부호는 하나만 ("반가워.!", "하늘아.~", "왔구나.!")
    ("반말", "어서 와! 반가워. 주인님!"),
    ("반말", "안녕! 반가워, 도윤아. 주인님~"),
    ("반말", "왔구나?! 반가워!!"),
    ("반말", "그래... 고마워,!"),
    ("반말", "고마워,... 그래.… 와...! 좋아"),
    ("반말", "별일 없었어...? 음…~ 그래..."),
    # 말줄임을 합친 뒤 드러난 평서 어미의 '?'도 고친다 ("기분이 좋구나...?"→"기분이 좋구나.")
    ("반말", "기분이 좋구나...? 오늘도 반갑구나…? 별일 없었어...?"),
    # 한 문장 안에서 두 글자 이상 같은 낱말은 하나만 남긴다 (drop_repeated_word)
    ("반말", "또 왔네, 다시 왔네!"),
    ("반말", "향로를 닦아줘서 고마워, 정말 고마워! 고마워 정말 고마워."),
    ("반말", "반가워, 또 왔네, 다시 왔네, 반가워! 나는 좋아, 나는 행복해."),
    ("해요체", "또 오셨네요, 다시 오셨네요."),
]
expected["_speechSamples"] = [
    {"label": label, "line": line, "want": run.normalize_speech(line, label)} for label, line in SPEECH_SAMPLES
]
# 플레이어를 가리키는 2인칭(너·네가·너를·네 덕분에)을 호칭('발굴자' 또는 닉네임)으로 — 조사는 받침에 맞춘다 (2026-11-17)
CALL_SAMPLES = [
    ("발굴자", "네가 관리해 줘서 고마워. 너를 기다렸어, 너는 최고야!"),
    ("도윤", "네가 관리해 줘서 고마워. 너를 기다렸어, 너는 최고야!"),
    ("발굴자", "너랑 놀면 좋아. 너한테 자랑하고 싶어. 네 덕분에 개운해."),
    ("도윤", "너랑 놀면 좋아. 너한테 자랑하고 싶어. 네 덕분에 개운해."),
    ("도윤", "너도 올 줄 알았어. 넌 정말 다정해, 널 좋아해. 너무 고마워."),
    ("발굴자", "네? 네 알겠어. 오늘은 네 생각이 났어, 너의 손길이 좋아."),
    ("도윤", "니가 와서 좋아. 너, 오늘 어때? 너처럼 다정한 친구는 처음이야."),
    ("발굴자", "주인이 관리해 줘서 개운해. 주인님을 보니 반가워."),
    ("도윤", "당신이 와서 좋아, 그대를 기다렸어. 그대로 있어."),
]
# 기록 없는 주인 장면의 "오랜만이야"·"또 왔구나" (2026-11-17), "덕분에 관리해 줘서"
NO_RECORD_SAMPLES = [
    "왔구나, 오랜만이야!", "오랜만에 왔구나, 반가워!", "또 왔구나! 오랜만이야.", "다시 왔네, 기다렸어.", "덕분에 관리해 줘서 고마워.", "덕분에 닦아 주셔서 개운해요.",
]
for line in NO_RECORD_SAMPLES:
    expected["_speechSamples"].append({"label": "반말", "line": line, "noRecord": True, "want": run.normalize_speech(line, "반말", None, None, None, None, "발굴자", True)})
    expected["_speechSamples"].append({"label": "반말", "line": line, "call": "발굴자", "want": run.normalize_speech(line, "반말", None, None, None, None, "발굴자", False)})
# 한 문장으로 줄이기 (2026-11-17 20:39)
ONE_SAMPLES = [
    ("자꾸 흐려져서 몸이 무거워. 깨끗해질 수 있게 닦아 줄래?", True), ("많이 흐려졌어. 마지막 관리는 9일 전이었어. 닦아줘.", True),
    ("이대로 흐려지다니 안타까워. 몸이 무거워.", True), ("몸이 흐려졌어. 닦아 줄래?", True), ("고마워. 몸이 선명해졌어.", False),
    ("왔구나, 반가워! 오늘도 수고 많았어.", False), ("깨끗하게 관리해 줘서 고마워! 덕분에 기분이 한결 좋아졌어!", False), ("한 문장이야.", False),
    ("몸이 반짝반짝해져서 기뻐! 발굴자가 관리해 줘서 고마워!", False), ("훨씬 더 깨끗해진 것 같아. 이틀 전에 관리받아서 고마워.", False),
]
for line, flag in ONE_SAMPLES:
    expected["_speechSamples"].append({"label": "반말", "line": line, "oneSentence": True, "needsAsk": flag, "want": run.keep_one_sentence(line, flag)})
for call, line in CALL_SAMPLES:
    expected["_speechSamples"].append({"label": "반말", "line": line, "call": call, "want": run.normalize_speech(line, "반말", None, None, None, None, call)})

# 2026-11-18: 이번에 더한 규칙 함수(인사 꼬리·부탁 꼴·플레이어 이름 지움·자기 솜씨·끊긴 말·남 얘기 이름·뒤바뀐 주체)도 C# 과 같은 결과인지
OWN_THINGS = ["자개", "옻칠", "몸"]
FN_SAMPLES = [
    ("greetingTail", "왔구나, 반가워, 무슨 일인지 궁금하네.", ""), ("greetingTail", "와, 반가워. 뭘 찾으러 왔어?", ""), ("greetingTail", "어서 와 도윤아, 오늘은 무슨 일이 있길래 찾아왔어?", ""),
    ("greetingTail", "또 와줘서 고마워, 향로랑 친구들 보고 싶었어?", ""), ("greetingTail", "안녕, 반가워?", ""), ("greetingTail", "안녕, 오늘은 좀 일찍 왔구나.", ""),
    ("greetingTail", "어서 와, 반가워!", ""), ("greetingTail", "왜 왔어?", ""), ("greetingTail", "와, 처음 온 친구야?", ""),
    ("cleanAsk", "시간 되면 닦아 줄 수 있어?", ""), ("cleanAsk", "괜찮으면 도와줄래?", ""), ("cleanAsk", "조심조심 들어와, 내 마음을 좀 닦아줄래?", ""),
    ("cleanAsk", "살살 닦아 줄까?", ""), ("cleanAsk", "이왕이면 깨워 줘.", ""), ("cleanAsk", "오늘은 닦아 줄래?", ""), ("cleanAsk", "닦아 주겠어.", ""),
    ("dropPlayer", "오랜만에 도윤을 찾아줘서 고마워!", "도윤"), ("dropPlayer", "발굴자가 날 관리해줘서 고마워!", "발굴자"), ("dropPlayer", "도윤이 보고 싶었어.", "도윤"),
    ("dropPlayer", "도윤아 안녕, 반가워.", "도윤"), ("dropPlayer", "고마워, 도윤!", "도윤"), ("dropPlayer", "도윤 덕분에 기뻐.", "도윤"), ("dropPlayer", "도윤을!", "도윤"),
    ("craft", "그 자개 공들여 만들었네", ""), ("craft", "이 자개는 사흘 전 자랑했어", ""), ("craft", "그 모습이 보기 좋아", ""), ("craft", "오늘은 기분이 좋네.", ""),
    ("craft", "옻칠을 정성껏 칠했네. 그 옻칠 반짝여.", ""),
    ("incomplete", "어서 와, 다은.", "다은"), ("incomplete", "어서 와, 다은.", ""), ("incomplete", "내 꼬리를", "다은"), ("incomplete", "관리해 줘서", "다은"),
    ("thirdPerson", "정말 고마워, 도윤을!", "도윤"), ("thirdPerson", "도윤한테 고마워", "도윤"), ("thirdPerson", "도윤아, 고마워!", "도윤"), ("thirdPerson", "발굴자의 손길", "발굴자"),
    ("reversed", "드디어 도윤을 만나러 왔다니 정말 반갑다!", "도윤"), ("reversed", "발굴자를 불러서 반가워", "발굴자"), ("reversed", "도윤이 만나러 왔구나.", "도윤"),
    # 2026-11-18 20:22 시험: 자기 것을 남이 만든 것처럼 입음꼴로 말함(S14 #1), 꾸밈말 뒤 '너를'을 지워 꾸밈말만 남음(S05 #1)
    ("subject", "오, 자개가 정말 정성스럽게 만들어졌네.", ""), ("subject", "자개가 공들여 새겨져 있네!", ""), ("subject", "옻칠이 정성스럽게 칠해졌구나.", ""),
    ("subject", "내 자개가 정성스럽게 만들어졌어.", ""), ("subject", "옻칠이 벗겨졌어.", ""), ("subject", "자개가 정성 덕분에 빛나네.", ""),
    ("pronouns", "와, 처음 온 너를 만나니 기쁘다!", "발굴자"), ("pronouns", "다시 만난 도윤을 보니 좋아.", "도윤"), ("pronouns", "보고 싶던 너를 만나 기뻐!", "도윤"),
    ("pronouns", "그건 너를 위한 거야.", "도윤"), ("pronouns", "도윤을 위해 준비했어.", "도윤"), ("pronouns", "오늘 너를 만나서 기뻐!", "도윤"),
    ("pronouns", "난 너를 기다렸어.", "도윤"), ("pronouns", "정말 너를 기다렸어.", "도윤"), ("pronouns", "오랜만에 너를 찾아줘서 고마워!", "도윤"),
    ("probe", "안녕, 오늘은 좀 일찍 왔구나.", "greet"), ("probe", "어서 와, 반가워!", "greet"), ("probe", "무슨 일이야?", "thank_calm"), ("probe", "뭘 찾으러 왔어?", "wander_greet"),
]


def run_fn(kind, line, arg):
    if kind == "greetingTail":
        return run.fix_greeting_tail(line)
    if kind == "cleanAsk":
        return "true" if run.is_clean_ask(line) else "false"
    if kind == "dropPlayer":
        return run.drop_player_reference(line, arg)
    if kind == "craft":
        return run.fix_craft_spectator(run.fix_own_possessive(line, OWN_THINGS))
    if kind == "incomplete":
        return run.incomplete_ending(line, arg or None) or ""
    if kind == "thirdPerson":
        return run.player_third_person(line, arg) or ""
    if kind == "reversed":
        return run.reversed_call(line, arg) or ""
    if kind == "probe":
        return run.greeting_probe(line, arg) or ""
    if kind == "subject":
        return run.subject_contradiction(line, OWN_THINGS) or ""
    if kind == "pronouns":
        return run.drop_player_reference(run.player_pronouns_to_name(line, arg), arg)
    raise ValueError(kind)


expected["_fnSamples"] = [{"fn": kind, "line": line, "arg": arg, "want": run_fn(kind, line, arg)} for kind, line, arg in FN_SAMPLES]

Path(sys.argv[2]).write_text(json.dumps(expected, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"기준 프롬프트 {prompt_count}개, 말투 다듬기 예문 {len(SPEECH_SAMPLES)}개")
