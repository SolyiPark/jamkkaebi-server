package com.example.jamkkaebi.friend.domain;

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
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDate;

/**
 * 상자 선물 한 건.
 *
 * <p>보내기 1일 1회는 {@code (fromUserId, sentDate)} 유니크 제약으로 막는다.
 *
 * <p>받는 쪽의 그날 첫 선물만 상자권이 되고({@code converted = false}), 그 뒤 도착한 선물은 정성으로
 * 환산된다({@code converted = true}). 상자권은 상자 도메인이 열 때 {@code claimed} 로 소진한다.
 */
@Entity
@Table(
        name = "gift_logs",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_gift_logs_sender_day", columnNames = {"from_user_id", "sent_date"}),
        indexes = @Index(name = "idx_gift_logs_receiver_day", columnList = "to_user_id, sent_date")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class GiftLog extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "from_user_id", nullable = false)
    private Long fromUserId;

    @Column(name = "to_user_id", nullable = false)
    private Long toUserId;


    @Column(name = "sent_date", nullable = false)
    private LocalDate sentDate;

    // 정성으로 환산됐는지
    @Column(nullable = false)
    private boolean converted;

    // 상자권으로 사용됐는지
    @Column(nullable = false)
    private boolean claimed;

    @Builder
    private GiftLog(Long fromUserId, Long toUserId, LocalDate sentDate, boolean converted) {
        this.fromUserId = fromUserId;
        this.toUserId = toUserId;
        this.sentDate = sentDate;
        this.converted = converted;
    }
}
