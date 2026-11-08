"""run.py 의 single 방식 프롬프트를 장면별로 뽑아, C# 프롬프트 조립기와 비교할 기준을 만든다.

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

args = SimpleNamespace(line_chars=30, max_chars=40)
content = run.load_content()
expected = {}
for scene in content["scenes"]:
    persona = content["personas"][scene["relicId"]]
    system, user, schema, _ = run.build_prompts(args, "single", scene, persona, content)
    expected[scene["id"]] = {
        "system": system,
        "user": user,
        # C# 조립기와 같은 모양으로 비교하려고 공백 없이 쓴다
        "schema": json.dumps(schema, ensure_ascii=False, separators=(",", ":")),
    }

Path(sys.argv[2]).write_text(json.dumps(expected, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"기준 프롬프트 {len(expected)}개")
