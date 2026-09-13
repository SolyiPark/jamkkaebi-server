package com.example.jamkkaebi.support;

import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Primary;

import java.time.LocalDateTime;

/** 친구 기능 테스트용 빈 — 옮길 수 있는 시계와 전시관 대역. */
@TestConfiguration
public class FriendTestConfig {

    @Bean
    @Primary
    public MutableClock testClock() {
        return new MutableClock(LocalDateTime.of(2026, 9, 12, 10, 0));
    }

    @Bean
    public FakeExhibitionViewReader fakeExhibitionViewReader() {
        return new FakeExhibitionViewReader();
    }
}
