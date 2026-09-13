package com.example.jamkkaebi.friend.repository;

import com.example.jamkkaebi.friend.domain.GiftLog;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDate;
import java.util.Collection;
import java.util.List;
import java.util.Optional;

public interface GiftLogRepository extends JpaRepository<GiftLog, Long> {

    Optional<GiftLog> findByFromUserIdAndSentDate(Long fromUserId, LocalDate sentDate);

    long countByToUserIdAndSentDate(Long toUserId, LocalDate sentDate);

    // 이 날 받은 선물 중 아직 쓰지 않은 상자권이 있는가.
    boolean existsByToUserIdAndSentDateAndConvertedFalseAndClaimedFalse(Long toUserId, LocalDate sentDate);

    @Query("""
            SELECT new com.example.jamkkaebi.friend.repository.UserCount(g.toUserId, COUNT(g))
              FROM GiftLog g
             WHERE g.toUserId IN :userIds AND g.sentDate = :sentDate
             GROUP BY g.toUserId
            """)
    List<UserCount> countReceivedByUsers(@Param("userIds") Collection<Long> userIds,
                                         @Param("sentDate") LocalDate sentDate);

    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM GiftLog g WHERE g.sentDate < :before")
    int deleteOlderThan(@Param("before") LocalDate before);
}
