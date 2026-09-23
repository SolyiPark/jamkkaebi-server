package com.example.jamkkaebi.purification.domain;

import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.domain.EntryRoute;
import com.example.jamkkaebi.artifact.domain.Phase;
import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Index;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;
import org.hibernate.annotations.DynamicUpdate;

import java.time.LocalDateTime;

/**
 * 유물 정화 한 판.
 *
 * <p><b>서버는 보드를 재현하지 않는다</b> 그래서 결과 보고 때 보는 것은 넷뿐이다.
 * ⑴ 발급한 세션인지 ⑵ 이미 정산했는지 ⑶ 탭·정찰·보너스 타일이 상한을 넘지 않는지 ⑷ 경과 시간이 너무 짧지 않은지.
 *
 * <p><b>실패도 행을 남긴다.</b> 실패 사유는 난이도 조정 데이터다.
 */
@DynamicUpdate
@Entity
@Table(
        name = "play_sessions",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_play_sessions_token", columnNames = "session_token"),
        indexes = {
                @Index(name = "idx_play_sessions_owner_artifact", columnList = "user_id, user_artifact_id"),
                @Index(name = "idx_play_sessions_expires", columnList = "expires_at")
        }
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class PlaySession extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    // 1회용 세션 토큰. 결과 보고 경로에 실리며 무작위 URL-safe 문자열이다.
    @Column(name = "session_token", nullable = false, length = 64)
    private String sessionToken;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "user_artifact_id", nullable = false)
    private Long userArtifactId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private Phase phase;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private Difficulty difficulty;

    @Enumerated(EnumType.STRING)
    @Column(name = "entry_route", nullable = false, length = 20)
    private EntryRoute entryRoute;

    // 보드 재현용 시드
    @Column(name = "board_seed", nullable = false)
    private long boardSeed;

    // 발급 시점의 탭 예산
    @Column(name = "tap_budget", nullable = false)
    private int tapBudget;

    @Column(name = "scout_limit", nullable = false)
    private int scoutLimit;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private SessionStatus status;

    @Column(name = "started_at", nullable = false)
    private LocalDateTime startedAt;

    @Column(name = "expires_at", nullable = false)
    private LocalDateTime expiresAt;

    // 결과 컬럼
    @Enumerated(EnumType.STRING)
    @Column(length = 20)
    private PlayResult result;

    @Enumerated(EnumType.STRING)
    @Column(name = "fail_reason", length = 20)
    private FailReason failReason;

    @Column(name = "taps_used")
    private Integer tapsUsed;

    @Column(name = "scouts_used")
    private Integer scoutsUsed;

    @Column(name = "damage_final")
    private Integer damageFinal;

    @Column(name = "bonus_jeongseong_tiles")
    private Integer bonusJeongseongTiles;

    @Column(name = "bonus_crystal_tiles")
    private Integer bonusCrystalTiles;

    @Column(name = "is_perfect")
    private Boolean perfect;

    @Column(name = "jeongseong_earned")
    private Integer jeongseongEarned;

    @Column(name = "crystal_earned")
    private Integer crystalEarned;

    @Column(name = "gauge_earned")
    private Integer gaugeEarned;

    @Column(name = "ended_at")
    private LocalDateTime endedAt;

    @Builder
    private PlaySession(String sessionToken, Long userId, Long userArtifactId, Phase phase, Difficulty difficulty,
                        EntryRoute entryRoute, long boardSeed, int tapBudget, int scoutLimit,
                        LocalDateTime startedAt, LocalDateTime expiresAt) {
        this.sessionToken = sessionToken;
        this.userId = userId;
        this.userArtifactId = userArtifactId;
        this.phase = phase;
        this.difficulty = difficulty;
        this.entryRoute = entryRoute;
        this.boardSeed = boardSeed;
        this.tapBudget = tapBudget;
        this.scoutLimit = scoutLimit;
        this.status = SessionStatus.ISSUED;
        this.startedAt = startedAt;
        this.expiresAt = expiresAt;
    }

    public boolean isSettled() {
        return status == SessionStatus.SETTLED;
    }

    public boolean isReportable(LocalDateTime now) {
        return status == SessionStatus.ISSUED && now.isBefore(expiresAt);
    }

    public void supersede() {
        if (status == SessionStatus.ISSUED) {
            status = SessionStatus.SUPERSEDED;
        }
    }

    // 결과를 기록하고 정산 완료로 넘긴다.
    public void settle(Settlement settlement, LocalDateTime endedAt) {
        this.status = SessionStatus.SETTLED;
        this.result = settlement.result();
        this.failReason = settlement.failReason();
        this.tapsUsed = settlement.tapsUsed();
        this.scoutsUsed = settlement.scoutsUsed();
        this.damageFinal = settlement.damage();
        this.bonusJeongseongTiles = settlement.bonusJeongseongTiles();
        this.bonusCrystalTiles = settlement.bonusCrystalTiles();
        this.perfect = settlement.perfect();
        this.jeongseongEarned = settlement.jeongseongEarned();
        this.crystalEarned = settlement.crystalEarned();
        this.gaugeEarned = settlement.gaugeEarned();
        this.endedAt = endedAt;
    }

    /**
     * 이미 정산한 세션에 <b>같은 내용</b>으로 다시 보고했는가.
     * 같으면 처음 정산 결과를 그대로 돌려주고, 다르면 충돌로 막는다.
     */
    public boolean matches(PlayResult result, FailReason failReason, int tapsUsed, int scoutsUsed, int damage,
                           int bonusJeongseongTiles, int bonusCrystalTiles) {
        return this.result == result
                && this.failReason == failReason
                && equalsInt(this.tapsUsed, tapsUsed)
                && equalsInt(this.scoutsUsed, scoutsUsed)
                && equalsInt(this.damageFinal, damage)
                && equalsInt(this.bonusJeongseongTiles, bonusJeongseongTiles)
                && equalsInt(this.bonusCrystalTiles, bonusCrystalTiles);
    }

    private static boolean equalsInt(Integer stored, int reported) {
        return stored != null && stored == reported;
    }

    // 정산 결과
    @Builder
    public record Settlement(
            PlayResult result,
            FailReason failReason,
            int tapsUsed,
            int scoutsUsed,
            int damage,
            int bonusJeongseongTiles,
            int bonusCrystalTiles,
            boolean perfect,
            int jeongseongEarned,
            int crystalEarned,
            int gaugeEarned
    ) {
    }
}
