package com.example.jamkkaebi.friend.repository;

import com.example.jamkkaebi.friend.domain.Block;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;

public interface BlockRepository extends JpaRepository<Block, Long> {

    boolean existsByBlockerIdAndBlockedId(Long blockerId, Long blockedId);

    //두 사용자 사이에 차단이 있는가
    @Query("""
            SELECT CASE WHEN COUNT(b) > 0 THEN true ELSE false END
              FROM Block b
             WHERE (b.blockerId = :userId AND b.blockedId = :otherUserId)
                OR (b.blockerId = :otherUserId AND b.blockedId = :userId)
            """)
    boolean existsBetween(@Param("userId") Long userId, @Param("otherUserId") Long otherUserId);

    List<Block> findAllByBlockerIdOrderByCreatedAtDescIdDesc(Long blockerId);

    // 나와 차단 관계인 상대
    @Query("""
            SELECT CASE WHEN b.blockerId = :userId THEN b.blockedId ELSE b.blockerId END
              FROM Block b
             WHERE b.blockerId = :userId OR b.blockedId = :userId
            """)
    List<Long> findCounterpartIds(@Param("userId") Long userId);

    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM Block b WHERE b.blockerId = :blockerId AND b.blockedId = :blockedId")
    int deleteByPair(@Param("blockerId") Long blockerId, @Param("blockedId") Long blockedId);
}
