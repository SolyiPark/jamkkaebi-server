-- =====================================================================================================
-- 유물 용어 변경 (artifact -> relic) — 기존 MySQL 데이터베이스용 수동 마이그레이션 (MySQL 8.0 이상)
--
-- 왜 필요한가
--   스키마는 ddl-auto: update 로 엔티티에서 만들어진다. update 는 테이블·컬럼 이름 변경을 알아보지 못해
--   새 이름(relics, user_relics, relic_id ...)으로 빈 테이블과 새 컬럼을 따로 만든다. 그러면 진행도가 사라진
--   것처럼 보이고, 옛 artifact_id 컬럼(NOT NULL)이 남아 상자 열기 INSERT 가 실패한다.
--
-- 언제 돌리나
--   relic 으로 바뀐 서버를 처음 띄우기 "전에" 한 번. 새로 만드는 DB(테스트의 H2 포함)에는 필요 없다.
--   이미 새 서버를 먼저 띄웠다면 아래 [0] 을 먼저 확인한다.
-- =====================================================================================================

-- [0] 새 서버를 이 스크립트보다 먼저 띄웠다면, update 가 만든 빈 테이블·컬럼을 지운 뒤 [1] 부터 돌린다.
--     relics 는 기동 시드가 9행을 채웠을 수 있다. user_relics 가 비어 있는지 먼저 확인할 것.
-- DROP TABLE IF EXISTS relics;
-- DROP TABLE IF EXISTS user_relics;
-- ALTER TABLE box_open_logs DROP COLUMN relic_id;
-- ALTER TABLE gacha_pity DROP INDEX uk_gacha_pity_owner_relic;
-- ALTER TABLE gacha_pity DROP COLUMN relic_id;
-- ALTER TABLE play_sessions DROP INDEX idx_play_sessions_owner_relic;
-- ALTER TABLE play_sessions DROP COLUMN user_relic_id;

-- [1] 테이블
RENAME TABLE artifacts TO relics,
             user_artifacts TO user_relics;

-- [2] 사용자 유물
ALTER TABLE user_relics RENAME COLUMN artifact_id TO relic_id;
ALTER TABLE user_relics RENAME INDEX uk_user_artifacts_owner_artifact TO uk_user_relics_owner_relic;
ALTER TABLE user_relics RENAME INDEX idx_user_artifacts_owner TO idx_user_relics_owner;

-- [3] 유물을 가리키는 다른 테이블
ALTER TABLE box_open_logs RENAME COLUMN artifact_id TO relic_id;
ALTER TABLE gacha_pity RENAME COLUMN artifact_id TO relic_id;
ALTER TABLE gacha_pity RENAME INDEX uk_gacha_pity_owner_artifact TO uk_gacha_pity_owner_relic;
ALTER TABLE play_sessions RENAME COLUMN user_artifact_id TO user_relic_id;
ALTER TABLE play_sessions RENAME INDEX idx_play_sessions_owner_artifact TO idx_play_sessions_owner_relic;

-- [4] 친구 전시관 스냅샷 — JSON 으로 저장돼 있어 키 이름도 바꿔야 한다.
--     바꾸지 않으면 주인이 다시 접속할 때까지 친구 화면에서 유물 번호가 빠진다.
UPDATE exhibition_snapshots
SET payload = REPLACE(payload, '"artifactId":', '"relicId":')
WHERE payload LIKE '%"artifactId"%';
