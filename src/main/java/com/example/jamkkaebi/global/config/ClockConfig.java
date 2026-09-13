package com.example.jamkkaebi.global.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.time.Clock;
import java.time.ZoneId;

/**
 * 서버 시각의 단일 출처.
 *
 * <p>1일 1회 제한·요청 만료·자정 리셋처럼 시간에 걸린 규칙은 {@code LocalDateTime.now()} 를 흩어 쓰지
 * 않고 이 {@link Clock} 을 거쳐 읽는다. 그래야 테스트에서 시계를 옮겨 14일 뒤·다음 날을 재현할 수 있다
 * — 실제로 기다려서 확인할 수는 없다.
 */
@Configuration
public class ClockConfig {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");

    @Bean
    public Clock clock() {
        return Clock.system(KST);
    }
}
