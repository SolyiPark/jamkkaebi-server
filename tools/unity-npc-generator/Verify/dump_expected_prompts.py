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

args = SimpleNamespace(line_chars=25, max_chars=40, max_sentences=0, format=True)
free = SimpleNamespace(line_chars=25, max_chars=40, max_sentences=0, format=False)
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
    ("하게체", "안녕하세요, 전시관을 둘러보시는군요."),
    ("하게체", "좋은 하루 보내세요, 처음 뵙겠습니다."),
    ("하게체", "고맙습니다, 좋은 향이 퍼지듯 기분이 나아지는 것 같네요."),
    ("하게체", "관리해 주셔서 감사합니다. 오늘도 힘내겠습니다."),
    ("하게체", "구경 잘 되나요? 또 오셨습니까?"),
    ("하게체", "전시관 구경은 언제나 즐거운 일이지요."),
    ("해요체", "고마워요, 닦아 주셔서 감사합니다."),
]
expected["_speechSamples"] = [
    {"label": label, "line": line, "want": run.normalize_speech(line, label)} for label, line in SPEECH_SAMPLES
]

Path(sys.argv[2]).write_text(json.dumps(expected, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"기준 프롬프트 {prompt_count}개, 말투 다듬기 예문 {len(SPEECH_SAMPLES)}개")
