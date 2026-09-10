package com.example.jamkkaebi.auth.repository;

import com.example.jamkkaebi.auth.domain.LoginSession;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDateTime;

public interface LoginSessionRepository extends JpaRepository<LoginSession, String> {

    /** 만료된 로그인 세션을 치운다. 쓰이지 않은 채 남은 행이 쌓이는 것을 막는다. */
    @Modifying(clearAutomatically = true, flushAutomatically = true)
    @Query("DELETE FROM LoginSession s WHERE s.expiresAt <= :now")
    int deleteExpired(@Param("now") LocalDateTime now);
}
