package com.example.jamkkaebi.auth.repository;

import com.example.jamkkaebi.auth.domain.AuthHandoff;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDateTime;

public interface AuthHandoffRepository extends JpaRepository<AuthHandoff, String> {

    /**
     * <b>아직 쓰이지 않았고 만료되지 않았을 때만</b> 코드를 소진하고, 소진한 행 수를 돌려준다.
     *
     * <p>조회로 확인한 뒤 사용 처리하면, 같은 코드로 동시에 두 번 교환했을 때 양쪽 모두 토큰을
     * 받아 간다. 일회용이라는 성질이 경합에서 깨지므로 한 문장으로 묶는다.
     */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("""
            UPDATE AuthHandoff h
               SET h.usedAt = :now
             WHERE h.code = :code
               AND h.usedAt IS NULL
               AND h.expiresAt > :now
            """)
    int consume(@Param("code") String code, @Param("now") LocalDateTime now);

    /** 다 쓴 인계 코드와 만료된 코드를 치운다. */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("DELETE FROM AuthHandoff h WHERE h.expiresAt <= :now")
    int deleteExpired(@Param("now") LocalDateTime now);
}
