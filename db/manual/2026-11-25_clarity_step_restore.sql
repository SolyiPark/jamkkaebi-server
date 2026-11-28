-- =====================================================================================================
-- 관리 플레이 복구 = 선명도 한 칸 회복 (2026-11-25) — 기존 MySQL 데이터베이스용 수동 마이그레이션 (MySQL 8.0 이상)
--
-- 바뀐 것
--   - 선명도 기준 시각 추가: user_relics.clarity_anchor_at. 선명도는 이제 이 시각으로 계산한다.
--     last_purified_at 은 "마지막 정화로부터 n일째" 표시용으로만 남는다.
--   - 복구 진행 삭제: user_relics.restore_phase, user_relics.restore_clarity. 다음 판의 페이즈는 지금 선명도로 정한다.
--   - 유물 봉인 매개체 재배정(1·5·8·9번)은 MasterDataSeeder 가 기동할 때 relics 를 덮어써서 SQL 이 필요 없다.
--
-- 왜 필요한가
--   ddl-auto: update 는 clarity_anchor_at 컬럼을 만들지만 값을 채우지 않고, 옛 컬럼도 지우지 않는다.
--   서버는 clarity_anchor_at 이 NULL 이면 last_purified_at 으로 대신 계산하므로(ClarityPolicy.anchorOf)
--   이 스크립트를 돌리기 전에도 표시는 그대로다. 그래도 한 번 돌려 값을 채우고 옛 컬럼을 정리한다.
--
-- 언제 돌리나
--   새 서버를 띄우기 전이든 후든 한 번. 여러 번 돌려도 안전하다. 새로 만드는 DB(테스트의 H2 포함)에는 필요 없다.
--   진행 중이던 복구(restore_phase)는 출시 전이라 버린다.
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

-- [1] 선명도 기준 시각 (서버를 먼저 띄워 update 가 이미 만들었으면 건너뛴다)
CALL jk_add_column_if_missing('user_relics', 'clarity_anchor_at', 'DATETIME(6) NULL');

-- [2] 기존 행은 마지막 정화 시각에서 시작한다 — 지금 보이는 선명도가 그대로 유지된다.
UPDATE user_relics SET clarity_anchor_at = last_purified_at
WHERE clarity_anchor_at IS NULL AND last_purified_at IS NOT NULL;

-- [3] 복구 진행 컬럼 삭제 (없으면 건너뛴다)
CALL jk_drop_column_if_exists('user_relics', 'restore_phase');
CALL jk_drop_column_if_exists('user_relics', 'restore_clarity');

-- [4] 정리
DROP PROCEDURE IF EXISTS jk_drop_column_if_exists;
DROP PROCEDURE IF EXISTS jk_add_column_if_missing;
