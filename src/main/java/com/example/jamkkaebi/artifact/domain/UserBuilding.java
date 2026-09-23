package com.example.jamkkaebi.artifact.domain;

import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
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

import java.time.LocalDateTime;

/**
 * 사용자가 완성한 건물. 시대 유물 3종을 모두 마치면 자동으로 지급된다.
 *
 * <p>건물은 시대의 결정으로 키우는 관리형 대상이라 레벨만 있다.
 */
@Entity
@Table(
        name = "user_buildings",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_user_buildings_owner_building", columnNames = {"user_id", "building_id"}),
        indexes = @Index(name = "idx_user_buildings_owner", columnList = "user_id")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class UserBuilding extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "building_id", nullable = false)
    private Integer buildingId;

    @ColumnDefault("1")
    @Column(nullable = false)
    private int level;

    @Column(name = "acquired_at", nullable = false)
    private LocalDateTime acquiredAt;

    @ColumnDefault("false")
    @Column(name = "is_hidden", nullable = false)
    private boolean hidden;

    @ColumnDefault("0")
    @Column(name = "pos_x", nullable = false)
    private int posX;

    @ColumnDefault("0")
    @Column(name = "pos_y", nullable = false)
    private int posY;

    private UserBuilding(Long userId, Integer buildingId, LocalDateTime acquiredAt) {
        this.userId = userId;
        this.buildingId = buildingId;
        this.level = 1;
        this.acquiredAt = acquiredAt;
    }

    // 도감 완성 보상으로 지급된 건물.
    public static UserBuilding awarded(Long userId, Integer buildingId, LocalDateTime acquiredAt) {
        return new UserBuilding(userId, buildingId, acquiredAt);
    }

    public void levelUp() {
        level++;
    }

    public void placeAt(int posX, int posY) {
        this.posX = posX;
        this.posY = posY;
    }

    public void changeHidden(boolean hidden) {
        this.hidden = hidden;
    }
}
