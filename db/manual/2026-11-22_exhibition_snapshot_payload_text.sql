-- =====================================================================================================
-- 전시관 스냅샷 컬럼 확대 (2026-11-22) — 기존 MySQL 데이터베이스용 수동 마이그레이션 (MySQL 8.0 이상)
--
-- 바뀐 것
--   - exhibition_snapshots.payload: TINYTEXT → TEXT
--     (@Lob 만 두면 MySQL 에서 255바이트로 만들어진다. 도깨비 1마리 스냅샷이 약 237바이트라, 2마리 이상
--      배치하면 PUT /api/exhibition/layout 이 "Data too long for column 'payload'" 로 실패하고 C003 이 나간다)
--
-- 왜 필요한가
--   엔티티에 columnDefinition = "TEXT" 를 넣었지만 ddl-auto: update 는 이미 있는 컬럼의 타입을 바꾸지 않는다.
--   이 변경 전에 서버를 한 번이라도 띄운 DB 는 TINYTEXT 그대로 남는다.
--
-- 언제 돌리나
--   새 서버를 띄우기 전이든 후든 한 번. 여러 번 돌려도 안전하다 — 이미 TEXT 면 아무것도 바뀌지 않는다.
--   기존 행은 그대로 유지된다. 새로 만드는 DB(테스트의 H2 포함)에는 필요 없다.
-- =====================================================================================================

ALTER TABLE exhibition_snapshots MODIFY COLUMN payload TEXT NOT NULL;
