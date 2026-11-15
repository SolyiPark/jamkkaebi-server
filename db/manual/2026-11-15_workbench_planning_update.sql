-- =====================================================================================================
-- 작업대 기획 변경 (2026-11-15) — 기존 MySQL 데이터베이스용 수동 마이그레이션 (MySQL 8.0 이상)
--
-- 바뀐 것
--   - 성장·각성 폐기: user_relics.awakening_stage, user_relics.growth_gauge, play_sessions.gauge_earned 삭제
--   - 돌봄 횟수 추가: user_relics.care_count (기본 0)
--   - 복구 시작 선명도: user_relics.restore_clarity (restore_phase 와 함께 있거나 함께 NULL)
--   - 세션 계획 선명도: play_sessions.plan_clarity (관리 플레이만)
--   - VERY_EASY 난이도 삭제
--   - relics.real_story: TINYTEXT → TEXT (@Lob 만 두면 MySQL 에서 255바이트로 만들어져 한글 실화 기록이 들어가지 않는다)
--
-- 왜 필요한가
--   ddl-auto: update 는 새 컬럼은 만들지만 컬럼 삭제는 하지 않는다. 남은 growth_gauge(NOT NULL, 기본값 0) 는
--   당장 INSERT 를 깨지 않지만 쓰레기로 남는다. 반면 play_sessions.difficulty = 'VERY_EASY' 행이 있으면
--   enum 매핑에 실패해 그 세션을 읽는 순간 500 이 난다 — [1] 은 반드시 돌린다.
--
-- 언제 돌리나
--   새 서버를 띄우기 전이든 후든 한 번. 여러 번 돌려도 안전하다 — 컬럼 추가·삭제는 있는지 확인한 뒤에만 한다
--   (MySQL 8 은 ADD/DROP COLUMN IF [NOT] EXISTS 를 지원하지 않아 아래 프로시저로 대신한다).
--   새로 만드는 DB(테스트의 H2 포함)에는 필요 없다.
-- =====================================================================================================

DELIMITER //
DROP PROCEDURE IF EXISTS jk_drop_column_if_exists //
CREATE PROCEDURE jk_drop_column_if_exists(IN tbl VARCHAR(64), IN col VARCHAR(64))
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` DROP COLUMN `', col, '`');
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END //
DROP PROCEDURE IF EXISTS jk_add_column_if_missing //
CREATE PROCEDURE jk_add_column_if_missing(IN tbl VARCHAR(64), IN col VARCHAR(64), IN def VARCHAR(255))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` ADD COLUMN `', col, '` ', def);
        PREPARE stmt FROM @sql;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END //
DELIMITER ;

-- [1] 없어진 난이도. 기록용 세션이라 쉬움으로 옮긴다.
UPDATE play_sessions SET difficulty = 'EASY' WHERE difficulty = 'VERY_EASY';

-- [2] 성장·각성 컬럼 삭제 (없으면 건너뛴다)
CALL jk_drop_column_if_exists('user_relics', 'awakening_stage');
CALL jk_drop_column_if_exists('user_relics', 'growth_gauge');
CALL jk_drop_column_if_exists('play_sessions', 'gauge_earned');

-- [3] 새 컬럼 (서버를 먼저 띄워 update 가 이미 만들었으면 건너뛴다)
CALL jk_add_column_if_missing('user_relics', 'care_count', 'INT NOT NULL DEFAULT 0');
CALL jk_add_column_if_missing('user_relics', 'restore_clarity', 'VARCHAR(20) NULL');
CALL jk_add_column_if_missing('play_sessions', 'plan_clarity', 'VARCHAR(20) NULL');

-- [4] 복구 도중이던 행. 예전에는 시작 선명도를 저장하지 않아 알 수 없으므로 진행을 비운다
--     (다음 관리 플레이는 지금 선명도가 정한 순서의 처음부터).
UPDATE user_relics SET restore_phase = NULL WHERE restore_phase IS NOT NULL AND restore_clarity IS NULL;

-- [5] 실화 기록 컬럼 크기. ddl-auto: update 는 이미 있는 컬럼의 타입을 바꾸지 않는다.
ALTER TABLE relics MODIFY real_story TEXT NOT NULL;

-- [6] 정리
DROP PROCEDURE IF EXISTS jk_drop_column_if_exists;
DROP PROCEDURE IF EXISTS jk_add_column_if_missing;
