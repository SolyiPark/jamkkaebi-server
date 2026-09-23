package com.example.jamkkaebi.artifact.domain;

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
import lombok.Getter;
import lombok.NoArgsConstructor;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.DynamicUpdate;

import java.time.LocalDateTime;

/**
 * 유물 한 종에 대한 <b>한 사용자의 상태 전부</b>.
 *
 * <p>도감·작업대·전시관·미니게임·재봉인 알림이 전부 같은 행을 다르게 읽는다. 중복 카드가 즉시 재화로 환전되기 때문에
 * 사용자 한 명당 유물 하나에 행이 정확히 하나이고, 미획득 유물은 <b>행이 아예 없다</b>
 *
 * <p><b>바뀐 컬럼만 UPDATE 한다</b>({@code @DynamicUpdate}).
 */
@DynamicUpdate
@Entity
@Table(
        name = "user_artifacts",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_user_artifacts_owner_artifact", columnNames = {"user_id", "artifact_id"}),
        indexes = @Index(name = "idx_user_artifacts_owner", columnList = "user_id")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class UserArtifact extends BaseTimeEntity {

    // 도깨비 이름 최소·최대 길이(코드포인트).
    public static final int SPIRIT_NAME_MIN_LENGTH = 2;
    public static final int SPIRIT_NAME_MAX_LENGTH = 10;

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "artifact_id", nullable = false)
    private Integer artifactId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private ArtifactStatus status;

    /**
     * 최초 정화에서 <b>다음에 할</b> 페이즈.
     * {@code COMPLETED} 이후에는 의미가 없어 {@link Phase#GUARD} 에 있는다.
     */
    @Enumerated(EnumType.STRING)
    @Column(name = "current_phase", nullable = false, length = 20)
    private Phase currentPhase;

    @Enumerated(EnumType.STRING)
    @Column(name = "awakening_stage", length = 20)
    private AwakeningStage awakeningStage;

    @ColumnDefault("0")
    @Column(name = "growth_gauge", nullable = false)
    private int growthGauge;

    // 마지막으로 선명도가 회복된 시각.
    @Column(name = "last_purified_at")
    private LocalDateTime lastPurifiedAt;

    // 이번 복구 순서에서 마지막으로 클리어한 페이즈
    @Enumerated(EnumType.STRING)
    @Column(name = "restore_phase", length = 20)
    private Phase restorePhase;

    // 사용자가 지어준 도깨비 이름. 짓지 않았으면 {@code null} 이고 기본 이름을 쓴다.
    @Column(name = "custom_name", length = SPIRIT_NAME_MAX_LENGTH)
    private String customName;

    // 전시관 배치 여부
    @ColumnDefault("false")
    @Column(name = "is_displayed", nullable = false)
    private boolean displayed;

    // 개별 숨기기
    @ColumnDefault("false")
    @Column(name = "is_hidden", nullable = false)
    private boolean hidden;

    @ColumnDefault("0")
    @Column(name = "pos_x", nullable = false)
    private int posX;

    @ColumnDefault("0")
    @Column(name = "pos_y", nullable = false)
    private int posY;

    // 연속 완벽 정화. (배지 지급 기준용)
    @ColumnDefault("0")
    @Column(name = "perfect_streak", nullable = false)
    private int perfectStreak;

    // 상자에서 뽑힌 시각
    @Column(name = "acquired_at", nullable = false)
    private LocalDateTime acquiredAt;

    private UserArtifact(Long userId, Integer artifactId, LocalDateTime acquiredAt) {
        this.userId = userId;
        this.artifactId = artifactId;
        this.status = ArtifactStatus.IN_PROGRESS;
        this.currentPhase = Phase.UNSEAL;
        this.acquiredAt = acquiredAt;
    }

    public static UserArtifact acquired(Long userId, Integer artifactId, LocalDateTime acquiredAt) {
        return new UserArtifact(userId, artifactId, acquiredAt);
    }

    public boolean isCompleted() {
        return status == ArtifactStatus.COMPLETED;
    }

    public RevealLevel revealLevel() {
        return RevealLevel.of(status, currentPhase);
    }

    //최초 플레이 시
    public void clearFirstPhase(LocalDateTime now) {
        if (isCompleted()) {
            return;
        }
        Phase next = currentPhase.next();
        if (next != null) {
            currentPhase = next;
            return;
        }
        status = ArtifactStatus.COMPLETED;
        awakeningStage = AwakeningStage.AWAKENED;
        lastPurifiedAt = now;
        restorePhase = null;
    }

    //관리 플레이 시
    public void clearCarePhase(Phase phase, LocalDateTime now) {
        if (phase.isLast()) {
            lastPurifiedAt = now;
            restorePhase = null;
            return;
        }
        restorePhase = phase;
    }

    public void chargeGrowth(int amount) {
        if (amount > 0) {
            growthGauge += amount;
        }
    }

    /**
     * 각성 단계를 확정한다. 넘친 게이지는 다음 단계로 이월한다.
     *
     * @param required 이번 단계에 필요했던 게이지 양
     * @return 다음 단계로 이월된 양
     */
    public int awaken(int required) {
        AwakeningStage next = awakeningStage.next();
        if (next == null) {
            return 0;
        }
        int carriedOver = next.isMax() ? 0 : Math.max(0, growthGauge - required);
        awakeningStage = next;
        growthGauge = carriedOver;
        return carriedOver;
    }

    public void recordPerfect(boolean perfect) {
        perfectStreak = perfect ? perfectStreak + 1 : 0;
    }

    public void renameSpirit(String customName) {
        this.customName = customName;
    }

    public void placeAt(int posX, int posY) {
        this.displayed = true;
        this.posX = posX;
        this.posY = posY;
    }

    public void changeHidden(boolean hidden) {
        this.hidden = hidden;
    }
}
