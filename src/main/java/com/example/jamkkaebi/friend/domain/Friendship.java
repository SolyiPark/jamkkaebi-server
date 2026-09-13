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

/**
 * 두 사용자의 친구 관계. <b>관계를 한 행으로</b> 저장한다.
 *
 * {@code userIdLow < userIdHigh} 로 순서를 고정하면 두 방향이 같은 행이 되어, "삭제는 양방향 동시 해제"라는 기획이 지켜진다.
 */
@Entity
@Table(
        name = "friendships",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_friendships_pair", columnNames = {"user_id_low", "user_id_high"}),
        indexes = @Index(name = "idx_friendships_high", columnList = "user_id_high")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class Friendship extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id_low", nullable = false)
    private Long userIdLow;

    @Column(name = "user_id_high", nullable = false)
    private Long userIdHigh;

    private Friendship(Long userIdLow, Long userIdHigh) {
        this.userIdLow = userIdLow;
        this.userIdHigh = userIdHigh;
    }

    // 관계 생성
    public static Friendship between(Long userId, Long otherUserId) {
        if (userId.equals(otherUserId)) {
            throw new IllegalArgumentException("자기 자신과는 친구가 될 수 없습니다.");
        }
        return new Friendship(Math.min(userId, otherUserId), Math.max(userId, otherUserId));
    }

    public Long otherThan(Long userId) {
        return userIdLow.equals(userId) ? userIdHigh : userIdLow;
    }
}
