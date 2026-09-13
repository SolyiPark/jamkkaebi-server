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
 * 차단 한 건. 차단 목록에는 내가 차단한 사람만 보인다.
 *
 * <p>효과는 방향과 무관하게 <b>양쪽 모두</b>에 걸린다. 어느 쪽이 차단했든 두 사람 사이에서는 코드 조회·
 * 추천·요청·방문·선물이 전부 "없는 사용자"로 처리된다.
 */
@Entity
@Table(
        name = "blocks",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_blocks_pair", columnNames = {"blocker_id", "blocked_id"}),
        indexes = @Index(name = "idx_blocks_blocked", columnList = "blocked_id")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class Block extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "blocker_id", nullable = false)
    private Long blockerId;

    @Column(name = "blocked_id", nullable = false)
    private Long blockedId;

    private Block(Long blockerId, Long blockedId) {
        this.blockerId = blockerId;
        this.blockedId = blockedId;
    }

    public static Block of(Long blockerId, Long blockedId) {
        return new Block(blockerId, blockedId);
    }
}
