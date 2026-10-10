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

    // 실제 전시관 구현(ExhibitionViewProvider)이 있어도 친구 테스트는 이 대역이 정한 모습을 찍는다.
    @Bean
    @Primary
    public FakeExhibitionViewReader fakeExhibitionViewReader() {
        return new FakeExhibitionViewReader();
    }
}
