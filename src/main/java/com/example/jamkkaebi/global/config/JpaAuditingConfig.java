package com.example.jamkkaebi.global.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.auditing.DateTimeProvider;
import org.springframework.data.jpa.repository.config.EnableJpaAuditing;

import java.time.LocalDateTime;
import java.time.ZoneId;
import java.util.Optional;

/**
 * BaseTimeEntity 의 createdAt/updatedAt 자동 기록을 위한 JPA Auditing 활성화.
 *
 * <p>시간대는 KST 로 고정한다 — 서버가 어느 리전에 뜨든 기록되는 시각이 같아야 한다.
 */
@Configuration
@EnableJpaAuditing(dateTimeProviderRef = "kstDateTimeProvider")
public class JpaAuditingConfig {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");

    @Bean
    public DateTimeProvider kstDateTimeProvider() {
        return () -> Optional.of(LocalDateTime.now(KST));
    }
}
