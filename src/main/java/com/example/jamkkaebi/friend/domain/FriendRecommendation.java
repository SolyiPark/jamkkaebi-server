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
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDate;

/**
 * 오늘의 추천 한 명.
 *
 * 최근 7일간 추천한 사람을 빼는 것도 같은 테이블 조회로 끝난다.
 */
@Entity
@Table(
        name = "friend_recommendations",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_friend_recommendations_day",
                columnNames = {"user_id", "recommended_date", "recommended_user_id"}),
        indexes = @Index(name = "idx_friend_recommendations_date", columnList = "recommended_date")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class FriendRecommendation extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    // 추천 친구
    @Column(name = "recommended_user_id", nullable = false)
    private Long recommendedUserId;

    @Column(name = "recommended_date", nullable = false)
    private LocalDate recommendedDate;

    // 추천받은 사람에게 요청을 보냈는지
    @Column(nullable = false)
    private boolean requested;

    private FriendRecommendation(Long userId, Long recommendedUserId, LocalDate recommendedDate) {
        this.userId = userId;
        this.recommendedUserId = recommendedUserId;
        this.recommendedDate = recommendedDate;
    }

    public static FriendRecommendation of(Long userId, Long recommendedUserId, LocalDate recommendedDate) {
        return new FriendRecommendation(userId, recommendedUserId, recommendedDate);
    }

    public void markRequested() {
        this.requested = true;
    }
}
