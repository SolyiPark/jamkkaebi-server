package com.example.jamkkaebi.support;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneId;

/**
 * 테스트가 옮길 수 있는 KST 시계. 14일 만료·자정 리셋을 기다리지 않고 확인하기 위해 쓴다.
 */
public class MutableClock extends Clock {

    private static final ZoneId KST = ZoneId.of("Asia/Seoul");

    private volatile Instant instant;

    public MutableClock(LocalDateTime start) {
        set(start);
    }

    public void set(LocalDateTime dateTime) {
        this.instant = dateTime.atZone(KST).toInstant();
    }

    public void advance(Duration duration) {
        this.instant = instant.plus(duration);
    }

    @Override
    public ZoneId getZone() {
        return KST;
    }

    @Override
    public Clock withZone(ZoneId zone) {
        throw new UnsupportedOperationException("테스트 시계는 KST 고정입니다.");
    }

    @Override
    public Instant instant() {
        return instant;
    }
}
