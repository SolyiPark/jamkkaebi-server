package com.example.jamkkaebi.friend.domain;

import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Lob;
import jakarta.persistence.Table;
import lombok.AccessLevel;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDateTime;

/**
 * 친구에게 보여 줄 전시관의 스냅샷.
 *
 * <p>친구가 오프라인이어도 전시관을 볼 수 있어야 하므로, 방문 시점에 계산하지 않고 주인이 마지막으로
 * 접속했을 때의 모습을 저장해 둔다.
 */
@Entity
@Table(name = "exhibition_snapshots")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class ExhibitionSnapshot extends BaseTimeEntity {

    @Id
    @Column(name = "user_id")
    private Long userId;

    @Lob
    @Column(nullable = false)
    private String payload;

    // 스냅샷을 찍은 접속 시각
    @Column(name = "captured_at", nullable = false)
    private LocalDateTime capturedAt;

    private ExhibitionSnapshot(Long userId, String payload, LocalDateTime capturedAt) {
        this.userId = userId;
        this.payload = payload;
        this.capturedAt = capturedAt;
    }

    public static ExhibitionSnapshot of(Long userId, String payload, LocalDateTime capturedAt) {
        return new ExhibitionSnapshot(userId, payload, capturedAt);
    }

    public void overwrite(String payload, LocalDateTime capturedAt) {
        this.payload = payload;
        this.capturedAt = capturedAt;
    }
}
