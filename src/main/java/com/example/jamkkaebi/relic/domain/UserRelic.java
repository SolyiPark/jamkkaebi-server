package com.example.jamkkaebi.relic.domain;

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
        name = "user_relics",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_user_relics_owner_relic", columnNames = {"user_id", "relic_id"}),
        indexes = @Index(name = "idx_user_relics_owner", columnList = "user_id")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class UserRelic extends BaseTimeEntity {

    // 도깨비 이름 최소·최대 길이
    public static final int SPIRIT_NAME_MIN_LENGTH = 2;
    public static final int SPIRIT_NAME_MAX_LENGTH = 10;

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "relic_id", nullable = false)
    private Integer relicId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private RelicStatus status;

    /**
     * 최초 정화에서 <b>다음에 할</b> 페이즈.
     * {@code COMPLETED} 이후에는 의미가 없어 {@link Phase#GUARD} 에 있는다.
     */
    @Enumerated(EnumType.STRING)
    @Column(name = "current_phase", nullable = false, length = 20)
    private Phase currentPhase;

    // 마지막으로 선명도가 회복된 시각.
    @Column(name = "last_purified_at")
    private LocalDateTime lastPurifiedAt;

    // 이번 복구 순서에서 마지막으로 클리어한 페이즈. 복구 중이 아니면 null
    @Enumerated(EnumType.STRING)
    @Column(name = "restore_phase", length = 20)
    private Phase restorePhase;

    /**
     * 이번 복구를 시작할 때의 선명도. {@link #restorePhase} 와 함께 있거나 함께 {@code null} 이고,
     * {@link Clarity#CLEAR} 일 수 없다. 지금 선명도와 다르면 저장된 복구 진행을 인정하지 않는다.
     */
    @Enumerated(EnumType.STRING)
    @Column(name = "restore_clarity", length = 20)
    private Clarity restoreClarity;

    // 관리 플레이 성공 횟수(난이도 무관, 최초 정화는 세지 않는다). 친밀도 계산에 쓴다.
    @ColumnDefault("0")
    @Column(name = "care_count", nullable = false)
    private int careCount;

    // 사용자가 지어준 도깨비 이름. 짓지 않았으면 기본 이름
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

    private UserRelic(Long userId, Integer relicId, LocalDateTime acquiredAt) {
        this.userId = userId;
        this.relicId = relicId;
        this.status = RelicStatus.IN_PROGRESS;
        this.currentPhase = Phase.UNSEAL;
        this.acquiredAt = acquiredAt;
    }

    public static UserRelic acquired(Long userId, Integer relicId, LocalDateTime acquiredAt) {
        return new UserRelic(userId, relicId, acquiredAt);
    }

    public boolean isCompleted() {
        return status == RelicStatus.COMPLETED;
    }

    public RevealLevel revealLevel() {
        return RevealLevel.of(status, currentPhase);
    }

    // 최초 정화 한 판 성공. 마지막 페이즈였으면 완료한다.
    public void clearFirstPlay(LocalDateTime now) {
        if (isCompleted()) {
            return;
        }
        Phase next = currentPhase.next();
        if (next != null) {
            currentPhase = next;
            return;
        }
        status = RelicStatus.COMPLETED;
        refresh(now);
    }

    /**
     * 관리 플레이 한 판 성공으로 한 페이즈 복구한다. 순서의 마지막 페이즈면 선명도를 회복한다.
     *
     * @param phase   이 판의 페이즈 (세션 시작 때 정한 값)
     * @param clarity 이 판을 시작할 때의 선명도
     */
    public void clearCarePlay(Phase phase, Clarity clarity, LocalDateTime now) {
        careCount++;
        if (phase.isLast() || clarity == Clarity.CLEAR) {
            refresh(now);
            return;
        }
        restorePhase = phase;
        restoreClarity = clarity;
    }

    // 관리 플레이 한 판 성공으로 바로 선명까지 복구한다. (어려움)
    public void restoreFully(LocalDateTime now) {
        careCount++;
        refresh(now);
    }

    /**
     * 지금 선명도로 복구할 때 이미 클리어했다고 인정하는 마지막 페이즈. 인정할 진행이 없으면 {@code null}.
     *
     * <p>복구를 시작한 선명도와 지금 선명도가 같을 때만 저장된 진행을 이어 간다. 다르면(복구 도중 더 흐려졌거나
     * 재봉인된 경우) 지난 진행을 버리고 지금 선명도가 정한 순서의 처음부터 한다.
     */
    public Phase restorePhaseFor(Clarity current) {
        return current != null && current == restoreClarity ? restorePhase : null;
    }

    // 선명도 회복: 마지막 정화 시각 갱신, 복구 진행 비우기
    private void refresh(LocalDateTime now) {
        lastPurifiedAt = now;
        restorePhase = null;
        restoreClarity = null;
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

    public void removeFromExhibition() {
        this.displayed = false;
    }

    public void changeHidden(boolean hidden) {
        this.hidden = hidden;
    }
}
