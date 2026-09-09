package com.example.jamkkaebi.auth.repository;

import com.example.jamkkaebi.auth.domain.RefreshToken;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDateTime;
import java.util.Optional;

public interface RefreshTokenRepository extends JpaRepository<RefreshToken, Long> {

    Optional<RefreshToken> findByTokenHash(String tokenHash);

    /**
     * <b>살아 있을 때만</b> 이 토큰을 폐기하고, 폐기한 행 수를 돌려준다. (0 또는 1)
     *
     * <p>회전형 토큰의 핵심 방어는 "한 번 쓰면 무효"다. 조회로 확인한 뒤 폐기하면 두 명령 사이에
     * 다른 요청이 끼어들어 <b>같은 토큰으로 동시에 재발급한 양쪽이 모두 성공</b>한다. 조건부 UPDATE
     * 한 문장으로 묶으면 DB 가 행을 잠그므로 먼저 도착한 하나만 1을 받는다.
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("""
            UPDATE RefreshToken r
               SET r.revokedAt = :now
             WHERE r.tokenHash = :tokenHash
               AND r.revokedAt IS NULL
               AND r.expiresAt > :now
            """)
    int revokeIfActive(@Param("tokenHash") String tokenHash, @Param("now") LocalDateTime now);

    /**
     * 이 사용자의 살아 있는 토큰을 모두 폐기한다. (로그아웃, 그리고 재사용 감지 시 전체 폐기)
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("""
            UPDATE RefreshToken r
               SET r.revokedAt = :now
             WHERE r.userId = :userId
               AND r.revokedAt IS NULL
            """)
    int revokeAllByUserId(@Param("userId") Long userId, @Param("now") LocalDateTime now);

    /**
     * 만료된 지 충분히 지난 행을 지운다.
     *
     * <p><b>기준이 만료 시각이지 폐기 여부가 아니다.</b> 폐기된 행은 재사용 감지의 유일한 근거라,
     * 회전 즉시 지우면 탈취된 옛 토큰이 다시 들어와도 "저장된 적 없는 토큰"으로 보여 경보가
     * 울리지 않는다. 원래 수명이 끝난 뒤에야 지우는 이유가 그것이다.
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("DELETE FROM RefreshToken r WHERE r.expiresAt <= :threshold")
    int deleteExpiredBefore(@Param("threshold") LocalDateTime threshold);
}
