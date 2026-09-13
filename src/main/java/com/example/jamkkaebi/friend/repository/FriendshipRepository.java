package com.example.jamkkaebi.friend.repository;

import com.example.jamkkaebi.friend.domain.Friendship;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.Collection;
import java.util.List;
import java.util.Optional;

public interface FriendshipRepository extends JpaRepository<Friendship, Long> {

    Optional<Friendship> findByUserIdLowAndUserIdHigh(Long userIdLow, Long userIdHigh);

    // 두 사용자 사이의 관계
    default Optional<Friendship> findBetween(Long userId, Long otherUserId) {
        return findByUserIdLowAndUserIdHigh(Math.min(userId, otherUserId), Math.max(userId, otherUserId));
    }

    default boolean existsBetween(Long userId, Long otherUserId) {
        return findBetween(userId, otherUserId).isPresent();
    }

    @Query("SELECT f FROM Friendship f WHERE f.userIdLow = :userId OR f.userIdHigh = :userId")
    List<Friendship> findAllOf(@Param("userId") Long userId);

    @Query("SELECT COUNT(f) FROM Friendship f WHERE f.userIdLow = :userId OR f.userIdHigh = :userId")
    long countOf(@Param("userId") Long userId);

    // 사용자별 친구 수를 셀 때 쓴다.
    @Query("SELECT f FROM Friendship f WHERE f.userIdLow IN :userIds OR f.userIdHigh IN :userIds")
    List<Friendship> findAllTouching(@Param("userIds") Collection<Long> userIds);
}
