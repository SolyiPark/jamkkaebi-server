package com.example.jamkkaebi.friend.domain;

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

import java.time.LocalDateTime;

/**
 * 친구 요청 한 건.
 *
 * <p>만료된 요청을 다시 보낼 때는 옛 행을 지우고 새로 만든다.
 *
 * <p><b>거절은 보낸 쪽에 드러나지 않는다.</b> 거절된 요청도 만료 전까지는 보낸 쪽 목록에 대기로 보이고
 * 보낸 요청 상한에도 포함된다. 거절을 행 삭제로 처리하면 보낸 쪽 목록에서 사라져 거절 사실이 새어 나간다.
 */
@Entity
@Table(
        name = "friend_requests",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_friend_requests_pair", columnNames = {"from_user_id", "to_user_id"}),
        indexes = {
                @Index(name = "idx_friend_requests_to", columnList = "to_user_id"),
                @Index(name = "idx_friend_requests_expires", columnList = "expires_at")
        }
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class FriendRequest extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "from_user_id", nullable = false)
    private Long fromUserId;

    @Column(name = "to_user_id", nullable = false)
    private Long toUserId;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 20)
    private FriendRequestStatus status;

    @Column(name = "requested_at", nullable = false)
    private LocalDateTime requestedAt;

    @Column(name = "expires_at", nullable = false)
    private LocalDateTime expiresAt;

    @Builder
    private FriendRequest(Long fromUserId, Long toUserId, LocalDateTime requestedAt, LocalDateTime expiresAt) {
        this.fromUserId = fromUserId;
        this.toUserId = toUserId;
        this.status = FriendRequestStatus.PENDING;
        this.requestedAt = requestedAt;
        this.expiresAt = expiresAt;
    }

    public boolean isExpired(LocalDateTime now) {
        return !expiresAt.isAfter(now);
    }

    // 거절됐어도 만료 전이면 참
    public boolean isOutstanding(LocalDateTime now) {
        return !isExpired(now);
    }

    public boolean isAwaitingResponse(LocalDateTime now) {
        return status == FriendRequestStatus.PENDING && !isExpired(now);
    }

    public void reject() {
        this.status = FriendRequestStatus.REJECTED;
    }
}
