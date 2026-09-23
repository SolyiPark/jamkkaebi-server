package com.example.jamkkaebi.box.domain;

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

/**
 *신규 카드를 보장하기 위한 소프트 천장 카운터 — <b>유물 하나의 미당첨 횟수</b>.
 *
 */
@Entity
@Table(
        name = "gacha_pity",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_gacha_pity_owner_artifact", columnNames = {"user_id", "artifact_id"}),
        indexes = @Index(name = "idx_gacha_pity_owner", columnList = "user_id")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class GachaPity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    @Column(name = "artifact_id", nullable = false)
    private Integer artifactId;

    @ColumnDefault("0")
    @Column(name = "miss_streak", nullable = false)
    private int missStreak;

    private GachaPity(Long userId, Integer artifactId) {
        this.userId = userId;
        this.artifactId = artifactId;
    }

    public static GachaPity start(Long userId, Integer artifactId) {
        return new GachaPity(userId, artifactId);
    }

    public void miss() {
        missStreak++;
    }

    public void hit() {
        missStreak = 0;
    }

    public boolean reached(int threshold) {
        return missStreak >= threshold;
    }
}
