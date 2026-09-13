package com.example.jamkkaebi.friend.repository;

import com.example.jamkkaebi.friend.domain.FriendRequest;
import com.example.jamkkaebi.friend.domain.FriendRequestStatus;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDateTime;
import java.util.Collection;
import java.util.List;
import java.util.Optional;

/**
 * 친구 요청 저장소.
 *
 * <p>모든 조회에 만료 조건({@code expiresAt > now})을 건다. 만료 행을 지우는 배치는 한 시간에 한 번이라,
 * 조회가 배치를 믿으면 만료된 요청이 최대 한 시간 동안 유효한 것처럼 보인다.
 */
public interface FriendRequestRepository extends JpaRepository<FriendRequest, Long> {

    Optional<FriendRequest> findByFromUserIdAndToUserId(Long fromUserId, Long toUserId);

    // 받은 쪽이 응답해야 할 요청 수 — 받은 요청함 상한 기준.
    @Query("""
            SELECT COUNT(r) FROM FriendRequest r
             WHERE r.toUserId = :userId AND r.status = :pending AND r.expiresAt > :now
            """)
    long countAwaiting(@Param("userId") Long userId,
                       @Param("pending") FriendRequestStatus pending,
                       @Param("now") LocalDateTime now);

    // 보낸 요청 수 — 보낸 요청함 상한 기준. 거절된 요청도 만료 전이면 센다.
    @Query("SELECT COUNT(r) FROM FriendRequest r WHERE r.fromUserId = :userId AND r.expiresAt > :now")
    long countOutstandingSent(@Param("userId") Long userId, @Param("now") LocalDateTime now);

    @Query("""
            SELECT r FROM FriendRequest r
             WHERE r.toUserId = :userId AND r.status = :pending AND r.expiresAt > :now
             ORDER BY r.requestedAt DESC, r.id DESC
            """)
    List<FriendRequest> findAwaitingReceived(@Param("userId") Long userId,
                                             @Param("pending") FriendRequestStatus pending,
                                             @Param("now") LocalDateTime now);

    @Query("""
            SELECT r FROM FriendRequest r
             WHERE r.fromUserId = :userId AND r.expiresAt > :now
             ORDER BY r.requestedAt DESC, r.id DESC
            """)
    List<FriendRequest> findOutstandingSent(@Param("userId") Long userId, @Param("now") LocalDateTime now);

    //추천에서 요청을 주고받는 중인 상대를 뺄 때 쓴다.
    @Query("""
            SELECT r FROM FriendRequest r
             WHERE (r.fromUserId = :userId OR r.toUserId = :userId) AND r.expiresAt > :now
            """)
    List<FriendRequest> findAllOutstandingInvolving(@Param("userId") Long userId, @Param("now") LocalDateTime now);

    //수신함이 찬 사용자를 추천에서 뺄 때 쓴다.
    @Query("""
            SELECT new com.example.jamkkaebi.friend.repository.UserCount(r.toUserId, COUNT(r))
              FROM FriendRequest r
             WHERE r.toUserId IN :userIds AND r.status = :pending AND r.expiresAt > :now
             GROUP BY r.toUserId
            """)
    List<UserCount> countAwaitingByReceivers(@Param("userIds") Collection<Long> userIds,
                                             @Param("pending") FriendRequestStatus pending,
                                             @Param("now") LocalDateTime now);

    // 요청 수락·차단
    @Modifying(flushAutomatically = true)
    @Query("""
            DELETE FROM FriendRequest r
             WHERE (r.fromUserId = :userId AND r.toUserId = :otherUserId)
                OR (r.fromUserId = :otherUserId AND r.toUserId = :userId)
            """)
    int deleteBetween(@Param("userId") Long userId, @Param("otherUserId") Long otherUserId);

    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM FriendRequest r WHERE r.expiresAt <= :now")
    int deleteExpired(@Param("now") LocalDateTime now);
}
