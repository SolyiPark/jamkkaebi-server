-- =====================================================================================================
-- 작업대 기획 변경 (2026-11-15) — 기존 MySQL 데이터베이스용 수동 마이그레이션 (MySQL 8.0 이상)
--
-- 바뀐 것
--   - 성장·각성 폐기: user_relics.awakening_stage, user_relics.growth_gauge, play_sessions.gauge_earned 삭제
--   - 돌봄 횟수 추가: user_relics.care_count (기본 0)
--   - 복구 시작 선명도: user_relics.restore_clarity (restore_phase 와 함께 있거나 함께 NULL)
--   - 세션 계획 선명도: play_sessions.plan_clarity (관리 플레이만)
--   - VERY_EASY 난이도 삭제
--
-- 왜 필요한가
--   ddl-auto: update 는 새 컬럼은 만들지만 컬럼 삭제는 하지 않는다. 남은 growth_gauge(NOT NULL, 기본값 0) 는
--   당장 INSERT 를 깨지 않지만 쓰레기로 남는다. 반면 play_sessions.difficulty = 'VERY_EASY' 행이 있으면
--   enum 매핑에 실패해 그 세션을 읽는 순간 500 이 난다 — [1] 은 반드시 돌린다.
--
-- 언제 돌리나
--   새 서버를 띄우기 전이든 후든 한 번. 새로 만드는 DB(테스트의 H2 포함)에는 필요 없다.
-- =====================================================================================================

-- [1] 없어진 난이도. 기록용 세션이라 쉬움으로 옮긴다.
UPDATE play_sessions SET difficulty = 'EASY' WHERE difficulty = 'VERY_EASY';

-- [2] 성장·각성 컬럼 삭제
ALTER TABLE user_relics DROP COLUMN awakening_stage;
ALTER TABLE user_relics DROP COLUMN growth_gauge;
ALTER TABLE play_sessions DROP COLUMN gauge_earned;

-- [3] 새 컬럼 — 서버를 먼저 띄웠다면 update 가 이미 만들었으니 건너뛴다.
ALTER TABLE user_relics ADD COLUMN care_count INT NOT NULL DEFAULT 0;
ALTER TABLE user_relics ADD COLUMN restore_clarity VARCHAR(20) NULL;
ALTER TABLE play_sessions ADD COLUMN plan_clarity VARCHAR(20) NULL;

-- [4] 복구 도중이던 행. 예전에는 시작 선명도를 저장하지 않아 알 수 없으므로 진행을 비운다
--     (다음 관리 플레이는 지금 선명도가 정한 순서의 처음부터).
UPDATE user_relics SET restore_phase = NULL WHERE restore_phase IS NOT NULL AND restore_clarity IS NULL;
