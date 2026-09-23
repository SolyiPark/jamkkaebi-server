package com.example.jamkkaebi.support;

import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Primary;

import java.time.LocalDateTime;

/**
 * 코어 루프 테스트용 빈 — 옮길 수 있는 시계.
 *
 * <p>선명도 7일·재봉인 14일·상자 자정 리셋은 실제로 기다려서 확인할 수 없다. 시계를 옮길 수 없으면
 * 이 규칙들은 사실상 테스트 불가능해지고, 그러면 12월에 손으로 확인하다 무너진다.
 */
@TestConfiguration
public class CoreLoopTestConfig {

    @Bean
    @Primary
    public MutableClock testClock() {
        return new MutableClock(LocalDateTime.of(2026, 9, 12, 10, 0));
    }
}
