package com.example.jamkkaebi.friend.repository;

import com.example.jamkkaebi.friend.domain.FriendRecommendation;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDate;
import java.util.List;
import java.util.Optional;

public interface FriendRecommendationRepository extends JpaRepository<FriendRecommendation, Long> {

    List<FriendRecommendation> findAllByUserIdAndRecommendedDateOrderByIdAsc(Long userId, LocalDate recommendedDate);

    Optional<FriendRecommendation> findByUserIdAndRecommendedUserIdAndRecommendedDate(
            Long userId, Long recommendedUserId, LocalDate recommendedDate);

    // 후보가 모자라 다시 넣어야 할 때 추천한 지 가장 오래된 사람부터 쓰도록, 사람마다 마지막으로 추천한 순서의
    // 오름차순으로 준다. 같은 날 추천은 저장 순서(id)로 가른다.
    @Query("""
            SELECT r.recommendedUserId FROM FriendRecommendation r
             WHERE r.userId = :userId AND r.recommendedDate >= :since
             GROUP BY r.recommendedUserId
             ORDER BY MAX(r.recommendedDate) ASC, MAX(r.id) ASC
            """)
    List<Long> findRecommendedUserIdsSinceOldestFirst(@Param("userId") Long userId, @Param("since") LocalDate since);

    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM FriendRecommendation r WHERE r.recommendedDate < :before")
    int deleteOlderThan(@Param("before") LocalDate before);
}
