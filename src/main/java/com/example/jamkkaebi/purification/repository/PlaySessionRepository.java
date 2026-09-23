package com.example.jamkkaebi.purification.repository;

import com.example.jamkkaebi.purification.domain.PlaySession;
import com.example.jamkkaebi.purification.domain.SessionStatus;
import jakarta.persistence.LockModeType;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Optional;

public interface PlaySessionRepository extends JpaRepository<PlaySession, Long> {

    Optional<PlaySession> findBySessionToken(String sessionToken);

    /**
     * 정산하려는 세션을 <b>잠그고</b> 읽는다.
     *
     * <p>클라이언트가 응답을 기다리다 끊고 같은 결과를 다시 보내면 두 요청이 겹친다. 평범하게 읽으면
     * 둘 다 {@code ISSUED} 를 보고 멱등 분기를 건너뛰어, 정성과 시대의 결정이 두 번 지급되고
     * 페이즈 진행도 두 번 오른다. 잠그고 읽으면 뒤엣것이 앞엣것의 커밋을 기다렸다가 {@code SETTLED}
     * 를 보게 되어 멱등 분기로 들어간다.
     */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("SELECT s FROM PlaySession s WHERE s.sessionToken = :sessionToken")
    Optional<PlaySession> findBySessionTokenForUpdate(@Param("sessionToken") String sessionToken);

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
