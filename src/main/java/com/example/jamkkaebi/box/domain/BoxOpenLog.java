package com.example.jamkkaebi.box.domain;

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

import java.time.LocalDate;
import java.time.LocalDateTime;

/**
 * 상자 한 번.
 *
 * <p>무료 상자 쿨타임도 {@code users} 컬럼이 아니라 여기서 센다. 하루 경계가 자정이라
 * {@code (userId, source, openedDate)} 조회 한 번이면 되고, 같은 표에서 구매 횟수도 나온다.
 */
@Entity
@Table(
        name = "box_open_logs",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_box_open_logs_request", columnNames = {"user_id", "request_id"}),
        indexes = @Index(name = "idx_box_open_logs_owner_day", columnList = "user_id, opened_date")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class BoxOpenLog extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    // 멱등키 (UUID)
    @Column(name = "request_id", nullable = false, length = 64)
    private String requestId;

    @Column(name = "artifact_id", nullable = false)
    private Integer artifactId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private BoxSource source;

    @Column(name = "was_duplicate", nullable = false)
    private boolean duplicate;

    // 중복 환전으로 받은 정성
    @Column(name = "jeongseong_gained", nullable = false)
    private int jeongseongGained;

    // 중복 환전으로 받은 시대의 결정
    @Column(name = "crystal_gained", nullable = false)
    private int crystalGained;

    // 소비한 정성
    @Column(name = "jeongseong_spent", nullable = false)
    private int jeongseongSpent;

    // 게임 일자
    @Column(name = "opened_date", nullable = false)
    private LocalDate openedDate;

    @Column(name = "opened_at", nullable = false)
    private LocalDateTime openedAt;

    @Builder
    private BoxOpenLog(Long userId, String requestId, Integer artifactId, BoxSource source, boolean duplicate,
                       int jeongseongGained, int crystalGained, int jeongseongSpent, LocalDateTime openedAt) {
        this.userId = userId;
        this.requestId = requestId;
        this.artifactId = artifactId;
        this.source = source;
        this.duplicate = duplicate;
        this.jeongseongGained = jeongseongGained;
        this.crystalGained = crystalGained;
        this.jeongseongSpent = jeongseongSpent;
        this.openedAt = openedAt;
        this.openedDate = openedAt.toLocalDate();
    }

    public BoxResult result() {
        return duplicate ? BoxResult.DUPLICATE : BoxResult.NEW;
    }
}
