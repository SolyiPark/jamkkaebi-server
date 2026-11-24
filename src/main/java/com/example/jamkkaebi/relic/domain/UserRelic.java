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

    /**
     * 마지막으로 정화에 성공한 시각. 성공한 판마다 갱신한다. "마지막 정화로부터 n일째" 같은 이력 표시에만 쓰고,
     * 선명도는 {@link #clarityAnchorAt} 으로 계산한다.
     */
    @Column(name = "last_purified_at")
    private LocalDateTime lastPurifiedAt;

    /**
     * 선명도 기준 시각. 선명도는 이 시각부터 흐른 시간으로 계산한다({@code ClarityPolicy}).
     * 선명이 되면 그 순간, 흐려짐이 되면 그 순간에서 흐려짐 기간을 뺀 시각이다.
     * 오염 관리를 마치기 전에는 {@code null}.
     */
    @Column(name = "clarity_anchor_at")
    private LocalDateTime clarityAnchorAt;

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

    // 최초 정화 한 판 성공. 마지막 페이즈였으면 완료하고 선명에서 시작한다.
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
        lastPurifiedAt = now;
        clarityAnchorAt = now;
    }

    /**
     * 관리 플레이 한 판 성공. 돌봄 횟수를 올리고 마지막 정화 시각과 선명도 기준 시각을 새로 정한다.
     * 몇 칸 오를지(기준 시각)는 정산이 난이도와 계획 선명도로 정해 넘긴다.
     *
     * @param clarityAnchorAt 이번 판으로 정해진 선명도 기준 시각 ({@code ClarityPolicy.anchorFor})
     * @throws IllegalStateException    오염 관리를 마치지 않은 유물
     * @throws IllegalArgumentException 기준 시각이 지금보다 늦은 경우
     */
    public void clearCarePlay(LocalDateTime clarityAnchorAt, LocalDateTime now) {
        if (!isCompleted()) {
            throw new IllegalStateException("오염 관리를 마치지 않은 유물은 관리 플레이를 할 수 없습니다.");
        }
        if (clarityAnchorAt.isAfter(now)) {
            throw new IllegalArgumentException("선명도 기준 시각은 지금보다 늦을 수 없습니다.");
        }
        careCount++;
        lastPurifiedAt = now;
        this.clarityAnchorAt = clarityAnchorAt;
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
