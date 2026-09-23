package com.example.jamkkaebi.purification.repository;

import com.example.jamkkaebi.purification.domain.PlaySession;
import com.example.jamkkaebi.purification.domain.SessionStatus;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Optional;

public interface PlaySessionRepository extends JpaRepository<PlaySession, Long> {

    Optional<PlaySession> findBySessionToken(String sessionToken);

    // 아직 보고하지 않은 세션. 새 세션을 시작할 때 전부 무효로 돌린다.
    List<PlaySession> findAllByUserIdAndStatus(Long userId, SessionStatus status);

    // 정산되지 않은 채 만료된 세션 정리
    @Modifying(flushAutomatically = true)
    @Query("""
            UPDATE PlaySession s
               SET s.status = com.example.jamkkaebi.purification.domain.SessionStatus.SUPERSEDED
             WHERE s.status = com.example.jamkkaebi.purification.domain.SessionStatus.ISSUED
               AND s.expiresAt < :now
            """)
    int expireIssuedBefore(@Param("now") LocalDateTime now);
}
